using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ShutdownTool
{
    /// <summary>
    /// v19.14：控制中心。把任务、清理、网络、企业、音效、插件、统计收进一个独立窗口，
    /// 不改主窗体 9 个导航槽，也不动 Alt+1…9 与语言数组。
    /// </summary>
    class ControlCenterForm : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static int StartPage = 0;        readonly MainForm owner;
        readonly string baseDir;
        Panel body, header;
        RoundBtn[] nav = new RoundBtn[7];
        Panel[] pages = new Panel[7];
        int active = 0;
        System.Windows.Forms.Timer tick;
        // 总览
        Label ovUse, ovClean, ovTask, ovNet, emptyRecent;
        ListView lvRecent;
        // 任务
        ListView lvTasks;
        Label taskDetail, emptyTasks;
        // 清理
        ListView lvClean;
        Label cleanSum, emptyClean;
        // 网络
        ListView lvNet;
        Button btnNet;
        // 企业
        Label polState, polSource, polSim;
        CheckBox ckNoUpdate, ckNoDiag, ckVerbose;
        ListView lvAudit;
        // 音效
        Button btnSkin, btnPack, btnVol;
        int skinSel, packSel, volSel;
        Panel skinSwatch;
        CheckBox ckNight, ckFocus;
        readonly List<CheckBox> evChecks = new List<CheckBox>();
        Label skinHint;
        // 插件
        ListView lvPlug;
        // 统计
        Label stCount, stMin, stStreak, stClean;
        Panel graph;
        int[] last7 = new int[7];

        public ControlCenterForm(MainForm owner, string baseDir)
        {
            this.owner = owner;
            this.baseDir = baseDir;
            Text = "控制中心";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1040, 700);
            BackColor = UiKit.Paper ? UiKit.PaperBg : UiKit.A(10, 14, 20);
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Close(); return; }
                if (e.KeyCode == Keys.F1) { ShowShortcuts(); return; }
                if (e.KeyCode == Keys.F5) { RefreshActive(); return; }
                if (e.Alt && e.KeyCode == Keys.Left && active > 0) { ShowPage(active - 1); return; }
                if (e.Alt && e.KeyCode == Keys.Right && active < pages.Length - 1) { ShowPage(active + 1); return; }
            };

            BuildHeader();
            BuildNav();
            BuildBody();
            ShowPage(StartPage);

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate
            {
                try
                {
                    if (active == 0) RefreshOverview();
                    else if (active == 1) RefreshTasks();
                }
                catch (Exception ex) { Diag.Swallow("ControlCenter.tick", ex); }
            };
            tick.Start();
            FormClosed += delegate { try { tick.Stop(); tick.Dispose(); } catch { } };
        }

        Color CardFill { get { return UiKit.Paper ? UiKit.PaperCard : UiKit.A(24, 30, 40); } }
        Color MainText { get { return UiKit.Paper ? UiKit.Ink : Color.White; } }
        Color SubText { get { return UiKit.Paper ? Color.FromArgb(92, 82, 66) : UiKit.A(139, 156, 176); } }
        Color Accent { get { return UiKit.ThemeColor; } }

        void BuildHeader()
        {
            header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(ClientSize.Width, 76);
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                try
                {
                    Rectangle r = header.ClientRectangle;
                    if (UiKit.Paper)
                    {
                        using (SolidBrush b = new SolidBrush(UiKit.Ink)) e.Graphics.FillRectangle(b, r);
                        using (Pen p = new Pen(Color.FromArgb(244, 255, 61), 3f))
                            e.Graphics.DrawLine(p, 0, r.Height - 3, r.Width, r.Height - 3);
                    }
                    else
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(r, UiKit.A(20, 64, 82), UiKit.A(11, 18, 27), 0f))
                            e.Graphics.FillRectangle(b, r);
                        using (Pen p = new Pen(UiKit.AA(80, 45, 212, 191), 1f))
                            e.Graphics.DrawLine(p, 0, r.Height - 1, r.Width, r.Height - 1);
                    }
                }
                catch { }
            };
            Controls.Add(header);

            Label title = new Label();
            title.Text = "控制中心";
            title.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
            title.ForeColor = UiKit.Paper ? Color.White : Color.White;
            title.AutoSize = true;
            title.BackColor = Color.Transparent;
            title.Location = new Point(26, 14);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "任务 · 空间 · 网络 · 策略 · 音效 · 插件 · 统计";
            sub.Font = new Font("Microsoft YaHei UI", 9F);
            sub.ForeColor = UiKit.Paper ? Color.FromArgb(220, 246, 241, 229) : UiKit.A(139, 205, 220);
            sub.AutoSize = true;
            sub.BackColor = Color.Transparent;
            sub.Location = new Point(28, 48);
            header.Controls.Add(sub);

            RoundBtn close = new RoundBtn(UiKit.A(30, 39, 52), Color.White);            close.Text = "✕";
            close.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            close.Size = new Size(34, 30);
            close.Location = new Point(ClientSize.Width - 50, 20);
            close.NoGloss = true;
            close.Click += delegate { Close(); };
            header.Controls.Add(close);

            RoundBtn help = new RoundBtn(UiKit.A(30, 39, 52), Color.White);
            help.Text = "?";
            help.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            help.Size = new Size(34, 30);
            help.Location = new Point(ClientSize.Width - 94, 20);
            help.NoGloss = true;
            help.TabStop = false;
            help.Click += delegate { ShowShortcuts(); };
            header.Controls.Add(help);
        }

        void BuildNav()
        {
            Panel navBar = new Panel();
            navBar.Location = new Point(0, 76);
            navBar.Size = new Size(190, ClientSize.Height - 76);
            navBar.BackColor = UiKit.Paper ? UiKit.PaperCard : UiKit.A(14, 19, 26);
            navBar.Paint += delegate(object s, PaintEventArgs e)
            {
                try
                {
                    using (Pen p = new Pen(UiKit.Paper ? UiKit.PaperLine : UiKit.AA(40, 45, 212, 191), 1f))
                        e.Graphics.DrawLine(p, navBar.ClientSize.Width - 1, 0, navBar.ClientSize.Width - 1, navBar.ClientSize.Height);
                }
                catch { }
            };
            Controls.Add(navBar);

            string[] names = { "总览", "任务中心", "空间与清理", "网络体检", "企业与审计", "外观与音效", "插件与统计" };
            for (int i = 0; i < names.Length; i++)
            {
                RoundBtn b = new RoundBtn(UiKit.A(24, 31, 41), MainText);
                b.Text = names[i];
                b.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
                b.Size = new Size(158, 42);
                b.Location = new Point(16, 20 + i * 52);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.NoGloss = true;
                b.TabStop = false;
                int idx = i;
                b.Click += delegate { ShowPage(idx); };
                navBar.Controls.Add(b);
                nav[i] = b;
            }

            Label tip = new Label();
            tip.Text = "Esc 关闭\n入口：页头 / 托盘\nCtrl+K 可搜索";
            tip.Font = new Font("Microsoft YaHei UI", 8F);
            tip.ForeColor = SubText;
            tip.AutoSize = false;
            tip.Size = new Size(160, 58);
            tip.Location = new Point(18, navBar.Height - 74);
            tip.BackColor = Color.Transparent;
            navBar.Controls.Add(tip);
        }

        void BuildBody()
        {
            body = new Panel();
            body.Location = new Point(190, 76);
            body.Size = new Size(ClientSize.Width - 190, ClientSize.Height - 76);
            body.BackColor = Color.Transparent;
            Controls.Add(body);
            body.BringToFront();

            for (int i = 0; i < pages.Length; i++)
            {
                Panel p = new Panel();
                p.Dock = DockStyle.Fill;
                p.BackColor = Color.Transparent;
                p.Visible = false;
                body.Controls.Add(p);
                pages[i] = p;
            }
            BuildOverview(pages[0]);
            BuildTasks(pages[1]);
            BuildClean(pages[2]);
            BuildNet(pages[3]);
            BuildPolicy(pages[4]);
            BuildSound(pages[5]);
            BuildPlugins(pages[6]);
        }

        void RefreshActive()
        {
            if (active == 0) RefreshOverview();
            else if (active == 1) RefreshTasks();
            else if (active == 2) RefreshClean();
            else if (active == 3) RunNetCheck();
            else if (active == 4) RefreshPolicy();
            else if (active == 5) RefreshSound();
            else if (active == 6) { RefreshPlugins(); RefreshStats(); }
        }

        void ShowPage(int idx)
        {
            active = idx;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = (i == idx);
                if (nav[i] != null)
                {
                    nav[i].Mark = (i == idx);
                    nav[i].NoGloss = (i != idx);
                    nav[i].Invalidate();
                }
            }
            if (idx == 0) RefreshOverview();
            else if (idx == 1) RefreshTasks();
            else if (idx == 2) RefreshClean();
            else if (idx == 4) RefreshPolicy();
            else if (idx == 5) RefreshSound();
            else if (idx == 6) { RefreshPlugins(); RefreshStats(); }
            Sfx.Switch();
        }

        CcCard Card(Control parent, int x, int y, int w, int h, string caption, Color accent)
        {
            CcCard c = new CcCard();
            c.Fill = CardFill;
            c.Line = UiKit.Paper ? UiKit.PaperLine : UiKit.A(48, 58, 74);
            c.Accent = accent;
            c.Caption = caption;
            c.TextColor = MainText;
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            parent.Controls.Add(c);
            return c;
        }

        Label Lab(Control parent, string text, int x, int y, int w, int h, float size, Color color, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoEllipsis = true;
            parent.Controls.Add(l);
            return l;
        }

        Button Btn(Control parent, string text, int x, int y, int w, int h, Color bg, Color fg, EventHandler click)
        {
            RoundBtn b = new RoundBtn(bg, fg);
            b.Text = text;
            b.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.NoGloss = true;
            if (click != null) b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        ListView List(Control parent, int x, int y, int w, int h, string[] cols, int[] widths)
        {
            Panel head = new Panel();
            head.Location = new Point(x, y);
            head.Size = new Size(w, 24);
            head.BackColor = UiKit.Paper ? UiKit.PaperCard : UiKit.A(26, 32, 42);
            parent.Controls.Add(head);
            int cx = 4;
            for (int i = 0; i < cols.Length; i++)
            {
                Label l = new Label();
                l.Text = cols[i];
                l.Location = new Point(cx, 3);
                l.Size = new Size(Math.Max(30, widths[i] - 4), 18);
                l.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Bold);
                l.ForeColor = SubText;
                l.BackColor = Color.Transparent;
                l.AutoEllipsis = true;
                head.Controls.Add(l);
                cx += widths[i];
            }

            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HeaderStyle = ColumnHeaderStyle.None;
            lv.BorderStyle = BorderStyle.None;
            lv.BackColor = UiKit.Paper ? UiKit.PaperCard : UiKit.A(20, 26, 35);
            lv.ForeColor = MainText;
            lv.Font = new Font("Microsoft YaHei UI", 9F);
            lv.Location = new Point(x, y + 24);
            lv.Size = new Size(w, Math.Max(30, h - 24));
            lv.HandleCreated += delegate
            {
                try { SetWindowTheme(lv.Handle, "DarkMode_Explorer", "DarkMode_Explorer"); } catch { }
            };
            for (int i = 0; i < cols.Length; i++) lv.Columns.Add(cols[i], widths[i]);
            parent.Controls.Add(lv);
            return lv;
        }

        static string FmtSize(long b)
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("0.0") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        static Color StateColor(string s)
        {
            if (s == "完成") return UiKit.A(45, 212, 191);
            if (s == "失败") return UiKit.A(248, 113, 113);
            if (s == "进行中") return UiKit.A(56, 189, 248);
            if (s == "中断" || s == "已取消") return UiKit.A(240, 180, 90);
            return UiKit.A(139, 156, 176);
        }

        // ---------------- 总览 ----------------
        void ShowShortcuts()
        {
            MessageBox.Show(this,
                "主窗口\n" +
                "Alt+1…9    直接切换 9 个功能页\n" +
                "Ctrl+K     打开命令面板（搜功能、设置、控制中心）\n" +
                "F1         打开这份快捷键地图\n" +
                "Esc        关闭当前弹窗 / 返回\n\n" +
                "控制中心\n" +
                "Alt+←/→    上一页 / 下一页\n" +
                "F5         刷新当前页\n" +
                "Esc        关闭\n\n" +
                "托盘\n" +
                "左键双击   恢复主窗口\n" +
                "右键       控制中心、扫描、计时、退出",
                "快捷键地图", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        void BuildOverview(Panel p)
        {
            int w = (ClientSize.Width - 190 - 40 - 30) / 4;
            CcCard c1 = Card(p, 20, 18, w, 108, "今日使用", Accent);
            ovUse = Lab(c1, "—", 16, 34, w - 32, 44, 18F, MainText, true);
            Lab(c1, "次数 · 分钟", 16, 76, w - 32, 20, 8F, SubText, false);
            CcCard c2 = Card(p, 20 + w + 10, 18, w, 108, "上次清理", UiKit.A(45, 212, 191));
            ovClean = Lab(c2, "—", 16, 34, w - 32, 44, 18F, MainText, true);
            Lab(c2, "扫描记录 · 非删除量", 16, 76, w - 32, 20, 8F, SubText, false);
            CcCard c3 = Card(p, 20 + (w + 10) * 2, 18, w, 108, "任务", UiKit.A(56, 189, 248));
            ovTask = Lab(c3, "—", 16, 34, w - 32, 44, 18F, MainText, true);
            Lab(c3, "进行中 / 最近记录", 16, 76, w - 32, 20, 8F, SubText, false);
            CcCard c4 = Card(p, 20 + (w + 10) * 3, 18, w, 108, "网络", UiKit.A(240, 180, 90));
            ovNet = Lab(c4, "—", 16, 34, w - 32, 44, 18F, MainText, true);
            Lab(c4, "上次体检结果", 16, 76, w - 32, 20, 8F, SubText, false);

            CcCard recent = Card(p, 20, 142, ClientSize.Width - 190 - 330, 330, "最近任务", Accent);
            lvRecent = List(recent, 14, 34, recent.Width - 28, recent.Height - 48, new string[] { "状态", "任务", "时间" }, new int[] { 70, 360, 110 });
            emptyRecent = Lab(recent, "暂无任务\r\n运行一次扫描、清理或下载后，这里会出现记录", 20, 130, recent.Width - 40, 60, 9F, SubText, false);
            emptyRecent.TextAlign = ContentAlignment.MiddleCenter;
            emptyRecent.AutoSize = false;
            emptyRecent.BackColor = CardFill;
            emptyRecent.BringToFront();
            lvRecent.DoubleClick += delegate { ShowPage(1); };

            CcCard quick = Card(p, ClientSize.Width - 190 - 300, 142, 280, 406, "快捷操作", UiKit.A(45, 212, 191));
            int y = 38;
            Btn(quick, "打开清理页", 16, y, 248, 36, UiKit.A(30, 39, 52), MainText, delegate { owner.CcOpenPage("clean"); }); y += 46;
            Btn(quick, "运行推荐清理", 16, y, 248, 36, UiKit.A(20, 122, 110), Color.White, delegate { owner.CcRunRecommendedClean(); }); y += 46;
            Btn(quick, "检查更新", 16, y, 248, 36, UiKit.A(30, 39, 52), MainText, delegate { owner.CcOpenUpdate(); }); y += 46;
            Btn(quick, "网络体检", 16, y, 248, 36, UiKit.A(30, 39, 52), MainText, delegate { ShowPage(3); RunNetCheck(); }); y += 46;
            Btn(quick, "打开诊断日志", 16, y, 248, 36, UiKit.A(30, 39, 52), MainText, delegate { owner.CcOpenLogs(); });
            y += 46;
            Btn(quick, "配置备份 / 还原", 16, y, 248, 36, UiKit.A(30, 39, 52), MainText, delegate { owner.CcOpenBackup(); });
            y += 46;
            Btn(quick, "定时任务 / 提醒", 16, y, 248, 36, UiKit.A(30, 39, 52), MainText, delegate { owner.CcOpenPage("power"); });

            Lab(p, "控制中心不改主窗体导航：任务、诊断、策略、插件都在这里汇总，清理与下载仍走各自页面。", 24, 560, ClientSize.Width - 240, 50, 8.5F, SubText, false);
        }

        void RefreshOverview()
        {
            try
            {
                int count, min, streak;
                UsageStats.Snapshot(out count, out min, out streak);
                ovUse.Text = count + " 次 / " + min + " 分";
                long lastClean = CleanHistory.Count > 0 ? CleanHistory.At(CleanHistory.Count - 1) : 0;
                ovClean.Text = lastClean > 0 ? FmtSize(lastClean) : "暂无记录";
                List<TaskHub.Item> ts = TaskHub.Snapshot();
                int run = 0;
                for (int i = 0; i < ts.Count; i++) if (ts[i].State == "进行中") run++;
                ovTask.Text = run + " / " + ts.Count;
                ovNet.Text = netSummary.Length > 0 ? netSummary : "未体检";

                lvRecent.BeginUpdate();
                lvRecent.Items.Clear();
                for (int i = 0; i < ts.Count && i < 8; i++)
                {
                    ListViewItem li = new ListViewItem(new string[] { ts[i].State, ts[i].Title, ts[i].Updated.ToString("HH:mm:ss") });
                    li.ForeColor = StateColor(ts[i].State);
                    lvRecent.Items.Add(li);
                }
                lvRecent.EndUpdate();
                lvRecent.Visible = ts.Count > 0;
                if (emptyRecent != null) { emptyRecent.Visible = ts.Count == 0; if (emptyRecent.Visible) emptyRecent.BringToFront(); }
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.overview", ex); }
        }

        // ---------------- 任务 ----------------
        void BuildTasks(Panel p)
        {
            CcCard c = Card(p, 20, 18, ClientSize.Width - 190 - 40, 440, "任务列表", Accent);
            lvTasks = List(c, 14, 34, c.Width - 28, 300, new string[] { "状态", "任务", "进度", "更新时间" }, new int[] { 76, 370, 92, 100 });
            lvTasks.Height = 300;
            lvTasks.SelectedIndexChanged += delegate
            {
                try
                {
                    if (lvTasks.SelectedItems.Count == 0) { taskDetail.Text = "选中一条任务查看详情。"; return; }
                    TaskHub.Item it = lvTasks.SelectedItems[0].Tag as TaskHub.Item;
                    if (it != null) taskDetail.Text = it.Detail + "    ·    " + it.Kind + "    ·    " + it.Id;
                }
                catch { }
            };
            taskDetail = Lab(c, "选中一条任务查看详情。", 16, 390, c.Width - 32, 34, 8.5F, SubText, false);
            emptyTasks = Lab(c, "暂无任务\r\n去首页跑一次扫描、清理或下载，任务会实时出现在这里", 20, 150, c.Width - 40, 70, 9.5F, SubText, false);
            emptyTasks.TextAlign = ContentAlignment.MiddleCenter;
            emptyTasks.AutoSize = false;
            emptyTasks.BackColor = CardFill;
            emptyTasks.BringToFront();

            CcCard b = Card(p, 20, 472, ClientSize.Width - 190 - 40, 100, "操作", UiKit.A(45, 212, 191));
            Btn(b, "刷新", 16, 38, 100, 34, UiKit.A(30, 39, 52), MainText, delegate { RefreshTasks(); });
            Btn(b, "清空已完成", 126, 38, 120, 34, UiKit.A(30, 39, 52), MainText, delegate
            {
                TaskHub.ClearFinished();
                RefreshTasks();
                Sfx.Copy();
            });
            Btn(b, "复制清单", 256, 38, 110, 34, UiKit.A(30, 39, 52), MainText, delegate { CopyTaskList(); });
            Lab(b, "任务记录落盘在程序目录的 任务记录.txt；重启后未完成任务会标为「中断」。", 16, 74, b.Width - 32, 20, 8F, SubText, false);
        }

        void RefreshTasks()
        {
            try
            {
                lvTasks.BeginUpdate();
                lvTasks.Items.Clear();
                List<TaskHub.Item> ts = TaskHub.Snapshot();
                for (int i = 0; i < ts.Count; i++)
                {
                    string prog = ts[i].Percent < 0 ? "—" : ts[i].Percent + "%";
                    ListViewItem li = new ListViewItem(new string[] { ts[i].State, ts[i].Title, prog, ts[i].Updated.ToString("HH:mm:ss") });
                    li.Tag = ts[i];
                    li.ForeColor = StateColor(ts[i].State);
                    lvTasks.Items.Add(li);
                }
                lvTasks.EndUpdate();
                lvTasks.Visible = ts.Count > 0;
                if (emptyTasks != null) emptyTasks.Visible = ts.Count == 0;
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.tasks", ex); }
        }

        void CopyTaskList()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                List<TaskHub.Item> ts = TaskHub.Snapshot();
                for (int i = 0; i < ts.Count; i++)
                    sb.AppendLine(ts[i].State + " | " + ts[i].Title + " | " + (ts[i].Percent < 0 ? "—" : ts[i].Percent + "%") + " | " + ts[i].Detail);
                Clipboard.SetText(sb.ToString());
                Sfx.Copy();
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.copyTasks", ex); }
        }

        // ---------------- 清理 ----------------
        void BuildClean(Panel p)
        {
            CcCard c = Card(p, 20, 18, ClientSize.Width - 190 - 40, 390, "清理扫描历史", UiKit.A(45, 212, 191));
            lvClean = List(c, 14, 34, c.Width - 28, 310, new string[] { "时间", "扫描大小", "文件数" }, new int[] { 150, 160, 120 });
            cleanSum = Lab(c, "—", 16, 350, c.Width - 32, 26, 9F, SubText, false);
            emptyClean = Lab(c, "暂无扫描记录\r\n在首页扫描一次后，这里会按时间列出体积与文件数", 20, 130, c.Width - 40, 70, 9.5F, SubText, false);
            emptyClean.TextAlign = ContentAlignment.MiddleCenter;
            emptyClean.AutoSize = false;
            emptyClean.BackColor = CardFill;
            emptyClean.BringToFront();

            CcCard b = Card(p, 20, 422, ClientSize.Width - 190 - 40, 150, "空间安全", Accent);
            Btn(b, "打开回收站（还原）", 16, 38, 150, 36, UiKit.A(30, 39, 52), MainText, delegate { OpenRecycle(); });
            Btn(b, "导出历史 CSV", 176, 38, 140, 36, UiKit.A(30, 39, 52), MainText, delegate { ExportCleanCsv(); });
            Btn(b, "重新扫描", 326, 38, 120, 36, UiKit.A(20, 122, 110), Color.White, delegate { owner.CcOpenPage("clean"); });
            Lab(b, "推荐清理（v8）：用户目录项目送入回收站，可在回收站还原；Windows 系统目录仍永久删除。极致清理全部永久删除。历史只记录体积与文件数。", 16, 82, b.Width - 32, 58, 8F, SubText, false);
        }

        void RefreshClean()
        {
            try
            {
                lvClean.BeginUpdate();
                lvClean.Items.Clear();
                long sum = 0;
                for (int i = CleanHistory.Count - 1; i >= 0; i--)
                {
                    long b = CleanHistory.At(i);
                    sum += b;
                    lvClean.Items.Add(new ListViewItem(new string[] { CleanHistory.WhenAt(i), FmtSize(b), CleanHistory.FilesAt(i).ToString("N0") }));
                }
                lvClean.EndUpdate();
                lvClean.Visible = CleanHistory.Count > 0;
                cleanSum.Text = CleanHistory.Count + " 条记录 · 合计扫描 " + FmtSize(sum);
                if (emptyClean != null) emptyClean.Visible = CleanHistory.Count == 0;
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.clean", ex); }
        }

        void OpenRecycle()
        {
            try { Process.Start("explorer.exe", "shell:RecycleBinFolder"); Sfx.Launch(); }
            catch (Exception ex) { MessageBox.Show(this, "打开回收站失败：" + ex.Message, "控制中心"); }
        }

        void ExportCleanCsv()
        {
            try
            {
                string path = Path.Combine(baseDir, "清理历史导出.csv");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("时间,扫描字节,文件数");
                for (int i = CleanHistory.Count - 1; i >= 0; i--)
                    sb.AppendLine(CleanHistory.WhenAt(i) + "," + CleanHistory.At(i) + "," + CleanHistory.FilesAt(i));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Sfx.Save();
                MessageBox.Show(this, "已导出：" + path, "控制中心");
            }
            catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "控制中心"); }
        }

        // ---------------- 网络 ----------------
        string netSummary = "";
        void BuildNet(Panel p)
        {
            CcCard c = Card(p, 20, 18, ClientSize.Width - 190 - 40, 480, "一页网络诊断", Accent);
            btnNet = Btn(c, "开始体检", 16, 34, 120, 36, UiKit.A(20, 122, 110), Color.White, delegate { RunNetCheck(); });
            Btn(c, "复制报告", 146, 34, 110, 36, UiKit.A(30, 39, 52), MainText, delegate
            {
                try { Clipboard.SetText(lvNet.Text); Sfx.Copy(); } catch { }
            });
            Lab(c, "检查 DNS、系统代理、默认网关、更新源 HEAD；全部在本机完成，不上传任何内容。", 270, 42, c.Width - 286, 26, 8F, SubText, false);
            lvNet = List(c, 14, 82, c.Width - 28, 380, new string[] { "项目", "结果" }, new int[] { 150, 500 });
        }

        void RunNetCheck()
        {
            if (btnNet != null) btnNet.Enabled = false;
            string id = TaskHub.Start("网络体检", "网络体检", "DNS / 代理 / 网关 / 更新源");
            lvNet.Items.Clear();
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool okAll = true;
                try
                {
                    NetLine("开始体检 " + DateTime.Now.ToString("HH:mm:ss"), SubText);
                    // DNS
                    try
                    {
                        string host = "raw.githubusercontent.com";
                        IPAddress[] ips = Dns.GetHostAddresses(host);
                        NetLine("DNS  " + host + "  →  " + (ips.Length > 0 ? ips[0].ToString() : "无结果"), ips.Length > 0 ? UiKit.A(45, 212, 191) : UiKit.A(248, 113, 113));
                        if (ips.Length == 0) okAll = false;
                    }
                    catch (Exception ex) { okAll = false; NetLine("DNS  失败：" + ex.Message, UiKit.A(248, 113, 113)); }

                    // 系统代理
                    try
                    {
                        Uri u = new Uri("https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/update.json");
                        IWebProxy px = WebRequest.GetSystemWebProxy();
                        Uri pu = px == null ? null : px.GetProxy(u);
                        string s = (pu == null || pu == u) ? "直连（未配置代理）" : pu.ToString();
                        NetLine("代理  " + s, UiKit.A(45, 212, 191));
                    }
                    catch (Exception ex) { NetLine("代理  读取失败：" + ex.Message, UiKit.A(240, 180, 90)); }

                    // 网关
                    try
                    {
                        string gw = "";
                        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.OperationalStatus != OperationalStatus.Up) continue;
                            foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses)
                            {
                                if (g.Address != null && g.Address.ToString() != "0.0.0.0") { gw = g.Address.ToString(); break; }
                            }
                            if (gw.Length > 0) break;
                        }
                        if (gw.Length == 0) { okAll = false; NetLine("网关  未找到默认网关", UiKit.A(248, 113, 113)); }
                        else
                        {
                            PingReply r = new Ping().Send(gw, 1200);
                            bool ok = r != null && r.Status == IPStatus.Success;
                            NetLine("网关  " + gw + "  →  " + (ok ? r.RoundtripTime + " ms" : (r == null ? "无响应" : r.Status.ToString())), ok ? UiKit.A(45, 212, 191) : UiKit.A(248, 113, 113));
                            if (!ok) okAll = false;
                        }
                    }
                    catch (Exception ex) { okAll = false; NetLine("网关  失败：" + ex.Message, UiKit.A(248, 113, 113)); }

                    // 更新源
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/update.json");
                        req.Method = "HEAD";
                        req.Timeout = 7000;
                        req.UserAgent = "MiniToolbox-ControlCenter";
                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        {
                            bool ok = resp.StatusCode == HttpStatusCode.OK;
                            NetLine("更新源  HTTP " + (int)resp.StatusCode + "  " + resp.StatusDescription, ok ? UiKit.A(45, 212, 191) : UiKit.A(248, 113, 113));
                            if (!ok) okAll = false;
                        }
                    }
                    catch (Exception ex) { okAll = false; NetLine("更新源  失败：" + ex.Message, UiKit.A(248, 113, 113)); }

                    NetLine(okAll ? "结论：链路正常" : "结论：有项目失败，按上面逐条排查", okAll ? UiKit.A(45, 212, 191) : UiKit.A(248, 113, 113));
                    netSummary = okAll ? "正常" : "有异常";
                    if (okAll) TaskHub.Done(id, "链路正常"); else TaskHub.Fail(id, "存在失败项");
                }
                catch (Exception ex)
                {
                    netSummary = "异常";
                    TaskHub.Fail(id, ex.Message);
                    NetLine("体检中断：" + ex.Message, UiKit.A(248, 113, 113));
                }
                finally
                {
                    Ui(delegate { if (btnNet != null) btnNet.Enabled = true; });
                }
            });
        }

        void NetLine(string s, Color c)
        {
            Ui(delegate
            {
                ListViewItem li = new ListViewItem(new string[] { s.StartsWith("结论") ? "结论" : s.Split(' ')[0], s });
                li.ForeColor = c;
                lvNet.Items.Add(li);
                lvNet.Items[lvNet.Items.Count - 1].EnsureVisible();
            });
        }

        void Ui(MethodInvoker m)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(m);
                else m();
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.ui", ex); }
        }

        // ---------------- 企业 ----------------
        void BuildPolicy(Panel p)
        {
            CcCard c = Card(p, 20, 18, ClientSize.Width - 190 - 40, 210, "策略模拟器（只读）", Accent);
            polState = Lab(c, "—", 16, 34, c.Width - 32, 28, 10F, MainText, true);
            polSource = Lab(c, "", 16, 64, c.Width - 32, 24, 8.5F, SubText, false);
            ckNoUpdate = new CheckBox(); ckNoUpdate.Text = "禁用在线更新"; ckNoUpdate.Location = new Point(16, 96); ckNoUpdate.AutoSize = true; ckNoUpdate.ForeColor = MainText; ckNoUpdate.BackColor = Color.Transparent; ckNoUpdate.CheckedChanged += delegate { SimPolicy(); }; c.Controls.Add(ckNoUpdate);
            ckNoDiag = new CheckBox(); ckNoDiag.Text = "禁用诊断导出"; ckNoDiag.Location = new Point(160, 96); ckNoDiag.AutoSize = true; ckNoDiag.ForeColor = MainText; ckNoDiag.BackColor = Color.Transparent; ckNoDiag.CheckedChanged += delegate { SimPolicy(); }; c.Controls.Add(ckNoDiag);
            ckVerbose = new CheckBox(); ckVerbose.Text = "强制详细日志"; ckVerbose.Location = new Point(304, 96); ckVerbose.AutoSize = true; ckVerbose.ForeColor = MainText; ckVerbose.BackColor = Color.Transparent; ckVerbose.CheckedChanged += delegate { SimPolicy(); }; c.Controls.Add(ckVerbose);
            polSim = Lab(c, "", 16, 126, c.Width - 32, 48, 8.5F, SubText, false);
            Btn(c, "打开策略目录", 16, 172, 140, 34, UiKit.A(30, 39, 52), MainText, delegate { OpenPolicyDir(); });

            CcCard a = Card(p, 20, 240, ClientSize.Width - 190 - 40, 290, "审计日志", UiKit.A(240, 180, 90));
            lvAudit = List(a, 14, 34, a.Width - 28, 180, new string[] { "时间", "用户", "动作", "详情" }, new int[] { 130, 100, 90, 400 });
            lvAudit.Height = 150;
            Btn(a, "刷新", 16, 210, 90, 32, UiKit.A(30, 39, 52), MainText, delegate { RefreshPolicy(); });
            Btn(a, "导出 CSV", 116, 210, 100, 32, UiKit.A(30, 39, 52), MainText, delegate { ExportAuditCsv(); });
            Btn(a, "打开日志目录", 226, 210, 130, 32, UiKit.A(30, 39, 52), MainText, delegate { OpenLogDir(); });
        }

        void RefreshPolicy()
        {
            try
            {
                polState.Text = Policy.LocatedState();
                polSource.Text = "来源：" + (Policy.Source.Length > 0 ? Policy.Source : "未下发（本机无策略文件）");
                ckNoUpdate.Checked = Policy.NoUpdate;
                ckNoDiag.Checked = Policy.NoDiagExport;
                ckVerbose.Checked = Policy.Verbose;
                SimPolicy();

                lvAudit.BeginUpdate();
                lvAudit.Items.Clear();
                if (Audit.FilePath.Length > 0 && File.Exists(Audit.FilePath))
                {
                    string[] lines = File.ReadAllLines(Audit.FilePath, Encoding.UTF8);
                    int start = Math.Max(0, lines.Length - 300);
                    for (int i = start; i < lines.Length; i++)
                    {
                        string[] a = lines[i].Split(new char[] { '|' }, 4);
                        if (a.Length < 4) continue;
                        lvAudit.Items.Add(new ListViewItem(new string[] { a[0].Trim(), a[1].Trim(), a[2].Trim(), a[3].Trim() }));
                    }
                }
                lvAudit.EndUpdate();
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.policy", ex); }
        }

        void SimPolicy()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("模拟效果：");
            if (ckNoUpdate != null && ckNoUpdate.Checked) sb.Append("更新入口被拦截 · ");
            if (ckNoDiag != null && ckNoDiag.Checked) sb.Append("诊断导出按钮置灰 · ");
            if (ckVerbose != null && ckVerbose.Checked) sb.Append("日志级别提高 · ");
            if (sb.Length == "模拟效果：".Length) sb.Append("不收紧任何功能");
            polSim.Text = sb.ToString();
        }

        void OpenPolicyDir()
        {
            try
            {
                string p = Policy.Source.Length > 0 ? Policy.Source : Policy.MachinePath();
                string d = Directory.Exists(p) ? p : Path.GetDirectoryName(p);
                if (Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\"");
                else MessageBox.Show(this, "策略目录还不存在：" + d, "控制中心");
            }
            catch (Exception ex) { MessageBox.Show(this, "打开失败：" + ex.Message, "控制中心"); }
        }

        void OpenLogDir()
        {
            try
            {
                string d = Audit.FilePath.Length > 0 ? Path.GetDirectoryName(Audit.FilePath) : Path.Combine(baseDir, "日志");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                Process.Start("explorer.exe", "\"" + d + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, "打开失败：" + ex.Message, "控制中心"); }
        }

        void ExportAuditCsv()
        {
            try
            {
                string outPath = Path.Combine(baseDir, "审计导出.csv");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("时间,用户,动作,详情");
                for (int i = 0; i < lvAudit.Items.Count; i++)
                {
                    ListViewItem li = lvAudit.Items[i];
                    sb.AppendLine(Csv(li.SubItems[0].Text) + "," + Csv(li.SubItems[1].Text) + "," + Csv(li.SubItems[2].Text) + "," + Csv(li.SubItems[3].Text));
                }
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
                Sfx.Save();
                MessageBox.Show(this, "已导出：" + outPath, "控制中心");
            }
            catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "控制中心"); }
        }

        static string Csv(string s)
        {
            if (s == null) return "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ---------------- 音效 ----------------
        void BuildSound(Panel p)
        {
            CcCard c = Card(p, 20, 18, ClientSize.Width - 190 - 40, 260, "外观与音色", Accent);
            skinSel = UiKit.SkinIdx;
            packSel = Sfx.Pack;
            volSel = Sfx.Volume;

            Lab(c, "皮肤", 16, 36, 60, 24, 9F, SubText, false);
            btnSkin = Btn(c, "", 80, 32, 170, 30, UiKit.A(30, 39, 52), MainText, delegate { CycleSkin(); });
            skinHint = Lab(c, "", 264, 36, c.Width - 280, 24, 8.5F, SubText, false);
            skinSwatch = new Panel();
            skinSwatch.Location = new Point(470, 34);
            skinSwatch.Size = new Size(c.Width - 486, 26);
            skinSwatch.BackColor = Color.Transparent;
            skinSwatch.Paint += delegate(object sw, PaintEventArgs se) { DrawSkinSwatch(se.Graphics, skinSwatch.ClientRectangle); };
            c.Controls.Add(skinSwatch);
            Btn(c, "应用皮肤", 80, 68, 110, 32, UiKit.A(30, 39, 52), MainText, delegate { ApplySkin(); });

            Lab(c, "音色", 16, 112, 60, 24, 9F, SubText, false);
            btnPack = Btn(c, "", 80, 108, 120, 30, UiKit.A(30, 39, 52), MainText, delegate { CyclePack(); });
            Lab(c, "音量", 216, 112, 60, 24, 9F, SubText, false);
            btnVol = Btn(c, "", 270, 108, 80, 30, UiKit.A(30, 39, 52), MainText, delegate { CycleVol(); });

            ckNight = new RoundCheck(); ckNight.Text = "夜间静音"; ckNight.Location = new Point(80, 150); ckNight.AutoSize = true; ckNight.ForeColor = MainText; ckNight.BackColor = Color.Transparent; ckNight.Checked = Sfx.Night; ckNight.CheckedChanged += delegate { try { Sfx.SetNight(ckNight.Checked); } catch { } }; c.Controls.Add(ckNight);
            ckFocus = new RoundCheck(); ckFocus.Text = "专注模式（只留失败/警告/闹钟）"; ckFocus.Location = new Point(190, 150); ckFocus.AutoSize = true; ckFocus.ForeColor = MainText; ckFocus.BackColor = Color.Transparent; ckFocus.Checked = Sfx.Focus; ckFocus.CheckedChanged += delegate { try { Sfx.SetFocus(ckFocus.Checked); } catch { } }; c.Controls.Add(ckFocus);

            Btn(c, "试听 点击", 80, 184, 100, 30, UiKit.A(30, 39, 52), MainText, delegate { Sfx.Preview(0); });
            Btn(c, "试听 成功", 190, 184, 100, 30, UiKit.A(30, 39, 52), MainText, delegate { Sfx.Preview(5); });
            Btn(c, "试听 失败", 300, 184, 100, 30, UiKit.A(30, 39, 52), MainText, delegate { Sfx.Preview(8); });
            Btn(c, "试听 完成", 410, 184, 100, 30, UiKit.A(30, 39, 52), MainText, delegate { Sfx.Preview(5); });
            Btn(c, "恢复默认", 520, 184, 110, 30, UiKit.A(30, 39, 52), MainText, delegate
            {
                try { Sfx.ResetDefaults(); RefreshSound(); Sfx.Ok(); } catch { }
            });

            CcCard e = Card(p, 20, 292, ClientSize.Width - 190 - 40, 260, "分事件开关", UiKit.A(45, 212, 191));
            int n = Sfx.EventKeys.Length;
            for (int i = 0; i < n; i++)
            {
                RoundCheck ck = new RoundCheck();
                ck.Text = Sfx.EventNames[i];
                ck.Tag = Sfx.EventKeys[i];
                ck.AutoSize = true;
                ck.ForeColor = MainText;
                ck.BackColor = Color.Transparent;
                ck.Checked = Sfx.EventOn(Sfx.EventKeys[i]);
                ck.Location = new Point(18 + (i % 4) * 190, 40 + (i / 4) * 44);
                ck.CheckedChanged += delegate(object s, EventArgs a)
                {
                    CheckBox cb = s as CheckBox;
                    if (cb != null) Sfx.SetEventOn(cb.Tag.ToString(), cb.Checked);
                };
                e.Controls.Add(ck);
                evChecks.Add(ck);
            }
            SyncSoundButtons();
        }

        void SyncSoundButtons()
        {
            if (btnSkin != null) btnSkin.Text = UiKit.SKIN_NAMES[skinSel] + "  ▾";
            if (btnPack != null) btnPack.Text = Sfx.PackNames[packSel] + "  ▾";
            if (btnVol != null) btnVol.Text = new string[] { "轻", "中", "响" }[volSel] + "  ▾";
        }

        void CycleSkin()
        {
            skinSel = (skinSel + 1) % UiKit.SKIN_NAMES.Length;
            SyncSoundButtons();
            SkinPreview();
            Sfx.Click();
        }

        void CyclePack()
        {
            packSel = (packSel + 1) % Sfx.PackNames.Length;
            try { Sfx.SetPack(packSel); } catch { }
            SyncSoundButtons();
            Sfx.Ok();
        }

        void CycleVol()
        {
            volSel = (volSel + 1) % 3;
            try { Sfx.SetVolume(volSel); } catch { }
            SyncSoundButtons();
            Sfx.Click();
        }

        void DrawSkinSwatch(Graphics g, Rectangle r)
        {
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int n = UiKit.SKIN_NAMES.Length;
                int gap = 6;
                int bw = (r.Width - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    Color col = Hsl(UiKit.SKIN_HUES[i], 0.55, 0.52);
                    Rectangle rr = new Rectangle(r.X + i * (bw + gap), r.Y + 3, bw, r.Height - 6);
                    using (SolidBrush b = new SolidBrush(col)) g.FillRectangle(b, rr);
                    if (i == skinSel)
                    {
                        using (Pen p = new Pen(Color.White, 2f)) g.DrawRectangle(p, rr);
                    }
                }
            }
            catch { }
        }

        static Color Hsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double rr = 0, gg = 0, bb = 0;
            if (h < 60) { rr = c; gg = x; }
            else if (h < 120) { rr = x; gg = c; }
            else if (h < 180) { gg = c; bb = x; }
            else if (h < 240) { gg = x; bb = c; }
            else if (h < 300) { rr = x; bb = c; }
            else { rr = c; bb = x; }
            return Color.FromArgb((int)((rr + m) * 255), (int)((gg + m) * 255), (int)((bb + m) * 255));
        }

        void SkinPreview()
        {
            try
            {
                if (skinSel < 0) return;
                skinHint.Text = "预览：" + UiKit.SKIN_NAMES[skinSel] + " · 应用后需重启主窗口";
                skinHint.ForeColor = Accent;
                if (skinSwatch != null) skinSwatch.Invalidate();
            }
            catch { }
        }

        void ApplySkin()
        {
            try
            {
                if (skinSel < 0 || skinSel == UiKit.SkinIdx) return;
                UiKit.ApplySkin(skinSel, true);
                Sfx.Ok();
                if (MessageBox.Show(this, "皮肤已写入，重启应用后生效。现在重启？", "控制中心", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    owner.CcRestartApp();
            }
            catch (Exception ex) { MessageBox.Show(this, "应用皮肤失败：" + ex.Message, "控制中心"); }
        }

        void RefreshSound()
        {
            try
            {
                skinSel = UiKit.SkinIdx;
                packSel = Sfx.Pack;
                volSel = Sfx.Volume;
                SyncSoundButtons();
                ckNight.Checked = Sfx.Night;
                ckFocus.Checked = Sfx.Focus;
                for (int i = 0; i < evChecks.Count; i++)
                {
                    string k = evChecks[i].Tag.ToString();
                    evChecks[i].Checked = Sfx.EventOn(k);
                }
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.sound", ex); }
        }

        // ---------------- 插件 ----------------
        void BuildPlugins(Panel p)
        {
            CcCard c = Card(p, 20, 18, ClientSize.Width - 190 - 40, 360, "已安装插件", Accent);
            lvPlug = List(c, 14, 34, c.Width - 28, 210, new string[] { "插件", "类型", "大小", "权限", "修改时间" }, new int[] { 180, 100, 70, 140, 130 });
            Btn(c, "刷新", 16, 280, 90, 32, UiKit.A(30, 39, 52), MainText, delegate { RefreshPlugins(); });
            Btn(c, "打开插件目录", 116, 280, 130, 32, UiKit.A(30, 39, 52), MainText, delegate { OpenPluginDir(); });
            Btn(c, "插件仓库", 256, 280, 110, 32, UiKit.A(30, 39, 52), MainText, delegate { owner.CcOpenPluginStore(); });
            Btn(c, "权限说明", 376, 280, 110, 32, UiKit.A(30, 39, 52), MainText, delegate
            {
                MessageBox.Show(this, "插件是本地脚本（.ps1 / .py / .cmd），运行权限等同于当前登录用户。\n本程序不自动提权、不联网校验脚本、不做代码签名。\n只运行来源可信、自己看过的脚本；列表里的「当前用户 · 未签名」就是真实权限状态。", "插件权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            Lab(c, "插件按当前用户权限运行，不自动提权；状态栏如实标注「未签名」，不伪装成已验签。", 16, 322, c.Width - 32, 20, 8F, SubText, false);

            // 统计放在同一页下半区
            CcCard s = Card(p, 20, 390, ClientSize.Width - 190 - 40, 180, "本地统计", UiKit.A(240, 180, 90));
            stCount = Lab(s, "—", 16, 36, 140, 32, 10F, MainText, true);
            stMin = Lab(s, "—", 170, 36, 140, 32, 10F, MainText, true);
            stStreak = Lab(s, "—", 324, 36, 140, 32, 10F, MainText, true);
            stClean = Lab(s, "—", 478, 36, 160, 32, 10F, MainText, true);
            Lab(s, "今日次数", 16, 70, 140, 18, 7.5F, SubText, false);
            Lab(s, "今日分钟", 170, 70, 140, 18, 7.5F, SubText, false);
            Lab(s, "连续天数", 324, 70, 140, 18, 7.5F, SubText, false);
            Lab(s, "累计扫描", 478, 70, 160, 18, 7.5F, SubText, false);
            graph = new Panel();
            graph.Location = new Point(16, 96);
            graph.Size = new Size(s.Width - 32, 70);
            graph.BackColor = Color.Transparent;
            graph.Paint += delegate(object gs, PaintEventArgs ge) { DrawBars(ge.Graphics, graph.ClientRectangle); };
            s.Controls.Add(graph);
        }

        void RefreshPlugins()
        {
            try
            {
                lvPlug.BeginUpdate();
                lvPlug.Items.Clear();
                string[] files = PluginCfg.Files();
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo fi = new FileInfo(files[i]);
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    string type = ext == ".ps1" ? "PowerShell" : ext == ".py" ? "Python" : (ext == ".cmd" || ext == ".bat") ? "CMD" : ext;
                    lvPlug.Items.Add(new ListViewItem(new string[] { PluginCfg.Title(files[i]), type, FmtSize(fi.Length), "当前用户 · 未签名", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") }));
                }
                lvPlug.EndUpdate();
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.plugins", ex); }
        }

        void OpenPluginDir()
        {
            try
            {
                string d = PluginCfg.Dir;
                if (d == null || d.Length == 0) d = Path.Combine(baseDir, "插件");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                Process.Start("explorer.exe", "\"" + d + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, "打开插件目录失败：" + ex.Message, "控制中心"); }
        }

        void RefreshStats()
        {
            try
            {
                int count, min, streak;
                last7 = UsageStats.Snapshot(out count, out min, out streak);
                stCount.Text = count.ToString();
                stMin.Text = min.ToString();
                stStreak.Text = streak.ToString();
                long all = 0;
                for (int i = 0; i < CleanHistory.Count; i++) all += CleanHistory.At(i);
                stClean.Text = FmtSize(all);
                graph.Invalidate();
            }
            catch (Exception ex) { Diag.Swallow("ControlCenter.stats", ex); }
        }

        void DrawBars(Graphics g, Rectangle r)
        {
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int max = 1;
                for (int i = 0; i < last7.Length; i++) if (last7[i] > max) max = last7[i];
                int gap = 8;
                int bw = Math.Max(10, (r.Width - gap * 6) / 7);
                for (int i = 0; i < 7; i++)
                {
                    int h = (int)((r.Height - 20) * (last7[i] / (double)max));
                    if (h < 2) h = 2;
                    Rectangle b = new Rectangle(r.X + i * (bw + gap), r.Y + r.Height - 18 - h, bw, h);
                    using (LinearGradientBrush br = new LinearGradientBrush(b, UiKit.AA(230, Accent.R, Accent.G, Accent.B), UiKit.AA(70, Accent.R, Accent.G, Accent.B), 90f))
                        g.FillRectangle(br, b);
                    string d = DateTime.Now.AddDays(-(6 - i)).ToString("MM-dd");
                    using (SolidBrush tb = new SolidBrush(SubText))
                        g.DrawString(d, new Font("Microsoft YaHei UI", 7F), tb, b.X - 2, r.Y + r.Height - 16);
                }
            }
            catch { }
        }
    }

    /// <summary>控制中心用的圆角卡片：自带填充、描边、顶部强调线与标题。</summary>
    class CcCard : Panel
    {
        public Color Fill, Line, Accent, TextColor;
        public string Caption = "";
        public CcCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Rectangle r = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                if (r.Width < 8 || r.Height < 8) return;
                using (GraphicsPath p = UiKit.RoundedPath(r, 10))
                using (SolidBrush b = new SolidBrush(Fill))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(Line, 1f)) e.Graphics.DrawPath(pen, p);
                }
                if (Accent.A > 0)
                {
                    using (SolidBrush ab = new SolidBrush(Accent))
                        e.Graphics.FillRectangle(ab, 1, 1, Math.Min(64, r.Width - 2), 3);
                }
                if (Caption.Length > 0)
                {
                    using (SolidBrush tb = new SolidBrush(TextColor))
                        e.Graphics.DrawString(Caption, new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), tb, 14, 10);
                }
            }
            catch { }
        }
    }
}

