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
    class UpdateForm : Form
    {
        bool en;
        internal bool autoRun = false;
        internal bool autoYes = false;
        const string APP_VER = UiKit.VER;
        // v19.12：反馈统一发到作者邮箱；mailto 会打开系统默认邮件应用并预填主题与正文
        const string FEEDBACK_MAIL = "m18371900557@163.com";
        Label lblState;
        Panel stateDot;
        Button btnCheck;
        string T(string zh, string en2) { return Lang.T(zh, en2); }

        // 关于页的胶囊被点击时记下目标工具页（值 = MainForm.SLOT 的下标），
        // 窗口先关掉，主窗口在 ShowDialog 返回后读走这个值再切页，免得模态期间切了也看不见
        internal static int PendingJump = -1;

        void JumpChip(int slot)
        {
            if (btnCheck != null && !btnCheck.Enabled) return;   // 正检查/下载更新时先别关窗
            PendingJump = slot;
            Close();
        }

        void GlassCard(object sender, PaintEventArgs e, Color top, Color bot)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Panel p = (Panel)sender;
            Rectangle rc = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
            using (GraphicsPath path = UiKit.RoundedPath(rc, 12))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(rc, top, bot, 78f))
                    g.FillPath(br, path);
                using (Pen pen = new Pen(UiKit.AA(46, 255, 255, 255), 1f))
                    g.DrawPath(pen, path);
            }
        }

        Panel MakeLinkCard(string title, string sub, string glyph, int x, int y, int w, int h, EventHandler onClick)
        {
            Panel card = new Panel();
            card.Location = new Point(x, y);
            card.Size = new Size(w, h);
            card.BackColor = UiKit.A(26, 30, 38);
            card.Cursor = Cursors.Hand;
            card.Paint += (s2, e2) => GlassCard(s2, e2, UiKit.A(38, 44, 55), UiKit.A(26, 30, 38));
            card.Click += onClick;

            Label ic = new Label();
            ic.Text = glyph;
            ic.ForeColor = UiKit.A(150, 162, 182);
            ic.Font = new Font("Segoe MDL2 Assets", 13F);
            ic.BackColor = Color.Transparent;
            ic.Location = new Point(16, (h - 24) / 2);
            ic.AutoSize = true;
            ic.Cursor = Cursors.Hand;
            ic.Click += onClick;
            card.Controls.Add(ic);

            Label ti = new Label();
            ti.Text = title;
            ti.ForeColor = UiKit.A(232, 236, 242);
            ti.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            ti.BackColor = Color.Transparent;
            ti.Location = new Point(46, h / 2 - 26);
            ti.AutoSize = true;
            ti.Cursor = Cursors.Hand;
            ti.Click += onClick;
            card.Controls.Add(ti);

            Label su = new Label();
            su.Text = sub;
            su.ForeColor = UiKit.A(122, 132, 148);
            su.Font = new Font("Microsoft YaHei UI", 8F);
            su.BackColor = Color.Transparent;
            su.Location = new Point(46, h / 2 - 4);
            su.AutoSize = true;
            su.Cursor = Cursors.Hand;
            su.Click += onClick;
            card.Controls.Add(su);

            Label ar = new Label();
            ar.Text = "\uE8A7";
            ar.ForeColor = UiKit.A(96, 108, 126);
            ar.Font = new Font("Segoe MDL2 Assets", 9F);
            ar.BackColor = Color.Transparent;
            ar.Location = new Point(w - 30, 12);
            ar.AutoSize = true;
            ar.Cursor = Cursors.Hand;
            ar.Click += onClick;
            card.Controls.Add(ar);
            return card;
        }

        public UpdateForm(bool langEn)
        {
            en = langEn;
            // autoRun 可能在构造之后才置位（/updatetest 用），所以握手时再判定，避免重复挂两个处理器
            this.Shown += delegate
            {
                if (UiKit.Paper) BackColor = UiKit.PaperBg;
                try
                {
                    for (int ai = 1; ai < Environment.GetCommandLineArgs().Length; ai++)
                        if (string.Equals(Environment.GetCommandLineArgs()[ai], "/changelog", StringComparison.OrdinalIgnoreCase))
                        { BeginInvoke((Action)delegate { ShowChangelog(); }); break; }
                }
                catch (Exception dg129_1) { Diag.Swallow("UpdateForm.cs:129", dg129_1); }
                if (autoRun) this.BeginInvoke((Action)delegate { CheckUpdate(); });
            };
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 596);
            BackColor = UiKit.Paper ? UiKit.PaperBg : UiKit.A(20, 24, 31);
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;

            Panel bar = new Panel();
            bar.BackColor = UiKit.A(26, 30, 38);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(640, 44);
            Controls.Add(bar);

            Label ti = new Label();
            ti.Text = T("关于 · 软件更新", "About · Update");
            ti.ForeColor = UiKit.A(232, 236, 242);
            ti.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            ti.BackColor = Color.Transparent;
            ti.Location = new Point(18, 12);
            ti.AutoSize = true;
            bar.Controls.Add(ti);

            Button btnX = new Button();
            btnX.Text = "✕";
            btnX.FlatStyle = FlatStyle.Standard;
            btnX.FlatAppearance.BorderSize = 0;
            btnX.BackColor = UiKit.A(26, 30, 38);
            btnX.ForeColor = UiKit.A(150, 158, 172);
            btnX.Font = new Font("Segoe UI", 10F);
            btnX.Location = new Point(598, 9);
            btnX.Size = new Size(32, 26);
            btnX.TabStop = false;
            btnX.Click += (s2, e2) => Close();
            bar.Controls.Add(btnX);

            Point dragPt = Point.Empty;
            bar.MouseDown += (s2, e2) => { dragPt = e2.Location; };
            bar.MouseMove += (s2, e2) =>
            {
                if (e2.Button == MouseButtons.Left && dragPt != Point.Empty)
                { Location = new Point(Location.X + e2.X - dragPt.X, Location.Y + e2.Y - dragPt.Y); }
            };
            bar.MouseUp += (s2, e2) => { dragPt = Point.Empty; };

            // ---- 品牌卡：图标 + 名称 + 徽章 + 一句话主张 + 简介，右侧版本小卡 ----
            Panel brand = new Panel();
            brand.Location = new Point(16, 58);
            brand.Size = new Size(608, 124);
            brand.BackColor = UiKit.A(26, 30, 38);
            brand.Paint += (s2, e2) => GlassCard(s2, e2, UiKit.A(40, 46, 58), UiKit.A(25, 29, 37));
            Controls.Add(brand);

            PictureBox logo = new PictureBox();
            logo.Location = new Point(18, 18);
            logo.Size = new Size(54, 54);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            try { logo.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch (Exception dg190_1) { Diag.Swallow("UpdateForm.cs:190", dg190_1); }
            brand.Controls.Add(logo);

            Label nm = new Label();
            nm.Text = T("小工具箱", "Mini Toolbox");
            nm.ForeColor = UiKit.A(242, 246, 252);
            nm.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
            nm.BackColor = Color.Transparent;
            nm.Location = new Point(86, 12);
            nm.AutoSize = true;
            brand.Controls.Add(nm);

            Label badge = new Label();
            badge.Text = T("免费 · 开源", "Free · Open source");
            badge.ForeColor = UiKit.A(92, 226, 178);
            badge.BackColor = UiKit.A(26, 58, 50);
            badge.Font = new Font("Microsoft YaHei UI", 8F);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Location = new Point(230, 22);
            badge.Size = new Size(84, 22);
            brand.Controls.Add(badge);

            Label sg = new Label();
            sg.Text = T("小工具，大效率", "Small tools, big speed");
            sg.ForeColor = UiKit.A(96, 186, 255);
            sg.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            sg.BackColor = Color.Transparent;
            sg.Location = new Point(88, 50);
            sg.AutoSize = true;
            brand.Controls.Add(sg);

            Label ds = new Label();
            ds.Text = T("20+ 个常用工具收在一个 exe：定时、清理、下载、去水印、Steam 成就；\n五套皮肤一键切换，双击即用。",
                "20+ tools in one portable exe; five skins, no install.");
            ds.ForeColor = UiKit.A(146, 158, 176);
            ds.Font = new Font("Microsoft YaHei UI", 8F);
            ds.BackColor = Color.Transparent;
            ds.Location = new Point(88, 78);
            ds.Size = new Size(310, 40);
            brand.Controls.Add(ds);

            Panel vbox = new Panel();
            vbox.Location = new Point(412, 16);
            vbox.Size = new Size(180, 92);
            vbox.BackColor = UiKit.A(22, 26, 33);
            vbox.Paint += (s2, e2) =>
            {
                Graphics g = e2.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, vbox.Width - 1, vbox.Height - 1);
                using (GraphicsPath pa = UiKit.RoundedPath(r, 4))
                {
                    using (LinearGradientBrush lg = new LinearGradientBrush(r, UiKit.A(34, 44, 60), UiKit.A(20, 26, 36), 35f))
                        g.FillPath(lg, pa);
                    using (Pen bp = new Pen(UiKit.AA(60, 120, 200, 255), 1f))
                        g.DrawPath(bp, pa);
                }
                using (SolidBrush ac = new SolidBrush(UiKit.A(61, 126, 255)))
                    g.FillRectangle(ac, 0, 0, 3, vbox.Height);
            };
            brand.Controls.Add(vbox);

            Label vnum = new Label();
            vnum.Text = "v" + APP_VER;
            vnum.ForeColor = UiKit.A(240, 246, 255);
            vnum.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            vnum.BackColor = Color.Transparent;
            vnum.Location = new Point(16, 14);
            vnum.AutoSize = true;
            vbox.Controls.Add(vnum);

            Label vcap = new Label();
            vcap.Text = T("纸感手绘 · 五套皮肤", "Paper sketch · 5 skins");
            vcap.ForeColor = UiKit.A(140, 156, 178);
            vcap.Font = new Font("Microsoft YaHei UI", 7.5F);
            vcap.BackColor = Color.Transparent;
            vcap.Location = new Point(16, 50);
            vcap.AutoSize = true;
            vbox.Controls.Add(vcap);

            // ---- 功能胶囊：六格一览（可点，点了关窗直接跳该工具页） ----
            string[] chipGlyph = { "⏱", "🧹", "🎬", "🎵", "🖼", "🏆" };
            string[] chipName = {
                T("定时关机", "Shutdown"), T("C盘清理", "Disk clean"), T("视频下载", "Video"),
                T("音乐下载", "Music"), T("去水印", "Watermark"), T("Steam 成就", "Steam")
            };
            int[] chipSlot = { 1, 2, 6, 7, 3, 8 };
            bool[] chipHov = new bool[6];
            ToolTip chipTip = new ToolTip();
            chipTip.AutoPopDelay = 6000;
            chipTip.InitialDelay = 320;

            for (int ci = 0; ci < 6; ci++)
            {
                int hi = ci;
                Panel cp = new Panel();
                cp.Location = new Point(16 + ci * 102, 192);
                cp.Size = new Size(94, 74);
                cp.BackColor = UiKit.A(28, 33, 42);
                cp.Cursor = Cursors.Hand;
                cp.Paint += (s2, e2) =>
                {
                    Graphics g = e2.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Control cc = (Control)s2;
                    Rectangle r = new Rectangle(0, 0, cc.Width - 1, cc.Height - 1);
                    using (GraphicsPath pa = UiKit.RoundedPath(r, 3))
                    {
                        using (SolidBrush fb = new SolidBrush(chipHov[hi] ? UiKit.A(35, 46, 63) : UiKit.A(30, 36, 46)))
                            g.FillPath(fb, pa);
                        using (Pen bp = new Pen(chipHov[hi] ? UiKit.A(120, 190, 255) : UiKit.AA(70, 130, 200, 255), chipHov[hi] ? 1.4f : 1f))
                            g.DrawPath(bp, pa);
                    }
                    using (Font af = new Font("Segoe UI", 8F, FontStyle.Bold))
                        TextRenderer.DrawText(g, "↗", af, new Point(r.Width - 16, 4),
                            chipHov[hi] ? UiKit.A(130, 195, 255) : UiKit.A(62, 90, 122));
                };
                Controls.Add(cp);

                Label gl = new Label();
                gl.Text = chipGlyph[ci];
                gl.Font = new Font("Segoe UI Emoji", 14F);
                gl.ForeColor = UiKit.A(110, 170, 255);
                gl.BackColor = Color.Transparent;
                gl.TextAlign = ContentAlignment.MiddleCenter;
                gl.Location = new Point(0, 12);
                gl.Size = new Size(94, 22);
                gl.Cursor = Cursors.Hand;
                cp.Controls.Add(gl);

                Label tl = new Label();
                tl.Text = chipName[ci];
                tl.Font = new Font("Microsoft YaHei UI", 8F);
                tl.ForeColor = UiKit.A(186, 196, 212);
                tl.BackColor = Color.Transparent;
                tl.TextAlign = ContentAlignment.MiddleCenter;
                tl.Location = new Point(0, 42);
                tl.Size = new Size(94, 20);
                tl.Cursor = Cursors.Hand;
                cp.Controls.Add(tl);

                // 悬停：底色提亮 + 描边转亮 + 右上角冒出箭头（子控件不接收鼠标，靠 MouseMove 补位）
                cp.MouseEnter += delegate { chipHov[hi] = true; cp.Invalidate(); };
                cp.MouseMove += delegate { if (!chipHov[hi]) { chipHov[hi] = true; cp.Invalidate(); } };
                cp.MouseLeave += delegate
                {
                    if (cp.ClientRectangle.Contains(cp.PointToClient(Cursor.Position))) return;
                    chipHov[hi] = false; cp.Invalidate();
                };
                gl.MouseEnter += delegate { chipHov[hi] = true; cp.Invalidate(); };
                gl.MouseMove += delegate { };
                gl.MouseLeave += delegate
                {
                    if (cp.ClientRectangle.Contains(cp.PointToClient(Cursor.Position))) return;
                    chipHov[hi] = false; cp.Invalidate();
                };
                tl.MouseEnter += delegate { chipHov[hi] = true; cp.Invalidate(); };
                tl.MouseLeave += delegate
                {
                    if (cp.ClientRectangle.Contains(cp.PointToClient(Cursor.Position))) return;
                    chipHov[hi] = false; cp.Invalidate();
                };

                // 点击：记下目标页并关窗，主窗口在 ShowDialog 返回后接手切页
                EventHandler jump = delegate { JumpChip(chipSlot[hi]); };
                cp.Click += jump; gl.Click += jump; tl.Click += jump;

                string tip = T("打开「" + chipName[ci] + "」", "Open " + chipName[ci]);
                chipTip.SetToolTip(cp, tip);
                chipTip.SetToolTip(gl, tip);
                chipTip.SetToolTip(tl, tip);
            }

            // ---- 版本卡片 ----
            Panel ver = new Panel();
            ver.Location = new Point(16, 280);
            ver.Size = new Size(608, 132);
            ver.BackColor = UiKit.A(26, 30, 38);
            ver.Paint += (s2, e2) => GlassCard(s2, e2, UiKit.A(40, 46, 58), UiKit.A(25, 29, 37));
            Controls.Add(ver);

            Label cap = new Label();
            cap.Text = T("当前版本", "Current version");
            cap.ForeColor = UiKit.A(130, 140, 156);
            cap.Font = new Font("Microsoft YaHei UI", 8.5F);
            cap.BackColor = Color.Transparent;
            cap.Location = new Point(20, 12);
            cap.AutoSize = true;
            ver.Controls.Add(cap);

            Label num = new Label();
            num.Text = APP_VER;
            num.ForeColor = UiKit.A(240, 244, 250);
            num.Font = new Font("Segoe UI", 21F, FontStyle.Bold);
            num.BackColor = Color.Transparent;
            num.Location = new Point(18, 34);
            num.AutoSize = true;
            ver.Controls.Add(num);

            Label logs = new Label();
            logs.Text = T("更新日志", "Changelog");
            logs.ForeColor = UiKit.A(78, 150, 240);
            logs.Font = new Font("Microsoft YaHei UI", 9F);
            logs.BackColor = Color.Transparent;
            logs.Location = new Point(462, 14);
            logs.AutoSize = true;
            logs.Cursor = Cursors.Hand;
            logs.Click += (s2, e2) => ShowInlineLog();
            ver.Controls.Add(logs);
            Label logsAr = new Label();
            logsAr.Text = "\uE8A7";
            logsAr.ForeColor = UiKit.A(78, 150, 240);
            logsAr.Font = new Font("Segoe MDL2 Assets", 8F);
            logsAr.BackColor = Color.Transparent;
            logsAr.Location = new Point(526, 15);
            logsAr.AutoSize = true;
            logsAr.Cursor = Cursors.Hand;
            logsAr.Click += (s2, e2) => ShowInlineLog();
            ver.Controls.Add(logsAr);

            Button btnCopy = new RoundBtn(UiKit.A(44, 52, 66), UiKit.A(226, 232, 240));
            btnCopy.Text = T("复制版本号", "Copy version");
            btnCopy.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnCopy.Location = new Point(462, 38);
            btnCopy.Size = new Size(118, 28);
            btnCopy.Click += (s2, e2) =>
            {
                try { Clipboard.SetText(T("小工具箱 v" + APP_VER, "Mini Toolbox v" + APP_VER)); } catch (Exception dg417_1) { Diag.Swallow("UpdateForm.cs:417", dg417_1); }
                btnCopy.Text = T("已复制", "Copied");
                System.Windows.Forms.Timer t1 = new System.Windows.Forms.Timer();
                t1.Interval = 1400;
                t1.Tick += (s3, e3) => { t1.Stop(); t1.Dispose(); try { btnCopy.Text = T("复制版本号", "Copy version"); } catch (Exception dg421_1) { Diag.Swallow("UpdateForm.cs:421", dg421_1); } };
                t1.Start();
            };
            ver.Controls.Add(btnCopy);

            Panel line = new Panel();
            line.BackColor = UiKit.A(44, 50, 62);
            line.Location = new Point(20, 86);
            line.Size = new Size(568, 1);
            ver.Controls.Add(line);

            stateDot = new Panel();
            stateDot.BackColor = Color.Transparent;
            stateDot.Location = new Point(22, 108);
            stateDot.Size = new Size(13, 13);
            stateDot.Paint += (s2, e2) =>
            {
                Graphics g = e2.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(UiKit.A(72, 220, 120), 2f))
                {
                    Rectangle rc = new Rectangle(1, 1, 10, 10);
                    g.DrawEllipse(pen, rc);
                }
                using (Pen pen = new Pen(UiKit.A(72, 220, 120), 1.6f))
                    g.DrawLines(pen, new Point[] { new Point(4, 6), new Point(6, 8), new Point(9, 4) });
            };
            ver.Controls.Add(stateDot);

            lblState = new Label();
            lblState.Text = T("当前已是最新版本", "You are up to date");
            lblState.ForeColor = UiKit.A(72, 208, 130);
            lblState.Font = new Font("Microsoft YaHei UI", 9F);
            lblState.BackColor = Color.Transparent;
            lblState.Location = new Point(44, 104);
            lblState.AutoSize = true;
            ver.Controls.Add(lblState);

            btnCheck = new RoundBtn(UiKit.A(44, 52, 66), UiKit.A(226, 232, 240));
            btnCheck.Text = T("检查更新", "Check for updates");
            btnCheck.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnCheck.Location = new Point(462, 96);
            btnCheck.Size = new Size(122, 28);
            btnCheck.Click += (s2, e2) => CheckUpdate();
            ver.Controls.Add(btnCheck);

            // ---- 四张入口卡片（v19.6 加官网）----
            Controls.Add(MakeLinkCard(T("官方网站", "Website"), T("截图与更新日志", "Shots & changelog"), "\uE774",
                16, 422, 146, 76, (s2, e2) => { try { Process.Start("https://wj5666619.github.io/MiniToolbox/"); } catch (Exception dg469_1) { Diag.Swallow("UpdateForm.cs:469", dg469_1); } }));
            Controls.Add(MakeLinkCard(T("打开目录", "Open folder"), T("程序文件与配置", "Program files"), "\uE8B7",
                168, 422, 146, 76, (s2, e2) => { try { Process.Start("explorer.exe", Path.GetDirectoryName(Application.ExecutablePath)); } catch (Exception dg471_1) { Diag.Swallow("UpdateForm.cs:471", dg471_1); } }));
            Controls.Add(MakeLinkCard(T("源码与编译", "Source & build"), T("csc 编译说明", "Single-file csc"), "\uE943",
                320, 422, 146, 76, (s2, e2) => ShowSourceInfo()));
            Controls.Add(MakeLinkCard(T("反馈与建议", "Feedback"), T("诊断包发到我的邮箱", "Email diag bundle"), "\uE715",
                472, 422, 146, 76, (s2, e2) => ExportInfo()));

            Label srcLbl = new Label();
            srcLbl.ForeColor = UiKit.Paper ? UiKit.Ink : UiKit.A(112, 124, 142);
            srcLbl.Font = new Font("Microsoft YaHei UI", 8F);
            srcLbl.BackColor = Color.Transparent;
            srcLbl.Location = new Point(18, 508);
            srcLbl.Size = new Size(520, 18);
            srcLbl.AutoEllipsis = true;
            Controls.Add(srcLbl);
            UpdateSourceLine(srcLbl);

            Label srcSet = new Label();
            srcSet.Text = T("换源", "Change");
            srcSet.ForeColor = UiKit.A(78, 150, 240);
            srcSet.Font = new Font("Microsoft YaHei UI", 8F);
            srcSet.BackColor = Color.Transparent;
            srcSet.Location = new Point(560, 508);
            srcSet.AutoSize = true;
            srcSet.Cursor = Cursors.Hand;
            srcSet.Click += (s2, e2) => { ChangeSource(); UpdateSourceLine(srcLbl); };
            Controls.Add(srcSet);

            Label foot = new Label();
            foot.Text = T("小工具箱 v" + APP_VER + " · 单文件绿软 · 无需安装即可运行 · 自签名证书",
                "Mini Toolbox v" + APP_VER + " · portable · self-signed");
            foot.ForeColor = UiKit.Paper ? UiKit.Ink : UiKit.A(96, 106, 122);
            foot.Font = new Font("Microsoft YaHei UI", 7.5F);
            foot.BackColor = Color.Transparent;
            foot.Location = new Point(18, 534);
            foot.AutoSize = true;
            Controls.Add(foot);

            Label note = new Label();
            note.Text = T("更新方式：用新版程序覆盖安装，原有配置会自动保留。源码与构建说明见项目主页。",
                "To update: run the newer build over this one; your config is kept.");
            note.ForeColor = UiKit.Paper ? UiKit.Ink : UiKit.A(120, 130, 146);
            note.Font = new Font("Microsoft YaHei UI", 8.5F);
            note.BackColor = Color.Transparent;
            note.Location = new Point(18, 556);
            note.AutoSize = true;
            Controls.Add(note);
            KeyDown += (s2, e2) =>
            {
                if (e2.KeyCode != Keys.Escape) return;
                if (logHost != null) { HideInlineLog(); e2.Handled = true; return; }
                Close();
            };
        }

        void UpdateSourceLine(Label lb)
        {
            try
            {
                string[] list = UpdateSourceList();
                string first = list.Length > 0 ? list[0] : "";
                string host = first;
                int mk = first.IndexOf("://");
                if (mk > 0) host = first.Substring(mk + 3);
                if (host.Length > 46) host = host.Substring(0, 44) + "…";
                lb.Text = T("更新源：" + host + (list.Length > 1 ? "  （另有 " + (list.Length - 1) + " 个备用）" : ""),
                            "Source: " + host);
            }
            catch (Exception dg538_1) { Diag.Swallow("UpdateForm.cs:538", dg538_1); }
        }

        void ChangeSource()
        {
            using (Form d = new Form())
            {
                d.FormBorderStyle = FormBorderStyle.FixedDialog;
                d.Text = T("更新源", "Update source");
                d.StartPosition = FormStartPosition.CenterParent;
                d.ClientSize = new Size(520, 190);
                d.BackColor = UiKit.A(22, 26, 34);
                d.ShowInTaskbar = false;
                d.Font = new Font("Microsoft YaHei UI", 9F);

                Label cap = new Label();
                cap.Text = T("填清单地址（update.json），一行一个，第一行为主源；也可以选本地文件。",
                    "One manifest URL per line; the first is primary. A local file works too.");
                cap.ForeColor = UiKit.A(150, 162, 178);
                cap.Font = new Font("Microsoft YaHei UI", 8.5F);
                cap.Location = new Point(16, 14);
                cap.Size = new Size(488, 34);
                d.Controls.Add(cap);

                TextBox tb = new TextBox();
                tb.Multiline = true;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.BackColor = UiKit.A(30, 35, 45);
                tb.ForeColor = UiKit.Txt(Color.White);
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.Font = new Font("Consolas", 9F);
                tb.Location = new Point(16, 56);
                tb.Size = new Size(488, 76);
                try
                {
                    string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "update_url.txt");
                    tb.Text = File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8).Trim() : DEFAULT_UPDATE_URL;
                }
                catch { tb.Text = DEFAULT_UPDATE_URL; }
                d.Controls.Add(tb);

                Button pick = new RoundBtn(UiKit.A(44, 52, 66), UiKit.A(226, 232, 240));
                pick.Text = T("选本地清单", "Pick local");
                pick.Font = new Font("Microsoft YaHei UI", 8.5F);
                pick.Location = new Point(16, 144);
                pick.Size = new Size(104, 30);
                pick.Click += (s2, e2) =>
                {
                    using (OpenFileDialog of = new OpenFileDialog())
                    {
                        of.Filter = "update.json|*.json|*.*|*.*";
                        if (of.ShowDialog(d) == DialogResult.OK) tb.Text = of.FileName;
                    }
                };
                d.Controls.Add(pick);

                Button rst = new RoundBtn(UiKit.A(44, 52, 66), UiKit.A(226, 232, 240));
                rst.Text = T("恢复默认", "Default");
                rst.Font = new Font("Microsoft YaHei UI", 8.5F);
                rst.Location = new Point(130, 144);
                rst.Size = new Size(96, 30);
                rst.Click += (s2, e2) => { tb.Text = DEFAULT_UPDATE_URL; };
                d.Controls.Add(rst);

                Button ok = new RoundBtn(UiKit.A(60, 110, 190), Color.White);
                ok.Text = T("保存", "Save");
                ok.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                ok.Location = new Point(404, 144);
                ok.Size = new Size(100, 30);
                ok.DialogResult = DialogResult.OK;
                d.Controls.Add(ok);

                Button no = new RoundBtn(UiKit.A(44, 52, 66), UiKit.A(226, 232, 240));
                no.Text = T("取消", "Cancel");
                no.Font = new Font("Microsoft YaHei UI", 8.5F);
                no.Location = new Point(300, 144);
                no.Size = new Size(100, 30);
                no.DialogResult = DialogResult.Cancel;
                d.Controls.Add(no);

                d.AcceptButton = ok;
                d.CancelButton = no;

                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "update_url.txt");
                    string txt = tb.Text.Replace("\r\n", "\n").Trim();
                    if (txt.Length == 0) txt = DEFAULT_UPDATE_URL;
                    File.WriteAllText(p, txt + "\n", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, T("写入 update_url.txt 失败：" + ex.Message, "Cannot write update_url.txt: " + ex.Message),
                        T("更新源", "Source"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void CheckUpdate()
        {
            // v19.9：企业策略「禁用在线更新」时不起网络请求
            if (Policy.NoUpdate)
            {
                btnCheck.Enabled = true;
                btnCheck.Text = T("检查更新", "Check for updates");
                lblState.ForeColor = UiKit.A(250, 190, 90);
                lblState.Text = T("管理员策略已禁用在线更新。", "Online updates are disabled by policy.");
                Diag.Warn("策略禁用在线更新，已跳过检查");
                return;
            }
            btnCheck.Enabled = false;
            btnCheck.Text = T("检查中…", "Checking...");
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string inst = Path.Combine(dir, "小工具箱_安装程序.exe");
            Application.DoEvents();

            // 一、优先走「在线更新源」：同目录 update_url.txt 里填 update.json 的地址（网址或本地路径都行）
            string src = UpdateSourceUrl();
            string fetchErr = "";
            if (src.Length > 0)
            {
                string manifest = "";
                string usedUrl = "", srcLog = "";
                manifest = FetchManifestAny(out usedUrl, out srcLog);
                fetchErr = manifest.Length > 0 ? "" : srcLog;
                if (manifest.Length > 0)
                {
                    string ver = "", url = "", notes = "", expectHash = "";
                    long expectSize = 0;
                    try
                    {
                        JavaScriptSerializer js = new JavaScriptSerializer();
                        Dictionary<string, object> o = js.Deserialize<Dictionary<string, object>>(manifest);
                        if (o != null)
                        {
                            if (o.ContainsKey("version")) ver = Convert.ToString(o["version"]);
                            if (o.ContainsKey("url")) url = Convert.ToString(o["url"]);
                            if (o.ContainsKey("notes"))
                            {
                                object nv = o["notes"];
                                object[] arr = nv as object[];
                                if (arr != null)
                                {
                                    StringBuilder nb = new StringBuilder();
                                    for (int i = 0; i < arr.Length; i++)
                                    {
                                        if (i > 0) nb.Append("\n");
                                        nb.Append("· " + Convert.ToString(arr[i]));
                                    }
                                    notes = nb.ToString();
                                }
                                else notes = Convert.ToString(nv);
                                if (notes.Length > 1200) notes = notes.Substring(0, 1200) + "…";
                            }
                            if (o.ContainsKey("size")) long.TryParse(Convert.ToString(o["size"]), out expectSize);
                            if (o.ContainsKey("sha256")) expectHash = Convert.ToString(o["sha256"]).Trim().ToLowerInvariant();
                        }
                    }
                    catch (Exception dg687_1) { Diag.Swallow("UpdateForm.cs:687", dg687_1); }
                    int cmp = CompareVersion(ver, APP_VER);
                    if (cmp > 0 && url.Length > 0)
                    {
                        lblState.Text = T("发现新版本 v" + ver, "New version v" + ver + " available");
                        lblState.ForeColor = UiKit.A(250, 190, 90);
                        stateDot.BackColor = UiKit.A(250, 190, 90);
                        stateDot.Invalidate();
                        btnCheck.Text = T("检查更新", "Check for updates");
                        btnCheck.Enabled = true;
                        if (autoYes || MessageBox.Show(this,
                            T("发现新版本 v" + ver + "\n\n" + (notes.Length > 0 ? notes + "\n\n" : "") + "现在下载并在软件内完成更新？（下载完会自动替换并重启）",
                              "New version v" + ver + " found.\n\n" + notes + "\n\nDownload and update in-app now?"),
                            T("软件更新", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            DownloadAndApply(url, expectSize, ver, expectHash);
                        }
                        return;
                    }
                    lblState.Text = T("在线源：当前已是最新版本", "Online: up to date");
                    lblState.ForeColor = UiKit.A(72, 208, 130);
                    stateDot.BackColor = UiKit.A(72, 208, 130);
                    stateDot.Invalidate();
                    btnCheck.Text = T("检查更新", "Check for updates");
                    btnCheck.Enabled = true;
                    return;
                }
            }

            // 二、没有在线源时，退回「同目录安装包比时间戳」的老办法
            try
            {
                if (File.Exists(inst))
                {
                    DateTime mine = File.GetLastWriteTime(Application.ExecutablePath);
                    DateTime theirs = File.GetLastWriteTime(inst);
                    if (theirs > mine)
                    {
                        lblState.Text = T("发现更新的安装包，可覆盖安装", "Newer installer found");
                        lblState.ForeColor = UiKit.A(250, 190, 90);
                        stateDot.BackColor = UiKit.A(250, 190, 90);
                        stateDot.Invalidate();
                        if (MessageBox.Show(this, T("检测到同目录的安装程序比当前程序新。\n\n现在打开它进行覆盖安装？", "A newer installer was found next to the app.\n\nRun it now?"),
                            T("检查更新", "Check"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            try { Process.Start(inst); Close(); } catch (Exception dg732_1) { Diag.Swallow("UpdateForm.cs:732", dg732_1); }
                        }
                    }
                    else
                    {
                        lblState.Text = T("当前已是最新版本", "You are up to date");
                        lblState.ForeColor = UiKit.A(72, 208, 130);
                    }
                }
                else
                {
                    lblState.Text = T("当前已是最新版本（未配置在线更新源）", "Up to date (no online source configured)");
                    lblState.ForeColor = UiKit.A(72, 208, 130);
                }
            }
            catch { lblState.Text = T("检查失败", "Check failed"); }
            if (src.Length > 0 && fetchErr.Length > 0)
            {
                string shortErr = fetchErr;
                if (shortErr.Length > 70) shortErr = shortErr.Substring(0, 70) + "…";
                lblState.Text = T("更新源全部读取失败，明细见提示框", "All update sources failed");
                lblState.ForeColor = UiKit.A(255, 120, 120);
                stateDot.BackColor = UiKit.A(255, 120, 120);
                stateDot.Invalidate();
                HandleUpdateFail(T("读不到更新清单（" + shortErr + "）", "Cannot read the update manifest"),
                    fetchErr, "");
            }
            btnCheck.Text = T("检查更新", "Check for updates");
            btnCheck.Enabled = true;
        }

        // 更新源配置：exe 同目录 update_url.txt（内容就是 update.json 的地址，支持 http/https/本地路径）
        // 文件不存在时自动补一份默认源，保证全新安装后开箱就能在线更新
        static readonly string DEFAULT_UPDATE_URL = "https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/update.json";

        public static void SelfTest(string outPath)
        {
            StringBuilder sb = new StringBuilder();
            string src = UpdateSourceUrl();
            sb.AppendLine("src=" + src);
            string used = "", log = "";
            string m = "";
            try { m = FetchManifestAny(out used, out log); }
            catch (Exception ex) { sb.AppendLine("EX=" + ex.Message); }
            sb.AppendLine("used=" + used);
            sb.AppendLine("log=" + log);
            sb.AppendLine("len=" + m.Length);
            if (m.Length > 0)
            {
                sb.AppendLine("--- manifest ---");
                sb.AppendLine(m);
                string ver = "";
                try
                {
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    Dictionary<string, object> o = js.Deserialize<Dictionary<string, object>>(m);
                    if (o != null && o.ContainsKey("version")) ver = Convert.ToString(o["version"]);
                    sb.AppendLine("ver=" + ver + " cmp_vs_" + APP_VER + "=" + CompareVersion(ver, APP_VER));
                    sb.AppendLine("has_url=" + (o != null && o.ContainsKey("url")));
                    sb.AppendLine("keys=" + (o == null ? "" : string.Join(",", new List<string>(o.Keys).ToArray())));
                }
                catch (Exception ex) { sb.AppendLine("PARSE_EX=" + ex.Message); }
            }
            try { File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg795_1) { Diag.Swallow("UpdateForm.cs:795", dg795_1); }
        }

        // update_url.txt 允许写多行：第一行是主源，其余当备用（比如再填一条镜像或本地清单）
        static string[] UpdateSourceList()
        {
            string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "update_url.txt");
            try
            {
                if (File.Exists(p))
                {
                    string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                    List<string> ok = new List<string>();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = (lines[i] ?? "").Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        ok.Add(t);
                    }
                    if (ok.Count > 0) return ok.ToArray();
                }
            }
            catch (Exception dg817_1) { Diag.Swallow("UpdateForm.cs:817", dg817_1); }
            return new string[] { DEFAULT_UPDATE_URL };
        }

        // v18.6 后台静默检查：只读清单比对版本，不下载、不弹窗，发现新版交给调用方做轻提示
        public static string QuietCheck()
        {
            if (Policy.NoUpdate) return "";
            try
            {
                NetSetup();
                string used = "", lg = "";
                string m = FetchManifestAny(out used, out lg);
                if (m == null || m.Trim().Length == 0) return "";
                System.Collections.Generic.Dictionary<string, object> o = null;
                try
                {
                    System.Web.Script.Serialization.JavaScriptSerializer js =
                        new System.Web.Script.Serialization.JavaScriptSerializer();
                    o = js.DeserializeObject(m) as System.Collections.Generic.Dictionary<string, object>;
                }
                catch (Exception dg837_1) { Diag.Swallow("UpdateForm.cs:837", dg837_1); }
                if (o == null) return "";
                object v = null;
                if (!o.TryGetValue("version", out v) || v == null) return "";
                string ver = v.ToString().Trim();
                if (ver.Length == 0) return "";
                if (ParseVer(ver) > ParseVer(UiKit.VER)) return ver;
            }
            catch (Exception dg845_1) { Diag.Swallow("UpdateForm.cs:845", dg845_1); }
            return "";
        }

        // "18.6" → 1806，便于比较；多点版本也只取前两段
        static double ParseVer(string s)
        {
            try
            {
                string[] a = s.Replace("v", "").Replace("V", "").Trim().Split('.');
                double major = double.Parse(a[0]);
                double minor = a.Length > 1 ? double.Parse(new string(a[1].ToCharArray()).Trim()) : 0;
                return major * 100 + minor;
            }
            catch { return 0; }
        }

        static string FetchManifestAny(out string used, out string log)
        {
            used = "";
            StringBuilder all = new StringBuilder();
            string[] srcs = UpdateSourceList();
            for (int i = 0; i < srcs.Length; i++)
            {
                string u = "", l = "";
                string m = FetchTextAny(srcs[i], out u, out l);
                if (m.Length > 0) { used = u; log = ""; return m; }
                all.Append("【源" + (i + 1) + "】" + srcs[i] + "\n" + l + "\n");
            }
            log = all.ToString();
            return "";
        }

        static string UpdateSourceUrl()
        {
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "update_url.txt");
                if (File.Exists(p))
                {
                    string u = File.ReadAllText(p, Encoding.UTF8).Trim();
                    if (u.Length > 0) return u;
                }
                try { File.WriteAllText(p, DEFAULT_UPDATE_URL, new UTF8Encoding(false)); }
                catch (Exception dg889_1) { Diag.Swallow("UpdateForm.cs:889", dg889_1); }
                return DEFAULT_UPDATE_URL;
            }
            catch (Exception dg892_1) { Diag.Swallow("UpdateForm.cs:892", dg892_1); }
            return "";
        }

        // GitHub raw / Releases 只接受 TLS1.2+，.NET 老版本默认还是 SSL3+TLS1.0，必须先打开
        static void NetSetup()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(0xF00 | 0xC00); }
            catch (Exception dg900_1) { Diag.Swallow("UpdateForm.cs:900", dg900_1); }
            try { ServicePointManager.Expect100Continue = false; }
            catch (Exception dg902_1) { Diag.Swallow("UpdateForm.cs:902", dg902_1); }
        }

        // 更新源候选：直连之外再挂两个国内常用镜像（jsDelivr / ghproxy），单源被墙或被重置时自动换下一个
        static string[] UpdateCandidates(string url)
        {
            List<string> list = new List<string>();
            if (url == null) return list.ToArray();
            url = url.Trim();
            if (url.Length == 0) return list.ToArray();
            if (File.Exists(url)) { list.Add(url); return list.ToArray(); }
            try
            {
                if (url.IndexOf("raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string rest = url.Substring(url.IndexOf("raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase) + 26);
                    rest = rest.TrimEnd('/');
                    string[] seg = rest.Split('/');
                    if (seg.Length >= 4)
                    {
                        string user = seg[0], repo = seg[1], branch = seg[2];
                        StringBuilder tail = new StringBuilder();
                        for (int i = 3; i < seg.Length; i++) { if (i > 3) tail.Append('/'); tail.Append(seg[i]); }
                        // 国内常用镜像先走，直连与 jsDelivr 垫后（GitHub raw 在大陆经常被重置）
                        // 直连最快（通的时候 0.5s 回），被墙时按顺序降级到 jsDelivr 与国内镜像
                        list.Add(url);
                        list.Add("https://cdn.jsdelivr.net/gh/" + user + "/" + repo + "@" + branch + "/" + tail);
                        list.Add("https://gh-proxy.com/" + url);
                        list.Add("https://raw.gitmirror.com/" + user + "/" + repo + "/" + branch + "/" + tail);
                        list.Add("https://ghfast.top/" + url);
                        list.Add("https://ghproxy.net/" + url);
                        list.Add("https://gh.llkk.cc/" + url);
                        list.Add("https://github.moeyy.xyz/" + url);
                        list.Add("https://fastly.jsdelivr.net/gh/" + user + "/" + repo + "@" + branch + "/" + tail);
                    }
                }
                else if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(url);
                    list.Add("https://ghproxy.net/" + url);
                    list.Add("https://gh-proxy.com/" + url);
                }
                else list.Add(url);
            }
            catch { list.Add(url); }
            // 去重
            List<string> uniq = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                bool dup = false;
                for (int k = 0; k < uniq.Count; k++) if (uniq[k] == list[i]) { dup = true; break; }
                if (!dup && list[i].Trim().Length > 0) uniq.Add(list[i]);
            }
            return uniq.ToArray();
        }

        // 依次尝试候选源，返回第一份读到的文本；全部失败返回空，并把每个源的失败原因记进 log
        static string FetchTextAny(string url, out string used, out string log)
        {
            used = "";
            log = "";
            StringBuilder lg = new StringBuilder();
            string[] cands = UpdateCandidates(url);
            int[] tmo = new int[] { 4500, 9000 };
            for (int round = 0; round < tmo.Length; round++)
            {
                for (int i = 0; i < cands.Length; i++)
                {
                    try
                    {
                        string t = FetchText(cands[i], tmo[round]);
                        if (t != null && t.Trim().Length > 0) { used = cands[i]; return t; }
                        if (round == tmo.Length - 1) lg.Append((i + 1) + ". " + cands[i] + " → 空内容；");
                    }
                    catch (Exception ex)
                    {
                        if (round == tmo.Length - 1)
                        {
                            string m = ex.Message;
                            Exception ie = ex.InnerException;
                            if (ie != null) m += " / " + ie.Message;
                            lg.Append((i + 1) + ". " + cands[i] + " → " + m + "；");
                        }
                    }
                }
            }
            log = lg.ToString();
            return "";
        }

        // 更新包候选：一个 404 的下载直链往往是「资产名对不上」，这里按常见资产名 + Release 版本号各试一遍
        static string[] DownloadCandidates(string url, string ver)
        {
            List<string> list = new List<string>();
            if (url == null) return list.ToArray();
            url = url.Trim();
            if (url.Length == 0) return list.ToArray();
            if (File.Exists(url)) { list.Add(url); return list.ToArray(); }
            list.Add(url);
            string[] names = new string[] { "default.exe", "MiniToolbox.exe", "小工具箱.exe" };
            try
            {
                int mk = url.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
                if (mk > 0)
                {
                    string repoHead = url.Substring(0, mk + 11);          // https://github.com/
                    string rest = url.Substring(mk + 11);                  // user/repo/releases/...
                    string[] seg = rest.Split('/');
                    if (seg.Length >= 2)
                    {
                        string owner = seg[0], repo = seg[1];
                        string baseUrl2 = repoHead + owner + "/" + repo;
                        if (ver != null && ver.Trim().Length > 0)
                        {
                            string tag = ver.Trim();
                            for (int i = 0; i < names.Length; i++)
                                list.Add(baseUrl2 + "/releases/download/" + tag + "/" + names[i]);
                        }
                        if (rest.IndexOf("releases/latest/download/", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            for (int i = 0; i < names.Length; i++)
                                list.Add(baseUrl2 + "/releases/latest/download/" + names[i]);
                        }
                    }
                }
            }
            catch (Exception dg1028_1) { Diag.Swallow("UpdateForm.cs:1028", dg1028_1); }
            // 全部再挂一层代理源，绕开直连被重置
            int n0 = list.Count;
            for (int i = 0; i < n0; i++)
            {
                if (list[i].StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add("https://ghproxy.net/" + list[i]);
                    list.Add("https://gh-proxy.com/" + list[i]);
                }
            }
            List<string> uniq = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                bool dup = false;
                for (int k = 0; k < uniq.Count; k++) if (uniq[k] == list[i]) { dup = true; break; }
                if (!dup && list[i].Trim().Length > 0) uniq.Add(list[i]);
            }
            if (uniq.Count > 14) uniq.RemoveRange(14, uniq.Count - 14);
            return uniq.ToArray();
        }

        // 自测探针：验证候选源生成、在线清单读取、失败日志（不下载、不替换）
        public static string UpdProbe()
        {
            StringBuilder sb = new StringBuilder();
            string raw = "https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/update.json";
            string[] c = UpdateCandidates(raw);
            sb.AppendLine("cands=" + c.Length);
            for (int i = 0; i < c.Length; i++) sb.AppendLine("  " + (i + 1) + " " + c[i]);
            string used = "", log = "";
            string t = FetchTextAny(raw, out used, out log);
            sb.AppendLine("live_ok=" + (t.Length > 0) + " used=" + (used.Length > 0 ? used : "-"));
            sb.AppendLine("live_has_version=" + t.Contains("\"version\""));
            string used2 = "", log2 = "";
            string t2 = FetchTextAny("https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/no_such_file_xyz.json", out used2, out log2);
            sb.AppendLine("bogus_ok=" + (t2.Length > 0));
            sb.AppendLine("bogus_log_len=" + log2.Length);
            if (log2.Length > 0)
            {
                string s = log2.Replace("\r", " ").Replace("\n", " ");
                sb.AppendLine("bogus_log=" + (s.Length > 200 ? s.Substring(0, 200) + "…" : s));
            }
            string murl = "https://github.com/wj5666619/MiniToolbox/releases/latest/download/MiniToolbox.exe";
            string[] dc = DownloadCandidates(murl, "6.9");
            sb.AppendLine("dl_cands=" + dc.Length);
            for (int i = 0; i < dc.Length; i++) sb.AppendLine("  d" + (i + 1) + " " + dc[i]);
            return sb.ToString();
        }

        static string FetchText(string url) { return FetchText(url, 6000); }

        static string FetchText(string url, int timeoutMs)
        {
            if (File.Exists(url)) return File.ReadAllText(url, Encoding.UTF8);
            NetSetup();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "MiniToolboxUpdater/1.0";
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 10;
            try { req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
            catch (Exception dg1091_1) { Diag.Swallow("UpdateForm.cs:1091", dg1091_1); }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        static int CompareVersion(string a, string b)
        {
            try
            {
                string[] x = (a ?? "0").Split('.'), y = (b ?? "0").Split('.');
                for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
                {
                    int p = i < x.Length ? ParseInt(x[i]) : 0;
                    int q = i < y.Length ? ParseInt(y[i]) : 0;
                    if (p != q) return p > q ? 1 : -1;
                }
                return 0;
            }
            catch { return 0; }
        }

        static int ParseInt(string s)
        {
            int v = 0;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) sb.Append(s[i]);
            int.TryParse(sb.ToString(), out v);
            return v;
        }

        // 真下载：流式写到临时目录，带进度；下载完写一个 PowerShell 脚本，等本程序退出后替换 exe 并重启
        void DownloadAndApply(string url, long expectSize, string ver, string expectHash)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "小工具箱_新版本.exe");
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception dg1132_1) { Diag.Swallow("UpdateForm.cs:1132", dg1132_1); }

            if (File.Exists(url))
            {
                try { File.Copy(url, tmp, true); } catch (Exception ex) { MessageBox.Show(this, "更新文件复制失败：" + ex.Message); return; }
                ApplyUpdate(tmp, expectSize);
                return;
            }

            btnCheck.Enabled = false;
            lblState.ForeColor = UiKit.A(250, 190, 90);
            string[] cands = DownloadCandidates(url, ver);
            Diag.Info("开始更新 v" + ver + "，期望 " + expectSize + " 字节，候选源 " + cands.Length + " 个");
            StringBuilder tried = new StringBuilder();
            string lastErr = "";
            for (int i = 0; i < cands.Length; i++)
            {
                for (int tryIdx = 0; tryIdx < 2; tryIdx++)
                {
                    try
                    {
                        lblState.Text = "正在下载更新…（第 " + (i + 1) + "/" + cands.Length + " 个源）";
                        Application.DoEvents();
                        using (WebClient wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", "MiniToolboxUpdater/1.0");
                            wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e)
                            {
                                lblState.Text = "正在下载更新… " + (e.ProgressPercentage > 0 ? e.ProgressPercentage + "%" : (e.BytesReceived / 1024) + " KB");
                                try { Application.DoEvents(); } catch (Exception dg1161_1) { Diag.Swallow("UpdateForm.cs:1161", dg1161_1); }
                            };
                            wc.DownloadFile(new Uri(cands[i]), tmp);
                        }
                        if (File.Exists(tmp) && new FileInfo(tmp).Length >= 100 * 1024)
                        {
                            // v18.6：清单里带 sha256 时先校验再替换；不带就退回旧的大小校验
                            string gotHash = "";
                            if (!HashMatch(tmp, expectHash, out gotHash))
                            {
                                lastErr = "SHA256 校验不通过（实际 " + gotHash + "）";
                                Diag.Warn("更新包校验失败：" + cands[i] + " 实际 " + gotHash + " 期望 " + expectHash);
                                tried.Append((i + 1) + ". " + cands[i] + " → 校验不通过；");
                                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception dg1174_1) { Diag.Swallow("UpdateForm.cs:1174", dg1174_1); }
                                continue;
                            }
                            ApplyUpdate(tmp, expectSize);
                            return;
                        }
                        lastErr = "下载到的文件不完整";
                        tried.Append((i + 1) + ". " + cands[i] + " → 文件不完整；");
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex.Message;
                        Diag.Warn("更新下载失败：" + cands[i] + " → " + ex.Message);
                        tried.Append((i + 1) + ". " + cands[i] + " → " + ex.Message + "；");
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception dg1188_1) { Diag.Swallow("UpdateForm.cs:1188", dg1188_1); }
                        if (tryIdx == 0) { try { Thread.Sleep(600); } catch (Exception dg1189_1) { Diag.Swallow("UpdateForm.cs:1189", dg1189_1); } }
                    }
                }
            }
            lblState.ForeColor = UiKit.A(255, 120, 120);
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string inst = Path.Combine(dir, "小工具箱_安装程序.exe");
                if (File.Exists(inst))
                {
                    if (MessageBox.Show(this, T("在线下载失败（" + lastErr + "）。\n\n程序目录里有安装包，要用它覆盖安装到新版吗？",
                        "Online download failed (" + lastErr + ").\n\nA local installer is available — run it to update?"),
                        T("软件更新", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        try { Process.Start(inst); Close(); return; } catch (Exception dg1204_1) { Diag.Swallow("UpdateForm.cs:1204", dg1204_1); }
                    }
                }
                HandleUpdateFail("下载失败：" + lastErr + "\n（试过的源：" + cands.Length + " 个）", tried.ToString(), ver);
            }
            catch (Exception dg1209_1) { Diag.Swallow("UpdateForm.cs:1209", dg1209_1); }
            lblState.Text = T("下载失败，可换本地安装包更新", "Download failed — use a local installer");
            btnCheck.Enabled = true;
        }

        // 更新失败的统一出口：给出可诊断信息 + 两条退路（选本地安装包 / 打开发布页）
        void HandleUpdateFail(string headline, string detail, string ver)
        {
            StringBuilder msg = new StringBuilder();
            msg.AppendLine(headline);
            msg.AppendLine("");
            if (detail != null && detail.Trim().Length > 0)
            {
                string d = detail.Trim();
                if (d.Length > 900) d = d.Substring(0, 900) + "\n…";
                msg.AppendLine(d);
                msg.AppendLine("");
            }
            msg.AppendLine("常见原因：仓库里还没发布这个版本的安装包（Release 不存在会一直 404），或当前网络连不上 GitHub。");
            msg.AppendLine("");
            msg.AppendLine("点「是」= 选一个本地安装包（或新版主程序）直接更新");
            msg.AppendLine("点「否」= 打开仓库发布页，手动下载后再覆盖安装");
            msg.AppendLine("点「取消」= 先不更新");
            DialogResult dr = MessageBox.Show(this, msg.ToString(), T("软件更新", "Update"),
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (dr == DialogResult.Yes) LocalUpdate();
            else if (dr == DialogResult.No)
            {
                try
                {
                    string u = "https://github.com/wj5666619/MiniToolbox/releases";
                    try
                    {
                        string baseUrl = UpdateSourceUrl();
                        int i = baseUrl.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
                        if (i > 0)
                        {
                            string repo = baseUrl.Substring(i);
                            repo = repo.Replace("/tree/", "/").Replace("/blob/", "/");
                            int cut = repo.IndexOf("/main", StringComparison.OrdinalIgnoreCase);
                            if (cut > 0) repo = repo.Substring(0, cut);
                            u = "https://" + repo + "/releases";
                        }
                    }
                    catch (Exception dg1253_1) { Diag.Swallow("UpdateForm.cs:1253", dg1253_1); }
                    Process.Start(u);
                }
                catch (Exception dg1256_1) { Diag.Swallow("UpdateForm.cs:1256", dg1256_1); }
            }
        }

        // 本地更新：挑一个新版主程序或安装包，直接覆盖
        void LocalUpdate()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = T("选择新版主程序或安装包", "Pick a new build or installer");
            ofd.Filter = T("程序或安装包|*.exe|所有文件|*.*", "Programs|*.exe|All files|*.*");
            ofd.InitialDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            string f = ofd.FileName;
            if (string.Equals(f, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, T("这就是当前正在运行的程序，选它没有意义。", "That is the running copy itself."), T("软件更新", "Update"));
                return;
            }
            try
            {
                if (!File.Exists(f) || new FileInfo(f).Length < 100 * 1024)
                {
                    MessageBox.Show(this, T("这个文件太小，不像完整的程序或安装包。", "That file is too small to be a full build."), T("软件更新", "Update"));
                    return;
                }
                string tmp = Path.Combine(Path.GetTempPath(), "小工具箱_新版本.exe");
                if (File.Exists(tmp)) File.Delete(tmp);
                File.Copy(f, tmp, true);
                ApplyUpdate(tmp, 0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("本地更新失败：" + ex.Message, "Local update failed: " + ex.Message), T("软件更新", "Update"));
            }
        }

        // 清单 sha256 校验：清单没写（旧格式）时直接放行，保持向后兼容
        bool HashMatch(string file, string expect, out string got)
        {
            got = "";
            if (expect == null || expect.Trim().Length == 0) return true;
            try
            {
                using (FileStream fs = File.OpenRead(file))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] d = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < d.Length; i++) sb.Append(d[i].ToString("x2"));
                    got = sb.ToString();
                }
                return string.Equals(got, expect.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        void ApplyUpdate(string tmp, long expectSize)
        {
            Diag.Info("更新包就绪：" + tmp + "（" + new FileInfo(tmp).Length + " 字节），准备替换并重启");
            if (!File.Exists(tmp) || new FileInfo(tmp).Length < 100 * 1024)
            {
                lblState.Text = "更新包不完整，已中止。";
                lblState.ForeColor = UiKit.A(255, 120, 120);
                btnCheck.Enabled = true;
                HandleUpdateFail("下载到的更新包不完整（小于 100 KB），已中止。", "", "");
                return;
            }
            if (expectSize > 0 && new FileInfo(tmp).Length != expectSize)
            {
                lblState.Text = "更新包大小不符（" + new FileInfo(tmp).Length + " / " + expectSize + "），已中止。";
                lblState.ForeColor = UiKit.A(255, 120, 120);
                btnCheck.Enabled = true;
                HandleUpdateFail("更新包大小不符（实际 " + new FileInfo(tmp).Length + " 字节 / 清单写 " + expectSize + " 字节）。\n多半是仓库里的发布包还是旧版本，或资产名对不上。", "", "");
                return;
            }
            // v19.9：更新包与当前程序必须同一张证书；开发构建未签名时只记日志
            string signNote = "";
            if (!Sign.SameSigner(tmp, out signNote))
            {
                lblState.Text = "更新包验签失败，已中止。";
                lblState.ForeColor = UiKit.A(255, 120, 120);
                btnCheck.Enabled = true;
                Diag.Warn("更新包验签失败：" + signNote + " → " + tmp);
                Audit.Write("拒绝更新", signNote + " | " + tmp);
                HandleUpdateFail("更新包数字签名校验失败（" + signNote + "），已中止替换。", "", "");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception dgSignDel) { Diag.Swallow("UpdateForm.cs:signDel", dgSignDel); }
                return;
            }
            Diag.Info("更新包验签：" + signNote);
            Audit.Write("安装更新", signNote + " | " + new FileInfo(tmp).Length + " 字节 | " + tmp);
            string target = Application.ExecutablePath;
            string ps1 = Path.Combine(Path.GetTempPath(), "mtb_update.ps1");
            string body =
                "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
                "Start-Sleep -Seconds 2\r\n" +
                "$target = '" + target + "'\r\n" +
                "$tmp = '" + tmp + "'\r\n" +
                "$old = $target + '.old'\r\n" +
                "$prev = Join-Path (Split-Path -Parent $target) '上一版_小工具箱.exe'\r\n" +
                "$log = Join-Path $env:TEMP 'mtb_update.log'\r\n" +
                "function Log($m) { Add-Content -LiteralPath $log -Value ('[' + (Get-Date -Format 'HH:mm:ss') + '] ' + $m) -Encoding UTF8 }\r\n" +
                "Log '开始更新'\r\n" +
                "$ok = $false\r\n" +
                "for ($i = 0; $i -lt 40; $i++) {\r\n" +
                "  Get-Process -Name '小工具箱' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue\r\n" +
                "  Start-Sleep -Milliseconds 500\r\n" +
                "  try {\r\n" +
                "    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $old -Force -ErrorAction SilentlyContinue }\r\n" +
                "    Copy-Item -LiteralPath $tmp -Destination $target -Force -ErrorAction Stop\r\n" +
                "    $src = (Get-Item -LiteralPath $tmp).Length\r\n" +
                "    $dst = (Get-Item -LiteralPath $target).Length\r\n" +
                "    if ($src -ne $dst) { throw ('大小不一致 ' + $src + ' / ' + $dst) }\r\n" +
                "    $ok = $true\r\n" +
                "    Log ('替换成功 大小=' + $dst)\r\n" +
                "    break\r\n" +
                "  } catch {\r\n" +
                "    Log ('第' + ($i + 1) + ' 次尝试失败: ' + $_.Exception.Message)\r\n" +
                "    if (-not (Test-Path -LiteralPath $target)) { if (Test-Path -LiteralPath $old) { Move-Item -LiteralPath $old -Destination $target -Force -ErrorAction SilentlyContinue } }\r\n" +
                "    Start-Sleep -Milliseconds 400\r\n" +
                "  }\r\n" +
                "}\r\n" +
                "if ($ok) {\r\n" +
                "  Remove-Item -LiteralPath $prev -Force -ErrorAction SilentlyContinue\r\n" +
                "  Move-Item -LiteralPath $old -Destination $prev -Force -ErrorAction SilentlyContinue\r\n" +
                "  Start-Process -FilePath $target\r\n" +
                "  Log '已重启新版本'\r\n" +
                "} else {\r\n" +
                "  if ((Test-Path -LiteralPath $old) -and (-not (Test-Path -LiteralPath $target))) { Move-Item -LiteralPath $old -Destination $target -Force -ErrorAction SilentlyContinue }\r\n" +
                "  Start-Process -FilePath $target\r\n" +
                "  Log '更新失败，已回滚到原版本'\r\n" +
                "  try { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('自动更新失败，已回滚到原版本。' + [Environment]::NewLine + '日志: ' + $log, '小工具箱更新') } catch {}\r\n" +
                "}\r\n" +
                "Remove-Item -LiteralPath '" + ps1 + "' -Force -ErrorAction SilentlyContinue\r\n";
            try
            {
                File.WriteAllText(ps1, body, new UTF8Encoding(true));
                Process.Start(new ProcessStartInfo("powershell", "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"")
                {
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                lblState.Text = "下载完成，正在替换并重启…";
                lblState.ForeColor = UiKit.A(72, 208, 130);
                Application.DoEvents();
                Close();
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动更新脚本失败：" + ex.Message + "\n\n可手动把更新包复制到程序目录覆盖。", "软件更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnCheck.Enabled = true;
            }
        }

        void AddSec(System.Collections.Generic.List<string> hs, System.Collections.Generic.List<System.Collections.Generic.List<string>> bs, string head, string[] items)
        {
            System.Collections.Generic.List<string> b = new System.Collections.Generic.List<string>();
            for (int i = 0; i < items.Length; i++) b.Add(items[i]);
            hs.Add(head); bs.Add(b);
        }
        // v18.8：更新日志从"一大坨只读文本框"改成按版本分块的时间线卡片
        // 同一套时间线两种用法：inlineMode=false 单独开窗（/changelog、独立日志窗）；true 嵌进关于页
        Panel BuildLogPanel(bool inlineMode, EventHandler onBack)
        {
            Panel host = new Panel();
            host.Size = new Size(600, 566);
            host.BackColor = UiKit.A(20, 24, 31);
            host.Font = new Font("Microsoft YaHei UI", 9F);

            Panel bar = new Panel();
            bar.BackColor = UiKit.A(26, 30, 38);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(600, 44);
            host.Controls.Add(bar);

            Label ti = new Label();
            ti.Text = T("更新日志", "Changelog");
            ti.ForeColor = UiKit.A(232, 236, 242);
            ti.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            ti.BackColor = Color.Transparent;
            ti.Location = new Point(18, 12);
            ti.AutoSize = true;
            bar.Controls.Add(ti);

            Label chipV = new Label();
            chipV.Text = "v" + APP_VER;
            chipV.ForeColor = UiKit.A(120, 190, 255);
            chipV.BackColor = UiKit.A(26, 40, 62);
            chipV.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            chipV.TextAlign = ContentAlignment.MiddleCenter;
            chipV.Location = new Point(112, 13);
            chipV.Size = new Size(52, 20);
            bar.Controls.Add(chipV);

            Button btnX = new Button();
            btnX.Text = "✕";
            btnX.FlatStyle = FlatStyle.Standard;
            btnX.FlatAppearance.BorderSize = 0;
            btnX.BackColor = UiKit.A(26, 30, 38);
            btnX.ForeColor = UiKit.A(150, 158, 172);
            btnX.Font = new Font("Segoe UI", 10F);
            btnX.Location = new Point(558, 9);
            btnX.Size = new Size(32, 26);
            btnX.TabStop = false;
            btnX.Click += (s2, e2) => onBack(s2, e2);
            bar.Controls.Add(btnX);

            Point dp = Point.Empty;
            bar.MouseDown += (s2, e2) => { dp = e2.Location; };
            bar.MouseMove += (s2, e2) =>
            {
                if (inlineMode || e2.Button != MouseButtons.Left || dp == Point.Empty) return;
                Form fr = host.FindForm();
                if (fr != null) fr.Location = new Point(fr.Location.X + e2.X - dp.X, fr.Location.Y + e2.Y - dp.Y);
            };
            bar.MouseUp += (s2, e2) => { dp = Point.Empty; };

            Panel body = new Panel();
            body.Location = new Point(16, 56);
            body.Size = new Size(568, 438);
            body.BackColor = UiKit.A(24, 28, 35);
            body.AutoScroll = true;
            body.Paint += delegate (object s2, PaintEventArgs e2)
            {
                Graphics g = e2.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, body.Width - 1, body.Height - 1);
                using (GraphicsPath pa = UiKit.RoundedPath(r, 4))
                using (Pen bp = new Pen(UiKit.AA(50, 120, 180, 255), 1f))
                    g.DrawPath(bp, pa);
            };
            host.Controls.Add(body);

            // v18.8：最近的版本用维护好的条目，更早的从旧日志正文里拆（只取前几段）
            System.Collections.Generic.List<string> heads = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<System.Collections.Generic.List<string>> blocks = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            AddSec(heads, blocks, T("v18.9 · 关于页与更新日志重做", "v18.9 · About page & changelog"),
                new string[] {
                    T("「关于 · 软件更新」整版重做：品牌头（图标底板 / 名称 / 免费开源徽章 / 主张 / 三行简介）+ 右侧版本小卡", "About window rebuilt: brand header, version card"),
                    T("六格功能胶囊可直接点击：定时关机 / C盘清理 / 视频下载 / 音乐下载 / 去水印 / Steam 成就，点了关窗并跳到该工具页", "Feature tiles are clickable and jump to the tool page"),
                    T("版本卡：当前版本大字 + 更新日志 + 复制版本号 + 检查更新 + 状态行；窗口 640×596", "Version card with state row"),
                    T("更新日志改为版本时间线：左侧强调条 + 圆点条目，新版本在前，只保留最近 10 个版本；「更新日志」入口不再弹新窗，改成在关于页内嵌展开，返回即回关于页", "Changelog is a version timeline now, shown inline in the About window")
                    ,T("新增 /about 与 /changelog 调试参数，启动 3 秒自动打开对应窗口", "New /about and /changelog switches")
                });
            AddSec(heads, blocks, T("v18.7 · 纸感手绘皮肤", "v18.7 · Paper skin"),
                new string[] {
                    T("第 5 套皮肤「纸感手绘」：米白方格纸底（26px 网格 + 底部暖褐渐变），皮肤面板可切换", "5th skin: paper sketch"),
                    T("页头与侧栏做成墨黑立柱，页头下沿 / 侧栏右沿各压一道 3px 荧光黄", "Ink header and sidebar with neon rules"),
                    T("卡片改为墨块填充 + 纸白细描边 + 4px 硬阴影，按钮平涂、圆角压到 0.28 倍", "Ink cards, flat buttons, tighter corners"),
                    T("浅底上的标题改走墨色，墨块区文字保白，解决「白字压浅底」看不清", "Text contrast fixed on paper background")
                });
            AddSec(heads, blocks, T("v18.6 · 更新链路与稳定性", "v18.6 · Update pipeline"),
                new string[] {
                    T("启动 25 秒后后台静默检查更新；清单新增 sha256 校验，对不上自动换源", "Silent update check + sha256"),
                    T("下载队列之外的三件兜底：下载大小校验、失败重试提示、error.log 崩溃记录", "Size check, retry, crash log"),
                    T("Toast 支持堆叠与琥珀色错误样式；键盘焦点环加强到 2px 强调色 + 内圈亮边", "Stacked toasts and focus ring"),
                    T("首次启动三步引导：功能概览 → 选风格 → 音效与托盘/快捷键", "First-run 3-step guide"),
                    T("源码拆成 125 个文件；自签名证书签署 exe，本机 Valid", "Source split, self-signed exe")
                });

            string raw = UpdateText().Replace("\r\n", "\n");
            string[] ls = raw.Split('\n');
            System.Collections.Generic.List<string> oHeads = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<System.Collections.Generic.List<string>> oBlocks = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            for (int i = 0; i < ls.Length; i++)
            {
                string ln = ls[i].Trim();
                if (ln.Length == 0) continue;
                if (ln.StartsWith("【"))
                {
                    oHeads.Add(ln.Trim('【', '】'));
                    oBlocks.Add(new System.Collections.Generic.List<string>());
                }
                else if (oHeads.Count > 0)
                {
                    oBlocks[oBlocks.Count - 1].Add(ln.TrimStart('·', '-', ' '));
                }
            }
            for (int i = 0; i < oHeads.Count && heads.Count < 10; i++)
            {
                System.Collections.Generic.List<string> tmp = oBlocks[i];
                string[] arr = new string[tmp.Count];
                for (int k = 0; k < tmp.Count; k++) arr[k] = tmp[k];
                AddSec(heads, blocks, oHeads[i], arr);
            }
            int y = 12;
            int shown = 0;
            for (int si = 0; si < heads.Count && shown < 10; si++, shown++)
            {
                Panel rail = new Panel();
                rail.BackColor = UiKit.A(61, 126, 255);
                rail.Location = new Point(14, y + 3);
                rail.Size = new Size(3, 15);
                body.Controls.Add(rail);

                Label hd = new Label();
                hd.Text = heads[si];
                hd.ForeColor = UiKit.A(232, 238, 246);
                hd.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
                hd.BackColor = Color.Transparent;
                hd.Location = new Point(26, y);
                hd.AutoSize = false;
                hd.Size = new Size(514, 22);
                hd.AutoEllipsis = true;
                body.Controls.Add(hd);
                System.Windows.Forms.ToolTip tipH = new System.Windows.Forms.ToolTip();
                tipH.SetToolTip(hd, heads[si]);
                y += 28;

                System.Collections.Generic.List<string> items = blocks[si];
                for (int k = 0; k < items.Count; k++)
                {
                    Label dot = new Label();
                    dot.Text = "•";
                    dot.ForeColor = UiKit.A(96, 150, 210);
                    dot.Font = new Font("Microsoft YaHei UI", 8.5F);
                    dot.BackColor = Color.Transparent;
                    dot.Location = new Point(30, y);
                    dot.AutoSize = true;
                    body.Controls.Add(dot);

                    Label it = new Label();
                    it.Text = items[k];
                    it.ForeColor = UiKit.A(180, 190, 204);
                    it.Font = new Font("Microsoft YaHei UI", 8.5F);
                    it.BackColor = Color.Transparent;
                    it.Location = new Point(44, y + 1);
                    it.AutoSize = false;
                    int hh = TextRenderer.MeasureText(items[k], it.Font, new Size(490, 2000), TextFormatFlags.WordBreak).Height;
                    it.Size = new Size(490, hh + 4);
                    body.Controls.Add(it);
                    y += hh + 10;
                }
                y += 14;
            }

            Label more = new Label();
            more.Text = T("更早的版本去 GitHub 看 →", "Full history on GitHub →");
            more.ForeColor = UiKit.A(78, 150, 240);
            more.Font = new Font("Microsoft YaHei UI", 8.5F);
            more.BackColor = Color.Transparent;
            more.Location = new Point(18, 508);
            more.AutoSize = true;
            more.Cursor = Cursors.Hand;
            more.Click += (s2, e2) => { try { Process.Start("https://github.com/wj5666619/MiniToolbox/releases"); } catch (Exception dg1584_1) { Diag.Swallow("UpdateForm.cs:1584", dg1584_1); } };
            host.Controls.Add(more);

            Button btnClose = new RoundBtn(UiKit.A(44, 52, 66), UiKit.A(226, 232, 240));
            btnClose.Text = inlineMode ? T("返回关于", "Back") : T("关闭", "Close");
            btnClose.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnClose.Size = new Size(94, 30);
            btnClose.Location = new Point(488, 504);
            btnClose.Click += (s2, e2) => onBack(s2, e2);
            host.Controls.Add(btnClose);

            Label foot = new Label();
            foot.Text = T("日志只保留最近 10 个版本，完整历史见仓库 Releases。", "Latest 10 versions shown.");
            foot.ForeColor = UiKit.A(96, 106, 122);
            foot.Font = new Font("Microsoft YaHei UI", 7.5F);
            foot.BackColor = Color.Transparent;
            foot.Location = new Point(18, 540);
            foot.AutoSize = true;
            host.Controls.Add(foot);

            body.AutoScrollPosition = new Point(0, 0);   // 时间线默认停在最新版本那一屏
            return host;
        }

        // 独立开窗：/changelog 与「更新日志」老入口走这里
        void ShowChangelog()
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(600, 566);
            f.BackColor = UiKit.A(20, 24, 31);
            f.ShowIcon = false;
            f.ShowInTaskbar = false;
            f.KeyPreview = true;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.Controls.Add(BuildLogPanel(false, delegate { f.Close(); }));
            f.KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Escape) f.Close(); };
            f.ShowDialog(this);
        }

        // 关于页内嵌：整窗盖一层时间线面板，返回即回到关于页，不弹新窗
        Panel logHost;
        void ShowInlineLog()
        {
            try
            {
                if (logHost != null) return;
                Panel shell = new Panel();
                shell.Size = new Size(640, 596);
                shell.BackColor = UiKit.A(16, 20, 26);
                Panel lp = BuildLogPanel(true, delegate { HideInlineLog(); });
                lp.Location = new Point(20, 15);
                shell.Controls.Add(lp);
                foreach (Control c in Controls) c.Visible = false;
                Controls.Add(shell);
                shell.BringToFront();
                logHost = shell;
                Sfx.Click();
            }
            catch (Exception dg1644_1) { Diag.Swallow("UpdateForm.cs:1644", dg1644_1); }
        }

        void HideInlineLog()
        {
            try
            {
                if (logHost == null) return;
                Controls.Remove(logHost);
                logHost.Dispose();
                logHost = null;
                foreach (Control c in Controls) c.Visible = true;
                Sfx.Click();
            }
            catch (Exception dg1658_1) { Diag.Swallow("UpdateForm.cs:1658", dg1658_1); }
        }

        string UpdateText()
        {
            if (en)
            {
                return "Mini Toolbox v" + APP_VER + "\r\n\r\n"
                    + "【v7.9 · Unified look / drop port / homemade sounds】\r\n"
                    + "· The whole interface got one careful pass instead of a rewrite: every form already uses Microsoft YaHei UI, so the sweep keeps that family, protects the icon font (Segoe MDL2 Assets) and the monospaced one (Consolas) that depend on glyph width, and only rides in on new windows as they appear (Ui2.StartWatch hooks Application.Idle, so future tools inherit the treatment automatically)\r\n"
                    + "· No label is allowed to cut its text any more: non-auto-size labels get AutoEllipsis and a hover tooltip carrying the full text, buttons get the same treatment plus a hover/down tint on flat buttons, and page titles are marked by a 4px gradient bar painted in the parent container background, so it can never sit on top of a glyph\r\n"
                    + "· The new /uiaudit channel measures every control on all nine pages with real layout (each page is shown before it is measured), counting overflow, out-of-parent clipping and text-on-text intersections: 286 controls, 0 overflow, 0 clipped, 0 overlap\r\n"
                    + "· Drop port: a dashed card at the bottom of the sidebar (10,442 180x100) and, more usefully, the whole window accepts a drop. While a file hovers, a full-area mask fades in with a flowing dashed frame, a 54x54 plus tile, the words 松手投递 and a chip naming what is about to be taken\r\n"
                    + "· Files are routed by type: images and videos switch to the Watermark page and load themselves, audio goes to the Audio Tool with its path already on the clipboard, anything else just has its path copied and says so in a toast; every result answers with a small rounded toast in the corner instead of a modal\r\n"
                    + "· All sounds are made in code now, so the program still ships with zero audio assets: seven voices (press, drop, done, error, bell, alarm, cancel) are synthesised at 22.05kHz/16bit when the program starts and played from memory. The error voice is the signature one - a two-stage descending buzz with 26Hz flutter and a little grit, like a latch refusing to close\r\n"
                    + "· The four system sounds are gone (the shutdown confirmation, the repeat-task chime, the reminder popup and the pomodoro switch now use the new voices), task start / cancel and a finished disk scan answer with their own tones, and the tray menu gained a 界面音效 switch that persists to 音效.txt\r\n"
                    + "· Three defects surfaced by the audit were fixed on the way: a status caption sat 2px inside a 15F number in the stat cards, the video page tip ran 881px wide and fell off the canvas, and the toast landed on top of the primary button\r\n"
                    + "· Self-tests grew to 50 (three new channels: /uiaudit, /sfxtest which exports all seven voices with peak/rms to 音效\\*.wav without making a sound, and /dropshot which screenshots the mask and the toast); all 50 exit 0\r\n"

                    + "【v8.0 · A cleaner shell / the command cockpit / three-step sound】\r\n"
                    + "· The interface pass stays incremental: auto-size labels now get a graded rule - try wrapping first, fall back to an ellipsis plus a hover tooltip only when wrapping genuinely will not fit (previously such labels were left alone entirely); text boxes gain a focus tint that leans a few percent towards the accent colour and restores on leave, without touching the palette; ListBox and ListView join the font normalisation for the first time. The audit numbers are unchanged: 286 page controls plus 7 in the palette, 0 overflow, 0 clipped, 0 overlap\r\n"
                    + "· The quick-command panel (Ctrl+Alt+Space) becomes a command cockpit: the input keeps a visible placeholder (function name / pinyin initials / try 关机 30), the lower-right corner reports 43 items or N matches live, an empty state takes over when nothing matches, and the matched slice is highlighted in place. Arrow keys step one row, PgUp / PgDn jump six, Tab completes the highlighted row back into the box, Enter runs it, Esc closes\r\n"
                    + "· A new intent layer turns one plain sentence into an executable row pinned on top: 关机 30 / 关机1小时 / 关机 90秒 schedule a real shutdown with confirmation, 清理c盘 jumps to the cleaner, 截图 5秒 fires a screenshot later. Intent rows carry a blue 意图 badge and a left accent bar so they never look like an ordinary command\r\n"
                    + "· Recently used commands float up: every executed row is appended to 指令历史.txt (8 entries, de-duplicated, newest first) and an empty query lists them at the top, so the same job never has to be retyped\r\n"
                    + "· Sounds went from seven to eleven (page switch, soft warning, saved, copied), still synthesised in code with zero assets: the switch is a 55ms 900→1150Hz lift, the warning a 420Hz pulse with a second harmonic that stays gentler than the error voice, saved is a two-stage 520→780Hz rise, copied is two 22ms 1200Hz typewriter ticks\r\n"
                    + "· Volume now has three steps (soft 0.62 / normal 0.82 / loud 1.00): the tray menu gained a Sound volume submenu, and choosing one rebuilds all eleven players and persists to 音效音量.txt. Each voice also carries a minimum gap (press 160ms, drop and done 260ms, error and alarm 320ms and so on), so hammering a button no longer stacks into noise\r\n"
                    + "· Two shell defects were fixed on the way: the cockpit placeholder used to be consumed by the automatic focus and never appeared (it now stays until the first keystroke), and the match highlight drew the whole row once and then the matched slice on top, blurring the glyph edges by two pixels (it is now painted once, in three segments)\r\n"
                    + "· Self-tests stay at 51 channels: /palettetest gained nine intent cases plus a history round-trip, /uiaudit gained a [palette] section that measures the cockpit too, and /sfxtest covers all eleven voices at three volume steps; everything exits 0\r\n"

                    + "【v8.1 · Three UI rounds / readable type / shutdown without consent】\r\n"
                    + "· Round one, hierarchy: a hairline gradient now separates the sidebar logo block from the nine destinations; the active pill carries an accent underline and the inactive icons are dimmed one step, so the current page is readable without reading the text. A fading rule extends from the page title to the right, drawn on the parent background so it can never sit on top of a control\r\n"
                    + "· Round two, type: the global minimum font size is now 8F, lifting the 7F and 7.5F captions (icon fonts and Consolas are untouched because their metrics come from glyph width). Secondary text is one step brighter (4.9:1 to 6.7:1 contrast), and a readability floor lifts any caption below 3:1 on its own background until it passes 3.2:1, changing colour only. The skin sweep now walks to depth 12 and runs a second pass before the window is shown\r\n"
                    + "· Round three, details: card outlines are unified as a 12px radius with one faint top highlight (two legacy hard 1px rectangles now match the rest of the app), and the rounded buttons gained keyboard focus, reachable with Tab, with a 2px accent ring that only appears for keyboard users\r\n"
                    + "· The audit reports two new sections: TYPO (minimum size, controls under 8F, off-family count) and CONTRAST (the three weakest captions). Current result: 286 controls, overflow 0, clipped 0, overlap 0, minimum size 8, none under 8F, weakest contrast 3.4:1\r\n"
                    + "· Shutdown no longer asks: typing 关机 30 in the cockpit schedules it and the 60s hotkey executes it, both without a confirmation box\r\n"

                    + "【v7.8 · Wishlist price watch / library health】\r\n"
                    + "· The Steam page gained two more buttons on its bottom row: Wishlist drops (370) and Library health (496). Both open read-only windows and both sit clear of the legacy buttons and the node-latency list; an overlap check now runs over all three forms\r\n"
                    + "· Wishlist price watch: the SteamID64 is read straight out of Steam's config\\loginusers.vdf, so no login is involved; the window then pages through store.steampowered.com/wishlist/profiles/<id>/wishlistdata to list every wishlisted title and prices them in batches of ten through appdetails (cc=cn, filters=price_overview). Each title is compared with the first price ever recorded for it in steam_wishlist_price.txt, so the baseline survives a restart, cheaper rows turn green, and the CSV export writes only the titles that actually dropped\r\n"
                    + "· The wishlist must be public; when it is not, Steam answers with nothing and the window says so in plain words instead of showing an empty list\r\n"
                    + "· Library health: reads every libraryfolders.vdf path and every appmanifest and flags four kinds of trouble — the fully-installed bit missing (unfinished), the update-required bit set or a byte count with bytes still owed (update), an install folder that no longer exists, and folders under steamapps\\common that no manifest points at (orphans); titles and bytes are totalled per library\r\n"
                    + "· One-click fix only hands the title to Steam: steam://validate/<appid> for updates and missing files, steam://install/<appid> for unfinished installs. Orphan folders are listed but never deleted, and the scan itself writes nothing to disk\r\n"
                    + "· A real bug found while building it: one library showed up twice, because the registry SteamPath arrives as d:/myq/steam while libraryfolders.vdf says D:\\myq\\steam, which doubled both totals and orphan rows. Every library path is now normalised and de-duplicated case-insensitively, and Steam's own system folders (Steamworks Shared, Steam Controller Configs, SteamVR, Steam Linux Runtime) are no longer mistaken for orphaned games\r\n"
                    + "· Four new self-test channels: /wishui (geometry plus an overlap check of all three forms), /libcheck (a real read-only scan written to _libcheck_test.txt), /wishlist (wishlist, five prices, cache round-trip, CSV) and /wishshot (renders both new windows to PNG); all 47 CLI self-tests exit 0\r\n\r\n"
                    + "【v7.7 · Completion board / phone quick transfer / daily wallpaper】\r\n"
                    + "· A fifth toolbar button on the achievements page, Completion: a read-only pass over the whole library that reads unlocked/total per title, computes the overall percentage, splits full / partial / zero / unreadable, and writes the detail (best first) to _achievement_progress.csv, which Excel opens directly; it runs strictly in dry mode, so no achievement is written and the account is untouched; it refuses to start while an unlock batch is running, and pressing it again stops the scan\r\n"
                    + "· Phone toolbox gained a quick-transfer row: screenshot straight to the clipboard (binary exec-out screencap, saved to 手机工具 and pushed into the Windows clipboard so Ctrl+V pastes it), read the phone clipboard, send the PC clipboard to the phone (plain ASCII is typed at the phone cursor via input text, anything with CJK is pushed to /sdcard/ as a txt file), and open the screenshot folder; the window grew from 940x660 to 940x706 and the command and log boxes moved down, geometry verified with no overlap\r\n"
                    + "· The desktop board can switch themes: two tiny labels '图' and '色' at the right of the title row plus a right-click menu (daily wallpaper / restore original wallpaper / cycle board colours); four palettes (deep blue, ink green, dark purple, warm brown) recolour both the border and the text bells and the choice is saved to 看板主题.txt\r\n"
                    + "· Daily wallpaper: pulls today's Bing image (_UHD, 4K), records the wallpaper you already have into 壁纸\\原壁纸.txt first, then fills the desktop; restorable in one click, and today's image is reused from cache (measured 3,567,792 B)\r\n"
                    + "· Fixed a real failure: .NET 4 negotiates TLS 1.0 by default, so the Bing endpoint returned 'could not create SSL/TLS secure channel'; TLS 1.2 is now requested explicitly before any download\r\n"
                    + "· Dead code removed: UiKit.LoadImageNoLock, MatrixText and V71Ui.PANEL had no callers, and one dead assignment (skipMsg) is gone; 0 warnings 0 errors\r\n"
                    + "· Three new self-test channels: /adbui (phone-toolbox geometry plus an overlap check), /wptest (fetch the image only, never touch the wallpaper) and /pcttest (read-only scan of five titles with the CSV written); all 43 CLI self-tests exit 0\r\n\r\n"
                    + "【v7.6 · Steam achievements are now one integrated workbench】\r\n"
                    + "· The achievements page is a single workbench: one 'Unlock all (full library)' button runs the whole flow — checks Steam and steam_api64.dll, reads local schemas, writes each title through Steamworks, streams progress and per-title results into the in-page console\r\n"
                    + "· Pressing the same button while a batch runs stops it (no more Yes/No dialog); the next press resumes from that title. The final summary is appended to the console instead of a modal dialog or notepad hop\r\n"
                    + "· New live progress bar (processed / total titles) and a Clear console button; the SAM host window stays collapsed by default and only expands for the legacy per-game path — opening the page no longer launches SAM\r\n"
                    + "· Export list / Last report became small toolbar buttons and report their state in the console; the sidebar subtitle changed to 'Unlock whole library in one click'\r\n"
                    + "· Fixed a layout flaw: the page used Right/Bottom anchors inside a fixed-size form, so the margins were computed against the wrong parent width and controls stretched to 1286x901, overflowing the canvas; now absolute coordinates (console 726x340, bar 726x6, status and warning lines inside bounds)\r\n"
                    + "· New /samdump self-test channel (switches to the page and dumps its control geometry to _samdump.txt); /uidump now exits after writing\r\n\r\n"
                    + "【v7.5 · Game Cheats & Desktop Pet removed】\r\n"
                    + "· Sidebar 'Game Cheats' (GCM) and 'Desktop Pet' slots are gone: sidebar 11 -> 9 entries; titles, subtitles, card colours, the whole page, page dispatch, /sel=gcm and /petlan channels all deleted\r\n"
                    + "· GCM chain removed end to end: pageGcm / gcmHost / procGcm / lblGcmState fields, the page build, the EmbedTick follow-up block, ShowGcmPage and EnsureGcmEmbedded, 8 visibility guards plus the HideAllPages one, the GcmZip name and size mapping, ResOf / DirOf branches, the /deploytest channel (it only ever tested GCM) and the installer extraction step\r\n"
                    + "· Pet chain removed end to end: PetForm / RemotePetForm / PetSetupForm (sprite sheets, level & hunger growth, UDP LAN mode, remote pet windows, setup dialog), petWin + TogglePet and the /petlan self-test; the desktop board stays (separate window, palette / HUD right-click / double-click entry points)\r\n"
                    + "· Disk cleanup: gcm_tool.zip (57.4 MB) and the extracted gcm_tool folder (135.0 MB) went to the Recycle Bin; installer rebuilt with four bundles, 104.3 MB -> 46.9 MB; main exe 1,210,368 B\r\n"
                    + "· Checkup now lists 4 components (GCM row dropped); 40 CLI self-tests exit 0; sidebar check 9/9 matches; version bumped to 7.5\r\n\r\n"
                    + "【v7.4 · PC Setup removed】\r\n"
                    + "· The PC Setup (dnkhw) sidebar slot, its in-app page and its embed host are gone: sidebar is now 11 entries, the 4th-row card and every visibility guard were deleted, the changelog history keeps the old rows for the record\r\n"
                    + "· Component pipeline removed end to end: DnkhZip mapping, dnkh_tool deploy path, the five-component checkup table (now 5 rows) and the installer extraction; dnkh_tool.zip (61.6 MB) and the extracted dnkh_tool folder (144.1 MB) went to the Recycle Bin\r\n"
                    + "· Installer drops from 166.0 MB to 104.3 MB; main exe 1,229,312 B; 40 CLI self-tests all exit 0 (record: _smoke_20261117_v74.txt)\r\n"
                    + "· Version bumped to 7.4\r\n\r\n"
                    + "【v7.0 · Six more tools】\r\n"
                    + "· Entry: '▸ Power Tools' now holds 17 cards (six rows)\r\n"
                    + "· ⑫ Command palette: Ctrl+Alt+Space, type a few letters (Chinese, English or initials) and jump to any of 28 actions\r\n"
                    + "· ⑬ System HUD: a 272x44 always-on-top bar at the top-right showing CPU / memory / up-down network speed, refresh every 1.5s; drag to move, right-click to pause / stick left / open dashboard / hide\r\n"
                    + "· ⑭ Batch rename & convert: six rename rules (prefix / suffix / serial / date / regex / extension) with preview, conflict marks and one-click undo from 重命名记录; images to PNG/JPG/BMP/GIF, MP4, MP3 and video-to-GIF via the bundled FFmpeg — originals never overwritten\r\n"
                    + "· ⑮ Macro recorder: F8 records keyboard and mouse through low-level hooks, F9 replays through SendInput with loop count and 0.5x-4x speed, Esc aborts; saved as 宏\\*.mac\r\n"
                    + "· ⑯ Password book & 2FA: AES-256 + PBKDF2 (12000 rounds) into 密码本.pwb, no plaintext on disk; built-in offline TOTP with a live 6-digit code and countdown\r\n"
                    + "· ⑰ Privacy cleanup: Chrome / Edge / Firefox caches and cookies, recent documents, thumb & icon cache, run history, typed URLs, clipboard, DNS cache, Recycle Bin — scanned and listed before cleaning\r\n"
                    + "· Achievement unlock is now 'Unlock all games (direct)': every game with achievements in your library is written straight through Steamworks in one go, no game page ever opens; a failed title is retried in place, the whole batch gets one final retry pass, and what still fails is listed in _unlock_failed.txt with the reason\r\n"
                    + "· Update check is far more resilient: if the manifest fails it retries through jsDelivr / fastly / ghproxy / gh-proxy (twice each); the download link is retried with common asset names (MiniToolbox.exe / default.exe / 小工具箱.exe) and the release tag (the old 404 came from the asset being named default.exe); if everything fails it lists the exact error per source and offers 'pick a local installer' or 'open the releases page'\r\n"
                    + "· Cleanup before release: removed 1.1 MB of test leftovers (9 _*_test.txt files, _uidump.txt, _batch_trace.txt, the source backup, empty test folders) and 4 dead code paths (unused HasFile, vault test hook, FreeLibrary declaration, broken clipboard self-test entry now moved to /lancliptest); normal use no longer writes _batch_trace.txt; 0 warnings 0 errors\r\n\r\n"
                    + "【v6.9 · Six more tools】\r\n"
                    + "· Entry: '▸ Power Tools' now holds 11 cards\r\n"
                    + "· ⑦ Clipboard history: records copied text while running (max 200, pinned kept), Ctrl+Alt+V, search/pin/clear; DPAPI-encrypted on disk\r\n"
                    + "· ⑧ Screen recorder: bundled FFmpeg captures MP4 (full screen or a region, 10-60 fps), Ctrl+Alt+R toggles; one-click GIF export\r\n"
                    + "· Screenshot editor: arrows, boxes, mosaic, text and numbered marks right after Ctrl+Alt+S\r\n"
                    + "· ⑨ Backup & restore: hotkeys, vault, boost backup, clipboard history and records into one zip (old files kept as .bak)\r\n"
                    + "· ⑩ File encrypt: any file to .mtbx with AES-256 and the original name embedded\r\n"
                    + "· ⑪ Update check: compares the GitHub manifest and updates in-app\r\n\r\n"
                    + "【v6.8 · Six power tools】\r\n"
                    + "· Entry: '▸ Power Tools' at the top-right of the PC Specs page opens a six-card center\r\n"
                    + "· ① Global hotkeys: shot / main window / transfer / this center on Ctrl+Alt+S, H, T, G; double-click a row to rebind anything with Ctrl/Alt/Shift/Win, saved to hotkeys.json\r\n"
                    + "· ② LAN clipboard: open http://<pc-ip>:8765/clip on your phone to read and rewrite the PC clipboard\r\n"
                    + "· ③ Game boost: game process to High priority, high-performance power plan, DNS flush, other apps to BelowNormal; everything backed up in 加速备份.tsv and fully restorable\r\n"
                    + "· ④ Desktop tidy: files grouped into 整理-XX by type (shortcuts untouched), every run recorded and undoable in one click\r\n"
                    + "· ⑤ Encrypted vault: AES-256 + PBKDF2 into 保险箱.box; wrong password fails, nothing readable on disk; clipboard cleared 30s after copying a password\r\n"
                    + "· ⑥ Duplicate finder: size → 8KB fingerprint → full MD5; one copy per group kept, deletes go to the Recycle Bin\r\n\r\n"
                    + "【v6.7 · Checkup / Startup / Board / Phone transfer】\r\n"
                    + "· One-click checkup: 15 checks (component integrity, Steam, network & update source, C: free space, WinRE, error.log, startup count); one-click repair for red items; report export\r\n"
                    + "· Startup manager: lists registry Run keys, the Startup folder and auto services; disabling keeps a backup and can be restored; services need admin\r\n"
                    + "· Desktop board: right-click the pet to show CPU / memory / network / C: free in a small always-on-top window\r\n"
                    + "· Phone transfer: same Wi-Fi, open the shown address in the phone browser to upload files to the PC or download PC files to the phone — no app, no cable\r\n\r\n"
                    + "【v6.6】\r\n"
                    + "· Steam unlock now goes straight to Steamworks: no game page is opened, ~0.5s per title, whole library in one pass\r\n"
                    + "· Full library actually run: 234 titles / 138s (written 0, protected 1, skipped 226, failed 7 — the account was already fully unlocked)\r\n"
                    + "· Accident-proof stop: pressing the button again asks before aborting; a cursor file resumes an interrupted run\r\n"
                    + "· The button shows live progress (Stop i/n) and the report adds a Protected bucket\r\n"
                    + "· All-feature hardening: 12s timeout + retry on music search (both stores searched in parallel), 60s watchdogs on component download / yt-dlp / ffmpeg, no more stdout-stderr deadlocks, images are loaded without locking the user's files, schema scan cached for 5 min\r\n"
                    + "· Feature upgrades per page: quick 10m/30m/1h/2h presets, cleanup report export + locate file, Steam value CSV export (and a fixed overlapping button), achievement library CSV export, WinRE self-check before reinstall, pet 'back to screen centre'\r\n"
                    + "· Pre-release cleanup: 195 MB of third-party build source removed, 3 dead methods deleted, 0 warnings 0 errors\r\n"
                    + "· Version bumped to 6.6\r\n\r\n"
                    + "【v6.5】\r\n"
                    + "· Slim build: the main exe drops from 136 MB to 0.6 MB\r\n"
                    + "· GCM / PC Setup / Video Download bundles are no longer embedded; they download on first use and stay cached locally\r\n"
                    + "· Download size is verified; failures are logged to error.log with a retry hint\r\n"
                    + "· Existing installs already have the bundles on disk — those open offline with no download\r\n"
                    + "· The update package is now about 2 MB, so checking and downloading updates is near instant\r\n"
                    + "· Deep clean before release: 300 MB of build/test leftovers deleted, 213 lines of dead code removed, 0 warnings 0 errors\r\n"
                    + "· Version bumped to 6.5\r\n\r\n"
                    + "【v6.4】\r\n"
                    + "· Update source is live on GitHub Releases (raw manifest + direct asset download), verified end to end\r\n"
                    + "· Fixed the old .NET TLS gap: manifest fetch and update download now force TLS 1.2 instead of failing silently\r\n"
                    + "· Replacing the exe now retries up to 40 times, checks the size, and rolls back on failure\r\n"
                    + "· Update trace written to %TEMP%\\mtb_update.log\r\n\r\n"
                    + "【v6.3】\r\n"
                    + "· Multi-platform music search: NetEase / Kugou / both, new Source column\r\n"
                    + "· Listen Together: synced rooms, queue, progress and chat, audio auto-distributed\r\n"
                    + "· In-app update: check, download with progress, replace and restart\r\n"
                    + "· Music page redesigned with dark platform switch buttons\r\n"
                    + "· All debug leftovers removed, clean rebuild with 0 warnings 0 errors\r\n\r\n"
                    + "【v6.2】\r\n"
                    + "· Scheduled reminders with custom title/body/time/repeat/sound\r\n\r\n"
                    + "【v6.1】\r\n"
                    + "· Removed the puzzle module and all of its data/files\r\n"
                    + "· Full data & file audit: 10 hard-coded absolute paths fixed\r\n"
                    + "· UI polish: glass buttons, diagonal background, brighter inputs, sidebar highlight\r\n"
                    + "· Every input box now has a real placeholder hint\r\n"
                    + "· All dropdowns use self-drawn dark menus (system-drawn ones were stuck white)\r\n\r\n"
                    + "【v6.0】\r\n"
                    + "· Desktop pet: growth, hunger, sprite groups, LAN mode over UDP broadcast\r\n"
                    + "· New modules: Video Download, Music Download, PC Setup, Desktop Pet\r\n\r\n"
                    + "【v5】\r\n"
                    + "· C: cleanup rewritten as a space heatmap with drill-down\r\n"
                    + "· PC Specs: live CPU / RAM / disk load plus GPU temperature, power, VRAM\r\n"
                    + "· Smart triggers: idle, low battery, process exit, high CPU, GPU overheat\r\n"
                    + "· De-watermark: multi-region selection, image queues, video time range\r\n\r\n"
                    + "【v4】\r\n"
                    + "· Video / music download pages; pure dark theme; 14-ish modules in one window\r\n\r\n"
                    + "Tip: power actions can interrupt unsaved work — keep the confirm popup on.";
            }
            return "小工具箱 v" + APP_VER + "\r\n\r\n"
                + "【v7.9 本次更新 · 界面统一美化 / 拖放投递舱 / 自研音效】\r\n"
                + "· 全程序界面只做增量升级，不重写：字体本来就是微软雅黑 UI 一族，所以升级点放在「统一族 + 保护图标字体与等宽字体 + 文字绝不裁切」；新增的皮肤巡检挂 Application.Idle，任何新窗体一出现就自动套用，以后新加的功能窗口不用再逐个改\r\n"
                + "· 文字防裁切：不自适应的 Label 一律给省略号，真的装不下才挂悬浮提示显示全文；扁平按钮补上悬浮/按下变色；页面大标题左侧加 4px 渐变强调条，画在父容器背景层，永远压不到字\r\n"
                + "· 新增 /uiaudit 通道：九个页面逐页真正显示后测量，统计溢出、越出父容器、文字互相压盖——286 个控件，溢出 0、越界 0、压盖 0\r\n"
                + "· 拖放投递舱：侧栏底部一张虚线卡片（10,442 180x100），同时整窗任意位置都能接住拖进来的文件；拖入时主区浮起遮罩，流动虚线框 + 54x54 加号 + 「松手投递」+ 即将投递的文件名\r\n"
                + "· 按类型自动分流：图片 / 视频切到「去水印」页并自动载入，音频打开音频工具且路径已进剪贴板，其他类型只把路径放进剪贴板；结果都用右下角圆角轻提示反馈，不再弹模态框\r\n"
                + "· 所有声音改为代码合成，程序依旧零音频素材：按键、投递、完成、错误、提醒、到点、取消七个音，启动时按 22.05kHz / 16bit 现算进内存；错误音是签名音——两段下坠的齿状音带 26Hz 振颤与砂砾，像卡扣卡住\r\n"
                + "· 原来四处系统提示音全部替换（到点确认、周期执行、提醒弹窗、番茄钟），开始/取消任务与磁盘扫描完成也各有各的声音；托盘菜单新增「界面音效」开关，开关状态存 音效.txt\r\n"
                + "· 审计顺手修掉三处真瑕疵：统计卡的标题被 15F 大数字压住 2px、视频页提示 881px 宽直接冲出版面、轻提示盖住「立即执行」主按钮\r\n"
                + "· 自测扩到 50 项：新增 /uiaudit（界面审计）、/sfxtest（把七个音落成 音效\\*.wav 并报峰值/有效值，跑测试不发声）、/dropshot（遮罩与轻提示真实截屏），全部 exit=0\r\n"

                + "【v8.0 本次更新 · 界面精修 / 智能指令舱 / 音效进三档】\r\n"
                + "· 界面还是只做增量：给“自动适应内容”的标签补上分级策略——先试着折行，真折不下才省略号 + 悬浮全文（原来这类标签超宽时是完全不管的）；输入框加了进场的焦点底色（朝强调色偏一点点，离开复原，不动原有配色）；ListBox / ListView 第一次纳入字体归一。审计口径不变：286 个页面控件 + 指令舱 7 个控件，溢出 0 / 越界 0 / 压盖 0\r\n"
                + "· 快捷指令面板（Ctrl+Alt+Space）升级成“智能指令舱”：输入框常显占位提示（功能名 · 拼音首字母 · 试试「关机 30」），右下角实时给出「N 项 / N 项匹配」，一条都没有时换成空态引导，命中片段整段高亮；↑↓ 逐条、PgUp / PgDn 一次翻六条、Tab 把选中项补全回输入框、Enter 执行、Esc 关闭\r\n"
                + "· 新增“意图层”：一句人话直接翻译成可执行条目并置顶——「关机 30」「关机1小时」「关机 90秒」（确认后真调 shutdown /s /t），「清理c盘」直接切到清理页，「截图 5秒」到点自动截图；意图条带蓝色「意图」标和左侧强调条，与普通条目一眼可分\r\n"
                + "· 最近用过的指令置顶：执行过的条目记进 指令历史.txt（最多 8 条、去重、新的在前），面板空查询时按历史顺序排最上面——同一件事不用再打第二遍字\r\n"
                + "· 音效从 7 个扩到 11 个，新增切页、软警告、保存成功、复制成功；仍然是代码合成、零素材：切页是 900→1150Hz 的 55ms 轻挑，软警告是 420Hz 单脉冲带二次谐波（比错误音温和），保存是 520→780Hz 两段上行，复制是两个 22ms 的 1200Hz 打字机式短 tick\r\n"
                + "· 音量分三档（轻 0.62 / 中 0.82 / 响 1.00）：托盘菜单多出「音效音量」三个勾选项，选完立刻重算 11 个播放器并写进 音效音量.txt，下次启动照旧；每个音还有最小间隔（按键 160ms、投递/完成 260ms、错误/提醒 320ms 等），连点按钮不会叠成噪声\r\n"
                + "· 顺手修掉两处界面瑕疵：指令舱的占位提示原来被“自动聚焦”吃掉、永远看不见（现在常显到第一次打字为止）；命中高亮原来整行画一次、命中段又叠一次，字边缘糊两个像素（现在只画一次、三段拼接）\r\n"
                + "· 自测仍是 51 项通道，不增不减：/palettetest 追加意图解析 9 个用例与历史落盘往返，/uiaudit 多出 [palette] 段把指令舱也纳入度量，/sfxtest 覆盖 11 个音与三档峰值对比；全部 exit=0\r\n"

                + "【v8.1 本次更新 · 界面三轮升级 / 文字可读性 / 关机不再确认】\r\n"
                + "· 第一轮「层级」：侧栏 Logo 区与 9 项导航之间加一条极淡渐变分隔线；当前页的胶囊底边多一条强调色细线，未选中项的图标压暗一档——不看文字也一眼认得出自己在哪一页；页面标题右侧延伸出一条渐隐细线，把标题和下面的控件分开（线画在父容器背景层，永远在控件之下，既不压字也不压按钮）\r\n"
                + "· 第二轮「文字」：全局最小字号抬到 8F（7F/7.5F 那批说明字全部升级，图标字体与 Consolas 等宽字体不动，它们靠字形宽度定位）；次要文字统一提亮一档，对比度从 4.9:1 提到 6.7:1；新增可读性下限——深底上低于 3:1 的说明字自动逐档提亮到 3.2:1 以上，颜色在变、位置与字号不动；皮肤巡检深度 7 → 12，并在窗体上屏前补一遍，深层卡片里的字不再漏\r\n"
                + "· 第三轮「细节」：卡片描边统一为 12 圆角 + 顶部一道极弱高光（原来两处硬直角 1px 方框和圆角卡片放一起像两个年代的界面，现已合一）；圆角按钮加入键盘焦点——Tab 能走到、聚焦时亮起 2px 强调色焦点环，鼠标点过不留痕\r\n"
                + "· 审计新增两段：TYPO 报最小字号 / 低于 8F 控件数 / 非目标字族数；CONTRAST 报对比度最低的三处文字。这次结果：286 个控件，overflow 0 / clipped 0 / overlap 0，min_size 8，under8 0，最低对比度 3.4:1\r\n"
                + "· 按你的要求，关机不再问许可：指令舱输「关机 30」直接排程，热键「60 秒后关机」直接执行，都不再弹确认框\r\n"

                + "【v7.8 本次更新 · 愿望单降价 / 游戏库体检】\r\n"
                + "· Steam 进阶页底部按钮行多出两个入口：「愿望单降价」（370）与「库体检 + 一键修」（496）；两个都是只读窗口，把几何检查跑遍三个窗体，老按钮与节点测速列表一律不压盖\r\n"
                + "· 愿望单降价：直接从 Steam 的 config\\loginusers.vdf 认出 SteamID64（不用登录），再分页读 store.steampowered.com/wishlist/profiles/<id>/wishlistdata 列出全部愿望单条目，价格按十个一批走 appdetails（cc=cn、只取 price_overview）；每款都和 steam_wishlist_price.txt 里第一次记下的价格比，基准跨次有效，真的便宜的整行标绿，导出 CSV 只写降价那几款\r\n"
                + "· 愿望单必须是公开的：不公开时商店只回空，窗口会直白说明，不会假装「你的愿望单是空的」\r\n"
                + "· 游戏库体检：读遍 libraryfolders.vdf 的每个库与每个 appmanifest，分四类挑问题——没有「完整安装」位（没装完）、带「需要更新」位或还有字节没下完（需要更新）、安装目录已经不存在（文件缺失）、steamapps\\common 下没有任何清单指向的目录（孤儿目录）；按库合计款数与体积\r\n"
                + "· 一键修只把活交给 Steam：更新与文件缺失走 steam://validate/<appid>，没装完走 steam://install/<appid>；孤儿目录只列不删，体检本身对磁盘零写入\r\n"
                + "· 顺手修掉一个真 bug：同一个库被算了两遍——注册表 SteamPath 读出来是 d:/myq/steam，libraryfolders.vdf 里写的是 D:\\myq\\steam，导致款数与孤儿条目全部翻倍；现在所有库路径统一归一化并按大小写不敏感去重，Steam 自带的系统目录（Steamworks Shared / Steam Controller Configs / SteamVR / Steam Linux Runtime）也不再被当成孤儿游戏\r\n"
                + "· 新增四个自测通道：/wishui（三个窗体几何 + 压盖检查）、/libcheck（真跑一遍只读体检写 _libcheck_test.txt）、/wishlist（愿望单 + 5 款价格 + 缓存往返 + CSV）、/wishshot（两个新窗体渲成 PNG）；47 项命令行自测全部 exit=0\r\n\r\n"
                + "【v7.7 本次更新 · 成就完成度 / 手机速传 / 每日一图壁纸】\r\n"
                + "· 成就页工具条多了第五个按钮「成就完成度」：只读跑全库，逐款读到「已解锁 / 总数」，算总完成率，分出全成就 / 有进度 / 零进度 / 读不到四档，明细按完成度从高到低写成 _achievement_progress.csv（Excel 直接打开）；全程走 dry 只读，不写任何成就、不动账号；跑批中点它会被挡回，统计中途再点同一按钮即停止\r\n"
                + "· 手机工具箱新增一行速传：截屏直传（exec-out 二进制直取，存盘同时塞进 Windows 剪贴板，Ctrl+V 直接粘）、读手机剪贴板、发送电脑剪贴板到手机（纯 ASCII 走 input text 直接打到手机光标处，含中文改推 /sdcard/ 同名 txt）、打开截图目录；窗体 940×660 → 940×706，命令框与日志框整体下移，几何实测零重叠\r\n"
                + "· 桌面看板可换主题：标题行右侧「图」「色」两个小标签，加右键菜单三项（每日一图壁纸 / 恢复原壁纸 / 切换看板配色）；四套配色（深蓝 / 墨绿 / 暗紫 / 暖棕）边框与文字一起换，选择落盘 看板主题.txt\r\n"
                + "· 每日一图壁纸：取 Bing 今日图（_UHD 4K），先把原壁纸记进 壁纸\\原壁纸.txt，再设成铺满桌面，随时一键还原；当日图已缓存直接复用（实测一张 3,567,792 B）\r\n"
                + "· 修掉一个真问题：.NET 4 默认只谈 TLS 1.0，Bing 接口直接报「未能创建 SSL/TLS 安全通道」，现在显式开 TLS 1.2 才拿到图\r\n"
                + "· 清死代码：删掉 UiKit.LoadImageNoLock、MatrixText、V71Ui.PANEL 三处无引用代码与一处死赋值 skipMsg；0 警告 0 错误\r\n"
                + "· 新增三个自测通道：/adbui（手机工具箱控件几何 + 重叠检查）、/wptest（只取图不换壁纸）、/pcttest（只读统计前 5 款并落 CSV）；43 项命令行自测全部 exit=0\r\n\r\n"
                + "【v7.6 本次更新 · Steam 成就改成一体系】\r\n"
                + "· 成就页重做成一体式工作台：一个「一键全成就（跑完全库）」按钮承包全部流程——自动检查 Steam 与 steam_api64.dll、读本地成就定义、逐款直连 Steamworks 写入，进度与逐款结果实时写进页内控制台\r\n"
                + "· 跑批中再点同一个按钮就是停止（不再弹「是 / 否」确认框），下次点会从停下那一款接着跑；跑完的汇总也直接追加进控制台，不再弹模态汇总框、不再跳记事本\r\n"
                + "· 页内新增实时进度条（已处理 / 全库款数）与「清空控制台」按钮；成就管理器窗口（SAM 宿主）默认收起，只有走旧「进游戏页解锁」时才展开，进页也不再自动拉起 SAM\r\n"
                + "· 导出全库清单 / 打开上次报告 降为工具条小按钮，提示改走控制台；侧栏副标题「Steam 成就管理器」改为「全库一键解锁成就」\r\n"
                + "· 修掉一个界面瑕疵：成就页原来给卡片 / 宿主用 Right / Bottom 锚点，固定尺寸窗体里锚点按错误父宽算出负数边距，控件被拉成 1286×901 溢出画布；现在改绝对坐标，控制台 726×340、进度条 726×6、状态行与警告行不越界\r\n"
                + "· 新增 /samdump 自测通道（切到成就页并把整页控件几何写进 _samdump.txt）；/uidump 写完后自动退出\r\n\r\n"
                + "【v7.5 本次更新 · 移除游戏作弊与桌面宠物】\r\n"
                + "· 侧栏「游戏作弊」（GCM）与「桌面宠物」两项拆除，侧栏 11 项 → 9 项；两项的标题 / 副标题 / 卡片配色 / 整段页面 / 页面分派 / /sel=gcm 与 /petlan 两个调试通道一并清掉\r\n"
                + "· 游戏作弊链路整条下线：pageGcm / gcmHost / procGcm / lblGcmState 四个字段、内嵌页面整段、EmbedTick 里的 GCM 跟进块、ShowGcmPage 与 EnsureGcmEmbedded、8 处页面隐藏守卫加 HideAllPages 里的一处、GcmZip 的包名与体积映射、ResOf / DirOf 分支、只服务于 GCM 的 /deploytest 部署自测通道、安装包里的解包步骤\r\n"
                + "· 桌面宠物链路整条下线：PetForm / RemotePetForm / PetSetupForm 三个窗体（精灵表分组、等级经验饱食度养成、UDP 广播联机、远程宠物小窗、设置窗）、petWin 字段与 TogglePet、/petlan 自测通道；桌面看板保留（独立窗体，快捷指令 / 监控条右键 / 双击入口都在）\r\n"
                + "· 磁盘清理：gcm_tool.zip（57.4 MB）与解压目录 gcm_tool（135.0 MB）送进回收站；安装包去掉 GCM 资源后重打，104.3 MB → 46.9 MB，主程序 1,210,368 B\r\n"
                + "· 体检表降为 4 个组件（游戏作弊组件一行移除）；40 项命令行自测全部 exit=0，侧栏 9 项逐项对上；版本号 7.4 → 7.5\r\n\r\n"
                + "【v7.4 本次更新 · 移除电脑开荒】\r\n"
                + "· 侧栏「电脑开荒」槽位、内嵌页、内嵌宿主一并拆除，侧栏现在 11 项；卡片配色、页面隐藏守卫、页面分派、/sel 通道、UI 转储里的 dnkh 全部清掉\r\n"
                + "· 组件链路整条下线：组件包名映射、dnkh_tool 部署目录、体检表里的「电脑开荒组件」（现在 5 项组件）、安装包里的解包步骤全部移除；dnkh_tool.zip（61.6 MB）与解压目录 dnkh_tool（144.1 MB）已送进回收站\r\n"
                + "· 安装包从 166.0 MB 瘦到 104.3 MB，主程序 1,229,312 B；40 项命令行自测全部 exit=0（记录见 _smoke_20261117_v74.txt）\r\n"
                + "· 版本号 7.3 → 7.4，编译 0 警告 0 错误\r\n\r\n"
                + "【v7.0 本次更新 · 再加六项工具】\r\n"
                + "· 入口：电脑配置页右上角「▸ 增强工具」→ 增强工具中心，现在共 17 张卡片（六行）\r\n"
                + "· ⑫ 快捷指令面板：Ctrl+Alt+Space 唤出，输入几个字（中文/英文/拼音首字母都行）就能跳到 28 个动作里的任意一个，Enter 执行、Esc 关闭\r\n"
                + "· ⑬ 系统监控悬浮条：桌面右上角常驻 272×44 小条，实时显示 CPU / 内存 / 上下行网速，1.5 秒刷新；可拖动、右键暂停刷新 / 贴左侧 / 打开看板 / 隐藏，双击打开看板\r\n"
                + "· ⑭ 批量重命名 / 格式转换：六种改名规则（前缀+原名 / 原名+后缀 / 序号 / 日期+原名 / 正则替换 / 只改扩展名），带预览和冲突提示，改完可「撤销上次」；图片转 PNG/JPG/BMP/GIF、视频转 MP4、音频转 MP3、视频转 GIF 全部走内嵌 FFmpeg，输出加后缀不覆盖原文件\r\n"
                + "· ⑮ 键鼠宏录制：F8 开始/停止录制（低级键盘鼠标钩子，全局录），F9 回放（SendInput 还原键鼠），可设循环次数和 0.5~4 倍速，Esc 中止；宏存 宏\\*.mac\r\n"
                + "· ⑯ 离线密码本 / 两步验证：AES-256 + PBKDF2（12000 轮）存成 密码本.pwb，盘上无明文；内置离线 TOTP，选中条目直接显示六位码 + 倒计时，一键复制\r\n"
                + "· ⑰ 隐私痕迹清理：Chrome / Edge / Firefox 缓存与 Cookie、最近文档、缩略图与图标缓存、运行框历史、输入过的网址、剪贴板、DNS 缓存、回收站 —— 先扫描成清单再逐项清理\r\n"
                + "· Steam 成就解锁升级为「一键全解锁（全库直连）」：一次把库里所有有成就的游戏全部直连 Steamworks 写入，全程不打开任何游戏页；单款失败先原地重试一次，整轮跑完再统一补跑一遍，补跑成功的回退失败计数，仍失败的连带原因写进 _unlock_failed.txt\r\n"
                + "· 检查更新更耐用：清单直连失败自动换 jsDelivr / fastly / ghproxy / gh-proxy 镜像并各试两遍；下载直链按资产名（MiniToolbox.exe / default.exe / 小工具箱.exe）和版本号自动换名重试（以前 404 就是因为发布资产叫 default.exe）；全部失败时列出每个源的具体错误，并给「选本地安装包更新」和「打开发布页」两条退路\r\n"
                + "· 部署前清理：删掉 1.1 MB 测试残留（9 个 _*_test.txt、_uidump.txt、_batch_trace.txt、源码备份、空测试目录）＋ 4 处死代码（未调用的 HasFile / 保险箱测试钩子 / FreeLibrary 声明 / 失效的剪贴板自测入口已改挂 /lancliptest），普通使用不再往目录里写 _batch_trace.txt，编译 0 警告 0 错误\r\n\r\n"
                + "【v6.9 本次更新 · 再添六项工具】\r\n"
                + "· 入口：电脑配置页右上角「▸ 增强工具」→ 增强工具中心，现在共 11 张卡片\r\n"
                + "· ⑦ 剪贴板历史：程序运行期间自动记录复制过的文字（最多 200 条、钉住不删），Ctrl+Alt+V 打开，可搜索/复制/钉住/清空；本机 DPAPI 加密保存，盘上无明文\r\n"
                + "· ⑧ 录屏 / GIF 工坊：用内嵌 FFmpeg 抓屏录 MP4（可选全屏或自定义区域、10~60 帧），Ctrl+Alt+R 一键启停；录完可一键压成 GIF\r\n"
                + "· 截图标注器：截图后直接进编辑器，箭头/矩形/马赛克/文字/序号，可撤销、复制或保存\r\n"
                + "· ⑨ 配置备份还原：热键、保险箱、加速备份、剪贴板历史、整理/查重记录 打包成 zip，换电脑解回来即可（旧文件自动留 .bak）\r\n"
                + "· ⑩ 单文件加密：任何文件加密成 .mtbx（AES-256，原文件名一起存进包里），凭密码解回；原文件保留，自行决定删不删\r\n"
                + "· ⑪ 检查更新：连 GitHub 清单对比版本，有新版本可一键下载替换并重启\r\n"
                + "· 自测：剪贴板历史加密无明文、录屏 3 秒出片并成功转 GIF、备份 zip 可还原、文件加密错密码被拒且往返一致、标注五件套渲染正常\r\n\r\n"
                + "【v6.8 本次更新 · 六项增强工具】\r\n"
                + "· 入口：电脑配置页右上角「▸ 增强工具」→ 增强工具中心，六个卡片一键进入\r\n"
                + "· ① 全局热键中心：截图 / 主窗口 / 互传 / 增强中心 绑 Ctrl+Alt+S、H、T、G，双击行可改成任意 Ctrl/Alt/Shift/Win 组合，存 hotkeys.json 立即生效\r\n"
                + "· ② 局域网剪贴板：手机浏览器打开 http://电脑IP:8765/clip，直接读电脑剪贴板、也能改；照样不装 App\r\n"
                + "· ③ 一键游戏加速：游戏进程提「高」优先级 + 高性能电源 + 刷 DNS + 其他前台程序让路；全程写 加速备份.tsv，「一键还原」把电源计划、优先级、服务全部改回去\r\n"
                + "· ④ 桌面自动整理：按图片/视频/音频/文档/压缩包/安装包/其他 收进「整理-XX」，.lnk 快捷方式不动；每次留记录，「撤销上次整理」把文件原样搬回桌面\r\n"
                + "· ⑤ 本地加密保险箱：AES-256 + PBKDF2 加密成 保险箱.box，密码错就解不开、文件被拷走也读不出明文；条目分类/账号/密码/备注，复制密码 30 秒后自动清剪贴板\r\n"
                + "· ⑥ 重复文件清理：大小 → 首 8KB 指纹 → 全文 MD5 三重比对，每组保留第一个、其余可选；删除走回收站，可还原\r\n"
                + "· 自测：热键 4 个默认组合全部注册成功；剪贴板接口 GET/POST/JSON/页面四项全通；保险箱错密码被拒、无明文泄漏；查重 3 副本正确识别为 1 组；整理 5 个文件后撤销全数回位\r\n\r\n"
                + "【v6.7 本次更新 · 体检 / 启动项 / 看板 / 互传】\r\n"
                + "· 一键体检：15 项检查（组件完整性、Steam、网络与更新源、C 盘余量、WinRE、error.log、启动项数量），红灯项一键修复，报告可导出\r\n"
                + "· 开机启动管理：注册表 Run / 启动文件夹 / 自启服务全部列出，禁用前先备份、随时可恢复（自启服务需要管理员权限）\r\n"
                + "· 桌面看板：宠物右键开启，实时显示 CPU / 内存 / 网速 / C 盘余量，置顶小窗、可随意拖动\r\n"
                + "· 手机互传：手机连同一个 WiFi，浏览器打开显示的地址，直接把文件传到电脑，也能把电脑文件下载到手机；不装 App、不走数据线\r\n"
                + "· 三个入口都在「电脑配置」页右上角并排（一键体检 / 开机启动项 / 手机互传）\r\n"
                + "· 发布前自测：互传服务端到端（页面渲染 / 上传落盘 / 下载 / 404）通过，启动项枚举 84 项正常，看板读数正常，0 警告 0 错误\r\n\r\n"
                + "【v6.6 本次更新 · 直连解锁 + 全功能升级】\r\n"
                + "· Steam 一键解锁改为「直连 Steamworks」：不打开任何游戏页，整库平均 0.5 秒一款，一次跑完全部游戏\r\n"
                + "· 全库实跑 234 款 138 秒（成功 0 / 受保护 1 / 跳过 226 / 失败 7）——账号本来就已全解锁，结果真实\r\n"
                + "· 防误停：跑批中再点按钮会先弹窗确认，不会又变成「手动停止」；中断后可用游标接着跑\r\n"
                + "· 按钮实时显示「停止解锁 i/n」，报告新增「受保护」分类，一眼能看出哪些游戏写不进去\r\n"
                + "· 全功能优化：音乐搜索两家并发 + 12 秒超时重试；组件下载 / yt-dlp / ffmpeg 都加了 60 秒看门狗；修掉 stdout-stderr 死锁；图片改为不占用文件；schema 扫描结果缓存 5 分钟\r\n"
                + "· 每页能力补齐：关机页 10分/30分/1时/2时 快捷预设，清理页导出报告 + 定位文件，Steam 价值导出 CSV（并修掉两个按钮重叠），成就页导出全库清单 + 打开上次报告，重装页环境检测（reagentc /info），宠物右键「回到屏幕中央」\r\n"
                + "· 发布前清理：删掉 195 MB 第三方构建源码残留（dnkh_src）与 3 个从未被调用的死方法，重编译 0 警告 0 错误\r\n"
                + "· 版本号对齐 6.6，侧栏 / 启动画面 / 更新清单统一读同一个版本常量\r\n\r\n"
                + "【v6.5 更新 · 体积精简】\r\n"
                + "· 主程序从 136 MB 瘦身到 0.6 MB（577 KiB）：GCM、电脑开荒、视频下载三个大组件不再内嵌\r\n"
                + "· 这三个组件改为首次使用该功能时自动下载到本机并部署，下完即长期可用，之后离线照旧\r\n"
                + "· 下载会校验字节数，失败写 error.log 并提示重试；下载框为暗色自绘，不弹白窗口\r\n"
                + "· 已装过旧版、或用安装程序安装的机器，组件本来就在本地，打开即用、完全不联网\r\n"
                + "· 更新包随之从 136 MB 降到 0.57 MB，检查更新和下载几乎瞬时完成。更新另有 4 秒端到端实测\r\n"
                + "· 发布前全量清理：删掉 300 MB 构建/测试残留（github_update 里 279 MB 的展开包、PortableGit 15 MB、旧源码快照等），源码清掉 213 行死代码（5 个从未被调用的启动方法 + 1 个无用常量）\r\n"
                + "· 清完重新编译：0 警告 0 错误\r\n"
                + "· 版本号对齐 6.5\r\n\r\n"
                + "【v6.4 更新】\r\n"
                + "· 更新源正式上线 GitHub Releases：同目录 update_url.txt 指向 update.json，raw 清单 + 直链下载，全链路实测通过\r\n"
                + "· 修掉老版 .NET 的 TLS 缺陷：拉清单、下更新包都强制 TLS1.2，不再静默退回「已是最新」\r\n"
                + "· 替换 exe 改为「杀进程 → 最多重试 40 次 → 大小校验 → 失败自动回滚」，句柄没释放也不会装半截\r\n"
                + "· 更新过程写日志到 %TEMP%\\mtb_update.log，装失败弹窗可直接查原因\r\n"
                + "· 版本号对齐 6.4\r\n\r\n"
                + "【v6.3】\r\n"
                + "· 内置音乐升级为多平台搜索：网易云 / 酷狗 / 两个平台一起搜，列表新增「平台」列\r\n"
                + "· 新增一起听：多人同步听歌，房间码直连，曲库 / 进度 / 聊天全同步，音频自动分发\r\n"
                + "· 软件内自更新：检查更新 → 下载进度 → 自动替换并重启（读同目录 update_url.txt 的更新源）\r\n"
                + "· 音乐页顶部换版：三段深色平台切换按钮 + 输入框底板，去掉系统下拉框的白底\r\n"
                + "· 清理全部调试残留与测试脚本，主程序 0 警告 0 错误重编译\r\n\r\n"
                + "【v6.2】\r\n"
                + "· 定时定点提醒：可自定义标题 / 内容 / 时间 / 重复 / 提示音，到点弹出置顶提醒卡\r\n"
                + "· 白边根因定论：页面亮灰块是自定义背景图透过蒙层，把蒙层加深到 205 / 220 彻底解决\r\n\r\n"
                + "【v6.1】\r\n"
                + "· 移除拼图模块，连同它的数据与文件一并删除\r\n"
                + "· 全量数据与文件审查：修掉 10 处硬编码绝对路径，正式运行不再往程序目录写调试文件\r\n"
                + "· 界面四层美化：圆角玻璃按钮（渐变 + 悬停提亮 + 按下位移）、对角渐变背景、提亮的输入框、侧栏选中胶囊\r\n"
                + "· 所有输入框补上真正的占位提示，切英文同步\r\n"
                + "· 所有下拉框改为自绘深色菜单（系统绘制的那种会固定白底，改不掉）\r\n\r\n"
                + "【v6.0】\r\n"
                + "· 桌面宠物：等级 / 经验 / 饱食度养成，多套素材分组，UDP 广播联机（同局域网可看到朋友的宠物）\r\n"
                + "· 新增视频下载、音乐下载、电脑开荒、桌面宠物四个模块\r\n\r\n"
                + "【v5】\r\n"
                + "· C 盘清理重做为空间热力图：双层钻取、大文件 Top 榜、占用统计卡\r\n"
                + "· 电脑配置新增实时负载（CPU / 内存 / C 盘）与显卡温度 / 功耗 / 显存\r\n"
                + "· 智能条件触发：空闲超时、电量过低、进程结束、CPU 高负载、显卡过热\r\n"
                + "· 去水印支持多区域叠加、图片批量队列、视频只处理指定时间段\r\n\r\n"
                + "【v4】\r\n"
                + "· 视频 / 音乐下载；全深色主题；十几个模块收进一个窗口\r\n\r\n"
                + "提示：关机 / 注销类动作可能中断未保存的工作，建议保持「到点弹窗确认」开启。";
        }

        void ShowSourceInfo()
        {
            string s = T("小工具箱是 C# WinForms 程序，源码公开在 GitHub：\n"
                + "https://github.com/wj5666619/MiniToolbox\n\n"
                + "官方网站（下载 / 截图 / 更新日志）：\n"
                + "https://wj5666619.github.io/MiniToolbox/\n\n"
                + "源码结构：定时关机.cs 为入口与主窗骨架，Src\\*.cs 共 125 个类型文件（各功能页、皮肤、音效、配置）。\n\n"
                + "编译只依赖系统自带的 csc（.NET Framework 4.x），不用 Visual Studio、不用 NuGet：\n\n"
                + "  .\\build.ps1          # 编译 → 签名 → 算 SHA256 → 7z 打包\n"
                + "  .\\build.ps1 -NoSign  # 跳过签名\n\n"
                + "手写命令时务必带上 Src\\*.cs，只编 定时关机.cs 会缺类型：\n\n"
                + "  csc.exe /nologo /target:winexe /optimize+ /codepage:65001\n"
                + "    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll\n"
                + "    /r:System.Web.Extensions.dll /r:Accessibility.dll\n"
                + "    /win32icon:定时关机.ico\n"
                + "    /resource:sam_tool.zip,SamZip /resource:clean_tool.zip,CleanZip\n"
                + "    /out:小工具箱.exe 定时关机.cs Src\\*.cs\n\n"
                + "欢迎读、改、提 Issue；自行构建的版本请自己签名。",
                "Mini Toolbox is a C# WinForms app with public source:\n"
                + "https://github.com/wj5666619/MiniToolbox\n\n"
                + "Official site (downloads, screenshots, changelog):\n"
                + "https://wj5666619.github.io/MiniToolbox/\n\n"
                + "Source layout: main.cs is the entry + main window shell; Src\\*.cs holds 125 type files (pages, skins, sounds, config).\n\n"
                + "Build with the csc that ships with Windows (.NET Framework 4.x) — no Visual Studio, no NuGet:\n\n"
                + "  .\\build.ps1          # compile, sign, hash, pack\n"
                + "  .\\build.ps1 -NoSign  # skip signing\n\n"
                + "When calling csc by hand, always include Src\\*.cs:\n\n"
                + "  csc.exe /nologo /target:winexe /optimize+ /codepage:65001\n"
                + "    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll\n"
                + "    /r:System.Web.Extensions.dll /r:Accessibility.dll\n"
                + "    /win32icon:app.ico\n"
                + "    /resource:sam_tool.zip,SamZip /resource:clean_tool.zip,CleanZip\n"
                + "    /out:toolbox.exe main.cs Src\\*.cs\n\n"
                + "Issues and PRs welcome; sign your own builds.");
            MessageBox.Show(this, s, T("源码与编译", "Source & build"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ExportInfo()
        {
            try
            {
                if (Policy.NoDiagExport)
                {
                    Diag.Warn("策略禁用诊断导出");
                    MessageBox.Show(this, T("管理员策略已禁用诊断导出。", "Diagnostics export is disabled by policy."),
                        T("反馈与建议", "Feedback"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!CanWriteDir(dir))
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string zip = Path.Combine(dir, "诊断_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".zip");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("小工具箱 v" + APP_VER);
                sb.AppendLine("程序: " + Application.ExecutablePath);
                sb.AppendLine("系统: " + Environment.OSVersion.VersionString + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine("机器名: " + Environment.MachineName);
                sb.AppendLine("处理器数: " + Environment.ProcessorCount);
                sb.AppendLine("配置目录: " + (Cfg.Dir.Length > 0 ? Cfg.Dir : "(未初始化)"));
                sb.AppendLine("日志文件: " + (Diag.FilePath.Length > 0 ? Diag.FilePath : "(未初始化)"));
                try
                {
                    Screen sc = Screen.PrimaryScreen;
                    sb.AppendLine("屏幕: " + sc.Bounds.Width + "x" + sc.Bounds.Height + " / 工作区 " + sc.WorkingArea.Width + "x" + sc.WorkingArea.Height);
                }
                catch (Exception dg2018_1) { Diag.Swallow("UpdateForm.cs:2018", dg2018_1); }
                sb.AppendLine("皮肤与界面: 缩放 " + (int)(Ui2.UiScale * 100) + "% / 语言 " + Lang.NAME[Math.Max(0, Math.Min(Lang.NAME.Length - 1, Lang.Id))]);
                sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("本包只含版本信息与最近日志，不含聊天记录、密码库、剪贴板内容。");

                using (FileStream fs = new FileStream(zip, FileMode.Create))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    AddTextEntry(za, "版本与环境.txt", sb.ToString());
                    if (Diag.FilePath.Length > 0 && File.Exists(Diag.FilePath))
                        za.CreateEntryFromFile(Diag.FilePath, "日志/小工具箱.log", CompressionLevel.Optimal);
                    if (Cfg.FilePath.Length > 0 && File.Exists(Cfg.FilePath))
                        za.CreateEntryFromFile(Cfg.FilePath, "配置.json", CompressionLevel.Optimal);
                }
                try { Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch (Exception dg2033_1) { Diag.Swallow("UpdateForm.cs:2033", dg2033_1); }
                Audit.Write("导出诊断包", zip);
                DialogResult send = MessageBox.Show(this,
                    T("诊断包已生成：\n" + zip + "\n\n现在打开邮件，把诊断包发到 " + FEEDBACK_MAIL + " 吗？\n邮件会自动写好版本、系统信息和问题模板，请把诊断包作为附件。",
                      "Diagnostics saved:\n" + zip + "\n\nOpen a mail draft to " + FEEDBACK_MAIL + " now?\nThe draft will contain version, system info and a template; attach the zip before sending."),
                    T("反馈与建议", "Feedback"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (send == DialogResult.Yes) OpenFeedbackMail(zip);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("导出失败：", "Export failed: ") + ex.Message, T("反馈与建议", "Feedback"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // v19.12：优先调系统邮件应用；没有桌面邮件应用时，复制邮件内容并打开 163 网页邮箱
        void OpenFeedbackMail(string zip)
        {
            string subject = "小工具箱 v" + APP_VER + " 反馈";
            string body = "版本：v" + APP_VER + "\r\n"
                + "系统：" + OSName() + " / " + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + "\r\n"
                + "诊断包：" + zip + "\r\n\r\n"
                + "问题 / 建议：\r\n\r\n\r\n"
                + "（请把诊断包 " + Path.GetFileName(zip) + " 作为附件一起发送）";
            string full = "收件人：" + FEEDBACK_MAIL + "\r\n主题：" + subject + "\r\n\r\n" + body;
            try { Clipboard.SetText(full); }
            catch (Exception dgClip) { Diag.Swallow("UpdateForm.cs:OpenFeedbackMail:clip", dgClip); }

            string prog = MailProgId();
            bool desktopMail = prog.Length > 0
                && !prog.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase)
                && !prog.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase);
            if (desktopMail)
            {
                try
                {
                    string url = "mailto:" + FEEDBACK_MAIL
                        + "?subject=" + Uri.EscapeDataString(subject)
                        + "&body=" + Uri.EscapeDataString(body);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    Audit.Write("打开反馈邮件", FEEDBACK_MAIL);
                    return;
                }
                catch (Exception ex) { Diag.Swallow("UpdateForm.cs:OpenFeedbackMail:mailto", ex); }
            }

            try { Process.Start(new ProcessStartInfo("https://mail.163.com/") { UseShellExecute = true }); }
            catch (Exception dgWeb) { Diag.Swallow("UpdateForm.cs:OpenFeedbackMail:web", dgWeb); }
            Audit.Write("打开反馈网页邮箱", FEEDBACK_MAIL);
            MessageBox.Show(this,
                T("邮件内容已复制到剪贴板。\n已打开 163 邮箱网页：登录后粘贴发送，并把诊断包作为附件。\n\n收件人：" + FEEDBACK_MAIL,
                  "The mail content is on your clipboard.\n163 webmail has been opened: sign in, paste and send, and attach the zip.\n\nTo: " + FEEDBACK_MAIL),
                T("反馈与建议", "Feedback"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string MailProgId()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\mailto\UserChoice"))
                    return k == null ? "" : (k.GetValue("ProgId") + "");
            }
            catch (Exception dg) { Diag.Swallow("UpdateForm.cs:MailProgId", dg); return ""; }
        }

        static string OSName()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = k == null ? "" : (k.GetValue("ProductName") + "");
                    string ver = k == null ? "" : (k.GetValue("DisplayVersion") + "");
                    if (name.Length > 0) return name + (ver.Length > 0 ? " " + ver : "");
                }
            }
            catch (Exception dg) { Diag.Swallow("UpdateForm.cs:OSName", dg); }
            return Environment.OSVersion.VersionString;
        }

        static void AddTextEntry(ZipArchive za, string name, string body)
        {
            ZipArchiveEntry e = za.CreateEntry(name, CompressionLevel.Optimal);
            using (StreamWriter w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(body);
        }

        static bool CanWriteDir(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".diag_write_test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }
}