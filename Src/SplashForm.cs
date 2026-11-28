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
    class SplashForm : Form
    {
        System.Windows.Forms.Timer tm;
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        bool finished;
        public Action OnDone;

        const int W = 600, H = 360;
        const double T_IN_END = 0.24, T_HOLD = 1.28, T_OUT = 0.30;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            BackColor = UiKit.A(9, 11, 15);
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            tm = new System.Windows.Forms.Timer();
            tm.Interval = 20;
            tm.Tick += (s2, e2) => Step();
            tm.Start();
            sw.Start();
            MouseDown += (s2, e2) => { if (sw.Elapsed.TotalSeconds > 0.12) Finish(); };
            KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Escape || e2.KeyCode == Keys.Space || e2.KeyCode == Keys.Enter) Finish(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                using (System.Drawing.Drawing2D.GraphicsPath gp = RoundRect(new Rectangle(0, 0, Width, Height), 22))
                    Region = new Region(gp);
            }
            catch (Exception dg62_1) { Diag.Swallow("SplashForm.cs:62", dg62_1); }
        }

        static System.Drawing.Drawing2D.GraphicsPath RoundRect(Rectangle r, int rad)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            rad = (int)Math.Round(rad * UiKit.SkinRadScale);
            if (rad < 3) rad = 3;
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static System.Drawing.Drawing2D.GraphicsPath HexPath(PointF c, float r)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            PointF[] pts = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 180.0 * (-90 + i * 60);
                pts[i] = new PointF((float)(c.X + r * Math.Cos(a)), (float)(c.Y + r * Math.Sin(a)));
            }
            p.AddPolygon(pts);
            return p;
        }

        static StringFormat CenterSF()
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            return sf;
        }

        // 逐字加字距地居中画一行字：比 DrawString 整行更"排版"，也更容易做轻微上浮
        void DrawTracked(Graphics g, string txt, Font f, Brush br, float centerY, float tracking)
        {
            if (txt == null || txt.Length == 0) return;
            float[] w = new float[txt.Length];
            float total = 0;
            for (int i = 0; i < txt.Length; i++)
            {
                w[i] = g.MeasureString(txt.Substring(i, 1), f, new PointF(0, 0), StringFormat.GenericTypographic).Width;
                if (txt[i] == ' ') w[i] += f.Size * 0.34f;      // 逐字画时空格宽度会被算成 0，这里补回来
                total += w[i];
            }
            total += tracking * (txt.Length - 1);
            float x = (W - total) / 2f;
            for (int i = 0; i < txt.Length; i++)
            {
                if (txt[i] != ' ')
                    g.DrawString(txt.Substring(i, 1), f, br, new PointF(x, centerY), StringFormat.GenericTypographic);
                x += w[i] + tracking;
            }
        }

        void DrawTracked(Graphics g, string txt, Font f, Color c, float centerY, float tracking)
        {
            using (SolidBrush b = new SolidBrush(c)) DrawTracked(g, txt, f, b, centerY, tracking);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            try { tm.Stop(); } catch (Exception dg131_1) { Diag.Swallow("SplashForm.cs:131", dg131_1); }
            try { Close(); } catch (Exception dg132_1) { Diag.Swallow("SplashForm.cs:132", dg132_1); }
            Action d = OnDone;
            OnDone = null;
            if (d != null) { try { d(); } catch (Exception dg135_1) { Diag.Swallow("SplashForm.cs:135", dg135_1); } }
        }

        void Step()
        {
            double T = sw.Elapsed.TotalSeconds;
            if (T < T_IN_END) Opacity = T / T_IN_END;
            else if (T < T_HOLD) Opacity = 1;
            else
            {
                double k = (T - T_HOLD) / T_OUT;
                Opacity = Math.Max(0.0, 1.0 - k);
                if (k >= 1) { Finish(); return; }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            Rectangle rc = ClientRectangle;
            double T = sw.Elapsed.TotalSeconds;

            // ---- 底色：竖向渐变 + 中心微光 ----
            using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(
                rc, UiKit.A(19, 23, 31), UiKit.A(5, 6, 9), 90f))
                g.FillRectangle(lb, rc);
            Rectangle glowR = new Rectangle(W / 2 - 190, 118 - 190, 380, 380);
            using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                gp.AddEllipse(glowR);
                using (System.Drawing.Drawing2D.PathGradientBrush pb = new System.Drawing.Drawing2D.PathGradientBrush(gp))
                {
                    pb.CenterColor = UiKit.AA(52, 236, 72, 84);
                    pb.SurroundColors = new Color[] { UiKit.AA(0, 236, 72, 84) };
                    pb.CenterPoint = new PointF(W / 2f, 122f);
                    g.FillPath(pb, gp);
                }
            }

            // ---- 暗角：四周收紧，视线自然收到中央 ----
            using (System.Drawing.Drawing2D.GraphicsPath vp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                vp.AddEllipse(new Rectangle(-W / 3, -H / 4, W + W * 2 / 3, H + H / 2));
                using (System.Drawing.Drawing2D.PathGradientBrush pb = new System.Drawing.Drawing2D.PathGradientBrush(vp))
                {
                    pb.CenterColor = UiKit.AA(0, 0, 0, 0);
                    pb.SurroundColors = new Color[] { UiKit.AA(190, 0, 0, 0) };
                    pb.CenterPoint = new PointF(W / 2f, 150f);
                    pb.FocusScales = new PointF(0.62f, 0.62f);
                    g.FillPath(pb, vp);
                }
            }

            // ---- 细网格雾点：越靠中心越亮，边缘自然消失 ----
            for (int gy = 18; gy < H; gy += 22)
                for (int gx = 18; gx < W; gx += 22)
                {
                    float dx = gx - W / 2f, dy = gy - 140f;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    int al = (int)(26 - dist / 16f);
                    if (al <= 2) continue;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(22, al), 150, 168, 194)))
                        g.FillRectangle(b, gx, gy, 1, 1);
                }

            // ---- 开场扫描带：一次竖向掠过，给"科技启动"的动势 ----
            if (T < 0.95)
            {
                float sy = (float)(T / 0.95) * (H + 80) - 40;
                RectangleF sbr = new RectangleF(-20, sy - 34, W + 40, 68);
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    sbr, UiKit.AA(0, 190, 220, 255), UiKit.AA(26, 190, 220, 255), 90f))
                    g.FillRectangle(b, sbr);
                using (Pen p = new Pen(UiKit.AA(52, 210, 235, 255), 1f))
                    g.DrawLine(p, 14, sy, W - 14, sy);
            }

            // ---- 入场节奏 ----
            double inK = Math.Min(1.0, T / 0.46);
            double ease = 1 - Math.Pow(1 - inK, 3);
            double back = 0.0;
            if (inK > 0) { double q = inK - 1; back = 1 + 2.70158 * q * q * q + 1.70158 * q * q; }   // easeOutBack
            int aIn = (int)(255 * ease);
            double pulse = 0.5 + 0.5 * Math.Sin(T * 3.4);

            int cx = W / 2, cy = 126;
            float rad = (float)(54 * (0.62 + 0.38 * back));

            // ---- 呼吸式多层光晕 ----
            for (int i = 5; i >= 1; i--)
            {
                int rr = (int)(rad + 16 + i * 18);
                int al = (int)((4 + 8 * pulse) * (6 - i) / 5.0) + 6;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(30, al), 244, 88, 96)))
                    g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
            }

            // ---- 外圈：两道反向旋转弧 + 刻度点 ----
            float ringR = rad + 26;
            float rot = (float)(T * 120);
            using (Pen p = new Pen(Color.FromArgb((int)(150 * ease), 240, 96, 104), 1.6f))
            {
                g.DrawArc(p, cx - ringR, cy - ringR, ringR * 2, ringR * 2, rot, 68);
                g.DrawArc(p, cx - ringR, cy - ringR, ringR * 2, ringR * 2, rot + 190, 42);
            }
            using (Pen p = new Pen(Color.FromArgb((int)(110 * ease), 120, 168, 220), 1.2f))
                g.DrawArc(p, cx - ringR, cy - ringR, ringR * 2, ringR * 2, -rot * 1.35f, 54);
            for (int i = 0; i < 24; i++)
            {
                double a = Math.PI * 2 * i / 24.0;
                double br = 0.35 + 0.65 * Math.Abs(Math.Sin(T * 3.2 + i * 0.42));
                int al = (int)((i % 3 == 0 ? 120 : 80) * br * ease);
                float r1 = ringR + 6, r2 = ringR + 6 + (float)(4 * br);
                Color tickC = i % 3 == 0 ? Color.FromArgb(al, 120, 208, 255) : Color.FromArgb(al, 214, 224, 240);
                using (Pen p = new Pen(tickC, 1f))
                    g.DrawLine(p, (float)(cx + r1 * Math.Cos(a)), (float)(cy + r1 * Math.Sin(a)),
                        (float)(cx + r2 * Math.Cos(a)), (float)(cy + r2 * Math.Sin(a)));
            }

            // ---- 轨道粒子（三个，带拖尾） ----
            for (int k = 0; k < 3; k++)
            {
                double baseA = T * 1.9 + k * (Math.PI * 2 / 3);
                for (int t2 = 4; t2 >= 0; t2--)
                {
                    double a2 = baseA - t2 * 0.10;
                    float pr = ringR + 2;
                    float px2 = (float)(cx + pr * Math.Cos(a2));
                    float py2 = (float)(cy + pr * Math.Sin(a2));
                    int al = (int)((70 - t2 * 13) * ease);
                    if (al <= 0) continue;
                    float sz = 3.4f - t2 * 0.4f;
                    int cr2 = 255, cg2 = (k == 1 ? 214 : 130), cb2 = (k == 1 ? 150 : 120);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(al, cr2, cg2, cb2)))
                        g.FillEllipse(b, px2 - sz / 2, py2 - sz / 2, sz, sz);
                }
            }

            // ---- 六边形宝石 ----
            using (System.Drawing.Drawing2D.GraphicsPath hex = HexPath(new PointF(cx, cy), rad))
            {
                RectangleF hr = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    hr, UiKit.A(250, 92, 100), UiKit.A(122, 16, 26), 55f))
                    g.FillPath(b, hex);
                // 六边形内的高光扫过
                g.SetClip(hex);
                float sx = (float)(((T * 0.9) % 1.6 - 0.3) * rad * 2);
                RectangleF swr = new RectangleF(cx - rad + sx - rad * 0.5f, cy - rad, rad, rad * 2);
                using (System.Drawing.Drawing2D.LinearGradientBrush sb = new System.Drawing.Drawing2D.LinearGradientBrush(
                    swr, UiKit.AA(0, 255, 240, 240), UiKit.AA(56, 255, 240, 240), 0f))
                    g.FillRectangle(sb, swr);
                g.ResetClip();
                using (Pen p = new Pen(UiKit.AA((int)(150 * ease), 255, 208, 212), 1.5f))
                    g.DrawPath(p, hex);
            }
            using (System.Drawing.Drawing2D.GraphicsPath hx2 = HexPath(new PointF(cx, cy), (float)(rad * 0.80)))
            using (Pen p = new Pen(UiKit.AA((int)(46 * ease), 255, 255, 255), 1f))
                g.DrawPath(p, hx2);

            // ---- 闪电 ----
            using (System.Drawing.Drawing2D.GraphicsPath bolt = new System.Drawing.Drawing2D.GraphicsPath())
            {
                float s2b = rad * 0.92f;
                PointF[] pts = new PointF[]
                {
                    new PointF(cx - 0.06f * s2b, cy - 0.60f * s2b),
                    new PointF(cx + 0.22f * s2b, cy - 0.60f * s2b),
                    new PointF(cx + 0.02f * s2b, cy - 0.06f * s2b),
                    new PointF(cx + 0.20f * s2b, cy - 0.06f * s2b),
                    new PointF(cx - 0.16f * s2b, cy + 0.60f * s2b),
                    new PointF(cx - 0.01f * s2b, cy + 0.02f * s2b),
                    new PointF(cx - 0.22f * s2b, cy + 0.02f * s2b)
                };
                bolt.AddPolygon(pts);
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), Color.FromArgb(aIn, 255, 255, 255),
                    Color.FromArgb(aIn, 255, 226, 200), 60f))
                    g.FillPath(b, bolt);
                using (Pen p = new Pen(Color.FromArgb((int)(aIn * 0.5), 255, 255, 255), 1f))
                    g.DrawPath(p, bolt);
            }

            // ---- 字标：上浮 + 淡入 ----
            float rise = (float)(14 * (1 - ease));
            using (Font f = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold))
            using (System.Drawing.Drawing2D.LinearGradientBrush tb = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 172, W, 48), Color.FromArgb(aIn, 255, 255, 255),
                Color.FromArgb(aIn, 176, 186, 202), 90f))
                DrawTracked(g, "小工具箱", f, tb, 196 + rise - 18, 3.2f);
            using (System.Drawing.Drawing2D.LinearGradientBrush ab = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(cx - 30, 224, 60, 2), UiKit.AA(0, 244, 88, 96), UiKit.AA((int)(150 * ease), 244, 88, 96), 0f))
                g.FillRectangle(ab, cx - 30, 224, 60, 2);
            using (Font f = new Font("Microsoft YaHei UI", 8.5F))
                DrawTracked(g, "DESKTOP TOOLBOX   ·   v" + UiKit.VER, f,
                    Color.FromArgb((int)(aIn * 0.66), 132, 143, 160), 236 + rise * 0.7f, 1.6f);

            // ---- 进度 ----
            int pw = 340, ph = 3;
            int px = cx - pw / 2, py = 276;
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(aIn * 0.55), 32, 38, 48)))
                g.FillRectangle(b, px, py, pw, ph);
            double pkRaw = Math.Min(1.0, T / T_HOLD);
            double pk = 1 - Math.Pow(1 - pkRaw, 2.1);
            int fw = (int)(pw * pk);
            if (fw > 0)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(px, py, Math.Max(2, fw), ph), UiKit.A(242, 78, 88), UiKit.A(255, 162, 96), 0f))
                    g.FillRectangle(b, px, py, fw, ph);
                // 流光：一小段高亮在已走进度里来回跑
                int sw2 = Math.Max(26, fw / 5);
                float pos = (float)((T * 0.75 % 1.0) * Math.Max(1, fw + sw2)) - sw2;
                int x1 = Math.Max(px, px + (int)pos);
                int x2 = Math.Min(px + fw, px + (int)pos + sw2);
                if (x2 > x1)
                    using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Rectangle(x1 - 10, py, (x2 - x1) + 20, ph), UiKit.AA(0, 255, 255, 235), UiKit.AA(90, 255, 255, 235), 0f))
                        g.FillRectangle(b, x1, py, x2 - x1, ph);
            }

            // ---- 阶段文案 + 百分比 ----
            string stage = pkRaw < 0.34 ? "正在加载核心组件" : (pkRaw < 0.74 ? "正在读取界面偏好" : "就绪 · 正在进入");
            int dots = (int)(T * 2.5) % 4;
            if (pkRaw < 0.96) stage += new string('·', dots);
            using (Font f = new Font("Microsoft YaHei UI", 8.5F))
                g.DrawString(stage, f, new SolidBrush(Color.FromArgb((int)(aIn * 0.72), 128, 139, 156)),
                    new RectangleF(px, py + 12, pw - 56, 18), CenterSF());
            using (Font f = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold))
                g.DrawString((int)(pk * 100) + "%", f, new SolidBrush(Color.FromArgb((int)(aIn * 0.8), 196, 206, 222)),
                    new RectangleF(px + pw - 52, py + 12, 52, 18),
                    new StringFormat() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

            // ---- 四角 HUD 折角 ----
            int cl = 26, co = 12;
            int ca = (int)(72 * ease);
            using (Pen p = new Pen(Color.FromArgb(ca, 236, 92, 100), 1.4f))
            {
                g.DrawLine(p, co, co, co + cl, co); g.DrawLine(p, co, co, co, co + cl);
                g.DrawLine(p, W - co, co, W - co - cl, co); g.DrawLine(p, W - co, co, W - co, co + cl);
                g.DrawLine(p, co, H - co, co + cl, H - co); g.DrawLine(p, co, H - co, co, H - co - cl);
                g.DrawLine(p, W - co, H - co, W - co - cl, H - co); g.DrawLine(p, W - co, H - co, W - co, H - co - cl);
            }

            // ---- 边框与角标 ----
            using (Pen p = new Pen(UiKit.AA((int)(60 * ease), 255, 190, 190), 1f))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1);
            using (Font f = new Font("Consolas", 7F))
            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(aIn * 0.35), 110, 124, 142)))
                g.DrawString("MINITOOLBOX · 小工具箱", f, b, new PointF(20, H - 22));
        }
    }
}