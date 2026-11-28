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
    class CondForm : Form
    {
        bool en;
        Func<string, bool> runner;
        System.Windows.Forms.Timer tickTimer;
        ComboBox cmbType, cmbAction;
        NumSpin numVal;
        TextBox txtProc;
         RoundCheck chkMute;
        Button btnStart, btnStop;
        Label lblHint, lblState;
        Panel pnlMeter;
        bool listening = false;
        long condSeconds = 0;
        string lastNote = "";

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        string T(string zh, string en2) { return Lang.T(zh, en2); }

        public CondForm(bool langEn, Func<string, bool> runAction)
        {
            en = langEn;
            try
            {
                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
                this.FormClosed += delegate
                {
                    try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; } catch (Exception dg54_1) { Diag.Swallow("CondForm.cs:54", dg54_1); }
                };
            }
            catch (Exception dg57_1) { Diag.Swallow("CondForm.cs:57", dg57_1); }
            runner = runAction;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            this.Text = langEn ? "Smart Triggers" : "智能条件触发";
            ClientSize = new Size(470, 452);
            BackColor = UiKit.A(30, 34, 41);
            ShowInTaskbar = false;

            Panel bar = new Panel();
            bar.BackColor = UiKit.A(26, 30, 37);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(470, 42);
            Controls.Add(bar);

            Label ti = new Label();
            ti.Text = T("智能条件触发", "Smart Triggers");
            ti.ForeColor = UiKit.A(230, 233, 238);
            ti.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            ti.Location = new Point(16, 10);
            ti.AutoSize = true;
            bar.Controls.Add(ti);

            Label x = new Label();
            x.Text = "✕";
            x.ForeColor = UiKit.A(150, 162, 180);
            x.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            x.Location = new Point(438, 8);
            x.Size = new Size(28, 26);
            x.TextAlign = ContentAlignment.MiddleCenter;
            x.Cursor = Cursors.Hand;
            x.Click += (s, e) => Close();
            bar.Controls.Add(x);

            bool drag = false; Point ds = Point.Empty;
            bar.MouseDown += (s, e) => { drag = true; ds = e.Location; };
            bar.MouseMove += (s, e) => { if (drag) Location = new Point(Location.X + e.X - ds.X, Location.Y + e.Y - ds.Y); };
            bar.MouseUp += (s, e) => { drag = false; };

            Label l1 = MkLabel(T("触发条件", "Trigger"), 16, 58);
            Controls.Add(l1);

            cmbType = new ComboBox();
            cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbType.BackColor = UiKit.A(27, 31, 38);
            cmbType.ForeColor = UiKit.A(230, 233, 238);
            cmbType.Font = new Font("Microsoft YaHei UI", 10F);
            cmbType.FlatStyle = FlatStyle.Standard;
            cmbType.Items.Add(T("电脑空闲超过 N 分钟", "Idle longer than N minutes"));
            cmbType.Items.Add(T("电量低于 N%", "Battery below N%"));
            cmbType.Items.Add(T("指定进程结束", "Process exited"));
            cmbType.Items.Add(T("CPU 连续高负载超过 N 分钟", "CPU busy for N minutes"));
            cmbType.Items.Add(T("显卡温度超过 N°C", "GPU temp above N°C"));
            cmbType.Items.Add(T("锁屏超过 N 分钟", "Locked for N minutes"));
            cmbType.Location = new Point(16, 82);
            cmbType.Size = new Size(438, 30);
            cmbType.SelectedIndex = 0;
            cmbType.SelectedIndexChanged += (s, e) => SyncTypeUI();
            cmbType.Visible = false;
            Controls.Add(cmbType);
            Controls.Add(MakeDrop(cmbType, 16, 82, 438, 30));

            Label l2 = MkLabel(T("阈值", "Threshold"), 16, 124);
            Controls.Add(l2);
            numVal = new NumSpin();
            numVal.Minimum = 1; numVal.Maximum = 720; numVal.Value = 10;
            numVal.BackColor = UiKit.A(27, 31, 38);
            numVal.ForeColor = UiKit.A(230, 233, 238);
            numVal.BorderStyle = BorderStyle.FixedSingle;
            numVal.Font = new Font("Consolas", 11F);
            numVal.Location = new Point(16, 148);
            numVal.Size = new Size(110, 28);
            Controls.Add(numVal);

            Label l3 = MkLabel(T("进程名（不含 .exe）", "Process name (no .exe)"), 146, 124);
            Controls.Add(l3);
            txtProc = new TextBox();
            txtProc.Text = "game";
            txtProc.BackColor = UiKit.A(27, 31, 38);
            txtProc.ForeColor = UiKit.A(230, 233, 238);
            txtProc.BorderStyle = BorderStyle.FixedSingle;
            txtProc.Font = new Font("Consolas", 11F);
            txtProc.Location = new Point(146, 148);
            txtProc.Size = new Size(308, 28);
            Controls.Add(txtProc);

            Label l4 = MkLabel(T("条件满足后执行", "Then run"), 16, 190);
            Controls.Add(l4);
            cmbAction = new ComboBox();
            cmbAction.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAction.BackColor = UiKit.A(27, 31, 38);
            cmbAction.ForeColor = UiKit.A(230, 233, 238);
            cmbAction.Font = new Font("Microsoft YaHei UI", 10F);
            cmbAction.FlatStyle = FlatStyle.Standard;
            cmbAction.Items.Add(T("关机", "Shut down"));
            cmbAction.Items.Add(T("重启", "Restart"));
            cmbAction.Items.Add(T("睡眠", "Sleep"));
            cmbAction.Items.Add(T("休眠", "Hibernate"));
            cmbAction.Items.Add(T("锁定", "Lock"));
            cmbAction.Items.Add(T("注销", "Sign out"));
            cmbAction.Items.Add(T("静音后睡眠", "Mute + Sleep"));
            cmbAction.Items.Add(T("关屏后锁定", "Screen off + Lock"));
            cmbAction.Location = new Point(16, 214);
            cmbAction.Size = new Size(438, 30);
            cmbAction.SelectedIndex = 0;
            cmbAction.Visible = false;
            Controls.Add(cmbAction);
            Controls.Add(MakeDrop(cmbAction, 16, 214, 438, 30));

            chkMute = new RoundCheck();
            chkMute.Text = T("执行前先静音", "Mute before running");
            chkMute.ForeColor = UiKit.A(176, 188, 206);
            chkMute.Font = new Font("Microsoft YaHei UI", 9F);
            chkMute.Location = new Point(16, 254);
            chkMute.AutoSize = true;
            chkMute.Checked = true;
            Controls.Add(chkMute);

            pnlMeter = new Panel();
            pnlMeter.BackColor = UiKit.A(24, 28, 35);
            pnlMeter.Location = new Point(16, 284);
            pnlMeter.Size = new Size(438, 74);
            pnlMeter.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rc = new Rectangle(0, 0, pnlMeter.Width - 1, pnlMeter.Height - 1);
                using (GraphicsPath p = UiKit.RoundedPath(rc, 10))
                {
                    e.Graphics.SetClip(p);
                    using (LinearGradientBrush br = new LinearGradientBrush(rc, UiKit.A(46, 54, 68), UiKit.A(31, 36, 46), 90F))
                        e.Graphics.FillRectangle(br, rc);
                    e.Graphics.ResetClip();
                    using (Pen pn = new Pen(UiKit.AA(46, 255, 255, 255), 1f)) e.Graphics.DrawPath(pn, p);
                }
            };
            Controls.Add(pnlMeter);

            lblState = new Label();
            lblState.Text = T("尚未开始监听", "Not listening");
            lblState.ForeColor = UiKit.A(150, 168, 190);
            lblState.Font = new Font("Microsoft YaHei UI", 9F);
            lblState.Location = new Point(12, 10);
            lblState.Size = new Size(414, 20);
            pnlMeter.Controls.Add(lblState);

            Label lblState2 = new Label();
            lblState2.Name = "lblState2";
            lblState2.Text = "";
            lblState2.ForeColor = UiKit.A(200, 214, 232);
            lblState2.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblState2.Location = new Point(12, 34);
            lblState2.Size = new Size(414, 24);
            pnlMeter.Controls.Add(lblState2);

            btnStart = MkBtn(T("开始监听", "Start watching"), UiKit.A(43, 95, 208), 16, 372, 210, 44);
            btnStart.Click += (s, e) => StartWatch();
            Controls.Add(btnStart);

            btnStop = MkBtn(T("停止", "Stop"), UiKit.A(58, 66, 80), 234, 372, 220, 44);
            btnStop.Enabled = false;
            btnStop.Click += (s, e) => StopWatch();
            Controls.Add(btnStop);

            lblHint = new Label();
            lblHint.Text = T("监听在后台每 3 秒检查一次；满足条件会直接执行所选动作，请先确认不会丢数据。",
                "Checks every 3s in background; the action runs immediately once the condition is met.");
            lblHint.ForeColor = UiKit.A(240, 160, 90);
            lblHint.Font = new Font("Microsoft YaHei UI", 8F);
            lblHint.Location = new Point(16, 424);
            lblHint.Size = new Size(438, 20);
            lblHint.AutoEllipsis = true;
            Controls.Add(lblHint);

            tickTimer = new System.Windows.Forms.Timer();
            tickTimer.Interval = 3000;
            tickTimer.Tick += (s, e) => TickCheck();

            Shown += (s, e) => { try { ActiveControl = null; } catch (Exception dg234_1) { Diag.Swallow("CondForm.cs:234", dg234_1); } };
            SyncTypeUI();
        }

        Label MkLabel(string t, int x, int y)
        {
            Label l = new Label();
            l.Text = t;
            l.ForeColor = UiKit.A(150, 165, 190);
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        Button MkBtn(string t, Color bg, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = t;
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = bg;
            b.ForeColor = UiKit.Txt(Color.White);
            b.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Cursor = Cursors.Hand;
            return b;
        }

        Button MakeDrop(ComboBox cb, int x, int y, int w, int h)
        {
            Button b = MkBtn(cb.Text + "   ▾", UiKit.A(27, 31, 38), x, y, w, h);
            b.Font = new Font("Microsoft YaHei UI", 9.5F);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(10, 0, 0, 0);
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = UiKit.A(58, 66, 82);
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.BackColor = UiKit.A(32, 36, 45);
            cms.ForeColor = UiKit.A(232, 236, 242);
            cms.Font = new Font("Microsoft YaHei UI", 9.5F);
            cms.Renderer = new DarkMenuRenderer();
            cms.ShowImageMargin = false;
            cms.Padding = new Padding(0, 4, 0, 4);
            foreach (object o in cb.Items)
            {
                string txt = o.ToString();
                cms.Items.Add(txt, null, (s2, e2) =>
                {
                    int idx = cb.Items.IndexOf(txt);
                    if (idx >= 0) cb.SelectedIndex = idx;
                    b.Text = cb.Text + "   ▾";
                });
            }
            b.Click += (s2, e2) => cms.Show(b, new Point(0, b.Height + 2));
            return b;
        }

        // 显卡温度读数（自包含，不依赖主窗口）
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlInit_v2();
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetCount_v2(ref uint c);
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetHandleByIndex_v2(uint i, ref IntPtr h);
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetTemperature(IntPtr h, uint s, ref uint v);
        // 锁屏状态（会话事件驱动，只改字段不碰 UI，无跨线程问题）
        DateTime lockAt = DateTime.MinValue;
        bool lockFired = false;

        void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            try
            {
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
                {
                    lockAt = DateTime.Now;
                    lockFired = false;
                }
                else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
                {
                    lockAt = DateTime.MinValue;
                    lockFired = false;
                }
            }
            catch (Exception dg321_1) { Diag.Swallow("CondForm.cs:321", dg321_1); }
        }

        bool nvInit = false;
        bool nvFail = false;
        IntPtr nvDev = IntPtr.Zero;

        int GpuTemp()
        {
            if (!nvInit && !nvFail)
            {
                try
                {
                    if (nvmlInit_v2() == 0)
                    {
                        uint c = 0;
                        if (nvmlDeviceGetCount_v2(ref c) == 0 && c > 0
                            && nvmlDeviceGetHandleByIndex_v2(0, ref nvDev) == 0) nvInit = true;
                    }
                    if (!nvInit) nvFail = true;
                }
                catch { nvFail = true; }
            }
            if (!nvInit) return -1;
            try
            {
                uint v = 0;
                if (nvmlDeviceGetTemperature(nvDev, 0, ref v) == 0) return (int)v;
            }
            catch (Exception dg350_1) { Diag.Swallow("CondForm.cs:350", dg350_1); }
            return -1;
        }

        void SyncTypeUI()
        {
            int t = cmbType.SelectedIndex;
            txtProc.Enabled = (t == 2);
            numVal.Enabled = (t != 2);
            if (t == 1) { numVal.Maximum = 100; numVal.Value = 20; }
            else if (t == 4) { numVal.Maximum = 100; numVal.Value = 80; }
            else if (t == 5) { numVal.Maximum = 720; numVal.Value = 30; }
            else if (t == 0 || t == 3) { numVal.Maximum = 720; if (numVal.Value < 1) numVal.Value = 10; }
        }

        void StartWatch()
        {
            if (cmbType.SelectedIndex == 2 && txtProc.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, T("请填写进程名。", "Enter a process name."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            listening = true;
            condSeconds = 0;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            cmbType.Enabled = false; numVal.Enabled = false; txtProc.Enabled = false; cmbAction.Enabled = false;
            tickTimer.Start();
            TickCheck();
        }

        void StopWatch()
        {
            listening = false;
            tickTimer.Stop();
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            cmbType.Enabled = true; numVal.Enabled = (cmbType.SelectedIndex != 2); cmbAction.Enabled = true;
            lblState.Text = T("已停止监听", "Stopped");
            SetBig(T("已停止", "Stopped"), UiKit.A(150, 168, 190));
        }

        void SetBig(string text, Color c)
        {
            Label big = null;
            foreach (Control cc in pnlMeter.Controls) if (cc.Name == "lblState2") big = (Label)cc;
            if (big != null) { big.Text = text; big.ForeColor = c; }
        }

        void TickCheck()
        {
            if (!listening) return;
            int type = cmbType.SelectedIndex;
            int need = (int)numVal.Value;
            string proc = txtProc.Text.Trim();
            bool fired = false;
            string note = "";

            try
            {
                if (type == 0 || type == 3)
                {
                    if (type == 0)
                    {
                        uint idle = IdleSeconds();
                        note = T("当前空闲 ", "idle ") + (idle / 60) + T(" 分 ", " min ") + (idle % 60) + T(" 秒，触发阈值 ", "s, need ") + need + T(" 分钟", " min");
                        if (idle >= need * 60) fired = true;
                        else condSeconds = idle;
                    }
                    else
                    {
                        double load = CpuLoad();
                        note = T("当前 CPU ", "cpu ") + load.ToString("0.0") + "%  " + T("（需连续高于 ", "(need ") + need + "% " + T("分钟）", "min)");
                        if (load >= need) { condSeconds += 3; }
                        else condSeconds = 0;
                        note += T("  已持续 ", "  held ") + condSeconds + T(" 秒", "s");
                        if (condSeconds >= need * 60) fired = true;
                    }
                }
                else if (type == 1)
                {
                    PowerStatus ps = SystemInformation.PowerStatus;
                    if (ps.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
                    {
                        note = T("本机没有电池，此条件不可用。", "No battery on this PC.");
                    }
                    else
                    {
                        int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                        note = T("当前电量 ", "battery ") + pct + "%，" + (ps.BatteryLifeRemaining > 0 ? T("剩余约 ", "about ") + (ps.BatteryLifeRemaining / 60) + T(" 分钟", " min") : T("正在充电或未知", "charging / unknown"));
                        if (pct <= need) fired = true;
                    }
                }
                else if (type == 5)
                {
                    if (lockAt == DateTime.MinValue) note = T("当前未锁屏", "not locked");
                    else
                    {
                        int mins = (int)((DateTime.Now - lockAt).TotalMinutes);
                        note = T("已锁屏 ", "locked ") + mins + T(" 分钟（阈值 ", " min (need ") + need + T(" 分钟）", " min)");
                        if (mins >= need && !lockFired) { lockFired = true; fired = true; }
                    }
                }
                else if (type == 4)
                {
                    int gt = GpuTemp();
                    if (gt < 0) note = T("未检测到 NVIDIA 显卡温度读数（NVML 不可用）", "No NVIDIA GPU temp (NVML unavailable)");
                    else
                    {
                        note = T("当前显卡 ", "gpu ") + gt + "°C  " + T("（阈值 ", "(need ") + need + "°C）";
                        if (gt >= need) fired = true;
                    }
                }
                else if (type == 2)
                {
                    int cnt = 0;
                    try { cnt = Process.GetProcessesByName(proc).Length; } catch { cnt = -1; }
                    note = cnt < 0 ? T("进程查询失败", "query failed") : (cnt > 0 ? T("「", "[") + proc + T("」正在运行（", "] running (") + cnt + T(" 个）", ")") : T("「", "[") + proc + T("」已退出", "] not running"));
                    if (cnt == 0) fired = true;
                }
            }
            catch (Exception dg471_1) { Diag.Swallow("CondForm.cs:471", dg471_1); }

            lblState.Text = T("监听中 · 每 3 秒检查", "Watching · every 3s");
            if (note != lastNote) { SetBig(note, UiKit.A(200, 214, 232)); lastNote = note; }

            if (fired)
            {
                tickTimer.Stop();
                listening = false;
                string act = cmbAction.SelectedIndex >= 0 ? cmbAction.Text : T("关机", "Shut down");
                if (en)
                {
                    string[] enAct = { "关机", "重启", "睡眠", "休眠", "锁定", "注销" };
                    if (cmbAction.SelectedIndex >= 0) act = enAct[cmbAction.SelectedIndex];
                }
                if (chkMute.Checked) { try { keybd_event(0xAD, 0, 0, 0); } catch (Exception dg486_1) { Diag.Swallow("CondForm.cs:486", dg486_1); } }
                SetBig(T("条件已满足，正在执行：", "Condition met, running: ") + act, UiKit.A(34, 197, 94));
                Application.DoEvents();
                bool ok = runner != null && runner(act);
                MessageBox.Show(this, (ok ? T("已执行：", "Done: ") : T("执行失败：", "Failed: ")) + act,
                    T("条件触发", "Triggers"), MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                Close();
            }
        }

        static uint IdleSeconds()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            uint now = (uint)Environment.TickCount;
            return (now - li.dwTime) / 1000;
        }

        static double CpuLoad()
        {
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher(
                    "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        object v = mo["PercentProcessorTime"];
                        if (v != null) return Convert.ToDouble(v);
                    }
                }
            }
            catch (Exception dg519_1) { Diag.Swallow("CondForm.cs:519", dg519_1); }
            return 0;
        }
    }
}