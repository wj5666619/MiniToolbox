using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ShutdownTool
{
    // v19.7 崩溃窗口：原来只有一个 MessageBox，用户除了点「是/否」什么也做不了。
    // 现在把三件企业级该做的事放在同一个面板里：看日志位置、一键复制诊断、直接重启；
    // 并且明确告诉用户「可以继续用」，避免一次小异常就丢掉正在做的事。
    class CrashForm : Form
    {
        Exception ex;
        Label lblMsg;
        string logPath = "";
        bool dragging = false;
        Point dragStart;

        public CrashForm(Exception e)
        {
            ex = e;
            Text = "小工具箱 · 诊断";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 312);
            BackColor = UiKit.A(18, 22, 29);
            DoubleBuffered = true;
            ShowInTaskbar = true;
            TopMost = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception dg33_1) { Diag.Swallow("CrashForm.cs:33", dg33_1); }

            Panel head = new Panel();
            head.Location = new Point(0, 0);
            head.Size = new Size(580, 54);
            head.BackColor = UiKit.A(24, 29, 38);
            Controls.Add(head);
            head.MouseDown += (s, m) => { if (m.Button == MouseButtons.Left) { dragging = true; dragStart = new Point(m.X, m.Y); } };
            head.MouseMove += (s, m) =>
            {
                if (!dragging) return;
                Point sc = PointToScreen(new Point(m.X, m.Y));
                Location = new Point(sc.X - dragStart.X, sc.Y - dragStart.Y);
            };
            head.MouseUp += (s, m) => { dragging = false; };

            Label t1 = new Label();
            t1.Text = "出了点问题";
            t1.ForeColor = Color.White;
            t1.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            t1.BackColor = Color.Transparent;
            t1.Location = new Point(20, 15);
            t1.AutoSize = true;
            head.Controls.Add(t1);

            Label sub = new Label();
            sub.Text = "已记录到诊断日志，可复制给开发者排查；不影响正在做的事可以继续";
            sub.ForeColor = UiKit.A(140, 152, 170);
            sub.Font = new Font("Microsoft YaHei UI", 8F);
            sub.BackColor = Color.Transparent;
            sub.Location = new Point(112, 19);
            sub.AutoSize = true;
            head.Controls.Add(sub);

            Label cls = new Label();
            cls.Text = "×";
            cls.ForeColor = UiKit.A(170, 180, 196);
            cls.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            cls.BackColor = Color.Transparent;
            cls.Location = new Point(552, 12);
            cls.Size = new Size(28, 28);
            cls.TextAlign = ContentAlignment.MiddleCenter;
            cls.Cursor = Cursors.Hand;
            cls.Click += (s, e2) => Close();
            head.Controls.Add(cls);

            lblMsg = new Label();
            lblMsg.ForeColor = UiKit.A(226, 232, 240);
            lblMsg.Font = new Font("Consolas", 9F);
            lblMsg.BackColor = Color.Transparent;
            lblMsg.Location = new Point(20, 72);
            lblMsg.Size = new Size(540, 62);
            lblMsg.AutoEllipsis = true;
            Controls.Add(lblMsg);

            string msg = ex == null ? "(未知错误)" : (ex.GetType().Name + ": " + ex.Message);
            if (msg.Length > 420) msg = msg.Substring(0, 420) + " …";
            lblMsg.Text = msg;

            logPath = Diag.FilePath.Length > 0 ? Diag.FilePath : "(日志未写入)";
            Label lp = new Label();
            lp.Text = "日志：" + logPath;
            lp.ForeColor = UiKit.A(120, 132, 150);
            lp.Font = new Font("Microsoft YaHei UI", 8F);
            lp.BackColor = Color.Transparent;
            lp.Location = new Point(22, 142);
            lp.Size = new Size(536, 22);
            lp.AutoEllipsis = true;
            Controls.Add(lp);

            Label tips = new Label();
            tips.Text = "选「复制诊断」会把版本、系统信息和最近 60 行日志放进剪贴板；反馈问题时粘贴即可。";
            tips.ForeColor = UiKit.A(104, 116, 134);
            tips.Font = new Font("Microsoft YaHei UI", 8F);
            tips.BackColor = Color.Transparent;
            tips.Location = new Point(22, 168);
            tips.Size = new Size(536, 22);
            Controls.Add(tips);

            // 按钮排：继续（默认）/ 复制诊断 / 打开日志 / 重启
            Button bClose = Flat("继续使用", 432, 236, 120, 36, UiKit.A(40, 48, 60), UiKit.A(226, 232, 240), false);
            bClose.Click += (s, e2) => Close();
            Controls.Add(bClose);

            Button bCopy = Flat("复制诊断", 300, 236, 120, 36, UiKit.A(40, 48, 60), UiKit.A(226, 232, 240), false);
            bCopy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(Diag.Summary(ex));
                    bCopy.Text = "已复制";
                    Timer tt = new Timer(); tt.Interval = 1500;
                    tt.Tick += (s2, e3) => { tt.Stop(); tt.Dispose(); try { bCopy.Text = "复制诊断"; } catch (Exception dg125_1) { Diag.Swallow("CrashForm.cs:125", dg125_1); } };
                    tt.Start();
                }
                catch (Exception dg128_1) { Diag.Swallow("CrashForm.cs:128", dg128_1); }
            };
            Controls.Add(bCopy);

            Button bLog = Flat("打开日志", 168, 236, 120, 36, UiKit.A(40, 48, 60), UiKit.A(226, 232, 240), false);
            bLog.Click += delegate
            {
                try
                {
                    if (Diag.Dir.Length > 0 && Directory.Exists(Diag.Dir)) Process.Start("explorer.exe", Diag.Dir);
                    else Process.Start("explorer.exe", Path.GetDirectoryName(Application.ExecutablePath));
                }
                catch (Exception dg140_1) { Diag.Swallow("CrashForm.cs:140", dg140_1); }
            };
            Controls.Add(bLog);

            Button bRestart = Flat("重新启动", 36, 236, 120, 36, UiKit.A(32, 92, 78), Color.White, true);
            bRestart.Click += (s, e2) => { try { Application.Restart(); } catch { Close(); } };
            Controls.Add(bRestart);

            KeyPreview = true;
            KeyDown += (s, e2) => { if (e2.KeyCode == Keys.Escape) Close(); };
        }

        Button Flat(string text, int x, int y, int w, int h, Color back, Color fore, bool bold)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = fore;
            b.Font = new Font("Microsoft YaHei UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.TabStop = true;
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(UiKit.A(44, 52, 66), 1f))
                g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (Pen p2 = new Pen(UiKit.A(232, 120, 100), 2f))
                g.DrawLine(p2, 0, 54, ClientSize.Width, 54);
        }

        public static void ShowCrash(Exception e)
        {
            try
            {
                CrashForm f = new CrashForm(e);
                f.ShowDialog();
            }
            catch
            {
                try
                {
                    MessageBox.Show("小工具箱遇到内部错误，已记录到日志：\n" + (Diag.FilePath.Length > 0 ? Diag.FilePath : "程序目录\\日志\\小工具箱.log"),
                        "出了点问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception dg193_1) { Diag.Swallow("CrashForm.cs:193", dg193_1); }
            }
        }
    }
}