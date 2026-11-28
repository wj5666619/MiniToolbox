using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using System.Media;
using Microsoft.Win32;
using System.Reflection;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Accessibility;

namespace ShutdownTool
{
    // v19.8：从 MainForm.cs 原样迁出（电脑配置/去水印/下载/音乐），仅为拆分文件，逻辑未改
    partial class MainForm
    {
        // ================= 电脑配置：实时负载与报告 =================
        void SysLoadTick()
        {
            try
            {
                ulong idle, kt, ut;
                if (GetSystemTimes(out idle, out kt, out ut))
                {
                    if (sysPrevInited)
                    {
                        ulong dIdle = idle - sysPrevIdle;
                        ulong dTotal = (kt - sysPrevKernel) + (ut - sysPrevUser);
                        if (dTotal > 0)
                        {
                            double busy = 1.0 - (double)dIdle / dTotal;
                            if (busy < 0) busy = 0;
                            if (busy > 1) busy = 1;
                            sysPct[0] = busy * 100.0;
                        }
                    }
                    sysPrevIdle = idle;
                    sysPrevKernel = kt;
                    sysPrevUser = ut;
                    sysPrevInited = true;
                }
                MEMORYSTATUSEX ms = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(ms))
                {
                    sysMemTotal = (long)ms.ullTotalPhys;
                    sysMemUsed = (long)(ms.ullTotalPhys - ms.ullAvailPhys);
                    sysPct[1] = ms.dwMemoryLoad;
                }
                DriveInfo di = new DriveInfo("C");
                sysDiskTotal = di.TotalSize;
                sysDiskFree = di.TotalFreeSpace;
                sysPct[2] = sysDiskTotal > 0 ? 100.0 * (sysDiskTotal - sysDiskFree) / sysDiskTotal : 0;
            }
            catch (Exception dg11117_1) { Diag.Swallow("MainForm.ToolPages.cs:63", dg11117_1); }

            if (sysVals[0] != null) sysVals[0].Text = sysPct[0].ToString("0.0") + "%";
            if (sysVals[1] != null) sysVals[1].Text = (sysMemUsed / 1073741824.0).ToString("0.0") + " GB";
            if (sysVals[2] != null) sysVals[2].Text = (sysDiskFree / 1073741824.0).ToString("0.0") + " GB";
            // —— 显卡：首次初始化 NVML，失败则永久降级不重试 ——
            if (!sysNvInit && !sysNvFail)
            {
                try
                {
                    if (nvmlInit_v2() == 0)
                    {
                        uint gc = 0;
                        if (nvmlDeviceGetCount_v2(ref gc) == 0 && gc > 0
                            && nvmlDeviceGetHandleByIndex_v2(0, ref sysNvHandle) == 0)
                            sysNvInit = true;
                    }
                    if (!sysNvInit) sysNvFail = true;
                }
                catch { sysNvFail = true; }
            }
            if (sysNvInit)
            {
                try
                {
                    uint gt = 0;
                    if (nvmlDeviceGetTemperature(sysNvHandle, 0, ref gt) == 0) sysGpuTemp = gt;
                    uint mw = 0;
                    if (nvmlDeviceGetPowerUsage(sysNvHandle, ref mw) == 0) sysGpuPower = mw / 1000;
                    NvMemInfo gm = new NvMemInfo();
                    if (nvmlDeviceGetMemoryInfo(sysNvHandle, ref gm) == 0)
                    {
                        sysGpuMemUsed = (long)(gm.Used / 1048576);
                        sysGpuMemTotal = (long)(gm.Total / 1048576);
                    }
                    sysPct[3] = sysGpuTemp;
                }
                catch (Exception dg11154_1) { Diag.Swallow("MainForm.ToolPages.cs:100", dg11154_1); }
            }
            if (sysSubs[0] != null) sysSubs[0].Text = T("每 2 秒实时采样", "live sample every 2s");
            if (sysSubs[1] != null) sysSubs[1].Text = T("已用 ", "used ") + (sysMemUsed / 1073741824.0).ToString("0.0") + " / " + (sysMemTotal / 1073741824.0).ToString("0.0") + " GB";
            if (sysSubs[2] != null) sysSubs[2].Text = T("已用 ", "used ") + sysPct[2].ToString("0") + "% · " + T("共 ", "of ") + (sysDiskTotal / 1073741824.0).ToString("0") + " GB";
            for (int i = 0; i < 3; i++) if (sysBars[i] != null) sysBars[i].Invalidate();
            if (lblSysGpu != null)
            {
                if (sysNvInit && sysGpuTemp > 0)
                    lblSysGpu.Text = T("显卡 ", "GPU ") + sysGpuTemp.ToString("0") + "°C · " + sysGpuPower.ToString("0") + " W · "
                        + T("显存 ", "VRAM ") + (sysGpuMemUsed / 1024.0).ToString("0.0") + " / " + (sysGpuMemTotal / 1024.0).ToString("0.0") + " GB";
                else if (sysNvFail)
                    lblSysGpu.Text = T("未检测到 NVIDIA 显卡读数（NVML 不可用或非 NVIDIA 显卡）", "No NVIDIA GPU reading (NVML unavailable)");
            }
        }

        void SysExportReport()
        {
            if (lvSys == null || lvSys.Items.Count == 0)
            {
                if (lblSysStatus != null)
                {
                    lblSysStatus.Text = T("还没有检测结果，先点「重新检测」。", "No data yet, click Detect Again.");
                    lblSysStatus.ForeColor = UiKit.A(240, 160, 90);
                }
                return;
            }
            int rows = lvSys.Items.Count;
            int w = 940, h = 210 + rows * 46 + 96;
            Bitmap bmp = new Bitmap(w, h);
            Graphics g = Graphics.FromImage(bmp);
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Rectangle rcAll = new Rectangle(0, 0, w, h);
                using (LinearGradientBrush bg = new LinearGradientBrush(rcAll,
                    UiKit.A(26, 30, 38), UiKit.A(40, 46, 58), 60F))
                    g.FillRectangle(bg, rcAll);
                Rectangle topBar = new Rectangle(0, 0, w, 6);
                using (LinearGradientBrush tb = new LinearGradientBrush(topBar,
                    UiKit.A(96, 165, 250), UiKit.A(45, 212, 191), 0F))
                    g.FillRectangle(tb, topBar);

                using (Font ft = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold))
                    g.DrawString(T("电脑配置报告", "PC Specs Report"), ft, Brushes.White, 48, 40);
                using (Font fs = new Font("Microsoft YaHei UI", 10.5F))
                using (SolidBrush bs = new SolidBrush(UiKit.A(150, 165, 190)))
                    g.DrawString(Environment.MachineName + "   ·   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                        + "   ·   " + T("小工具箱 v5.1", "Toolbox v5.1"), fs, bs, 50, 94);
                using (Pen pl = new Pen(UiKit.AA(64, 255, 255, 255), 1f))
                    g.DrawLine(pl, 48, 130, w - 48, 130);

                int y = 148;
                using (Font fk = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold))
                using (Font fv = new Font("Microsoft YaHei UI", 10.5F))
                using (SolidBrush bk = new SolidBrush(UiKit.A(152, 167, 192)))
                using (SolidBrush bv = new SolidBrush(UiKit.A(238, 244, 255)))
                using (SolidBrush alt = new SolidBrush(UiKit.AA(14, 255, 255, 255)))
                {
                    for (int i = 0; i < lvSys.Items.Count; i++)
                    {
                        ListViewItem it = lvSys.Items[i];
                        if (i % 2 == 1) g.FillRectangle(alt, 40, y - 8, w - 80, 44);
                        string key = it.Text;
                        string val = it.SubItems.Count > 1 ? it.SubItems[1].Text : "";
                        g.DrawString(key, fk, bk, 50, y + 4);
                        g.DrawString(val, fv, bv, new RectangleF(252, y + 4, w - 300, 44));
                        y += 46;
                    }
                }
                using (Font ff = new Font("Microsoft YaHei UI", 9F))
                using (SolidBrush bf = new SolidBrush(UiKit.A(118, 132, 154)))
                {
                    g.DrawString(T("硬件信息由 WMI 采集 · 仅供查看，不修改系统设置", "Collected via WMI · view only"), ff, bf, 50, h - 56);
                    g.DrawString(T("生成时间 ", "Generated ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ff, bf, 50, h - 34);
                }
            }
            catch (Exception dg11232_1) { Diag.Swallow("MainForm.ToolPages.cs:178", dg11232_1); }
            finally { g.Dispose(); }

            string path = Path.Combine(BASE_DIR, "电脑配置报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            try
            {
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                if (lblSysStatus != null)
                {
                    lblSysStatus.Text = T("报告已导出：", "Saved: ") + Path.GetFileName(path);
                    lblSysStatus.ForeColor = C_ON;
                }
                if (!sysAutoTest) Process.Start("explorer.exe", "/select," + path);
            }
            catch (Exception ex)
            {
                try { bmp.Dispose(); } catch (Exception dg11249_1) { Diag.Swallow("MainForm.ToolPages.cs:195", dg11249_1); }
                if (lblSysStatus != null)
                {
                    lblSysStatus.Text = T("导出失败：", "Export failed: ") + ex.Message;
                    lblSysStatus.ForeColor = UiKit.A(255, 120, 120);
                }
            }
        }

        // ================= 电脑配置 =================
        void SysUi(MethodInvoker a)
        {
            if (this.InvokeRequired) { try { this.BeginInvoke(a); } catch (Exception dg11261_1) { Diag.Swallow("MainForm.ToolPages.cs:207", dg11261_1); } }
            else a();
        }

        void SysStartScan()
        {
            if (lvSys == null) return;
            lvSys.Items.Clear();
            btnSysScan.Enabled = false;
            lblSysStatus.Text = T("正在读取硬件信息，请稍候…", "Reading hardware info...");
            lblSysStatus.ForeColor = C_SUB;
            Thread th = new Thread(delegate ()
            {
                List<string[]> rows = new List<string[]>();
                try { SysCollect(rows); }
                catch (Exception ex) { rows.Add(new string[] { T("错误", "Error"), ex.Message }); }
                SysUi(delegate ()
                {
                    lvSys.BeginUpdate();
                    lvSys.Items.Clear();
                    foreach (string[] r in rows)
                    {
                        ListViewItem it = new ListViewItem(r[0]);
                        it.SubItems.Add(r[1]);
                        lvSys.Items.Add(it);
                    }
                    lvSys.EndUpdate();
                    lblSysStatus.Text = T("检测完成，共 " + rows.Count + " 项。", "Done, " + rows.Count + " items.");
                    lblSysStatus.ForeColor = C_ON;
                    btnSysScan.Enabled = true;
                });
                if (sysAutoTest)
                {
                    StringBuilder sb2 = new StringBuilder();
                    foreach (string[] r in rows) sb2.AppendLine(r[0] + " = " + r[1]);
                    try { SysExportReport(); } catch (Exception dg11296_1) { Diag.Swallow("MainForm.ToolPages.cs:242", dg11296_1); }
                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_sys_test.txt"), sb2.ToString(), Encoding.UTF8); }
                    catch (Exception dg11298_1) { Diag.Swallow("MainForm.ToolPages.cs:244", dg11298_1); }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void SysCollect(List<string[]> rows)
        {
            // 处理器
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        string nm = Convert.ToString(mo["Name"]).Trim();
                        string cores = Convert.ToString(mo["NumberOfCores"]);
                        string ths = Convert.ToString(mo["NumberOfLogicalProcessors"]);
                        string mhz = Convert.ToString(mo["MaxClockSpeed"]);
                        rows.Add(new string[] { T("处理器", "CPU"), nm + "  ·  " + cores + T(" 核 ", "C / ") + ths + T(" 线程", "T") + "  ·  " + mhz + " MHz" });
                    }
                }
            }
            catch { rows.Add(new string[] { T("处理器", "CPU"), T("读取失败（WMI 不可用）", "read failed (WMI)") }); }

            // 主板
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        string mf = Convert.ToString(mo["Manufacturer"]).Trim();
                        string pd = Convert.ToString(mo["Product"]).Trim();
                        rows.Add(new string[] { T("主板", "Motherboard"), (mf + " " + pd).Trim() });
                    }
                }
            }
            catch (Exception dg11337_1) { Diag.Swallow("MainForm.ToolPages.cs:283", dg11337_1); }

            // 内存总量
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        long bytes = 0;
                        try { bytes = Convert.ToInt64(mo["TotalPhysicalMemory"]); } catch (Exception dg11347_1) { Diag.Swallow("MainForm.ToolPages.cs:293", dg11347_1); }
                        string name = Convert.ToString(mo["Name"]).Trim();
                        rows.Add(new string[] { T("内存总量", "RAM total"), ValFmtSize(bytes) });
                        if (name.Length > 0) rows.Add(new string[] { T("计算机名", "Computer"), name });
                    }
                }
            }
            catch (Exception dg11354_1) { Diag.Swallow("MainForm.ToolPages.cs:300", dg11354_1); }

            // 内存条
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                {
                    int idx = 0;
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        idx++;
                        long cap = 0;
                        try { cap = Convert.ToInt64(mo["Capacity"]); } catch (Exception dg11366_1) { Diag.Swallow("MainForm.ToolPages.cs:312", dg11366_1); }
                        string spd = Convert.ToString(mo["Speed"]);
                        string mf2 = Convert.ToString(mo["Manufacturer"]).Trim();
                        if (mf2.StartsWith("0x") || mf2.StartsWith("0X")) mf2 = "";
                        string mt = "";
                        try { mt = SysMemType(Convert.ToInt32(mo["SMBIOSMemoryType"])); } catch (Exception dg11371_1) { Diag.Swallow("MainForm.ToolPages.cs:317", dg11371_1); }
                        if (mt.Length > 0) mf2 = mt;
                        rows.Add(new string[] { T("内存条 " + idx, "RAM stick " + idx), ValFmtSize(cap) + (spd.Length > 0 && spd != "0" ? "  ·  " + spd + " MHz" : "") + (mf2.Length > 0 ? "  ·  " + mf2 : "") });
                    }
                }
            }
            catch (Exception dg11377_1) { Diag.Swallow("MainForm.ToolPages.cs:323", dg11377_1); }

            // 显卡
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        string nm2 = Convert.ToString(mo["Name"]).Trim();
                        if (nm2.Length == 0) continue;
                        long vram = 0;
                        try { vram = Convert.ToInt64(mo["AdapterRAM"]); } catch (Exception dg11389_1) { Diag.Swallow("MainForm.ToolPages.cs:335", dg11389_1); }
                        // WMI 的 AdapterRAM 是 32 位字段，大显存会溢出，优先用注册表 64 位值
                        long vramReg = SysGpuVram(nm2);
                        if (vramReg > vram) vram = vramReg;
                        string drv = Convert.ToString(mo["DriverVersion"]).Trim();
                        string line = nm2;
                        if (vram > 0) line += "  ·  " + ValFmtSize(vram) + T(" 显存", " VRAM");
                        if (drv.Length > 0) line += "  ·  " + T("驱动 ", "driver ") + drv;
                        rows.Add(new string[] { T("显卡", "GPU"), line });
                    }
                }
            }
            catch (Exception dg11401_1) { Diag.Swallow("MainForm.ToolPages.cs:347", dg11401_1); }

            // 硬盘
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    int di = 0;
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        di++;
                        string model = Convert.ToString(mo["Model"]).Trim();
                        long sz = 0;
                        try { sz = Convert.ToInt64(mo["Size"]); } catch (Exception dg11414_1) { Diag.Swallow("MainForm.ToolPages.cs:360", dg11414_1); }
                        string itf = Convert.ToString(mo["InterfaceType"]).Trim();
                        string line = model + (sz > 0 ? "  ·  " + ValFmtSize(sz) : "") + (itf.Length > 0 ? "  ·  " + itf : "");
                        rows.Add(new string[] { T("硬盘 " + di, "Disk " + di), line });
                    }
                }
            }
            catch (Exception dg11421_1) { Diag.Swallow("MainForm.ToolPages.cs:367", dg11421_1); }

            // 系统
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        string cap = Convert.ToString(mo["Caption"]).Trim();
                        string ver = Convert.ToString(mo["Version"]).Trim();
                        string arch = Convert.ToString(mo["OSArchitecture"]).Trim();
                        rows.Add(new string[] { T("操作系统", "OS"), cap + "  ·  " + ver + "  ·  " + arch });
                    }
                }
            }
            catch (Exception dg11437_1) { Diag.Swallow("MainForm.ToolPages.cs:383", dg11437_1); }

            // 分辨率
            try
            {
                Screen sc = Screen.PrimaryScreen;
                if (sc != null)
                    rows.Add(new string[] { T("屏幕分辨率", "Resolution"), sc.Bounds.Width + " × " + sc.Bounds.Height });
            }
            catch (Exception dg11446_1) { Diag.Swallow("MainForm.ToolPages.cs:392", dg11446_1); }
        }

        // 内存条类型：SMBIOSMemoryType 数字转名称，WMI 的 Manufacturer 常返回 0x0D70 这类厂商 ID
        string SysMemType(int t)
        {
            switch (t)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 22: return "DDR2 FB-DIMM";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return "";
            }
        }

        // 显卡真实显存（64 位）：WMI AdapterRAM 只有 32 位，16GB 会溢出成 4GB
        long SysGpuVram(string gpuName)
        {
            try
            {
                RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
                if (rk != null)
                {
                    string[] names = rk.GetSubKeyNames();
                    foreach (string k in names)
                    {
                        RegistryKey k1 = rk.OpenSubKey(k + @"\0000");
                        if (k1 == null) continue;
                        string adapter = "";
                        try
                        {
                            object ast = k1.GetValue("HardwareInformation.AdapterString");
                            if (ast is byte[]) adapter = Encoding.Unicode.GetString((byte[])ast).Replace("\0", "").Trim();
                            else if (ast is string) adapter = ((string)ast).Trim();
                        }
                        catch (Exception dg11489_1) { Diag.Swallow("MainForm.ToolPages.cs:435", dg11489_1); }
                        if (adapter.Length == 0) continue;
                        if (gpuName.IndexOf(adapter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        object q = null;
                        try { q = k1.GetValue("HardwareInformation.qwMemorySize"); }
                        catch (Exception dg11494_1) { Diag.Swallow("MainForm.ToolPages.cs:440", dg11494_1); }
                        if (q != null)
                        {
                            long v = Convert.ToInt64(q);
                            if (v > 0) return v;
                        }
                    }
                }
            }
            catch (Exception dg11503_1) { Diag.Swallow("MainForm.ToolPages.cs:449", dg11503_1); }
            return 0;
        }

        void SysCopy()
        {
            if (lvSys == null || lvSys.Items.Count == 0)
            {
                lblSysStatus.Text = T("还没有检测结果，先点「重新检测」。", "No data yet, click Detect Again.");
                lblSysStatus.ForeColor = UiKit.A(240, 160, 90);
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(T("【电脑配置】", "[PC Specs]"));
            foreach (ListViewItem it in lvSys.Items)
                sb.AppendLine(it.Text + "：" + (it.SubItems.Count > 1 ? it.SubItems[1].Text : ""));
            try
            {
                Clipboard.SetText(sb.ToString());
                lblSysStatus.Text = T("已复制到剪贴板。", "Copied to clipboard.");
                lblSysStatus.ForeColor = C_ON;
            }
            catch (Exception ex)
            {
                lblSysStatus.Text = T("复制失败：", "Copy failed: ") + ex.Message;
                lblSysStatus.ForeColor = UiKit.A(255, 120, 120);
            }
        }

        // ================= 去水印 =================
        void ShowWmPage(bool show)
        {
            if (pageWatermark == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageSam != null) pageSam.Visible = false;
            if (pageVideo != null) pageVideo.Visible = false;
            if (pageMusic != null) pageMusic.Visible = false;
            pageWatermark.Visible = show;
            if (show) { lblPeer.Text = T("去水印", "Watermark Remover"); lblPeer.ForeColor = ActCol("wm"); }
            else RestoreTimerHeader();
        }

        Label WmMiniLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = C_SUB;
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        void WmSetStatus(string text, Color c)
        {
            if (lblWmStatus == null) return;
            lblWmStatus.Text = text;
            lblWmStatus.ForeColor = c;
        }

        void WmClearCoordBoxes()
        {
            wmBoxLock = true;
            if (txtWmX != null) txtWmX.Text = "0";
            if (txtWmY != null) txtWmY.Text = "0";
            if (txtWmW != null) txtWmW.Text = "0";
            if (txtWmH != null) txtWmH.Text = "0";
            wmBoxLock = false;
        }

        void WmSelToBoxes()
        {
            if (txtWmX == null) return;
            wmBoxLock = true;
            txtWmX.Text = wmSel.X.ToString();
            txtWmY.Text = wmSel.Y.ToString();
            txtWmW.Text = wmSel.Width.ToString();
            txtWmH.Text = wmSel.Height.ToString();
            wmBoxLock = false;
        }

        void WmBoxesToSel()
        {
            if (wmBoxLock || txtWmX == null) return;
            int x, y, w, h;
            if (!int.TryParse(txtWmX.Text.Trim(), out x)) return;
            if (!int.TryParse(txtWmY.Text.Trim(), out y)) return;
            if (!int.TryParse(txtWmW.Text.Trim(), out w)) return;
            if (!int.TryParse(txtWmH.Text.Trim(), out h)) return;
            if (w <= 0 || h <= 0) { wmSel = Rectangle.Empty; wmPreview.Invalidate(); return; }
            wmSel = new Rectangle(x, y, w, h);
            wmPreview.Invalidate();
        }

        void WmPickFile()
        {
            if (wmBusy)
            {
                MessageBox.Show(this, T("正在处理中，请稍候。", "Busy, please wait."), T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OpenFileDialog d = new OpenFileDialog();
            d.Title = T("选择要去水印的视频或图片", "Pick a video or image");
            d.Filter = "视频文件|*.mp4;*.mkv;*.mov;*.avi;*.flv;*.wmv;*.webm;*.m4v;*.ts|图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|全部支持的文件|*.mp4;*.mkv;*.mov;*.avi;*.flv;*.wmv;*.webm;*.m4v;*.ts;*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            WmLoadFile(d.FileName);
        }

        void WmLoadFile(string path)
        {
            if (path == null || path.Length == 0 || !File.Exists(path)) return;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            wmImageMode = (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp" || ext == ".gif");
            wmSrcPath = path;
            wmSel = Rectangle.Empty;
            wmDragging = false;
            WmClearCoordBoxes();
            wmPreview.Image = null;
            if (wmPreviewBmp != null) { wmPreviewBmp.Dispose(); wmPreviewBmp = null; }
            wmImgW = wmImgH = 0;
            wmPreview.Invalidate();

            if (wmImageMode)
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    using (Image img = Image.FromStream(fs))
                        wmPreviewBmp = new Bitmap(img);
                    wmImgW = wmPreviewBmp.Width;
                    wmImgH = wmPreviewBmp.Height;
                    wmPreview.Image = wmPreviewBmp;
                    lblWmFile.Text = Path.GetFileName(path) + "  ·  " + wmImgW + " × " + wmImgH + T(" 像素 · 图片", " px · image");
                    WmSetStatus(T("已载入图片，在预览图上拖动框出水印区域。", "Image loaded. Drag a box over the watermark."), C_SUB);
                }
                catch (Exception ex)
                {
                    lblWmFile.Text = Path.GetFileName(path) + T("  ·  读取失败", "  ·  read failed");
                    WmSetStatus(T("图片读取失败：", "Image read failed: ") + ex.Message, UiKit.A(248, 113, 113));
                }
                return;
            }

            // 视频：抽首帧做预览
            lblWmFile.Text = Path.GetFileName(path) + T("  ·  视频（正在抽取预览帧…）", "  ·  video (extracting preview frame...)");
            WmSetStatus(T("正在抽取预览帧…", "Extracting preview frame..."), UiKit.A(240, 160, 90));
            if (!File.Exists(WmFfmpegPath())) UnpackResource("FfmpegZip", VideoToolDir(), "FFmpeg");
            if (!File.Exists(WmFfmpegPath()))
            {
                lblWmFile.Text = Path.GetFileName(path) + T("  ·  视频（缺少 FFmpeg）", "  ·  video (FFmpeg missing)");
                WmSetStatus(T("没有找到 FFmpeg，视频去水印无法运行。请先在「视频下载」页选择带「需FFmpeg」的高清格式，程序会自动部署 FFmpeg。",
                    "FFmpeg not found. Open the Video Download page and pick an HD format; FFmpeg will be deployed automatically."),
                    UiKit.A(248, 113, 113));
                return;
            }
            WmExtractFrame(path);
        }

        void WmExtractFrame(string path)
        {
            string tmp = Path.Combine(WmTempBase(), "wm_frame_" + DateTime.Now.Ticks + ".png");
            bool copiedIn = false;
            string inPath = path;
            try
            {
                inPath = WmPrepInput(path, out copiedIn, "frame");
                Process p = new Process();
                p.StartInfo.FileName = WmFfmpegPath();
                p.StartInfo.Arguments = "-y -hide_banner -loglevel error -ss 1 -i \"" + inPath + "\" -frames:v 1 -q:v 2 \"" + tmp + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                if (!p.WaitForExit(25000)) { try { p.Kill(); } catch (Exception dg11677_1) { Diag.Swallow("MainForm.ToolPages.cs:623", dg11677_1); } }
                if (File.Exists(tmp) && new FileInfo(tmp).Length > 1024)
                {
                    using (FileStream fs = new FileStream(tmp, FileMode.Open, FileAccess.Read))
                    using (Image img = Image.FromStream(fs))
                        wmPreviewBmp = new Bitmap(img);
                    wmImgW = wmPreviewBmp.Width;
                    wmImgH = wmPreviewBmp.Height;
                    wmPreview.Image = wmPreviewBmp;
                    lblWmFile.Text = Path.GetFileName(path) + "  ·  " + wmImgW + " × " + wmImgH + T(" 像素 · 视频", " px · video");
                    WmSetStatus(T("已载入视频首帧，在预览图上拖动框出水印区域。", "Video frame loaded. Drag a box over the watermark."), C_SUB);
                    try { File.Delete(tmp); } catch (Exception dg11688_1) { Diag.Swallow("MainForm.ToolPages.cs:634", dg11688_1); }
                    return;
                }
                lblWmFile.Text = Path.GetFileName(path) + T("  ·  视频（预览失败）", "  ·  video (preview failed)");
                WmSetStatus(T("抽帧失败，视频可能损坏或格式不受支持。", "Frame extraction failed; the video may be corrupt or unsupported."), UiKit.A(248, 113, 113));
            }
            catch (Exception ex)
            {
                WmSetStatus(T("抽帧异常：", "Frame error: ") + ex.Message, UiKit.A(248, 113, 113));
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception dg11700_1) { Diag.Swallow("MainForm.ToolPages.cs:646", dg11700_1); }
                try { if (copiedIn && File.Exists(inPath)) File.Delete(inPath); } catch (Exception dg11701_1) { Diag.Swallow("MainForm.ToolPages.cs:647", dg11701_1); }
            }
        }

        void WmPickOutDir()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = T("选择去水印输出目录", "Pick output folder");
            d.SelectedPath = wmOutDir;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                wmOutDir = d.SelectedPath;
                lblWmOut.Text = wmOutDir;
            }
        }

        void WmOpenDir()
        {
            try
            {
                if (!Directory.Exists(wmOutDir)) Directory.CreateDirectory(wmOutDir);
                Process.Start("explorer.exe", "\"" + wmOutDir + "\"");
            }
            catch (Exception dg11724_1) { Diag.Swallow("MainForm.ToolPages.cs:670", dg11724_1); }
        }

        string WmFfmpegPath()
        {
            return Path.Combine(VideoToolDir(), "ffmpeg.exe");
        }

        // FFmpeg 在 Windows 上对非 ASCII 路径支持很差，命令行里统一换成短路径 / 临时 ASCII 路径
        bool WmIsAscii(string s)
        {
            if (s == null) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] > 127) return false;
            return true;
        }

        string WmShortPath(string p)
        {
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint n = GetShortPathNameW(p, sb, 1024);
                if (n > 0 && n < 1024)
                {
                    string s = sb.ToString();
                    if (s.Length > 0 && WmIsAscii(s)) return s;
                }
            }
            catch (Exception dg11752_1) { Diag.Swallow("MainForm.ToolPages.cs:698", dg11752_1); }
            return null;
        }

        string WmTempBase()
        {
            string t = Path.GetTempPath();
            if (WmIsAscii(t)) return t;
            string s = WmShortPath(t);
            return s != null ? s : t;
        }

        // 返回 FFmpeg 能接受的输入路径；必要时复制到临时 ASCII 文件名
        string WmPrepInput(string src, out bool copied, string tag)
        {
            copied = false;
            if (WmIsAscii(src)) return src;
            string sp = WmShortPath(src);
            if (sp != null) return sp;
            string tmp = Path.Combine(WmTempBase(), "wm_" + tag + "_" + DateTime.Now.Ticks + Path.GetExtension(src));
            File.Copy(src, tmp, true);
            copied = true;
            return tmp;
        }

        // ---------- 预览坐标换算（Zoom 模式下的留白偏移） ----------
        Rectangle WmDisplayRect()
        {
            if (wmPreview == null || wmPreviewBmp == null) return Rectangle.Empty;
            double s = Math.Min((double)wmPreview.Width / wmPreviewBmp.Width, (double)wmPreview.Height / wmPreviewBmp.Height);
            int dw = Math.Max(1, (int)(wmPreviewBmp.Width * s));
            int dh = Math.Max(1, (int)(wmPreviewBmp.Height * s));
            int dx = (wmPreview.Width - dw) / 2;
            int dy = (wmPreview.Height - dh) / 2;
            return new Rectangle(dx, dy, dw, dh);
        }

        Point WmClientToImg(Point p)
        {
            Rectangle d = WmDisplayRect();
            if (d.Width <= 0 || wmPreviewBmp == null) return Point.Empty;
            int ix = (int)((p.X - d.X) * (double)wmPreviewBmp.Width / d.Width);
            int iy = (int)((p.Y - d.Y) * (double)wmPreviewBmp.Height / d.Height);
            if (ix < 0) ix = 0;
            if (iy < 0) iy = 0;
            if (ix > wmPreviewBmp.Width - 1) ix = wmPreviewBmp.Width - 1;
            if (iy > wmPreviewBmp.Height - 1) iy = wmPreviewBmp.Height - 1;
            return new Point(ix, iy);
        }

        Rectangle WmImgToClient(Rectangle r)
        {
            Rectangle d = WmDisplayRect();
            if (d.Width <= 0 || wmPreviewBmp == null) return Rectangle.Empty;
            int x = d.X + (int)(r.X * (double)d.Width / wmPreviewBmp.Width);
            int y = d.Y + (int)(r.Y * (double)d.Height / wmPreviewBmp.Height);
            int w = Math.Max(1, (int)(r.Width * (double)d.Width / wmPreviewBmp.Width));
            int h = Math.Max(1, (int)(r.Height * (double)d.Height / wmPreviewBmp.Height));
            return new Rectangle(x, y, w, h);
        }

        void WmMouseDown(MouseEventArgs e)
        {
            if (wmPreviewBmp == null || wmBusy || e.Button != MouseButtons.Left) return;
            wmDragging = true;
            wmDragStart = WmClientToImg(e.Location);
            wmSel = new Rectangle(wmDragStart.X, wmDragStart.Y, 0, 0);
            wmPreview.Invalidate();
        }

        void WmMouseMove(MouseEventArgs e)
        {
            if (!wmDragging || wmPreviewBmp == null) return;
            Point img = WmClientToImg(e.Location);
            wmSel = Rectangle.FromLTRB(
                Math.Min(img.X, wmDragStart.X),
                Math.Min(img.Y, wmDragStart.Y),
                Math.Max(img.X, wmDragStart.X),
                Math.Max(img.Y, wmDragStart.Y));
            wmPreview.Invalidate();
        }

        void WmMouseUp(MouseEventArgs e)
        {
            if (!wmDragging) return;
            wmDragging = false;
            if (wmSel.Width < 3 || wmSel.Height < 3)
            {
                wmSel = Rectangle.Empty;
                WmClearCoordBoxes();
                WmSetStatus(T("框选区域太小，请重新拖动。", "Box too small, drag again."), UiKit.A(240, 160, 90));
            }
            else
            {
                WmSelToBoxes();
                WmSetStatus(T("已框出 " + wmSel.Width + " × " + wmSel.Height + " 像素，可以点「开始处理」。",
                    "Boxed " + wmSel.Width + " x " + wmSel.Height + " px. Press Process."), C_ON);
            }
            wmPreview.Invalidate();
        }

        void WmPaint(PaintEventArgs e)
        {
            if (wmPreviewBmp == null)
            {
                // 空态：别留一整块白。虚线框 + 一句引导，说明这里该放什么。
                try
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rc = new Rectangle(24, 24, wmPreview.Width - 48, wmPreview.Height - 48);
                    using (GraphicsPath pp = UiKit.RoundedPath(rc, 10))
                    using (Pen pd = new Pen(Color.FromArgb(158, 144, 120), 1.4f))
                    {
                        pd.DashStyle = DashStyle.Dash;
                        g.DrawPath(pd, pp);
                    }
                    using (Font f1 = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold))
                    using (Font f2 = new Font("Microsoft YaHei UI", 8.5F))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(76, 69, 55)))
                            g.DrawString(T("拖入视频或图片，开始框选水印", "Drop a video or image to start"), f1, b1,
                                new RectangleF(rc.X, rc.Y + rc.Height / 2 - 30, rc.Width, 26), sf);
                        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(116, 105, 87)))
                            g.DrawString(T("支持 MP4 / MOV / MKV / AVI / FLV / WEBM / JPG / PNG / BMP", "MP4 / MOV / MKV / AVI / FLV / WEBM / JPG / PNG / BMP"), f2, b2,
                                new RectangleF(rc.X, rc.Y + rc.Height / 2 - 2, rc.Width, 22), sf);
                    }
                }
                catch (Exception dgWmEmpty_1) { Diag.Swallow("MainForm.ToolPages.cs:wmempty", dgWmEmpty_1); }
                return;
            }
            for (int i = 0; i < wmSels.Count; i++)
                WmPaintOne(e.Graphics, wmSels[i], UiKit.A(239, 68, 68), (i + 1).ToString());
            if (wmSel.Width > 0 && wmSel.Height > 0)
                WmPaintOne(e.Graphics, wmSel, UiKit.A(250, 204, 21), "+");
        }

        void WmPaintOne(Graphics g, Rectangle area, Color c, string tag)
        {
            Rectangle r = WmImgToClient(area);
            if (r.Width <= 0 || r.Height <= 0) return;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(c.A == 239 ? 58 : 70, c.R, c.G, c.B)))
                g.FillRectangle(b, r);
            using (Pen p = new Pen(c, 1.5f))
                g.DrawRectangle(p, r);
            using (Pen p2 = new Pen(UiKit.Txt(Color.White), 1))
            {
                p2.DashStyle = DashStyle.Dot;
                g.DrawRectangle(p2, r);
            }
            using (Font f = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
            using (SolidBrush bt = new SolidBrush(c))
                g.DrawString(tag, f, bt, r.X + 2, r.Y + 1);
        }

        List<Rectangle> WmAreas()
        {
            List<Rectangle> list = new List<Rectangle>();
            foreach (Rectangle r in wmSels)
                if (r.Width >= 3 && r.Height >= 3) list.Add(r);
            if (wmSel.Width >= 3 && wmSel.Height >= 3) list.Add(wmSel);
            return list;
        }

        void WmUpdateAreaTitle()
        {
            if (lblWmAreaTitle == null) return;
            lblWmAreaTitle.Text = T("水印区域（图像像素）", "Region (image pixels)")
                + (wmSels.Count > 0 ? T("  · 已存 " + wmSels.Count, "  · saved " + wmSels.Count) : "");
        }

        void WmAddAreaNow()
        {
            if (wmSel.Width < 3 || wmSel.Height < 3)
            {
                WmSetStatus(T("先在预览图上框出一个区域，再点「＋区域」。", "Box a region first, then press +Region."), UiKit.A(240, 160, 90));
                return;
            }
            bool dup = false;
            foreach (Rectangle r in wmSels) if (r.Equals(wmSel)) { dup = true; break; }
            if (!dup)
            {
                wmSels.Add(wmSel);
                WmUpdateAreaTitle();
                WmSetStatus(T("已加入第 " + wmSels.Count + " 个区域，继续框选可叠加。", "Region " + wmSels.Count + " added; box again to stack."), C_ON);
            }
            else
            {
                WmSetStatus(T("这个区域已经添加过了。", "That region is already in the list."), UiKit.A(240, 160, 90));
            }
            wmSel = Rectangle.Empty;
            WmClearCoordBoxes();
            wmPreview.Invalidate();
        }

        void WmAddRow(string file, int mode, string outPath, string status)
        {
            if (lvWm == null) return;
            string[] names = langEn
                ? new string[] { "Auto", "Video delogo", "Image inpaint", "Image blur", "Image mosaic" }
                : new string[] { "自动", "视频 delogo", "图片填补", "图片模糊", "图片马赛克" };
            ListViewItem it = new ListViewItem(file);
            it.SubItems.Add(mode >= 0 && mode < names.Length ? names[mode] : "-");
            it.SubItems.Add(outPath == "-" ? "-" : Path.GetFileName(outPath));
            it.SubItems.Add(status);
            if (status == T("完成", "Done")) it.ForeColor = UiKit.A(134, 239, 172);
            else if (status == T("失败", "Failed")) it.ForeColor = UiKit.A(248, 113, 113);
            lvWm.Items.Add(it);
            it.EnsureVisible();
        }

        // 自测通道：/wmtest=文件路径,x,y,w,h[,模式] —— 自动载入、框选、处理并把结果写入 _wm_test.txt
        void WmAutoTest(string spec)
        {
            string log = Path.Combine(BASE_DIR, "_wm_test.txt");
            try
            {
                string[] parts = spec.Split(',');
                string path = parts[0];
                int x = int.Parse(parts[1]);
                int y = int.Parse(parts[2]);
                int w = int.Parse(parts[3]);
                int h = int.Parse(parts[4]);
                int mode = parts.Length > 5 ? int.Parse(parts[5]) : 0;
                double vStart = parts.Length >= 12 ? double.Parse(parts[10]) : 0;
                double vDur = parts.Length >= 12 ? double.Parse(parts[11]) : 0;
                SelectAction(Array.IndexOf(SLOT, "wm"));
                Application.DoEvents();
                WmLoadFile(path);
                Application.DoEvents();
                List<Rectangle> testAreas = new List<Rectangle>();
                testAreas.Add(new Rectangle(x, y, w, h));
                if (parts.Length >= 10)
                    testAreas.Add(new Rectangle(int.Parse(parts[6]), int.Parse(parts[7]), int.Parse(parts[8]), int.Parse(parts[9])));
                wmSels.Clear();
                for (int ai = 1; ai < testAreas.Count; ai++) wmSels.Add(testAreas[ai]);
                wmSel = testAreas[0];
                WmSelToBoxes();
                WmUpdateAreaTitle();
                cmbWmMode.SelectedIndex = mode;
                wmPreview.Invalidate();
                int md = mode;
                string outPath = wmImageMode ? WmProcessImageSync(path, testAreas, md) : WmProcessVideoSync(path, testAreas, vStart, vDur);
                File.WriteAllText(log, "OK " + outPath + " size=" + new FileInfo(outPath).Length, Encoding.UTF8);
                WmAddRow(Path.GetFileName(path), md, outPath, T("完成", "Done"));
                WmSetStatus("自测完成：" + outPath, C_ON);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(log, "ERR " + ex.Message + "\r\n" + ex.StackTrace, Encoding.UTF8); } catch (Exception dg11974_1) { Diag.Swallow("MainForm.ToolPages.cs:920", dg11974_1); }
                WmSetStatus("自测失败：" + ex.Message, UiKit.A(248, 113, 113));
            }
        }

        // 自测通道：/wmbatch=文件1|文件2@x,y,w,h,模式 —— 走真实批量队列并把每行结果写入 _wm_test.txt
        void WmBatchAutoTest(string spec)
        {
            string log = Path.Combine(BASE_DIR, "_wm_test.txt");
            try
            {
                string[] seg = spec.Split('@');
                string[] files = seg[0].Split('|');
                string[] rc = seg[1].Split(',');
                int x = int.Parse(rc[0]);
                int y = int.Parse(rc[1]);
                int w = int.Parse(rc[2]);
                int h = int.Parse(rc[3]);
                int mode = rc.Length > 4 ? int.Parse(rc[4]) : 0;
                SelectAction(Array.IndexOf(SLOT, "wm"));
                Application.DoEvents();
                WmLoadFile(files[0]);
                Application.DoEvents();
                wmSels.Clear();
                wmSel = new Rectangle(x, y, w, h);
                WmSelToBoxes();
                WmUpdateAreaTitle();
                cmbWmMode.SelectedIndex = mode;
                wmPreview.Invalidate();
                WmBatchRun(files);
                DateTime t0 = DateTime.Now;
                while (wmBusy && (DateTime.Now - t0).TotalSeconds < 90) { Application.DoEvents(); Thread.Sleep(200); }
                Application.DoEvents();
                StringBuilder sb = new StringBuilder();
                sb.Append("BATCH rows=" + lvWm.Items.Count);
                for (int i = 0; i < lvWm.Items.Count; i++)
                {
                    ListViewItem it = lvWm.Items[i];
                    sb.Append("\r\n" + it.SubItems[0].Text + " | " + it.SubItems[2].Text + " | " + it.SubItems[3].Text);
                }
                File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(log, "ERR " + ex.Message + "\r\n" + ex.StackTrace, Encoding.UTF8); } catch (Exception dg12018_1) { Diag.Swallow("MainForm.ToolPages.cs:964", dg12018_1); }
            }
        }

        double WmParseSec(string text)
        {
            double v = 0;
            try { double.TryParse(text, out v); } catch (Exception dg12025_1) { Diag.Swallow("MainForm.ToolPages.cs:971", dg12025_1); }
            if (v < 0 || v > 86400) return 0;
            return v;
        }

        bool WmIsImageFile(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".jpg" || e == ".jpeg" || e == ".png" || e == ".bmp" || e == ".webp" || e == ".gif";
        }

        void WmBatchPick()
        {
            if (wmBusy) return;
            List<Rectangle> areas = WmAreas();
            if (areas.Count == 0)
            {
                MessageBox.Show(this, T("批量处理需要复用当前区域，请先在预览图上框出水印。",
                    "Batch reuses the current region; box the watermark first."),
                    T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            ofd.Title = T("选择要批量去水印的图片", "Pick images to batch process");
            ofd.Filter = T("图片文件", "Images")
                + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|" + T("所有文件", "All files") + "|*.*";
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            WmBatchRun(ofd.FileNames);
        }

        void WmBatchRun(string[] files)
        {
            if (files == null || files.Length == 0) return;
            List<Rectangle> areas = WmAreas();
            int md = cmbWmMode.SelectedIndex;
            wmBusy = true;
            btnWmRun.Enabled = false;
            btnWmBatch.Enabled = false;
            btnWmPick.Enabled = false;
            btnWmAddArea.Enabled = false;
            int total = files.Length, done = 0, okCnt = 0;
            WmSetStatus(T("批量队列已开始，共 " + total + " 个文件…", "Batch started, " + total + " files..."), UiKit.A(240, 160, 90));

            Thread th = new Thread(delegate()
            {
                foreach (string f in files)
                {
                    string name = Path.GetFileName(f);
                    if (!WmIsImageFile(f))
                    {
                        done++;
                        BeginInvoke((Action)delegate
                        {
                            WmAddRow(name, md, "-", T("跳过", "Skipped"));
                            WmSetStatus(T("已跳过视频：", "Skipped: ") + name, UiKit.A(240, 160, 90));
                        });
                        continue;
                    }
                    try
                    {
                        string outPath = WmProcessImageSync(f, areas, md);
                        done++; okCnt++;
                        BeginInvoke((Action)delegate
                        {
                            WmAddRow(name, md, outPath, T("完成", "Done"));
                            WmSetStatus(T("批量处理中 " + done + " / " + total + "：", "Batch " + done + " / " + total + ": ") + name,
                                UiKit.A(240, 160, 90));
                        });
                    }
                    catch (Exception ex)
                    {
                        done++;
                        string msg = ex.Message;
                        BeginInvoke((Action)delegate
                        {
                            WmAddRow(name, md, "-", T("失败", "Failed"));
                            WmSetStatus(T("失败：", "Failed: ") + name + " · " + msg, UiKit.A(248, 113, 113));
                        });
                    }
                }
                BeginInvoke((Action)delegate
                {
                    wmBusy = false;
                    btnWmRun.Enabled = true;
                    btnWmBatch.Enabled = true;
                    btnWmPick.Enabled = true;
                    btnWmAddArea.Enabled = true;
                    WmSetStatus(T("批量完成：成功 " + okCnt + " / 共 " + total + " 个，输出在 " + wmOutDir,
                        "Batch done: " + okCnt + " / " + total + " saved to " + wmOutDir), C_ON);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        void WmRun()
        {
            if (wmBusy) return;
            if (wmSrcPath == null || wmSrcPath.Length == 0 || !File.Exists(wmSrcPath))
            {
                MessageBox.Show(this, T("请先点「选择文件」，或把文件直接拖到预览框里。", "Pick a file first, or drag it onto the preview."), T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<Rectangle> areas = WmAreas();
            if (areas.Count == 0)
            {
                MessageBox.Show(this, T("请先在预览图上拖动，框出水印所在的区域。", "Drag a box over the watermark first."), T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int mode = cmbWmMode.SelectedIndex;
            if (wmImageMode && mode == 1)
            {
                MessageBox.Show(this, T("当前是图片，请选「图片 · 智能填补 / 高斯模糊 / 马赛克」，或直接选「自动识别」。",
                    "This is an image; choose an image mode or Auto."), T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!wmImageMode && mode >= 2)
            {
                MessageBox.Show(this, T("当前是视频，请选「视频 · delogo 滤波」或「自动识别」。",
                    "This is a video; choose Video delogo or Auto."), T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!wmImageMode && !File.Exists(WmFfmpegPath())) UnpackResource("FfmpegZip", VideoToolDir(), "FFmpeg");
            if (!wmImageMode && !File.Exists(WmFfmpegPath()))
            {
                MessageBox.Show(this, T("视频去水印需要 FFmpeg，但当前没有找到。请先在「视频下载」页触发一次 FFmpeg 部署，或把 ffmpeg.exe 放进 video_tool 目录。",
                    "FFmpeg is required for video. Deploy it from the Video Download page, or drop ffmpeg.exe into the video_tool folder."),
                    T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Directory.Exists(wmOutDir)) Directory.CreateDirectory(wmOutDir);

            wmBusy = true;
            btnWmRun.Enabled = false;
            btnWmPick.Enabled = false;
            btnWmClear.Enabled = false;
            string src = wmSrcPath;
            double stSec = WmParseSec(txtWmStart.Text);
            double duSec = WmParseSec(txtWmDur.Text);
            bool isImg = wmImageMode;
            int md = mode;
            WmSetStatus(T("正在处理，请稍候…", "Processing..."), UiKit.A(240, 160, 90));

            Thread th = new Thread(delegate()
            {
                string outPath = null;
                string err = null;
                DateTime t0 = DateTime.Now;
                try
                {
                    if (isImg) outPath = WmProcessImageSync(src, areas, md);
                    else outPath = WmProcessVideoSync(src, areas, stSec, duSec);
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        wmBusy = false;
                        btnWmRun.Enabled = true;
                        btnWmPick.Enabled = true;
                        btnWmClear.Enabled = true;
                        if (err == null && outPath != null)
                        {
                            int secs = (int)(DateTime.Now - t0).TotalSeconds;
                            WmSetStatus(T("处理完成：", "Done: ") + Path.GetFileName(outPath) + "  ·  " + secs + T(" 秒", " s"), C_ON);
                            WmAddRow(Path.GetFileName(src), md, outPath, T("完成", "Done"));
                            if (MessageBox.Show(this, T("去水印完成，已保存到：\n", "Done. Saved to:\n") + outPath + T("\n\n是否打开所在文件夹？", "\n\nOpen the folder?"),
                                T("去水印", "Watermark Remover"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            {
                                try { Process.Start("explorer.exe", "/select,\"" + outPath + "\""); } catch (Exception dg12196_1) { Diag.Swallow("MainForm.ToolPages.cs:1142", dg12196_1); }
                            }
                        }
                        else
                        {
                            WmSetStatus(T("处理失败：", "Failed: ") + (err == null ? "unknown" : err), UiKit.A(248, 113, 113));
                            WmAddRow(Path.GetFileName(src), md, "-", T("失败", "Failed"));
                            MessageBox.Show(this, T("处理失败：", "Failed: ") + (err == null ? "unknown" : err), T("去水印", "Watermark Remover"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    });
                }
                catch (Exception dg12207_1) { Diag.Swallow("MainForm.ToolPages.cs:1153", dg12207_1); }
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---------- 图片处理 ----------
        string WmProcessImageSync(string src, List<Rectangle> areas, int mode)
        {
            if (!Directory.Exists(wmOutDir)) Directory.CreateDirectory(wmOutDir);
            Bitmap bmp;
            using (FileStream fs = new FileStream(src, FileMode.Open, FileAccess.Read))
            using (Image img = Image.FromStream(fs))
                bmp = new Bitmap(img);
            try
            {
                int applied = 0;
                foreach (Rectangle area in areas)
                {
                    Rectangle rc = Rectangle.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height), area);
                    if (rc.Width < 1 || rc.Height < 1) continue;
                    int eff = mode == 0 ? 2 : mode;
                    if (eff == 2) WmInpaint(bmp, rc);
                    else if (eff == 3) WmBlurArea(bmp, rc, 8);
                    else WmMosaic(bmp, rc, 10);
                    applied++;
                }
                if (applied == 0) throw new Exception(T("选区超出图片范围", "Selection is outside the image"));

                string ext = Path.GetExtension(src);
                string el = ext.ToLowerInvariant();
                string outPath = Path.Combine(wmOutDir, Path.GetFileNameWithoutExtension(src) + T("_无水印", "_nw") + ext);
                if (el == ".jpg" || el == ".jpeg") WmSaveJpeg(bmp, outPath, 95);
                else if (el == ".bmp") bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Bmp);
                else bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                return outPath;
            }
            finally { bmp.Dispose(); }
        }

        void WmSaveJpeg(Bitmap bmp, string path, int quality)
        {
            System.Drawing.Imaging.ImageCodecInfo codec = null;
            foreach (System.Drawing.Imaging.ImageCodecInfo c in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) { codec = c; break; }
            if (codec == null) { bmp.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg); return; }
            System.Drawing.Imaging.EncoderParameters ep = new System.Drawing.Imaging.EncoderParameters(1);
            ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
            bmp.Save(path, codec, ep);
        }

        // 智能填补：从选区边界向内逐层扩散，每像素取已定邻居的均值，最后轻度平滑去条带
        void WmInpaint(Bitmap bmp, Rectangle rc)
        {
            int W = bmp.Width, H = bmp.Height;
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] buf = new byte[stride * H];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            try
            {
                int rw = rc.Width, rh = rc.Height;
                bool[] done = new bool[rw * rh];
                Queue<int> q = new Queue<int>();
                for (int y = rc.Y; y < rc.Bottom; y++)
                {
                    for (int x = rc.X; x < rc.Right; x++)
                    {
                        if (x == rc.X || y == rc.Y || x == rc.Right - 1 || y == rc.Bottom - 1)
                            q.Enqueue((y - rc.Y) * rw + (x - rc.X));
                    }
                }
                int[] dxs = { -1, 1, 0, 0 };
                int[] dys = { 0, 0, -1, 1 };
                while (q.Count > 0)
                {
                    int id = q.Dequeue();
                    if (done[id]) continue;
                    int x = rc.X + id % rw;
                    int y = rc.Y + id / rw;
                    long sr = 0, sg = 0, sb = 0;
                    int n = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + dxs[k], ny = y + dys[k];
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        bool inside = (nx >= rc.X && nx < rc.Right && ny >= rc.Y && ny < rc.Bottom);
                        if (inside)
                        {
                            int nid = (ny - rc.Y) * rw + (nx - rc.X);
                            if (!done[nid]) continue;
                        }
                        int off = ny * stride + nx * 4;
                        sb += buf[off]; sg += buf[off + 1]; sr += buf[off + 2];
                        n++;
                    }
                    done[id] = true;
                    if (n > 0)
                    {
                        int o2 = y * stride + x * 4;
                        buf[o2] = (byte)(sb / n);
                        buf[o2 + 1] = (byte)(sg / n);
                        buf[o2 + 2] = (byte)(sr / n);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + dxs[k], ny = y + dys[k];
                        if (nx >= rc.X && nx < rc.Right && ny >= rc.Y && ny < rc.Bottom)
                        {
                            int nid = (ny - rc.Y) * rw + (nx - rc.X);
                            if (!done[nid]) q.Enqueue(nid);
                        }
                    }
                }
                WmSmoothArea(buf, stride, rc, W, H, 2);
            }
            finally
            {
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                bmp.UnlockBits(bd);
            }
        }

        void WmSmoothArea(byte[] buf, int stride, Rectangle rc, int W, int H, int pass)
        {
            if (pass <= 0) return;
            byte[] tmp = new byte[buf.Length];
            for (int p = 0; p < pass; p++)
            {
                Buffer.BlockCopy(buf, 0, tmp, 0, buf.Length);
                for (int y = rc.Y; y < rc.Bottom; y++)
                {
                    for (int x = rc.X; x < rc.Right; x++)
                    {
                        int sr = 0, sg = 0, sb = 0, n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int ny = y + dy;
                            if (ny < 0 || ny >= H) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx;
                                if (nx < 0 || nx >= W) continue;
                                int off = ny * stride + nx * 4;
                                sb += tmp[off]; sg += tmp[off + 1]; sr += tmp[off + 2];
                                n++;
                            }
                        }
                        if (n == 0) continue;
                        int o2 = y * stride + x * 4;
                        buf[o2] = (byte)(sb / n);
                        buf[o2 + 1] = (byte)(sg / n);
                        buf[o2 + 2] = (byte)(sr / n);
                    }
                }
            }
        }

        void WmBlurArea(Bitmap bmp, Rectangle rc, int pass)
        {
            int W = bmp.Width, H = bmp.Height;
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] buf = new byte[stride * H];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            try
            {
                WmSmoothArea(buf, stride, rc, W, H, pass);
            }
            finally
            {
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                bmp.UnlockBits(bd);
            }
        }

        void WmMosaic(Bitmap bmp, Rectangle rc, int block)
        {
            int W = bmp.Width, H = bmp.Height;
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] buf = new byte[stride * H];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            try
            {
                for (int by = rc.Y; by < rc.Bottom; by += block)
                {
                    for (int bx = rc.X; bx < rc.Right; bx += block)
                    {
                        int ex = Math.Min(bx + block, rc.Right);
                        int ey = Math.Min(by + block, rc.Bottom);
                        long sr = 0, sg = 0, sb = 0;
                        int n = 0;
                        for (int y = by; y < ey; y++)
                        {
                            for (int x = bx; x < ex; x++)
                            {
                                int off = y * stride + x * 4;
                                sb += buf[off]; sg += buf[off + 1]; sr += buf[off + 2];
                                n++;
                            }
                        }
                        if (n == 0) continue;
                        byte ab = (byte)(sb / n), ag = (byte)(sg / n), ar = (byte)(sr / n);
                        for (int y = by; y < ey; y++)
                        {
                            for (int x = bx; x < ex; x++)
                            {
                                int off = y * stride + x * 4;
                                buf[off] = ab; buf[off + 1] = ag; buf[off + 2] = ar;
                            }
                        }
                    }
                }
            }
            finally
            {
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                bmp.UnlockBits(bd);
            }
        }

        // ---------- 视频处理 ----------
        double WmProbeDuration(string src)
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = WmFfmpegPath();
                p.StartInfo.Arguments = "-hide_banner -i \"" + src + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                string errOut = p.StandardError.ReadToEnd();
                try { p.WaitForExit(10000); } catch (Exception dg12445_1) { Diag.Swallow("MainForm.ToolPages.cs:1391", dg12445_1); }
                int idx = errOut.IndexOf("Duration:");
                if (idx < 0) return 0;
                string part = errOut.Substring(idx + 9, Math.Min(30, errOut.Length - idx - 9)).Trim();
                part = part.Split(',')[0].Trim();
                string[] hms = part.Split(':');
                if (hms.Length != 3) return 0;
                double s = 0;
                try
                {
                    s = int.Parse(hms[0]) * 3600 + int.Parse(hms[1]) * 60 + double.Parse(hms[2], System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { return 0; }
                return s;
            }
            catch { return 0; }
        }

        string WmProcessVideoSync(string src, List<Rectangle> areas, double startSec, double durSec)
        {
            if (!Directory.Exists(wmOutDir)) Directory.CreateDirectory(wmOutDir);
            string ff = WmFfmpegPath();
            if (!File.Exists(ff)) throw new Exception(T("FFmpeg 未就绪", "FFmpeg not ready"));
            int W = wmImgW, H = wmImgH;
            if (W <= 0 || H <= 0) throw new Exception(T("视频尺寸未知，请重新选择文件", "Video size unknown, pick the file again"));

            List<string> filters = new List<string>();
            foreach (Rectangle area in areas)
            {
                int x = Math.Max(1, area.X);
                int y = Math.Max(1, area.Y);
                int w = Math.Min(area.Width, W - 1 - x);
                int h = Math.Min(area.Height, H - 1 - y);
                if (x + w >= W) w = W - x - 1;
                if (y + h >= H) h = H - y - 1;
                if (w < 2 || h < 2) continue;
                filters.Add("delogo=x=" + x + ":y=" + y + ":w=" + w + ":h=" + h);
            }
            if (filters.Count == 0) throw new Exception(T("水印区域太靠边或太小，请往画面内侧重新框选", "Region too close to the edge or too small; box it again inward"));
            string vf = string.Join(",", filters.ToArray());
            string timeArgs = "";
            if (startSec > 0) timeArgs += " -ss " + startSec.ToString("0.###");
            if (durSec > 0) timeArgs += " -t " + durSec.ToString("0.###");
            string ext = Path.GetExtension(src);
            string finalPath = Path.Combine(wmOutDir, Path.GetFileNameWithoutExtension(src) + T("_无水印", "_nw") + ext);
            bool copiedIn = false;
            string inPath = src;
            string tmpOut = Path.Combine(WmTempBase(), "wm_out_" + DateTime.Now.Ticks + ext);
            try
            {
                inPath = WmPrepInput(src, out copiedIn, "vid");
                string args = "-y -hide_banner -loglevel error -progress pipe:1 -nostats -i \"" + inPath + "\"" + timeArgs + " -vf \"" + vf
                    + "\" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 192k \"" + tmpOut + "\"";

                double total = WmProbeDuration(inPath);
                Process p = new Process();
                p.StartInfo.FileName = ff;
                p.StartInfo.Arguments = args;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                DateTime last = DateTime.Now;
                // 卡死保护：ffmpeg 60 秒没有任何新输出就直接结束，避免界面永远停在「处理中…」
                DateTime lastBeat = DateTime.Now;
                Thread wd = new Thread(delegate ()
                {
                    while (true)
                    {
                        Thread.Sleep(2000);
                        if (p.HasExited) break;
                        if ((DateTime.Now - lastBeat).TotalSeconds > 60)
                        {
                            try { p.Kill(); } catch (Exception dg12519_1) { Diag.Swallow("MainForm.ToolPages.cs:1465", dg12519_1); }
                            break;
                        }
                    }
                });
                wd.IsBackground = true;
                wd.Start();
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    lastBeat = DateTime.Now;
                    if (line.StartsWith("out_time_ms="))
                    {
                        long us = 0;
                        if (long.TryParse(line.Substring(12).Trim(), out us))
                        {
                            if (DateTime.Now.Subtract(last).TotalMilliseconds > 400)
                            {
                                last = DateTime.Now;
                                double sec = us / 1000000.0;
                                string txt;
                                if (total > 0)
                                {
                                    int pct = (int)Math.Min(99, (sec / total) * 100);
                                    txt = T("处理中… " + pct + "%（", "Processing... " + pct + "% (") + WmFmtSec(sec) + " / " + WmFmtSec(total) + "）";
                                }
                                else txt = T("处理中… ", "Processing... ") + WmFmtSec(sec);
                                BeginInvoke((Action)delegate { WmSetStatus(txt, UiKit.A(240, 160, 90)); });
                            }
                        }
                    }
                    else if (line == "progress=end") break;
                }
                string errTxt = "";
                try { errTxt = p.StandardError.ReadToEnd(); } catch (Exception dg12553_1) { Diag.Swallow("MainForm.ToolPages.cs:1499", dg12553_1); }
                p.WaitForExit();
                if (p.ExitCode != 0 || !File.Exists(tmpOut))
                {
                    string brief = errTxt == null ? "" : errTxt.Trim();
                    if (brief.Length > 400) brief = brief.Substring(brief.Length - 400);
                    throw new Exception(T("FFmpeg 处理失败：", "FFmpeg failed: ") + brief);
                }
                try { if (File.Exists(finalPath)) File.Delete(finalPath); } catch (Exception dg12561_1) { Diag.Swallow("MainForm.ToolPages.cs:1507", dg12561_1); }
                File.Move(tmpOut, finalPath);
                return finalPath;
            }
            finally
            {
                try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch (Exception dg12567_1) { Diag.Swallow("MainForm.ToolPages.cs:1513", dg12567_1); }
                try { if (copiedIn && File.Exists(inPath)) File.Delete(inPath); } catch (Exception dg12568_1) { Diag.Swallow("MainForm.ToolPages.cs:1514", dg12568_1); }
            }
        }

        string WmFmtSec(double s)
        {
            int t = (int)s;
            int hh = t / 3600, mm = (t % 3600) / 60, ss = t % 60;
            if (hh > 0) return hh + ":" + mm.ToString("00") + ":" + ss.ToString("00");
            return mm.ToString("00") + ":" + ss.ToString("00");
        }

        string VideoToolDir() { return Path.Combine(BASE_DIR, "video_tool"); }
        string VideoToolExe() { return Path.Combine(VideoToolDir(), "yt-dlp.exe"); }

        bool EnsureVideoTool()
        {
            string exe = VideoToolExe();
            if (File.Exists(exe)) return true;
            Cursor.Current = Cursors.WaitCursor;
            bool ok = UnpackResource("VideoZip", VideoToolDir(), "视频下载引擎");
            Cursor.Current = Cursors.Default;
            if (ok && File.Exists(exe))
            {
                MessageBox.Show(this, "视频下载引擎已自动部署完成，现在可以正常使用了。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            MessageBox.Show(this, "视频下载引擎（yt-dlp）缺失且自动部署失败。\n请重新安装小工具箱后再试。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        void PickVideoDir()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选择视频保存目录";
            d.SelectedPath = vOutDir;
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                vOutDir = d.SelectedPath;
                lblVDir.Text = vOutDir;
            }
        }

        void OpenVideoDir()
        {
            if (!Directory.Exists(vOutDir)) Directory.CreateDirectory(vOutDir);
            try { Process.Start("explorer.exe", "\"" + vOutDir + "\""); } catch (Exception dg12614_1) { Diag.Swallow("MainForm.ToolPages.cs:1560", dg12614_1); }
        }

        void OpenMusicDir()
        {
            string dir = Path.Combine(vOutDir, "音乐");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch (Exception dg12621_1) { Diag.Swallow("MainForm.ToolPages.cs:1567", dg12621_1); }
        }

        // ---------- 代理支持 ----------
        bool TcpPortOpen(int port)
        {
            try
            {
                var c = new System.Net.Sockets.TcpClient();
                var ar = c.BeginConnect("127.0.0.1", port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(300);
                if (ok) { c.EndConnect(ar); c.Close(); return true; }
                c.Close();
                return false;
            }
            catch { return false; }
        }

        string DetectProxy()
        {
            if (TcpPortOpen(10808)) return "socks5://127.0.0.1:10808";   // v2rayN 默认
            if (TcpPortOpen(10809)) return "http://127.0.0.1:10809";     // v2rayN http
            if (TcpPortOpen(7890)) return "http://127.0.0.1:7890";       // Clash
            if (TcpPortOpen(1080)) return "socks5://127.0.0.1:1080";
            return "";
        }

        string ProxyArgs()
        {
            string p = txtVProxy != null ? txtVProxy.Text.Trim() : "";
            if (p.Length == 0) p = DetectProxy();
            return p.Length > 0 ? " --proxy \"" + p + "\"" : "";
        }

        // ---------- 视频解析 ----------
        void VideoParse()
        {
            if (vBusy) return;
            string url = txtVUrl.Text.Trim();
            if (url.Length == 0) { MessageBox.Show(this, "请先粘贴视频链接。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!EnsureVideoTool()) return;
            vBusy = true;
            btnVParse.Enabled = false;
            btnVParse.Text = "解析中…";
            lblVInfo.Text = "正在解析视频信息…";
            lblVInfo.ForeColor = C_SUB;
            cmbVFormat.Items.Clear();
            Thread t = new Thread(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), "-J --no-playlist --no-warnings --no-check-certificate" + ProxyArgs() + " \"" + url + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                    Process p = Process.Start(psi);
                    // 两路都异步读：只读 stdout 再读 stderr，会在 stderr 写满缓冲区时死锁
                    StringBuilder soSb = new StringBuilder(), seSb = new StringBuilder();
                    p.OutputDataReceived += delegate(object s3, DataReceivedEventArgs e3) { if (e3.Data != null) soSb.AppendLine(e3.Data); };
                    p.ErrorDataReceived += delegate(object s3, DataReceivedEventArgs e3) { if (e3.Data != null) seSb.AppendLine(e3.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    bool exited = false;
                    try { exited = p.WaitForExit(60000); } catch (Exception dg12687_1) { Diag.Swallow("MainForm.ToolPages.cs:1633", dg12687_1); }
                    if (!exited) { try { p.Kill(); } catch (Exception dg12688_1) { Diag.Swallow("MainForm.ToolPages.cs:1634", dg12688_1); } }
                    string json = soSb.ToString();
                    string err = exited ? seSb.ToString() : "解析超时（60 秒无响应），已中止";
                    object[] res = exited ? ParseYtJson(json) : null;
                    this.Invoke((Action)(() =>
                    {
                        if (res == null)
                        {
                            lblVInfo.Text = "解析失败：\n" + (err.Trim().Length > 0 ? FirstLine(err) : "无法获取视频信息（链接无效 / 平台限制 / 需要登录）")
                                + "\n若为国外平台（YouTube 等），请开启代理后重试，或点击「自动检测」配置代理。";
                            lblVInfo.ForeColor = UiKit.A(240, 120, 100);
                        }
                        else
                        {
                            lblVInfo.Text = (string)res[0];
                            lblVInfo.ForeColor = C_SUB;
                            List<object[]> fmts = (List<object[]>)res[1];
                            cmbVFormat.Items.Clear();
                            vFmtIds.Clear();
                            vFmtMerge.Clear();
                            foreach (object[] f in fmts)
                            {
                                cmbVFormat.Items.Add((string)f[1]);
                                vFmtIds.Add((string)f[0]);
                                vFmtMerge.Add((bool)f[3]);
                            }
                            if (cmbVFormat.Items.Count > 0) cmbVFormat.SelectedIndex = 0;
                            lblVTip.Text = "已解析 " + fmts.Count + " 种格式，选择后点击「开始下载」。";
                            lblVTip.ForeColor = UiKit.A(240, 160, 90);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() => { lblVInfo.Text = "解析异常：" + ex.Message; lblVInfo.ForeColor = UiKit.A(240, 120, 100); }));
                }
                finally { this.Invoke((Action)(() => { vBusy = false; btnVParse.Enabled = true; btnVParse.Text = "解析"; })); }
            });
            t.IsBackground = true;
            t.Start();
        }

        object[] ParseYtJson(string json)
        {
            try
            {
                if (json == null || json.Trim().Length == 0) return null;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
                if (root == null || !root.ContainsKey("title")) return null;
                string title = Convert.ToString(root["title"]);
                List<object[]> fmts = new List<object[]>();
                if (root.ContainsKey("formats"))
                {
                    object[] arr = ToArr(root["formats"]);
                    if (arr != null)
                    {
                        List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
                        foreach (object o in arr)
                        {
                            Dictionary<string, object> d = o as Dictionary<string, object>;
                            if (d != null) list.Add(d);
                        }
                        list.Sort((a, b) => HOf(b).CompareTo(HOf(a)));
                        HashSet<string> seen = new HashSet<string>();
                        foreach (Dictionary<string, object> d in list)
                        {
                            string id = SOf(d, "format_id");
                            string ext = SOf(d, "ext");
                            string vc = SOf(d, "vcodec");
                            string ac = SOf(d, "acodec");
                            if (id.Length == 0) continue;
                            bool hasV = vc.Length > 0 && vc != "none";
                            if (!hasV) continue;
                            int h = HOf(d);
                            string note = h > 0 ? h + "p" : SOf(d, "format_note");
                            if (note.Length == 0) note = ext;
                            string size = "";
                            object fsz;
                            if (d.TryGetValue("filesize", out fsz) && fsz != null)
                            {
                                long fs = 0; long.TryParse(Convert.ToString(fsz), out fs);
                                if (fs > 0) size = " · 约 " + FmtSize(fs);
                            }
                            bool merge = ac.Length == 0 || ac == "none";
                            string key = (merge ? "M" : "F") + h + "_" + ext;
                            if (!seen.Add(key)) continue;
                            string disp = note + " " + ext + (merge ? "（需FFmpeg）" : "") + size;
                            fmts.Add(new object[] { id, disp, h, merge });
                        }
                        // 无需合并的排前面
                        fmts.Sort((a, b) => { int ma = (bool)a[3] ? 1 : 0; int mb = (bool)b[3] ? 1 : 0; return ma.CompareTo(mb); });
                        if (fmts.Count == 0) return null;
                    }
                }
                else if (root.ContainsKey("requested_formats")) { } // 单格式情况忽略，fmts 空则下面回退
                return new object[] { title, fmts };
            }
            catch { return null; }
        }

        string SOf(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        // JavaScriptSerializer 的 JSON 数组是 ArrayList，统一转 object[]
        object[] ToArr(object v)
        {
            if (v == null) return null;
            if (v is object[]) return (object[])v;
            System.Collections.ArrayList al = v as System.Collections.ArrayList;
            if (al == null) return null;
            object[] r = new object[al.Count];
            for (int i = 0; i < al.Count; i++) r[i] = al[i];
            return r;
        }

        int HOf(Dictionary<string, object> d)
        {
            object v;
            int h = 0;
            if (d.TryGetValue("height", out v) && v != null) int.TryParse(Convert.ToString(v), out h);
            return h;
        }

        string FmtSize(long b)
        {
            if (b >= 1024L * 1024 * 1024) return (b / 1048576.0 / 1024.0).ToString("0.0") + "GB";
            if (b >= 1048576) return (b / 1048576.0).ToString("0.0") + "MB";
            if (b >= 1024) return (b / 1024.0).ToString("0.0") + "KB";
            return b + "B";
        }

        string FirstLine(string s)
        {
            string[] ls = s.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return ls.Length > 0 ? ls[0] : s;
        }

        // ---------- 视频下载 ----------
        void VideoDownload()
        {
            if (vBusy) { MessageBox.Show(this, "正在解析中，请稍候…", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (vDlProc != null) { MessageBox.Show(this, "已有下载任务进行中，请等待完成。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            bool audioOnly = (vAudioOnly != null && vAudioOnly.Checked);
            if (!audioOnly && cmbVFormat.SelectedIndex < 0) { MessageBox.Show(this, "请先解析视频并选择清晰度（或勾选「仅音频」）。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string url = txtVUrl.Text.Trim();
            if (url.Length == 0) { MessageBox.Show(this, "请先粘贴视频链接。", "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!EnsureVideoTool()) return;
            int selIdx = audioOnly ? -1 : cmbVFormat.SelectedIndex;
            string fmtId = audioOnly ? "ba/b" : vFmtIds[selIdx];
            bool needMerge = audioOnly ? true : vFmtMerge[selIdx];
            if (needMerge && !File.Exists(Path.Combine(VideoToolDir(), "ffmpeg.exe")))
            {
                if (MessageBox.Show(this, "该格式需要 FFmpeg 合并音视频（高清视频通常需要）。\n是否现在自动获取 FFmpeg（约90MB）？", "视频下载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                while (!EnsureFfmpeg())
                {
                    DialogResult dr = ShowFfmpegFailDialog();
                    if (dr != DialogResult.Retry) return;
                }
            }
            if (!Directory.Exists(vOutDir)) Directory.CreateDirectory(vOutDir);
            string title = lblVInfo.Text.Length > 0 ? lblVInfo.Text : "视频";
            ListViewItem li = lvVideos.Items.Add(title);
            li.SubItems.Add("");
            li.SubItems.Add("0%");
            li.SubItems.Add("");
            li.SubItems.Add("排队中");
            lblVTip.Text = "开始下载：" + title;
            lblVTip.ForeColor = UiKit.A(240, 160, 90);
            string extra = audioOnly ? " -x --audio-format mp3 --audio-quality 0 --no-part" : " --write-auto-subs --sub-langs \"zh.*,en.*\" --convert-subs srt";
            string args = "--yes-playlist --continue --windows-filenames --no-check-certificate --newline --retries 20 --fragment-retries 20 --concurrent-fragments 4" + extra + ProxyArgs() + " -f \"" + fmtId + "\" -o \"" + Path.Combine(vOutDir, "%(title)s.%(ext)s") + "\" --progress-template \"download:DP|%(progress._percent_str)s|%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s|%(progress._speed_str)s|%(progress._eta_str)s\" \"" + url + "\"";
            ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
            Dictionary<string, DateTime> snap = SnapDir(vOutDir);   // v13.2：记下现有文件，下载完才能认出新文件
            try
            {
                Process p = Process.Start(psi);
                p.BeginOutputReadLine();
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    this.Invoke((Action)(() => HandleVLine(li, e.Data)));
                };
                p.EnableRaisingEvents = true;
                p.Exited += (s, e) =>
                {
                    this.Invoke((Action)(() =>
                    {
                        vDlProc = null;
                        if (p.ExitCode == 0 && vCmpOn != null && vCmpOn.Checked)
                        {
                            List<string> nv = NewVideos(vOutDir, snap);
                            if (nv.Count > 0) VideoCompressRun(nv.ToArray());
                            else lblVTip.Text = "下载完成，但没识别到新文件，已跳过自动压缩。";
                        }
                        if (li.ListView != null && !li.ListView.IsDisposed)
                        {
                            if (li.SubItems[4].Text != "失败") { li.SubItems[4].Text = (p.ExitCode == 0) ? "完成" : "失败"; if (p.ExitCode == 0) li.SubItems[2].Text = "100%"; }
                        }
                        if (p.ExitCode != 0) lblVTip.Text = "下载失败，详见列表状态。";
                        else { lblVTip.Text = "下载完成，文件已保存到 " + vOutDir; Sfx.Done(); }
                    }));
                };
                vDlProc = p;
            }
            catch (Exception ex)
            {
                li.SubItems[4].Text = "失败";
                MessageBox.Show(this, "启动下载失败：" + ex.Message, "视频下载", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void HandleVLine(ListViewItem li, string line)
        {
            if (li.ListView == null || li.ListView.IsDisposed) return;
            if (line.StartsWith("DP|"))
            {
                string[] parts = line.Split('|');
                string pct = parts.Length > 1 ? parts[1].Trim() : "";
                string dl = parts.Length > 2 ? parts[2].Trim() : "";
                string total = parts.Length > 3 ? parts[3].Trim() : "";
                string spd = parts.Length > 4 ? parts[4].Trim() : "";
                string eta = parts.Length > 5 ? parts[5].Trim() : "";
                li.SubItems[2].Text = pct;
                li.SubItems[3].Text = spd;
                li.SubItems[4].Text = eta.Length > 0 ? "下载中 · 剩余 " + eta : "下载中";
                if (total.Length > 0 && li.SubItems[1].Text.Length == 0) li.SubItems[1].Text = total;
                else if (dl.Length > 0 && total.Length > 0) li.SubItems[1].Text = dl + " / " + total;
            }
            else if (line.StartsWith("ERROR"))
            {
                li.SubItems[4].Text = "失败";
                lblVTip.Text = "下载失败：" + line;
                lblVTip.ForeColor = UiKit.A(240, 120, 100);
            }
            else if (line.Contains("[download] 100%"))
            {
                li.SubItems[2].Text = "100%";
                li.SubItems[4].Text = "完成";
            }
        }

        // ---------- v13.2 视频压缩（下载页工具条 + 下载后自动压） ----------
        int CmpTargetMb()
        {
            if (cmbVCmpMb == null || cmbVCmpMb.SelectedItem == null) return 0;
            string t = cmbVCmpMb.SelectedItem.ToString();
            if (t.IndexOf("质量") >= 0) return 0;
            string num = t.Replace("MB", "").Replace(" ", "").Trim();
            int mb = 0;
            return int.TryParse(num, out mb) ? mb : 0;
        }

        void VCmpUi(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (Exception dg12954_1) { Diag.Swallow("MainForm.ToolPages.cs:1900", dg12954_1); }
        }

        void VideoCompressPick()
        {
            if (vCmpBusy) { lblVTip.Text = "已有压缩任务在跑，等它结束再试。"; lblVTip.ForeColor = UiKit.A(240, 160, 90); return; }
            if (VideoCmp.Ffmpeg(BASE_DIR).Length == 0)
            {
                MessageBox.Show(this, "压缩需要 FFmpeg：先在「视频下载」页用带 FFmpeg 的高清格式下一次，程序会自动部署。",
                    "视频压缩", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OpenFileDialog od = new OpenFileDialog();
            od.Filter = "视频|*.mp4;*.mkv;*.mov;*.avi;*.flv;*.wmv;*.webm;*.ts|所有文件|*.*";
            od.Multiselect = true;
            if (od.ShowDialog(this) != DialogResult.OK) return;
            VideoCompressRun(od.FileNames);
        }

        void VideoCompressRun(string[] files)
        {
            if (files == null || files.Length == 0 || vCmpBusy) return;
            int mb = CmpTargetMb();
            List<string> todo = new List<string>();
            foreach (string f in files) { try { if (File.Exists(f)) todo.Add(f); } catch (Exception dg12978_1) { Diag.Swallow("MainForm.ToolPages.cs:1924", dg12978_1); } }
            if (todo.Count == 0) return;
            vCmpBusy = true;
            btnVCmp.Enabled = false;
            lblVCmp.ForeColor = UiKit.A(240, 186, 90);
            lblVCmp.Text = "准备 0/" + todo.Count;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int done = 0, fail = 0;
                for (int i = 0; i < todo.Count; i++)
                {
                    int idx = i;
                    string src = todo[i];
                    string nm = Path.GetFileName(src);
                    long srcSize = 0;
                    try { srcSize = new FileInfo(src).Length; } catch (Exception dg12993_1) { Diag.Swallow("MainForm.ToolPages.cs:1939", dg12993_1); }
                    VCmpUi(delegate { lblVCmp.Text = "压缩 " + (idx + 1) + "/" + todo.Count + " · 0%"; });
                    string outFile = "", err = "";
                    bool ok = VideoCmp.Compress(BASE_DIR, src, mb, 0, out outFile, out err, delegate(int pct, string txt)
                    {
                        VCmpUi(delegate
                        {
                            string tail = txt.Length > 0 ? " · " + txt : "";
                            if (pct >= 0) lblVCmp.Text = "压缩 " + (idx + 1) + "/" + todo.Count + " · " + pct + "%" + tail;
                            else if (txt.Length > 0) lblVCmp.Text = "压缩 " + (idx + 1) + "/" + todo.Count + " · " + txt;
                        });
                    });
                    if (!ok)
                    {
                        fail++;
                        VCmpUi(delegate
                        {
                            lblVCmp.Text = "失败 " + (idx + 1) + "/" + todo.Count;
                            lblVCmp.ForeColor = UiKit.A(240, 120, 100);
                            ListViewItem lf = lvVideos.Items.Add(nm);
                            lf.SubItems.Add("");
                            lf.SubItems.Add("");
                            lf.SubItems.Add("");
                            lf.SubItems.Add("压缩失败");
                        });
                        continue;
                    }
                    done++;
                    long outSize = 0;
                    try { outSize = new FileInfo(outFile).Length; } catch (Exception dg13022_1) { Diag.Swallow("MainForm.ToolPages.cs:1968", dg13022_1); }
                    double ratio = srcSize > 0 ? (1.0 - (double)outSize / srcSize) * 100.0 : 0;
                    string onm = Path.GetFileName(outFile);
                    string txtSize = (outSize / 1048576.0).ToString("0.00") + " MB";
                    string txtRate = (ratio >= 0 ? "-" : "+") + Math.Abs(ratio).ToString("0") + "%";
                    VCmpUi(delegate
                    {
                        ListViewItem li = lvVideos.Items.Add(onm);
                        li.SubItems.Add(txtSize);
                        li.SubItems.Add("100%");
                        li.SubItems.Add(txtRate);
                        li.SubItems.Add("已压缩");
                        lblVCmp.Text = "完成 " + done + "/" + todo.Count;
                        lblVCmp.ForeColor = UiKit.A(120, 220, 150);
                    });
                }
                VCmpUi(delegate
                {
                    vCmpBusy = false;
                    btnVCmp.Enabled = true;
                    lblVTip.Text = "压缩结束：成功 " + done + " 个，失败 " + fail + " 个，结果在源文件旁的 _压缩 目录。";
                    lblVTip.ForeColor = fail == 0 ? UiKit.A(120, 220, 150) : UiKit.A(240, 160, 90);
                });
            });
        }

        Dictionary<string, DateTime> SnapDir(string dir)
        {
            Dictionary<string, DateTime> d = new Dictionary<string, DateTime>();
            try
            {
                if (!Directory.Exists(dir)) return d;
                foreach (string f in Directory.GetFiles(dir))
                {
                    try { d[f] = File.GetLastWriteTime(f); } catch (Exception dg13056_1) { Diag.Swallow("MainForm.ToolPages.cs:2002", dg13056_1); }
                }
            }
            catch (Exception dg13059_1) { Diag.Swallow("MainForm.ToolPages.cs:2005", dg13059_1); }
            return d;
        }

        List<string> NewVideos(string dir, Dictionary<string, DateTime> before)
        {
            List<string> r = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if (ext != ".mp4" && ext != ".mkv" && ext != ".webm" && ext != ".flv" && ext != ".mov" && ext != ".ts") continue;
                    DateTime t;
                    if (before.TryGetValue(f, out t) && t == File.GetLastWriteTime(f)) continue;
                    r.Add(f);
                }
            }
            catch (Exception dg13077_1) { Diag.Swallow("MainForm.ToolPages.cs:2023", dg13077_1); }
            return r;
        }

        // ---------- v13.2 自检：视频压缩引擎 ----------
        void CmpSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string ff = VideoCmp.Ffmpeg(BASE_DIR);
                sb.AppendLine("ffmpeg=" + (ff.Length > 0 ? ff : "MISSING"));
                if (ff.Length == 0) { File.WriteAllText(Path.Combine(BASE_DIR, "_cmptest.txt"), sb.ToString(), Encoding.UTF8); Close(); return; }
                string src = Path.Combine(BASE_DIR, "_cmptest_src.mp4");
                try { if (File.Exists(src)) File.Delete(src); } catch (Exception dg13091_1) { Diag.Swallow("MainForm.ToolPages.cs:2037", dg13091_1); }
                string gen = "-hide_banner -y -f lavfi -i \"testsrc2=size=1920x1080:rate=30:duration=12\" -f lavfi -i \"sine=frequency=440:duration=12\" -c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 128k \"" + src + "\"";
                string go = VideoProCore.Run(ff, gen, 180000, null);
                sb.AppendLine("gen=" + (File.Exists(src) ? new FileInfo(src).Length.ToString() : "FAIL") + " tail=" + (go.Length > 120 ? go.Substring(go.Length - 120).Replace("\n", " ") : go.Replace("\n", " ")));
                double dur = VideoProCore.Duration(BASE_DIR, src);
                sb.AppendLine("dur=" + dur.ToString("0.0"));
                int maxPct = 0, seen = 0;
                string o1 = "", e1 = "";
                DateTime t0 = DateTime.Now;
                bool ok1 = VideoCmp.Compress(BASE_DIR, src, 3, 0, out o1, out e1, delegate(int pct, string txt) { seen++; if (pct > maxPct) maxPct = pct; });
                sb.AppendLine("target3MB ok=" + ok1 + " bytes=" + (ok1 ? new FileInfo(o1).Length.ToString() : "0")
                    + " ratio=" + (ok1 ? ((1.0 - (double)new FileInfo(o1).Length / new FileInfo(src).Length) * 100).ToString("0") + "%" : "-")
                    + " ms=" + (int)(DateTime.Now - t0).TotalMilliseconds + " progress_callbacks=" + seen + " maxPct=" + maxPct
                    + " err=" + e1);
                string o2 = "", e2 = "";
                DateTime t1 = DateTime.Now;
                bool ok2 = VideoCmp.Compress(BASE_DIR, src, 0, 720, out o2, out e2, null);
                sb.AppendLine("crf720 ok=" + ok2 + " bytes=" + (ok2 ? new FileInfo(o2).Length.ToString() : "0")
                    + " ms=" + (int)(DateTime.Now - t1).TotalMilliseconds + " err=" + e2);
                sb.AppendLine("PASS=" + ((ok1 && ok2) ? "1" : "0"));
                try { File.Delete(src); } catch (Exception dg13111_1) { Diag.Swallow("MainForm.ToolPages.cs:2057", dg13111_1); }
            }
            catch (Exception ex)
            {
                sb.AppendLine("EX=" + ex.Message);
            }
            File.WriteAllText(Path.Combine(BASE_DIR, "_cmptest.txt"), sb.ToString(), Encoding.UTF8);
            Close();
        }

        // ---------- v13.2 截图：视频进阶窗（压缩区改版后自检） ----------
        void ProShot()
        {
            StringBuilder r = new StringBuilder();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 1400;
            t.Tick += delegate
            {
                t.Stop();
                GrabAnyForm("_videopro.png", "视频进阶", r);
                File.WriteAllText(Path.Combine(BASE_DIR, "_proshot.txt"), r.ToString(), Encoding.UTF8);
                Application.Exit();
            };
            t.Start();
            ShowVideoPro();
        }

        bool EnsureFfmpeg()
        {
            string ff = Path.Combine(VideoToolDir(), "ffmpeg.exe");
            if (File.Exists(ff)) return true;
            // v17.9：同目录的 ffmpeg_tool.zip 先本地解压（几秒完成、不耗流量），失败再走在线镜像
            if (UnpackResource("FfmpegZip", VideoToolDir(), "FFmpeg") && File.Exists(ff))
            {
                lblVTip.Text = "FFmpeg 已由本地组件包部署完成，高清视频可正常合并下载。";
                lblVTip.ForeColor = C_ON;
                return true;
            }
            // 本机已装有 FFmpeg → 直接复制使用
            string local = FindLocalFfmpeg();
            if (local != null)
            {
                try
                {
                    File.Copy(local, ff, true);
                    lblVTip.Text = "已使用本机检测到的 FFmpeg，高清视频可正常合并下载。";
                    lblVTip.ForeColor = C_ON;
                    return true;
                }
                catch (Exception dg13160_1) { Diag.Swallow("MainForm.ToolPages.cs:2106", dg13160_1); }
            }
            string[] mirrors = new string[] {
                "https://ghfast.top/",
                "https://gh-proxy.com/",
                "",                                  // 直连 GitHub（有代理时通常最快）
                "https://ghproxy.cn/",
                "https://ghproxy.net/",
                "https://mirror.ghproxy.com/",
                "https://ghps.cc/",
                "https://github.moeyy.xyz/"
            };
            string rel = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip";
            string zipPath = Path.Combine(VideoToolDir(), "ffmpeg_tmp.zip");
            string exDir = Path.Combine(VideoToolDir(), "ffx");
            bool anyErr = false;
            for (int i = 0; i < mirrors.Length; i++)
            {
                string url = mirrors[i] + rel;
                lblVTip.Text = "正在下载 FFmpeg 合并工具（约90MB）… 镜像 " + (i + 1) + "/" + mirrors.Length;
                lblVTip.ForeColor = UiKit.A(240, 160, 90);
                Application.DoEvents();
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 40000;
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream src = resp.GetResponseStream())
                    using (FileStream dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[65536];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                    }
                    if (Directory.Exists(exDir)) Directory.Delete(exDir, true);
                    Directory.CreateDirectory(exDir);
                    ZipFile.ExtractToDirectory(zipPath, exDir);
                    string found = FindFile(exDir, "ffmpeg.exe");
                    if (found != null) File.Copy(found, ff, true);
                    try { File.Delete(zipPath); } catch (Exception dg13201_1) { Diag.Swallow("MainForm.ToolPages.cs:2147", dg13201_1); }
                    try { Directory.Delete(exDir, true); } catch (Exception dg13202_1) { Diag.Swallow("MainForm.ToolPages.cs:2148", dg13202_1); }
                    if (File.Exists(ff))
                    {
                        lblVTip.Text = "FFmpeg 已就绪，高清视频可正常合并下载。";
                        lblVTip.ForeColor = C_ON;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    anyErr = true;
                    try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] FFmpeg 镜像" + (i + 1) + " 失败：" + url + " => " + ex.Message + "\r\n"); } catch (Exception dg13213_1) { Diag.Swallow("MainForm.ToolPages.cs:2159", dg13213_1); }
                }
            }
            if (anyErr)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] FFmpeg 全部镜像下载失败\r\n"); } catch (Exception dg13218_1) { Diag.Swallow("MainForm.ToolPages.cs:2164", dg13218_1); }
            }
            lblVTip.Text = "FFmpeg 下载失败，可点击「开始下载」重试；或手动放入 ffmpeg.exe。";
            lblVTip.ForeColor = UiKit.A(230, 120, 110);
            return false;
        }

        string FindLocalFfmpeg()
        {
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg\\bin\\ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop\\apps\\ffmpeg\\current\\bin\\ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WinGet\\Links\\ffmpeg.exe")
            };
            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch (Exception dg13237_1) { Diag.Swallow("MainForm.ToolPages.cs:2183", dg13237_1); }
            }
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string p in pathEnv.Split(';'))
            {
                try
                {
                    string f = Path.Combine(p.Trim().Trim('"'), "ffmpeg.exe");
                    if (File.Exists(f)) return f;
                }
                catch (Exception dg13247_1) { Diag.Swallow("MainForm.ToolPages.cs:2193", dg13247_1); }
            }
            return null;
        }

        DialogResult ShowFfmpegFailDialog()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "视频下载";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(480, 262);
                dlg.BackColor = C_BG;
                dlg.Font = new Font("Microsoft YaHei UI", 9F);
                Label icon = new Label();
                icon.Text = "⚠";
                icon.ForeColor = UiKit.A(240, 160, 60);
                icon.Font = new Font("Microsoft YaHei UI", 30F);
                icon.Location = new Point(18, 22);
                icon.AutoSize = true;
                Label msg = new Label();
                msg.Text = "FFmpeg 自动下载失败（已尝试多个镜像源，可能是网络原因）。\n\n三种解决办法：\n① 将随软件提供的 ffmpeg.exe 复制到程序目录的\n   video_tool 文件夹；\n② 点「打开下载页面」用浏览器搜索下载后放入；\n③ 稍后点「重试」再次自动下载。";
                msg.ForeColor = C_TEXT;
                msg.Location = new Point(82, 16);
                msg.Size = new Size(384, 158);
                Button b1 = new Button();
                b1.Text = "打开下载页面";
                b1.Width = 130; b1.Height = 36;
                b1.Location = new Point(82, 200);
                b1.Click += (s, e) => { try { Process.Start("https://www.baidu.com/s?wd=ffmpeg%207.1%20essentials_build%20zip%20%E4%B8%8B%E8%BD%BD"); } catch (Exception dg13279_1) { Diag.Swallow("MainForm.ToolPages.cs:2225", dg13279_1); } };
                Button b2 = new Button();
                b2.Text = "重试";
                b2.Width = 86; b2.Height = 36;
                b2.Location = new Point(230, 200);
                b2.DialogResult = DialogResult.Retry;
                Button b3 = new Button();
                b3.Text = "取消";
                b3.Width = 86; b3.Height = 36;
                b3.Location = new Point(326, 200);
                b3.DialogResult = DialogResult.Cancel;
                foreach (Button b in new Button[] { b1, b2, b3 })
                {
                    b.FlatStyle = FlatStyle.Standard;
                    b.FlatAppearance.BorderColor = C_BORDER;
                    b.BackColor = C_MAIN;
                    b.ForeColor = C_TEXT;
                }
                dlg.Controls.Add(icon);
                dlg.Controls.Add(msg);
                dlg.Controls.Add(b1);
                dlg.Controls.Add(b2);
                dlg.Controls.Add(b3);
                return dlg.ShowDialog(this);
            }
        }

        string FindFile(string dir, string name)
        {
            foreach (string f in Directory.GetFiles(dir, name, SearchOption.AllDirectories)) return f;
            return null;
        }

        // 平台切换：选中的用品牌蓝，未选中用深色气泡色
        void SetPlat(int p)
        {
            mPlat = p;
            if (btnPlat0 == null) return;
            Color on = UiKit.A(59, 130, 246);
            Color off = C_BUBBLE_ALT();
            btnPlat0.BackColor = p == 0 ? on : off;
            btnPlat1.BackColor = p == 1 ? on : off;
            btnPlat2.BackColor = p == 2 ? on : off;
            btnPlat0.ForeColor = p == 0 ? UiKit.Txt(Color.White) : UiKit.A(200, 206, 216);
            btnPlat1.ForeColor = p == 1 ? UiKit.Txt(Color.White) : UiKit.A(200, 206, 216);
            btnPlat2.ForeColor = p == 2 ? UiKit.Txt(Color.White) : UiKit.A(200, 206, 216);
        }

        // ---------- 音乐搜索与下载 ----------
        void MusicSearch()
        {
            if (mBusy) return;
            string kw = txtMSearch.Text.Trim();
            if (kw.Length == 0) { MessageBox.Show(this, "请输入歌名或歌手。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            int plat = mPlat;
            mBusy = true;
            btnMSearch.Text = "搜索中…";
            btnMSearch.Enabled = false;
            lvSongs.Items.Clear();
            mSongs.Clear();
            Thread t = new Thread(() =>
            {
                List<object[]> all = new List<object[]>();
                object lockObj = new object();
                StringBuilder errSb = new StringBuilder();
                // 两个音源并发搜，谁先回谁先入列；每个请求都有超时+一次重试，不会卡住不返回
                List<Thread> ts = new List<Thread>();
                if (plat == 0 || plat == 2)
                {
                    Thread tn = new Thread(() =>
                    {
                        try
                        {
                            List<object[]> r = SearchNetease(kw);
                            lock (lockObj) all.AddRange(r);
                        }
                        catch (Exception ex) { lock (lockObj) errSb.Append("网易云：" + ex.Message + " "); }
                    });
                    tn.IsBackground = true; ts.Add(tn);
                }
                if (plat == 1 || plat == 2)
                {
                    Thread tk = new Thread(() =>
                    {
                        try
                        {
                            List<object[]> r = SearchKugou(kw);
                            lock (lockObj) all.AddRange(r);
                        }
                        catch (Exception ex) { lock (lockObj) errSb.Append("酷狗：" + ex.Message + " "); }
                    });
                    tk.IsBackground = true; ts.Add(tk);
                }
                for (int i = 0; i < ts.Count; i++) ts[i].Start();
                for (int i = 0; i < ts.Count; i++) { try { ts[i].Join(20000); } catch (Exception dg13373_1) { Diag.Swallow("MainForm.ToolPages.cs:2319", dg13373_1); } }
                string err = errSb.ToString();
                try
                {
                    List<object[]> snapshot = all;
                    int cnt = snapshot.Count;
                    string errTxt = err;
                    this.Invoke((Action)(() =>
                    {
                        lvSongs.BeginUpdate();
                        lvSongs.Items.Clear();
                        mSongs.Clear();
                        foreach (object[] s in snapshot)
                        {
                            mSongs.Add(s);
                            ListViewItem li = lvSongs.Items.Add(Convert.ToString(s[1]));
                            li.SubItems.Add(Convert.ToString(s[2]));
                            li.SubItems.Add(Convert.ToString(s[3]));
                            li.SubItems.Add(Convert.ToString(s[4]));
                            li.SubItems.Add(Convert.ToString(s[5]));
                        }
                        lvSongs.EndUpdate();
                        if (cnt == 0) lblMStatus.Text = "未找到相关歌曲。" + errTxt;
                        else lblMStatus.Text = "找到 " + cnt + " 首（网易云可直接下载，酷狗结果双击打开网页）" + (errTxt.Length > 0 ? " · " + errTxt : "");
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() => lblMStatus.Text = "搜索失败：" + ex.Message));
                }
                finally { this.Invoke((Action)(() => { mBusy = false; btnMSearch.Text = "搜索"; btnMSearch.Enabled = true; })); }
            });
            t.IsBackground = true;
            t.Start();
        }

        List<object[]> SearchNetease(string kw)
        {
            string api = "https://music.163.com/api/search/get/web?s=" + Uri.EscapeDataString(kw) + "&type=1&limit=30&offset=0";
            string json = HttpGetText(api, "https://music.163.com/", 12000, 1);
            List<object[]> raw = ParseNeteaseSongs(json);
            List<object[]> outp = new List<object[]>();
            for (int i = 0; i < raw.Count; i++)
            {
                object[] s = raw[i];
                outp.Add(new object[] { s[0], s[1], s[2], s[3], s[4], "网易云", "" });
            }
            return outp;
        }

        // 酷狗：公开搜索接口能拿到歌名/歌手/专辑/时长，音频直链需要会员登录
        List<object[]> SearchKugou(string kw)
        {
            List<object[]> list = new List<object[]>();
            string api = "http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword=" + Uri.EscapeDataString(kw) + "&page=1&pagesize=30";
            string json = HttpGetText(api, null, 12000, 1);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("data")) return list;
            Dictionary<string, object> data = root["data"] as Dictionary<string, object>;
            if (data == null || !data.ContainsKey("info")) return list;
            object[] arr = ToArr(data["info"]);
            if (arr == null) return list;
            foreach (object o in arr)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                string name = d.ContainsKey("songname") ? Convert.ToString(d["songname"]) : "";
                string artist = d.ContainsKey("singername") ? Convert.ToString(d["singername"]) : "";
                string album = d.ContainsKey("albumname") ? Convert.ToString(d["albumname"]) : "";
                long sec = 0;
                if (d.ContainsKey("duration")) long.TryParse(Convert.ToString(d["duration"]), out sec);
                string dur = (sec / 60) + ":" + (sec % 60).ToString("00");
                string page = "https://www.kugou.com/yy/html/search.html#searchType=song&searchKeyWord=" + Uri.EscapeDataString(name + " " + artist);
                list.Add(new object[] { 0L, name, artist, album, dur, "酷狗", page });
            }
            return list;
        }

        List<object[]> ParseNeteaseSongs(string json)
        {
            List<object[]> list = new List<object[]>();
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
                if (root == null || !root.ContainsKey("result")) return list;
                Dictionary<string, object> res = root["result"] as Dictionary<string, object>;
                if (res == null || !res.ContainsKey("songs")) return list;
                object[] arr = ToArr(res["songs"]);
                if (arr == null) return list;
                foreach (object o in arr)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    long id = 0;
                    object ido;
                    if (d.TryGetValue("id", out ido) && ido != null) long.TryParse(Convert.ToString(ido), out id);
                    string name = d.ContainsKey("name") ? Convert.ToString(d["name"]) : "";
                    string artist = "", album = "", dur = "";
                    object ao;
                    if (d.TryGetValue("artists", out ao))
                    {
                        object[] ar = ToArr(ao);
                        if (ar != null && ar.Length > 0 && ar[0] is Dictionary<string, object>)
                        {
                            Dictionary<string, object> a0 = (Dictionary<string, object>)ar[0];
                            if (a0.ContainsKey("name")) artist = Convert.ToString(a0["name"]);
                        }
                    }
                    object alo;
                    if (d.TryGetValue("album", out alo) && alo is Dictionary<string, object>)
                    {
                        Dictionary<string, object> al = (Dictionary<string, object>)alo;
                        if (al.ContainsKey("name")) album = Convert.ToString(al["name"]);
                    }
                    object duo;
                    if (d.TryGetValue("duration", out duo) && duo != null)
                    {
                        long ms = 0; long.TryParse(Convert.ToString(duo), out ms);
                        dur = (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00");
                    }
                    list.Add(new object[] { id, name, artist, album, dur });
                }
            }
            catch (Exception dg13500_1) { Diag.Swallow("MainForm.ToolPages.cs:2446", dg13500_1); }
            return list;
        }

        // 单曲串行下载（给批量用，不占用 MusicDownloadByUrl 的进度条逻辑）
        bool MusicDlOne(string url, string dir)
        {
            try
            {
                string args = "--no-check-certificate --newline -f standard -o \"" + Path.Combine(dir, "%(title)s.%(ext)s") + "\" --no-progress \"" + url + "\"";
                ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(600000);
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        void MusicDownloadAll()
        {
            if (mBusy) return;
            if (mSongs == null || mSongs.Count == 0)
            {
                MessageBox.Show(this, "当前没有搜索结果，请先搜索。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!EnsureVideoTool()) return;
            int cnt = mSongs.Count;
            string dir = Path.Combine(vOutDir, "音乐");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            mBusy = true;
            pbMusic.Visible = true;
            pbMusic.Value = 0;
            btnMDl.Enabled = false;
            btnMUrl.Enabled = false;
            Thread t = new Thread(delegate()
            {
                int ok = 0, fail = 0;
                for (int i = 0; i < cnt; i++)
                {
                    object[] s = mSongs[i];
                    string plat = s.Length > 5 ? Convert.ToString(s[5]) : "网易云";
                    long id = 0;
                    try { id = Convert.ToInt64(s[0]); } catch (Exception dg13547_1) { Diag.Swallow("MainForm.ToolPages.cs:2493", dg13547_1); }
                    if (plat != "网易云" || id <= 0) { fail++; continue; }
                    string name = (string)s[1];
                    int cur = i;
                    this.BeginInvoke((Action)delegate
                    {
                        lblMStatus.Text = T("批量下载 ", "Downloading ") + (cur + 1) + " / " + cnt + "：" + name;
                        pbMusic.Value = (cur * 100) / cnt;
                    });
                    if (MusicDlOne("https://music.163.com/song?id=" + id, dir)) ok++; else fail++;
                }
                this.BeginInvoke((Action)delegate
                {
                    pbMusic.Value = 100;
                    lblMStatus.Text = T("批量完成：成功 ", "Done: ok ") + ok + T(" 首，失败 ", " / failed ") + fail + T(" 首", "");
                    mBusy = false;
                    btnMDl.Enabled = true;
                    btnMUrl.Enabled = true;
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void MusicDownloadSelected()
        {
            if (mBusy) return;
            if (lvSongs.SelectedIndices.Count == 0) { MessageBox.Show(this, "请先在列表中选择一首歌曲。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            int idx = lvSongs.SelectedIndices[0];
            if (idx < 0 || idx >= mSongs.Count) return;
            object[] s = mSongs[idx];
            string plat = s.Length > 5 ? Convert.ToString(s[5]) : "网易云";
            string other = s.Length > 6 ? Convert.ToString(s[6]) : "";
            long sid = 0;
            try { sid = Convert.ToInt64(s[0]); } catch (Exception dg13581_1) { Diag.Swallow("MainForm.ToolPages.cs:2527", dg13581_1); }
            if (plat != "网易云" || sid <= 0)
            {
                if (other.Length > 0)
                {
                    try { Process.Start(other); } catch (Exception dg13586_1) { Diag.Swallow("MainForm.ToolPages.cs:2532", dg13586_1); }
                    lblMStatus.Text = plat + " 的歌需要在网页里下载（要登录/会员），已为你打开对应页面。";
                }
                else lblMStatus.Text = plat + " 暂不支持直接下载。";
                return;
            }
            string name = (string)s[1];
            MusicDownloadByUrl("https://music.163.com/song?id=" + sid, name);
        }

        void MusicByUrl()
        {
            if (mBusy) return;
            string url = txtMSearch.Text.Trim();
            if (url.Length == 0 || !url.StartsWith("http"))
            {
                MessageBox.Show(this, "请在搜索框内粘贴音乐链接（网易云 / QQ音乐 等单曲链接）。", "音乐下载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MusicDownloadByUrl(url, url);
        }

        void MusicDownloadByUrl(string url, string label)
        {
            if (!EnsureVideoTool()) return;
            mBusy = true;
            string dir = Path.Combine(vOutDir, "音乐");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            pbMusic.Visible = true;
            pbMusic.Value = 0;
            lblMStatus.Text = "正在解析链接并下载…";
            Thread t = new Thread(() =>
            {
                try
                {
                    string args = "--no-check-certificate --newline -f standard -o \"" + Path.Combine(dir, "%(title)s.%(ext)s") + "\" --progress-template \"DP|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s\" \"" + url + "\"";
                    ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), args);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                    Process p = Process.Start(psi);
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        if (line.StartsWith("DP|"))
                        {
                            string[] parts = line.Split('|');
                            string pct = parts.Length > 1 ? parts[1].Trim() : "";
                            string spd = parts.Length > 2 ? parts[2].Trim() : "";
                            string eta = parts.Length > 3 ? parts[3].Trim() : "";
                            int pv = 0;
                            float pf;
                            if (float.TryParse(pct.TrimEnd('%'), out pf)) pv = (int)pf;
                            string msg = "正在下载：" + pct;
                            if (spd.Length > 0) msg += "  " + spd;
                            if (eta.Length > 0) msg += "  剩余 " + eta;
                            this.Invoke((Action)(() => { pbMusic.Value = Math.Min(100, Math.Max(0, pv)); lblMStatus.Text = msg; }));
                        }
                        else if (line.StartsWith("ERROR"))
                        {
                            this.Invoke((Action)(() => lblMStatus.Text = "下载失败：" + line));
                        }
                    }
                    p.WaitForExit();
                    this.Invoke((Action)(() =>
                    {
                        if (p.ExitCode == 0) { lblMStatus.Text = "下载完成，已保存到「下载\\音乐」文件夹。"; Sfx.Done(); }
                        else if (!lblMStatus.Text.StartsWith("下载失败")) lblMStatus.Text = "下载未完成（可能链接受限）。";
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() => lblMStatus.Text = "下载异常：" + ex.Message));
                }
                finally { this.Invoke((Action)(() => { mBusy = false; pbMusic.Visible = false; })); }
            });
            t.IsBackground = true;
            t.Start();
        }

        // 组件包文件名（用于「同目录 zip」与「在线下载」两条非内嵌路径）
        static string ComponentZipName(string resName)
        {
            switch (resName)
            {
                case "VideoZip": return "video_tool.zip";
                case "SamZip": return "sam_tool.zip";
                case "CleanZip": return "clean_tool.zip";
                case "FfmpegZip": return "ffmpeg_tool.zip";
            }
            return "";
        }

        // 组件在线下载根地址：小包走旧 Release，大包走 Pages（国内直连更快）
        static readonly string COMPONENT_BASE = "https://github.com/wj5666619/MiniToolbox/releases/download/6.5/";
        static readonly string COMPONENT_BASE_PAGES = "https://wj5666619.github.io/MiniToolbox/";

        // 解压到目标目录（先落临时目录再拷贝，避免目标目录非空导致解压失败）
        void ExtractZipSafe(string zipPath, string targetDir)
        {
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            string stage = Path.Combine(Path.GetTempPath(), "mtb_unpack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, stage);
                string root = stage.TrimEnd('\\');
                foreach (string f in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(root.Length + 1);
                    string dst = Path.Combine(targetDir, rel);
                    string dd = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dd) && !Directory.Exists(dd)) Directory.CreateDirectory(dd);
                    try { File.Copy(f, dst, true); } catch (Exception dg13701_1) { Diag.Swallow("MainForm.ToolPages.cs:2647", dg13701_1); }
                }
            }
            finally { try { Directory.Delete(stage, true); } catch (Exception dg13704_1) { Diag.Swallow("MainForm.ToolPages.cs:2650", dg13704_1); } }
        }

        // 部署组件：内嵌资源 → 同目录 zip → 在线下载（精简版本靠后两条）
        bool UnpackResource(string resName, string targetDir, string display)
        {
            try
            {
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                Assembly asm = Assembly.GetExecutingAssembly();
                Stream rs = null;
                try { rs = asm.GetManifestResourceStream(resName); } catch (Exception dg13715_1) { Diag.Swallow("MainForm.ToolPages.cs:2661", dg13715_1); }
                if (rs != null)
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "pkg_" + Guid.NewGuid().ToString("N") + ".zip");
                    using (rs)
                    using (FileStream fs = new FileStream(tmp, FileMode.Create))
                        rs.CopyTo(fs);
                    ExtractZipSafe(tmp, targetDir);
                    try { File.Delete(tmp); } catch (Exception dg13723_1) { Diag.Swallow("MainForm.ToolPages.cs:2669", dg13723_1); }
                    Log("[部署] " + display + " 组件已从内嵌资源部署到 " + targetDir + " " + DateTime.Now.ToString("HH:mm:ss"));
                    return true;
                }

                string zipName = ComponentZipName(resName);
                if (zipName.Length == 0) return false;

                // 1) 安装器铺在 exe 同目录的组件包
                string localZip = Path.Combine(BASE_DIR, zipName);
                if (!File.Exists(localZip))
                {
                    string sub = Path.Combine(BASE_DIR, "tools");
                    string alt = Path.Combine(sub, zipName);
                    if (File.Exists(alt)) localZip = alt;
                }
                if (File.Exists(localZip))
                {
                    ExtractZipSafe(localZip, targetDir);
                    Log("[部署] " + display + " 组件由本地包解压（" + zipName + "）" + DateTime.Now.ToString("HH:mm:ss"));
                    return true;
                }

                // 2) 在线下载（大组件优先 Pages，避免 Release 通道在部分网络下过慢）
                string url = (zipName == "video_tool.zip" || zipName == "ffmpeg_tool.zip")
                    ? COMPONENT_BASE_PAGES + zipName
                    : COMPONENT_BASE + zipName;
                string downloaded = Path.Combine(Path.GetTempPath(), "mtb_dl_" + Guid.NewGuid().ToString("N") + ".zip");
                bool got = DownloadComponent(url, downloaded, display);
                if (!got || !File.Exists(downloaded))
                {
                    try { File.Delete(downloaded); } catch (Exception dg13752_1) { Diag.Swallow("MainForm.ToolPages.cs:2698", dg13752_1); }
                    File.AppendAllText(Path.Combine(BASE_DIR, "error.log"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 组件下载失败 " + display + "（" + url + "）\r\n");
                    return false;
                }
                long expect = ComponentSize(resName);
                long real = new FileInfo(downloaded).Length;
                if (expect > 0 && real != expect)
                {
                    try { File.Delete(downloaded); } catch (Exception dg13761_1) { Diag.Swallow("MainForm.ToolPages.cs:2707", dg13761_1); }
                    File.AppendAllText(Path.Combine(BASE_DIR, "error.log"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 组件大小不符 " + display + " 期望 " + expect + " 实际 " + real + "\r\n");
                    return false;
                }
                ExtractZipSafe(downloaded, targetDir);
                try { File.Delete(downloaded); } catch (Exception dg13767_1) { Diag.Swallow("MainForm.ToolPages.cs:2713", dg13767_1); }
                Log("[部署] " + display + " 组件已在线下载并部署到 " + targetDir + " " + DateTime.Now.ToString("HH:mm:ss"));
                return true;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 组件部署失败 " + display + "：" + ex + "\r\n"); } catch (Exception dg13774_1) { Diag.Swallow("MainForm.ToolPages.cs:2720", dg13774_1); }
                return false;
            }
        }

        // 组件包字节数（用于下载后校验，填 0 表示不校验）
        static long ComponentSize(string resName)
        {
            switch (resName)
            {
                case "VideoZip": return 17620273;
                case "SamZip": return 353069;
                case "CleanZip": return 4860;
                case "FfmpegZip": return 29958432;
            }
            return 0;
        }

        // 带暗色进度框的组件下载
        bool DownloadComponent(string url, string savePath, string display)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(0xF00 | 0xC00); } catch (Exception dg13794_1) { Diag.Swallow("MainForm.ToolPages.cs:2740", dg13794_1); }
            bool ok = false;
            string err = "";
            string tmpPart = savePath + ".part";
            try { File.Delete(tmpPart); } catch (Exception dg13798_1) { Diag.Swallow("MainForm.ToolPages.cs:2744", dg13798_1); }
            // 卡死保护：连上却一直没数据（服务器挂起/断流）时自动取消，不留一个永远的「正在下载」
            WebClient wcRef = null;
            long lastBytes = -1;
            DateTime lastMove = DateTime.Now;
            using (var pf = new CompProgressForm(display))
            {
                pf.Shown += delegate
                {
                    try
                    {
                        WebClient wc = new WebClient();
                        wcRef = wc;
                        wc.Headers.Add("User-Agent", "MiniToolbox/1.0");
                        wc.DownloadProgressChanged += delegate(object s2, DownloadProgressChangedEventArgs e2)
                        {
                            if (e2.BytesReceived != lastBytes) { lastBytes = e2.BytesReceived; lastMove = DateTime.Now; }
                            pf.SetProgress(e2.BytesReceived, e2.TotalBytesToReceive);
                        };
                        wc.DownloadFileCompleted += delegate(object s2, System.ComponentModel.AsyncCompletedEventArgs e2)
                        {
                            if (e2.Error != null) err = e2.Error.Message;
                            else if (e2.Cancelled) { if (err.Length == 0) err = "已取消"; }
                            else ok = true;
                            pf.CloseSafe();
                        };
                        wc.DownloadFileAsync(new Uri(url), tmpPart);
                        Thread wd = new Thread(delegate()
                        {
                            while (wcRef != null)
                            {
                                Thread.Sleep(5000);
                                if ((DateTime.Now - lastMove).TotalSeconds > 60)
                                {
                                    try { err = "下载中断（60 秒无数据，已自动取消）"; wcRef.CancelAsync(); } catch (Exception dg13832_1) { Diag.Swallow("MainForm.ToolPages.cs:2778", dg13832_1); }
                                    break;
                                }
                                if (pf.IsDisposed) break;
                            }
                        });
                        wd.IsBackground = true;
                        wd.Start();
                    }
                    catch (Exception ex) { err = ex.Message; pf.CloseSafe(); }
                };
                pf.ShowDialog(this);
            }
            if (ok && File.Exists(tmpPart))
            {
                try
                {
                    if (File.Exists(savePath)) File.Delete(savePath);
                    File.Move(tmpPart, savePath);
                }
                catch (Exception ex) { err = ex.Message; ok = false; }
            }
            else
            {
                try { File.Delete(tmpPart); } catch (Exception dg13856_1) { Diag.Swallow("MainForm.ToolPages.cs:2802", dg13856_1); }
            }
            if (!ok && err.Length > 0)
            {
                Log("[部署] " + display + " 组件下载失败：" + err);
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 下载 " + display + " 失败：" + err + "\r\n"); } catch (Exception dg13862_1) { Diag.Swallow("MainForm.ToolPages.cs:2808", dg13862_1); }
            }
            return ok;
        }

    }
}