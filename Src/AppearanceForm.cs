using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.IO;
using System.Text;

namespace ShutdownTool
{
    // v19.0 外观设置：五套皮肤缩略预览 + 界面缩放滑杆（替代托盘里那条文字子菜单）
    // v19.6 加「主题包」：皮肤 + 音色一次切换（强调色本来就随皮肤色相走，不用另设）
    class AppearanceForm : Form
    {
        int pickSkin = UiKit.SkinIdx;          // 预览里点中的那套（未应用）
        int pickPack = Sfx.Pack;
        int pickScale = (int)Math.Round(Ui2.UiScale * 100);
        bool wasSkinChanged, wasScaleChanged, wasPackChanged;

        // v19.6 主题包：名字 / 说明 / 皮肤索引 / 音色索引
        static readonly string[] PK_NAME = { "静谧便当", "赛博霓虹", "弹性色块", "纸感手绘" };
        static readonly string[] PK_SUB = { "青绿 · 标准", "青色 · 清亮", "紫罗兰 · 清亮", "纸黄 · 沉稳" };
        static readonly int[] PK_SKIN = { 3, 1, 2, 4 };
        static readonly int[] PK_PACK = { 1, 0, 0, 2 };

        static readonly Color[] P_BG =
        {
            Color.FromArgb(11, 13, 17), Color.FromArgb(6, 16, 24), Color.FromArgb(17, 18, 24),
            Color.FromArgb(10, 15, 22), Color.FromArgb(246, 241, 229)
        };
        static readonly Color[] P_CARD =
        {
            Color.FromArgb(26, 28, 32), Color.FromArgb(13, 34, 48), Color.FromArgb(28, 30, 40),
            Color.FromArgb(20, 27, 38), Color.FromArgb(255, 252, 243)
        };
        static readonly Color[] P_ACC =
        {
            Color.FromArgb(255, 212, 0), Color.FromArgb(0, 229, 255), Color.FromArgb(255, 92, 138),
            Color.FromArgb(61, 126, 255), Color.FromArgb(255, 225, 44)
        };
        static string T(string zh, string en) { return Lang.T(zh, en); }

        public AppearanceForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(652, 604);
            BackColor = UiKit.A(16, 20, 26);
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            Panel bar = new Panel();
            bar.BackColor = UiKit.A(24, 28, 36);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(652, 44);
            Controls.Add(bar);

            Label ti = new Label();
            ti.Text = T("外观与缩放", "Appearance & scale");
            ti.ForeColor = UiKit.A(236, 240, 246);
            ti.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            ti.BackColor = Color.Transparent;
            ti.Location = new Point(18, 12);
            ti.AutoSize = true;
            bar.Controls.Add(ti);

            Button x = new Button();
            x.Text = "✕";
            x.FlatStyle = FlatStyle.Standard;
            x.FlatAppearance.BorderSize = 0;
            x.BackColor = UiKit.A(24, 28, 36);
            x.ForeColor = UiKit.A(150, 158, 172);
            x.Location = new Point(610, 9);
            x.Size = new Size(32, 26);
            x.TabStop = false;
            x.Click += delegate { Close(); };
            bar.Controls.Add(x);

            Point dp = Point.Empty;
            bar.MouseDown += delegate (object s, MouseEventArgs e) { dp = e.Location; };
            bar.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && dp != Point.Empty)
                    Location = new Point(Location.X + e.X - dp.X, Location.Y + e.Y - dp.Y);
            };
            bar.MouseUp += delegate { dp = Point.Empty; };

            // ---------- 主题包 ----------
            Label capP = new Label();
            capP.Text = T("主题包（皮肤 + 音色 一次切换，选完点「应用并重启」）", "Theme packs — skin + sound tone in one click");
            capP.ForeColor = UiKit.A(148, 158, 176);
            capP.Font = new Font("Microsoft YaHei UI", 8.5F);
            capP.BackColor = Color.Transparent;
            capP.Location = new Point(20, 56);
            capP.AutoSize = true;
            Controls.Add(capP);

            Panel[] packs = new Panel[PK_NAME.Length];
            for (int i = 0; i < PK_NAME.Length; i++)
            {
                int pi = i;
                Panel pc = new Panel();
                pc.Location = new Point(20 + i * 158, 80);
                pc.Size = new Size(148, 62);
                pc.Cursor = Cursors.Hand;
                pc.BackColor = UiKit.A(20, 24, 31);
                pc.Paint += delegate (object s2, PaintEventArgs e2) { DrawPack(e2.Graphics, (Control)s2, pi, pickSkin == PK_SKIN[pi]); };
                pc.Click += delegate { pickSkin = PK_SKIN[pi]; pickPack = PK_PACK[pi]; SyncFlags(); Sfx.Click(); Invalidate(true); };
                Controls.Add(pc);
                packs[i] = pc;
            }

            // ---------- 皮肤 ----------
            Label cap1 = new Label();
            cap1.Text = T("界面风格（也可以单独换皮肤）", "Style — click to preview");
            cap1.ForeColor = UiKit.A(148, 158, 176);
            cap1.Font = new Font("Microsoft YaHei UI", 8.5F);
            cap1.BackColor = Color.Transparent;
            cap1.Location = new Point(20, 152);
            cap1.AutoSize = true;
            Controls.Add(cap1);

            Panel[] cards = new Panel[UiKit.SKIN_NAMES.Length];
            for (int i = 0; i < UiKit.SKIN_NAMES.Length; i++)
            {
                int idx = i;
                int col = i % 3, row = i / 3;
                Panel pc = new Panel();
                pc.Location = new Point(20 + col * 208, 174 + row * 130);
                pc.Size = new Size(196, 120);
                pc.Cursor = Cursors.Hand;
                pc.BackColor = UiKit.A(20, 24, 31);
                pc.Paint += delegate (object s2, PaintEventArgs e2) { DrawThumb(e2.Graphics, (Control)s2, idx, pickSkin == idx); };
                pc.Click += delegate { pickSkin = idx; SyncFlags(); Sfx.Click(); Invalidate(true); };
                Controls.Add(pc);
                cards[i] = pc;

                Label ln = new Label();
                ln.Text = UiKit.SKIN_NAMES[i];
                ln.ForeColor = (i == 4) ? UiKit.Ink : UiKit.A(214, 222, 234);
                ln.BackColor = Color.Transparent;
                ln.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                ln.Location = new Point(10, 3);
                ln.AutoSize = false;
                ln.Cursor = Cursors.Hand;
                ln.Size = new Size(176, 18);
                ln.Click += delegate { pickSkin = idx; SyncFlags(); Sfx.Click(); Invalidate(true); };
                pc.Controls.Add(ln);
            }

            // ---------- 缩放 ----------
            Label cap2 = new Label();
            cap2.Text = T("界面缩放（改完需要重启一次才生效）", "UI scale — takes effect after restart");
            cap2.ForeColor = UiKit.A(148, 158, 176);
            cap2.Font = new Font("Microsoft YaHei UI", 8.5F);
            cap2.BackColor = Color.Transparent;
            cap2.Location = new Point(20, 432);
            cap2.AutoSize = true;
            Controls.Add(cap2);

            TrackBar tb = new TrackBar();
            tb.Minimum = 95;
            tb.Maximum = 115;
            tb.Value = Math.Max(95, Math.Min(115, pickScale));
            tb.SmallChange = 5;
            tb.LargeChange = 5;
            tb.TickFrequency = 5;
            tb.Location = new Point(18, 454);
            tb.Size = new Size(300, 32);
            tb.BackColor = UiKit.A(20, 24, 31);
            Controls.Add(tb);

            Label pct = new Label();
            pct.Text = pickScale + "%";
            pct.ForeColor = UiKit.A(120, 176, 255);
            pct.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            pct.BackColor = Color.Transparent;
            pct.Location = new Point(326, 458);
            pct.Size = new Size(70, 24);
            Controls.Add(pct);

            Button sysBtn = new RoundBtn(UiKit.A(40, 48, 62), UiKit.A(230, 236, 244));
            sysBtn.Text = T("跟随系统 DPI", "Match system DPI");
            sysBtn.Font = new Font("Microsoft YaHei UI", 8.5F);
            sysBtn.Location = new Point(400, 454);
            sysBtn.Size = new Size(112, 30);
            sysBtn.Click += delegate
            {
                int dpiPct = SystemDpiPercent();
                tb.Value = Math.Max(95, Math.Min(115, dpiPct));
                Sfx.Click();
            };
            Controls.Add(sysBtn);

            Label sysTip = new Label();
            sysTip.Text = T("当前系统：" + SystemDpiPercent() + "%（超过 115% 会按 115% 渲染，避免卡片溢出）", "System: " + SystemDpiPercent() + "%");
            sysTip.ForeColor = UiKit.A(120, 130, 146);
            sysTip.Font = new Font("Microsoft YaHei UI", 8F);
            sysTip.BackColor = Color.Transparent;
            sysTip.Location = new Point(20, 494);
            sysTip.Size = new Size(560, 18);
            Controls.Add(sysTip);

            tb.ValueChanged += delegate
            {
                pickScale = tb.Value;
                pct.Text = tb.Value + "%";
                wasScaleChanged = (tb.Value != (int)Math.Round(Ui2.UiScale * 100));
            };

            Button apply = new RoundBtn(UiKit.A(52, 108, 190), Color.White);
            apply.Text = T("应用并重启", "Apply & restart");
            apply.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            apply.Location = new Point(404, 548);
            apply.Size = new Size(124, 34);
            apply.Click += delegate { Apply(true); };
            Controls.Add(apply);

            Button cancel = new RoundBtn(UiKit.A(40, 48, 62), UiKit.A(226, 232, 240));
            cancel.Text = T("关闭", "Close");
            cancel.Font = new Font("Microsoft YaHei UI", 9F);
            cancel.Location = new Point(532, 548);
            cancel.Size = new Size(94, 34);
            cancel.Click += delegate { Apply(false); };
            Controls.Add(cancel);

            KeyDown += delegate (object s2, KeyEventArgs e2) { if (e2.KeyCode == Keys.Escape) Close(); };
            SyncFlags();
        }

        void SyncFlags()
        {
            wasSkinChanged = (pickSkin != UiKit.SkinIdx);
            wasPackChanged = (pickPack != Sfx.Pack);
        }

        static int SystemDpiPercent()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(g.DpiX / 96f * 100);
            }
            catch { return 100; }
        }

        void Apply(bool restart)
        {
            try
            {
                if (wasPackChanged) Sfx.SetPack(pickPack);
                if (wasSkinChanged) UiKit.ApplySkin(pickSkin, true);
                if (wasScaleChanged) Ui2.SaveUiScale(pickScale);
                Diag.Info("外观应用：主题包 " + PK_NAME[pickPack] + " / 皮肤 " + pickSkin + " / 缩放 " + pickScale + "% / 重启=" + restart);
                if (wasSkinChanged || wasScaleChanged || wasPackChanged)
                    Sfx.Ok();
                else
                    Sfx.Click();
            }
            catch (Exception dg259_1) { Diag.Swallow("AppearanceForm.cs:259", dg259_1); }
            if (restart && (wasSkinChanged || wasScaleChanged))
            {
                try { Application.Restart(); return; } catch (Exception dg262_1) { Diag.Swallow("AppearanceForm.cs:262", dg262_1); }
            }
            Close();
        }

        // 主题包小卡：肤色块 + 声波三条 + 名字与配置
        void DrawPack(Graphics g, Control c, int idx, bool selected)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sk = PK_SKIN[idx];
            Rectangle r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            using (GraphicsPath pa = UiKit.RoundedPath(r, 6))
            using (SolidBrush b = new SolidBrush(P_BG[sk]))
            {
                g.FillPath(b, pa);
                using (Pen p = new Pen(selected ? P_ACC[sk] : UiKit.A(48, 56, 70), selected ? 2f : 1f))
                    g.DrawPath(p, pa);
            }
            using (SolidBrush b = new SolidBrush(selected ? P_ACC[sk] : Color.FromArgb(110, P_ACC[sk])))
                g.FillRectangle(b, 10, 14, 3, c.Height - 28);
            using (Font f1 = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold))
                TextRenderer.DrawText(g, PK_NAME[idx], f1, new Point(22, 10),
                    sk == 4 ? UiKit.Ink : UiKit.A(232, 238, 246));
            using (Font f2 = new Font("Microsoft YaHei UI", 8F))
                TextRenderer.DrawText(g, PK_SUB[idx], f2, new Point(22, 32),
                    sk == 4 ? Color.FromArgb(110, 24, 22, 20) : UiKit.A(146, 158, 176));
            if (selected)
            {
                using (Font f3 = new Font("Segoe UI", 9F, FontStyle.Bold))
                    TextRenderer.DrawText(g, "✓", f3, new Point(126, 10), sk == 4 ? UiKit.Ink : P_ACC[sk]);
            }
        }

        // 缩略预览：底色 + 一块卡片 + 强调色条 + 两条"文字"，做成小界面而不是色板
        void DrawThumb(Graphics g, Control c, int idx, bool selected)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            using (GraphicsPath pa = UiKit.RoundedPath(r, 4))
            {
                using (SolidBrush b = new SolidBrush(P_BG[idx]))
                    g.FillPath(b, pa);
                using (Pen p = new Pen(selected ? UiKit.A(120, 176, 255) : UiKit.A(56, 64, 78), selected ? 2f : 1f))
                    g.DrawPath(p, pa);
            }

            bool paper = idx == 4;
            // 左侧竖栏
            using (SolidBrush sb = new SolidBrush(selected ? P_ACC[idx] : Color.FromArgb(70, P_ACC[idx])))
                g.FillRectangle(sb, 0, 0, 4, c.Height);

            // 卡片区
            Rectangle cr = new Rectangle(14, 30, c.Width - 28, c.Height - 46);
            using (GraphicsPath cp = UiKit.RoundedPath(cr, paper ? 2 : 4))
            {
                using (SolidBrush b = new SolidBrush(P_CARD[idx]))
                    g.FillPath(b, cp);
                using (Pen p = new Pen(Color.FromArgb(paper ? 190 : 46, paper ? 24 : 255, paper ? 22 : 255, paper ? 20 : 255), 1f))
                    g.DrawPath(p, cp);
            }

            // 顶条 + 两条假文字
            using (SolidBrush b = new SolidBrush(P_ACC[idx]))
            {
                g.FillRectangle(b, 24, 40, 44, 4);
                g.FillRectangle(b, 24, 54, c.Width - 70, 2);
                g.FillRectangle(b, 24, 62, (c.Width - 70) * 2 / 3, 2);
            }
            using (SolidBrush b = new SolidBrush(paper ? Color.FromArgb(24, 22, 20) : Color.FromArgb(150, 160, 176)))
                g.FillRectangle(b, 24, c.Height - 26, 60, 3);

            using (Font f = new Font("Microsoft YaHei UI", 8F))
                TextRenderer.DrawText(g, "Aa", f, new Point(c.Width - 30, c.Height - 30),
                    paper ? UiKit.Ink : UiKit.A(200, 210, 226));

            if (selected)
            {
                using (Font f2 = new Font("Segoe UI", 10F, FontStyle.Bold))
                    TextRenderer.DrawText(g, "✓", f2, new Point(2, c.Height - 24), UiKit.A(120, 176, 255));
            }
        }
    }
}