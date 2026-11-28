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
    class SysHudForm : Form
    {
        Label lbl = new Label();
        System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
        bool paused = false;
        long lastRx = -1, lastTx = -1;
        ulong lastIdle = 0, lastKernel = 0, lastUser = 0;
        DateTime lastAt = DateTime.MinValue;
        double cpu = 0;
        public Action OnOpenBoard;

        [StructLayout(LayoutKind.Sequential)]
        struct MEMSTAT
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMSTAT lpBuffer);
        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("user32.dll")]
        static extern IntPtr ReleaseCapture();
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public SysHudForm(bool langEn)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = UiKit.A(22, 27, 34);
            ClientSize = new Size(272, 44);
            Opacity = 0.93;
            StartPosition = FormStartPosition.Manual;
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 12, wa.Top + 12);
            }
            catch (Exception dg71_1) { Diag.Swallow("SysHudForm.cs:71", dg71_1); }

            lbl.Dock = DockStyle.Fill;
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.ForeColor = UiKit.A(214, 226, 240);
            lbl.BackColor = Color.Transparent;
            lbl.Font = new Font("Consolas", 8.5F);
            lbl.Text = langEn ? "loading..." : "读取中…";
            Controls.Add(lbl);

            Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiKit.A(60, 74, 92)))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };

            MouseEventHandler drag = delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            MouseDown += drag;
            lbl.MouseDown += drag;
            DoubleClick += delegate { try { if (OnOpenBoard != null) OnOpenBoard(); } catch (Exception dg97_1) { Diag.Swallow("SysHudForm.cs:97", dg97_1); } };
            lbl.DoubleClick += delegate { try { if (OnOpenBoard != null) OnOpenBoard(); } catch (Exception dg98_1) { Diag.Swallow("SysHudForm.cs:98", dg98_1); } };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(langEn ? "Pause" : "暂停刷新", null, delegate { paused = true; });
            menu.Items.Add(langEn ? "Resume" : "继续刷新", null, delegate { paused = false; });
            menu.Items.Add(langEn ? "Stick to left" : "贴到左侧", null, delegate
            {
                try
                {
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    Location = new Point(wa.Left + 12, wa.Top + 12);
                }
                catch (Exception dg110_1) { Diag.Swallow("SysHudForm.cs:110", dg110_1); }
            });
            menu.Items.Add(langEn ? "Open dashboard" : "打开看板", null, delegate { try { if (OnOpenBoard != null) OnOpenBoard(); } catch (Exception dg112_1) { Diag.Swallow("SysHudForm.cs:112", dg112_1); } });
            menu.Items.Add(langEn ? "Hide" : "隐藏悬浮条", null, delegate { Hide(); });
            ContextMenuStrip = menu;

            tm.Interval = 1500;
            tm.Tick += delegate { Refresh2(); };
            tm.Start();
            Refresh2();
        }

        public void StopTimer()
        {
            try { tm.Stop(); tm.Dispose(); } catch (Exception dg124_1) { Diag.Swallow("SysHudForm.cs:124", dg124_1); }
        }

        public string DumpLine()
        {
            return lbl.Text.Replace("\r", "").Replace("\n", " | ");
        }

        void Refresh2()
        {
            if (paused) return;
            try
            {
                ulong idle = 0, kernel = 0, user = 0;
                if (GetSystemTimes(out idle, out kernel, out user) && lastKernel > 0)
                {
                    ulong dIdle = idle - lastIdle;
                    ulong dKernel = kernel - lastKernel;
                    ulong dUser = user - lastUser;
                    ulong dTotal = dKernel + dUser;
                    if (dTotal > 0)
                    {
                        double busy = (double)(dTotal - dIdle) / (double)dTotal * 100.0;
                        if (busy < 0) busy = 0;
                        if (busy > 100) busy = 100;
                        cpu = cpu * 0.6 + busy * 0.4;
                    }
                }
                lastIdle = idle; lastKernel = kernel; lastUser = user;

                MEMSTAT ms = new MEMSTAT();
                ms.dwLength = (uint)Marshal.SizeOf(typeof(MEMSTAT));
                double memPct = 0;
                double usedGB = 0, totalGB = 0;
                if (GlobalMemoryStatusEx(ref ms))
                {
                    memPct = ms.dwMemoryLoad;
                    usedGB = (double)(ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
                    totalGB = (double)ms.ullTotalPhys / 1073741824.0;
                }

                long rx = 0, tx = 0;
                try
                {
                    System.Net.NetworkInformation.NetworkInterface[] nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                    for (int i = 0; i < nics.Length; i++)
                    {
                        if (nics[i].OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        try
                        {
                            System.Net.NetworkInformation.IPv4InterfaceStatistics st = nics[i].GetIPv4Statistics();
                            rx += st.BytesReceived;
                            tx += st.BytesSent;
                        }
                        catch (Exception dg178_1) { Diag.Swallow("SysHudForm.cs:178", dg178_1); }
                    }
                }
                catch (Exception dg181_1) { Diag.Swallow("SysHudForm.cs:181", dg181_1); }

                double sec = 1.5;
                if (lastAt != DateTime.MinValue) sec = (DateTime.Now - lastAt).TotalSeconds;
                if (sec <= 0) sec = 1.5;
                double rxKb = lastRx < 0 ? 0 : (rx - lastRx) / 1024.0 / sec;
                double txKb = lastTx < 0 ? 0 : (tx - lastTx) / 1024.0 / sec;
                if (rxKb < 0) rxKb = 0;
                if (txKb < 0) txKb = 0;
                lastRx = rx; lastTx = tx; lastAt = DateTime.Now;

                lbl.Text = "CPU " + Bar(cpu) + " " + ((int)cpu).ToString() + "%  MEM " + ((int)memPct).ToString()
                    + "% " + usedGB.ToString("0.0") + "/" + totalGB.ToString("0.0") + "G\r\n↓" + FmtKb(rxKb) + "  ↑" + FmtKb(txKb);
            }
            catch (Exception dg195_1) { Diag.Swallow("SysHudForm.cs:195", dg195_1); }
        }

        string Bar(double pct)
        {
            int n = (int)(pct / 100.0 * 6.0);
            if (n < 0) n = 0;
            if (n > 6) n = 6;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(i < n ? '#' : '.');
            return sb.ToString();
        }

        string FmtKb(double kb)
        {
            if (kb >= 1024) return (kb / 1024.0).ToString("0.0") + "M/s";
            return ((int)kb).ToString() + "K/s";
        }
    }
}