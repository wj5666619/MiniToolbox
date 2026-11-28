using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Globalization;

namespace ShutdownTool
{
    // v19.11：从 3top.top 精选的本地小工具，独立窗口，不联网
    class UtilToolboxForm : Form
    {
        static readonly Color BG = UiKit.A(10, 15, 21);
        static readonly Color CARD = UiKit.A(21, 28, 39);
        static readonly Color INPUT = UiKit.A(14, 21, 30);
        static readonly Color LINE = UiKit.A(48, 60, 78);
        static readonly Color TEXT = UiKit.A(229, 237, 246);
        static readonly Color SUB = UiKit.A(139, 156, 176);
        static readonly Color ACC = UiKit.A(45, 212, 191);
        static readonly Color ACC2 = UiKit.A(56, 189, 248);

        const int W = 1040, H = 740, TOP = 58, NAVW = 170;
        Panel content;
        Panel[] pages = new Panel[6];
        Panel[] navs = new Panel[6];
        int sel = 0;

        // 哈希
        TextBox hashPath, hashCompare, hashOut;
        ComboBox hashAlgo;
        UtilBtn hashBtn;
        Label hashStatus;

        // 取色
        PictureBox colorPic;
        Panel colorSwatch, colorPalette;
        Label hexLbl, rgbLbl, hslLbl;
        Color picked = UiKit.A(45, 212, 191);
        List<Color> palette = new List<Color>();

        // 印章
        TextBox sealName, sealCenter, sealBottom;
        Panel sealPreview;
        Color sealColor = Color.FromArgb(198, 32, 40);
        NumericUpDown sealSize;

        // Base64
        TextBox b64Path, b64Out, b64In;
        PictureBox b64Pic;
        CheckBox b64Prefix;
        Label b64Status;

        // 时间戳
        Label nowTime, nowStamp;
        TextBox tsIn, tsOut, dtIn, dtOut;

        // 密码
        NumericUpDown pwLen;
        CheckBox pwUpper, pwLower, pwDigit, pwSymbol, pwNoAmb;
        TextBox[] pwOut = new TextBox[3];

        System.Windows.Forms.Timer clock;

        public UtilToolboxForm()
        {
            Text = "实用工具集";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, H);
            BackColor = BG;
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            try { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); } catch (Exception dg0) { Diag.Swallow("UtilToolbox.cs:0", dg0); }

            BuildTop();
            BuildNav();
            content = new Panel();
            content.Location = new Point(NAVW, TOP);
            content.Size = new Size(W - NAVW, H - TOP);
            content.BackColor = BG;
            Controls.Add(content);

            BuildHashPage();
            BuildColorPage();
            BuildSealPage();
            BuildBase64Page();
            BuildTimePage();
            BuildPasswordPage();

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Location = Point.Empty;
                pages[i].Size = content.ClientSize;
                pages[i].BackColor = BG;
                pages[i].Visible = false;
                content.Controls.Add(pages[i]);
            }
            Switch(0);
            ApplyPaperPalette();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // 纸感独立窗口：整窗收成暖纸底，大块系统白改成米白卡，输入框收成纸白面。
        void ApplyPaperPalette()
        {
            if (!UiKit.Paper) return;
            try
            {
                BackColor = Color.FromArgb(246, 241, 229);
                content.BackColor = Color.FromArgb(246, 241, 229);
                Paperize(Controls);
            }
            catch (Exception dgUT_1) { Diag.Swallow("UtilToolbox.cs:paper", dgUT_1); }
        }

        void Paperize(Control.ControlCollection kids)
        {
            foreach (Control c in kids)
            {
                try
                {
                    if (c is TextBoxBase)
                    {
                        c.BackColor = Color.FromArgb(245, 238, 223);   // 暖纸输入面，别用纯纸白（会像系统白块）
                        c.ForeColor = UiKit.Ink;
                    }
                    else if (c is Panel && c.BackColor.A > 200 && UiKit.Lum(c.BackColor) > 0.90)
                    {
                        // 大块内容面板用纸底，小卡片/导航块用米白，拉开一层层次
                        if (c.Width * c.Height > 260000) c.BackColor = Color.FromArgb(246, 241, 229);
                        else c.BackColor = Color.FromArgb(252, 247, 235);
                    }
                    if (c.HasChildren) Paperize(c.Controls);
                }
                catch (Exception dgUT_2) { Diag.Swallow("UtilToolbox.cs:paper2", dgUT_2); }
            }
        }

        // ================= 外框 =================
        void BuildTop()
        {
            Panel top = new Panel();
            top.Location = Point.Empty;
            top.Size = new Size(W, TOP);
            top.BackColor = BG;
            top.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (UiKit.Paper)
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(top.ClientRectangle,
                            Color.FromArgb(252, 247, 236), Color.FromArgb(242, 235, 221), 0F))
                            e.Graphics.FillRectangle(br, top.ClientRectangle);
                        using (Pen p = new Pen(Color.FromArgb(178, 165, 141), 1F))
                            e.Graphics.DrawLine(p, 0, TOP - 1, W, TOP - 1);
                    }
                    else
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(top.ClientRectangle,
                            UiKit.A(24, 54, 62), UiKit.A(13, 20, 29), 0F))
                            e.Graphics.FillRectangle(br, top.ClientRectangle);
                        using (Pen p = new Pen(UiKit.AA(70, 45, 212, 191), 1F))
                            e.Graphics.DrawLine(p, 0, TOP - 1, W, TOP - 1);
                    }
                    UiKit.DrawLogo(e.Graphics, new Rectangle(22, 11, 36, 36));
                }
                catch (Exception dg1) { Diag.Swallow("UtilToolbox.cs:1", dg1); }
            };
            Controls.Add(top);

            Label t = new Label();
            t.Text = "实用工具集";
            t.ForeColor = UiKit.Paper ? UiKit.Ink : TEXT;
            t.BackColor = Color.Transparent;
            t.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            t.Location = new Point(72, 9);
            t.AutoSize = true;
            top.Controls.Add(t);

            Label st = new Label();
            st.Text = "本地运行 · 文件不上传 · 关窗即结束";
            st.ForeColor = UiKit.Paper ? Color.FromArgb(104, 94, 78) : UiKit.A(128, 148, 168);
            st.BackColor = Color.Transparent;
            st.Font = new Font("Microsoft YaHei UI", 8F);
            st.Location = new Point(74, 34);
            st.AutoSize = true;
            top.Controls.Add(st);

            Label ver = new Label();
            ver.Text = "v" + UiKit.VER + "  ·  6 TOOLS";
            ver.ForeColor = UiKit.Paper ? Color.FromArgb(150, 112, 38) : UiKit.A(45, 212, 191);
            ver.BackColor = Color.Transparent;
            ver.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            ver.Location = new Point(W - 210, 23);
            ver.Size = new Size(140, 18);
            ver.TextAlign = ContentAlignment.MiddleRight;
            top.Controls.Add(ver);

            Panel close = new Panel();
            close.Size = new Size(34, 34);
            close.Location = new Point(W - 46, 12);
            close.Cursor = Cursors.Hand;
            close.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = UiKit.RoundedPath(new Rectangle(0, 0, 33, 33), 8))
                {
                    using (SolidBrush b = new SolidBrush(close.Tag == null
                        ? (UiKit.Paper ? Color.FromArgb(232, 224, 206) : UiKit.A(34, 44, 58))
                        : UiKit.A(160, 54, 62)))
                        e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(UiKit.Paper ? Color.FromArgb(190, 178, 156) : UiKit.AA(60, 255, 255, 255), 1F))
                        e.Graphics.DrawPath(pen, p);
                }
                TextRenderer.DrawText(e.Graphics, "✕", new Font("Segoe UI", 9F),
                    new Rectangle(0, 0, 34, 34), UiKit.Paper ? UiKit.Ink : TEXT, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            close.MouseEnter += (s, e) => { close.Tag = "1"; close.Invalidate(); };
            close.MouseLeave += (s, e) => { close.Tag = null; close.Invalidate(); };
            close.Click += (s, e) => Close();
            top.Controls.Add(close);

            top.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
            t.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
            st.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        void BuildNav()
        {
            Panel nav = new Panel();
            nav.Location = new Point(0, TOP);
            nav.Size = new Size(NAVW, H - TOP);
            nav.BackColor = UiKit.A(13, 19, 27);
            nav.Paint += (s, e) =>
            {
                try
                {
                    using (Pen p = new Pen(UiKit.AA(40, 60, 78, 96), 1F))
                        e.Graphics.DrawLine(p, NAVW - 1, 0, NAVW - 1, nav.Height);
                }
                catch (Exception dg2) { Diag.Swallow("UtilToolbox.cs:2", dg2); }
            };
            Controls.Add(nav);

            string[] names = { "哈希校验", "图片取色", "电子印章", "图片 Base64", "时间戳", "密码生成" };
            string[] subs = { "MD5 / SHA", "主色板", "透明 PNG", "编码 / 解码", "互转", "强随机" };
            string[] icons = { "#", "◉", "印", "64", "◷", "⚿" };
            Color[] cols = { UiKit.A(56, 189, 248), UiKit.A(45, 212, 191), Color.FromArgb(214, 69, 69), UiKit.A(96, 165, 250), UiKit.A(34, 211, 238), UiKit.A(129, 140, 248) };

            for (int i = 0; i < 6; i++)
            {
                int ii = i;
                Panel it = new Panel();
                it.Location = new Point(0, 14 + i * 60);
                it.Size = new Size(NAVW, 52);
                it.Cursor = Cursors.Hand;
                it.BackColor = UiKit.A(13, 19, 27);
                it.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    bool on = (ii == sel);
                    Rectangle r = new Rectangle(6, 2, NAVW - 12, 48);
                    if (on)
                    {
                        using (GraphicsPath p = UiKit.RoundedPath(r, 9))
                        {
                            using (LinearGradientBrush b = new LinearGradientBrush(r, UiKit.A(28, 45, 55), UiKit.A(19, 28, 38), 0F))
                                e.Graphics.FillPath(b, p);
                            using (Pen pen = new Pen(UiKit.AA(110, 45, 212, 191), 1F))
                                e.Graphics.DrawPath(pen, p);
                        }
                        using (SolidBrush b = new SolidBrush(cols[ii]))
                            e.Graphics.FillRectangle(b, 6, 12, 3, 28);
                    }
                    Rectangle ic = new Rectangle(18, 10, 32, 32);
                    using (GraphicsPath p = UiKit.RoundedPath(ic, 9))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(ic, UiKit.Lighten(cols[ii], 8), UiKit.Darken(cols[ii], 42), 90F))
                            e.Graphics.FillPath(b, p);
                    }
                    TextRenderer.DrawText(e.Graphics, icons[ii], new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                        ic, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, names[ii], new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                        new Rectangle(60, 8, 104, 18), on ? Color.White : TEXT, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, subs[ii], new Font("Microsoft YaHei UI", 7F),
                        new Rectangle(60, 27, 104, 16), on ? UiKit.A(150, 170, 190) : SUB, TextFormatFlags.NoPadding);
                };
                it.Click += (s, e) => Switch(ii);
                it.MouseEnter += (s, e) => { if (ii != sel) it.BackColor = UiKit.A(18, 26, 36); };
                it.MouseLeave += (s, e) => { it.BackColor = UiKit.A(13, 19, 27); };
                nav.Controls.Add(it);
                navs[i] = it;
            }

            Label foot = new Label();
            foot.Text = "数据不出本机\n关窗即释放";
            foot.ForeColor = UiKit.A(88, 104, 124);
            foot.BackColor = Color.Transparent;
            foot.Font = new Font("Microsoft YaHei UI", 7.5F);
            foot.Location = new Point(18, H - TOP - 58);
            foot.Size = new Size(140, 40);
            nav.Controls.Add(foot);
        }

        void Switch(int i)
        {
            if (i < 0 || i >= 6) return;
            sel = i;
            for (int k = 0; k < 6; k++)
            {
                if (pages[k] != null) pages[k].Visible = (k == i);
                if (navs[k] != null) navs[k].Invalidate();
            }
        }

        Panel Page()
        {
            Panel p = new Panel();
            p.BackColor = BG;
            return p;
        }

        void Head(Panel p, string title, string sub)
        {
            Label t = new Label();
            t.Text = title;
            t.ForeColor = TEXT;
            t.BackColor = Color.Transparent;
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.Location = new Point(26, 16);
            t.AutoSize = true;
            p.Controls.Add(t);

            Label d = new Label();
            d.Text = sub;
            d.ForeColor = SUB;
            d.BackColor = Color.Transparent;
            d.Font = new Font("Microsoft YaHei UI", 8.5F);
            d.Location = new Point(27, 48);
            d.AutoSize = true;
            p.Controls.Add(d);
        }

        Panel Card(Control parent, int x, int y, int w, int h, Color accent)
        {
            Panel c = new Panel();
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            c.BackColor = BG;
            c.Paint += (s, e) =>
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
                    using (GraphicsPath p = UiKit.RoundedPath(r, 12))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(r, UiKit.A(26, 34, 46), CARD, 90F))
                            e.Graphics.FillPath(b, p);
                        using (Pen pen = new Pen(UiKit.AA(70, accent.R, accent.G, accent.B), 1F))
                            e.Graphics.DrawPath(pen, p);
                    }
                }
                catch (Exception dg3) { Diag.Swallow("UtilToolbox.cs:3", dg3); }
            };
            parent.Controls.Add(c);
            return c;
        }

        Label Lab(Control parent, string text, int x, int y, float fs, bool bold, Color col)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = col;
            l.BackColor = Color.Transparent;
            l.Font = new Font("Microsoft YaHei UI", fs, bold ? FontStyle.Bold : FontStyle.Regular);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            parent.Controls.Add(l);
            return l;
        }

        TextBox Input(Control parent, int x, int y, int w, int h, bool multi)
        {
            Panel box = new Panel();
            box.Location = new Point(x, y);
            box.Size = new Size(w, h);
            box.BackColor = CARD;
            box.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, box.Width - 1, box.Height - 1);
                using (GraphicsPath p = UiKit.RoundedPath(r, 8))
                {
                    using (SolidBrush b = new SolidBrush(UiKit.Paper ? Color.FromArgb(255, 252, 243) : INPUT))
                        e.Graphics.FillPath(b, p);
                    using (Pen pen = new Pen(UiKit.Paper ? Color.FromArgb(196, 184, 162) : UiKit.AA(64, 74, 92, 112), 1F))
                        e.Graphics.DrawPath(pen, p);
                }
            };
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.None;
            t.BackColor = INPUT;
            t.ForeColor = TEXT;
            t.Font = new Font("Microsoft YaHei UI", 9F);
            t.Multiline = multi;
            if (multi) { t.ScrollBars = ScrollBars.Vertical; t.SetBounds(10, 8, w - 20, h - 18); }
            else t.SetBounds(10, (h - 22) / 2, w - 20, 22);
            box.Controls.Add(t);
            parent.Controls.Add(box);
            return t;
        }

        UtilBtn Btn(Control parent, string text, int x, int y, int w, int h, bool primary)
        {
            UtilBtn b = new UtilBtn(primary ? UiKit.A(20, 122, 110) : UiKit.A(35, 45, 60),
                primary ? Color.White : UiKit.A(214, 226, 240));
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            parent.Controls.Add(b);
            return b;
        }

        ComboBox Combo(Control parent, int x, int y, int w, int h, string[] items)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = INPUT;
            c.ForeColor = TEXT;
            c.Font = new Font("Microsoft YaHei UI", 9F);
            c.Items.AddRange(items);
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.ItemHeight = 22;
            c.DrawItem += (s, e) =>
            {
                e.DrawBackground();
                using (SolidBrush b = new SolidBrush(e.Index == c.SelectedIndex ? UiKit.A(32, 48, 62) : CARD))
                    e.Graphics.FillRectangle(b, e.Bounds);
                e.Graphics.DrawString(c.Items[e.Index].ToString(), c.Font,
                    new SolidBrush(TEXT), e.Bounds.X + 6, e.Bounds.Y + 3);
            };
            parent.Controls.Add(c);
            return c;
        }

        // ================= 哈希 =================
        void BuildHashPage()
        {
            Panel p = Page();
            Head(p, "文件哈希校验", "选一个文件，算 MD5 / SHA，和官方给的哈希比对");
            Panel c1 = Card(p, 26, 86, 818, 140, ACC2);

            hashPath = Input(c1, 16, 16, 512, 36, false);
            hashPath.ReadOnly = true;
            Btn(c1, "选择文件", 540, 16, 100, 36, false).Click += (s, e) => PickHashFile();
            hashBtn = Btn(c1, "开始计算", 652, 16, 150, 36, true);
            hashBtn.Click += (s, e) => CalcHash();

            Lab(c1, "算法", 16, 68, 8.5F, false, SUB);
            hashAlgo = Combo(c1, 62, 64, 130, 30, new string[] { "SHA-256", "MD5", "SHA-1", "SHA-512" });
            hashAlgo.SelectedIndex = 0;

            Lab(c1, "期望哈希（可选）", 216, 68, 8.5F, false, SUB);
            hashCompare = Input(c1, 216, 88, 380, 36, false);
            Btn(c1, "比对", 612, 88, 90, 36, false).Click += (s, e) => CompareHash();
            Btn(c1, "清空", 714, 88, 90, 36, false).Click += (s, e) =>
            {
                hashPath.Text = ""; hashOut.Text = ""; hashCompare.Text = ""; hashStatus.Text = "";
                hashStatus.ForeColor = SUB;
            };

            Panel c2 = Card(p, 26, 240, 818, 400, ACC);
            Lab(c2, "计算结果", 18, 14, 9F, true, TEXT);
            hashOut = Input(c2, 16, 44, 786, 280, true);
            hashOut.ReadOnly = true;
            if (UiKit.Paper) { hashOut.BackColor = Color.FromArgb(247, 240, 226); hashOut.ForeColor = UiKit.Ink; }
            hashOut.ScrollBars = ScrollBars.None;
            hashOut.Font = new Font("Consolas", 11F);
            Btn(c2, "复制结果", 16, 340, 110, 36, true).Click += (s, e) =>
            {
                if (hashOut.Text.Length == 0) return;
                Clipboard.SetText(hashOut.Text);
                hashStatus.Text = "已复制到剪贴板"; hashStatus.ForeColor = ACC;
            };
            Btn(c2, "校验文件来源提示", 138, 340, 180, 36, false).Click += (s, e) =>
            {
                MessageBox.Show(this, "哈希只能证明文件与发布方给出的一致。\r\n请从官网或官方仓库获取哈希值，再在本页比对。",
                    "说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            hashStatus = Lab(c2, "", 16, 330, 9F, false, SUB);
            hashStatus.Location = new Point(334, 350);
            hashStatus.AutoSize = true;
            hashStatus.MaximumSize = new Size(460, 0);
            pages[0] = p;
        }

        void PickHashFile()
        {
            using (OpenFileDialog fd = new OpenFileDialog())
            {
                fd.Title = "选择要计算哈希的文件";
                fd.Filter = "所有文件 (*.*)|*.*";
                if (fd.ShowDialog(this) == DialogResult.OK)
                {
                    hashPath.Text = fd.FileName;
                    hashOut.Text = ""; hashStatus.Text = "";
                }
            }
        }

        void CalcHash()
        {
            string path = hashPath.Text.Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                hashStatus.Text = "请先选择一个存在的文件"; hashStatus.ForeColor = Color.FromArgb(240, 160, 90);
                return;
            }
            string algo = (string)hashAlgo.SelectedItem;
            hashBtn.Enabled = false;
            hashBtn.Text = "计算中…";
            hashStatus.Text = "正在读取文件…"; hashStatus.ForeColor = SUB;
            hashOut.Text = "";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result = "";
                string err = "";
                long len = 0;
                try
                {
                    FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, false);
                    try
                    {
                        len = fs.Length;
                        HashAlgorithm ha = algo == "MD5" ? (HashAlgorithm)new MD5CryptoServiceProvider()
                            : algo == "SHA-1" ? (HashAlgorithm)new SHA1CryptoServiceProvider()
                            : algo == "SHA-512" ? (HashAlgorithm)new SHA512Managed()
                            : (HashAlgorithm)new SHA256Managed();
                        byte[] h = ha.ComputeHash(fs);
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                        result = sb.ToString();
                        ha.Clear();
                    }
                    finally { fs.Dispose(); }
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        try
                        {
                            hashBtn.Enabled = true;
                            hashBtn.Text = "开始计算";
                            if (err.Length > 0)
                            {
                                hashStatus.Text = "失败：" + err; hashStatus.ForeColor = Color.FromArgb(240, 120, 110);
                            }
                            else
                            {
                                hashOut.Text = result;
                                hashStatus.Text = algo + "  ·  " + len.ToString("N0") + " 字节";
                                hashStatus.ForeColor = ACC;
                                if (hashCompare.Text.Trim().Length > 0) CompareHash();
                            }
                        }
                        catch (Exception dg4) { Diag.Swallow("UtilToolbox.cs:4", dg4); }
                    }));
                }
                catch (Exception dg5) { Diag.Swallow("UtilToolbox.cs:5", dg5); }
            });
        }

        void CompareHash()
        {
            string a = hashOut.Text.Trim().ToLowerInvariant();
            string b = hashCompare.Text.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            if (a.Length == 0) { hashStatus.Text = "先计算一次哈希"; hashStatus.ForeColor = SUB; return; }
            if (b.Length == 0) { hashStatus.Text = "请粘贴期望哈希"; hashStatus.ForeColor = SUB; return; }
            if (a == b) { hashStatus.Text = "✓ 一致，文件未被改动"; hashStatus.ForeColor = Color.FromArgb(74, 222, 128); }
            else { hashStatus.Text = "✗ 不一致，文件可能被修改"; hashStatus.ForeColor = Color.FromArgb(248, 113, 113); }
        }

        // ================= 取色 =================
        void BuildColorPage()
        {
            Panel p = Page();
            Head(p, "图片取色与配色", "点图片任意位置取色，载入时自动提取主色板");
            colorPic = new PictureBox();
            colorPic.Location = new Point(26, 96);
            colorPic.Size = new Size(470, 470);
            colorPic.BackColor = UiKit.A(8, 12, 17);
            colorPic.SizeMode = PictureBoxSizeMode.Zoom;
            colorPic.Cursor = Cursors.Cross;
            colorPic.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(UiKit.AA(70, 45, 212, 191), 1F))
                    e.Graphics.DrawRectangle(pen, 0, 0, colorPic.Width - 1, colorPic.Height - 1);
                if (colorPic.Image == null)
                {
                    TextRenderer.DrawText(e.Graphics, "点击「打开图片」载入 JPG / PNG / BMP",
                        new Font("Microsoft YaHei UI", 10F), new Rectangle(0, 0, colorPic.Width, colorPic.Height),
                        UiKit.A(100, 116, 136), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            };
            colorPic.MouseClick += (s, e) => PickPixel(e.X, e.Y);
            p.Controls.Add(colorPic);

            Btn(p, "打开图片", 26, 580, 120, 38, true).Click += (s, e) => LoadColorImage();
            Btn(p, "复制 HEX", 158, 580, 120, 38, false).Click += (s, e) =>
            {
                Clipboard.SetText("#" + Hex(picked));
            };
            Lab(p, "图片只在内存里读取，不写盘、不联网", 292, 590, 8.5F, false, SUB);

            colorSwatch = new Panel();
            colorSwatch.Location = new Point(520, 96);
            colorSwatch.Size = new Size(324, 120);
            colorSwatch.BackColor = BG;
            colorSwatch.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, colorSwatch.Width - 1, colorSwatch.Height - 1);
                using (GraphicsPath gp = UiKit.RoundedPath(r, 12))
                {
                    using (SolidBrush b = new SolidBrush(picked))
                        e.Graphics.FillPath(b, gp);
                    using (Pen pen = new Pen(UiKit.AA(80, 255, 255, 255), 1F))
                        e.Graphics.DrawPath(pen, gp);
                }
                TextRenderer.DrawText(e.Graphics, "#" + Hex(picked).ToUpperInvariant(),
                    new Font("Consolas", 13F, FontStyle.Bold), new Rectangle(14, 12, 250, 24),
                    UiKit.Lum(picked) > 0.62 ? UiKit.Ink : Color.White, TextFormatFlags.NoPadding);
            };
            p.Controls.Add(colorSwatch);

            hexLbl = Lab(p, "#2DD4BF", 520, 226, 12F, true, TEXT);
            hexLbl.Font = new Font("Consolas", 12F, FontStyle.Bold);
            rgbLbl = Lab(p, "RGB 45, 212, 191", 520, 254, 9F, false, SUB);
            hslLbl = Lab(p, "HSL 171°, 63%, 50%", 520, 278, 9F, false, SUB);

            Lab(p, "主色板", 520, 314, 9F, true, TEXT);
            colorPalette = new Panel();
            colorPalette.Location = new Point(520, 340);
            colorPalette.Size = new Size(324, 160);
            colorPalette.BackColor = BG;
            colorPalette.Paint += (s, e) => PaintPalette(e.Graphics);
            p.Controls.Add(colorPalette);
            Lab(p, "载入图片后自动统计主色；单击色块可切到该颜色", 520, 510, 8F, false, SUB);

            pages[1] = p;
            UpdateColorLabels();
        }

        void LoadColorImage()
        {
            using (OpenFileDialog fd = new OpenFileDialog())
            {
                fd.Title = "选择图片";
                fd.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*";
                if (fd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Image old = colorPic.Image;
                    using (FileStream fs = new FileStream(fd.FileName, FileMode.Open, FileAccess.Read))
                    {
                        Image img = Image.FromStream(fs);
                        colorPic.Image = new Bitmap(img);
                        img.Dispose();
                    }
                    if (old != null) old.Dispose();
                    BuildDominant((Bitmap)colorPic.Image);
                    picked = palette.Count > 0 ? palette[0] : picked;
                    UpdateColorLabels();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "图片打不开：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void BuildDominant(Bitmap bmp)
        {
            palette.Clear();
            try
            {
                int step = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 180);
                int[] cnt = new int[512];
                int[] sr = new int[512], sg = new int[512], sb = new int[512];
                for (int y = 0; y < bmp.Height; y += step)
                {
                    for (int x = 0; x < bmp.Width; x += step)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.A < 20) continue;
                        int key = ((c.R >> 5) << 6) | ((c.G >> 5) << 3) | (c.B >> 5);
                        cnt[key]++; sr[key] += c.R; sg[key] += c.G; sb[key] += c.B;
                    }
                }
                List<int> order = new List<int>();
                for (int i = 0; i < 512; i++) if (cnt[i] > 0) order.Add(i);
                order.Sort((a, b) => cnt[b].CompareTo(cnt[a]));
                for (int i = 0; i < order.Count && palette.Count < 10; i++)
                {
                    int k = order[i];
                    palette.Add(Color.FromArgb(sr[k] / cnt[k], sg[k] / cnt[k], sb[k] / cnt[k]));
                }
            }
            catch (Exception dg6) { Diag.Swallow("UtilToolbox.cs:6", dg6); }
            colorPalette.Invalidate();
        }

        void PickPixel(int mx, int my)
        {
            Image img = colorPic.Image;
            if (img == null) return;
            try
            {
                double sc = Math.Min((double)colorPic.ClientSize.Width / img.Width, (double)colorPic.ClientSize.Height / img.Height);
                int dw = (int)Math.Round(img.Width * sc), dh = (int)Math.Round(img.Height * sc);
                int ox = (colorPic.ClientSize.Width - dw) / 2, oy = (colorPic.ClientSize.Height - dh) / 2;
                if (mx < ox || my < oy || mx >= ox + dw || my >= oy + dh) return;
                int px = (int)((mx - ox) / sc), py = (int)((my - oy) / sc);
                if (px < 0) px = 0; if (py < 0) py = 0;
                if (px >= img.Width) px = img.Width - 1; if (py >= img.Height) py = img.Height - 1;
                using (Bitmap b = new Bitmap(img))
                {
                    picked = b.GetPixel(px, py);
                }
                palette.Insert(0, picked);
                if (palette.Count > 16) palette.RemoveAt(palette.Count - 1);
                UpdateColorLabels();
                colorPalette.Invalidate();
                colorSwatch.Invalidate();
            }
            catch (Exception dg7) { Diag.Swallow("UtilToolbox.cs:7", dg7); }
        }

        void UpdateColorLabels()
        {
            if (hexLbl == null) return;
            hexLbl.Text = "#" + Hex(picked).ToUpperInvariant();
            rgbLbl.Text = "RGB  " + picked.R + ", " + picked.G + ", " + picked.B;
            float h, s, l;
            RgbToHsl(picked, out h, out s, out l);
            hslLbl.Text = "HSL  " + (int)Math.Round(h) + "°, " + (int)Math.Round(s * 100) + "%, " + (int)Math.Round(l * 100) + "%";
        }

        void PaintPalette(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cols = 6, sw = 46, sh = 60;
            for (int i = 0; i < palette.Count && i < 18; i++)
            {
                int x = (i % cols) * (sw + 6);
                int y = (i / cols) * (sh + 8);
                Color c = palette[i];
                Rectangle r = new Rectangle(x, y, sw, sh);
                using (GraphicsPath p = UiKit.RoundedPath(r, 7))
                {
                    using (SolidBrush b = new SolidBrush(c))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(UiKit.AA(90, 255, 255, 255), 1F))
                        g.DrawPath(pen, p);
                }
                if (i == 0)
                {
                    using (SolidBrush b = new SolidBrush(UiKit.Lum(c) > 0.62 ? UiKit.Ink : Color.White))
                        g.DrawString("•", new Font("Microsoft YaHei UI", 7F), b, x + 4, y + 2);
                }
            }
        }

        static string Hex(Color c) { return c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

        static void RgbToHsl(Color c, out float h, out float s, out float l)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            l = (mx + mn) / 2f;
            if (mx == mn) { h = 0; s = 0; return; }
            float d = mx - mn;
            s = l > 0.5f ? d / (2f - mx - mn) : d / (mx + mn);
            if (mx == r) h = ((g - b) / d + (g < b ? 6f : 0f)) * 60f;
            else if (mx == g) h = ((b - r) / d + 2f) * 60f;
            else h = ((r - g) / d + 4f) * 60f;
        }

        // ================= 印章 =================
        void BuildSealPage()
        {
            Panel p = Page();
            Head(p, "电子印章生成", "环形文字 + 五角星 + 做旧质感，导出透明 PNG");
            Lab(p, "单位名称", 26, 92, 8.5F, false, SUB);
            sealName = Input(p, 26, 114, 360, 36, false);
            sealName.Text = "十堰市示例科技有限公司";
            sealName.TextChanged += (s, e) => { if (sealPreview != null) sealPreview.Invalidate(); };

            Lab(p, "中间文字", 26, 172, 8.5F, false, SUB);
            sealCenter = Input(p, 26, 194, 360, 36, false);
            sealCenter.Text = "专用章";
            sealCenter.TextChanged += (s, e) => { if (sealPreview != null) sealPreview.Invalidate(); };

            Lab(p, "底部编号 / 日期", 26, 252, 8.5F, false, SUB);
            sealBottom = Input(p, 26, 274, 360, 36, false);
            sealBottom.Text = DateTime.Now.ToString("yyyy.MM.dd");
            sealBottom.TextChanged += (s, e) => { if (sealPreview != null) sealPreview.Invalidate(); };

            Lab(p, "印泥颜色", 26, 332, 8.5F, false, SUB);
            Color[] cols = { Color.FromArgb(198, 32, 40), Color.FromArgb(28, 58, 140), Color.FromArgb(22, 22, 22) };
            string[] names = { "朱红", "深蓝", "墨黑" };
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                Panel chip = new Panel();
                chip.Location = new Point(26 + i * 64, 354);
                chip.Size = new Size(54, 30);
                chip.Cursor = Cursors.Hand;
                chip.BackColor = cols[i];
                chip.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Pen pen = new Pen(sealColor == cols[ii] ? Color.White : UiKit.AA(60, 255, 255, 255), sealColor == cols[ii] ? 2F : 1F))
                        e.Graphics.DrawRectangle(pen, 0, 0, chip.Width - 1, chip.Height - 1);
                    TextRenderer.DrawText(e.Graphics, names[ii], new Font("Microsoft YaHei UI", 7F),
                        new Rectangle(0, 0, 54, 30), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                };
                chip.Click += (s, e) => { sealColor = cols[ii]; Invalidate(); sealPreview.Invalidate(); };
                p.Controls.Add(chip);
            }

            Lab(p, "导出尺寸", 26, 406, 8.5F, false, SUB);
            sealSize = new NumericUpDown();
            sealSize.Minimum = 600; sealSize.Maximum = 2400; sealSize.Increment = 100; sealSize.Value = 1200;
            sealSize.Location = new Point(26, 428);
            sealSize.Size = new Size(110, 30);
            sealSize.BackColor = INPUT; sealSize.ForeColor = TEXT;
            sealSize.BorderStyle = BorderStyle.FixedSingle;
            sealSize.Font = new Font("Microsoft YaHei UI", 9F);
            foreach (Control cc in sealSize.Controls) if (cc.GetType().Name == "UpDownButtons") cc.Visible = false;
            p.Controls.Add(sealSize);

            Lab(p, "提示：自用请遵守当地规定，本工具不生成任何机构真实印章。", 26, 486, 8F, false, SUB);
            Lab(p, "导出 PNG 为透明底，可直接盖在合同 / 图片上。", 26, 510, 8F, false, SUB);

            Panel c = Card(p, 430, 86, 414, 540, Color.FromArgb(198, 32, 40));
            sealPreview = new Panel();
            sealPreview.Location = new Point(16, 16);
            sealPreview.Size = new Size(382, 440);
            sealPreview.BackColor = UiKit.A(16, 22, 30);
            sealPreview.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int size = Math.Min(sealPreview.Width, sealPreview.Height) - 24;
                DrawSeal(e.Graphics, new Rectangle((sealPreview.Width - size) / 2, (sealPreview.Height - size) / 2, size, size), 1.0f);
            };
            c.Controls.Add(sealPreview);

            Btn(p, "导出透明 PNG", 430, 632, 220, 40, true).Click += (s, e) => ExportSeal();
            Btn(p, "打印预览", 664, 632, 120, 40, false).Click += (s, e) => sealPreview.Invalidate();
            pages[2] = p;
        }

        void DrawSeal(Graphics g, Rectangle box, float alpha)
        {
            int size = Math.Min(box.Width, box.Height);
            float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
            float R = size * 0.47f;
            Color cc = Color.FromArgb((int)(255 * alpha), sealColor.R, sealColor.G, sealColor.B);

            float ow = Math.Max(2f, size * 0.020f);
            using (Pen pen = new Pen(cc, ow))
                g.DrawEllipse(pen, cx - R, cy - R, R * 2, R * 2);
            using (Pen pen = new Pen(Color.FromArgb((int)(220 * alpha), sealColor.R, sealColor.G, sealColor.B), Math.Max(1f, size * 0.006f)))
                g.DrawEllipse(pen, cx - R * 0.87f, cy - R * 0.87f, R * 1.74f, R * 1.74f);

            // 五角星
            using (GraphicsPath star = new GraphicsPath())
            {
                PointF[] pts = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double ang = -Math.PI / 2 + i * Math.PI / 5;
                    float rr = (i % 2 == 0) ? R * 0.20f : R * 0.085f;
                    pts[i] = new PointF((float)(cx + Math.Cos(ang) * rr), (float)(cy - R * 0.02f + Math.Sin(ang) * rr));
                }
                star.AddPolygon(pts);
                using (SolidBrush b = new SolidBrush(cc))
                    g.FillPath(b, star);
            }

            string center = sealCenter.Text.Trim();
            if (center.Length > 0)
            {
                float fs = R * 0.15f;
                using (Font f = new Font("宋体", fs, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (SolidBrush b = new SolidBrush(cc))
                        g.DrawString(center, f, b, new RectangleF(cx - R * 0.7f, cy + R * 0.30f, R * 1.4f, fs * 1.3f), sf);
                }
            }

            string name = sealName.Text.Trim();
            if (name.Length > 0)
            {
                using (Font f = new Font("宋体", R * 0.105f, FontStyle.Bold, GraphicsUnit.Pixel))
                    DrawArcText(g, name, cx, cy, R * 0.75f, true, f, cc);
            }
            string bottom = sealBottom.Text.Trim();
            if (bottom.Length > 0)
            {
                using (Font f = new Font("宋体", R * 0.075f, FontStyle.Regular, GraphicsUnit.Pixel))
                    DrawArcText(g, bottom, cx, cy, R * 0.74f, false, f, cc);
            }

            // 做旧：叠几道极淡的弧线与斑点
            try
            {
                Random rnd = new Random(20261127);
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(22 * alpha), sealColor.R, sealColor.G, sealColor.B)))
                {
                    for (int i = 0; i < 26; i++)
                    {
                        float ang = (float)(rnd.NextDouble() * Math.PI * 2);
                        float rr = R * (0.55f + (float)rnd.NextDouble() * 0.4f);
                        float x = cx + (float)Math.Cos(ang) * rr, y = cy + (float)Math.Sin(ang) * rr;
                        g.FillEllipse(b, x, y, size * 0.006f, size * 0.006f);
                    }
                }
            }
            catch (Exception dg8) { Diag.Swallow("UtilToolbox.cs:8", dg8); }
        }

        void DrawArcText(Graphics g, string text, float cx, float cy, float radius, bool top, Font font, Color color)
        {
            List<char> cs = new List<char>();
            float totalW = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch)) continue;
                cs.Add(ch);
                totalW += g.MeasureString(ch.ToString(), font, 1000, StringFormat.GenericTypographic).Width;
            }
            if (cs.Count == 0) return;
            float totalDeg = totalW * 360f / (float)(2 * Math.PI * radius);
            if (totalDeg > (top ? 160f : 140f)) totalDeg = top ? 160f : 140f;
            float perDeg = totalDeg / cs.Count;
            float cur = top ? 270f - totalDeg / 2f : 90f + totalDeg / 2f;
            StringFormat sf = StringFormat.GenericTypographic;
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            using (SolidBrush b = new SolidBrush(color))
            {
                for (int i = 0; i < cs.Count; i++)
                {
                    float mid = top ? cur + perDeg / 2f : cur - perDeg / 2f;
                    float rad = mid * (float)Math.PI / 180f;
                    float x = cx + (float)Math.Cos(rad) * radius;
                    float y = cy + (float)Math.Sin(rad) * radius;
                    System.Drawing.Drawing2D.Matrix old = g.Transform.Clone();
                    g.TranslateTransform(x, y);
                    g.RotateTransform(mid + (top ? 90f : -90f));
                    g.DrawString(cs[i].ToString(), font, b, new PointF(0, 0), sf);
                    g.Transform.Dispose();
                    g.Transform = old;
                    cur = top ? cur + perDeg : cur - perDeg;
                }
            }
        }

        void ExportSeal()
        {
            using (SaveFileDialog fd = new SaveFileDialog())
            {
                fd.Title = "导出电子印章";
                fd.Filter = "PNG 图片 (*.png)|*.png";
                fd.FileName = "电子印章_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".png";
                if (fd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int size = (int)sealSize.Value;
                    using (Bitmap bmp = new Bitmap(size, size))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.Clear(Color.Transparent);
                            DrawSeal(g, new Rectangle(0, 0, size, size), 1.0f);
                        }
                        bmp.Save(fd.FileName, ImageFormat.Png);
                    }
                    MessageBox.Show(this, "已导出：\r\n" + fd.FileName, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ================= Base64 =================
        void BuildBase64Page()
        {
            Panel p = Page();
            Head(p, "图片 Base64 互转", "图片转成 Base64 文本，或把 Base64 还原成图片");
            Panel c1 = Card(p, 26, 86, 818, 258, ACC2);
            Lab(c1, "图片 → Base64", 16, 12, 9F, true, TEXT);
            b64Path = Input(c1, 16, 40, 600, 36, false);
            b64Path.ReadOnly = true;
            Btn(c1, "选择图片", 628, 40, 100, 36, false).Click += (s, e) => PickB64Image();
            Btn(c1, "编码", 740, 40, 62, 36, true).Click += (s, e) => EncodeB64();
            b64Out = Input(c1, 16, 88, 786, 112, true);
            b64Out.ReadOnly = true;
            b64Out.ScrollBars = ScrollBars.None;
            b64Out.Font = new Font("Consolas", 8.5F);
            b64Prefix = new CheckBox();
            b64Prefix.Text = "带 data:image 前缀";
            b64Prefix.ForeColor = SUB;
            b64Prefix.BackColor = Color.Transparent;
            b64Prefix.Font = new Font("Microsoft YaHei UI", 8F);
            b64Prefix.Location = new Point(16, 210);
            b64Prefix.AutoSize = true;
            c1.Controls.Add(b64Prefix);
            Btn(c1, "复制", 156, 206, 80, 32, false).Click += (s, e) =>
            {
                if (b64Out.Text.Length == 0) return;
                Clipboard.SetText(b64Out.Text);
                b64Status.Text = "Base64 已复制"; b64Status.ForeColor = ACC;
            };
            b64Status = Lab(c1, "", 250, 212, 8.5F, false, SUB);

            Panel c2 = Card(p, 26, 360, 818, 292, ACC);
            Lab(c2, "Base64 → 图片", 16, 12, 9F, true, TEXT);
            b64In = Input(c2, 16, 40, 540, 150, true);
            b64In.ScrollBars = ScrollBars.None;
            b64In.Font = new Font("Consolas", 8.5F);
            b64Pic = new PictureBox();
            b64Pic.Location = new Point(576, 40);
            b64Pic.Size = new Size(222, 222);
            b64Pic.BackColor = UiKit.A(12, 17, 24);
            b64Pic.SizeMode = PictureBoxSizeMode.Zoom;
            b64Pic.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(UiKit.AA(60, 45, 212, 191), 1F))
                    e.Graphics.DrawRectangle(pen, 0, 0, b64Pic.Width - 1, b64Pic.Height - 1);
            };
            c2.Controls.Add(b64Pic);
            Btn(c2, "解码预览", 16, 202, 120, 36, true).Click += (s, e) => DecodeB64();
            Btn(c2, "保存为 PNG", 148, 202, 130, 36, false).Click += (s, e) => SaveB64Image();
            Btn(c2, "清空", 290, 202, 80, 36, false).Click += (s, e) =>
            {
                b64In.Text = ""; if (b64Pic.Image != null) { b64Pic.Image.Dispose(); b64Pic.Image = null; }
                b64Status.Text = "";
            };
            Lab(c2, "支持带换行的 Base64，也接受 data:image/...;base64, 前缀", 16, 252, 8F, false, SUB);
            pages[3] = p;
        }

        void PickB64Image()
        {
            using (OpenFileDialog fd = new OpenFileDialog())
            {
                fd.Title = "选择图片";
                fd.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*";
                if (fd.ShowDialog(this) == DialogResult.OK) b64Path.Text = fd.FileName;
            }
        }

        void EncodeB64()
        {
            string path = b64Path.Text.Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show(this, "请先选择图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string s = Convert.ToBase64String(bytes);
                if (b64Prefix.Checked)
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
                    if (ext == "jpg") ext = "jpeg";
                    s = "data:image/" + ext + ";base64," + s;
                }
                b64Out.Text = s;
                b64Status.Text = "已编码 · " + bytes.Length.ToString("N0") + " 字节"; b64Status.ForeColor = ACC;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "编码失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void DecodeB64()
        {
            string s = b64In.Text.Trim();
            if (s.Length == 0) return;
            try
            {
                int comma = s.IndexOf(',');
                if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) s = s.Substring(comma + 1);
                s = s.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                byte[] bytes = Convert.FromBase64String(s);
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    Image img = Image.FromStream(ms);
                    Image copy = new Bitmap(img);
                    img.Dispose();
                    if (b64Pic.Image != null) b64Pic.Image.Dispose();
                    b64Pic.Image = copy;
                }
                b64Status.Text = "解码成功 · " + bytes.Length.ToString("N0") + " 字节"; b64Status.ForeColor = ACC;
            }
            catch (Exception ex)
            {
                b64Status.Text = "解码失败：" + ex.Message; b64Status.ForeColor = Color.FromArgb(248, 113, 113);
            }
        }

        void SaveB64Image()
        {
            if (b64Pic.Image == null) { DecodeB64(); if (b64Pic.Image == null) return; }
            using (SaveFileDialog fd = new SaveFileDialog())
            {
                fd.Title = "保存图片";
                fd.Filter = "PNG 图片 (*.png)|*.png";
                fd.FileName = "base64_" + DateTime.Now.ToString("HHmmss") + ".png";
                if (fd.ShowDialog(this) != DialogResult.OK) return;
                try { b64Pic.Image.Save(fd.FileName, ImageFormat.Png); b64Status.Text = "已保存：" + fd.FileName; b64Status.ForeColor = ACC; }
                catch (Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        // ================= 时间戳 =================
        void BuildTimePage()
        {
            Panel p = Page();
            Head(p, "时间戳转换", "Unix 时间戳与本地时间互转，秒 / 毫秒都认");

            Panel c1 = Card(p, 26, 86, 818, 152, ACC2);
            Lab(c1, "现在", 18, 14, 9F, true, TEXT);
            nowTime = Lab(c1, "", 18, 42, 17F, true, TEXT);
            nowTime.Font = new Font("Consolas", 17F, FontStyle.Bold);
            nowStamp = Lab(c1, "", 18, 82, 10F, false, SUB);
            nowStamp.Font = new Font("Consolas", 10F);
            Btn(c1, "复制秒", 610, 40, 90, 34, false).Click += (s, e) => Clipboard.SetText(((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString());
            Btn(c1, "复制毫秒", 710, 40, 100, 34, false).Click += (s, e) => Clipboard.SetText(((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds).ToString());

            Panel c2 = Card(p, 26, 254, 818, 178, ACC);
            Lab(c2, "时间戳 → 本地时间", 18, 12, 9F, true, TEXT);
            tsIn = Input(c2, 18, 42, 360, 36, false);
            tsIn.Font = new Font("Consolas", 9.5F);
            Btn(c2, "转换", 388, 42, 92, 36, true).Click += (s, e) => StampToTime();
            tsOut = Input(c2, 18, 96, 786, 40, false);
            tsOut.ReadOnly = true;
            Lab(c2, "支持 10 位秒 / 13 位毫秒；按本机时区显示", 18, 144, 8F, false, SUB);

            Panel c3 = Card(p, 26, 448, 818, 190, UiKit.A(96, 165, 250));
            Lab(c3, "本地时间 → 时间戳", 18, 12, 9F, true, TEXT);
            dtIn = Input(c3, 18, 42, 360, 36, false);
            dtIn.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Btn(c3, "转换", 388, 42, 92, 36, true).Click += (s, e) => TimeToStamp();
            dtOut = Input(c3, 18, 96, 786, 40, false);
            dtOut.ReadOnly = true;
            Lab(c3, "格式：2026-11-27 21:30:00（也可只写日期）", 18, 150, 8F, false, SUB);

            clock = new System.Windows.Forms.Timer();
            clock.Interval = 1000;
            clock.Tick += (s, e) => UpdateNow();
            clock.Start();
            UpdateNow();
            pages[4] = p;
        }

        void UpdateNow()
        {
            try
            {
                DateTime n = DateTime.Now;
                if (nowTime != null) nowTime.Text = n.ToString("yyyy-MM-dd  HH:mm:ss");
                long sec = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                if (nowStamp != null) nowStamp.Text = "秒 " + sec + "    毫秒 " + ms + "    时区 " + TimeZoneInfo.Local.DisplayName;
            }
            catch (Exception dg9) { Diag.Swallow("UtilToolbox.cs:9", dg9); }
        }

        void StampToTime()
        {
            try
            {
                string s = tsIn.Text.Trim();
                long v = long.Parse(s);
                if (s.Length > 12 || v > 99999999999L) v = v / 1000;
                DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(v).ToLocalTime();
                tsOut.Text = t.ToString("yyyy-MM-dd HH:mm:ss") + "  " + TimeZoneInfo.Local.DisplayName;
            }
            catch (Exception)
            {
                tsOut.Text = "格式不对：请输入纯数字时间戳";
            }
        }

        void TimeToStamp()
        {
            try
            {
                DateTime t;
                string s = dtIn.Text.Trim();
                if (!DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out t) &&
                    !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                {
                    dtOut.Text = "格式不对：示例 2026-11-27 21:30:00"; return;
                }
                long unix = (long)((t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
                dtOut.Text = "秒 " + unix + "    毫秒 " + unix * 1000;
            }
            catch (Exception)
            {
                dtOut.Text = "转换失败，请检查日期";
            }
        }

        // ================= 密码 =================
        void BuildPasswordPage()
        {
            Panel p = Page();
            Head(p, "密码生成", "加密安全随机，至少各取一位，可排除易混淆字符");

            Panel c = Card(p, 26, 86, 818, 360, UiKit.A(129, 140, 248));
            Lab(c, "长度", 18, 18, 8.5F, false, SUB);
            pwLen = new NumericUpDown();
            pwLen.Minimum = 6; pwLen.Maximum = 64; pwLen.Value = 16;
            pwLen.Location = new Point(18, 38);
            pwLen.Size = new Size(80, 30);
            pwLen.BackColor = INPUT; pwLen.ForeColor = TEXT; pwLen.BorderStyle = BorderStyle.FixedSingle;
            pwLen.Font = new Font("Microsoft YaHei UI", 9F);
            foreach (Control cc in pwLen.Controls) if (cc.GetType().Name == "UpDownButtons") cc.Visible = false;
            c.Controls.Add(pwLen);

            pwUpper = Chk(c, "大写 A-Z", 120, 40, true);
            pwLower = Chk(c, "小写 a-z", 232, 40, true);
            pwDigit = Chk(c, "数字 0-9", 344, 40, true);
            pwSymbol = Chk(c, "符号 !@#", 456, 40, true);
            pwNoAmb = Chk(c, "排除 O0 / l1 / |", 568, 40, false);

            Btn(c, "生成 3 组", 650, 72, 150, 40, true).Click += (s, e) => GenPasswords();

            for (int i = 0; i < 3; i++)
            {
                int y = 130 + i * 54;
                pwOut[i] = Input(c, 18, y, 640, 38, false);
                pwOut[i].ReadOnly = true;
                pwOut[i].Font = new Font("Consolas", 11F);
                int ii = i;
                Btn(c, "复制", 670, y + 2, 80, 34, false).Click += (s, e) =>
                {
                    if (pwOut[ii].Text.Length == 0) return;
                    Clipboard.SetText(pwOut[ii].Text);
                };
            }
            Btn(c, "全部复制", 18, 292, 120, 34, false).Click += (s, e) =>
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 3; i++) if (pwOut[i].Text.Length > 0) sb.AppendLine(pwOut[i].Text);
                if (sb.Length > 0) Clipboard.SetText(sb.ToString());
            };
            Lab(c, "长度越长越安全；重要账号建议 16 位以上并开启两步验证。", 156, 301, 8.5F, false, SUB);

            Lab(p, "生成过程只在本机内存中完成，不保存、不上传。", 26, 466, 8.5F, false, SUB);
            pages[5] = p;
        }

        CheckBox Chk(Control parent, string text, int x, int y, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.ForeColor = TEXT;
            c.BackColor = Color.Transparent;
            c.Font = new Font("Microsoft YaHei UI", 8.5F);
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.Checked = on;
            c.FlatStyle = FlatStyle.Flat;
            parent.Controls.Add(c);
            return c;
        }

        void GenPasswords()
        {
            string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string lower = "abcdefghijklmnopqrstuvwxyz";
            string digit = "0123456789";
            string symbol = "!@#$%^&*()-_=+[]{};:,.?";
            if (pwNoAmb.Checked)
            {
                upper = upper.Replace("O", "").Replace("I", "");
                lower = lower.Replace("l", "").Replace("o", "");
                digit = digit.Replace("0", "").Replace("1", "");
                symbol = symbol.Replace("|", "");
            }
            List<string> sets = new List<string>();
            if (pwUpper.Checked) sets.Add(upper);
            if (pwLower.Checked) sets.Add(lower);
            if (pwDigit.Checked) sets.Add(digit);
            if (pwSymbol.Checked) sets.Add(symbol);
            if (sets.Count == 0)
            {
                MessageBox.Show(this, "至少勾选一种字符类型", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int len = (int)pwLen.Value;
            if (len < sets.Count) len = sets.Count;

            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            try
            {
                for (int n = 0; n < 3; n++)
                {
                    StringBuilder all = new StringBuilder();
                    foreach (string s in sets) all.Append(s);
                    char[] buf = new char[len];
                    int pos = 0;
                    foreach (string s in sets) buf[pos++] = s[NextInt(rng, s.Length)];
                    while (pos < len) buf[pos++] = all[NextInt(rng, all.Length)];
                    for (int i = len - 1; i > 0; i--)
                    {
                        int j = NextInt(rng, i + 1);
                        char tmp = buf[i]; buf[i] = buf[j]; buf[j] = tmp;
                    }
                    pwOut[n].Text = new string(buf);
                }
            }
            finally { rng.Dispose(); }
        }

        static int NextInt(RandomNumberGenerator rng, int max)
        {
            if (max <= 1) return 0;
            byte[] b = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)max);
            while (true)
            {
                rng.GetBytes(b);
                uint v = BitConverter.ToUInt32(b, 0);
                if (v < limit) return (int)(v % (uint)max);
            }
        }
    }

    // 深色圆角按钮：不依赖皮肤，避免 Paper 皮肤把字翻黑
    class UtilBtn : Button
    {
        public Color Bg, Fg;
        bool over, down;
        public UtilBtn(Color bg, Color fg)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = bg;
            ForeColor = fg;
            Bg = bg; Fg = fg;
            TabStop = false;
        }
        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = down ? UiKit.Darken(Bg, 18) : (over ? UiKit.Lighten(Bg, 10) : Bg);
            using (GraphicsPath p = UiKit.RoundedPath(r, Math.Min(16, Height / 2)))
            {
                using (SolidBrush b = new SolidBrush(bg))
                    e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(UiKit.AA(over ? 150 : 70, 255, 255, 255), 1F))
                    e.Graphics.DrawPath(pen, p);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, r, Enabled ? Fg : UiKit.A(110, 122, 140),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
