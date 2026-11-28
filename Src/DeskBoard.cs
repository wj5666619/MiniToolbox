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
    class DeskBoard : Form
    {
        Label lbCpu, lbMem, lbNet, lbDisk, lbTitle;
        Color cBg = UiKit.A(18, 22, 28), cBorder = UiKit.A(60, 120, 180), cText = UiKit.A(220, 228, 240), cTitle = UiKit.A(150, 200, 255);
        int themeIdx = 0;
        System.Windows.Forms.Timer tmr;
        ulong pIdle, pKernel, pUser;
        bool hasPrev = false;
        long pRx;
        DateTime pAt;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

        [StructLayout(LayoutKind.Sequential)]
        class DeskMem
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
            public DeskMem() { dwLength = (uint)Marshal.SizeOf(typeof(DeskMem)); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] DeskMem lpBuffer);

        static readonly Color[][] BoardThemes = new Color[][]
        {
            new Color[] { UiKit.A(18, 22, 28), UiKit.A(60, 120, 180), UiKit.A(220, 228, 240), UiKit.A(150, 200, 255) },
            new Color[] { UiKit.A(14, 24, 20), UiKit.A(52, 168, 120), UiKit.A(224, 240, 230), UiKit.A(130, 240, 190) },
            new Color[] { UiKit.A(24, 18, 30), UiKit.A(140, 100, 190), UiKit.A(238, 228, 248), UiKit.A(205, 165, 255) },
            new Color[] { UiKit.A(32, 24, 16), UiKit.A(214, 150, 60), UiKit.A(245, 236, 225), UiKit.A(255, 200, 120) }
        };

        public DeskBoard()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = UiKit.A(18, 22, 28);
            Size = new Size(214, 132);
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 9F);

            Label t = new Label();
            t.Text = "系统看板";
            t.ForeColor = UiKit.A(150, 200, 255);
            t.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            t.Location = new Point(12, 8);
            t.AutoSize = true;
            Controls.Add(t);
            lbTitle = t;

            Label x = new Label();
            x.Text = "✕";
            x.ForeColor = UiKit.A(120, 130, 145);
            x.Font = new Font("Microsoft YaHei UI", 8F);
            x.Location = new Point(190, 6);
            x.AutoSize = true;
            x.Cursor = Cursors.Hand;
            x.Click += delegate { Close(); };
            Controls.Add(x);

            Label bWall = new Label();
            bWall.Text = "图";
            bWall.ForeColor = UiKit.A(150, 200, 255);
            bWall.Font = new Font("Microsoft YaHei UI", 8F);
            bWall.Location = new Point(136, 6);
            bWall.AutoSize = true;
            bWall.Cursor = Cursors.Hand;
            bWall.Click += delegate { WallpaperDaily(); };
            Controls.Add(bWall);

            Label bThm = new Label();
            bThm.Text = "色";
            bThm.ForeColor = UiKit.A(150, 200, 255);
            bThm.Font = new Font("Microsoft YaHei UI", 8F);
            bThm.Location = new Point(160, 6);
            bThm.AutoSize = true;
            bThm.Cursor = Cursors.Hand;
            bThm.Click += delegate { themeIdx = (themeIdx + 1) % BoardThemes.Length; ApplyBoardTheme(themeIdx); };
            Controls.Add(bThm);
            themeLabels = new Label[] { bWall, bThm };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("每日一图壁纸", null, delegate { WallpaperDaily(); });
            menu.Items.Add("恢复原壁纸", null, delegate
            {
                string err = "";
                if (WpCore.RestoreOriginal(BoardDir(), out err)) lbTitle.Text = "壁纸已还原";
                else lbTitle.Text = "还原失败：" + err;
            });
            menu.Items.Add("切换看板配色", null, delegate { themeIdx = (themeIdx + 1) % BoardThemes.Length; ApplyBoardTheme(themeIdx); });
            this.ContextMenuStrip = menu;

            themeIdx = LoadBoardTheme();
            ApplyBoardTheme(themeIdx);

            lbCpu = MkLabel(12, 32);
            lbMem = MkLabel(12, 56);
            lbNet = MkLabel(12, 80);
            lbDisk = MkLabel(12, 104);

            tmr = new System.Windows.Forms.Timer();
            tmr.Interval = 2000;
            tmr.Tick += delegate { Refresh2(); };
            tmr.Start();
            Refresh2();

            MouseDown += delegate (object s, MouseEventArgs e) { dragX = e.X; dragY = e.Y; };
            MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Location = new Point(Left + e.X - dragX, Top + e.Y - dragY);
            };
        }

        int dragX, dragY;
        Label[] themeLabels;

        string BoardDir()
        {
            try { return Path.GetDirectoryName(Application.ExecutablePath); } catch { return ""; }
        }

        string ThemeFile()
        {
            string d = BoardDir();
            return d.Length > 0 ? Path.Combine(d, "看板主题.txt") : "";
        }

        int LoadBoardTheme()
        {
            try
            {
                string f = ThemeFile();
                if (f.Length > 0 && File.Exists(f))
                {
                    int v;
                    if (int.TryParse(File.ReadAllText(f, Encoding.UTF8).Trim(), out v) && v >= 0 && v < BoardThemes.Length) return v;
                }
            }
            catch (Exception dg170_1) { Diag.Swallow("DeskBoard.cs:170", dg170_1); }
            return 0;
        }

        void ApplyBoardTheme(int idx)
        {
            try
            {
                if (idx < 0 || idx >= BoardThemes.Length) idx = 0;
                Color[] th = BoardThemes[idx];
                cBg = th[0]; cBorder = th[1]; cText = th[2]; cTitle = th[3];
                BackColor = cBg;
                if (lbTitle != null) lbTitle.ForeColor = cTitle;
                Label[] subs = new Label[] { lbCpu, lbMem, lbNet, lbDisk };
                for (int i = 0; i < subs.Length; i++) if (subs[i] != null) subs[i].ForeColor = cText;
                if (themeLabels != null)
                    for (int i = 0; i < themeLabels.Length; i++) if (themeLabels[i] != null) themeLabels[i].ForeColor = cTitle;
                try
                {
                    string f = ThemeFile();
                    if (f.Length > 0) File.WriteAllText(f, idx.ToString(), new UTF8Encoding(false));
                }
                catch (Exception dg192_1) { Diag.Swallow("DeskBoard.cs:192", dg192_1); }
                Invalidate();
            }
            catch (Exception dg195_1) { Diag.Swallow("DeskBoard.cs:195", dg195_1); }
        }

        void WallpaperDaily()
        {
            string dir = Path.Combine(BoardDir(), "壁纸");
            string path, title, err;
            try { lbTitle.Text = "取图中…"; Application.DoEvents(); } catch (Exception dg202_1) { Diag.Swallow("DeskBoard.cs:202", dg202_1); }
            WpCore.BackupOnce(dir);
            if (!WpCore.DownloadDaily(dir, out path, out title, out err))
            {
                try { lbTitle.Text = "取图失败"; } catch (Exception dg206_1) { Diag.Swallow("DeskBoard.cs:206", dg206_1); }
                return;
            }
            if (!WpCore.Apply(path))
            {
                try { lbTitle.Text = "设壁纸失败"; } catch (Exception dg211_1) { Diag.Swallow("DeskBoard.cs:211", dg211_1); }
                return;
            }
            try { lbTitle.Text = "壁纸已换"; } catch (Exception dg214_1) { Diag.Swallow("DeskBoard.cs:214", dg214_1); }
        }

        Label MkLabel(int x, int y)
        {
            Label l = new Label();
            l.Text = "—";
            l.ForeColor = UiKit.A(220, 228, 240);
            l.Font = new Font("Consolas", 9F, FontStyle.Bold);
            l.Location = new Point(x, y);
            l.Size = new Size(196, 20);
            Controls.Add(l);
            return l;
        }

        void Refresh2()
        {
            try
            {
                ulong idle, kernel, user;
                double cpu = -1;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    if (hasPrev)
                    {
                        double dIdle = (double)(idle - pIdle);
                        double dTotal = (double)((kernel - pKernel) + (user - pUser));
                        if (dTotal > 0) cpu = Math.Max(0, Math.Min(100, (dTotal - dIdle) * 100.0 / dTotal));
                    }
                    pIdle = idle; pKernel = kernel; pUser = user; hasPrev = true;
                }
                if (cpu >= 0) lbCpu.Text = "CPU   " + cpu.ToString("0") + "%";

                DeskMem m = new DeskMem();
                if (GlobalMemoryStatusEx(m))
                {
                    double used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                    double total = m.ullTotalPhys / 1073741824.0;
                    lbMem.Text = "内存  " + used.ToString("0.0") + "/" + total.ToString("0.0") + " GB";
                }

                long rx = 0;
                try
                {
                    foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                        rx += ni.GetIPv4Statistics().BytesReceived;
                    }
                }
                catch (Exception dg265_1) { Diag.Swallow("DeskBoard.cs:265", dg265_1); }
                DateTime now = DateTime.Now;
                double sec = (now - pAt).TotalSeconds;
                if (sec <= 0.2) sec = 2;
                double kb = (rx - pRx) / sec / 1024.0;
                pRx = rx; pAt = now;
                if (kb > 1024) lbNet.Text = "网速  " + (kb / 1024).ToString("0.0") + " MB/s";
                else lbNet.Text = "网速  " + kb.ToString("0") + " KB/s";

                try
                {
                    DriveInfo d = new DriveInfo("C");
                    lbDisk.Text = "C盘余 " + (d.AvailableFreeSpace / 1073741824L) + " GB";
                }
                catch (Exception dg279_1) { Diag.Swallow("DeskBoard.cs:279", dg279_1); }
                Invalidate();
            }
            catch (Exception dg282_1) { Diag.Swallow("DeskBoard.cs:282", dg282_1); }
        }

        public string DumpText()
        {
            return lbCpu.Text + " | " + lbMem.Text + " | " + lbNet.Text + " | " + lbDisk.Text;
        }

        public string DumpExtra()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.Append("board size=" + Width + "x" + Height + " theme=" + themeIdx
                    + " bg=" + cBg.R + "," + cBg.G + "," + cBg.B
                    + " border=" + cBorder.R + "," + cBorder.G + "," + cBorder.B + "\r\n");
                foreach (Control c in Controls)
                {
                    if (c is Label)
                        sb.Append("  [" + c.Text + "] " + c.Left + "," + c.Top + " " + c.Width + "x" + c.Height + " color=" + c.ForeColor.R + "," + c.ForeColor.G + "," + c.ForeColor.B + "\r\n");
                }
                sb.Append("menu=" + (ContextMenuStrip != null ? ContextMenuStrip.Items.Count + " 项" : "无") + "\r\n");
                sb.Append("当前壁纸=" + WpCore.Current() + "\r\n");
            }
            catch (Exception e) { sb.Append("EX " + e.Message + "\r\n"); }
            return sb.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(cBorder, 1F))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            try { if (tmr != null) tmr.Stop(); } catch (Exception dg320_1) { Diag.Swallow("DeskBoard.cs:320", dg320_1); }
            base.Dispose(disposing);
        }
    }
}