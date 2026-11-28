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
    class PagePanel : Panel
    {
        public static Image Wall;
        public static bool WallOn;
        // v17.9：内置动态壁纸。0=关 1=极光 2=星尘 3=流光；WallAlpha 是背景可见度(0-100)
        public static int WallStyle;
        public static int WallAlpha = 55;
        public static double WallT;
        // v17.9：可导入的动态壁纸。视频/GIF 导入时先抽成 jpg 帧序列，这里按时间推进播放
        public static string[] DynFrames;
        public static double DynFps = 12;
        public static double DynTime;
        static Image dynImg;
        static int dynIdx = -1;
        static float[] wx, wy, wr;   // 星尘用的确定性随机表

        static void InitStars()
        {
            if (wx != null) return;
            wx = new float[120]; wy = new float[120]; wr = new float[120];
            for (int i = 0; i < wx.Length; i++)
            {
                wx[i] = (float)((i * 37.7 % 100.0) / 100.0);
                wy[i] = (float)((i * 61.3 % 100.0) / 100.0);
                wr[i] = (float)((i * 13.1 % 100.0) / 100.0);
            }
        }

        // 动效层：低透明度的极光块 / 星尘 / 流光，画在页面底色之上、卡片之下
        public static void PaintAnim(Graphics g, Rectangle rc)
        {
            int st = WallStyle;
            if (st <= 0 || rc.Width < 60 || rc.Height < 60) return;
            double t = WallT;
            int vis = WallAlpha;
            if (vis < 5) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (st == 1)
            {
                Color[]cols = new Color[] { UiKit.A(45, 212, 191), UiKit.A(99, 102, 241), UiKit.A(56, 189, 248) };
                for (int i = 0; i < 3; i++)
                {
                    double ph = t * 0.30 + i * 2.2;
                    int cx = (int)(rc.Width * (0.5 + 0.36 * Math.Sin(ph)));
                    int cy = (int)(rc.Height * (0.5 + 0.30 * Math.Cos(ph * 0.8 + i)));
                    int rad = (int)(Math.Min(rc.Width, rc.Height) * (0.42 + 0.10 * Math.Sin(ph * 0.7 + i)));
                    if (rad < 20) continue;
                    Rectangle er = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);
                    using (GraphicsPath pth = new GraphicsPath())
                    {
                        pth.AddEllipse(er);
                        using (PathGradientBrush pg = new PathGradientBrush(pth))
                        {
                            pg.CenterPoint = new PointF((float)cx, (float)cy);
                            pg.CenterColor = Color.FromArgb((int)(vis * 1.15), cols[i]);
                            pg.SurroundColors = new Color[] { Color.FromArgb(0, cols[i]) };
                            g.FillPath(pg, pth);
                        }
                    }
                }
            }
            else if (st == 2)
            {
                InitStars();
                for (int i = 0; i < wx.Length; i++)
                {
                    int x = (int)(wx[i] * rc.Width);
                    double yy = wy[i] * (rc.Height + 60);
                    yy = (yy - t * (6 + (i % 5) * 5));
                    yy = ((yy % (rc.Height + 60)) + (rc.Height + 60)) % (rc.Height + 60);
                    int y = (int)yy - 30;
                    int sz = 1 + (int)(wr[i] * 2.2);
                    int alpha = (int)(vis * 1.2 * (0.45 + 0.55 * Math.Sin(t * 1.7 + i)));
                    if (alpha < 4) continue;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(190, alpha), 150, 220, 255)))
                        g.FillEllipse(b, x, y, sz, sz);
                }
            }
            else if (st == 3)
            {
                int bw = Math.Max(120, rc.Width / 6);
                double speed = 46;
                for (int i = 0; i < 5; i++)
                {
                    int x = (int)((t * speed + i * (rc.Width + bw) / 4.0) % (rc.Width + bw * 2)) - bw * 2;
                    int c1 = (int)(vis * 1.1), c0 = (int)(vis * 0.75);
                    using (LinearGradientBrush lb = new LinearGradientBrush(
                        new Rectangle(x, -20, bw * 2, rc.Height + 40),
                        Color.FromArgb(c0, 45, 212, 191), Color.FromArgb(c1, 99, 102, 241), 0f))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Colors = new Color[] { Color.FromArgb(0, 45, 212, 191), Color.FromArgb(c0, 45, 212, 191), Color.FromArgb(0, 99, 102, 241) };
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        lb.InterpolationColors = cb;
                        g.FillRectangle(lb, x, 0, bw * 2, rc.Height);
                    }
                }
            }
        }

        public PagePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.SupportsTransparentBackColor, true);   // v16.0：让直属 Label 真正透出自绘底色
            BackColor = Color.Transparent;
        }

        // v17.9：动态壁纸帧按时间取图，只保留当前这一张，内存不会随帧数膨胀
        public static void PaintDyn(Graphics g, Rectangle rc)
        {
            if (DynFrames == null || DynFrames.Length == 0 || rc.Width <= 0 || rc.Height <= 0) return;
            try
            {
                int idx = (int)(DynTime * DynFps);
                idx %= DynFrames.Length;
                if (idx < 0) idx = 0;
                if (idx != dynIdx || dynImg == null)
                {
                    try { if (dynImg != null) dynImg.Dispose(); } catch (Exception dg141_1) { Diag.Swallow("PagePanel.cs:141", dg141_1); }
                    dynImg = null;
                    try { dynImg = Image.FromFile(DynFrames[idx]); } catch (Exception dg143_1) { Diag.Swallow("PagePanel.cs:143", dg143_1); }
                    dynIdx = idx;
                }
                if (dynImg == null) return;
                float s = Math.Max((float)rc.Width / dynImg.Width, (float)rc.Height / dynImg.Height);
                int w = (int)(dynImg.Width * s), h = (int)(dynImg.Height * s);
                g.DrawImage(dynImg, rc.X + (rc.Width - w) / 2, rc.Y + (rc.Height - h) / 2, w, h);
                int mv = 205 - Math.Min(180, Math.Max(0, WallAlpha));
                using (Brush b = new SolidBrush(Color.FromArgb(mv, 12, 16, 22)))
                    g.FillRectangle(b, rc);
            }
            catch (Exception dg154_1) { Diag.Swallow("PagePanel.cs:154", dg154_1); }
        }

        // v8.5：渐变改成纯纵向（42° → 90°），这样"页面某一点的颜色"可以被精确算出来。
        // 页面直属 Label 一律按自己所在高度取同一个色当底色，灰底块就从根上消失了。
        public static Color BgAt(int y, int h)
        {
            if (h < 1) h = 1;
            double t = (double)y / h;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            if (UiKit.Paper) return UiKit.PaperBg;
            int r = (int)Math.Round(25 + (9 - 25) * t);
            int g2 = (int)Math.Round(32 + (12 - 32) * t);
            int b2 = (int)Math.Round(45 + (17 - 45) * t);
            return Color.FromArgb(r, g2, b2);
        }

        // 静态壁纸（含强度遮罩）合成到一张位图缓存里，尺寸/图片/强度/皮肤任一变化才重做。
        // 这样面板打开、切页、悬停重绘时都只是 BitBlt，不会再出现"先黑底再贴图"的闪一下。
        static Image bgCacheImg;
        static int bgcW, bgcH, bgcAlpha, bgcHash, bgcSkin;
        static bool bgcOn;

        void PaintStaticBg(Graphics g, Rectangle rc)
        {
            int hsh = (Wall == null) ? 0 : Wall.GetHashCode();
            if (bgCacheImg == null || bgcW != rc.Width || bgcH != rc.Height || bgcAlpha != WallAlpha
                || bgcHash != hsh || bgcSkin != UiKit.SkinIdx || bgcOn != WallOn)
            {
                try { if (bgCacheImg != null) bgCacheImg.Dispose(); } catch (Exception dg184_1) { Diag.Swallow("PagePanel.cs:184", dg184_1); }
                bgCacheImg = null;
                Bitmap bmp = new Bitmap(Math.Max(1, rc.Width), Math.Max(1, rc.Height));
                using (Graphics bgg = Graphics.FromImage(bmp))
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush gb = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Rectangle(0, 0, rc.Width, rc.Height), UiKit.A(25, 32, 45), UiKit.A(9, 12, 17), 90f))
                        bgg.FillRectangle(gb, 0, 0, rc.Width, rc.Height);
                    if (Wall != null)
                    {
                        float sc = Math.Max((float)rc.Width / Wall.Width, (float)rc.Height / Wall.Height);
                        int w = (int)(Wall.Width * sc), h = (int)(Wall.Height * sc);
                        bgg.DrawImage(Wall, (rc.Width - w) / 2, (rc.Height - h) / 2, w, h);
                        int mv = 205 - Math.Min(180, Math.Max(0, WallAlpha));
                        using (Brush b = new SolidBrush(Color.FromArgb(mv, 12, 16, 22)))
                            bgg.FillRectangle(b, 0, 0, rc.Width, rc.Height);
                    }
                }
                bgCacheImg = bmp;
                bgcW = rc.Width; bgcH = rc.Height; bgcAlpha = WallAlpha; bgcHash = hsh;
                bgcSkin = UiKit.SkinIdx; bgcOn = WallOn;
            }
            if (bgCacheImg != null) g.DrawImageUnscaled(bgCacheImg, rc.X, rc.Y);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle rc = ClientRectangle;
            if (Environment.GetEnvironmentVariable("TB_PROBE") == "1") { using (Brush mb = new SolidBrush(Color.Magenta)) e.Graphics.FillRectangle(mb, rc); return; }
            if (rc.Width <= 0 || rc.Height <= 0) return;
            if (UiKit.SkinIdx == 0)
            {
                // 新粗野：纯黑底 + 深灰网格，壁纸与动效一律不画，避免画面花
                using (Brush pb = new SolidBrush(Color.FromArgb(8, 8, 8)))
                    e.Graphics.FillRectangle(pb, rc);
                using (Pen gp = new Pen(Color.FromArgb(18, 28, 28, 26), 1f))
                {
                    for (int gx = 0; gx < rc.Width; gx += 32) e.Graphics.DrawLine(gp, gx, 0, gx, rc.Height);
                    for (int gy = 0; gy < rc.Height; gy += 32) e.Graphics.DrawLine(gp, 0, gy, rc.Width, gy);
                }
                DrawPageAccent(e.Graphics, rc);
                return;
            }
            if (UiKit.Paper)
            {
                // v19.16 纸感手绘：米白纸底 + 更淡的方格，避免网格抢正文
                using (Brush pb = new SolidBrush(UiKit.PaperBg))
                    e.Graphics.FillRectangle(pb, rc);
                using (Pen gp = new Pen(Color.FromArgb(12, 150, 140, 120), 1f))
                {
                    for (int gx = 0; gx < rc.Width; gx += 32) e.Graphics.DrawLine(gp, gx, 0, gx, rc.Height);
                    for (int gy = 0; gy < rc.Height; gy += 32) e.Graphics.DrawLine(gp, 0, gy, rc.Width, gy);
                }
                // 四周压一点点暖褐，避免整块死白
                using (System.Drawing.Drawing2D.LinearGradientBrush vg = new System.Drawing.Drawing2D.LinearGradientBrush(
                    rc, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(14, 146, 130, 100), 90f))
                    e.Graphics.FillRectangle(vg, rc);
                DrawPageAccent(e.Graphics, rc);
                return;
            }
            using (System.Drawing.Drawing2D.LinearGradientBrush gb = new System.Drawing.Drawing2D.LinearGradientBrush(
                rc, UiKit.A(25, 32, 45), UiKit.A(9, 12, 17), 90f))
                e.Graphics.FillRectangle(gb, rc);
            if (DynFrames != null && DynFrames.Length > 0)
            {
                PaintDyn(e.Graphics, rc);
            }
            else if (WallOn && Wall != null)
            {
                try { PaintStaticBg(e.Graphics, rc); }
                catch (Exception dg252_1) { Diag.Swallow("PagePanel.cs:252", dg252_1); }
            }
            if (DynFrames == null || DynFrames.Length == 0)
                try { PaintAnim(e.Graphics, rc); } catch (Exception dg255_1) { Diag.Swallow("PagePanel.cs:255", dg255_1); }
            // v19.4：内容区底部渐隐——壁纸只在顶部露脸，下面压深，保证文字读得清
            {
                int fadeY = rc.Height * 42 / 100;
                if (fadeY < rc.Height - 1)
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush fb = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Rectangle(rc.X, fadeY, rc.Width, rc.Height - fadeY),
                        Color.FromArgb(0, 9, 12, 18), Color.FromArgb(232, 9, 12, 18), 90f))
                        e.Graphics.FillRectangle(fb, rc.X, fadeY, rc.Width, rc.Height - fadeY);
                }
            }
            if (UiKit.SkinIdx == 1)
            {
                using (Pen gp = new Pen(Color.FromArgb(26, 34, 211, 238), 1f))
                {
                    for (int gx = 0; gx < rc.Width; gx += 32) e.Graphics.DrawLine(gp, gx, 0, gx, rc.Height);
                    for (int gy = 0; gy < rc.Height; gy += 32) e.Graphics.DrawLine(gp, 0, gy, rc.Width, gy);
                }
                using (Pen sp = new Pen(Color.FromArgb(18, 244, 114, 182), 1f))
                    for (int gy = 0; gy < rc.Height; gy += 8) e.Graphics.DrawLine(sp, 0, gy, rc.Width, gy);
            }
            DrawPageAccent(e.Graphics, rc);
            // v16.0 Hero 带：顶部 88px 深青渐变，下缘 22px 渐隐收回页面底色，
            // 再压一道极淡的青绿分隔光。页面标题就落在这条带子里。
            int hh = 0;   // v17.6：Hero 由全宽 Header 承担
            if (rc.Height < hh + 80) hh = Math.Max(44, rc.Height / 3);
            if (hh <= 0) return;
            Rectangle hr = new Rectangle(rc.X, rc.Y, rc.Width, hh);
            using (System.Drawing.Drawing2D.LinearGradientBrush hb = new System.Drawing.Drawing2D.LinearGradientBrush(
                hr, UiKit.A(21, 78, 94), UiKit.A(13, 33, 48), 90f))
                e.Graphics.FillRectangle(hb, hr);
            using (Pen hpTop = new Pen(UiKit.AA(64, 118, 236, 226), 1f))
                e.Graphics.DrawLine(hpTop, rc.X, rc.Y, rc.Right, rc.Y);
            Rectangle tr = new Rectangle(rc.X, rc.Y + hh, rc.Width, 22);
            using (System.Drawing.Drawing2D.LinearGradientBrush tb = new System.Drawing.Drawing2D.LinearGradientBrush(
                tr, UiKit.AA(255, 10, 21, 33), UiKit.AA(0, 25, 32, 45), 90f))
                e.Graphics.FillRectangle(tb, tr);
            using (Pen hline = new Pen(UiKit.AA(44, 45, 212, 191), 1f))
                e.Graphics.DrawLine(hline, rc.X + 20, rc.Y + hh, rc.Right - 20, rc.Y + hh);
        }

        // v19.16：所有页面统一的标题锚点——左侧 3px 强调短竖条，给绝对布局的十页一个共同节奏
        void DrawPageAccent(Graphics g, Rectangle rc)
        {
            try
            {
                if (rc.Width < 60 || rc.Height < 70) return;
                Color ac = UiKit.Paper ? Color.FromArgb(214, 176, 86) : UiKit.A(45, 212, 191);
                using (Pen p = new Pen(ac, 3f))
                    g.DrawLine(p, 18, 29, 18, 51);
                using (Pen p2 = new Pen(UiKit.Paper ? Color.FromArgb(120, 214, 176, 86) : UiKit.AA(100, 45, 212, 191), 1f))
                    g.DrawLine(p2, 22, 40, 27, 40);
            }
            catch { }
        }
    }
}