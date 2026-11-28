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
    class CompProgressForm : Form
    {
        Label lbTitle;
        Label lbInfo;
        Panel track;
        Panel fill;
        System.Windows.Forms.Timer tm;
        long got;
        long total;
        long lastGot;
        DateTime lastAt;

        public CompProgressForm(string display)
        {
            Text = "下载组件";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 104);
            BackColor = UiKit.A(20, 22, 28);
            ShowInTaskbar = false;
            DoubleBuffered = true;
            ControlBox = false;

            lbTitle = new Label();
            lbTitle.AutoSize = false;
            lbTitle.SetBounds(22, 16, 376, 24);
            lbTitle.Text = "正在下载「" + display + "」组件";
            lbTitle.ForeColor = UiKit.A(226, 232, 240);
            lbTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lbTitle.BackColor = Color.Transparent;
            Controls.Add(lbTitle);

            track = new Panel();
            track.SetBounds(22, 52, 376, 8);
            track.BackColor = UiKit.A(38, 42, 52);
            Controls.Add(track);

            fill = new Panel();
            fill.SetBounds(0, 0, 0, 8);
            fill.BackColor = UiKit.A(56, 189, 248);
            track.Controls.Add(fill);

            lbInfo = new Label();
            lbInfo.AutoSize = false;
            lbInfo.SetBounds(22, 68, 376, 20);
            lbInfo.Text = "正在连接服务器…";
            lbInfo.ForeColor = UiKit.A(148, 163, 184);
            lbInfo.Font = new Font("Microsoft YaHei UI", 8.5F);
            lbInfo.BackColor = Color.Transparent;
            Controls.Add(lbInfo);

            lastAt = DateTime.Now;
            tm = new System.Windows.Forms.Timer();
            tm.Interval = 150;
            tm.Tick += delegate(object s, EventArgs e) { RefreshUi(); };
            tm.Start();
            FormClosed += delegate(object s, FormClosedEventArgs e) { try { tm.Stop(); tm.Dispose(); } catch (Exception dg79_1) { Diag.Swallow("CompProgressForm.cs:79", dg79_1); } };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(UiKit.A(56, 189, 248), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public void SetProgress(long g, long t)
        {
            got = g;
            total = t;
        }

        void RefreshUi()
        {
            if (IsDisposed) return;
            int pct = 0;
            if (total > 0) pct = (int)(got * 100L / total);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            int w = (int)(track.Width * pct / 100.0);
            if (w < 0) w = 0;
            fill.SetBounds(0, 0, w, track.Height);

            string spd = "";
            DateTime now = DateTime.Now;
            double sec = (now - lastAt).TotalSeconds;
            if (sec >= 0.3)
            {
                if (got > lastGot) spd = "　" + ((got - lastGot) / 1048576.0 / sec).ToString("0.0") + " MB/s";
                lastGot = got;
                lastAt = now;
            }
            double mbg = got / 1048576.0;
            double mbt = total > 0 ? total / 1048576.0 : 0;
            if (mbt > 0)
                lbInfo.Text = "已下载 " + mbg.ToString("0.0") + " / " + mbt.ToString("0.0") + " MB　" + pct + "%" + spd;
            else
                lbInfo.Text = "已下载 " + mbg.ToString("0.0") + " MB" + spd;
        }

        public void CloseSafe()
        {
            try
            {
                if (IsHandleCreated) BeginInvoke((Action)(delegate { try { Close(); } catch (Exception dg127_1) { Diag.Swallow("CompProgressForm.cs:127", dg127_1); } }));
            }
            catch (Exception dg129_1) { Diag.Swallow("CompProgressForm.cs:129", dg129_1); }
        }
    }
}