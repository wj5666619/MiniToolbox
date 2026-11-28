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
    class BgForm : Form
    {
        public string BaseDir;
        public Func<int> GetMode;          // 当前生效的壁纸（0 关 / 1 静态 / 2 极光 / 3 星尘 / 4 流光 / 5 动态）
        public Action<int> ApplyMode2;
        public Func<bool> ImportStatic;
        public Func<bool> ImportDynamic;
        public Func<int> GetAlpha;
        public Action<int> ApplyAlpha2;

        Panel[] tiles = new Panel[6];
        Panel[] alphaChips = new Panel[5];
        int[] alphaVals = new int[] { 25, 40, 55, 70, 85 };
        System.Windows.Forms.Timer anim;
        double pt;
        int sel = 0;
        int dragX, dragY;
        bool dragging;
        Image thumbStatic, thumbDyn;
        Font fTitle, fDesc, fSmall, fBtn;
        Color cAccent = UiKit.A(94, 160, 255);
        string[] tName = new string[] { "关闭", "静态图片", "极光", "星尘", "流光", "动态壁纸" };
        string[] tDesc = new string[] { "只留深色底", "导入一张图", "呼吸光晕", "星点漂浮", "斜向光带", "视频 / GIF 导入" };

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED：整窗合成，动画缩略图不再闪
                return cp;
            }
        }

        // 缩略图格子：改成不透明 + 自绘双缓冲。原来用透明 Panel，每次 Invalidate 都要让父窗体
        // 先把背景重画一遍，12fps 动画下就是肉眼可见的闪烁。
        class TilePanel : Panel
        {
            public TilePanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint, true);
            }
        }

        public BgForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 480);
            BackColor = UiKit.A(13, 17, 24);
            DoubleBuffered = true;
            ShowInTaskbar = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            fTitle = new Font(Ui2.FamilyName, 10F, FontStyle.Bold);
            fDesc = new Font(Ui2.FamilyName, 8F);
            fSmall = new Font(Ui2.FamilyName, 8.5F);
            fBtn = new Font(Ui2.FamilyName, 9.5F, FontStyle.Bold);

            for (int i = 0; i < 6; i++)
            {
                int col = i % 3, row = i / 3;
                TilePanel t = new TilePanel();
                t.BackColor = UiKit.A(20, 26, 36);
                t.Location = new Point(24 + col * 228, 84 + row * 144);
                t.Size = new Size(210, 126);
                t.Cursor = Cursors.Hand;
                int mode = i;
                t.Paint += delegate (object s3, PaintEventArgs e3) { PaintTile(e3.Graphics, t, mode); };
                t.Click += delegate
                {
                    sel = mode;
                    try { if (ApplyMode2 != null) ApplyMode2(mode); } catch (Exception dg96_1) { Diag.Swallow("BgForm.cs:96", dg96_1); }
                    RefreshSel();
                };
                Controls.Add(t);
                tiles[i] = t;
            }

            for (int i = 0; i < 5; i++)
            {
                TilePanel c = new TilePanel();
                c.BackColor = UiKit.A(20, 26, 36);
                c.Location = new Point(96 + i * 68, 372);
                c.Size = new Size(60, 26);
                c.Cursor = Cursors.Hand;
                int v = alphaVals[i];
                c.Paint += delegate (object s3, PaintEventArgs e3)
                {
                    Graphics g = e3.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle r = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
                    bool on = (GetAlpha != null && GetAlpha() == v);
                    using (GraphicsPath p = UiKit.RoundedPath(r, 8))
                    {
                        using (SolidBrush b = new SolidBrush(on ? UiKit.A(44, 72, 116) : UiKit.A(30, 37, 48)))
                            g.FillPath(b, p);
                        using (Pen pp = new Pen(on ? cAccent : UiKit.AA(26, 255, 255, 255), 1f))
                            g.DrawPath(pp, p);
                    }
                    using (Font f = new Font(Ui2.FamilyName, 8.5F))
                        TextRenderer.DrawText(g, v + "%", f, r, on ? UiKit.Txt(Color.White) : UiKit.A(168, 182, 202),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                c.Click += delegate
                {
                    try { if (ApplyAlpha2 != null) ApplyAlpha2(v); } catch (Exception dg130_1) { Diag.Swallow("BgForm.cs:130", dg130_1); }
                    foreach (Panel pc in alphaChips) if (pc != null) pc.Invalidate();
                    foreach (Panel pc in tiles) if (pc != null) pc.Invalidate();
                };
                Controls.Add(c);
                alphaChips[i] = c;
            }

            Button bPic = MkBtn("导入图片", UiKit.A(38, 46, 60), Color.White, 24, 420, 112, 36);
            bPic.Click += delegate
            {
                bool ok = false;
                try { if (ImportStatic != null) ok = ImportStatic(); } catch (Exception dg142_1) { Diag.Swallow("BgForm.cs:142", dg142_1); }
                if (ok) { sel = 1; RefreshThumbs(); RefreshSel(); }
            };
            Controls.Add(bPic);

            Button bDyn = MkBtn("导入动态壁纸", UiKit.A(46, 74, 150), Color.White, 144, 420, 140, 36);
            bDyn.Click += delegate
            {
                bool ok = false;
                try { if (ImportDynamic != null) ok = ImportDynamic(); } catch (Exception dg151_1) { Diag.Swallow("BgForm.cs:151", dg151_1); }
                if (ok) { sel = 5; RefreshThumbs(); RefreshSel(); }
            };
            Controls.Add(bDyn);

            Button bDone = MkBtn("完成", UiKit.A(94, 160, 255), Color.White, 598, 420, 98, 36);
            bDone.Click += delegate { Close(); };
            Controls.Add(bDone);

            Panel bClose = new Panel();
            bClose.BackColor = Color.Transparent;
            bClose.Size = new Size(34, 30);
            bClose.Location = new Point(ClientSize.Width - 50, 10);
            bClose.Cursor = Cursors.Hand;
            bClose.Paint += delegate (object s3, PaintEventArgs e3)
            {
                using (Font f = new Font(Ui2.FamilyName, 11F, FontStyle.Bold))
                    TextRenderer.DrawText(e3.Graphics, "✕", f, new Rectangle(0, 0, bClose.Width, bClose.Height),
                        UiKit.A(150, 162, 180), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);

            MouseDown += delegate (object s3, MouseEventArgs e3)
            {
                if (e3.Y < 60) { dragging = true; dragX = e3.X; dragY = e3.Y; }
            };
            MouseMove += delegate (object s3, MouseEventArgs e3)
            {
                if (!dragging) return;
                Left += e3.X - dragX;
                Top += e3.Y - dragY;
            };
            MouseUp += delegate { dragging = false; };

            anim = new System.Windows.Forms.Timer();
            anim.Interval = 80;
            anim.Tick += delegate
            {
                pt += 0.08;
                for (int i = 0; i < tiles.Length; i++)
                {
                    if (tiles[i] == null) continue;
                    if (i >= 2 && i <= 4) tiles[i].Invalidate();
                }
            };
            anim.Start();
        }

        Button MkBtn(string text, Color bg, Color fg, int x, int y, int w, int h)
        {
            RoundBtn b = new RoundBtn(bg, fg);
            b.Text = text;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.Font = fBtn;
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { sel = GetMode(); } catch (Exception dg217_1) { Diag.Swallow("BgForm.cs:217", dg217_1); }
            RefreshThumbs();
            RefreshSel();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (anim != null) anim.Stop(); } catch (Exception dg224_1) { Diag.Swallow("BgForm.cs:224", dg224_1); }
            try { if (thumbStatic != null) thumbStatic.Dispose(); } catch (Exception dg225_1) { Diag.Swallow("BgForm.cs:225", dg225_1); }
            try { if (thumbDyn != null) thumbDyn.Dispose(); } catch (Exception dg226_1) { Diag.Swallow("BgForm.cs:226", dg226_1); }
            base.OnFormClosed(e);
        }

        void RefreshSel()
        {
            foreach (Panel t in tiles) if (t != null) t.Invalidate();
            foreach (Panel c in alphaChips) if (c != null) c.Invalidate();
        }

        void RefreshThumbs()
        {
            try
            {
                string p = Path.Combine(BaseDir, "bg.jpg");
                if (File.Exists(p))
                {
                    try { if (thumbStatic != null) thumbStatic.Dispose(); } catch (Exception dg243_1) { Diag.Swallow("BgForm.cs:243", dg243_1); }
                    thumbStatic = null;
                    try { thumbStatic = LoadCopy(p); } catch (Exception dg245_1) { Diag.Swallow("BgForm.cs:245", dg245_1); }
                }
                string d = Path.Combine(BaseDir, "壁纸缓存", "动态壁纸");
                if (Directory.Exists(d))
                {
                    string[] fs = Directory.GetFiles(d, "*.jpg");
                    if (fs.Length > 0)
                    {
                        Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
                        try { if (thumbDyn != null) thumbDyn.Dispose(); } catch (Exception dg254_1) { Diag.Swallow("BgForm.cs:254", dg254_1); }
                        thumbDyn = null;
                        try { thumbDyn = LoadCopy(fs[0]); } catch (Exception dg256_1) { Diag.Swallow("BgForm.cs:256", dg256_1); }
                    }
                }
            }
            catch (Exception dg260_1) { Diag.Swallow("BgForm.cs:260", dg260_1); }
        }

        static Image LoadCopy(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image tmp = Image.FromStream(fs))
                return new Bitmap(tmp);
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle all = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (GraphicsPath p = UiKit.RoundedPath(all, 14))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(all, UiKit.A(20, 26, 36), UiKit.A(10, 13, 19), 90F))
                    g.FillPath(b, p);
                using (Pen pp = new Pen(UiKit.AA(56, 255, 255, 255), 1f))
                    g.DrawPath(pp, p);
            }
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, ClientSize.Width, 3),
                UiKit.A(96, 165, 250), UiKit.A(147, 197, 253), 0F))
                g.FillRectangle(b, 0, 0, ClientSize.Width, 3);
            TextRenderer.DrawText(g, "背景与壁纸", fTitle, new Rectangle(24, 14, 300, 24), UiKit.Txt(Color.White),
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "选中即时生效 · 动态壁纸支持导入视频或 GIF（自动抽 12 秒画面，循环播放）",
                fDesc, new Rectangle(24, 40, 560, 20), UiKit.A(146, 162, 182),
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "背景强度", fSmall, new Rectangle(24, 372, 70, 26), UiKit.A(190, 202, 220),
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        }

        void PaintTile(Graphics g, Panel t, int mode)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, t.Width - 1, t.Height - 1);
            using (GraphicsPath path = UiKit.RoundedPath(r, 12))
            {
                g.SetClip(path);
                using (LinearGradientBrush gb = new LinearGradientBrush(r, UiKit.A(32, 40, 54), UiKit.A(11, 14, 20), 90F))
                    g.FillRectangle(gb, r);
                try
                {
                    if (mode == 1)
                    {
                        if (thumbStatic != null) CoverDraw(g, thumbStatic, r);
                        else TipText(g, r, "还没有导入图片");
                    }
                    else if (mode == 5)
                    {
                        if (thumbDyn != null) CoverDraw(g, thumbDyn, r);
                        else TipText(g, r, "还没有导入动态壁纸");
                    }
                    else if (mode >= 2 && mode <= 4)
                    {
                        int os = PagePanel.WallStyle;
                        double ot = PagePanel.WallT;
                        int oa = PagePanel.WallAlpha;
                        PagePanel.WallStyle = mode - 1;
                        PagePanel.WallT = pt + mode * 1.7;
                        PagePanel.WallAlpha = 90;
                        try { PagePanel.PaintAnim(g, r); } catch (Exception dg324_1) { Diag.Swallow("BgForm.cs:324", dg324_1); }
                        PagePanel.WallStyle = os;
                        PagePanel.WallT = ot;
                        PagePanel.WallAlpha = oa;
                    }
                }
                catch (Exception dg330_1) { Diag.Swallow("BgForm.cs:330", dg330_1); }
                // 底部信息带
                Rectangle band = new Rectangle(0, r.Height - 44, r.Width, 44);
                using (LinearGradientBrush bg2 = new LinearGradientBrush(band, Color.FromArgb(30, 6, 9, 14), Color.FromArgb(225, 6, 9, 14), 90F))
                    g.FillRectangle(bg2, band);
                TextRenderer.DrawText(g, tName[mode], fTitle, new Rectangle(12, r.Height - 42, r.Width - 24, 20), UiKit.Txt(Color.White),
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, tDesc[mode], fDesc, new Rectangle(12, r.Height - 22, r.Width - 24, 16), UiKit.A(150, 164, 184),
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                g.ResetClip();

                bool on = (sel == mode);
                using (GraphicsPath path2 = UiKit.RoundedPath(r, 12))
                {
                    using (Pen pp = new Pen(on ? cAccent : UiKit.AA(30, 255, 255, 255), on ? 2f : 1f))
                        g.DrawPath(pp, path2);
                }
                if (on)
                {
                    Rectangle ck = new Rectangle(r.Width - 30, 10, 18, 18);
                    using (SolidBrush b = new SolidBrush(cAccent)) g.FillEllipse(b, ck);
                    TextRenderer.DrawText(g, "✓", fSmall, ck, UiKit.Txt(Color.White),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        static void CoverDraw(Graphics g, Image im, Rectangle r)
        {
            float s = Math.Max((float)r.Width / im.Width, (float)r.Height / im.Height);
            int w = (int)(im.Width * s), h = (int)(im.Height * s);
            g.DrawImage(im, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        }

        void TipText(Graphics g, Rectangle r, string txt)
        {
            using (Font f = new Font(Ui2.FamilyName, 8.5F))
                TextRenderer.DrawText(g, txt, f, new Rectangle(r.X, r.Y, r.Width, r.Height - 44),
                    UiKit.A(124, 138, 158), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}