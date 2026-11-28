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
    static class UiKit
    {
        // v10.0：把系统标题栏染成深色（Win10 1809+ / Win11 用 20，老版本退回 19）。
        // 不加这个，弹窗上面就顶着一条白条，跟深色界面完全不是一套东西。
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        public static void DarkCaption(Form f)
        {
            try
            {
                if (f == null) return;
                IntPtr h = f.Handle;
                if (h == IntPtr.Zero) return;
                int on = 1;
                int r = DwmSetWindowAttribute(h, 20, ref on, 4);
                if (r != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
            }
            catch (Exception dg41_1) { Diag.Swallow("UiKit.cs:41", dg41_1); }
        }
        /// <summary>全程序唯一的版本号来源，改这里即可（侧栏、启动画面、更新公告、更新包清单同步）。</summary>
        public const string VER = "19.15";

        public static GraphicsPath RoundedPath(Rectangle rc, int r)
        {
            int rs = (int)Math.Round(r * SkinRadScale);
            if (rs < 2) rs = 2;
            if (rs > 40) rs = 40;
            r = rs;
            GraphicsPath p = new GraphicsPath();
            int d = r * 2;
            p.AddArc(rc.X, rc.Y, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ============ v10.0 主题引擎 ============
        // 全程序的"强调色"不再写死，改为按色相旋转到主题色：
        //   只旋转 蓝/青 系（hue 180°~270°）、中高饱和、中高亮度的颜色；
        //   近灰的底色、绿/红/橙/黄色的语义色、以及所有文字灰阶一律原样返回。
        // 这样换主题=换一个色相，整套强调色（按钮、光效、图标、图表、进度条）一起变，不会花。
        public static double ThemeHue = 218.0;     // 218 = 原来的品牌蓝
        public static string ThemeName = "蓝";
        static readonly Dictionary<int, Color> acCache = new Dictionary<int, Color>();

        public static readonly string[] THEME_NAMES = { "蓝", "青", "紫", "薄荷", "玫红" };
        public static readonly double[] THEME_HUES = { 218.0, 190.0, 272.0, 158.0, 330.0 };
        public static readonly int[] THEME_RGB = { 0x3B82F6, 0x0EA5E9, 0x8B5CF6, 0x10B981, 0xE11D48 };

        public static Color ThemeColor
        {
            get
            {
                int v = THEME_RGB[ThemeIndex()];
                return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            }
        }

        public static int ThemeIndex()
        {
            for (int i = 0; i < THEME_HUES.Length; i++) if (Math.Abs(THEME_HUES[i] - ThemeHue) < 2.0) return i;
            return 0;
        }

        // ============ v18.4 界面风格：四套皮肤，可在托盘里切换 ============
        // 做法：颜色全部走 UiKit.A/AA 这一层，所以"换皮肤"= 在色相映射之后再加一道色调变换 + 圆角比例。
        //   0 新粗野  = 亮度反转成纸色 + 中线压黑 + 直角化 + 亮黄强调
        //   1 赛博HUD = 暗部压到近黑 + 青色霓虹强调 + 小圆角
        //   2 弹性色块 = 底色微提亮偏蓝 + 紫色强调 + 超大圆角
        //   3 静谧便当 = 保留深中性底 + 青绿强调 + 原圆角（默认）
        public static int SkinIdx = 3;
        public static readonly string[] SKIN_NAMES = { "新粗野", "赛博HUD", "弹性色块", "静谧便当", "纸感手绘" };
        public static readonly double[] SKIN_HUES = { 62.0, 190.0, 272.0, 172.0, 48.0 };
        public static readonly string SKIN_FILE = "皮肤.txt";

        // v18.7 纸感手绘：米白纸底 + 墨黑描边 + 荧光马克笔色块（唯一一套浅色皮肤）
        public static bool Paper { get { return SkinIdx == 4; } }
        public static readonly Color Ink = Color.FromArgb(255, 24, 22, 20);
        public static readonly Color PaperBg = Color.FromArgb(255, 246, 241, 229);
        public static readonly Color PaperCard = Color.FromArgb(255, 255, 252, 243);
        public static readonly Color PaperLine = Color.FromArgb(255, 205, 196, 176);

        public static string SkinName { get { return SKIN_NAMES[Math.Max(0, Math.Min(SKIN_NAMES.Length - 1, SkinIdx))]; } }

        public static float SkinRadScale
        {
            get
            {
                return SkinIdx == 0 ? 0.45f : (SkinIdx == 1 ? 0.80f : (SkinIdx == 2 ? 1.70f : (SkinIdx == 4 ? 0.28f : 1.00f)));
            }
        }

        public static void LoadSkin(string baseDir)
        {
            try
            {
                string f = Path.Combine(baseDir, SKIN_FILE);
                if (!File.Exists(f)) return;
                SetSkin(File.ReadAllText(f).Trim(), false);
            }
            catch (Exception dg126_1) { Diag.Swallow("UiKit.cs:126", dg126_1); }
        }
        public static void SetSkin(string name, bool save)
        {
            for (int i = 0; i < SKIN_NAMES.Length; i++)
                if (name == SKIN_NAMES[i] || name == i.ToString()) { ApplySkin(i, save); return; }
        }
        public static void ApplySkin(int idx, bool save)
        {
            if (idx < 0 || idx >= SKIN_NAMES.Length) idx = 3;
            SkinIdx = idx;
            ThemeHue = SKIN_HUES[idx];
            ThemeName = SKIN_NAMES[idx];
            acCache.Clear();
            if (save)
            {
                try { File.WriteAllText(Path.Combine(Application.StartupPath, SKIN_FILE), SKIN_NAMES[idx], new UTF8Encoding(false)); }
                catch (Exception dg143_1) { Diag.Swallow("UiKit.cs:143", dg143_1); }
            }
        }

        // 色调变换：强调色已被 ThemeHue 转过，这里处理"中性色"的黑白关系与彩色饱和度
        static Color SkinTone(int r, int g, int b)
        {
            double h, s2, l;
            RgbToHsl(r, g, b, out h, out s2, out l);
            bool vivid = (s2 >= 0.30 && l >= 0.20);
            switch (SkinIdx)
            {
                case 0:   // 新粗野：纯黑底 + 亮黄海报色 + 直角，白字保持满对比
                    if (vivid) return HslToColor(62.0, Math.Min(1.0, s2 * 1.20), Math.Min(0.50, Math.Max(0.28, l)));
                    if (l < 0.34) return Color.FromArgb(255, 6, 6, 6);            // 底色压成墨黑，边界更硬
                    if (l < 0.62) return Color.FromArgb(255, 226, 226, 226);      // 次要字与描边提亮
                    return Color.FromArgb(255, 252, 252, 250);                    // 正文纯白
                case 1:   // 赛博HUD：暗部压到近黑，暖色改霓虹洋红，青蓝改霓虹青
                    if (vivid)
                    {
                        double nh = (h < 75.0 || h > 290.0) ? 322.0 : 190.0;
                        return HslToColor(nh, Math.Min(1.0, s2 * 1.25), Math.Max(0.48, Math.Min(0.72, l)));
                    }
                    if (l < 0.40) return HslToColor(215.0, Math.Min(0.55, s2 + 0.10), l * 0.55);
                    return HslToColor(h, s2, l * 0.97);
                case 2:   // 弹性色块：底色微提亮偏蓝紫，暗部不死黑
                    if (vivid) return HslToColor(272.0, Math.Min(1.0, s2 * 1.10), Math.Max(0.42, l));
                    if (l < 0.26) return HslToColor(236.0, s2 * 1.1, l * 1.28);
                    return Color.FromArgb(255, r, g, b);
                default:  // 静谧便当：暗部略深，强调色转青绿，其余不动
                    if (l < 0.26) return HslToColor(h, s2 * 0.92, l * 0.90);
                    return Color.FromArgb(255, r, g, b);
                case 4:   // 纸感手绘 v2：米白纸底 + 纸白卡片 + 暖墨文字，不再用整块黑卡压白纸
                    if (vivid)
                    {
                        // 黄/青/粉：保留色相，压到马克笔那种偏亮、偏纯的亮度
                        return HslToColor(h, Math.Min(1.0, Math.Max(0.62, s2)), 0.62);
                    }
                    {
                        // 深色→纸白卡，浅色→暖墨字；中间调留给描边和次级文字
                        if (l < 0.16) return HslToColor(38.0, 0.16, 0.955);  // 纸白卡
                        if (l < 0.34) return HslToColor(37.0, 0.13, 0.760);  // 暖灰描边
                        if (l < 0.62) return HslToColor(36.0, 0.10, 0.420);  // 次级字
                        return HslToColor(34.0, 0.09, 0.135);                 // 暖墨主文字
                    }
            }
        }

        public static Color A(int r, int g, int b) { return AMap(r, g, b); }

        // v18.7 纸感皮肤的文字翻墨：原来是白色的字/线，换到纸底上必须变墨色
        public static Color Txt(Color c)
        {
            if (!Paper || c == null) return c;
            return c;   // 纸感方案已改为"纸底 + 墨块"，文字不再翻墨
        }

        public static Color AA(int a, int r, int g, int b)        {
            Color c = AMap(r, g, b);
            // 纸感皮肤：原来是"半透明白字"，反转成墨色后在纸底上会偏淡，这里把 alpha 提上来保住可读性
            if (SkinIdx == 4 && r >= 225 && g >= 225 && b >= 225 && a > 0 && a < 255)
                a = Math.Min(255, (int)(a * 1.55) + 12);
            return Color.FromArgb(a < 0 ? 0 : (a > 255 ? 255 : a), c.R, c.G, c.B);
        }

        static Color AMap(int r, int g, int b)
        {
            r = r < 0 ? 0 : (r > 255 ? 255 : r);
            g = g < 0 ? 0 : (g > 255 ? 255 : g);
            b = b < 0 ? 0 : (b > 255 ? 255 : b);
            int key = (r << 16) | (g << 8) | b;
            Color hit;
            if (acCache.TryGetValue(key, out hit)) return hit;
            double h, sa, l;
            RgbToHsl(r, g, b, out h, out sa, out l);
            Color res = Color.FromArgb(255, r, g, b);
            // 只动"强调色"：蓝青系 + 够饱和 + 够亮
            if (sa >= 0.35 && l >= 0.33 && h >= 180.0 && h <= 270.0)
                res = HslToColor(ThemeHue, sa, l);
            if (SkinIdx != 3) res = SkinTone(res.R, res.G, res.B);
            if (acCache.Count > 6000) acCache.Clear();
            acCache[key] = res;
            return res;
        }

        static void RgbToHsl(int r, int g, int b, out double h, out double s, out double l)
        {
            double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
            double mx = Math.Max(rr, Math.Max(gg, bb)), mn = Math.Min(rr, Math.Min(gg, bb));
            l = (mx + mn) / 2.0;
            if (mx == mn) { h = 0; s = 0; return; }
            double d = mx - mn;
            s = l > 0.5 ? d / (2.0 - mx - mn) : d / (mx + mn);
            if (mx == rr) h = (gg - bb) / d + (gg < bb ? 6.0 : 0.0);
            else if (mx == gg) h = (bb - rr) / d + 2.0;
            else h = (rr - gg) / d + 4.0;
            h = h * 60.0;
            if (h < 0) h += 360.0;
        }

        public static Color HslToColor(double h, double s, double l)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            if (s <= 0) { int gv = (int)Math.Round(l * 255); return Color.FromArgb(255, gv, gv, gv); }
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r1 = 0, g1 = 0, b1 = 0;
            if (h < 60) { r1 = c; g1 = x; }
            else if (h < 120) { r1 = x; g1 = c; }
            else if (h < 180) { g1 = c; b1 = x; }
            else if (h < 240) { g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; b1 = c; }
            else { r1 = c; b1 = x; }
            int R = (int)Math.Round((r1 + m) * 255), G = (int)Math.Round((g1 + m) * 255), B = (int)Math.Round((b1 + m) * 255);
            R = R < 0 ? 0 : (R > 255 ? 255 : R); G = G < 0 ? 0 : (G > 255 ? 255 : G); B = B < 0 ? 0 : (B > 255 ? 255 : B);
            return Color.FromArgb(255, R, G, B);
        }

        // 主题落盘：主题色.txt 存一行名字（蓝/青/紫/薄荷/玫红），也接受序号 0..4
        public static void LoadTheme(string baseDir)
        {
            try
            {
                string f = Path.Combine(baseDir, "主题色.txt");
                if (!File.Exists(f)) return;
                string t = File.ReadAllText(f).Trim();
                SetTheme(t, false);
            }
            catch (Exception dg270_1) { Diag.Swallow("UiKit.cs:270", dg270_1); }
        }
        public static void SetTheme(string name, bool save)
        {
            for (int i = 0; i < THEME_NAMES.Length; i++)
            {
                if (name == THEME_NAMES[i] || name == i.ToString()) { ApplyTheme(i, save); return; }
            }
        }
        public static void ApplyTheme(int idx, bool save)
        {
            if (idx < 0 || idx >= THEME_HUES.Length) idx = 0;
            ThemeHue = THEME_HUES[idx];
            ThemeName = THEME_NAMES[idx];
            acCache.Clear();
            if (save)
            {
                try { File.WriteAllText(Path.Combine(Application.StartupPath, "主题色.txt"), THEME_NAMES[idx], new UTF8Encoding(false)); }
                catch (Exception dg288_1) { Diag.Swallow("UiKit.cs:288", dg288_1); }
            }
        }
        /// <summary>v8.1：给卡片装一条统一的圆角描边（12 圆角 + 顶部一道微弱高光），
        /// 只画边框不填底，卡片原有的底色与子控件一概不动。</summary>
        /// <remarks>v15.1：改成"先铺一层与工作台同样的玻璃渐变，再描边"。这四张卡原来只用平涂 C_BUBBLE，
        /// 和旁边走 DrawGlassCard 的渐变卡并排时明显发平、发暗；填充画在子控件之前，不会盖住任何东西。</remarks>
        public static void CardBorder(Control c, int radius) { CardBorder(c, radius, Color.Empty); }

        // v15.2：第三参 accent 非空时在卡片顶端画一条 3px 主题色渐变细条（左浓右淡），用来标记"这张卡属于哪个功能"
        public static void CardBorder(Control c, int radius, Color accent)
        {
            if (c == null) return;
            c.Paint += delegate (object s1, PaintEventArgs e1)
            {
                try
                {
                    Graphics g = e1.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rc = new Rectangle(0, 0, c.ClientSize.Width - 1, c.ClientSize.Height - 1);
                    if (rc.Width < 12 || rc.Height < 12) return;
                    using (GraphicsPath path = RoundedPath(rc, radius))
                    {
                        g.SetClip(path);
                        if (Paper)
                        {
                            using (LinearGradientBrush pbr = new LinearGradientBrush(rc,
                                Color.FromArgb(255, 252, 243), Color.FromArgb(246, 240, 226), 90f))
                                g.FillRectangle(pbr, rc);
                        }
                        else
                        using (LinearGradientBrush br = new LinearGradientBrush(rc,
                            UiKit.A(57, 66, 82), UiKit.A(34, 40, 52), 90f))
                            g.FillRectangle(br, rc);
                        if (!Paper)
                        {
                        Rectangle hr2 = new Rectangle(rc.X, rc.Y, rc.Width, Math.Max(6, rc.Height / 2));
                        using (LinearGradientBrush hl2 = new LinearGradientBrush(hr2,
                            UiKit.AA(16, 255, 255, 255), UiKit.AA(0, 255, 255, 255), 90f))
                            g.FillRectangle(hl2, hr2);
                        }
                        g.ResetClip();
                        using (Pen bp = new Pen(Paper ? Color.FromArgb(178, 165, 141) : UiKit.AA(64, 190, 220, 255), Paper ? 1.15f : 1f))
                            g.DrawPath(bp, path);
                    }
                    if (accent.A > 0)
                    {
                        using (GraphicsPath ap2 = RoundedPath(rc, radius))
                        {
                            g.SetClip(ap2);
                            Rectangle ar = new Rectangle(rc.X + 1, rc.Y, rc.Width - 2, 3);
                            if (ar.Width > 6)
                            {
                                using (LinearGradientBrush ab = new LinearGradientBrush(ar,
                                    Color.FromArgb(238, accent.R, accent.G, accent.B),
                                    Color.FromArgb(96, accent.R, accent.G, accent.B), 0f))
                                    g.FillRectangle(ab, ar);
                            }
                            g.ResetClip();
                        }
                    }
                }
                catch (Exception dg340_1) { Diag.Swallow("UiKit.cs:340", dg340_1); }
            };
        }

        public static Color Lighten(Color c, int a) { return Color.FromArgb(Math.Min(255, c.R + a), Math.Min(255, c.G + a), Math.Min(255, c.B + a)); }

        // v8.1：WCAG 亮度与对比度，文字可读性统一走这一套口径
        public static double Lum(Color c)
        {
            double rc = c.R / 255.0, gc = c.G / 255.0, bc = c.B / 255.0;
            rc = (rc <= 0.03928) ? rc / 12.92 : Math.Pow((rc + 0.055) / 1.055, 2.4);
            gc = (gc <= 0.03928) ? gc / 12.92 : Math.Pow((gc + 0.055) / 1.055, 2.4);
            bc = (bc <= 0.03928) ? bc / 12.92 : Math.Pow((bc + 0.055) / 1.055, 2.4);
            return 0.2126 * rc + 0.7152 * gc + 0.0722 * bc;
        }
        public static double ContrastRatio(Color a, Color b)
        {
            double l1 = Lum(a), l2 = Lum(b);
            double hi = Math.Max(l1, l2), lo = Math.Min(l1, l2);
            return (hi + 0.05) / (lo + 0.05);
        }
        /// <summary>取最近一个不透明祖先的底色，用来判断“这段字到底画在什么颜色上”。</summary>
        /// <summary>把带 alpha 的前景合到底色上，得到肉眼实际看到的颜色。</summary>
        public static Color Composite(Color fg, Color bg)
        {
            if (fg.A >= 255) return fg;
            double a = fg.A / 255.0;
            return Color.FromArgb(255,
                (int)(fg.R * a + bg.R * (1 - a)),
                (int)(fg.G * a + bg.G * (1 - a)),
                (int)(fg.B * a + bg.B * (1 - a)));
        }
        // v8.2：自绘控件要跟卡片底色完全一致，就不能只拿父容器的 BackColor
        // （卡片是 Paint 里画的渐变，透明子控件只能取到父容器的纯色底）。
        // 这里直接把父容器在该区域“真正画出来的样子”借过来：先铺父容器底色，
        // 再反射调用父容器的 Paint（InvokePaint 在 Control 里是 protected，全信任下可反射）。
        // v17.6：SetStyle 是 protected，这里反射拿一份，
        // 用来把那些"屏幕上仍被系统按主题画"的按钮强制切回自绘。
        static readonly System.Reflection.MethodInfo mSetStyle =
            typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        static readonly System.Reflection.MethodInfo mInvokePaint =
            typeof(Control).GetMethod("InvokePaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // v17.6：PagePanel / BgPanel 这类页面面板把渐变底色画在 OnPaintBackground 里，
        // 而不是 OnPaint。只借 InvokePaint 拿不到底，圆角控件外面就会露出自己的黑 BackColor。
        static readonly System.Reflection.MethodInfo mOnPaintBg =
            typeof(Control).GetMethod("OnPaintBackground", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public static bool PaintParentBack(Control self, Graphics g)
        {
            try
            {
                Control p = self.Parent;
                if (p == null || g == null) return false;
                Color fallback = (p.BackColor != Color.Transparent) ? p.BackColor : UiKit.A(30, 36, 46);
                using (SolidBrush fb = new SolidBrush(fallback))
                    g.FillRectangle(fb, new Rectangle(0, 0, self.Width, self.Height));
                if (mInvokePaint == null && mOnPaintBg == null) return false;
                Rectangle r = new Rectangle(self.Left, self.Top, self.Width, self.Height);
                GraphicsState st = g.Save();
                try
                {
                    g.TranslateTransform(-self.Left, -self.Top);
                    g.SetClip(r, CombineMode.Intersect);
                    using (PaintEventArgs pe = new PaintEventArgs(g, r))
                    {
                        if (mOnPaintBg != null)
                        {
                            try { mOnPaintBg.Invoke(p, new object[] { pe }); }
                            catch (Exception dg410_1) { Diag.Swallow("UiKit.cs:410", dg410_1); }
                        }
                        if (mInvokePaint != null)
                        {
                            // v17.6：页面自己的 Paint 处理器在"小区域重放"时有可能抛异常
                            // （例如引用了还没准备好的数据）。这里必须单独兜住——
                            // 以前异常直接把 Graphics 的裁剪与平移状态留在原地没恢复，
                            // 结果自绘全部落空，按钮就退回成系统白底。
                            try { mInvokePaint.Invoke(p, new object[] { p, pe }); }
                            catch (Exception dg419_1) { Diag.Swallow("UiKit.cs:419", dg419_1); }
                        }
                    }
                }
                catch (Exception dg423_1) { Diag.Swallow("UiKit.cs:423", dg423_1); }
                finally { g.Restore(st); }
                return true;
            }
            catch { return false; }
        }

        // v17.6：程序里还有一批早年写下的原生 Button（白底黑字、系统灰边），
        // 在深色界面里就是一块块"系统色胶布"，跟方案四格格不入。
        // 这里统一接管：扁平化 + 深底浅字 + 1px 描边 + 按高度裁圆角；
        // 原本有彩色的（蓝 / 青绿 / 红）保留色相，只换白字和暗描边，主操作仍然跳得出来。
        // v17.8：方案四 Logo —— 尖顶六边形，深色底 + 青绿描边 + 白色实心闪电
        // v17.9：功能图标（线性白描符号），kind = ACTIONS 下标 0..9
        public static void Glyph(Graphics g, Rectangle rc, int kind)
        {
            float s = Math.Min(rc.Width, rc.Height) / 28f;
            GraphicsState gs = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(rc.X + (rc.Width - 28 * s) / 2f, rc.Y + (rc.Height - 28 * s) / 2f);
            g.ScaleTransform(s, s);
            Color w = Paper ? Color.FromArgb(255, 255, 255) : A(243, 251, 255);
            float pw = Math.Max(1f, 1.95f / s);
            using (Pen p = new Pen(w, pw))
            {
                p.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case 0:   // 工作台：立方体
                        g.DrawPolygon(p, new PointF[] { new PointF(14, 3.5f), new PointF(24.5f, 9), new PointF(14, 14.5f), new PointF(3.5f, 9) });
                        g.DrawLine(p, 3.5f, 9, 3.5f, 19.5f);
                        g.DrawLine(p, 24.5f, 9, 24.5f, 19.5f);
                        g.DrawLine(p, 3.5f, 19.5f, 14, 25);
                        g.DrawLine(p, 14, 25, 24.5f, 19.5f);
                        g.DrawLine(p, 14, 14.5f, 14, 25);
                        break;
                    case 1:   // 关机：电源
                        g.DrawArc(p, 5, 8, 18, 18, -62, 304);
                        g.DrawLine(p, 14, 3.5f, 14, 11.5f);
                        break;
                    case 2:   // C 盘清理：磁盘
                        g.DrawEllipse(p, 3, 4.5f, 22, 6);
                        g.DrawLine(p, 3, 7.5f, 3, 20);
                        g.DrawLine(p, 25, 7.5f, 25, 20);
                        g.DrawArc(p, 3, 17, 22, 6, 0, 180);
                        break;
                    case 3:   // 去水印：图片
                        using (GraphicsPath rp = RoundedPath(new Rectangle(3, 5, 22, 18), 3))
                            g.DrawPath(p, rp);
                        g.DrawLine(p, 5.5f, 20.5f, 10.5f, 13);
                        g.DrawLine(p, 10.5f, 13, 15.5f, 18.5f);
                        g.DrawLine(p, 15.5f, 18.5f, 19.5f, 14);
                        g.DrawLine(p, 19.5f, 14, 22.5f, 20.5f);
                        g.DrawEllipse(p, 7.5f, 8, 3.2f, 3.2f);
                        break;
                    case 4:   // Steam 价值：价签
                        g.DrawPolygon(p, new PointF[] { new PointF(10.5f, 3.5f), new PointF(24.5f, 10), new PointF(14, 24.5f), new PointF(3.5f, 10) });
                        g.DrawEllipse(p, 11, 9, 5, 5);
                        break;
                    case 5:   // 电脑配置：显示器
                        using (GraphicsPath rp = RoundedPath(new Rectangle(3, 5, 22, 14), 3))
                            g.DrawPath(p, rp);
                        g.DrawLine(p, 14, 19, 14, 23.5f);
                        g.DrawLine(p, 9.5f, 23.5f, 18.5f, 23.5f);
                        break;
                    case 6:   // 视频下载：播放
                        g.DrawEllipse(p, 4, 4, 20, 20);
                        g.DrawPolygon(p, new PointF[] { new PointF(11.5f, 9.5f), new PointF(11.5f, 18.5f), new PointF(19, 14) });
                        break;
                    case 7:   // 音乐下载：音符
                        g.DrawEllipse(p, 4.5f, 18.5f, 7.5f, 5.5f);
                        g.DrawLine(p, 12, 21.5f, 12, 5.5f);
                        g.DrawLine(p, 12, 5.5f, 20.5f, 3.5f);
                        g.DrawLine(p, 20.5f, 3.5f, 20.5f, 8.5f);
                        break;
                    case 8:   // Steam 成就：奖杯
                        g.DrawLine(p, 8.5f, 4, 19.5f, 4);
                        g.DrawLine(p, 8.5f, 4, 10, 13.5f);
                        g.DrawLine(p, 10, 13.5f, 14, 16.5f);
                        g.DrawLine(p, 14, 16.5f, 18, 13.5f);
                        g.DrawLine(p, 18, 13.5f, 19.5f, 4);
                        g.DrawArc(p, 3, 5, 6.5f, 8, 90, 180);
                        g.DrawArc(p, 18.5f, 5, 6.5f, 8, 270, 180);
                        g.DrawLine(p, 14, 16.5f, 14, 21);
                        g.DrawLine(p, 9, 24, 19, 24);
                        g.DrawLine(p, 11.5f, 21, 16.5f, 21);
                        break;
                    default:  // 一键重装：循环
                        g.DrawArc(p, 4.5f, 4.5f, 19, 19, 38, 292);
                        g.DrawPolygon(p, new PointF[] { new PointF(24.5f, 7.5f), new PointF(19.5f, 5.5f), new PointF(20.5f, 11) });
                        break;
                }
            }
            g.Restore(gs);
        }

        public static PointF[] HexPoints(Rectangle rc)
        {
            float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f;
            float rad = Math.Min(rc.Width, rc.Height) / 2f - 1.5f;
            PointF[] p = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 180.0 * (-90 + 60 * i);
                p[i] = new PointF((float)(cx + rad * Math.Cos(a)), (float)(cy + rad * Math.Sin(a)));
            }
            return p;
        }

        public static void DrawLogo(Graphics g, Rectangle rc)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 外圈柔光
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddPolygon(HexPoints(rc));
                using (Pen gp = new Pen(AA(40, 66, 228, 198), 4f))
                    g.DrawPath(gp, hp);
            }
            // 六边形本体
            using (GraphicsPath hp = new GraphicsPath())
            {
                hp.AddPolygon(HexPoints(rc));
                using (LinearGradientBrush b = new LinearGradientBrush(rc, A(18, 36, 46), A(8, 15, 22), 90f))
                    g.FillPath(b, hp);
                using (Pen sp = new Pen(A(64, 226, 198), 2.2f))
                    g.DrawPath(sp, hp);
            }
            // 白色闪电
            float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f;
            float k = Math.Min(rc.Width, rc.Height) / 40f;
            float[,] pts = { { 1.6f, -13.5f }, { -8.1f, 0.8f }, { -2.2f, 0.8f }, { -4.1f, 12.2f },
                             { 7.6f, -2.7f }, { 1.1f, -2.7f }, { 3.1f, -13.5f } };
            PointF[] bp = new PointF[7];
            for (int i = 0; i < 7; i++) bp[i] = new PointF(cx + pts[i, 0] * k, cy + pts[i, 1] * k);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(bp);
                using (SolidBrush wb = new SolidBrush(Color.White))
                    g.FillPath(wb, gp);
            }
        }

        public static void SkinButtons(Control root)
        {
            if (root == null) return;
            for (int i = 0; i < root.Controls.Count; i++)
            {
                Control c = root.Controls[i];
                if (c == null) continue;
                RoundBtn rb0 = c as RoundBtn;
                if (rb0 != null)
                {
                    // v17.6：RoundBtn 也钉死成"系统只能按我们的色画"的状态。
                    // 关键在 UseVisualStyleBackColor —— 它一旦是 true，系统就按主题白底再画一遍，
                    // 自绘结果直接被盖掉，看上去就是一颗白按钮。
                    try
                    {
                        rb0.UseVisualStyleBackColor = false;
                        rb0.FlatStyle = FlatStyle.Flat;
                        rb0.FlatAppearance.BorderSize = 0;
                        rb0.BackColor = rb0.Bg;
                        rb0.ForeColor = rb0.Fg;
                        try
                        {
                            mSetStyle.Invoke(rb0, new object[] { ControlStyles.UserPaint, true });
                            mSetStyle.Invoke(rb0, new object[] { ControlStyles.AllPaintingInWmPaint, true });
                            mSetStyle.Invoke(rb0, new object[] { ControlStyles.OptimizedDoubleBuffer, true });
                        }
                        catch (Exception dg591_1) { Diag.Swallow("UiKit.cs:591", dg591_1); }
                        // v17.6：切页后这些按钮是"刚被显示出来"，但一直没收到一次 WM_PAINT，
                        // 屏幕上就停在系统给的白底。这里强制立刻重画一遍。
                        rb0.Invalidate();
                        rb0.Update();
                        // v17.6：这里不再给 RoundBtn 设 Region —— 设了以后系统会按 Region 之外
                        // 的区域重画一次主题背景，自绘结果被盖掉，按钮就成了一颗白按钮。
                        // 圆角由 OnPaint 自己画（四角铺的是页面底色），不需要系统裁剪。
                    }
                    catch (Exception dg600_1) { Diag.Swallow("UiKit.cs:600", dg600_1); }
                }
                Button b = c as Button;
                if (b != null && !(b is RoundBtn))
                {
                    try
                    {
                        Color bg = b.BackColor;
                        bool transparent = false;
                        try { transparent = (bg == Color.Transparent); } catch (Exception dg609_1) { Diag.Swallow("UiKit.cs:609", dg609_1); }
                        if (transparent) bg = A(26, 36, 48);
                        int mx = Math.Max(bg.R, Math.Max(bg.G, bg.B));
                        int mn = Math.Min(bg.R, Math.Min(bg.G, bg.B));
                        bool nearWhite = transparent || (mx >= 168 && (mx - mn) <= 30);
                        // v17.6：必须是 Flat —— Standard 会走系统视觉样式，BackColor 直接被忽略，
                        // 屏幕上就是一块白底；Flat 才会老老实实按 BackColor + BorderColor 画。
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderSize = 1;
                        b.UseVisualStyleBackColor = false;
                        if (nearWhite)
                        {
                            b.BackColor = A(26, 36, 48);
                            b.ForeColor = A(226, 232, 240);
                            b.FlatAppearance.BorderColor = A(58, 70, 88);
                            b.FlatAppearance.MouseOverBackColor = A(36, 48, 64);
                            b.FlatAppearance.MouseDownBackColor = A(22, 31, 43);
                        }
                        else
                        {
                            b.ForeColor = Color.White;
                            b.FlatAppearance.BorderColor = Darken(bg, 26);
                            b.FlatAppearance.MouseOverBackColor = Lighten(bg, 18);
                            b.FlatAppearance.MouseDownBackColor = Darken(bg, 14);
                        }
                        int rr = Math.Max(4, Math.Min(10, Math.Min(b.Height, 22) / 2));
                        using (GraphicsPath gp = RoundedPath(new Rectangle(0, 0, b.Width, b.Height), rr))
                            b.Region = new Region(gp);
                    }
                    catch (Exception dg638_1) { Diag.Swallow("UiKit.cs:638", dg638_1); }
                }
                SkinButtons(c);   // 递归到卡片、面板里的按钮
            }
        }

        public static Color BackBehind(Control c)
        {
            Control p = c;
            for (int i = 0; i < 24 && p != null; i++)
            {
                if (p.BackColor.A >= 200) return p.BackColor;
                p = p.Parent;
            }
            return UiKit.A(24, 28, 34);
        }
        public static Color Darken(Color c, int a) { return Color.FromArgb(Math.Max(0, c.R - a), Math.Max(0, c.G - a), Math.Max(0, c.B - a)); }

    }
}