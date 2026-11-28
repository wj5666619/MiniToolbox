using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ShutdownTool
{
    // v19.13：把 3top.top / e-CNY 开源工具站整站装进独立深色窗口（88 个工具）。
    // v19.15：离线包不再内嵌，优先读程序目录 zip，缺失时按需下载并缓存。
    static class WebView2Boot
    {
        static bool inited = false;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                try
                {
                    string n = new AssemblyName(e.Name).Name;
                    if (n == "Microsoft.Web.WebView2.Core") return LoadRes("WV2Core");
                    if (n == "Microsoft.Web.WebView2.WinForms") return LoadRes("WV2WinForms");
                }
                catch (Exception dg) { Diag.Swallow("ToolsSite.cs:resolve", dg); }
                return null;
            };
        }

        static Assembly LoadRes(string name)
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (st == null) return null;
                byte[] b = new byte[st.Length];
                st.Read(b, 0, b.Length);
                return Assembly.Load(b);
            }
        }

        // 把原生 WebView2Loader.dll 释放到本地组件目录，再告诉托管层去哪里找
        public static void PrepareLoader()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniToolbox", "组件", "WebView2", "loader-1.0.4370.26");
            Directory.CreateDirectory(dir);
            string dll = Path.Combine(dir, "WebView2Loader.dll");
            string res = Environment.Is64BitProcess ? "WV2Loader64" : "WV2Loader32";
            if (!File.Exists(dll))
            {
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (st == null) throw new Exception("安装包缺少 WebView2Loader");
                    using (FileStream fs = File.Create(dll)) st.CopyTo(fs);
                }
            }
            CoreWebView2Environment.SetLoaderDllFolderPath(dir);
        }
    }

    static class ToolsSitePack
    {
        public const string SITE_HOST = "tools.local";
        public const string HOME_URL = "https://tools.local/index.html";
        static readonly string Stamp = "3top-88|20261127|v19.13";

        public static string EnsureSite(Action<int> progress)
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniToolbox", "工具站");
            string dir = Path.Combine(baseDir, "3top-88");
            string marker = Path.Combine(dir, ".ready");
            if (File.Exists(marker) && File.Exists(Path.Combine(dir, "index.html")))
            {
                try { if (File.ReadAllText(marker).Trim() == Stamp) return dir; } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:marker", dg); }
            }

            string tmp = dir + ".tmp";
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:tmpdel", dg); }
            Directory.CreateDirectory(tmp);

            int n = 0;
            Stream zs = OpenSiteZip(baseDir);
            try
            {
                using (ZipArchive za = new ZipArchive(zs, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in za.Entries)
                    {
                        string rel = entry.FullName.Replace('/', '\\');
                        string full = Path.GetFullPath(Path.Combine(tmp, rel));
                        if (!full.StartsWith(Path.GetFullPath(tmp), StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (entry.Name.Length == 0)
                        {
                            Directory.CreateDirectory(full);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        using (Stream input = entry.Open())
                        using (FileStream output = File.Create(full))
                            input.CopyTo(output);
                        n++;
                        if (progress != null && (n % 40 == 0)) progress(n);
                    }
                }
            }
            finally { try { zs.Dispose(); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:zipclose", dg); } }
            File.WriteAllText(Path.Combine(tmp, ".ready"), Stamp);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:olddel", dg); }
            Directory.Move(tmp, dir);
            if (progress != null) progress(n);
            return dir;
        }

        // v19.15：主程序不再内嵌 22.7MB 工具站；优先用同目录离线包，没有就按需下载。
        const string SiteAssetUrl = "https://wj5666619.github.io/MiniToolbox/ToolSite.zip";
        static Stream OpenSiteZip(string cacheDir)
        {
            try
            {
                Stream emb = Assembly.GetExecutingAssembly().GetManifestResourceStream("SiteZip");
                if (emb != null) return emb;
            }
            catch (Exception dg) { Diag.Swallow("ToolsSite.cs:embed", dg); }

            string appDir = Path.GetDirectoryName(Application.ExecutablePath);
            string[] local = new string[] {
                Path.Combine(appDir, "工具站.zip"),
                Path.Combine(appDir, "ToolSite.zip"),
                Path.Combine(appDir, "site.zip")
            };
            for (int i = 0; i < local.Length; i++)
            {
                try { if (File.Exists(local[i]) && new FileInfo(local[i]).Length > 1048576) return File.OpenRead(local[i]); }
                catch (Exception dg) { Diag.Swallow("ToolsSite.cs:localzip", dg); }
            }

            Directory.CreateDirectory(cacheDir);
            string cache = Path.Combine(cacheDir, "ToolSite.zip");
            if (!(File.Exists(cache) && new FileInfo(cache).Length > 1048576))
            {
                string part = cache + ".part";
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                try
                {
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(0xF00 | 0xC00); } catch { }
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(SiteAssetUrl);
                    req.UserAgent = "MiniToolbox";
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 60000;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream rs = resp.GetResponseStream())
                    using (FileStream fs = File.Create(part))
                        rs.CopyTo(fs);
                    if (new FileInfo(part).Length < 1048576) throw new Exception("下载到的离线包不完整");
                    if (File.Exists(cache)) File.Delete(cache);
                    File.Move(part, cache);
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(part)) File.Delete(part); } catch { }
                    throw new Exception("工具站离线包未安装，自动下载失败：" + ex.Message
                        + "\r\n可手动下载 ToolSite.zip 放到程序目录后重试。");
                }
            }
            return File.OpenRead(cache);
        }
    }

    class ToolsSiteForm : Form
    {
        const int W = 1180, H = 880, TOP = 58;
        static readonly Color BG = UiKit.A(13, 11, 9);
        static readonly Color HEAD1 = UiKit.A(46, 35, 22);
        static readonly Color HEAD2 = UiKit.A(20, 16, 12);
        static readonly Color TEXT = UiKit.A(244, 236, 222);
        static readonly Color SUB = UiKit.A(167, 150, 128);
        static readonly Color AMBER = UiKit.A(245, 158, 11);

        WebView2 web;
        Panel top, loading;
        Label titleLbl, subLbl, loadTxt;
        Panel backBtn, retryBtn;
        ToolTip tips;
        bool ready = false;

        public ToolsSiteForm()
        {
            WebView2Boot.Init();
            Text = "工具站 · 88 个工具";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, H);
            BackColor = BG;
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            BuildTop();
            BuildWeb();
            BuildLoading();

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.F5 && ready) web.CoreWebView2.Reload();
                else if (e.Alt && e.KeyCode == Keys.Left && ready && web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack();
            };
            Shown += async delegate { await StartSite(); };
        }

        void BuildTop()
        {
            top = new Panel();
            top.Location = Point.Empty;
            top.Size = new Size(W, TOP);
            top.BackColor = BG;
            top.Paint += delegate(object s, PaintEventArgs e)
            {
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush br = new LinearGradientBrush(top.ClientRectangle, HEAD1, HEAD2, 0F))
                        e.Graphics.FillRectangle(br, top.ClientRectangle);
                    using (Pen p = new Pen(UiKit.AA(80, 245, 158, 11), 1F))
                        e.Graphics.DrawLine(p, 0, TOP - 1, W, TOP - 1);
                    Rectangle lr = new Rectangle(22, 11, 36, 36);
                    using (GraphicsPath lp = UiKit.RoundedPath(lr, 10))
                    using (LinearGradientBrush lb = new LinearGradientBrush(lr, UiKit.A(245, 158, 11), UiKit.A(180, 83, 9), 90F))
                        e.Graphics.FillPath(lb, lp);
                    TextRenderer.DrawText(e.Graphics, "88", new Font(Ui2.FamilyName, 11F, FontStyle.Bold), lr,
                        Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                catch (Exception dg) { Diag.Swallow("ToolsSite.cs:top", dg); }
            };
            Controls.Add(top);

            titleLbl = new Label();
            titleLbl.Text = "工具站";
            titleLbl.ForeColor = TEXT;
            titleLbl.BackColor = Color.Transparent;
            titleLbl.Font = new Font(Ui2.FamilyName, 13F, FontStyle.Bold);
            titleLbl.Location = new Point(72, 8);
            titleLbl.AutoSize = true;
            top.Controls.Add(titleLbl);

            subLbl = new Label();
            subLbl.Text = "88 个开源小工具 · 全部离线运行 · 不上传文件";
            subLbl.ForeColor = SUB;
            subLbl.BackColor = Color.Transparent;
            subLbl.Font = new Font(Ui2.FamilyName, 8F);
            subLbl.Location = new Point(74, 33);
            subLbl.Size = new Size(620, 16);
            subLbl.AutoEllipsis = true;
            top.Controls.Add(subLbl);

            tips = new ToolTip();
            tips.SetToolTip(titleLbl, "拖动标题栏可移动窗口");
            titleLbl.MouseDown += DragTop;
            subLbl.MouseDown += DragTop;
            top.MouseDown += DragTop;

            // 右侧按钮：在线版 / 刷新 / 后退 / 首页 / 关闭
            int x = W - 44;
            Panel close = MiniBtn("✕", "关闭", x, delegate { Close(); }); close.Name = "close"; x -= 40;
            MiniBtn("⌂", "回到工具站首页", x, delegate { GoHome(); }); x -= 40;
            backBtn = MiniBtn("←", "后退", x, delegate { GoBack(); }); x -= 40;
            MiniBtn("⟳", "刷新（F5）", x, delegate { Reload(); }); x -= 40;
            MiniBtn("↗", "用系统浏览器打开在线版", x, delegate
            {
                try { System.Diagnostics.Process.Start("https://3top.top/"); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:online", dg); }
            });
        }

        Panel MiniBtn(string glyph, string tip, int x, Action act)
        {
            Panel p = new Panel();
            p.Location = new Point(x, 11);
            p.Size = new Size(34, 34);
            p.Cursor = Cursors.Hand;
            p.BackColor = Color.Transparent;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, 33, 33);
                bool over = p.Tag != null;
                bool disabled = p.Tag == "off";
                Color fill = disabled ? UiKit.A(26, 21, 16) : (over ? UiKit.A(92, 63, 28) : UiKit.A(39, 31, 23));
                if (p.Name == "close" && over) fill = UiKit.A(160, 54, 62);
                using (GraphicsPath path = UiKit.RoundedPath(r, 8))
                using (SolidBrush b = new SolidBrush(fill)) e.Graphics.FillPath(b, path);
                TextRenderer.DrawText(e.Graphics, glyph, new Font("Segoe UI Symbol", 11F),
                    r, disabled ? UiKit.A(92, 78, 62) : TEXT,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            p.MouseEnter += delegate { if (p.Tag != "off") { p.Tag = "1"; p.Invalidate(); } };
            p.MouseLeave += delegate { if (p.Tag != "off") { p.Tag = null; p.Invalidate(); } };
            p.Click += delegate { if (p.Tag != "off") act(); };
            tips.SetToolTip(p, tip);
            top.Controls.Add(p);
            return p;
        }

        void DragTop(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        void BuildWeb()
        {
            web = new WebView2();
            web.Location = new Point(0, TOP);
            web.Size = new Size(W, H - TOP);
            web.BackColor = BG;
            web.DefaultBackgroundColor = Color.FromArgb(18, 16, 14);
            Controls.Add(web);
        }

        void BuildLoading()
        {
            loading = new Panel();
            loading.Location = new Point(0, TOP);
            loading.Size = new Size(W, H - TOP);
            loading.BackColor = UiKit.A(18, 16, 14);
            Controls.Add(loading);
            loading.BringToFront();

            Label mark = new Label();
            mark.Text = "🧰";
            mark.Font = new Font("Segoe UI Emoji", 34F);
            mark.ForeColor = AMBER;
            mark.BackColor = Color.Transparent;
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.Location = new Point(0, 250);
            mark.Size = new Size(W, 60);
            loading.Controls.Add(mark);

            loadTxt = new Label();
            loadTxt.Text = "正在准备 88 个离线工具…";
            loadTxt.ForeColor = TEXT;
            loadTxt.BackColor = Color.Transparent;
            loadTxt.Font = new Font(Ui2.FamilyName, 11F, FontStyle.Bold);
            loadTxt.TextAlign = ContentAlignment.MiddleCenter;
            loadTxt.Location = new Point(0, 320);
            loadTxt.Size = new Size(W, 26);
            loading.Controls.Add(loadTxt);

            Label hint = new Label();
            hint.Text = "首次打开会解压一次，约 45 MB；以后直接秒开。";
            hint.ForeColor = SUB;
            hint.BackColor = Color.Transparent;
            hint.Font = new Font(Ui2.FamilyName, 9F);
            hint.TextAlign = ContentAlignment.MiddleCenter;
            hint.Location = new Point(0, 352);
            hint.Size = new Size(W, 22);
            loading.Controls.Add(hint);

            ProgressBar bar = new ProgressBar();
            bar.Style = ProgressBarStyle.Marquee;
            bar.MarqueeAnimationSpeed = 28;
            bar.Location = new Point((W - 320) / 2, 396);
            bar.Size = new Size(320, 4);
            loading.Controls.Add(bar);

            retryBtn = new Panel();
            retryBtn.Location = new Point((W - 132) / 2, 448);
            retryBtn.Size = new Size(132, 38);
            retryBtn.Cursor = Cursors.Hand;
            retryBtn.Visible = false;
            retryBtn.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, 131, 37);
                using (GraphicsPath p = UiKit.RoundedPath(r, 10))
                using (LinearGradientBrush b = new LinearGradientBrush(r, UiKit.A(245, 158, 11), UiKit.A(180, 83, 9), 90F))
                    e.Graphics.FillPath(b, p);
                TextRenderer.DrawText(e.Graphics, "重试", new Font(Ui2.FamilyName, 10F, FontStyle.Bold), r,
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            retryBtn.Click += async delegate { retryBtn.Visible = false; await StartSite(); };
            loading.Controls.Add(retryBtn);
        }

        async Task StartSite()
        {
            try
            {
                loading.Visible = true;
                loading.BringToFront();
                loadTxt.Text = "正在准备 88 个离线工具…";
                retryBtn.Visible = false;

                string dir = await Task.Run(delegate
                {
                    return ToolsSitePack.EnsureSite(delegate(int n)
                    {
                        try
                        {
                            if (!IsDisposed)
                                BeginInvoke(new Action(delegate { loadTxt.Text = "正在解压工具站… " + n + " 个文件"; }));
                        }
                        catch (Exception dg) { Diag.Swallow("ToolsSite.cs:progress", dg); }
                    });
                });

                loadTxt.Text = "正在启动内置浏览器…";
                WebView2Boot.PrepareLoader();

                string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MiniToolbox", "工具站", "浏览器数据");
                Directory.CreateDirectory(dataDir);
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataDir, null);
                await web.EnsureCoreWebView2Async(env);

                CoreWebView2 core = web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                core.SetVirtualHostNameToFolderMapping(ToolsSitePack.SITE_HOST, dir, CoreWebView2HostResourceAccessKind.Allow);

                core.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs e)
                {
                    e.State = CoreWebView2PermissionState.Allow;
                };
                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    if (e.Uri != null && e.Uri.StartsWith("https://" + ToolsSitePack.SITE_HOST + "/"))
                        core.Navigate(e.Uri);
                    else
                    {
                        try { System.Diagnostics.Process.Start(e.Uri); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:new-window", dg); }
                    }
                };
                core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.IsSuccess)
                    {
                        ready = true;
                        loading.Visible = false;
                    }
                    else
                    {
                        ready = false;
                        loadTxt.Text = "工具站加载失败（" + e.WebErrorStatus + "），请重试。";
                        retryBtn.Visible = true;
                        loading.Visible = true;
                        loading.BringToFront();
                    }
                };
                core.DocumentTitleChanged += delegate
                {
                    try
                    {
                        string t = core.DocumentTitle;
                        if (t == null || t.Trim().Length == 0) return;
                        t = t.Trim();
                        // 站点自身标题常见两种写法：“xxx - 工具站”“xxx · 工具站”，统一去掉后缀
                        if (t.EndsWith("工具站", StringComparison.Ordinal) && t.Length > 3)
                        {
                            string pre = t.Substring(0, t.Length - 3).Trim().TrimEnd('-', '·', '|', ' ');
                            if (pre.Length > 0) t = pre;
                        }
                        subLbl.Text = (t == "工具站") ? "88 个开源小工具 · 全部离线运行 · 不上传文件" : ("工具站 / " + t);
                        titleLbl.Text = (t == "工具站") ? "工具站" : (t + " · 工具站");
                    }
                    catch (Exception dg) { Diag.Swallow("ToolsSite.cs:title", dg); }
                };
                core.SourceChanged += delegate { UpdateBack(); };

                core.Navigate(ToolsSitePack.HOME_URL);
                try { Audit.Write("打开工具站", "88 个离线工具 | " + dir); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:audit", dg); }
            }
            catch (Exception ex)
            {
                ready = false;
                loadTxt.Text = "工具站启动失败：" + ex.Message;
                retryBtn.Visible = true;
                loading.Visible = true;
                loading.BringToFront();
                Diag.Warn("工具站启动失败：" + ex);
            }
        }

        void UpdateBack()
        {
            try
            {
                if (ready && web.CoreWebView2.CanGoBack) { backBtn.Tag = null; backBtn.Invalidate(); }
                else { backBtn.Tag = "off"; backBtn.Invalidate(); }
            }
            catch (Exception dg) { Diag.Swallow("ToolsSite.cs:backstate", dg); }
        }

        void GoHome()
        {
            try { if (ready) web.CoreWebView2.Navigate(ToolsSitePack.HOME_URL); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:home", dg); }
        }

        void GoBack()
        {
            try { if (ready && web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack(); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:back", dg); }
        }

        void Reload()
        {
            try { if (ready) web.CoreWebView2.Reload(); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:reload", dg); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (web != null) web.Dispose(); } catch (Exception dg) { Diag.Swallow("ToolsSite.cs:dispose", dg); }
            base.OnFormClosing(e);
        }
    }
}
