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
    static class Ui2
    {
        public const int R_CARD = 12;
        public const int R_CTRL = 9;
        public const int GAP = 8;

        // v8.8：实测结论——本程序里交给系统的控件（TextBox / Panel / DateTimePicker / RichTextBox）
        // 底色会被整体提亮，且是**逐通道加值**：R+34、G+28、B+25（设 38,45,58 渲染成 72,73,83；
        // 设 4,11,24 渲染成 38,39,49，两次实验完全一致）。所以"想让控件看起来是 c"，
        // 就必须先给 c 减掉这份加值；反过来要还原某个控件当前肉眼的颜色，就再加回去。
        public static Color CtlInk(Color c)
        {
            return Color.FromArgb(Math.Max(0, c.R - 34), Math.Max(0, c.G - 28), Math.Max(0, c.B - 25));
        }

        public static Color UnCtlInk(Color c)
        {
            return Color.FromArgb(Math.Min(255, c.R + 34), Math.Min(255, c.G + 28), Math.Min(255, c.B + 25));
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr h, int msg, int wParam, int lParam);

        // 占位提示是贴在输入框上的一层透明 Label。只要有人把输入框 BringToFront 一次，
        // 这层提示就被压到下面、整块看不见了（音乐页搜索框就是这样变空白的）。
        // 谁调过输入框的前后顺序，就顺手调用一次这里把提示重新提上来。
        public static void FrontPlaceholders(Control parent)
        {
            try
            {
                if (parent == null) return;
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c = parent.Controls[i];
                    if (c is Label && "placeholder".Equals(c.Tag)) c.BringToFront();
                }
            }
            catch (Exception dg60_1) { Diag.Swallow("Ui2.cs:60", dg60_1); }
        }

        // 输入框左右内边距：原生 TextBox 的字符是贴着边框起笔的，深色圆角框里显得挤。
        // EM_SETMARGINS(0xD3) + EC_LEFTMARGIN|EC_RIGHTMARGIN(0x3)，lParam 高 16 位是右边距。
        public static void PadTextBox(TextBoxBase tb, int lr)
        {
            try
            {
                if (tb == null || tb.IsDisposed) return;
                if (!tb.IsHandleCreated) { tb.HandleCreated += delegate { PadTextBox(tb, lr); }; return; }
                SendMessage(tb.Handle, 0xD3, 0x3, (lr << 16) | lr);
            }
            catch (Exception dg73_1) { Diag.Swallow("Ui2.cs:73", dg73_1); }
        }

        public static readonly Color BG = UiKit.A(24, 28, 34);
        public static readonly Color SURF = UiKit.A(30, 35, 43);
        public static readonly Color SURF2 = UiKit.A(26, 30, 38);
        public static readonly Color LINE = UiKit.A(48, 56, 68);
        public static readonly Color TEXT = UiKit.A(232, 236, 242);
        public static readonly Color SUB = UiKit.A(146, 156, 176);
        public static readonly Color ACCENT = UiKit.A(64, 132, 255);
        public static readonly Color GOOD = UiKit.A(52, 199, 110);
        public static readonly Color WARN = UiKit.A(240, 180, 70);
        public static readonly Color BAD = UiKit.A(238, 96, 96);

        static readonly string FAM = PickFamily();
        public static string FamilyName { get { return FAM; } }

        // 字体族只认这三档：优先「微软雅黑 UI」，不到位再退雅黑 / Segoe UI
        static string PickFamily()
        {
            string[] names = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    using (Font f = new Font(names[i], 9F))
                        if (string.Equals(f.Name, names[i], StringComparison.OrdinalIgnoreCase)) return names[i];
                }
                catch (Exception dg101_1) { Diag.Swallow("Ui2.cs:101", dg101_1); }
            }
            return "Segoe UI";
        }

        // 图标字体 / 等宽字体不动：它们靠字形宽度取尺寸，改了会错位
        public static bool IsIconFont(Font f)
        {
            if (f == null) return false;
            string n = f.Name;
            return n.IndexOf("MDL2", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Symbol", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Marlett", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Webdings", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Wingdings", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static bool IsFamilyOk(string name)
        {
            if (name == null) return false;
            return name.IndexOf("Microsoft YaHei", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("微软雅黑", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Segoe UI", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static bool IsIconText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] >= '\uE000' && s[i] <= '\uF8FF') return true;
            return false;
        }

        static readonly Dictionary<Form, bool> skinned = new Dictionary<Form, bool>();
        static readonly Dictionary<Form, DateTime> repolish = new Dictionary<Form, DateTime>();
        static readonly Dictionary<Control, List<Label>> titles = new Dictionary<Control, List<Label>>();
        static readonly HashSet<Control> wired = new HashSet<Control>();

        /// <summary>在 Program.Main 里挂一次：任何新窗体第一次出现时自动套皮肤（新加的功能窗口不必逐个改）。</summary>
        public static void StartWatch()
        {
            try { Application.Idle += new EventHandler(OnIdle); } catch (Exception dg140_1) { Diag.Swallow("Ui2.cs:140", dg140_1); }
        }
        static void OnIdle(object s, EventArgs e) { Sweep(); }
        static void Sweep()
        {
            try
            {
                FormCollection fc = Application.OpenForms;
                for (int i = 0; i < fc.Count; i++)
                {
                    Form f = fc[i];
                    if (f == null || f.IsDisposed) continue;
                    if (!f.IsHandleCreated) continue;
                    if (!skinned.ContainsKey(f))
                    {
                        Apply(f);
                        skinned[f] = true;
                        repolish[f] = DateTime.Now.AddMilliseconds(900);
                        continue;
                    }
                    // 第二遍：控件在启动过程中还会陆续生成，隔 900ms 再补一次皮肤，尺寸/字号一并校正
                    if (repolish.ContainsKey(f) && DateTime.Now >= repolish[f])
                    {
                        repolish.Remove(f);
                        Apply(f);
                        TryReflow(f);
                    }
                }
            }
            catch (Exception dg169_1) { Diag.Swallow("Ui2.cs:169", dg169_1); }
        }

        public static void Apply(Form f)
        {
            if (f == null) return;
            try
            {
                Font cur = f.Font;
                if (cur != null && !IsIconFont(cur) && !IsFamilyOk(cur.Name)) f.Font = new Font(FAM, cur.Size, cur.Style);
            }
            catch (Exception dg180_1) { Diag.Swallow("Ui2.cs:180", dg180_1); }
            try
            {
                ToolTip tip = new ToolTip();
                tip.AutoPopDelay = 15000;
                tip.InitialDelay = 350;
                tip.ReshowDelay = 150;
                tip.ShowAlways = true;
                Walk(f, tip, 0);
                try { UiType.Polish(f); } catch (Exception dg189_1) { Diag.Swallow("Ui2.cs:189", dg189_1); }
            }
            catch (Exception dg191_1) { Diag.Swallow("Ui2.cs:191", dg191_1); }
        }

        // ===== v13.1 界面缩放：100% / 110% / 125%，落盘 界面缩放.txt，重启生效 =====
        // 只做「等比放大」：控件位置/尺寸、字号（对齐 UiType.Steps 档位，避免自检 font_off_step 报警）、
        // ListView 列宽、窗口客户区一起放大；Dock 控件交给布局自己撑开，AutoSize 标签交给文字自己长大。
        public static float UiScale = 1.0f;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, object> scaledOnce =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Form, object>();

        public static void LoadUiScale()
        {
            try
            {
                string p = Path.Combine(Application.StartupPath, "界面缩放.txt");
                if (!File.Exists(p)) return;
                string t = File.ReadAllText(p).Trim().Replace("%", "").Trim();
                float v;
                if (float.TryParse(t, out v) && v >= 95) UiScale = (v > 115 ? 115f : v) / 100f;   // v14.5 实测：125% 在三卡与磁贴上会溢出，维持 115 上限
            }
            catch (Exception dg211_1) { Diag.Swallow("Ui2.cs:211", dg211_1); }
        }

        public static void SaveUiScale(int percent)
        {
            try { File.WriteAllText(Path.Combine(Application.StartupPath, "界面缩放.txt"), percent.ToString(), new UTF8Encoding(false)); } catch (Exception dg216_1) { Diag.Swallow("Ui2.cs:216", dg216_1); }
        }

        static float StepOf(float want)
        {
            float[] st = UiType.Steps;
            for (int i = 0; i < st.Length; i++) if (st[i] >= want - 0.05F) return st[i];
            return st[st.Length - 1];
        }

        static void ScaleKids(Control parent, float k)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                try
                {
                    if (c.Dock == DockStyle.None)
                    {
                        c.Location = new Point((int)Math.Round(c.Left * k), (int)Math.Round(c.Top * k));
                        if (!c.AutoSize && c.Width > 0 && c.Height > 0)
                            c.Size = new Size((int)Math.Round(c.Width * k), (int)Math.Round(c.Height * k));
                    }
                    else
                    {
                        // 停靠的容器只改"厚度"：Left/Right 调宽、Top/Bottom 调高，撑开的部分交给布局
                        if ((c.Dock == DockStyle.Left || c.Dock == DockStyle.Right) && c.Width > 0)
                            c.Width = (int)Math.Round(c.Width * k);
                        else if ((c.Dock == DockStyle.Top || c.Dock == DockStyle.Bottom) && c.Height > 0)
                            c.Height = (int)Math.Round(c.Height * k);
                    }
                    Font f0 = c.Font;
                    string tn = c.GetType().Name;
                    bool rowItem = (c is RadioButton || c is CheckBox || tn == "RoundRadio" || tn == "RoundCheck");
                    if (!rowItem && f0 != null && f0.Size > 5F && f0.Size < 40F
                        && (f0.Name == null || f0.Name.IndexOf("MDL2", StringComparison.OrdinalIgnoreCase) < 0))
                        c.Font = new Font(f0.FontFamily, StepOf(f0.Size * k), f0.Style);
                    // 放大字号后，固定宽度的单选/复选/按钮/单行标签要按文字实宽补宽，
                    // 否则会出现「指定...」这种省略号——这正是用户明确不要的"残缺文案"
                    string tx = c.Text;
                    if (!c.AutoSize && tx != null && tx.Length > 0 && tx.IndexOf('\n') < 0
                        && (c is Button
                            || (c is Label && tx.Length <= 40 && c.Height <= (int)(c.Font.Height * 2.2f))))
                    {
                        int pad = (c is RadioButton || c is CheckBox) ? 20 : 12;
                        int need = TextRenderer.MeasureText(tx, c.Font).Width + pad;
                        if (need > c.Width) c.Width = need;
                    }
                    ListView lv = c as ListView;
                    if (lv != null && lv.Columns.Count > 0)
                    {
                        for (int ci = 0; ci < lv.Columns.Count; ci++)
                            lv.Columns[ci].Width = (int)Math.Round(lv.Columns[ci].Width * k);
                    }
                    ScaleKids(c, k);
                }
                catch (Exception dg272_1) { Diag.Swallow("Ui2.cs:272", dg272_1); }
            }
        }

        public static void ScaleAll(Form f)
        {
            if (f == null || f.IsDisposed) return;
            float k = UiScale;
            if (k <= 1.01F || k > 1.5F) return;
            object seen;
            if (scaledOnce.TryGetValue(f, out seen)) return;   // 每个窗体只放大一次
            scaledOnce.Add(f, (object)true);
            try
            {
                if (f.MinimumSize.Width > 0 || f.MinimumSize.Height > 0)
                    f.MinimumSize = new Size((int)Math.Round(f.MinimumSize.Width * k), (int)Math.Round(f.MinimumSize.Height * k));
                f.ClientSize = new Size((int)Math.Round(f.ClientSize.Width * k), (int)Math.Round(f.ClientSize.Height * k));
                ScaleKids(f, k);
            }
            catch (Exception dg291_1) { Diag.Swallow("Ui2.cs:291", dg291_1); }
        }

        static void DumpPaperStrips(Control p, int depth, System.Text.StringBuilder sb)
        {
            if (p == null || depth > 24) return;
            for (int i = 0; i < p.Controls.Count; i++)
            {
                Control c = p.Controls[i];
                Color bc = c.BackColor;
                if (c is NumSpin)
                {
                    sb.Append("NUMSPIN vis=").Append(c.Visible).Append(" bc=").Append(c.BackColor).Append(" bounds=").Append(c.Bounds);
                    if (c.Controls.Count > 0) sb.Append(" editBc=").Append(c.Controls[0].BackColor).Append(" editFg=").Append(c.Controls[0].ForeColor);
                    sb.AppendLine();
                }
                if (c is Label && c.Text != null && (c.Text.IndexOf("选择操作与计时方式") >= 0 || c.Text == "当前任务" || c.Text == "快捷预设" || c.Text == "进阶设置"))
                    sb.Append("FINAL ").Append(c.Text).Append(" vis=").Append(c.Visible).Append(" fg=").Append(c.ForeColor).Append(" bc=").Append(bc).Append(" bounds=").Append(c.Bounds).AppendLine();
                if (c.Width > 380 && c.Height >= 12 && c.Height <= 72 && bc.A >= 200 && UiKit.Lum(bc) > 0.80)
                {
                    sb.Append(depth).Append(" ").Append(c.GetType().Name)
                      .Append(" bounds=").Append(c.Bounds.ToString())
                      .Append(" bc=").Append(bc.ToString())
                      .Append(" text=").Append((c.Text ?? "").Replace("\r", " ").Replace("\n", " "))
                      .Append(" parent=").Append(c.Parent == null ? "-" : c.Parent.GetType().Name + c.Parent.Bounds.ToString())
                      .AppendLine();
                }
                DumpPaperStrips(c, depth + 1, sb);
            }
        }

        public static void Polish(Form f)
        {
            try
            {
                if (f == null || f.IsDisposed) return;
                Apply(f);
                try { ChromePage(f); } catch (Exception dg300_1) { Diag.Swallow("Ui2.cs:300", dg300_1); }
                TryReflow(f);
                try { UiType.Polish(f); } catch (Exception dg302_1) { Diag.Swallow("Ui2.cs:302", dg302_1); }
                ScaleAll(f);
            }
            catch (Exception dg305_1) { Diag.Swallow("Ui2.cs:305", dg305_1); }
        }

        // ============ v14.0 页眉装饰：顶部主题渐变线 + 标题左侧胶囊竖条 ============
        static readonly System.Collections.Generic.Dictionary<Control, bool> chromeDone =
            new System.Collections.Generic.Dictionary<Control, bool>();
        static readonly System.Collections.Generic.Dictionary<Control, bool> chromePaint =
            new System.Collections.Generic.Dictionary<Control, bool>();
        static readonly System.Collections.Generic.Dictionary<Control, bool> cardDone =
            new System.Collections.Generic.Dictionary<Control, bool>();
        static readonly System.Collections.Generic.Dictionary<Control, bool> chromeWatch =
            new System.Collections.Generic.Dictionary<Control, bool>();

        public static void ChromePage(Control parent)
        {
            try
            {
                if (parent == null) return;
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c = parent.Controls[i];
                    // 页面容器有两种：定时页是 PagePanel，别的页是普通大 Panel（背景色 C_MAIN）
                    bool isPage = (c is PagePanel) || (c is Panel && c.Width > 500 && c.Height > 300 && c.Controls.Count >= 2);
                    if (isPage && !chromeWatch.ContainsKey(c))
                    {
                        // 页面启动时是隐藏的，标题 Label 也就不可见、找不到；等它第一次显示时再补一次
                        chromeWatch[c] = true;
                        Control w = c;
                        c.VisibleChanged += delegate
                        {
                            try
                            {
                                if (!w.Visible) return;
                                chromeDone.Remove(w);
                                Control par = w.Parent;
                                ChromePage(par == null ? w : par);
                            }
                            catch (Exception dg342_1) { Diag.Swallow("Ui2.cs:342", dg342_1); }
                        };
                    }
                    if (isPage && !chromeDone.ContainsKey(c))
                    {
                        chromeDone[c] = true;
                        Control pp = c;
                        // 页标题可能在页面顶部的 topbar 里，要递归才找得到
                        Label ttl = FindTitleDeep(pp, 0);
                        Control host = (ttl == null) ? null : ttl.Parent;
                        if (host != null && !chromePaint.ContainsKey(host))
                        {
                            chromePaint[host] = true;
                            Control h2 = host;
                            Label t2 = ttl;
                            host.Paint += delegate(object s2, PaintEventArgs e2) { DrawTopChrome(h2, t2, e2.Graphics); };
                            host.Resize += delegate { try { h2.Invalidate(); } catch (Exception dg358_1) { Diag.Swallow("Ui2.cs:358", dg358_1); } };
                            // 九个页面共用同一个标题 Label，切页只改它的文字和颜色：
                            // 文字一变就让标题所在容器重绘，页标签才会跟着换
                            for (int k = 0; k < host.Controls.Count; k++)
                            {
                                Control hc = host.Controls[k];
                                if (hc is Label) hc.TextChanged += delegate { try { h2.Invalidate(); } catch (Exception dg364_1) { Diag.Swallow("Ui2.cs:364", dg364_1); } };
                            }
                        }
                        DecorateCards(pp);
                    }
                    if (c.Controls.Count > 0) ChromePage(c);
                }
            }
            catch (Exception dg372_1) { Diag.Swallow("Ui2.cs:372", dg372_1); }
        }

        static Label FindTitleDeep(Control parent, int depth)
        {
            try
            {
                if (parent == null || depth > 6) return null;
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c = parent.Controls[i];
                    Label l = c as Label;
                    if (l != null && l.Visible && l.Font != null && l.Font.Bold && l.Font.Size >= 10.5F
                        && l.Left < 80 && l.Top < 72 && l.Text != null
                        && l.Text.Trim().Length > 0 && l.Text.Length <= 14)
                        return l;
                    Label sub = FindTitleDeep(c, depth + 1);
                    if (sub != null) return sub;
                }
            }
            catch (Exception dg392_1) { Diag.Swallow("Ui2.cs:392", dg392_1); }
            return null;
        }

        static readonly Font ChTagFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);

        static string PageTagOf(string title)
        {
            string t = title == null ? "" : title;
            if (t.IndexOf("工作台", StringComparison.Ordinal) >= 0 || t.IndexOf("dashboard", StringComparison.OrdinalIgnoreCase) >= 0) return "00 / DASHBOARD";
            if (t.IndexOf("关机", StringComparison.Ordinal) >= 0 || t.IndexOf("shutdown", StringComparison.OrdinalIgnoreCase) >= 0) return "01 / SHUTDOWN";
            if (t.IndexOf("清理", StringComparison.Ordinal) >= 0 || t.IndexOf("clean", StringComparison.OrdinalIgnoreCase) >= 0) return "02 / CLEAN";
            if (t.IndexOf("成就", StringComparison.Ordinal) >= 0 || t.IndexOf("achiev", StringComparison.OrdinalIgnoreCase) >= 0) return "03 / STEAM";
            if (t.IndexOf("视频", StringComparison.Ordinal) >= 0 || t.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0) return "04 / VIDEO";
            if (t.IndexOf("音乐", StringComparison.Ordinal) >= 0 || t.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0) return "05 / MUSIC";
            if (t.IndexOf("水印", StringComparison.Ordinal) >= 0 || t.IndexOf("watermark", StringComparison.OrdinalIgnoreCase) >= 0) return "06 / WATERMARK";
            if (t.IndexOf("价值", StringComparison.Ordinal) >= 0 || t.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0) return "07 / VALUE";
            if (t.IndexOf("配置", StringComparison.Ordinal) >= 0 || t.IndexOf("system", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("hardware", StringComparison.OrdinalIgnoreCase) >= 0) return "08 / SYSTEM";
            return "";
        }

        // v14.1 卡片标题小圆点：卡片里第一个 8pt 左右、贴着左上角的 Label 认定为标题，
        // 在它左侧 9px 画一枚 5px 主题色圆点；不挪任何控件坐标，只在 Paint 里补装饰。
        static Label FindCardHead(Panel card)
        {
            try
            {
                for (int j = 0; j < card.Controls.Count; j++)
                {
                    Label l = card.Controls[j] as Label;
                    if (l == null || l.Font == null) continue;
                    if (l.Font.Size > 8.6F || l.Left < 8 || l.Left > 30 || l.Top < 2 || l.Top > 18) continue;
                    if (l.Text == null || l.Text.Trim().Length == 0 || l.Text.Length > 12) continue;
                    return l;
                }
            }
            catch (Exception dg428_1) { Diag.Swallow("Ui2.cs:428", dg428_1); }
            return null;
        }

        static void DecorateCards(Control pp)
        {
            try
            {
                for (int i = 0; i < pp.Controls.Count; i++)
                {
                    Panel card = pp.Controls[i] as Panel;
                    if (card == null || card is PagePanel) continue;
                    if (cardDone.ContainsKey(card)) continue;
                    cardDone[card] = true;
                    Panel cd = card;
                    card.Paint += delegate(object s3, PaintEventArgs e3)
                    {
                        try
                        {
                            // 卡片在挂载时可能还没布局（隐藏页），所以尺寸与标题都留到 Paint 时才判断
                            if (cd.Width < 240 || cd.Height < 90) return;
                            Label hl = FindCardHead(cd);
                            if (hl == null) return;
                            int dx = hl.Left - 9;
                            if (dx < 4) return;
                            e3.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                            Rectangle dr = new Rectangle(dx, hl.Top + Math.Max(0, (hl.Height - 5) / 2), 5, 5);
                            using (GraphicsPath dp = UiKit.RoundedPath(dr, 2))
                            using (SolidBrush db = new SolidBrush(Color.FromArgb(180, UiKit.ThemeColor)))
                                e3.Graphics.FillPath(db, dp);
                        }
                        catch (Exception dg459_1) { Diag.Swallow("Ui2.cs:459", dg459_1); }
                    };
                    try { card.Invalidate(); } catch (Exception dg461_1) { Diag.Swallow("Ui2.cs:461", dg461_1); }
                }
            }
            catch (Exception dg464_1) { Diag.Swallow("Ui2.cs:464", dg464_1); }
        }

        static Label FindPageTitle(PagePanel pp)
        {
            try
            {
                for (int i = 0; i < pp.Controls.Count; i++)
                {
                    Label l = pp.Controls[i] as Label;
                    if (l == null || l.IsDisposed) continue;
                    if (l.Font != null && l.Font.Bold && l.Font.Size >= 14F && l.Text != null && l.Text.Trim().Length > 0)
                        return l;
                }
            }
            catch (Exception dg479_1) { Diag.Swallow("Ui2.cs:479", dg479_1); }
            return null;
        }

        // 页面顶栏的排版语言：标题与右侧「序号 / 英文短名」之间用一小段细线连起来
        // 每页一个专属强调色，和左侧导航图标同色系，页标签与分区圆点都用它
        static Color PageAccentOf(string title)
        {
            string tag = PageTagOf(title);
            if (tag.Length == 0) return Color.Empty;
            switch (tag.Substring(0, 2))
            {
                case "00": return Color.FromArgb(129, 140, 248);
                case "01": return Color.FromArgb(248, 113, 113);
                case "02": return Color.FromArgb(56, 189, 248);
                case "03": return Color.FromArgb(244, 114, 182);
                case "04": return Color.FromArgb(52, 211, 153);
                case "05": return Color.FromArgb(167, 139, 250);
                case "06": return Color.FromArgb(251, 191, 36);
                case "07": return Color.FromArgb(96, 165, 250);
                case "08": return Color.FromArgb(245, 158, 11);
                case "09": return Color.FromArgb(34, 211, 238);
            }
            return Color.Empty;
        }

        static void DrawTopChrome(Control host, Label ttl, Graphics g)
        {
            try
            {
                if (host == null || ttl == null) return;
                int w = host.ClientSize.Width;
                if (w < 200) return;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // 跟随这一页标题自己的强调色（每页颜色不同），标题是纯白时退回全局主题色
                Color accent = ttl.ForeColor;
                if (accent.R > 232 && accent.G > 232 && accent.B > 232) accent = UiKit.ThemeColor;
                Color own = PageAccentOf(ttl.Text);
                if (own != Color.Empty) accent = own;
                // 扁平页（没有卡片的页）里，各个分区的 10pt 小标题前也补一枚同样的圆点
                for (int i = 0; i < host.Controls.Count; i++)
                {
                    Label l = host.Controls[i] as Label;
                    if (l == null || l.Font == null || !l.Font.Bold) continue;
                    if (l.Font.Size < 9.4F || l.Font.Size > 10.6F) continue;
                    if (l.Left < 28 || l.Left > 60) continue;
                    if (l.Top < ttl.Top + ttl.Height + 8) continue;
                    if (l.Text == null || l.Text.Trim().Length == 0 || l.Text.Length > 12) continue;
                    Rectangle dr2 = new Rectangle(l.Left - 9, l.Top + Math.Max(0, (l.Height - 5) / 2), 5, 5);
                    using (GraphicsPath dp2 = UiKit.RoundedPath(dr2, 2))
                    using (SolidBrush db2 = new SolidBrush(Color.FromArgb(180, accent)))
                        g.FillPath(db2, dp2);
                }
                string tag = PageTagOf(ttl.Text);
                if (tag.Length == 0) return;
                SizeF ts = g.MeasureString(tag, ChTagFont);
                int tx = ttl.Right + 14;
                int capH = (int)Math.Round(ts.Height) + 4;
                int padX = 8;
                int midY = ttl.Top + ttl.Height / 2;
                Rectangle cap = new Rectangle(tx, midY - capH / 2, (int)Math.Round(ts.Width) + padX * 2, capH);
                if (cap.Right > w - 16) return;
                using (Pen lp = new Pen(Color.FromArgb(92, accent), 1f))
                    g.DrawLine(lp, ttl.Right + 4, midY, tx - 6, midY);
                // 页标签做成小胶囊：淡底 + 细边 + 亮字，比一行灰字更像"章节号"
                using (GraphicsPath cp = UiKit.RoundedPath(cap, capH / 2))
                {
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(24, accent))) g.FillPath(cb, cp);
                    using (Pen cpen = new Pen(Color.FromArgb(78, accent), 1f)) g.DrawPath(cpen, cp);
                }
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(225, accent)))
                    g.DrawString(tag, ChTagFont, tb, cap.X + padX, cap.Y + (capH - ts.Height) / 2f);
            }
            catch (Exception dg552_1) { Diag.Swallow("Ui2.cs:552", dg552_1); }
        }

        static void TryReflow(Control parent)
        {
            try
            {
                if (parent == null) return;
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c = parent.Controls[i];
                    Font f = c.Font;
                    if (f != null && !IsIconFont(f) && f.Size < 8F && f.Size > 4F)
                    {
                        c.Font = new Font(FAM, 8F, f.Style);
                        if (c is Label) EnsureReadable((Label)c);
                    }
                    if (c.Controls.Count > 0) TryReflow(c);
                }
            }
            catch (Exception dg572_1) { Diag.Swallow("Ui2.cs:572", dg572_1); }
        }

        static void Walk(Control parent, ToolTip tip, int depth)
        {
            if (parent == null || depth > 24) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c is Label) PrepLabel((Label)c, tip);
                else if (c is Button) PrepButton((Button)c);
                else if (c is TextBoxBase) { PrepFontOnly(c); PrepInputLook((TextBoxBase)c); PrepInput((TextBoxBase)c); }
                else if (c is ListBox) PrepFontOnly(c);
                else if (c is ListView) PrepFontOnly(c);
                else if (c is CheckBox) { PrepFontOnly(c); PrepCheck(c); }
                else if (c is RadioButton) { PrepFontOnly(c); PrepCheck(c); }
                else if (c is ComboBox) { PrepFontOnly(c); PrepCombo((ComboBox)c); }
                else if (c is RichTextBox) PrepFontOnly(c);
                else if (c is NumSpin) { PrepFontOnly(c); if (UiKit.Paper) ((NumSpin)c).ApplyPaper(); }
                else if (c is Panel) PrepPanel((Panel)c);
                PaperFix(c);
                SquashTiny(c);
                if (c.Controls.Count > 0) Walk(c, tip, depth + 1);
            }
        }

        // v18.7 纸感皮肤：暗色皮肤里大量控件直接写 Color.White / 深色底，
        // 换到纸底上会白字压白纸。这里统一翻成墨色文字 + 纸白底。
        // 纸感：墨块区域（侧栏/页头）里的文字一律保住白色
        public static void PaperWhiteText(Control root)
        {
            try
            {
                foreach (Control c in root.Controls)
                {
                    if (c is Label)
                    {
                        // 小字号的副标/版本号在墨块上不要跟主标题一样白，留出层次
                        c.ForeColor = (c.Font != null && c.Font.Size <= 7.5F)
                            ? Color.FromArgb(176, 166, 146) : Color.White;
                    }
                    else if (c is Button)
                        c.ForeColor = Color.White;
                    if (c.HasChildren) PaperWhiteText(c);
                }
            }
            catch (Exception dg613_1) { Diag.Swallow("Ui2.cs:613", dg613_1); }
        }
        static void PaperFix(Control c)
        {
            if (!UiKit.Paper || c == null) return;   // v19.16 纸感 v2：纸白卡片 + 暖墨文字
            if (c.Tag is string && (string)c.Tag == "sidenav") return;   // 侧栏导航条本身保持暖墨
            try
            {
                if (c is TextBoxBase || c is ComboBox || c is NumericUpDown || c is TrackBar || c is ProgressBar) return;
                if (c is Button)
                {
                    c.ForeColor = UiKit.Ink;
                }
                else if (c.ForeColor.A > 120 && UiKit.Lum(c.ForeColor) > 0.72)
                {
                    c.ForeColor = UiKit.Ink;
                }
                else if (c is Label && c.ForeColor.A > 120 && UiKit.Lum(c.ForeColor) > 0.50)
                {
                    int mx = Math.Max(c.ForeColor.R, Math.Max(c.ForeColor.G, c.ForeColor.B));
                    int mn = Math.Min(c.ForeColor.R, Math.Min(c.ForeColor.G, c.ForeColor.B));
                    if (mx - mn < 58) c.ForeColor = Color.FromArgb(58, 52, 43);
                }
                // 原本的深色底（卡片/输入框/占位条）换成纸白或米白，避免黑块压在白纸上太突兀
                if (c.BackColor.A > 200 && UiKit.Lum(c.BackColor) < 0.18)
                    c.BackColor = UiKit.PaperCard;
            }
            catch (Exception dg633_1) { Diag.Swallow("Ui2.cs:633", dg633_1); }
        }

        // v8.5：页面里那些 8x30 之类的小碎块（历史遗留的空占位控件）自带浅底，
        // 在深色页上就是一根根小灰条。统一按它所在那一行的页面色压掉。
        static void SquashTiny(Control c)
        {
            try
            {
                if (c is TextBoxBase || c is ComboBox || c is NumSpin) return;
                // 极窄（<=12px）又没有文字的小控件：不管是 Button 还是 Panel，都是历史遗留的占位碎块，直接藏掉
                if (c.Width <= 12 && c.Height <= 48 && !(c is TrackBar) && !(c is ProgressBar)
                    && (c.Text == null || c.Text.Trim().Length == 0))
                {
                    c.Visible = false;
                    return;
                }
                if (c is Button) return;
                if (c.Width > 64 || c.Height > 64) return;
                Color bc = c.BackColor;
                if (bc.A < 200) return;
                if (UiKit.Lum(bc) <= 0.09) return;
                Control par = c.Parent;
                Control page = par;
                while (page != null && !(page is PagePanel)) page = page.Parent;
                if (page == null) return;
                c.BackColor = PagePanel.BgAt(c.Top + c.Height / 2, page.ClientSize.Height);
            }
            catch (Exception dg661_1) { Diag.Swallow("Ui2.cs:661", dg661_1); }
        }

        // v8.4：没有子控件的大块浅色 Panel 也压深（空态底板/占位条这类）
        static void PrepPanel(Panel pn)
        {
            try
            {
                if (pn == null || pn.IsDisposed) return;
                if (UiKit.Paper)
                {
                    // 纸感里这些无子控件的纯白横条是旧深色版的“标题底/说明底”，
                    // 留在纸面上就是一条刺眼白块；直接把底板撤掉，露出卡片纸纹。
                    if (pn.Controls.Count == 0 && pn.BackColor.A >= 200 && UiKit.Lum(pn.BackColor) > 0.82
                        && pn.Width >= 140 && pn.Height <= 70)
                        pn.Visible = false;
                    else if (pn.BackColor.A >= 200 && UiKit.Lum(pn.BackColor) > 0.82
                        && pn.Width >= 140 && pn.Height <= 70)
                    {
                        bool onlyLabels = true;
                        for (int ci = 0; ci < pn.Controls.Count; ci++)
                            if (!(pn.Controls[ci] is Label)) { onlyLabels = false; break; }
                        if (onlyLabels) pn.BackColor = Color.FromArgb(243, 236, 220);
                    }
                    return;
                }
                if (pn.BackgroundImage != null) return;
                if (pn.Controls.Count > 0) return;
                Color bc = pn.BackColor;
                if (bc.A < 200) return;
                if (pn.Width < 160 || pn.Height < 22) return;
                if (UiKit.Lum(bc) <= 0.18) return;
                pn.BackColor = UiKit.A(20, 24, 30);
            }
            catch (Exception dg678_1) { Diag.Swallow("Ui2.cs:678", dg678_1); }
        }

        static void PrepFontOnly(Control c)
        {
            try
            {
                Font f = c.Font;
                if (f == null) return;
                bool icon = IsIconFont(f);
                // v8.1：读不清的最小字号统一抬到 8F（纸感浅底 8.5F，125% DPI 下 8F 偏细）
                float floor = (UiKit.Paper && UiKit.Lum(UiKit.BackBehind(c)) > 0.50) ? 8.5F : 8F;
                if (!icon && f.Size < floor && f.Size > 4F) f = new Font(FAM, floor, f.Style);
                if (!icon && !IsFamilyOk(f.Name)) f = new Font(FAM, f.Size, f.Style);
                if (f != c.Font) c.Font = f;
            }
            catch (Exception dg693_1) { Diag.Swallow("Ui2.cs:693", dg693_1); }
        }

        // 文字防裁切：不 AutoSize 的 Label 一律给省略号；装不下时挂一条悬浮提示，鼠标停上去能看全
        static Color PaperCardBgAt(Control c)
        {
            try
            {
                Control card = c.Parent;
                int y = c.Top;
                int guard = 0;
                while (card != null && card.Parent != null && card.Height < 80 && guard++ < 8)
                {
                    y += card.Top;
                    card = card.Parent;
                }
                int h = (card == null) ? 1 : Math.Max(1, card.Height);
                double t = Math.Max(0.0, Math.Min(1.0, (double)y / h));
                return Color.FromArgb(243, 236, 220);
            }
            catch (Exception dgbg_1) { Diag.Swallow("Ui2.cs:paperbgat", dgbg_1); return Color.FromArgb(250, 245, 233); }
        }

        static readonly Dictionary<Control, List<Label>> paperLbls = new Dictionary<Control, List<Label>>();
        static readonly HashSet<Control> paperLblWired = new HashSet<Control>();

        // 纸感：透明 Label 直接在自身透明表面上合成会发白。把文字交给父容器的 Paint
        // 统一画在不透明的纸面上，既不会白字消失，也不会在卡片上留下浅色长条。
        static void RegisterPaperLabel(Label l)
        {
            try
            {
                Control p = l.Parent;
                if (p == null || p.IsDisposed) return;
                List<Label> ls;
                if (!paperLbls.ContainsKey(p)) { ls = new List<Label>(); paperLbls[p] = ls; }
                else ls = paperLbls[p];
                if (!ls.Contains(l)) ls.Add(l);
                if (paperLblWired.Add(p))
                    p.Paint += new PaintEventHandler(PaperLabelPaint);
                l.BackColor = Color.Transparent;
                l.Visible = false;
            }
            catch (Exception dgpl_1) { Diag.Swallow("Ui2.cs:paperlabel", dgpl_1); }
        }

        static void PaperLabelPaint(object s, PaintEventArgs e)
        {
            try
            {
                Control p = (Control)s;
                List<Label> ls;
                if (p == null || !paperLbls.TryGetValue(p, out ls)) return;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                for (int i = 0; i < ls.Count; i++)
                {
                    Label l = ls[i];
                    if (l == null || l.IsDisposed || l.Text == null || l.Text.Length == 0 || l.Parent != p) continue;
                    TextFormatFlags fl = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                    int w = l.Width, h = l.Height;
                    if (l.AutoSize)
                    {
                        Size sz = TextRenderer.MeasureText(l.Text, l.Font, new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        w = sz.Width + 2; h = Math.Max(h, sz.Height);
                    }
                    if ((l.TextAlign & ContentAlignment.MiddleCenter) == ContentAlignment.MiddleCenter
                        || (l.TextAlign & ContentAlignment.BottomCenter) == ContentAlignment.BottomCenter
                        || (l.TextAlign & ContentAlignment.TopCenter) == ContentAlignment.TopCenter)
                        fl |= TextFormatFlags.HorizontalCenter;
                    else if ((l.TextAlign & ContentAlignment.MiddleRight) == ContentAlignment.MiddleRight
                        || (l.TextAlign & ContentAlignment.BottomRight) == ContentAlignment.BottomRight
                        || (l.TextAlign & ContentAlignment.TopRight) == ContentAlignment.TopRight)
                        fl |= TextFormatFlags.Right;
                    if ((l.TextAlign & ContentAlignment.MiddleLeft) == ContentAlignment.MiddleLeft
                        || (l.TextAlign & ContentAlignment.MiddleCenter) == ContentAlignment.MiddleCenter
                        || (l.TextAlign & ContentAlignment.MiddleRight) == ContentAlignment.MiddleRight)
                        fl |= TextFormatFlags.VerticalCenter;
                    else if ((l.TextAlign & ContentAlignment.BottomLeft) == ContentAlignment.BottomLeft
                        || (l.TextAlign & ContentAlignment.BottomCenter) == ContentAlignment.BottomCenter
                        || (l.TextAlign & ContentAlignment.BottomRight) == ContentAlignment.BottomRight)
                        fl |= TextFormatFlags.Bottom;
                    if (!l.AutoSize) fl |= TextFormatFlags.WordBreak;
                    TextRenderer.DrawText(e.Graphics, l.Text, l.Font,
                        new Rectangle(l.Left, l.Top, Math.Max(4, w), Math.Max(4, h)),
                        l.ForeColor, fl);
                }
            }
            catch (Exception dgpl_2) { Diag.Swallow("Ui2.cs:paperlabelpaint", dgpl_2); }
        }

        static void PrepLabel(Label l, ToolTip tip)
        {
            PrepFontOnly(l);
            if (l == null || l.IsDisposed) return;
            // v19.16 纸感浅底：GDI 的 TextRenderer 在浅底透明标签上会被按低透明度合成（字发白），
            // 改走 GDI+ DrawString，墨色文字才真正落成墨色；深色皮肤保持原渲染不变。
            if (UiKit.Paper)
            {
                try { l.UseCompatibleTextRendering = true; } catch (Exception dg703_1) { Diag.Swallow("Ui2.cs:703", dg703_1); }
            }
            if (UiKit.Paper && l.Text == "--:--:--" && l.Font != null && l.Font.Size >= 16F)
            {
                l.BackColor = PaperCardBgAt(l);
                l.ForeColor = Color.FromArgb(44, 39, 32);
                l.Font = new Font("Consolas", 24F, FontStyle.Bold);
            }
            if (IsIconText(l.Text) || IsIconFont(l.Font)) return;
            // 纸感：PagePanel 内的透明 Label 在透明表面上做 GDI+ 合成会整体发白。
            // 给它铺一块与所在层级一致的纸面（页面=PaperBg，卡片=PaperCard），文字才真正落墨。
            if (UiKit.Paper && (l.BackColor.A < 200 || UiKit.Lum(l.BackColor) > 0.90))
            {
                Control pg = l.Parent;
                int guard = 0;
                while (pg != null && !(pg is PagePanel) && guard++ < 16) pg = pg.Parent;
                if (pg is PagePanel && !(l.Parent is PagePanel) && l.Text != null && l.Text.Length > 0)
                {
                    Color bg = PaperCardBgAt(l);
                    l.BackColor = bg;
                    if (l.ForeColor.A < 200 || UiKit.Lum(l.ForeColor) > 0.66
                        || UiKit.ContrastRatio(l.ForeColor, bg) < 4.2)
                        l.ForeColor = Color.FromArgb(36, 32, 27);
                }
            }
            if (!l.AutoSize && l.Text != null && l.Text.Length > 0)
            {
                try { l.AutoEllipsis = true; } catch (Exception dg704_1) { Diag.Swallow("Ui2.cs:704", dg704_1); }
                try
                {
                    int avail = l.Width - l.Padding.Horizontal;
                    if (avail > 10)
                    {
                        Size need = TextRenderer.MeasureText(l.Text, l.Font, new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        bool bad = false;
                        if (need.Width > avail)
                        {
                            Size wr = TextRenderer.MeasureText(l.Text, l.Font, new Size(avail, int.MaxValue),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                            bad = wr.Height > l.ClientSize.Height + 2;
                        }
                        if (bad && tip != null) tip.SetToolTip(l, l.Text);
                    }
                }
                catch (Exception dg722_1) { Diag.Swallow("Ui2.cs:722", dg722_1); }
            }
            // v8.4：深色页面上"自带浅底"的 Label 全部收拾掉：
            //  · 大块底板（空态说明区）→ 直接透明，让页面背景透出来，不再出现灰色长方块
            //  · 明显偏亮的底板 → 压成日志同色
            //  · 空文字的窄小碎块（历史遗留占位 Label）→ 透明，别在深色页上留浅色小条
            try
            {
                Color bc = l.BackColor;
                if (!UiKit.Paper && bc.A >= 200)
                {
                    double lu = UiKit.Lum(bc);
                    if (l.Width >= 160 && l.Height >= 20)
                    {
                        if (lu >= 0.42)
                        {
                            l.BackColor = UiKit.A(20, 24, 30);
                            if (UiKit.Lum(l.ForeColor) < 0.45) l.ForeColor = UiKit.A(196, 208, 226);
                        }
                        else if (lu > 0.09) l.BackColor = Color.Transparent;
                    }
                    else if ((l.Text == null || l.Text.Trim().Length == 0) && l.Width <= 64 && l.Height <= 64 && lu > 0.09)
                        l.BackColor = Color.Transparent;   // 空格/空串的占位 Label 也算空
                    // v8.5：页面直属的 Label 直接取"它所在那一行"的页面底色，
                    // 不管原来是 C_MAIN 还是透明，都不会再在深色页上留一块浅灰。
                    if (l.Parent is PagePanel)
                    {
                        PagePanel ppg = (PagePanel)l.Parent;
                        Color tone = PagePanel.BgAt(l.Top + l.Height / 2, ppg.ClientSize.Height);
                        Color cur = l.BackColor;
                        if (cur == Color.Transparent || (cur.A >= 200 && Math.Abs(UiKit.Lum(cur) - UiKit.Lum(tone)) > 0.015))
                            l.BackColor = tone;
                    }
                }
            }
            catch (Exception dg757_1) { Diag.Swallow("Ui2.cs:757", dg757_1); }
            EnsureReadable(l);
            WrapAutoSize(l, tip);
            RegisterPaperPageTitle(l);
            RegisterTitle(l);
            RegisterCardTitle(l);
        }

        // v8.1：文字可读性下限。深灰说明字在深底上只有 2.1:1，远低于 3:1 的底线；
        // 逐档提亮前景色直到够 3.2:1，跳过的只是“颜色太暗”这一种情况，位置与字号一律不动。
        static void EnsureReadable(Label l)
        {
            try
            {
                if (l == null || l.IsDisposed || string.IsNullOrEmpty(l.Text)) return;
                Color bg = UiKit.BackBehind(l);
                Color fg = UiKit.Composite(l.ForeColor, bg);
                if (fg.A < 200) return;
                double ratio = UiKit.ContrastRatio(fg, bg);
                if (ratio >= 4.6) return;
                Color cur = fg;
                bool lightBg = UiKit.Lum(bg) > 0.55;
                for (int step = 0; step < 12; step++)
                {
                    // 深底提亮、浅底压暗：纸感是米白纸面，继续往白提只会把墨字洗成白字
                    int dlt = lightBg ? -14 : 14;
                    cur = Color.FromArgb(Math.Max(0, Math.Min(255, cur.R + dlt)),
                        Math.Max(0, Math.Min(255, cur.G + dlt)),
                        Math.Max(0, Math.Min(255, cur.B + dlt)));
                    if (UiKit.ContrastRatio(cur, bg) >= 4.6) break;
                }
                // 提亮/压暗后仍不够时退回统一副文字色
                if (UiKit.ContrastRatio(cur, bg) < 4.2) cur = lightBg ? UiKit.Ink : SUB;
                l.ForeColor = cur;
            }
            catch (Exception dg786_1) { Diag.Swallow("Ui2.cs:786", dg786_1); }
        }

        // （对比度实现已统一到 UiKit.ContrastRatio）

        // v8.0：AutoSize 的长提示不再“放着不管”——下面有空间就自动折行，没空间才省略号 + 悬浮提示。
        // 折行前后都不会压到任何已有控件：只在下边界内换行，绝不越界。
        static void WrapAutoSize(Label l, ToolTip tip)
        {
            try
            {
                if (l == null || l.IsDisposed || !l.AutoSize) return;
                if (l.MaximumSize.Width > 0) return;
                string txt = l.Text;
                if (txt == null || txt.Length < 18) return;
                if (txt.IndexOf('\n') >= 0) return;
                Control p = l.Parent;
                if (p == null || p.ClientSize.Width < 120) return;
                // 隐藏页还没参与布局，ClientSize 停在 200x100 的默认值；
                // 这时算出来的可用宽度是假的（会把副标题永久锁成 149px、挤成三行），所以先跳过
                if (p.ClientSize.Height < 160) return;
                int avail = p.ClientSize.Width - l.Left - 10;
                if (avail < 90) return;
                Size need = TextRenderer.MeasureText(txt, l.Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (need.Width <= avail) return;
                Size wr = TextRenderer.MeasureText(txt, l.Font, new Size(avail, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                int limit = p.ClientSize.Height - 6;
                int nearest = int.MaxValue;
                for (int i = 0; i < p.Controls.Count; i++)
                {
                    Control c = p.Controls[i];
                    if (c == l || !c.Visible || c.Height <= 0) continue;
                    int ovl = Math.Min(l.Right, c.Right) - Math.Max(l.Left, c.Left);
                    if (ovl <= 0) continue;
                    if (c.Top >= l.Bottom - 2) nearest = Math.Min(nearest, c.Top - 4);
                }
                int room = (nearest == int.MaxValue ? limit : Math.Min(limit, nearest)) - l.Top;
                if (wr.Height <= room && wr.Height > l.Height) l.MaximumSize = new Size(avail, 0);
                else if (wr.Height > room && tip != null) tip.SetToolTip(l, txt);
            }
            catch (Exception dg828_1) { Diag.Swallow("Ui2.cs:828", dg828_1); }
        }

        // v8.0：输入框的焦点态——一进场底色朝强调色偏一点点，离开复原；成本极低但“活的”感觉明显
        static readonly Dictionary<TextBoxBase, Color> inputBg = new Dictionary<TextBoxBase, Color>();

        static void PrepInput(TextBoxBase tb)
        {
            if (tb == null || tb.IsDisposed || tb.ReadOnly) return;
            try
            {
                if (inputBg.ContainsKey(tb)) return;
                inputBg[tb] = tb.BackColor;
                tb.Enter += delegate
                {
                    try
                    {
                        Color b = inputBg.ContainsKey(tb) ? inputBg[tb] : tb.BackColor;
                        tb.BackColor = MixColor(b, UiKit.A(64, 132, 255), 0.12);
                    }
                    catch (Exception dg848_1) { Diag.Swallow("Ui2.cs:848", dg848_1); }
                };
                tb.Leave += delegate
                {
                    try { if (inputBg.ContainsKey(tb)) tb.BackColor = inputBg[tb]; } catch (Exception dg852_1) { Diag.Swallow("Ui2.cs:852", dg852_1); }
                };
            }
            catch (Exception dg855_1) { Diag.Swallow("Ui2.cs:855", dg855_1); }
        }

        static Color MixColor(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(a.A, Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, bl)));
        }

        // 按钮：同族字体 + 省略号 + Flat 按钮的悬浮提亮（原来没有 hover 反馈的扁按钮会明显“活”起来）
        // v8.4：按钮配色收敛。原来一页里蓝/绿/橙/粉混着用，饱和度也各不一样，
        // 看着零散。这里按色相归到四档语义色（主行动 / 成功 / 提醒 / 危险），
        // 并把亮度压到同一档，整页颜色一眼能分出层级。
        public static readonly Color TONE_PRIMARY = UiKit.A(56, 120, 226);
        public static readonly Color TONE_GOOD = UiKit.A(46, 158, 96);
        public static readonly Color TONE_WARN = UiKit.A(206, 150, 58);
        public static readonly Color TONE_BAD = UiKit.A(200, 78, 84);

        public static Color Tone(Color c)
        {
            try
            {
                double h = c.GetHue();
                double sat = c.GetSaturation();
                double br = c.GetBrightness();
                if (sat < 0.16) return c;          // 近灰：卡片/条带不参与
                if (br < 0.28) return c;           // 深底次要按钮不动
                if (h < 18 || h >= 332) return TONE_BAD;
                if (h < 62) return TONE_WARN;
                if (h < 172) return TONE_GOOD;
                if (h < 300) return TONE_PRIMARY;
                return TONE_BAD;                   // 紫/洋红一并归到危险档，去掉刺眼的粉
            }
            catch { return c; }
        }

        // 次要动作判定：这些按钮点了只是"换个地方/拿个结果"，不该和主按钮抢颜色
        static bool IsSecondaryAction(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            string[] keys = { "返回", "返回定时", "打开", "更改", "复制配置", "复制结果", "导出", "清空", "清除选区",
                              "选择文件", "定位", "换一批", "刷新", "重新检测" };
            for (int i = 0; i < keys.Length; i++)
                if (t.IndexOf(keys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // v8.7：列表空着的时候，"导出/复制结果/清空/下载选中/下载全部/删除选中"这类只能吃列表内容的
        // 按钮直接置灰，有数据再恢复——避免点了没反应又不知道为啥。
        public static void SyncListActions(Control page)
        {
            if (page == null || page.IsDisposed) return;
            bool empty = false, hasList = false;
            try
            {
                for (int i = 0; i < page.Controls.Count; i++)
                {
                    ListView lv = page.Controls[i] as ListView;
                    if (lv == null || !lv.Visible) continue;
                    hasList = true;
                    if (lv.Items.Count == 0) { empty = true; break; }
                }
            }
            catch { return; }
            if (!hasList) return;
            string[] dep = { "导出", "复制结果", "清空", "下载选中", "下载全部", "删除选中", "移除选中", "保存结果" };
            try
            {
                for (int i = 0; i < page.Controls.Count; i++)
                {
                    Button b = page.Controls[i] as Button;
                    if (b == null) continue;
                    string t = b.Text;
                    if (string.IsNullOrEmpty(t)) continue;
                    bool hits = false;
                    for (int k = 0; k < dep.Length; k++)
                        if (t.IndexOf(dep[k], StringComparison.Ordinal) >= 0) { hits = true; break; }
                    if (hits) b.Enabled = !empty;
                }
            }
            catch (Exception dg939_1) { Diag.Swallow("Ui2.cs:939", dg939_1); }
        }

        static void PrepButton(Button b)
        {
            PrepFontOnly(b);
            if (b == null || b.IsDisposed) return;
            try
            {
                RoundBtn rb = b as RoundBtn;
                if (rb != null)
                {
                    Color t = Tone(rb.Bg);
                    if (t != rb.Bg) rb.Bg = t;
                    // v8.7：按语义收敛——返回/打开/更改/复制/导出/清空/选择/定位这类"次要动作"统一收成深灰，
                    // 彩色只留给真正的主行动（开始/下载/处理/清理/扫描/搜索）和危险动作（取消/停止/删除）。
                    if (IsSecondaryAction(b.Text))
                    {
                        rb.Bg = UiKit.A(34, 41, 53);
                        Color fg0 = rb.Fg;
                        if (fg0.GetBrightness() < 0.42) rb.Fg = UiKit.A(198, 208, 222);
                    }
                }
                else if (b.BackColor != Color.Transparent)
                {
                    Color t = Tone(b.BackColor);
                    if (t != b.BackColor) b.BackColor = t;
                }
                if (rb != null && UiKit.Paper)
                {
                    string tx = b.Text ?? "";
                    if (tx.IndexOf("停止", StringComparison.Ordinal) >= 0 || tx.IndexOf("取消", StringComparison.Ordinal) >= 0
                        || tx.IndexOf("删除", StringComparison.Ordinal) >= 0)
                        rb.Bg = Color.FromArgb(228, 186, 172);
                    else if (tx.IndexOf("扫描", StringComparison.Ordinal) >= 0 || tx.IndexOf("开始", StringComparison.Ordinal) >= 0
                        || tx.IndexOf("清理", StringComparison.Ordinal) >= 0 || tx.IndexOf("下载", StringComparison.Ordinal) >= 0
                        || tx.IndexOf("搜索", StringComparison.Ordinal) >= 0 || tx.IndexOf("计算", StringComparison.Ordinal) >= 0
                        || tx.IndexOf("应用", StringComparison.Ordinal) >= 0 || tx.IndexOf("确定", StringComparison.Ordinal) >= 0
                        || tx.IndexOf("关机", StringComparison.Ordinal) >= 0)
                        rb.Bg = Color.FromArgb(212, 176, 92);
                    else if (rb.Bg.GetSaturation() < 0.28 && UiKit.Lum(rb.Bg) > 0.45 && UiKit.Lum(rb.Bg) < 0.94)
                        rb.Bg = Color.FromArgb(250, 245, 233);
                    Color pw = Color.FromArgb(246, 241, 229);
                    rb.Fg = UiKit.ContrastRatio(UiKit.Ink, rb.Bg) >= UiKit.ContrastRatio(pw, rb.Bg) ? UiKit.Ink : pw;
                }
                if (UiKit.Paper && rb == null)
                {
                    // 纸感中原生按钮一律收成纸面系：危险浅红、主行动黄铜、其余暖纸。
                    // 不再按原底色亮度判断，避免系统灰按钮遗留在浅色页面上。
                    b.UseCompatibleTextRendering = true;
                    if (b.BackColor.A >= 200)
                    {
                        string tx = b.Text ?? "";
                        Color pbg;
                        if (tx.IndexOf("停止", StringComparison.Ordinal) >= 0 || tx.IndexOf("取消", StringComparison.Ordinal) >= 0
                            || tx.IndexOf("删除", StringComparison.Ordinal) >= 0)
                            pbg = Color.FromArgb(228, 186, 172);
                        else if (tx.IndexOf("扫描", StringComparison.Ordinal) >= 0 || tx.IndexOf("开始", StringComparison.Ordinal) >= 0
                            || tx.IndexOf("清理", StringComparison.Ordinal) >= 0 || tx.IndexOf("下载", StringComparison.Ordinal) >= 0
                            || tx.IndexOf("搜索", StringComparison.Ordinal) >= 0 || tx.IndexOf("计算", StringComparison.Ordinal) >= 0
                            || tx.IndexOf("应用", StringComparison.Ordinal) >= 0 || tx.IndexOf("确定", StringComparison.Ordinal) >= 0)
                            pbg = Color.FromArgb(212, 176, 92);
                        else
                            pbg = Color.FromArgb(250, 245, 233);
                        b.BackColor = pbg;
                        b.UseVisualStyleBackColor = false;
                        try
                        {
                            b.FlatStyle = FlatStyle.Flat;
                            b.FlatAppearance.BorderColor = Color.FromArgb(190, 178, 152);
                            b.FlatAppearance.BorderSize = 1;
                        }
                        catch (Exception dg990_1) { Diag.Swallow("Ui2.cs:990", dg990_1); }
                    }
                    if (b.BackColor.A >= 200)
                    {
                        Color pw = Color.FromArgb(246, 241, 229);
                        b.ForeColor = UiKit.ContrastRatio(UiKit.Ink, b.BackColor) >= UiKit.ContrastRatio(pw, b.BackColor) ? UiKit.Ink : pw;
                    }
                }
            }
            catch (Exception dg968_1) { Diag.Swallow("Ui2.cs:968", dg968_1); }
            try { b.AutoEllipsis = true; } catch (Exception dg969_1) { Diag.Swallow("Ui2.cs:969", dg969_1); }
            try
            {
                if (b.FlatStyle == FlatStyle.Flat && b.BackColor != Color.Transparent)
                {
                    b.FlatAppearance.MouseOverBackColor = UiKit.Lighten(b.BackColor, 16);
                    b.FlatAppearance.MouseDownBackColor = UiKit.Darken(b.BackColor, 12);
                }
            }
            catch (Exception dg978_1) { Diag.Swallow("Ui2.cs:978", dg978_1); }
        }

        // 页面/窗体大标题左侧加一根渐变强调条（画在父容器 Paint 里，永远在文字下层，不会遮住任何字）
        static void RegisterTitle(Label l)
        {
            try
            {
                if (l == null || l.Parent == null || l.Font == null) return;
                if (l.Font.Size < 15F) return;
                if (!l.AutoSize) return;
                if (l.Left < 16) return;
                if (l.Text == null || l.Text.Length == 0) return;
                if (IsIconFont(l.Font) || IsIconText(l.Text)) return;
                Control p = l.Parent;
                if (!(p is PagePanel || p is Form)) return;
                List<Label> list = null;
                if (!titles.ContainsKey(p)) { list = new List<Label>(); titles[p] = list; }
                else list = titles[p];
                if (list.Contains(l)) return;
                list.Add(l);
                if (!wired.Contains(p))
                {
                    wired.Add(p);
                    p.Paint += new PaintEventHandler(TitleBarPaint);
                }
            }
            catch (Exception dg1005_1) { Diag.Swallow("Ui2.cs:1005", dg1005_1); }
        }

        // v8.2：卡片里的小标题也带一枚 3px 强调短条（画在卡片背景层，永远在子控件之下）。
        // 页面大标题是 4px 长条，卡片小标题是 3px 短条，层级一眼就分得开。
        static readonly Dictionary<Control, List<Label>> cardTitles = new Dictionary<Control, List<Label>>();

        // v19.16 纸感：页面级标题（≥9.5F 粗体）不再交给透明 Label，
        // 统一在 PagePanel 的 Paint 里用 AntiAliasGridFit 自绘，彻底消掉 ClearType 白边/空心。
        static readonly Dictionary<Control, List<Label>> paperTitles = new Dictionary<Control, List<Label>>();
        static readonly HashSet<Control> paperTitleWired = new HashSet<Control>();

        static void RegisterPaperPageTitle(Label l)
        {
            try
            {
                if (!UiKit.Paper || l == null || l.Parent == null) return;
                if (!(l.Parent is PagePanel)) return;
                if (!l.Visible || !l.AutoSize || l.Font == null) return;
                if (!l.Font.Bold || l.Font.Size < 9.5F) return;
                if (l.Text == null || l.Text.Trim().Length == 0) return;
                if (IsIconFont(l.Font) || IsIconText(l.Text)) return;
                Control p = l.Parent;
                List<Label> list;
                if (!paperTitles.TryGetValue(p, out list)) { list = new List<Label>(); paperTitles[p] = list; }
                if (!list.Contains(l)) list.Add(l);
                l.Visible = false;
                if (!paperTitleWired.Contains(p))
                {
                    paperTitleWired.Add(p);
                    p.Paint += new PaintEventHandler(PaperPageTitlePaint);
                }
            }
            catch (Exception dg1036b_1) { Diag.Swallow("Ui2.cs:1036b", dg1036b_1); }
        }

        static void PaperPageTitlePaint(object s, PaintEventArgs e)
        {
            try
            {
                Control p = (Control)s;
                List<Label> list;
                if (p == null || !paperTitles.TryGetValue(p, out list)) return;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(UiKit.Ink))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        Label l = list[i];
                        if (l == null || l.IsDisposed) continue;
                        e.Graphics.DrawString(l.Text, l.Font, br, new PointF(l.Left, l.Top - 1f));
                    }
                }
            }
            catch (Exception dg1036c_1) { Diag.Swallow("Ui2.cs:1036c", dg1036c_1); }
        }

        static void RegisterCardTitle(Label l)
        {
            try
            {
                if (l == null || l.Parent == null || l.Font == null) return;
                if (!l.Font.Bold) return;
                if (l.Font.Size < 9.5F || l.Font.Size > 13.5F) return;
                if (!l.AutoSize) return;
                if (l.Left < 12) return;
                if (l.Text == null || l.Text.Length == 0) return;
                if (IsIconFont(l.Font) || IsIconText(l.Text)) return;
                Control p = l.Parent;
                if (p is Form) return;
                if (p is PagePanel) return;   // 页面小节标题已有页面锚点，不再加卡片小竖条（否则像一颗黑点）
                List<Label> list;
                if (!cardTitles.ContainsKey(p)) { list = new List<Label>(); cardTitles[p] = list; }
                else list = cardTitles[p];
                if (list.Contains(l)) return;
                list.Add(l);
                if (!wired.Contains(p))
                {
                    wired.Add(p);
                    p.Paint += new PaintEventHandler(CardTitlePaint);
                }
            }
            catch (Exception dg1036_1) { Diag.Swallow("Ui2.cs:1036", dg1036_1); }
        }

        static void CardTitlePaint(object s, PaintEventArgs e)
        {
            Control p = (Control)s;
            if (p == null || !cardTitles.ContainsKey(p)) return;
            List<Label> list = cardTitles[p];
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < list.Count; i++)
            {
                Label l = list[i];
                if (l == null || l.IsDisposed || !l.Visible) continue;
                int x = l.Left - 9;
                int h = Math.Max(8, l.Height - 5);
                if (x < 2 || h < 8) continue;
                Rectangle rc = new Rectangle(x, l.Top + 2, 3, h);
                using (GraphicsPath path = UiKit.RoundedPath(rc, 1))
                using (LinearGradientBrush br = new LinearGradientBrush(rc,
                    Color.FromArgb(190, ACCENT), UiKit.A(46, 120, 200), 90F))
                    e.Graphics.FillPath(br, path);
            }
        }

        // v8.2：复选框/单选框是唯一还留着“系统白框”的地方，改成 Flat 深色描边
        // v8.3：输入区统一外观。单行 TextBox 在外面套一圈 7 圆角深边（画在父容器背景层，
        // 输入框自己盖住中间，留下的就是一圈规整的边），多行文本框（日志/报告）统一压成深底，
        // 免得整块浅灰在深色页面上特别跳。
        static readonly Dictionary<Control, List<TextBoxBase>> inputRims = new Dictionary<Control, List<TextBoxBase>>();
        static readonly Dictionary<TextBoxBase, Panel> paperInputVeils = new Dictionary<TextBoxBase, Panel>();

        // 纸感下原生 TextBox 会把 BackColor 画成系统白块，文字也被系统主题洗淡。
        // 这里给单行输入框盖一层同尺寸纸面 Panel：未聚焦时显示纸面 + 墨字，点一下才露出真正的输入框。
        public static void InstallPaperInputVeil(TextBoxBase tb)
        {
            try
            {
                if (tb == null || tb.IsDisposed || tb.Multiline || tb.Parent == null) return;
                if (tb.Parent is NumSpin) return;
                TextBox tx = tb as TextBox;
                if (tx != null && (tx.PasswordChar != '\0' || tx.UseSystemPasswordChar)) return;
                if (paperInputVeils.ContainsKey(tb)) return;
                if (!tb.Visible) return;
                Control p = tb.Parent;
                Panel veil = new Panel();
                veil.Bounds = tb.Bounds;
                veil.BackColor = Color.FromArgb(245, 238, 223);
                veil.Tag = "paperveil";
                veil.Cursor = Cursors.IBeam;
                veil.Paint += delegate (object vs, PaintEventArgs ve)
                {
                    Graphics vg = ve.Graphics;
                    vg.Clear(Color.FromArgb(245, 238, 223));
                    vg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (StringFormat vf = new StringFormat(StringFormatFlags.NoWrap))
                    {
                        vf.LineAlignment = StringAlignment.Center;
                        vf.Alignment = StringAlignment.Near;
                        vf.Trimming = StringTrimming.EllipsisCharacter;
                        using (SolidBrush vb = new SolidBrush(Color.FromArgb(18, 16, 14)))
                            vg.DrawString(tb.Text, tb.Font, vb, new RectangleF(7, 0, veil.Width - 9, veil.Height), vf);
                    }
                };
                p.Controls.Add(veil);
                veil.BringToFront();
                paperInputVeils[tb] = veil;
                tb.Visible = false;
                veil.Click += delegate
                {
                    veil.Visible = false;
                    tb.Visible = true;
                    tb.BringToFront();
                    tb.Focus();
                };
                tb.TextChanged += delegate { if (veil != null && !veil.IsDisposed) veil.Invalidate(); };
                tb.Move += delegate { if (veil != null && !veil.IsDisposed) veil.Bounds = tb.Bounds; };
                tb.Resize += delegate { if (veil != null && !veil.IsDisposed) veil.Bounds = tb.Bounds; };
                tb.Enter += delegate { veil.Visible = false; };
                tb.LostFocus += delegate
                {
                    tb.Visible = false;
                    veil.Bounds = tb.Bounds;
                    veil.Visible = true;
                    veil.BringToFront();
                    veil.Invalidate();
                };
            }
            catch (Exception dgveil) { Diag.Swallow("Ui2.cs:veil", dgveil); }
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subApp, string subId);

        // v8.4：单行输入框的空态占位提示。Win 原生 edit 控件支持 EM_SETCUEBANNER，
        // 空值时由系统在框内画一行灰字，聚焦后自动消失，不占任何版面。
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        const int EM_SETCUEBANNER = 0x1501;

        static void TryCue(TextBoxBase tb)
        {
            try
            {
                if (tb == null || tb.IsDisposed || tb.Multiline) return;
                Control p = tb.Parent;
                if (p == null) return;
                string hint = null;
                int bestTop = int.MinValue;
                for (int i = 0; i < p.Controls.Count; i++)
                {
                    Control c = p.Controls[i];
                    if (!(c is Label) || !c.Visible) continue;
                    string t = ((Label)c).Text;
                    if (string.IsNullOrEmpty(t)) continue;
                    t = t.Trim();
                    while (t.Length > 0 && (t[t.Length - 1] == '：' || t[t.Length - 1] == ':' || t[t.Length - 1] == '＊' || t[t.Length - 1] == '*'))
                        t = t.Substring(0, t.Length - 1).Trim();
                    int cutAt = t.IndexOfAny(new char[] { '·', '｜', '|', '。', '，', ',' });
                    if (cutAt > 1) t = t.Substring(0, cutAt).Trim();
                    if (t.Length < 2 || t.Length > 14) continue;
                    if (t.IndexOf('\n') >= 0) continue;
                    int ovl = Math.Min(tb.Right, c.Right) - Math.Max(tb.Left, c.Left);
                    if (ovl < 20) continue;
                    if (c.Top < tb.Top - 46 || c.Bottom > tb.Top + 6) continue;
                    if (c.Top > bestTop) { bestTop = c.Top; hint = t; }
                }
                if (hint == null) return;
                IntPtr h = tb.Handle;
                if (h == IntPtr.Zero) return;
                SendMessageW(h, EM_SETCUEBANNER, (IntPtr)1, hint);
            }
            catch (Exception dg1108_1) { Diag.Swallow("Ui2.cs:1108", dg1108_1); }
        }

        static void TryDarkScroll(Control c)
        {
            try
            {
                if (c == null || !c.IsHandleCreated) return;
                SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch (Exception dg1118_1) { Diag.Swallow("Ui2.cs:1118", dg1118_1); }
        }

        static void PrepInputLook(TextBoxBase tb)
        {
            try
            {
                if (tb == null || tb.IsDisposed) return;
                if (tb.Multiline)
                {
                    // 纸感下多行框统一收成纸白面 + 暖墨字，和卡片同族，不再是一块深色墨块
                    tb.BackColor = UiKit.Paper ? Color.FromArgb(245, 238, 223) : UiKit.A(20, 24, 30);
                    if (tb.ForeColor.A < 200 || UiKit.Lum(tb.ForeColor) < 0.5 || UiKit.Paper)
                        tb.ForeColor = UiKit.Paper ? Color.FromArgb(64, 58, 47) : UiKit.A(200, 212, 228);
                    try { tb.BorderStyle = BorderStyle.None; } catch (Exception dg1130_1) { Diag.Swallow("Ui2.cs:1130", dg1130_1); }
                    TryDarkScroll(tb);
                    try { DarkScrollBar.Attach(tb); } catch (Exception dg1132_1) { Diag.Swallow("Ui2.cs:1132", dg1132_1); }   // v12.8：预览框右侧滚动槽同样换深色
                    return;
                }
                if (UiKit.Paper)
                {
                    if (tb.Parent is NumSpin)
                    {
                        tb.BackColor = Color.FromArgb(243, 236, 220);
                        tb.ForeColor = Color.FromArgb(18, 16, 14);
                        try { tb.BorderStyle = BorderStyle.None; } catch (Exception dgns) { Diag.Swallow("Ui2.cs:numspininput", dgns); }
                        return;
                    }
                    tb.BackColor = Color.FromArgb(245, 238, 223);
                    tb.ForeColor = UiKit.Ink;
                    if (tb.Width <= 8 || tb.Height <= 8)
                    {
                        try { tb.Visible = false; } catch (Exception dg1144p) { Diag.Swallow("Ui2.cs:1144p", dg1144p); }
                        return;
                    }
                    try { tb.BorderStyle = BorderStyle.None; } catch (Exception dg1147p) { Diag.Swallow("Ui2.cs:1147p", dg1147p); }
                    try { SetWindowTheme(tb.Handle, " ", " "); } catch (Exception dgtheme) { Diag.Swallow("Ui2.cs:theme", dgtheme); }
                    try { tb.Enabled = true; tb.ReadOnly = false; } catch (Exception dgen) { Diag.Swallow("Ui2.cs:enable", dgen); }
                    tb.ForeColor = Color.FromArgb(18, 16, 14);
                    tb.Invalidate();
                    InstallPaperInputVeil(tb);
                    TryCue(tb);
                    return;
                }
                if (UiKit.Lum(tb.BackColor) > 0.35)
                {
                    tb.BackColor = UiKit.A(24, 29, 38);
                    if (UiKit.Lum(tb.ForeColor) < 0.5) tb.ForeColor = UiKit.A(214, 226, 242);
                }
                // 宽/高只有几个像素的单行输入框是历史遗留的占位空控件，
                // 套上圆角边后在深色页面上就成了旁边一根根小灰条，直接隐藏。
                if (tb.Width <= 8 || tb.Height <= 8)
                {
                    try { tb.Visible = false; } catch (Exception dg1144_1) { Diag.Swallow("Ui2.cs:1144", dg1144_1); }
                    return;
                }
                try { if (tb.BorderStyle != BorderStyle.None) tb.BorderStyle = BorderStyle.None; } catch (Exception dg1147_1) { Diag.Swallow("Ui2.cs:1147", dg1147_1); }
                RegisterInputRim(tb);
                TryCue(tb);
            }
            catch (Exception dg1151_1) { Diag.Swallow("Ui2.cs:1151", dg1151_1); }
        }

        static void RegisterInputRim(TextBoxBase tb)
        {
            try
            {
                Control p = tb.Parent;
                if (p == null) return;
                List<TextBoxBase> l;
                if (!inputRims.ContainsKey(p)) { l = new List<TextBoxBase>(); inputRims[p] = l; }
                else l = inputRims[p];
                if (l.Contains(tb)) return;
                l.Add(tb);
                if (!wired.Contains(p))
                {
                    wired.Add(p);
                    p.Paint += new PaintEventHandler(InputRimPaint);
                }
            }
            catch (Exception dg1171_1) { Diag.Swallow("Ui2.cs:1171", dg1171_1); }
        }

        static void InputRimPaint(object s, PaintEventArgs e)
        {
            Control p = (Control)s;
            List<TextBoxBase> l;
            if (p == null || !inputRims.TryGetValue(p, out l)) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < l.Count; i++)
            {
                TextBoxBase tb = l[i];
                if (tb == null || tb.IsDisposed || !tb.Visible) continue;
                Rectangle rc = new Rectangle(tb.Left - 3, tb.Top - 3, tb.Width + 6, tb.Height + 6);
                if (rc.Width < 12 || rc.Height < 12 || rc.Top < 0) continue;
                // 控件底色是反补过的，这里先还原成"肉眼看到的颜色"再算描边和填充，
                // 否则按反补值算会得到一块比输入框更黑的框。
                Color bg = UnCtlInk(tb.BackColor);
                if (bg == Color.Transparent) bg = UiKit.A(26, 31, 41);
                using (GraphicsPath path = UiKit.RoundedPath(rc, 7))
                {
                    using (SolidBrush b = new SolidBrush(UiKit.Darken(bg, 4)))
                        e.Graphics.FillPath(b, path);
                    // v8.6：描边原来是 74,86,104，比输入框亮一档，深色页上右端会读成"一根浮着的小浅条"。
                    // 常态降到比底色深一点的收边色，聚焦时才换强调色，既有回应也不再抢眼。
                    Color penC = UiKit.Paper
                        ? (tb.Focused ? Color.FromArgb(158, 120, 44) : Color.FromArgb(186, 172, 146))
                        : (tb.Focused ? UiKit.A(86, 140, 222) : UiKit.A(50, 60, 76));
                    using (Pen pen = new Pen(penC, 1f))
                        e.Graphics.DrawPath(pen, path);
                    // v8.8：聚焦时再套一圈更淡的蓝，做出"灯亮起来"的柔光感，而不是只换一根线的颜色
                    // v9.0：one 2px 圈在 100% 缩放下读起来偏"毛边"，拆成 1px 贴边亮线 + 1px 极淡外圈，
                    // 层次更像真的光晕，边缘也更干净。
                    if (tb.Focused)
                    {
                        Rectangle rg1 = new Rectangle(rc.X - 1, rc.Y - 1, rc.Width + 2, rc.Height + 2);
                        using (GraphicsPath gp1 = UiKit.RoundedPath(rg1, 8))
                        using (Pen gpen1 = new Pen(UiKit.AA(104, 86, 140, 222), 1f))
                            e.Graphics.DrawPath(gpen1, gp1);
                        Rectangle rg2 = new Rectangle(rc.X - 2, rc.Y - 2, rc.Width + 4, rc.Height + 4);
                        using (GraphicsPath gp2 = UiKit.RoundedPath(rg2, 9))
                        using (Pen gpen2 = new Pen(UiKit.AA(34, 86, 140, 222), 1f))
                            e.Graphics.DrawPath(gpen2, gp2);
                    }
                }
            }
        }

        // v8.3：下拉列表自绘。原生 ComboBox 的弹出列表是系统白底黑字，
        // 开着 OwnerDrawFixed 后列表每一行都由我们画，深底浅字，配选中高亮。
        static readonly List<ComboBox> combos = new List<ComboBox>();

        static void PrepCombo(ComboBox cb)
        {
            try
            {
                if (cb == null || cb.IsDisposed) return;
                if (combos.Contains(cb)) return;
                combos.Add(cb);
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = 22;
                cb.DrawItem += ComboDrawItem;
            }
            catch (Exception dg1232_1) { Diag.Swallow("Ui2.cs:1232", dg1232_1); }
        }

        static void ComboDrawItem(object s, DrawItemEventArgs e)
        {
            ComboBox cb = s as ComboBox;
            if (cb == null || e.Index < 0 || e.Index >= cb.Items.Count) return;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Rectangle rc = e.Bounds;
            using (SolidBrush b = new SolidBrush(sel ? UiKit.A(54, 76, 110) : UiKit.A(24, 29, 38)))
                e.Graphics.FillRectangle(b, rc);
            Rectangle tr = new Rectangle(rc.X + 8, rc.Y, Math.Max(8, rc.Width - 12), rc.Height);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(cb.Items[e.Index]), cb.Font, tr,
                sel ? UiKit.A(238, 244, 255) : UiKit.A(198, 208, 224),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // v8.3：底部收口。页面最下面那行提示原来会顶出窗口底边、只剩上半截字。
        // 这里把超出底边的控件整体上移到安全线，同时把正上方那块可压缩的区域（列表/日志）等量压矮，
        // 保证既不裁字、也不压住别的东西。
        public static int fixBumps = 0;

        public static void FixBottom(Control parent, int depth)
        {
            if (parent == null || depth > 12) return;
            try
            {
                int limit = parent.ClientSize.Height - 8;
                List<Control> kids = new List<Control>();
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c = parent.Controls[i];
                    if (c.Dock != DockStyle.None) continue;
                    if (c.Width < 8 || c.Height < 8) continue;
                    kids.Add(c);
                }
                kids.Sort(delegate(Control a, Control b) { return b.Top.CompareTo(a.Top); });
                for (int i = 0; i < kids.Count; i++)
                {
                    Control c = kids[i];
                    if (c is Panel && c.Controls.Count > 0) continue;
                    int over = c.Bottom - limit;
                    if (over <= 0 || over > 80) continue;
                    Control shrink = null;
                    int bestBottom = -1;
                    for (int k = 0; k < parent.Controls.Count; k++)
                    {
                        Control s2 = parent.Controls[k];
                        if (s2 == c || s2.Dock != DockStyle.None) continue;
                        bool xo = (s2.Left < c.Right) && (s2.Right > c.Left);
                        if (!xo) continue;
                        if (s2.Bottom > c.Top) continue;
                        bool can = (s2 is ListView) || (s2 is RichTextBox) || (s2 is TextBoxBase) || (s2 is DataGridView) || (s2 is ListBox);
                        if (!can) continue;
                        if (s2.Bottom > bestBottom) { bestBottom = s2.Bottom; shrink = s2; }
                    }
                    if (shrink != null && shrink.Height - over >= 60)
                    {
                        shrink.Height = shrink.Height - over;
                        shrink = null;
                    }
                    c.Top = c.Top - over;
                    fixBumps++;
                }
            }
            catch (Exception dg1297_1) { Diag.Swallow("Ui2.cs:1297", dg1297_1); }
            try
            {
                for (int i = 0; i < parent.Controls.Count; i++)
                {
                    Control c = parent.Controls[i];
                    if (c.Controls.Count > 0) FixBottom(c, depth + 1);
                }
            }
            catch (Exception dg1306_1) { Diag.Swallow("Ui2.cs:1306", dg1306_1); }
        }

        static void PrepCheck(Control c)
        {
            try
            {
                Color bd = UiKit.A(118, 130, 150);
                Color ck = UiKit.A(46, 58, 78);
                // v8.2 起复选框/单选框是自绘控件（RoundCheck / RoundRadio），这里只留一句兜底
                CheckBox cb = c as CheckBox;
                if (cb != null && !(cb is RoundCheck)) { cb.FlatStyle = FlatStyle.Standard; cb.FlatAppearance.BorderColor = bd; }
                RadioButton rb = c as RadioButton;
                if (rb != null && !(rb is RoundRadio)) { rb.FlatStyle = FlatStyle.Standard; rb.FlatAppearance.BorderColor = bd; }
            }
            catch (Exception dg1321_1) { Diag.Swallow("Ui2.cs:1321", dg1321_1); }
        }

        static void TitleBarPaint(object s, PaintEventArgs e)
        {
            List<Label> list = null;
            Control p = (Control)s;
            if (p == null || !titles.ContainsKey(p)) return;
            list = titles[p];
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < list.Count; i++)
            {
                Label l = list[i];
                if (l == null || l.IsDisposed) continue;
                int x = l.Left - 11;
                int y = l.Top + 3;
                int w = 4;
                int h = Math.Max(10, l.Height - 6);
                if (x < 6 || h < 10) continue;
                Rectangle rc = new Rectangle(x, y, w, h);
                using (GraphicsPath path = UiKit.RoundedPath(rc, 2))
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(rc, Color.FromArgb(235, ACCENT), UiKit.A(70, 120, 200), 90F))
                        e.Graphics.FillPath(br, path);
                }
                // v8.1：标题条向右延伸成一条渐隐的细线，把“标题”和“下面一堆控件”分开；
                // 线画在背景层，任何子控件都盖在它上面，所以既不会压字也不会压按钮
                int lx = l.Right + 12;
                int lw = p.ClientSize.Width - lx - 18;
                if (lx > 60 && lw > 40)
                {
                    Rectangle lr = new Rectangle(lx, l.Top + Math.Max(8, l.Height / 2) - 1, lw, 1);
                    using (LinearGradientBrush lb = new LinearGradientBrush(lr,
                        UiKit.AA(56, 120, 168, 255), UiKit.AA(0, 120, 168, 255), 0f))
                        e.Graphics.FillRectangle(lb, lr);
                }
            }
        }

        /// <summary>虚线“扫描框”：四角加粗，虚线随 phase 流动，用于投递遮罩。</summary>
        public static void DrawDashFrame(Graphics g, Rectangle rc, Color accent, int phase)
        {
            if (rc.Width < 30 || rc.Height < 30) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiKit.RoundedPath(rc, 16))
            {
                using (Pen pen = new Pen(Color.FromArgb(150, accent), 1.3f))
                {
                    pen.DashStyle = DashStyle.Custom;
                    pen.DashPattern = new float[] { 6f, 5f };
                    pen.DashOffset = (float)((phase % 11) * 0.55);
                    g.DrawPath(pen, path);
                }
            }
            int L = 26;
            using (Pen cp = new Pen(Color.FromArgb(225, accent), 2.2f))
            {
                g.DrawLine(cp, rc.Left + 8, rc.Top, rc.Left + 8 + L, rc.Top);
                g.DrawLine(cp, rc.Left, rc.Top + 8, rc.Left, rc.Top + 8 + L);
                g.DrawLine(cp, rc.Right - 8 - L, rc.Top, rc.Right - 8, rc.Top);
                g.DrawLine(cp, rc.Right, rc.Top + 8, rc.Right, rc.Top + 8 + L);
                g.DrawLine(cp, rc.Left + 8, rc.Bottom, rc.Left + 8 + L, rc.Bottom);
                g.DrawLine(cp, rc.Left, rc.Bottom - 8 - L, rc.Left, rc.Bottom - 8);
                g.DrawLine(cp, rc.Right - 8 - L, rc.Bottom, rc.Right - 8, rc.Bottom);
                g.DrawLine(cp, rc.Right, rc.Bottom - 8 - L, rc.Right, rc.Bottom - 8);
            }
        }

        // ---------------- 轻提示（toast）：右下角滑入，默认 2.4 秒自动消失，不打断操作 ----------------
        // v18.6：改为可堆叠（同类提示不再互相顶掉），并支持时长与"警示"样式
        static readonly Dictionary<Form, List<Panel>> toastStack = new Dictionary<Form, List<Panel>>();

        public static void Toast(Form owner, string msg) { Toast(owner, msg, ACCENT, 2400); }

        public static void Toast(Form owner, string msg, Color accent) { Toast(owner, msg, accent, 2400); }

        // 错误/警告专用：琥珀色、4 秒
        public static void ToastBad(Form owner, string msg) { Toast(owner, msg, Color.FromArgb(255, 176, 74), 4000); }

        public static void Toast(Form owner, string msg, Color accent, int ms)
        {
            if (owner == null || owner.IsDisposed || msg == null || msg.Length == 0) return;
            try
            {
                List<Panel> list;
                if (!toastStack.ContainsKey(owner)) toastStack[owner] = new List<Panel>();
                list = toastStack[owner];
                // 先清掉已经失效的
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i] == null || list[i].IsDisposed) list.RemoveAt(i);

                Size need = TextRenderer.MeasureText(msg, new Font(FAM, 9.5F, FontStyle.Bold), new Size(520, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                int w = Math.Min(560, Math.Max(170, need.Width + 62));
                int h = Math.Max(40, need.Height + 20);
                Panel card = new Panel();
                card.Size = new Size(w, h);
                card.BackColor = UiKit.A(30, 36, 46);
                card.Tag = new object[] { msg, accent };
                card.Paint += new PaintEventHandler(ToastPaint);

                int baseBottom = owner.ClientSize.Height - ((owner is MainForm) ? 96 : 18);
                int x = owner.ClientSize.Width - w - 22;
                if (x < 8) x = 8;

                card.Location = new Point(x, baseBottom - h);
                card.Top += 10;   // 起始略低，动画上浮
                owner.Controls.Add(card);
                card.BringToFront();
                list.Add(card);

                // 已有提示往上顶，避免叠在一起
                int shift = h + 8;
                for (int i = 0; i < list.Count - 1; i++)
                {
                    Panel p = list[i];
                    if (p == null || p.IsDisposed) continue;
                    p.Top -= shift;
                    if (p.Top < 8) p.Top = 8;
                }

                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 15;
                int step = 0;
                int top = card.Top;
                int life = Math.Max(40, ms / 15);
                t.Tick += delegate
                {
                    try
                    {
                        if (card.IsDisposed || owner.IsDisposed) { t.Stop(); t.Dispose(); return; }
                        step++;
                        if (step <= 8) card.Top = top + 10 - step;
                        else if (step >= life)
                        {
                            try { owner.Controls.Remove(card); card.Dispose(); } catch (Exception dg1456_1) { Diag.Swallow("Ui2.cs:1456", dg1456_1); }
                            try { if (toastStack.ContainsKey(owner)) toastStack[owner].Remove(card); } catch (Exception dg1457_1) { Diag.Swallow("Ui2.cs:1457", dg1457_1); }
                            t.Stop();
                            t.Dispose();
                        }
                    }
                    catch { try { t.Stop(); t.Dispose(); } catch (Exception dg1462_1) { Diag.Swallow("Ui2.cs:1462", dg1462_1); } }
                };
                t.Start();
            }
            catch (Exception dg1466_1) { Diag.Swallow("Ui2.cs:1466", dg1466_1); }
        }

        static void ToastPaint(object s, PaintEventArgs e)
        {
            Panel c = (Panel)s;
            object[] tag = null;
            try { tag = (object[])c.Tag; } catch (Exception dg1473_1) { Diag.Swallow("Ui2.cs:1473", dg1473_1); }
            if (tag == null) return;
            string text = (string)tag[0];
            Color accent = (Color)tag[1];
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            using (GraphicsPath path = UiKit.RoundedPath(rc, 12))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(rc, UiKit.A(38, 45, 57), UiKit.A(24, 29, 37), 90F))
                    g.FillPath(br, path);
                using (Pen pen = new Pen(UiKit.AA(70, 255, 255, 255), 1f))
                    g.DrawPath(pen, path);
                // 左侧 4px 强调条
                Rectangle bar = new Rectangle(6, 8, 4, c.Height - 16);
                using (GraphicsPath bp = UiKit.RoundedPath(bar, 2))
                using (LinearGradientBrush bb = new LinearGradientBrush(bar, Color.FromArgb(240, accent), Color.FromArgb(60, accent), 90F))
                    g.FillPath(bb, bp);
                // 小圆点状态灯
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(230, accent)))
                    g.FillEllipse(dot, new Rectangle(18, c.Height / 2 - 3, 6, 6));
            }
            using (Font f = new Font(FAM, 9.5F, FontStyle.Bold))
                TextRenderer.DrawText(g, text, f, new Rectangle(34, 0, c.Width - 44, c.Height), TEXT,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
