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
    class ScreenRecForm : Form
    {
        bool en;
        string dir;
        Label st;
        Label el;
        ComboBox cbFps;
        ComboBox cbMode;
        TextBox tbOut;
        Button bStart;
        Button bStop;
        Button bPick;
        System.Windows.Forms.Timer tick;
        Process rec;
        DateTime t0;
        string outFile = "";
        Rectangle region = Rectangle.Empty;

        public ScreenRecForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "高清录屏 · 小工具箱", "Screen Recorder · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(840, 600);
            MinimumSize = new Size(840, 600);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "高清录屏 → MP4", "Screen recorder → MP4"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "走 ffmpeg gdigrab 抓屏，优先 NVIDIA NVENC 硬编（没有就退 Intel QSV / AMD AMF / CPU）。全屏或框选区域，可随时停止。",
                "Captures the desktop with ffmpeg gdigrab, prefers NVENC hardware encoding. Full screen or a picked region."), 20, 48, 800, 40));

            string ff = ScreenRecCore.Ffmpeg(dir);
            Label lff = V71Ui.Note("", 20, 92, 800, 22);
            lff.Text = ff.Length > 0
                ? V71Ui.TT(en, "ffmpeg：", "ffmpeg: ") + ff + V71Ui.TT(en, "　编码器：", "   encoder: ") + ScreenRecCore.EncoderLabel(ScreenRecCore.PickEncoder(dir))
                : V71Ui.TT(en, "未找到 ffmpeg（先到「视频下载」用带 FFmpeg 的高清格式下一次即自动部署）", "ffmpeg missing: download one HD (FFmpeg) format once to deploy it.");
            lff.ForeColor = ff.Length > 0 ? V71Ui.SUB : V71Ui.WARN;
            Controls.Add(lff);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "录制范围", "Capture area"), 20, 130, 120, 22, 9F, V71Ui.SUB));
            cbMode = V71Ui.Combo(new string[] {
                V71Ui.TT(en, "全屏（主显示器）", "Full screen (primary)"),
                V71Ui.TT(en, "框选区域", "Picked region") }, 20, 154, 240);
            Controls.Add(cbMode);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "帧率", "Frame rate"), 300, 130, 120, 22, 9F, V71Ui.SUB));
            cbFps = V71Ui.Combo(new string[] { "15", "30", "60" }, 300, 154, 90);
            cbFps.SelectedIndex = 1;
            Controls.Add(cbFps);

            bPick = V71Ui.Btn(V71Ui.TT(en, "框选一下", "Pick region"), UiKit.A(52, 58, 70), 430, 152, 120, 30);
            Controls.Add(bPick);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "输出目录", "Output folder"), 20, 200, 120, 22, 9F, V71Ui.SUB));
            tbOut = V71Ui.Box(Path.Combine(dir, "录屏"), 20, 224, 560, 28);
            Controls.Add(tbOut);
            Button bBrow = V71Ui.Btn("…", UiKit.A(52, 58, 70), 592, 222, 60, 30);
            Controls.Add(bBrow);
            bBrow.Click += delegate
            {
                FolderBrowserDialog fd = new FolderBrowserDialog();
                if (fd.ShowDialog(this) == DialogResult.OK) tbOut.Text = fd.SelectedPath;
            };

            bStart = V71Ui.Btn(V71Ui.TT(en, "开始录制", "Start"), UiKit.A(34, 197, 94), 20, 274, 180, 42);
            bStop = V71Ui.Btn(V71Ui.TT(en, "停止并保存", "Stop & save"), UiKit.A(232, 82, 61), 216, 274, 180, 42);
            bStop.Enabled = false;
            Controls.Add(bStart); Controls.Add(bStop);

            el = V71Ui.Text("", 420, 282, 300, 32, 15F, Color.White);
            Controls.Add(el);

            st = V71Ui.Note("", 20, 330, 800, 60);
            Controls.Add(st);

            CheckBox cTop = new RoundCheck();
            cTop.Text = V71Ui.TT(en, "录制时把本窗口最小化（免得录到自己）", "Minimize this window while recording");
            cTop.ForeColor = V71Ui.SUB;
            cTop.BackColor = Color.Transparent;
            cTop.Checked = true;
            cTop.Location = new Point(20, 400);
            cTop.Size = new Size(360, 22);
            Controls.Add(cTop);

            Label note = V71Ui.Note(V71Ui.TT(en,
                "说明：录屏是「抓整屏」的方式，全屏与框选都不需要额外驱动；硬件编码只影响 CPU 占用，画质由 cq/crf 23 控制。停止时会先给 ffmpeg 发 q 让它收尾，不会留半截文件。",
                "Notes: capture works without extra drivers. Hardware encoding only changes CPU load. Stop sends q so the file is finalized cleanly."), 20, 430, 780, 60);
            Controls.Add(note);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 700, 548, 100, 32);
            Controls.Add(bClose);
            bClose.Click += delegate
            {
                if (rec != null && !rec.HasExited) StopRec(true);
                Close();
            };

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 500;
            tick.Tick += delegate
            {
                if (rec == null) return;
                double sec = (DateTime.Now - t0).TotalSeconds;
                el.Text = TimeSpan.FromSeconds(sec).ToString(@"hh\:mm\:ss");
                try
                {
                    if (File.Exists(outFile)) st.Text = V71Ui.TT(en, "录制中… 已写入 ", "Recording… ") + FmtKb(new FileInfo(outFile).Length);
                }
                catch (Exception dg135_1) { Diag.Swallow("ScreenRecForm.cs:135", dg135_1); }
            };

            bPick.Click += delegate
            {
                Rectangle r = PickRegion();
                if (r.Width < 40 || r.Height < 40) { st.Text = V71Ui.TT(en, "框选太小，已取消。", "Selection too small."); return; }
                region = r;
                cbMode.SelectedIndex = 1;
                st.Text = V71Ui.TT(en, "已选区域：", "Region: ") + r.X + "," + r.Y + " " + r.Width + "x" + r.Height;
            };

            bStart.Click += delegate { StartRec(cTop.Checked); };
            bStop.Click += delegate { StopRec(false); };

            FormClosing += delegate { if (rec != null && !rec.HasExited) StopRec(true); };
        }

        static string FmtKb(long b)
        {
            if (b > 1048576 * 1024) return (b / 1048576.0 / 1024).ToString("0.0") + " GB";
            if (b > 1048576) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1024.0).ToString("0") + " KB";
        }

        Rectangle PickRegion()
        {
            Rectangle full = Rectangle.Empty;
            foreach (Screen s in Screen.AllScreens) full = Rectangle.Union(full, s.Bounds);
            Form ov = new Form();
            ov.FormBorderStyle = FormBorderStyle.None;
            ov.StartPosition = FormStartPosition.Manual;
            ov.Bounds = full;
            ov.BackColor = Color.Black;
            ov.Opacity = 0.35;
            ov.TopMost = true;
            ov.ShowInTaskbar = false;
            ov.Cursor = Cursors.Cross;
            Rectangle sel = Rectangle.Empty;
            Point p0 = Point.Empty;
            bool drag = false;
            ov.MouseDown += delegate (object s, MouseEventArgs e) { p0 = e.Location; drag = true; };
            ov.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!drag) return;
                sel = new Rectangle(Math.Min(p0.X, e.X), Math.Min(p0.Y, e.Y), Math.Abs(p0.X - e.X), Math.Abs(p0.Y - e.Y));
                ov.Invalidate();
            };
            ov.MouseUp += delegate
            {
                drag = false;
                ov.Close();
            };
            ov.Paint += delegate (object s, PaintEventArgs e)
            {
                if (sel.Width > 0 && sel.Height > 0)
                {
                    using (Pen pen = new Pen(UiKit.A(96, 165, 250), 2)) e.Graphics.DrawRectangle(pen, sel);
                    using (SolidBrush b = new SolidBrush(UiKit.AA(24, 165, 250, 96))) e.Graphics.FillRectangle(b, sel);
                }
            };
            ov.ShowDialog();
            return sel;
        }

        void StartRec(bool minimize)
        {
            string ff = ScreenRecCore.Ffmpeg(dir);
            if (ff.Length == 0) { V71Ui.Say(this, V71Ui.TT(en, "缺少 ffmpeg，先部署组件。", "ffmpeg missing.")); return; }
            string od = tbOut.Text == null ? "" : tbOut.Text.Trim();
            if (od.Length == 0) od = Path.Combine(dir, "录屏");
            try { if (!Directory.Exists(od)) Directory.CreateDirectory(od); } catch (Exception ex) { V71Ui.Say(this, ex.Message); return; }
            int fps = 30;
            try { fps = int.Parse(cbFps.SelectedItem.ToString()); } catch (Exception dg208_1) { Diag.Swallow("ScreenRecForm.cs:208", dg208_1); }
            outFile = Path.Combine(od, "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
            bool reg = cbMode.SelectedIndex == 1 && region.Width > 0;
            string enc = ScreenRecCore.PickEncoder(dir);
            string args = ScreenRecCore.BuildArgs(reg, region.X, region.Y, region.Width, region.Height, fps, enc, outFile);

            ProcessStartInfo psi = new ProcessStartInfo(ff, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                rec = Process.Start(psi);
                t0 = DateTime.Now;
                tick.Start();
                bStart.Enabled = false;
                bStop.Enabled = true;
                st.Text = V71Ui.TT(en, "已开始录制：", "Recording to: ") + outFile;
                if (minimize) BeginInvoke(new MethodInvoker(delegate { WindowState = FormWindowState.Minimized; }));
            }
            catch (Exception ex)
            {
                rec = null;
                V71Ui.Say(this, V71Ui.TT(en, "启动失败：", "Start failed: ") + ex.Message);
            }
        }

        void StopRec(bool silent)
        {
            tick.Stop();
            try
            {
                if (rec != null && !rec.HasExited)
                {
                    try { rec.StandardInput.Write("q\n"); rec.StandardInput.Flush(); } catch (Exception dg245_1) { Diag.Swallow("ScreenRecForm.cs:245", dg245_1); }
                    if (!rec.WaitForExit(4000)) { try { rec.Kill(); } catch (Exception dg246_1) { Diag.Swallow("ScreenRecForm.cs:246", dg246_1); } }
                }
            }
            catch (Exception dg249_1) { Diag.Swallow("ScreenRecForm.cs:249", dg249_1); }
            rec = null;
            bStart.Enabled = true;
            bStop.Enabled = false;
            long len = 0;
            try { if (File.Exists(outFile)) len = new FileInfo(outFile).Length; } catch (Exception dg254_1) { Diag.Swallow("ScreenRecForm.cs:254", dg254_1); }
            if (!silent)
            {
                st.Text = V71Ui.TT(en, "已保存：", "Saved: ") + outFile + "  (" + FmtKb(len) + ")";
                if (len <= 0) st.Text = V71Ui.TT(en, "没有写出文件，检查 ffmpeg 与显示器。", "No file written; check ffmpeg.");
            }
        }
    }
}