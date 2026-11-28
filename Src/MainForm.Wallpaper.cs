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
    // v19.8：从 MainForm.cs 原样迁出（壁纸与动效），仅为拆分文件，逻辑未改
    partial class MainForm
    {
        // ===== v17.9 壁纸：静态图 / 程序化动效 / 导入型动态壁纸 =====
        string BgDynDir() { return Path.Combine(BASE_DIR, "壁纸缓存", "动态壁纸"); }

        // 扫描帧序列并挂到 PagePanel 上（不重启即时生效）
        void ApplyDynWall(bool resetPhase)
        {
            try
            {
                PagePanel.DynFrames = null;
                if (!bgDyn) return;   // 没开动态壁纸就不去读帧序列，避免旧的缓存把静态图顶掉
                string d = BgDynDir();
                if (!Directory.Exists(d)) return;
                string[] fs = Directory.GetFiles(d, "*.jpg");
                if (fs.Length < 2) return;
                Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
                PagePanel.DynFrames = fs;
                double fp = 12;
                try
                {
                    string ip = Path.Combine(d, "fps.txt");
                    if (File.Exists(ip))
                    {
                        double v2 = 0;
                        if (double.TryParse(File.ReadAllText(ip).Trim(), out v2)) fp = v2;
                    }
                }
                catch (Exception dg14533_1) { Diag.Swallow("MainForm.Wallpaper.cs:52", dg14533_1); }
                if (fp < 5) fp = 5;
                if (fp > 30) fp = 30;
                PagePanel.DynFps = fp;
                if (resetPhase) PagePanel.DynTime = 0;
            }
            catch { PagePanel.DynFrames = null; }
        }

        int CurrentWallMode()
        {
            if (bgDyn) return 5;
            if (bgOn) return 1;
            if (bgStyle >= 1 && bgStyle <= 3) return bgStyle + 1;
            return 0;
        }

        void RefreshWallpaperVisual()
        {
            PagePanel.Wall = bgImage;
            PagePanel.WallOn = (bgOn && bgImage != null);
            PagePanel.WallStyle = bgStyle;
            PagePanel.WallAlpha = bgAlpha;
            ApplyDynWall(false);
            StartWallAnim();
            try
            {
                foreach (Control c in content.Controls) if (c is PagePanel) c.Invalidate();
                content.Invalidate();
                mainArea.Invalidate();
                Invalidate(true);
            }
            catch (Exception dg14565_1) { Diag.Swallow("MainForm.Wallpaper.cs:84", dg14565_1); }
        }

        void ApplyWallMode(int mode)
        {
            try
            {
                if (mode == 0)
                {
                    bgOn = false; bgStyle = 0; bgDyn = false;
                }
                else if (mode == 1)
                {
                    if (!File.Exists(Path.Combine(BASE_DIR, "bg.jpg")))
                    {
                        if (!PickStaticWall()) return;
                    }
                    if (!File.Exists(Path.Combine(BASE_DIR, "bg.jpg"))) return;
                    if (bgImage == null) bgImage = SafeLoadImage(Path.Combine(BASE_DIR, "bg.jpg"));
                    bgOn = true; bgStyle = 0; bgDyn = false;
                }
                else if (mode >= 2 && mode <= 4)
                {
                    bgOn = false; bgStyle = mode - 1; bgDyn = false;
                }
                else if (mode == 5)
                {
                    ApplyDynWall(false);
                    if (PagePanel.DynFrames == null)
                    {
                        if (!PickDynWall()) return;
                    }
                    bgOn = false; bgStyle = 0; bgDyn = true;
                }
                SaveUiPrefs();
                RefreshWallpaperVisual();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[wall-apply] " + ex + "\r\n"); } catch (Exception dg14604_1) { Diag.Swallow("MainForm.Wallpaper.cs:123", dg14604_1); }
            }
        }

        bool PickStaticWall()
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = T("图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
                "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*");
            ofd.Title = T("选择背景图片", "Choose background image");
            if (ofd.ShowDialog() != DialogResult.OK) return false;
            return ApplyStaticImage(ofd.FileName);
        }

        bool PickDynWall()
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = T("动态壁纸|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.m4v;*.gif|视频|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.m4v|动图|*.gif|所有文件|*.*",
                "Animated|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.m4v;*.gif|Video|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.m4v|GIF|*.gif|All files|*.*");
            ofd.Title = T("选择动态壁纸（视频或 GIF）", "Choose animated wallpaper (video or GIF)");
            if (ofd.ShowDialog() != DialogResult.OK) return false;
            string ext = Path.GetExtension(ofd.FileName).ToLower();
            if (ext == ".gif") return ImportDynGifFile(ofd.FileName);
            return ImportDynVideoFile(ofd.FileName);
        }

        bool ApplyStaticImage(string path)
        {
            try
            {
                string dst = Path.Combine(BASE_DIR, "bg.jpg");
                using (Image src = SafeLoadImage(path))
                {
                    int mw = 1920, mh = 1080;
                    float s = Math.Min(1F, Math.Min((float)mw / src.Width, (float)mh / src.Height));
                    int w = Math.Max(1, (int)(src.Width * s));
                    int h = Math.Max(1, (int)(src.Height * s));
                    using (Bitmap bmp = new Bitmap(w, h))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(src, 0, 0, w, h);
                        }
                        bmp.Save(dst, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                }
                try { if (bgImage != null) bgImage.Dispose(); } catch (Exception dg14651_1) { Diag.Swallow("MainForm.Wallpaper.cs:170", dg14651_1); }
                bgImage = SafeLoadImage(dst);
                bgOn = true; bgStyle = 0; bgDyn = false;
                SaveUiPrefs();
                RefreshWallpaperVisual();
                return true;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[bg-pick] " + ex + "\r\n"); } catch (Exception dg14660_1) { Diag.Swallow("MainForm.Wallpaper.cs:179", dg14660_1); }
                MessageBox.Show(this, T("设置背景失败：" + ex.Message, "Failed to set background: " + ex.Message),
                    T("自定义背景", "Custom Background"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // GIF：逐帧取出来存成 jpg 序列，播放与视频完全同一条路径
        bool ImportDynGifFile(string path)
        {
            string d = BgDynDir();
            try
            {
                Directory.CreateDirectory(d);
                foreach (string old in Directory.GetFiles(d, "*.jpg")) { try { File.Delete(old); } catch (Exception dg14674_1) { Diag.Swallow("MainForm.Wallpaper.cs:193", dg14674_1); } }
                double fps = 10;
                int n = 1;
                using (Image img = SafeLoadImage(path))
                {
                    try
                    {
                        n = img.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    }
                    catch { n = 1; }
                    if (n > 240) n = 240;
                    if (n < 2)
                    {
                        return ApplyStaticImage(path);
                    }
                    try
                    {
                        var pi = img.GetPropertyItem(0x5100);
                        byte[] by = pi.Value;
                        int frames = by.Length / 4, total = 0;
                        for (int i = 0; i < frames; i++) total += by[i * 4] + (by[i * 4 + 1] << 8);
                        if (total > 0 && frames > 0) fps = 100.0 * frames / total;
                    }
                    catch (Exception dg14697_1) { Diag.Swallow("MainForm.Wallpaper.cs:216", dg14697_1); }
                    if (fps < 5) fps = 5;
                    if (fps > 30) fps = 30;
                    int mw = 1280;
                    float sc = Math.Min(1F, (float)mw / img.Width);
                    int w = Math.Max(1, (int)(img.Width * sc)), h = Math.Max(1, (int)(img.Height * sc));
                    for (int i = 0; i < n; i++)
                    {
                        try { img.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, i); } catch (Exception dg14705_1) { Diag.Swallow("MainForm.Wallpaper.cs:224", dg14705_1); }
                        using (Bitmap bmp = new Bitmap(w, h))
                        {
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(img, new Rectangle(0, 0, w, h));
                            }
                            bmp.Save(Path.Combine(d, "f" + i.ToString("D4") + ".jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
                        }
                        Application.DoEvents();
                    }
                }
                File.WriteAllText(Path.Combine(d, "fps.txt"), fps.ToString("0.#"), Encoding.UTF8);
                bgDyn = true; bgOn = false; bgStyle = 0;
                SaveUiPrefs();
                RefreshWallpaperVisual();
                return true;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[gif-import] " + ex + "\r\n"); } catch (Exception dg14726_1) { Diag.Swallow("MainForm.Wallpaper.cs:245", dg14726_1); }
                MessageBox.Show(this, T("导入 GIF 失败：" + ex.Message, "GIF import failed: " + ex.Message),
                    T("动态壁纸", "Animated wallpaper"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // 视频：用 ffmpeg 抽 12 秒 / 12fps / 1280 宽的 jpg 序列（播放端只保留当前一帧，内存稳定）
        bool ImportDynVideoFile(string path)
        {
            string ff = WmFfmpegPath();
            if (!File.Exists(ff))
            {
                try { UnpackResource("FfmpegZip", VideoToolDir(), "FFmpeg"); } catch (Exception dg14739_1) { Diag.Swallow("MainForm.Wallpaper.cs:258", dg14739_1); }
            }
            if (!File.Exists(ff))
            {
                MessageBox.Show(this, T("导入视频壁纸需要 FFmpeg，但本机组件缺失。请重新安装小工具箱后再试。",
                    "FFmpeg is required to import a video wallpaper."),
                    T("动态壁纸", "Animated wallpaper"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string d = BgDynDir();
            try
            {
                Directory.CreateDirectory(d);
                foreach (string old in Directory.GetFiles(d, "*.jpg")) { try { File.Delete(old); } catch (Exception dg14752_1) { Diag.Swallow("MainForm.Wallpaper.cs:271", dg14752_1); } }
            }
            catch (Exception dg14754_1) { Diag.Swallow("MainForm.Wallpaper.cs:273", dg14754_1); }
            string outPat = Path.Combine(d, "f%04d.jpg");
            string args = "-y -hide_banner -loglevel error -i \"" + path + "\" -t 12 -vf \"fps=12,scale=1280:-2\" -q:v 4 \"" + outPat + "\"";
            bool ok = false;
            Cursor prev = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ff, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardError = true;
                StringBuilder err = new StringBuilder();
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.ErrorDataReceived += delegate (object s3, DataReceivedEventArgs e3) { if (e3.Data != null) err.AppendLine(e3.Data); };
                    p.Start();
                    p.BeginErrorReadLine();
                    while (!p.HasExited)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(60);
                    }
                    ok = (p.ExitCode == 0);
                }
                if (!ok)
                {
                    try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[dyn-import] " + err.ToString() + "\r\n"); } catch (Exception dg14782_1) { Diag.Swallow("MainForm.Wallpaper.cs:301", dg14782_1); }
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[dyn-import] " + ex + "\r\n"); } catch (Exception dg14787_1) { Diag.Swallow("MainForm.Wallpaper.cs:306", dg14787_1); }
            }
            finally { Cursor = prev; }
            int cnt = Directory.GetFiles(d, "*.jpg").Length;
            if (cnt < 4)
            {
                MessageBox.Show(this, T("没有抽出可用的画面，这个视频可能太短或格式不受支持。",
                    "No frames were extracted; the video may be too short or unsupported."),
                    T("动态壁纸", "Animated wallpaper"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            File.WriteAllText(Path.Combine(d, "fps.txt"), "12", Encoding.UTF8);
            bgDyn = true; bgOn = false; bgStyle = 0;
            SaveUiPrefs();
            RefreshWallpaperVisual();
            return true;
        }

        void ShowBgDialog()
        {
            try
            {
                BgForm f = new BgForm();
                f.BaseDir = BASE_DIR;
                f.GetMode = new Func<int>(CurrentWallMode);
                f.ApplyMode2 = new Action<int>(ApplyWallMode);
                f.ImportStatic = new Func<bool>(delegate { return PickStaticWall(); });
                f.ImportDynamic = new Func<bool>(delegate { return PickDynWall(); });
                f.GetAlpha = new Func<int>(delegate { return bgAlpha; });
                f.ApplyAlpha2 = new Action<int>(delegate (int a) { SetBgAlpha(a); });
                f.ShowDialog(this);
                RefreshWallpaperVisual();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[bgform] " + ex + "\r\n"); } catch (Exception dg14822_1) { Diag.Swallow("MainForm.Wallpaper.cs:341", dg14822_1); }
            }
        }

        void ChooseBackground()
        {
            PickStaticWall();
        }

        void ClearBackground()
        {
            try { File.Delete(Path.Combine(BASE_DIR, "bg.jpg")); } catch (Exception dg14833_1) { Diag.Swallow("MainForm.Wallpaper.cs:352", dg14833_1); }
            try { if (bgImage != null) bgImage.Dispose(); } catch (Exception dg14834_1) { Diag.Swallow("MainForm.Wallpaper.cs:353", dg14834_1); }
            bgOn = false;
            bgImage = null;
            bgStyle = 0;
            bgDyn = false;
            SaveUiPrefs();
            RefreshWallpaperVisual();
        }

        // v17.9：动态壁纸开关（不重启，直接换画面）
        void SetBgStyle(int st)
        {
            bgStyle = Math.Max(0, Math.Min(3, st));
            if (bgStyle > 0) { bgOn = false; bgDyn = false; }
            PagePanel.WallStyle = bgStyle;
            PagePanel.WallOn = (bgOn && bgImage != null);
            PagePanel.WallT = 0;
            PagePanel.WallAlpha = bgAlpha;
            ApplyDynWall(false);
            SaveUiPrefs();
            StartWallAnim();
            Panel vp = VisiblePagePanel();
            if (vp != null) { vp.Invalidate(); }
        }
        void SetBgAlpha(int a)
        {
            bgAlpha = Math.Max(10, Math.Min(95, a));
            PagePanel.WallAlpha = bgAlpha;
            SaveUiPrefs();
            Panel vp = VisiblePagePanel();
            if (vp != null) { vp.Invalidate(); }
        }

        // 可见页（9 页里当前显示的那块 PagePanel）
        Panel VisiblePagePanel()
        {
            Panel[] all = new Panel[] { pageHome, pageTimer, pageClean, pageSam, pageVideo, pageMusic, pageWatermark, pageValue, pageSys };
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].Visible) return all[i];
            return null;
        }

        // 12 帧/秒推进相位；只在有动态壁纸且窗口可见时才跑，省 CPU
        void StartWallAnim()
        {
            PagePanel.WallStyle = bgStyle;
            PagePanel.WallAlpha = bgAlpha;
            bool hasDyn = (PagePanel.DynFrames != null && PagePanel.DynFrames.Length > 0);
            if (bgStyle <= 0 && !hasDyn)
            {
                if (wallTimer != null) { wallTimer.Stop(); }
                return;
            }
            if (wallTimer == null)
            {
                wallTimer = new System.Windows.Forms.Timer();
                wallTimer.Interval = 80;
                wallTimer.Tick += delegate
                {
                    try
                    {
                        bool hasDyn2 = (PagePanel.DynFrames != null && PagePanel.DynFrames.Length > 0);
                        if (PagePanel.WallStyle <= 0 && !hasDyn2) return;
                        if (Form.ActiveForm == null && !this.ContainsFocus) { }
                        PagePanel.WallT += 0.08;
                        PagePanel.DynTime += 0.08;
                        Panel p = VisiblePagePanel();
                        if (p != null && p.Width > 0) p.Invalidate();
                    }
                    catch (Exception dg14902_1) { Diag.Swallow("MainForm.Wallpaper.cs:421", dg14902_1); }
                };
            }
            wallTimer.Start();
        }

    }
}