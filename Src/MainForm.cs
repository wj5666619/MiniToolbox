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
    partial class MainForm : Form
    {
        // 状态
        TaskCfg cfg = new TaskCfg();
        bool taskActive = false;
        // v10.0：每次热键真的触发了记一笔时间，热键中心拿它显示"上次触发"
        Dictionary<string, DateTime> hkLastFire = new Dictionary<string, DateTime>();
        bool confirmShown = false;
        DateTime endTime;
        int totalSec = 0;
        string curAction = "关机";
        string curDesc = "";
        System.Windows.Forms.Timer uiTimer;
        ConfirmForm confirmWin;
        NotifyIcon trayIcon;
        ContextMenuStrip trayMenu;

        string BASE_DIR = Path.GetDirectoryName(Application.ExecutablePath);
        string logFile;

        // ===== 语言 =====
        // v17.7：语言由 Lang.Id 统一决定（中 / 英 / 法 / 俄 / 西），
        // langEn 只留给少量"是否按英文口径处理"的分支，不再是中英二态的开关。
        bool langEn = Lang.Id == Lang.EN;
        string T(string zh, string en) { return Lang.T(zh, en); }

        // 配色（仅黑色天蓝主题）
        Color C_BG { get { return UiKit.A(31, 35, 41); } }
        Color C_SIDE { get { return UiKit.A(9, 13, 19); } }
        Color C_MAIN { get { return UiKit.A(13, 19, 28); } }
        Color C_BUBBLE { get { return UiKit.A(19, 30, 42); } }
        Color C_BORDER { get { return UiKit.A(51, 59, 72); } }
        Color C_TEXT { get { return UiKit.Paper ? UiKit.Ink : UiKit.A(230, 233, 238); } }
        Color C_SUB { get { return UiKit.Paper ? Color.FromArgb(255, 58, 52, 43) : UiKit.A(138, 155, 176); } }
        Color C_ACCENT { get { return UiKit.A(61, 126, 255); } }
        Color C_ON { get { return UiKit.A(45, 212, 191); } }
        Color C_SEL { get { return UiKit.A(40, 50, 74); } }
        // 输入框/列表底色
        Color C_INPUT { get { return UiKit.A(38, 45, 58); } }
        Color C_INPUT_TEXT { get { return UiKit.Paper ? UiKit.Ink : Color.White; } }

        // 控件
        Panel sidebar, mainArea, content, header, statusbar;
        Label hName, hCode, hDesc;                 // v17.6：Header 右侧页面信息
        Label winMin, winClose;                    // v17.9：自绘的最小化 / 关闭
        // v19.6：窗口位置记忆 + 双击标题栏最大化
        bool maxed = false;
        Rectangle preMaxBounds = Rectangle.Empty;
        const string WIN_GEO_FILE = "窗口位置.txt";
        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        // v17.9：无边框窗口靠 Header 拖动；子控件会吞掉鼠标，所以同一个处理挂到它们身上
        void AttachWindowDrag(Control c)
        {
            if (c == null) return;
            c.MouseDown += delegate (object s0, MouseEventArgs e0)
            {
                if (e0.Button != MouseButtons.Left) return;
                try { ReleaseCapture(); SendMessage(this.Handle, 0xA1, 0x2, 0); } catch (Exception dg84_1) { Diag.Swallow("MainForm.cs:84", dg84_1); }
            };
        }
        // v19.6：双击标题栏最大化 / 还原。无边框窗口覆盖任务栏的坑用 WorkingArea 规避。
        void ToggleMax()
        {
            try
            {
                if (!maxed)
                {
                    preMaxBounds = Bounds;
                    Rectangle wa = Screen.FromControl(this).WorkingArea;
                    maxed = true;
                    Bounds = wa;
                }
                else
                {
                    maxed = false;
                    if (preMaxBounds.Width > 300 && preMaxBounds.Height > 200) Bounds = preMaxBounds;
                }
                SaveWindowGeometry();
                try { Sfx.Click(); } catch (Exception dg105_1) { Diag.Swallow("MainForm.cs:105", dg105_1); }
            }
            catch (Exception dg107_1) { Diag.Swallow("MainForm.cs:107", dg107_1); }
        }

        // v19.6：位置记忆（尺寸仍固定，只记 x/y 与是否最大化）
        void LoadWindowGeometry()
        {
            try
            {
                string p = Path.Combine(BASE_DIR, WIN_GEO_FILE);
                if (!File.Exists(p)) return;
                string[] ls = File.ReadAllLines(p, Encoding.UTF8);
                int x = int.MinValue, y = int.MinValue, mx = 0;
                foreach (string l in ls)
                {
                    int i = l.IndexOf('=');
                    if (i < 0) continue;
                    string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                    if (k == "x") int.TryParse(v, out x);
                    if (k == "y") int.TryParse(v, out y);
                    if (k == "max") int.TryParse(v, out mx);
                }
                if (x == int.MinValue || y == int.MinValue) return;
                Rectangle want;
                if (mx == 1 && x <= 0 && y <= 0)
                {
                    // 上次是最大化退出、没留正常位置：给一个居中的默认位置
                    Rectangle pwa = Screen.PrimaryScreen.WorkingArea;
                    want = new Rectangle(pwa.X + (pwa.Width - Width) / 2, pwa.Y + (pwa.Height - Height) / 2, Width, Height);
                }
                else want = new Rectangle(x, y, Width, Height);
                // 越界兜底：屏幕拔掉或分辨率变了，把窗口拉回主屏中央
                Rectangle wa = Screen.FromPoint(new Point(x + Width / 2, y + Height / 2)).WorkingArea;
                if (!wa.Contains(new Point(want.X + 20, want.Y + 10)))
                    want = new Rectangle(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2, Width, Height);
                preMaxBounds = want;
                Bounds = want;
                if (mx == 1)
                {
                    maxed = true;
                    Bounds = Screen.FromControl(this).WorkingArea;
                }
            }
            catch (Exception dg149_1) { Diag.Swallow("MainForm.cs:149", dg149_1); }
        }

        void SaveWindowGeometry()
        {
            try
            {
                Rectangle r = maxed && preMaxBounds.Width > 300 ? preMaxBounds : Bounds;
                if (maxed && preMaxBounds.Width <= 300)
                {
                    Rectangle pwa = Screen.PrimaryScreen.WorkingArea;
                    r = new Rectangle(pwa.X + (pwa.Width - Width) / 2, pwa.Y + (pwa.Height - Height) / 2, Width, Height);
                }
                File.WriteAllText(Path.Combine(BASE_DIR, WIN_GEO_FILE),
                    "x=" + r.X + "\r\ny=" + r.Y + "\r\nmax=" + (maxed ? 1 : 0), Encoding.UTF8);
            }
            catch (Exception dg165_1) { Diag.Swallow("MainForm.cs:165", dg165_1); }
        }

        // v19.6：二次启动唤醒后跳页（/go:home 之类）
        public void JumpToSlot(string slot)
        {
            try
            {
                if (slot == null) return;
                slot = slot.Trim().ToLowerInvariant();
                int idx = Array.IndexOf(SLOT, slot);
                if (idx < 0)
                {
                    for (int i = 0; i < ACTIONS.Length; i++)
                        if (ACTIONS[i] != null && ACTIONS[i].Trim() == slot) { idx = i; break; }
                }
                if (idx < 0) return;
                int fi = idx;
                Diag.Info("跳页请求：" + slot + " → " + ACTIONS[fi]);
                try { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; } catch (Exception dg184_1) { Diag.Swallow("MainForm.cs:184", dg184_1); }
                try { Show(); Activate(); } catch (Exception dg185_1) { Diag.Swallow("MainForm.cs:185", dg185_1); }
                try { BeginInvoke(new MethodInvoker(delegate { try { SelectAction(fi); } catch (Exception dg186_1) { Diag.Swallow("MainForm.cs:186", dg186_1); } })); } catch (Exception dg186_2) { Diag.Swallow("MainForm.cs:186", dg186_2); }
            }
            catch (Exception dg188_1) { Diag.Swallow("MainForm.cs:188", dg188_1); }
        }
        Panel tCard1, tCard2, tCard3, tQuick;       // v17.9：定时页四张卡（宽度/右列跟随窗口自适应）
        bool shellLaid = false;                     // v17.9：LayoutShell 跑过一次后，页面尺寸才是真的
        Button hbNews, hbBg, hbCmd, hbCenter;
        Control hbStart1, hbCancel1, hbImm1, hbEn;
        Button btnCond;
        Label sbSlogan1, sbSlogan2;                // v17.6：Header 右侧三个胶囊按钮
        Label[] convName = new Label[16];
        Panel[] convItem = new Panel[16];
        int sidebarSel = 0;

        // ===== v18.2 顺滑度：切页交叉淡出 / 侧栏悬停渐入 / 页面记忆 / 快捷键 =====
        int prevSelIdx = -1;
        float[] navHovT;
        bool[] navHovTarget;
        System.Windows.Forms.Timer navHovTimer;
        string lastPageSlot;
        ToolTip navTip;
        Label lblPeer, lblStatus, lblDetail, lblTime, lblTimeSub, lblTyping, lblService, lblAbout;
        Panel ringPanel;
        // v9.1：侧栏位置指示条（一条在全栏滑动的竖条）+ 倒计时盘的呼吸动画
        double navY = 85.0;          // 指示针当前的纵向坐标（相对侧栏）
        double navTargetY = 85.0;    // 目标坐标
        System.Windows.Forms.Timer navSlideTimer;
        System.Windows.Forms.Timer breathTimer;
        double breathPh;
        int lastTickBeep = 99;
        // v9.2：快捷预设按钮登记表（点过的那一颗留光环）
        List<RoundBtn> pstBtns = new List<RoundBtn>();
        CheckBox chkConfirm, chkSound, chkAutorun, chkTray;
        NumSpin numH, numM, numS, numConfirm;
        RadioButton rbCountdown, rbDatetime, rbDaily, rbWeekly;
        DateTimePicker dtpTime, dtpDaily, dtpWeekly;
        CheckBox[] weekCb = new CheckBox[7];
        Panel pCountdown, pDatetime, pDaily, pWeekly;
        Button btnStart, btnCancel, btnImm, btnLog;

        Panel pageTimer, pageReset, pageClean, pageSam, pageVideo, pageMusic, pageWatermark, pageValue, pageSys;
        Panel pageHome;   // v14.2 工作台首页

        // ===== v7.9 投递舱：侧栏入口 + 全窗拖放 + 投递遮罩 =====
        Panel dropPort, dropMask;
        bool dropHot, dropDragOn, dropTapPlayed;
        string dropPreview = "";
        int dropPhase;
        System.Windows.Forms.Timer dropPulseTimer, dropHideTimer;

        // ===== C盘清理：空间可视化 =====
        PictureBox pnlTree;
        Label lblDiskCap, lblDiskSub, lblBigCap, lblBigSub, lblJunkCap, lblJunkSub, lblCleanScan;
        long lastCleanScanBytes;
        int lastCleanScanFiles;
        ListView lvCleanBig;
        List<CleanBlk> cleanRaw = new List<CleanBlk>();
        List<CleanBlk> cleanBlks = new List<CleanBlk>();
        List<string[]> cleanBigRows = new List<string[]>();
        long cleanDiskTotal = 0, cleanDiskUsed = 0, cleanBigTotal = 0;
        long cleanFileCount = 0, cleanFileBytes = 0;
        bool cleanScanning = false, cleanStopFlag = false;
        string taskCleanScan = "", taskVideoQueue = "";
        bool cleanAutoTest = false;
        int cleanHoverBlk = -1;
        Rectangle cleanLayoutRc = Rectangle.Empty;

        // ===== 自定义背景 =====
        bool bgOn = false;
        Image bgImage = null;
        int bgStyle = 0;        // v17.9：0 关 / 1 极光 / 2 星尘 / 3 流光
        int bgAlpha = 55;       // v17.9：背景强度（透明度，0-100）
        bool bgDyn = false;     // v17.9：是否使用导入的动态壁纸（视频 / GIF 抽帧）
        System.Windows.Forms.Timer wallTimer = null;

        // ===== 嵌入外部组件 =====
        Process procSam = null;
        IntPtr samGameHwnd = IntPtr.Zero;   // SAM.Game 嵌入窗口句柄（供一键解锁使用）
        bool samTestMode = false;           // 自测模式：演练解锁但跳过「提交修改」，不写入 Steam
        Button btnUnlockDirect = null;      // 「一键全解锁（直接）」按钮，跑批时实时显示进度
        Panel samHost = null;
        TextBox samConsole = null;           // 一体式控制台：报告全文实时镜像（替代原来的汇总弹窗）
        Panel samBarTrack = null;            // 一体式进度条底槽
        Panel samBarFill = null;             // 一体式进度条填充
        Label lblSamState;
        bool samGamePending = false;

        // ===== 视频下载页 =====
        TextBox txtVUrl, txtVProxy;
        Button btnVParse, btnVDl, btnVProxyAuto;
        ComboBox cmbVFormat;
        CheckBox vAudioOnly;     // v12.0：仅音频（mp3）开关
        // v13.2：本页压缩条（下载后可直接按目标体积压小）
        RoundCheck vCmpOn;
        ComboBox cmbVCmpMb;
        Button btnVCmp;
        Label lblVCmp;
        bool vCmpBusy = false;
        Label lblVInfo, lblVDir, lblVTip;
        ListView lvVideos;
        string vOutDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "下载");
        Process vDlProc = null;
        bool vBusy = false;
        List<string> vFmtIds = new List<string>();
        List<bool> vFmtMerge = new List<bool>();

        // ===== 音乐下载页 =====
        TextBox txtMSearch;
        Button btnMSearch, btnMDl, btnMUrl, btnMOpen;
        Button btnPlat0, btnPlat1, btnPlat2;
        int mPlat = 0;                                  // 0 网易云 1 酷狗 2 两个平台一起搜
        ListView lvSongs;
        ProgressBar pbMusic;
        Label lblMStatus;
        List<object[]> mSongs = new List<object[]>();   // id, name, artist, album, duration, platform, otherUrl
        bool mBusy = false;

        // ===== 去水印页 =====
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);
        PictureBox wmPreview;
        Button btnWmPick, btnWmRun, btnWmOut, btnWmOpen, btnWmClear, btnWmAddArea, btnWmBatch;
        TextBox txtWmX, txtWmY, txtWmW, txtWmH;
        ComboBox cmbWmMode;
        Label lblWmFile, lblWmStatus, lblWmOut;
        ListView lvWm;
        string wmSrcPath = "";
        string wmOutDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "去水印");
        Rectangle wmSel = Rectangle.Empty;        // 图像像素坐标中的水印区域
        List<Rectangle> wmSels = new List<Rectangle>();   // 多区域：已固化的水印区域
        Label lblWmAreaTitle;
        TextBox txtWmStart, txtWmDur;
        Point wmDragStart = Point.Empty;
        bool wmDragging = false;
        bool wmBoxLock = false;                   // 防止输入框与选区互相触发
        Bitmap wmPreviewBmp = null;
        bool wmImageMode = false;
        int wmImgW = 0, wmImgH = 0;
        bool wmBusy = false;

        // ===== Steam 账号价值页 =====
        Label lblValGames, lblValWorth, lblValAvg, lblValSize, lblValStatus, lblValPath;
        TextBox lblValDetail;
        Button btnValScan;
        Button btnValReal;
        bool vRealBusy = false;
        ListView lvValue;
        bool valBusy = false;
        bool valAutoTest = false;

        // ===== 电脑配置页 =====
        ListView lvSys;
        Label lblSysStatus;
        Button btnSysScan, btnSysCopy;

        // ===== 电脑配置：实时负载 =====
        Panel[] sysBars = new Panel[3];
        Label[] sysVals = new Label[3];
        Label[] sysSubs = new Label[3];
        double[] sysPct = new double[4];
        Label lblSysGpu;

        // 显卡实时读数：直连 NVIDIA 驱动自带 nvml.dll（零第三方依赖、无需管理员权限）
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlInit_v2();
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetCount_v2(ref uint count);
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetHandleByIndex_v2(uint index, ref IntPtr dev);
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetTemperature(IntPtr dev, uint sensor, ref uint val);
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetPowerUsage(IntPtr dev, ref uint mw);
        [System.Runtime.InteropServices.DllImport("nvml.dll")]
        static extern int nvmlDeviceGetMemoryInfo(IntPtr dev, ref NvMemInfo mem);
        struct NvMemInfo { public ulong Total; public ulong Free; public ulong Used; }
        bool sysNvInit = false;
        bool sysNvFail = false;
        IntPtr sysNvHandle = IntPtr.Zero;
        uint sysGpuTemp = 0;
        uint sysGpuPower = 0;
        long sysGpuMemUsed = 0;
        long sysGpuMemTotal = 0;
        System.Windows.Forms.Timer sysLoadTimer;
        ulong sysPrevIdle = 0, sysPrevKernel = 0, sysPrevUser = 0;
        bool sysPrevInited = false;
        long sysMemUsed = 0, sysMemTotal = 0, sysDiskFree = 0, sysDiskTotal = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out ulong lpIdle, out ulong lpKernel, out ulong lpUser);

        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
        bool sysAutoTest = false;
        // v4：侧栏顺序由 SLOT 决定，颜色/页面按 key 查找，重排顺序不再需要改索引
        string[] SLOT = { "home", "power", "clean", "wm", "val", "sys", "video", "music", "sam" };
        string[] ACTIONS_ZH = { "工作台", "关机", "C盘清理", "去水印", "Steam价值", "电脑配置", "视频下载", "音乐下载", "Steam成就" };
        string[] ACTIONS_EN = { "Dashboard", "Shutdown", "Disk Clean", "Watermark", "Steam Value", "System", "Video", "Music", "Achievements" };
        string[] ACTION_SUB_ZH = { "状态一览 · 常用入口", "关闭计算机", "清理系统缓存垃圾", "视频 / 图片去水印", "账号资产估价", "硬件配置一览", "全网视频一键下载", "网易云曲库在线下载", "全库一键解锁成就" };
        string[] ACTION_SUB_EN = { "Overview & shortcuts", "Shut down computer", "Clean system cache & junk", "Remove watermark from video/image", "Account value estimate", "Hardware specs", "Download video from 1000+ sites", "NetEase music download", "Unlock whole library in one click" };
        string[] ACTIONS_FR = { "Tableau de bord", "Arrêt", "Nettoyage C:", "Filigrane", "Valeur Steam", "Configuration", "Vidéos", "Musique", "Succès Steam" };
        string[] ACTIONS_RU = { "Панель", "Завершение", "Очистка C:", "Водяной знак", "Цена Steam", "Конфигурация", "Видео", "Музыка", "Достижения Steam" };
        string[] ACTIONS_ES = { "Panel", "Apagar", "Limpieza C:", "Marca de agua", "Valor Steam", "Hardware", "Vídeos", "Música", "Logros Steam" };
        string[] ACTIONS
        {
            get
            {
                switch (Lang.Id)
                {
                    case Lang.FR: return ACTIONS_FR;
                    case Lang.RU: return ACTIONS_RU;
                    case Lang.ES: return ACTIONS_ES;
                    case Lang.EN: return ACTIONS_EN;
                    default: return ACTIONS_ZH;
                }
            }
        }
        string[] ACTION_SUB_FR = { "Aperçu et accès", "Éteindre le PC", "Nettoyer caches", "Retirer le filigrane", "Estimation du compte", "Aperçu matériel", "Vidéos, tous sites", "Musique NetEase", "Tous les succès" };
        string[] ACTION_SUB_RU = { "Обзор и ярлыки", "Выключить ПК", "Кэш и мусор", "Убрать водяной знак", "Оценка аккаунта", "Обзор железа", "Видео с любых сайтов", "Музыка NetEase", "Все достижения" };
        string[] ACTION_SUB_ES = { "Resumen y accesos", "Apagar el equipo", "Caché y basura", "Quitar marca de agua", "Valoración de cuenta", "Resumen hardware", "Vídeos, todo sitio", "Música de NetEase", "Todos los logros" };
        string[] ACTION_SUB
        {
            get
            {
                switch (Lang.Id)
                {
                    case Lang.FR: return ACTION_SUB_FR;
                    case Lang.RU: return ACTION_SUB_RU;
                    case Lang.ES: return ACTION_SUB_ES;
                    case Lang.EN: return ACTION_SUB_EN;
                    default: return ACTION_SUB_ZH;
                }
            }
        }
        // v14.3：侧栏分组小标题。组标题画在 NAV_GROUP_AT 指定的索引项之前（Common / Tools / System）
        // v17.7：侧栏只有 108px 文字宽，法/俄/西的长名放不下，这里给一套侧栏专用短名 + 短副名。
        // 首页磁贴、页面标题仍用 ACTIONS / ACTION_SUB 的完整名，不受影响。
        string[] NavShort()
        {
            switch (Lang.Id)
            {
                case Lang.EN: return new string[] { "Dashboard", "Scheduler", "Disk Clean", "Watermark", "Steam Value", "System", "Video", "Music", "Achievements" };
                case Lang.FR: return new string[] { "Accueil", "Minuterie", "Nettoyage", "Filigrane", "Valeur", "Matériel", "Vidéos", "Musique", "Succès" };
                case Lang.RU: return new string[] { "Панель", "Таймер", "Очистка", "Знак", "Цена", "Железо", "Видео", "Музыка", "Достиж." };
                case Lang.ES: return new string[] { "Panel", "Temporizador", "Limpieza", "Marca", "Valor", "Hardware", "Vídeos", "Música", "Logros" };
                default: return new string[] { "工作台", "定时关机", "C盘清理", "去水印", "Steam价值", "电脑配置", "视频下载", "音乐下载", "Steam成就" };
            }
        }

        string[] NavSubShort()
        {
            switch (Lang.Id)
            {
                case Lang.EN: return new string[] { "Overview", "Timer", "Caches", "Media", "Value", "Specs", "Sites", "NetEase", "Games" };
                case Lang.FR: return new string[] { "Aperçu", "Éteindre", "Caches", "Média", "Estimation", "Spécs", "Sites", "NetEase", "Jeux" };
                case Lang.RU: return new string[] { "Обзор", "Выключение", "Кэш", "Медиа", "Оценка", "Железо", "Сайты", "NetEase", "Игры" };
                case Lang.ES: return new string[] { "Resumen", "Apagar", "Caché", "Multimedia", "Valor", "Equipo", "Sitios", "NetEase", "Juegos" };
                default: return new string[] { "状态一览", "到点提醒", "清理缓存垃圾", "视频 / 图片", "账号资产估价", "硬件一览", "全网视频", "网易云曲库", "全库解锁" };
            }
        }

        string[] NAV_GROUP_ZH = { "常用", "工具", "系统" };
        string[] NAV_GROUP_EN = { "CORE", "TOOLS", "SYSTEM" };
        string[] NAV_GROUP_FR = { "ESSENTIEL", "OUTILS", "SYSTÈME" };
        string[] NAV_GROUP_RU = { "ОСНОВНОЕ", "ИНСТРУМЕНТЫ", "СИСТЕМА" };
        string[] NAV_GROUP_ES = { "ESENCIAL", "HERRAMIENTAS", "SISTEMA" };
        string[] NAV_GROUP
        {
            get
            {
                switch (Lang.Id)
                {
                    case Lang.FR: return NAV_GROUP_FR;
                    case Lang.RU: return NAV_GROUP_RU;
                    case Lang.ES: return NAV_GROUP_ES;
                    case Lang.EN: return NAV_GROUP_EN;
                    default: return NAV_GROUP_ZH;
                }
            }
        }
        int[] NAV_GROUP_AT = { 0, 3, 8 };   // 常用 0-2 / 工具 3-7 / 系统 8-9
        // v19.5：九项功能统一到冷色系（青绿 → 青 → 蓝 → 灰蓝），只靠深浅与色相微差区分，
        // 不再是红黄粉紫大杂烩；主色取青绿 (45,212,191)，与 Header / 主按钮同一支。
        Color[] ACTION_COLOR = {
            UiKit.A(94, 234, 212),   // home  mint
            UiKit.A(56, 189, 248),   // power  sky
            UiKit.A(45, 212, 191),   // clean  teal
            UiKit.A(125, 211, 252),  // wm     light-sky
            UiKit.A(96, 165, 250),   // val    blue
            UiKit.A(148, 163, 184),  // sys    slate
            UiKit.A(34, 211, 238),   // video  cyan
            UiKit.A(20, 184, 166),   // music  emerald
            UiKit.A(129, 140, 248)   // sam    indigo
        };
        Color ActCol(string k)
        {
            int i = Array.IndexOf(SLOT, k);
            return (i >= 0 && i < ACTION_COLOR.Length) ? ACTION_COLOR[i] : C_TEXT;
        }
        Color curActionColor;

        public MainForm()
        {
            logFile = Path.Combine(BASE_DIR, "shutdown_history.log");
            // v10.0：主题色必须在 InitUI 之前定下来，否则界面是用上一套色相建的
            try { UiKit.LoadTheme(BASE_DIR); } catch (Exception dg503_1) { Diag.Swallow("MainForm.cs:503", dg503_1); }
            try { UiKit.LoadSkin(BASE_DIR); } catch (Exception dg504_1) { Diag.Swallow("MainForm.cs:504", dg504_1); }
            LoadUiPrefs();
            curActionColor = ACTION_COLOR[0];
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception dg507_1) { Diag.Swallow("MainForm.cs:507", dg507_1); }
            InitUI();
            // v8.1：窗口一建完先补一遍皮肤。启动过程中生成的深层控件（卡片内的说明字、提示字）
            // 第一遍巡检可能还没轮到，这里在上屏前统一收口，字号与可读性都在此刻定型。
            this.Shown += delegate
            {
                Ui2.Polish(this);
            };
            // v14.2：上屏后默认落在工作台首页（装饰层由 VisibleChanged 自动补上）
            // v18.2：记住上次停在哪一页；键盘 Alt+1…9 / Ctrl+Tab 也能直接换页
            this.KeyPreview = true;
            this.KeyDown += HandleNavKeys;
            this.Shown += delegate
            {
                try
                {
                    int pi0 = (lastPageSlot == null) ? -1 : Array.IndexOf(SLOT, lastPageSlot);
                    if (pi0 < 0 || pi0 >= ACTIONS.Length) pi0 = Array.IndexOf(SLOT, "home");
                    if (pageHome != null && pi0 >= 0) SelectAction(pi0);
                }
                catch (Exception dg524_1) { Diag.Swallow("MainForm.cs:524", dg524_1); }
            };
            // v19.6：记住上次停的位置（尺寸固定，只记 x/y 与最大化状态）
            this.Shown += delegate { LoadWindowGeometry(); };
            this.FormClosing += delegate { SaveWindowGeometry(); };
            // v19.6：Esc 逐级退出 —— 先关指令舱，再回工作台
            this.KeyDown += delegate (object sEsc, KeyEventArgs eEsc)
            {
                if (eEsc.KeyCode != Keys.Escape) return;
                try
                {
                    foreach (Form of in Application.OpenForms)
                    {
                        if (of != this && of.Visible && of is PaletteForm)
                        {
                            eEsc.Handled = true;
                            try { of.Close(); } catch (Exception dg540_1) { Diag.Swallow("MainForm.cs:540", dg540_1); }
                            return;
                        }
                    }
                }
                catch (Exception dg545_1) { Diag.Swallow("MainForm.cs:545", dg545_1); }
                if (sidebarSel != 0)
                {
                    eEsc.Handled = true;
                    try { SelectAction(0); } catch (Exception dg549_1) { Diag.Swallow("MainForm.cs:549", dg549_1); }
                }
            };
            FlattenCheckBacks(this);
            HookPageAnim();
            InitTray();
            InitReminders();
            // v10.0：开机 6 秒后托盘点一下"今日一句"，不打断启动，也不弹模态窗
            this.Shown += delegate
            {
                System.Windows.Forms.Timer qtTimer = new System.Windows.Forms.Timer();
                qtTimer.Interval = 6000;
                qtTimer.Tick += delegate
                {
                    qtTimer.Stop();
                    qtTimer.Dispose();
                    try
                    {
                        if (File.Exists(Path.Combine(BASE_DIR, "每日一句关.txt"))) return;
                        trayIcon.ShowBalloonTip(4200, T("今日一句", "Today's line"), TodayQuote(), ToolTipIcon.Info);
                    }
                    catch (Exception dg570_1) { Diag.Swallow("MainForm.cs:570", dg570_1); }
                };
                qtTimer.Start();
            };
            // /tingtest：自检通道，自动开「一起听」房间并起播测试音频
            try
            {
                string[] av = Environment.GetCommandLineArgs();
                for (int i = 1; i < av.Length; i++)
                {
                    if ((av[i] ?? "").Trim().ToLowerInvariant() == "/tingtest")
                    {
                        TingForm tf = new TingForm();
                        tf.Show();
                        tf.TestHost();
                    }
                }
            }
            catch (Exception dg588_1) { Diag.Swallow("MainForm.cs:588", dg588_1); }
            InitState();
            // v10.0：用量统计与动作链配置（纯文件，缺了就当默认）
            try { UsageStats.Init(BASE_DIR); } catch (Exception dg591_1) { Diag.Swallow("MainForm.cs:591", dg591_1); }
            try { CleanHistory.Init(BASE_DIR); } catch (Exception dg592_1) { Diag.Swallow("MainForm.cs:592", dg592_1); }
            try { TaskHub.Init(BASE_DIR); } catch (Exception dg592a_1) { Diag.Swallow("MainForm.cs:592a", dg592a_1); }
            try { ChainCfg.Init(BASE_DIR); } catch (Exception dg593_1) { Diag.Swallow("MainForm.cs:593", dg593_1); }
            try { FileIndex.Init(BASE_DIR); } catch (Exception dg594_1) { Diag.Swallow("MainForm.cs:594", dg594_1); }
            try { TriggerCfg.Init(BASE_DIR); } catch (Exception dg595_1) { Diag.Swallow("MainForm.cs:595", dg595_1); }
            try { PluginCfg.Init(BASE_DIR); } catch (Exception dg596_1) { Diag.Swallow("MainForm.cs:596", dg596_1); }
            // v12.4：本地 AI 助手目录（可选模型）与插件仓库索引
            try { AiLocal.Init(BASE_DIR); } catch (Exception dg598_1) { Diag.Swallow("MainForm.cs:598", dg598_1); }
                    try { AiPhrase.Init(BASE_DIR); } catch (Exception dg599_1) { Diag.Swallow("MainForm.cs:599", dg599_1); }
                    try { AiVoice.Probe(); } catch (Exception dg600_1) { Diag.Swallow("MainForm.cs:600", dg600_1); }
            try { PluginStore.Init(BASE_DIR); } catch (Exception dg601_1) { Diag.Swallow("MainForm.cs:601", dg601_1); }
            try { StartTriggerEngine(); } catch (Exception dg602_1) { Diag.Swallow("MainForm.cs:602", dg602_1); }
            cfg = Config.Load(BASE_DIR);
            ApplyConfigToUI();
            TryRestoreTask();
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 300;
            uiTimer.Tick += (s, e) => { Tick(); EmbedTick(); };
            uiTimer.Start();
            // v9.1：呼吸光环的驱动。只在倒计时盘可见且没有任务时重画，空闲时约 2.4 秒一次起伏。
            breathTimer = new System.Windows.Forms.Timer();
            breathTimer.Interval = 45;
            breathTimer.Tick += delegate
            {
                breathPh += 0.09;
                if (breathPh > 6280.0) breathPh = 0;
                try { if (!taskActive && ringPanel != null && ringPanel.Visible && ringPanel.IsHandleCreated) ringPanel.Invalidate(); }
                catch (Exception dg618_1) { Diag.Swallow("MainForm.cs:618", dg618_1); }
            };
            breathTimer.Start();
            // 自测/调试通道：/sel=sam 自动切入对应页面；/unlocktest 演练解锁但不提交（不写入 Steam）
            try
            {
                string[] a = Environment.GetCommandLineArgs();
                bool wantTest = false, wantBatch = false;
                bool wantDirect = false;
                if (a != null)
                {
                    for (int i = 1; i < a.Length; i++)
                    {
                        string arg = a[i];
                        if (arg == "/unlocktest") { samTestMode = true; wantTest = true; }
                        else if (arg == "/uidump")
                        {
                            this.Shown += delegate
                            {
                                this.BeginInvoke((Action)delegate { DumpUiFile(); });
                            };
                        }
                        else if (arg == "/unlockbatch") { wantBatch = true; }
                        else if (arg == "/sampshot")
                    {
                        this.Shown += delegate { this.BeginInvoke((Action)delegate { SamShotFile(); }); };
                    }
                    else if (arg == "/samdump")
                        {
                            this.Shown += delegate
                            {
                                this.BeginInvoke((Action)delegate
                                {
                                    SelectAction(Array.IndexOf(SLOT, "sam"));
                                    DumpSamUiFile();
                                });
                            };
                        }
                        else if (arg == "/directbatch") { wantBatch = true; wantDirect = true; }
                        else if (arg != null && arg.StartsWith("/pcttest"))
                        {
                            samPctTest = true;
                            samScanLimit = 5;
                            this.Shown += delegate
                            {
                                this.BeginInvoke((Action)delegate
                                {
                                    SelectAction(Array.IndexOf(SLOT, "sam"));
                                    SamCompletionScan();
                                });
                            };
                        }
                        else if (arg != null && arg.StartsWith("/directapp="))
                        {
                            int v0 = 0;
                            if (int.TryParse(arg.Substring(11), out v0) && v0 > 0) { samOnlyAppId = v0; wantBatch = true; wantDirect = true; }
                        }
                        else if (arg != null && arg.StartsWith("/unlockapp="))
                        {
                            int v = 0;
                            if (int.TryParse(arg.Substring(11), out v) && v > 0) { samOnlyAppId = v; wantBatch = true; }
                        }
                        else if (arg != null && arg.StartsWith("/sel="))
                        {
                            string sel = arg.Substring(5);
                            this.Shown += delegate
                            {
                                if (sel == "sam") SelectAction(Array.IndexOf(SLOT, "sam"));
                                else if (sel == "wm") SelectAction(Array.IndexOf(SLOT, "wm"));
                                else if (sel == "val") SelectAction(Array.IndexOf(SLOT, "val"));
                                else if (sel == "sys") SelectAction(Array.IndexOf(SLOT, "sys"));
                                else if (sel.StartsWith("idx"))
                                {
                                    int si = -1;
                                    if (int.TryParse(sel.Substring(3), out si) && si >= 0 && si < SLOT.Length) SelectAction(si);
                                }
                                else
                                {
                                    int si2 = Array.IndexOf(SLOT, sel);
                                    if (si2 >= 0) SelectAction(si2);
                                }
                            };
                        }
                        else if (arg != null && arg.StartsWith("/cleantest"))
                        {
                            this.Shown += delegate
                            {
                                try
                                {
                                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_clean_start.txt"), "shown " + DateTime.Now.ToString("HH:mm:ss.fff"), Encoding.UTF8); } catch (Exception dg707_1) { Diag.Swallow("MainForm.cs:707", dg707_1); }
                                    this.Text = "小工具箱 - CLEANTEST";
                                    cleanAutoTest = true;
                                    SelectAction(Array.IndexOf(SLOT, "clean"));
                                    CleanScanStart();
                                }
                                catch (Exception ex)
                                {
                                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_clean_err.txt"), ex.ToString(), Encoding.UTF8); } catch (Exception dg715_1) { Diag.Swallow("MainForm.cs:715", dg715_1); }
                                }
                            };
                        }
                        else if (arg != null && arg.StartsWith("/xfertest"))
                        {
                            this.Shown += delegate { XferSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/remotetest"))
                        {
                            this.Shown += delegate { RemoteSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/fstest"))
                        {
                            this.Shown += delegate { FileSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/aitest"))
                        {
                            this.Shown += delegate { AiSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/power"))
                        {
                            keepFormAfterGrab = true;
                            this.Shown += delegate { ShowPlusCenter(); };
                        }
                        else if (arg != null && arg.StartsWith("/timer"))
                        {
                            // v17.9：调试通道 —— 启动后直接停在定时页，便于抓图核对布局
                            keepFormAfterGrab = true;
                            this.Shown += delegate { try { SelectAction(Array.IndexOf(SLOT, "power")); } catch (Exception dg744_1) { Diag.Swallow("MainForm.cs:744", dg744_1); } };
                        }
                        else if (arg != null && arg.StartsWith("/plugtest"))
                        {
                            this.Shown += delegate { PluginSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/phrasetest"))
                        {
                            this.Shown += delegate { PhraseSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/voicetest"))
                        {
                            this.Shown += delegate { VoiceSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/mactest"))
                        {
                            this.Shown += delegate { MacroPhraseSelfTest(); };
                        }
                        else if (arg != null && (arg == "/clipdbg" || arg == "/clipdbg2"))
                        {
                            clipDiagOn = true;
                            keepFormAfterGrab = true;
                            this.Shown += delegate { ShowClipHist(); };
                        }
                        else if (arg != null && arg.StartsWith("/bootscan"))
                        {
                            keepFormAfterGrab = true;
                            this.Shown += delegate
                            {
                                System.Windows.Forms.Timer bt = new System.Windows.Forms.Timer();
                                bt.Interval = 1600;
                                bt.Tick += delegate
                                {
                                    bt.Stop();
                                    try
                                    {
                                        StringBuilder br = new StringBuilder();
                                        GrabAnyForm("_bootscan.png", "启动项体检 · 小工具箱", br);
                                        File.WriteAllText(Path.Combine(BASE_DIR, "_bootscan.txt"), br.ToString(), new UTF8Encoding(false));
                                    }
                                    catch (Exception dg784_1) { Diag.Swallow("MainForm.cs:784", dg784_1); }
                                    Application.Exit();
                                };
                                bt.Start();
                                try { ShowStartupCheckup(CollectStartupItems()); } catch (Exception dg788_1) { Diag.Swallow("MainForm.cs:788", dg788_1); }
                            };
                        }
                        else if (arg != null && arg.StartsWith("/imgtest"))
                        {
                            keepFormAfterGrab = true;
                            this.Shown += delegate { ShowClipHist(); };
                        }
                        else if (arg != null && arg.StartsWith("/boottest"))
                        {
                            this.Shown += delegate { BootSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/checkupdumptest"))
                        {
                            checkupAutoDump = true;
                            this.Shown += delegate
                            {
                                try { ShowCheckup(); }
                                catch (Exception ex)
                                {
                                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_checkup_test.txt"), "EX " + ex, Encoding.UTF8); } catch (Exception dg808_1) { Diag.Swallow("MainForm.cs:808", dg808_1); }
                                }
                                Application.Exit();
                            };
                        }
                        else if (arg != null && arg.StartsWith("/bootuidumptest"))
                        {
                            bootAutoDump = true;
                            this.Shown += delegate
                            {
                                try { ShowStartupManager(); }
                                catch (Exception ex)
                                {
                                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_boot_ui_test.txt"), "EX " + ex, Encoding.UTF8); } catch (Exception dg821_1) { Diag.Swallow("MainForm.cs:821", dg821_1); }
                                }
                                Application.Exit();
                            };
                        }
                        else if (arg != null && arg.StartsWith("/boardtest"))
                        {
                            this.Shown += delegate
                            {
                                DeskBoard bd = new DeskBoard();
                                bd.Show();
                                System.Threading.Thread th = new System.Threading.Thread(delegate ()
                                {
                                    System.Threading.Thread.Sleep(4200);
                                    string dump = "n/a";
                                    try { dump = bd.DumpText(); } catch (Exception ex) { dump = "EX " + ex.Message; }
                                    try { dump = dump + "\r\n" + bd.DumpExtra(); } catch (Exception ex) { dump = dump + "\r\nEX " + ex.Message; }
                                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_board_test.txt"), dump, Encoding.UTF8); } catch (Exception dg838_1) { Diag.Swallow("MainForm.cs:838", dg838_1); }
                                    try { bd.Invoke(new MethodInvoker(delegate { bd.Close(); })); } catch (Exception dg839_1) { Diag.Swallow("MainForm.cs:839", dg839_1); }
                                    System.Threading.Thread.Sleep(300);
                                    Application.Exit();
                                });
                                th.IsBackground = true;
                                th.Start();
                            };
                        }
                        else if (arg != null && arg.StartsWith("/hktest"))
                        {
                            this.Shown += delegate { HkSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/cliptest"))
                        {
                            this.Shown += delegate { HistSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/boosttest"))
                        {
                            this.Shown += delegate { BoostSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/desktest68"))
                        {
                            this.Shown += delegate { DeskSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/vaulttest"))
                        {
                            this.Shown += delegate { VaultSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/duptest"))
                        {
                            this.Shown += delegate { DupSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v68dump"))
                        {
                            this.Shown += delegate { V68UiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/chiptest"))
                        {
                            this.Shown += delegate { HistSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/rectest"))
                        {
                            this.Shown += delegate { RecSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/bktest"))
                        {
                            this.Shown += delegate { BkSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/fctest"))
                        {
                            this.Shown += delegate { FcSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/anneditest"))
                        {
                            this.Shown += delegate { AnnSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v69dump"))
                        {
                            this.Shown += delegate { V69UiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/proshot"))
                        {
                            this.Shown += delegate { ProShot(); };
                        }
                        else if (arg != null && arg.StartsWith("/cmptest"))
                        {
                            this.Shown += delegate { CmpSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/palettetest"))
                        {
                            this.Shown += delegate { PalSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/hudtest"))
                        {
                            this.Shown += delegate { HudSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/batchtest"))
                        {
                            this.Shown += delegate { BatchSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/macrotest"))
                        {
                            this.Shown += delegate { MacroSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/pwbtest"))
                        {
                            this.Shown += delegate { PwbSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/privacytest"))
                        {
                            this.Shown += delegate { PrivacySelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v70dump"))
                        {
                            this.Shown += delegate { V70UiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/lancliptest"))
                        {
                            this.Shown += delegate { ClipSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/wmtest="))
                        {
                            string spec = arg.Substring(8);
                            this.Shown += delegate { WmAutoTest(spec); };
                        }
                        else if (arg != null && arg.StartsWith("/wmbatch="))
                        {
                            string spec2 = arg.Substring(9);
                            this.Shown += delegate { WmBatchAutoTest(spec2); };
                        }
                        else if (arg != null && arg.StartsWith("/winmgrtest"))
                        {
                            this.Shown += delegate { WinMgrSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/startuptest"))
                        {
                            this.Shown += delegate { StartupTaskSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/videoprot"))
                        {
                            this.Shown += delegate { VideoProSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/imgqrtest"))
                        {
                            this.Shown += delegate { ImgQrSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/stickytest"))
                        {
                            this.Shown += delegate { StickySelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v71dump"))
                        {
                            this.Shown += delegate { V71UiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/porttest"))
                        {
                            this.Shown += delegate { PortSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/recstest"))
                        {
                            this.Shown += delegate { ScreenRecSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/audiotest"))
                        {
                            this.Shown += delegate { AudioSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/proxytest"))
                        {
                            this.Shown += delegate { ProxySelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/wishshot"))
                        {
                            this.Shown += delegate { WishShot(); };
                        }
                        else if (arg != null && arg.StartsWith("/wishui"))
                        {
                            this.Shown += delegate { WishUiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/libcheck"))
                        {
                            this.Shown += delegate { LibCheckTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/wishlist"))
                        {
                            this.Shown += delegate { WishTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/uiaudit"))
                        {
                            this.Shown += delegate { UiAudit(); };
                        }
                        else if (arg != null && arg.StartsWith("/uitype"))
                        {
                            this.Shown += delegate { UiTypeSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v10self"))
                        {
                            this.Shown += delegate { V10SelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/sfxtest"))
                        {
                            this.Shown += delegate { SfxSelftest(); };
                        }
                        else if (arg != null && arg.StartsWith("/dropsim"))
                        {
                            this.Shown += delegate { DropSim(); };
                        }
                        else if (arg != null && arg.StartsWith("/dropshot"))
                        {
                            this.Shown += delegate { DropShot(); };
                        }
                        else if (arg != null && arg.StartsWith("/palshot"))
                        {
                            this.Shown += delegate { PalShot(); };
                        }
                        else if (arg != null && arg.StartsWith("/shotall"))
                        {
                            this.Shown += delegate { ShotAllPages(); };
                        }
                        else if (arg != null && arg.StartsWith("/pmarkshot"))
                        {
                            // v9.2：验证快捷预设的"选中光环"，命令行走一遍并把窗口拍下来
                            this.Shown += delegate { PmarkShot(); };
                        }
                        else if (arg != null && arg.StartsWith("/at"))
                        {
                            this.Shown += delegate { AtDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/updtest"))
                        {
                            this.Shown += delegate
                            {
                                UpdateForm.SelfTest(Path.Combine(BASE_DIR, "_updtest.txt"));
                                Application.Exit();
                            };
                        }
                        else if (arg != null && arg.StartsWith("/plus"))
                        {
                            this.Shown += delegate { ShowPlusCenter(); };
                        }
                        else if (arg != null && arg.StartsWith("/bg"))
                        {
                            this.Shown += delegate
                            {
                                try { ShowBgDialog(); }
                                catch (Exception ex) { try { File.WriteAllText(Path.Combine(BASE_DIR, "bgerr.txt"), ex.ToString()); } catch (Exception dg1063_1) { Diag.Swallow("MainForm.cs:1063", dg1063_1); } }
                            };
                        }
                        else if (arg != null && arg.StartsWith("/sfxtest"))
                        {
                            this.Shown += delegate
                            {
                                DumpTest("_sfx_test.txt", Sfx.SelfTest(BASE_DIR));
                                Application.Exit();
                            };
                        }
                        else if (arg != null && arg.StartsWith("/sfxpanel"))
                        {
                            this.Shown += delegate
                            {
                                try { ShowSfxPanel(); }
                                catch (Exception ex) { try { File.WriteAllText(Path.Combine(BASE_DIR, "sfxerr.txt"), ex.ToString()); } catch (Exception dg1079_1) { Diag.Swallow("MainForm.cs:1079", dg1079_1); } }
                            };
                        }
                        else if (arg != null && arg.StartsWith("/appearance"))
                        {
                            this.Shown += delegate
                            {
                                try { using (AppearanceForm af = new AppearanceForm()) af.ShowDialog(this); } catch (Exception dg1086_1) { Diag.Swallow("MainForm.cs:1086", dg1086_1); }
                            };
                        }
                        else if (arg != null && arg.StartsWith("/hit"))
                        {
                            this.Shown += delegate { HitScan(); };
                        }
                        else if (arg != null && arg.StartsWith("/adbtest"))
                        {
                            this.Shown += delegate { AdbSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/adbui"))
                        {
                            this.Shown += delegate { AdbUiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/wptest"))
                        {
                            this.Shown += delegate { WpSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v72dump"))
                        {
                            this.Shown += delegate { V72UiDump(); };
                        }
                        else if (arg != null && arg.StartsWith("/upnptest"))
                        {
                            this.Shown += delegate { UpnpSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/latencytest"))
                        {
                            this.Shown += delegate { LatencySelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/wifitest"))
                        {
                            this.Shown += delegate { WifiSelfTest(); };
                        }
                        else if (arg != null && arg.StartsWith("/v73dump"))
                        {
                            this.Shown += delegate { V73UiDump(); };
                        }
                        else if (arg == "/valtest")
                        {
                            this.Shown += delegate { ValAutoTestRun(); };
                        }
                        else if (arg == "/systest")
                        {
                            this.Shown += delegate { this.Text = "小工具箱 - SYSTEST"; SelectAction(Array.IndexOf(SLOT, "sys")); sysAutoTest = true; SysStartScan(); };
                        }
                        else if (arg == "/about")
                        {
                            this.Shown += delegate { ShowAbout(); };
                        }
                        else if (arg != null && arg.StartsWith("/upctest"))
                        {
                            this.Shown += delegate { DumpTest("_up_test.txt", UpdateForm.UpdProbe()); Application.Exit(); };
                        }
                        else if (arg == "/updatetest")
                        {
                            // 自更新全链路自测：自动检查、自动确认下载、自动替换重启
                            this.Shown += delegate
                            {
                                UpdateForm uf = new UpdateForm(langEn);
                                uf.autoRun = true;
                                uf.autoYes = true;
                                uf.Show(this);
                            };
                        }
                        else if (arg == "/condtest")
                        {
                            this.Shown += delegate
                            {
                                this.Text = "小工具箱 - CONDTEST";
                                SelectAction(Array.IndexOf(SLOT, "power"));
                                CondForm cf = new CondForm(langEn, RunAnyAction);
                                cf.Show(this);
                            };
                        }
                        else if (arg == "/v4check")
                        {
                            // 侧栏顺序回归自检：逐项切换并记录实际打开的页面
                            this.Shown += delegate
                            {
                                StringBuilder rep = new StringBuilder();
                                for (int si = 0; si < SLOT.Length; si++)
                                {
                                    SelectAction(si);
                                    Application.DoEvents();
                                    StringBuilder vis = new StringBuilder();
                                    if (pageTimer.Visible) vis.Append("timer ");
                                    if (pageReset != null && pageReset.Visible) vis.Append("reset ");
                                    if (pageClean.Visible) vis.Append("clean ");
                                    if (pageSam.Visible) vis.Append("sam ");
                                    if (pageVideo.Visible) vis.Append("video ");
                                    if (pageMusic.Visible) vis.Append("music ");
                                    if (pageWatermark.Visible) vis.Append("wm ");
                                    if (pageValue.Visible) vis.Append("val ");
                                    if (pageSys.Visible) vis.Append("sys ");
                                    rep.AppendLine(si + "  " + SLOT[si] + "  " + ACTIONS_ZH[si] + "  =>  " + vis.ToString().Trim());
                                }
                                try { File.WriteAllText(Path.Combine(BASE_DIR, "_v4_check.txt"), rep.ToString(), Encoding.UTF8); } catch (Exception dg1184_1) { Diag.Swallow("MainForm.cs:1184", dg1184_1); }
                                Application.Exit();
                            };
                        }
                    }
                }
                if (wantTest || wantBatch)
                {
                    System.Windows.Forms.Timer tt = new System.Windows.Forms.Timer();
                    tt.Interval = 3000;
                    int ticks = 0;
                    tt.Tick += delegate
                    {
                        try
                        {
                            ticks++;
                            if (ticks > 40) { tt.Stop(); SamSetState("等待游戏列表超时，已放弃", "Timeout waiting for the game list"); return; }
                            // 直启批量不需要 Picker 列表；旧 Picker 路径才等列表
                            if ((wantBatch && samHost != null) || FindPickerList() != IntPtr.Zero)
                            {
                                tt.Stop();
                                SamSetState(wantBatch ? "全库自动开跑…" : "【自测】游戏列表已就绪，开始全库检查…",
                                    wantBatch ? "Auto batch started" : "TEST: list ready, running batch");
                                if (wantDirect) DirectUnlockAll();
                                else UnlockAllSam();
                            }
                        }
                        catch (Exception dg1211_1) { Diag.Swallow("MainForm.cs:1211", dg1211_1); }
                    };
                    tt.Start();
                }
            }
            catch (Exception dg1216_1) { Diag.Swallow("MainForm.cs:1216", dg1216_1); }
        }

        Panel VisiblePage()
        {
            Panel[] list = new Panel[] { pageHome, pageTimer, pageReset, pageClean, pageSam,
                pageVideo, pageMusic, pageWatermark, pageValue, pageSys };
            foreach (Panel p in list) if (p != null && p.Visible) return p;
            return null;
        }

        // 侧栏悬停的渐入：目标值在鼠标进出时翻转，一个 16ms 计时器把各条目一起逼近目标
        void NudgeNavHover(int i)
        {
            try
            {
                if (navHovT == null) return;
                if (navHovTimer == null)
                {
                    navHovTimer = new System.Windows.Forms.Timer();
                    navHovTimer.Interval = 16;
                    navHovTimer.Tick += delegate
                    {
                        bool busy = false;
                        for (int k = 0; k < navHovT.Length; k++)
                        {
                            float tgt = navHovTarget[k] ? 1f : 0f;
                            if (navHovT[k] == tgt) continue;
                            navHovT[k] += (tgt - navHovT[k]) * 0.24f;
                            if (Math.Abs(navHovT[k] - tgt) < 0.02f) navHovT[k] = tgt;
                            else busy = true;
                            if (k < convItem.Length && convItem[k] != null) convItem[k].Invalidate();
                        }
                        if (!busy) navHovTimer.Stop();
                    };
                }
                if (!navHovTimer.Enabled) navHovTimer.Start();
            }
            catch (Exception dg1254_1) { Diag.Swallow("MainForm.cs:1254", dg1254_1); }
        }

        // ================= 界面偏好（语言） =================
        void LoadUiPrefs()
        {
            try
            {
                string p = Path.Combine(BASE_DIR, "ui.txt");
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "lang")
                    {
                        int li = 0;
                        if (int.TryParse(v, out li) && li >= Lang.ZH && li <= Lang.ES) Lang.Id = li;
                        langEn = (Lang.Id == Lang.EN);
                    }
                    if (k == "bg" && v == "1") bgOn = true;
                    if (k == "bgstyle") { int bs = 0; if (int.TryParse(v, out bs)) bgStyle = Math.Max(0, Math.Min(3, bs)); }
                    if (k == "bgalpha") { int ba = 55; if (int.TryParse(v, out ba)) bgAlpha = Math.Max(10, Math.Min(95, ba)); }
                    if (k == "bgdyn" && v == "1") bgDyn = true;
                    if (k == "page") lastPageSlot = v;
                }
                if (bgOn)
                {
                    try { bgImage = SafeLoadImage(Path.Combine(BASE_DIR, "bg.jpg")); }
                    catch { bgImage = null; bgOn = false; }
                }
                PagePanel.WallStyle = bgStyle;
                PagePanel.WallAlpha = bgAlpha;
                PagePanel.Wall = bgImage;
                PagePanel.WallOn = (bgOn && bgImage != null);
                ApplyDynWall(false);
            }
            catch (Exception dg1292_1) { Diag.Swallow("MainForm.cs:1292", dg1292_1); }
        }
        void SaveUiPrefs()
        {
            try
            {
                File.WriteAllText(Path.Combine(BASE_DIR, "ui.txt"),
                    "lang=" + Lang.Id + "\r\nbg=" + (bgOn ? 1 : 0)
                    + "\r\nbgstyle=" + bgStyle + "\r\nbgalpha=" + bgAlpha
                    + "\r\nbgdyn=" + (bgDyn ? 1 : 0)
                    + "\r\npage=" + ((sidebarSel >= 0 && sidebarSel < SLOT.Length) ? SLOT[sidebarSel] : "home"), Encoding.UTF8);
            }
            catch (Exception dg1304_1) { Diag.Swallow("MainForm.cs:1304", dg1304_1); }
        }
        void RestartApp()
        {
            try { Program.mutex.ReleaseMutex(); Program.mutex.Close(); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[restart-mutex] " + ex + "\r\n"); } catch (Exception dg1309_1) { Diag.Swallow("MainForm.cs:1309", dg1309_1); } }
            try { Process.Start(Application.ExecutablePath); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[restart-start] " + ex + "\r\n"); } catch (Exception dg1311_1) { Diag.Swallow("MainForm.cs:1311", dg1311_1); } }
            Application.Exit();
        }
        // v17.7：语言不再"点一下切一次"，改成点开菜单直接选 —— 选中即保存并自动重启，
        // 一次点击完成，不用先切过去再切回来。
        void SetLang(int id)
        {
            if (id < Lang.ZH || id > Lang.ES) return;
            if (id == Lang.Id) return;
            Lang.Id = id;
            langEn = (id == Lang.EN);
            SaveUiPrefs();
            RestartApp();
        }

        void ShowLangMenu(Control anchor)
        {
            try
            {
                var m = new ContextMenuStrip();
                m.BackColor = UiKit.A(28, 32, 39);
                m.ForeColor = UiKit.Txt(Color.White);
                m.ShowImageMargin = false;
                m.Padding = new Padding(6, 4, 6, 4);
                for (int i = 0; i < Lang.NAME.Length; i++)
                {
                    int id2 = i;
                    var it = new ToolStripMenuItem(Lang.NAME[i] + (i == Lang.Id ? "  ✓" : ""));
                    it.ForeColor = (i == Lang.Id) ? UiKit.A(45, 212, 191) : UiKit.Txt(Color.White);
                    it.Font = new Font("Microsoft YaHei UI", 10F, (i == Lang.Id) ? FontStyle.Bold : FontStyle.Regular);
                    it.Padding = new Padding(8, 5, 8, 5);
                    it.Click += (s2, e2) => { try { SetLang(id2); } catch (Exception dg1342_1) { Diag.Swallow("MainForm.cs:1342", dg1342_1); } };
                    m.Items.Add(it);
                }
                m.Items.Add(new ToolStripSeparator());
                var itInfo = new ToolStripMenuItem(T("切换到该语言后界面自动重启，约 1 秒", "Switches and restarts in about 1s"));
                itInfo.Enabled = false;
                itInfo.ForeColor = UiKit.A(140, 152, 170);
                m.Items.Add(itInfo);
                m.Show(anchor, new Point(0, anchor.Height + 4));
            }
            catch (Exception dg1352_1) { Diag.Swallow("MainForm.cs:1352", dg1352_1); }
        }

        // 液态玻璃（DWM 亚克力背景）
        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttribData { public int Attrib; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);
        void ApplyAcrylic(IntPtr hwnd)
        {
            try
            {
                AccentPolicy ap = new AccentPolicy();
                ap.AccentState = 4;   // ACCENT_ENABLE_ACRYLICBLURBEHIND
                ap.GradientColor = UiKit.AA(170, 26, 30, 37).ToArgb();
                WindowCompositionAttribData data = new WindowCompositionAttribData();
                data.Attrib = 19;     // WCA_ACCENT_POLICY
                data.Data = Marshal.AllocHGlobal(Marshal.SizeOf(ap));
                Marshal.StructureToPtr(ap, data.Data, false);
                data.SizeOfData = Marshal.SizeOf(ap);
                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(data.Data);
            }
            catch (Exception dg1377_1) { Diag.Swallow("MainForm.cs:1377", dg1377_1); }
        }

        // v19.8：4249 行已原样迁到 MainForm.Ui.cs（界面构建与页面切换），逻辑未改
        // ================= 托盘 =================
        void InitTray()
        {
            trayIcon = new NotifyIcon();
            try { trayIcon.Icon = this.Icon; } catch (Exception dg5633_1) { Diag.Swallow("MainForm.cs:1385", dg5633_1); }
            trayIcon.Text = "小工具箱";
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(T("显示主窗口", "Show Main Window"), null, (s, e) => ShowMain());
            trayMenu.Items.Add(T("控制中心…", "Control center…"), null, (s, e) => OpenControlCenter());
            trayMenu.Items.Add((langEn ? "Run " : "立即") + curAction, null, (s, e) => Immediate());
            ToolStripMenuItem miSfx = new ToolStripMenuItem(langEn ? "UI sounds" : "界面音效");
            miSfx.Checked = Sfx.On;
            miSfx.Click += delegate { Sfx.Toggle(); miSfx.Checked = Sfx.On; Sfx.Click(); };
            trayMenu.Items.Add(miSfx);
            ToolStripMenuItem miVol = new ToolStripMenuItem(langEn ? "Sound volume" : "音效音量");
            ToolStripMenuItem[] volItems = new ToolStripMenuItem[3];
            string[] volNames = new string[] { langEn ? "Soft" : "轻", langEn ? "Normal" : "中", langEn ? "Loud" : "响" };
            for (int vi = 0; vi < 3; vi++)
            {
                int vv = vi;
                ToolStripMenuItem mv = new ToolStripMenuItem(volNames[vi]);
                mv.Checked = (Sfx.Volume == vi);
                mv.Click += delegate
                {
                    Sfx.SetVolume(vv);
                    for (int k = 0; k < volItems.Length; k++) volItems[k].Checked = (k == vv);
                    Sfx.Click();
                };
                volItems[vi] = mv;
                miVol.DropDownItems.Add(mv);
            }
            trayMenu.Items.Add(miVol);
            trayMenu.Items.Add(T("音效设置…", "Sound settings…"), null, (s2, e2) => ShowSfxPanel());
            trayMenu.Items.Add(T("外观设置…", "Appearance…"), null, (s2, e2) => { using (AppearanceForm af = new AppearanceForm()) af.ShowDialog(this); });

            // ===== v10.0 新增：主题色 / 迷你倒计时 / 今日概览 / 智能口令 / 插件命令 =====
            ToolStripMenuItem miTheme = new ToolStripMenuItem(langEn ? "Theme color" : "主题色");
            ToolStripMenuItem[] thItems = new ToolStripMenuItem[UiKit.THEME_NAMES.Length];
            for (int ti = 0; ti < UiKit.THEME_NAMES.Length; ti++)
            {
                int ii = ti;
                ToolStripMenuItem mt = new ToolStripMenuItem(UiKit.THEME_NAMES[ti]);
                mt.Checked = (UiKit.ThemeIndex() == ti);
                mt.Click += delegate
                {
                    UiKit.ApplyTheme(ii, true);
                    for (int k = 0; k < thItems.Length; k++) thItems[k].Checked = (k == ii);
                    Sfx.Save();
                    if (MessageBox.Show(T("主题色已切换为「" + UiKit.THEME_NAMES[ii] + "」。\n整套强调色要重新着色一次才能生效，现在重启应用吗？",
                                          "Theme set to " + UiKit.THEME_NAMES[ii] + ". Restart the app now to repaint everything?"),
                        T("重启生效", "Restart"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                        RestartApp();
                };
                thItems[ti] = mt;
                miTheme.DropDownItems.Add(mt);
            }
            trayMenu.Items.Add(miTheme);

            // v18.4：四套界面风格，整站一起换（颜色 + 圆角比例），和主题色一样重启生效
            ToolStripMenuItem miSkin = new ToolStripMenuItem(langEn ? "Interface style" : "界面风格");
            ToolStripMenuItem[] skItems = new ToolStripMenuItem[UiKit.SKIN_NAMES.Length];
            for (int si = 0; si < UiKit.SKIN_NAMES.Length; si++)
            {
                int jj = si;
                ToolStripMenuItem ms = new ToolStripMenuItem(UiKit.SKIN_NAMES[si]);
                ms.Checked = (UiKit.SkinIdx == si);
                ms.Click += delegate
                {
                    UiKit.ApplySkin(jj, true);
                    for (int k = 0; k < skItems.Length; k++) skItems[k].Checked = (k == jj);
                    Sfx.Save();
                    if (MessageBox.Show(T("界面风格已切换为「" + UiKit.SKIN_NAMES[jj] + "」。\n整站配色与圆角要重新绘一遍才能生效，现在重启应用吗？",
                                          "Style set to " + UiKit.SKIN_NAMES[jj] + ". Restart the app now to repaint everything?"),
                        T("重启生效", "Restart"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                        RestartApp();
                };
                skItems[si] = ms;
                miSkin.DropDownItems.Add(ms);
            }
            trayMenu.Items.Add(miSkin);

            // v13.1：界面缩放（100% / 110% / 125%），和主题一样重启生效
            ToolStripMenuItem miZoom = new ToolStripMenuItem(langEn ? "UI scale" : "界面缩放");
            int[] zp = new int[] { 100, 110 };
            ToolStripMenuItem[] zi = new ToolStripMenuItem[zp.Length];
            for (int z = 0; z < zp.Length; z++)
            {
                int zz = z;
                ToolStripMenuItem mz = new ToolStripMenuItem(zp[z] + "%");
                mz.Checked = Math.Abs(Ui2.UiScale * 100f - zp[z]) < 1f;
                mz.Click += delegate
                {
                    Ui2.SaveUiScale(zp[zz]);
                    for (int k = 0; k < zi.Length; k++) zi[k].Checked = (k == zz);
                    Sfx.Save();
                    if (MessageBox.Show(T("界面缩放已设为 " + zp[zz] + "%。\n整窗尺寸与字号需要重新排一次才能生效，现在重启应用吗？",
                                          "UI scale set to " + zp[zz] + "%. Restart the app now?"),
                        T("重启生效", "Restart"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                        RestartApp();
                };
                zi[z] = mz;
                miZoom.DropDownItems.Add(mz);
            }
            trayMenu.Items.Add(miZoom);

            ToolStripMenuItem miMini = new ToolStripMenuItem(langEn ? "Mini countdown" : "迷你倒计时窗");
            miMini.Click += delegate { ToggleMini(); };
            trayMenu.Items.Add(miMini);

            trayMenu.Items.Add(T("今日概览", "Today"), null, (s3, e3) => ShowToday());
            trayMenu.Items.Add(T("智能口令…", "Smart command…"), null, (s3, e3) => ShowVoiceCmd());

            // 插件命令：插件\*.txt 里一行一条「名称|命令行」，丢文件进去菜单就多一项
            ToolStripMenuItem miPlug = new ToolStripMenuItem(langEn ? "Plugins" : "插件命令");
            try
            {
                string pdir = Path.Combine(BASE_DIR, "插件");
                if (Directory.Exists(pdir))
                {
                    string[] pf = Directory.GetFiles(pdir, "*.txt");
                    Array.Sort(pf);
                    int pc = 0;
                    foreach (string f1 in pf)
                    {
                        foreach (string ln in File.ReadAllLines(f1, Encoding.UTF8))
                        {
                            string[] kv = ln.Split('|');
                            if (kv.Length < 2) continue;
                            string nm = kv[0].Trim(), cm = kv[1].Trim();
                            if (nm.Length == 0 || cm.Length == 0) continue;
                            string cmd2 = cm;
                            miPlug.DropDownItems.Add(nm, null, (s4, e4) => RunPlugin(nm, cmd2));
                            pc++;
                        }
                    }
                    if (pc == 0) miPlug.DropDownItems.Add(T("（插件目录为空）", "(no plugins)"), null, (s4, e4) => OpenPluginFolder());
                    miPlug.DropDownItems.Add(new ToolStripSeparator());
                    miPlug.DropDownItems.Add(T("打开插件目录…", "Open plugins folder…"), null, (s4, e4) => OpenPluginFolder());
                }
                else
                {
                    miPlug.DropDownItems.Add(T("创建插件目录…", "Create plugins folder…"), null, (s4, e4) => OpenPluginFolder());
                }
            }
            catch (Exception dg5772_1) { Diag.Swallow("MainForm.cs:1524", dg5772_1); }
            trayMenu.Items.Add(miPlug);

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(T("全局搜索 (Ctrl+K)", "Global search (Ctrl+K)"), null, (s3, e3) => ShowGlobalSearch());
            trayMenu.Items.Add(T("视频批量队列…", "Video batch queue…"), null, (s3, e3) => ShowVideoQueue());
            trayMenu.Items.Add(T("手机遥控…", "Phone remote…"), null, (s3, e3) => ShowPhoneRemote());
            trayMenu.Items.Add(T("配置同步…", "Config sync…"), null, (s3, e3) => ShowSyncCfg());
            trayMenu.Items.Add(T("重启应用", "Restart app"), null, (s3, e3) => RestartApp());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(T("退出程序", "Exit"), null, (s, e) => ExitApp());
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => ShowMain();
            trayIcon.Visible = true;
        }

        // ===== v10.0 迷你倒计时窗 =====
        MiniForm miniWin;
        void ToggleMini()
        {
            if (miniWin != null && !miniWin.IsDisposed)
            {
                miniWin.Close();
                miniWin.Dispose();
                miniWin = null;
                return;
            }
            miniWin = new MiniForm(langEn, CalcNextDaily, delegate
            {
                string t2 = taskActive ? Fmt((int)Math.Max(0, (endTime - DateTime.Now).TotalSeconds)) : "--:--:--";
                string sub2 = taskActive ? curAction + " · " + (cfg.Periodic ? T("周期任务", "periodic") : T("倒计时", "countdown")) : T("空闲", "idle");
                return new string[] { t2, sub2 };
            },
            delegate { ShowMain(); },
            delegate { ShowMain(); ToggleMini(); });
            miniWin.Show(this);
            Sfx.Switch();
        }

        // ===== v10.0 每日一句 =====
        static readonly string[] QUOTES_ZH = new string[] {
            "把今天的十分钟，交给一件明天会感激的事。",
            "先做完，再做好；先小步，再远行。",
            "工具的意义不是让你更快地忙，而是让你有空停下来。",
            "一件一件来，电脑会等你，人也该等自己。",
            "定时关机的不是电脑，是那点舍不得睡的心。",
            "清理的是磁盘，理清的是脑子。",
            "把重复的事交给工具，把需要耐心的留给自己。",
            "今天少点一次熬夜，明天多一点从容。",
            "别和进度条较劲，它只是在替你说：还在走。",
            "慢工不一定出细活，但赶工一定出返工。",
            "会停的人，才跑得远。",
            "桌面收拾干净的那一刻，人也轻了一点。",
            "好习惯不是坚持 365 天，是今天先做一次。",
            "给自己设个到点提醒，比给自己立个毒誓有用。",
            "省下的每一分钟，都值得花在喜欢的事情上。",
        };
        static readonly string[] QUOTES_EN = new string[] {
            "Give today ten minutes to something tomorrow will thank you for.",
            "First finish, then polish; first a small step, then a long road.",
            "Tools should not make you busier faster — they should buy you pauses.",
            "One thing at a time. The PC can wait; so can you.",
            "A scheduled shutdown is less about the machine than about letting go.",
            "You clear the disk; the disk clears your head.",
            "Hand the repetitive work to tools, keep the patient work for yourself.",
            "One less late night today, a little more ease tomorrow.",
            "Don't fight the progress bar — it is just saying: still moving.",
            "Slow work is not always fine work, but rushing always means rework.",
            "Those who can stop are the ones who get far.",
            "The desk looks lighter, and so do you.",
            "A habit is not 365 days — it is doing it once today.",
            "A reminder at the right time beats a vow at the wrong one.",
            "Every minute saved is worth spending on something you love.",
        };

        string TodayQuote()
        {
            try
            {
                string qf = Path.Combine(BASE_DIR, "每日一句.txt");
                if (File.Exists(qf))
                {
                    string[] qs = File.ReadAllLines(qf, Encoding.UTF8);
                    int nq = 0;
                    for (int i = 0; i < qs.Length; i++) if (qs[i].Trim().Length > 0) nq++;
                    if (nq > 0)
                    {
                        int want = (DateTime.Now.DayOfYear - 1) % nq;
                        int seen = 0;
                        for (int i = 0; i < qs.Length; i++)
                        {
                            if (qs[i].Trim().Length == 0) continue;
                            if (seen == want) return qs[i].Trim();
                            seen++;
                        }
                    }
                }
            }
            catch (Exception dg5870_1) { Diag.Swallow("MainForm.cs:1622", dg5870_1); }
            int idx2 = (DateTime.Now.DayOfYear - 1) % QUOTES_ZH.Length;
            return langEn ? QUOTES_EN[idx2] : QUOTES_ZH[idx2];
        }

        // ===== v10.0 今日概览 =====
        void ShowToday()
        {
            Form f = new Form();
            f.Text = T("今日概览 · 小工具箱", "Today · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(640, 430);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("今日概览", "Today");
            ttl.ForeColor = UiKit.A(235, 242, 250);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label dt = new Label();
            dt.Text = DateTime.Now.ToString("yyyy-MM-dd") + " · " + T("第 ", "day ") + DateTime.Now.DayOfYear + T(" 天", "");
            dt.ForeColor = UiKit.A(140, 152, 168);
            dt.Font = new Font("Microsoft YaHei UI", 8.5F);
            dt.Location = new Point(20, 48);
            dt.AutoSize = true;
            f.Controls.Add(dt);

            int c1, m1, streak;
            int[] last7 = UsageStats.Snapshot(out c1, out m1, out streak);
            string[] caps = new string[] {
                T("今日任务", "Tasks today") + "  " + c1 + T(" 次", ""),
                T("今日定时", "Scheduled today") + "  " + m1 + T(" 分钟", " min"),
                T("连续使用", "Streak") + "  " + streak + T(" 天", " d"),
            };
            for (int i = 0; i < 3; i++)
            {
                Panel cd = new Panel();
                cd.BackColor = UiKit.A(26, 32, 41);
                cd.Location = new Point(20 + i * 202, 76);
                cd.Size = new Size(190, 66);
                UiKit.CardBorder(cd, 10, UiKit.ThemeColor);
                f.Controls.Add(cd);
                Label cl = new Label();
                cl.Text = caps[i];
                cl.ForeColor = UiKit.A(198, 210, 226);
                cl.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                cl.Location = new Point(14, 22);
                cl.AutoSize = true;
                cd.Controls.Add(cl);
            }

            Label bT = new Label();
            bT.Text = T("近 7 天定时时长（分钟）", "Last 7 days (minutes)");
            bT.ForeColor = UiKit.A(150, 162, 178);
            bT.Font = new Font("Microsoft YaHei UI", 8.5F);
            bT.Location = new Point(20, 156);
            bT.AutoSize = true;
            f.Controls.Add(bT);

            Panel chart = new Panel();
            chart.BackColor = UiKit.A(22, 27, 34);
            chart.Location = new Point(20, 180);
            chart.Size = new Size(596, 130);
            UiKit.CardBorder(chart, 12, UiKit.ThemeColor);
            int[] data = last7;
            chart.Paint += delegate (object s5, PaintEventArgs e5)
            {
                try
                {
                    Graphics g = e5.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    int mx = 1;
                    for (int i = 0; i < data.Length; i++) if (data[i] > mx) mx = data[i];
                    int bw = 44, gap = (chart.Width - 40 - bw * 7) / 6;
                    if (gap < 6) gap = 6;
                    Color ac = UiKit.ThemeColor;
                    // 基线：哪怕一根柱子都没有，这块也不该是空的
                    int baseY = chart.Height - 34;
                    using (Pen bl = new Pen(UiKit.AA(70, 120, 140, 170), 1f))
                        g.DrawLine(bl, 16, baseY, chart.Width - 16, baseY);
                    bool allZero = true;
                    for (int i = 0; i < data.Length; i++) if (data[i] > 0) allZero = false;
                    if (allZero)
                    {
                        using (Font fe = new Font("Microsoft YaHei UI", 9F))
                        using (SolidBrush se = new SolidBrush(UiKit.A(130, 142, 158)))
                        {
                            string em = T("还没有记录：跑一次定时任务，这里就开始长柱子",
                                          "No data yet: run one scheduled task and bars appear here");
                            SizeF esz = g.MeasureString(em, fe);
                            g.DrawString(em, fe, se, new PointF((chart.Width - esz.Width) / 2, (baseY - 40) / 2));
                        }
                    }
                    using (Font fb = new Font("Microsoft YaHei UI", 7.5F))
                    using (Font fv = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                    {
                        for (int i = 0; i < data.Length; i++)
                        {
                            int x = 20 + i * (bw + gap);
                            int hgt = data[i] <= 0 ? 3 : Math.Max(6, (int)((chart.Height - 56) * (data[i] / (double)mx)));
                            Rectangle br = new Rectangle(x, chart.Height - 34 - hgt, bw, hgt);
                            using (GraphicsPath gp = UiKit.RoundedPath(br, 6))
                            using (LinearGradientBrush lb = new LinearGradientBrush(br,
                                Color.FromArgb(210, ac), Color.FromArgb(90, ac), 90f))
                                g.FillPath(lb, gp);
                            string day = DateTime.Now.AddDays(-(6 - i)).ToString("MM/dd");
                            using (SolidBrush sa = new SolidBrush(UiKit.A(120, 132, 148)))
                                g.DrawString(day, fb, sa, new PointF(x, chart.Height - 26));
                            if (data[i] > 0)
                                using (SolidBrush sv = new SolidBrush(UiKit.A(198, 210, 226)))
                                    g.DrawString(data[i].ToString(), fv, sv, new PointF(x + 4, br.Y - 16));
                        }
                    }
                }
                catch (Exception dg5990_1) { Diag.Swallow("MainForm.cs:1742", dg5990_1); }
            };
            f.Controls.Add(chart);

            Label qt = new Label();
            qt.Text = T("今日一句", "Today's line");
            qt.ForeColor = UiKit.A(150, 162, 178);
            qt.Font = new Font("Microsoft YaHei UI", 8.5F);
            qt.Location = new Point(20, 322);
            qt.AutoSize = true;
            f.Controls.Add(qt);

            Label qv = new Label();
            qv.Text = "「" + TodayQuote() + "」";
            qv.ForeColor = UiKit.A(206, 218, 234);
            qv.Font = new Font("Microsoft YaHei UI", 10F);
            qv.Location = new Point(20, 344);
            qv.Size = new Size(596, 40);
            f.Controls.Add(qv);

            Label tip2 = new Label();
            tip2.Text = T("统计写在 用时统计.txt；把 每日一句.txt 里一行写一句，就能换成自己的文案。",
                "Stats live in 用时统计.txt; put one line per day in 每日一句.txt to use your own quotes.");
            tip2.ForeColor = UiKit.A(112, 124, 140);
            tip2.Font = new Font("Microsoft YaHei UI", 8F);
            tip2.Location = new Point(20, 390);
            tip2.Size = new Size(596, 20);
            f.Controls.Add(tip2);

            ShowOrTrack(f);
        }

        // ===== v10.0 动作链：设置窗 =====
        void ShowChainDlg()
        {
            Form f = new Form();
            f.Text = T("完成动作 · 小工具箱", "Action Chain · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(560, 430);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("到点时的收尾动作", "Chain before the action");
            ttl.ForeColor = UiKit.A(235, 242, 250);
            ttl.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("按顺序先做完这些，再把主任务（关机 / 重启 / 睡眠）交给系统。全部可单独关掉。",
                "Runs these first, then hands the main action to Windows. Each item is optional.");
            sub.ForeColor = UiKit.A(140, 152, 168);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(520, 34);
            f.Controls.Add(sub);

            CheckBox cMute = new RoundCheck();
            cMute.Text = T("静音到 0（先把声音收干净）", "Mute audio first");
            cMute.Checked = ChainCfg.Mute;
            cMute.ForeColor = UiKit.A(200, 212, 228);
            cMute.Location = new Point(22, 90);
            cMute.Size = new Size(330, 24);
            f.Controls.Add(cMute);

            CheckBox cScreen = new RoundCheck();
            cScreen.Text = T("先关屏（显示器先熄，再关机）", "Screen off first");
            cScreen.Checked = ChainCfg.ScreenOff;
            cScreen.ForeColor = UiKit.A(200, 212, 228);
            cScreen.Location = new Point(22, 120);
            cScreen.Size = new Size(360, 24);
            f.Controls.Add(cScreen);

            CheckBox cLock = new RoundCheck();
            cLock.Text = T("先锁屏（适合「睡眠」前收好桌面）", "Lock before sleep");
            cLock.Checked = ChainCfg.Lock;
            cLock.ForeColor = UiKit.A(200, 212, 228);
            cLock.Location = new Point(22, 150);
            cLock.Size = new Size(380, 24);
            f.Controls.Add(cLock);

            CheckBox cNotify = new RoundCheck();
            cNotify.Text = T("弹一条托盘通知（到点前留个念想）", "Tray notification first");
            cNotify.Checked = ChainCfg.Notify;
            cNotify.ForeColor = UiKit.A(200, 212, 228);
            cNotify.Location = new Point(22, 180);
            cNotify.Size = new Size(390, 24);
            f.Controls.Add(cNotify);

            CheckBox cKill = new RoundCheck();
            cKill.Text = T("结束指定进程：", "End process:");
            cKill.Checked = ChainCfg.Kill;
            cKill.ForeColor = UiKit.A(200, 212, 228);
            cKill.Location = new Point(22, 210);
            cKill.Size = new Size(150, 24);
            f.Controls.Add(cKill);

            TextBox tbName = new TextBox();
            tbName.Text = ChainCfg.KillName;
            tbName.BackColor = UiKit.A(28, 33, 41);
            tbName.ForeColor = UiKit.A(226, 234, 245);
            tbName.BorderStyle = BorderStyle.FixedSingle;
            tbName.Location = new Point(168, 210);
            tbName.Size = new Size(240, 24);
            f.Controls.Add(tbName);

            Label ln = new Label();
            ln.Text = T("（不带 .exe 也行，多个用逗号分开）", "(comma-separated, .exe optional)");
            ln.ForeColor = UiKit.A(120, 132, 148);
            ln.Font = new Font("Microsoft YaHei UI", 7.5F);
            ln.Location = new Point(168, 238);
            ln.AutoSize = true;
            f.Controls.Add(ln);

            CheckBox cTemp = new RoundCheck();
            cTemp.Text = T("清临时文件（%TEMP%，只删文件不删文件夹）", "Clear %TEMP% files");
            cTemp.Checked = ChainCfg.Temp;
            cTemp.ForeColor = UiKit.A(200, 212, 228);
            cTemp.Location = new Point(22, 266);
            cTemp.Size = new Size(420, 24);
            f.Controls.Add(cTemp);

            CheckBox cClip = new RoundCheck();
            cClip.Text = T("清空本程序的剪贴板历史", "Clear clipboard history");
            cClip.Checked = ChainCfg.Clip;
            cClip.ForeColor = UiKit.A(200, 212, 228);
            cClip.Location = new Point(22, 296);
            cClip.Size = new Size(420, 24);
            f.Controls.Add(cClip);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 162, 178);
            st.Font = new Font("Microsoft YaHei UI", 8F);
            st.Location = new Point(20, 336);
            st.Size = new Size(520, 20);
            f.Controls.Add(st);

            Button ok = MakeBtn(T("保存", "Save"), UiKit.A(43, 95, 208), Color.White, 20, 366, 120, 36);
            ok.Click += delegate
            {
                ChainCfg.Mute = cMute.Checked;
                ChainCfg.ScreenOff = cScreen.Checked;
                ChainCfg.Lock = cLock.Checked;
                ChainCfg.Notify = cNotify.Checked;
                ChainCfg.Kill = cKill.Checked;
                ChainCfg.KillName = tbName.Text.Trim();
                ChainCfg.Temp = cTemp.Checked;
                ChainCfg.Clip = cClip.Checked;
                ChainCfg.Save();
                Sfx.Save();
                st.Text = T("已保存到 动作链.txt", "Saved to 动作链.txt");
            };
            f.Controls.Add(ok);

            Button test = MakeBtn(T("试一遍", "Test"), C_BUBBLE_ALT(), Color.White, 152, 366, 110, 36);
            test.Click += delegate { RunChain(); st.Text = T("已按当前勾选执行了一遍", "Ran once with current settings"); };
            f.Controls.Add(test);

            Button close = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 274, 366, 110, 36);
            close.Click += delegate { f.Close(); };
            f.Controls.Add(close);

            ShowOrTrack(f);
        }

        // ===== v10.0 动作链：真正执行 =====
        void RunChain()
        {
            StringBuilder lg = new StringBuilder();
            if (ChainCfg.Mute) { try { MuteNow(); lg.Append("mute "); } catch (Exception dg6163_1) { Diag.Swallow("MainForm.cs:1915", dg6163_1); } }
            if (ChainCfg.Notify)
            {
                try { trayIcon.ShowBalloonTip(2600, "小工具箱", T("即将执行：" + curAction, "About to run: " + curAction), ToolTipIcon.Info); lg.Append("notify "); }
                catch (Exception dg6167_1) { Diag.Swallow("MainForm.cs:1919", dg6167_1); }
                System.Threading.Thread.Sleep(500);
            }
            if (ChainCfg.Kill && ChainCfg.KillName.Length > 0)
            {
                foreach (string nm in ChainCfg.KillName.Split(','))
                {
                    string t2 = nm.Trim();
                    if (t2.Length == 0) continue;
                    if (t2.ToLowerInvariant().EndsWith(".exe")) t2 = t2.Substring(0, t2.Length - 4);
                    try
                    {
                        Process[] ps = Process.GetProcessesByName(t2);
                        foreach (Process p3 in ps) { try { p3.CloseMainWindow(); } catch (Exception dg6180_1) { Diag.Swallow("MainForm.cs:1932", dg6180_1); } }
                        System.Threading.Thread.Sleep(200);
                        foreach (Process p3 in ps) { try { if (!p3.HasExited) p3.Kill(); } catch (Exception dg6182_1) { Diag.Swallow("MainForm.cs:1934", dg6182_1); } }
                        lg.Append("kill:" + nm.Trim() + " ");
                    }
                    catch (Exception dg6185_1) { Diag.Swallow("MainForm.cs:1937", dg6185_1); }
                }
            }
            if (ChainCfg.Temp)
            {
                try
                {
                    string tmp = Path.GetTempPath();
                    DateTime keep = DateTime.Now.AddDays(-1);
                    foreach (string fp in Directory.GetFiles(tmp))
                    {
                        try { if (File.GetLastWriteTime(fp) < keep) File.Delete(fp); } catch (Exception dg6196_1) { Diag.Swallow("MainForm.cs:1948", dg6196_1); }
                    }
                    lg.Append("temp ");
                }
                catch (Exception dg6200_1) { Diag.Swallow("MainForm.cs:1952", dg6200_1); }
            }
            if (ChainCfg.Clip)
            {
                try
                {
                    if (clipHist != null) { clipHist.Items.Clear(); clipHist.Save(); lg.Append("clip "); }
                }
                catch (Exception dg6208_1) { Diag.Swallow("MainForm.cs:1960", dg6208_1); }
            }
            if (ChainCfg.ScreenOff) { try { ScreenOff(); lg.Append("screenoff "); } catch (Exception dg6210_1) { Diag.Swallow("MainForm.cs:1962", dg6210_1); } }
            if (ChainCfg.Lock) { try { Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true }); lg.Append("lock "); } catch (Exception dg6211_1) { Diag.Swallow("MainForm.cs:1963", dg6211_1); } }
            try { Log("[动作链] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + lg.ToString().Trim()); } catch (Exception dg6212_1) { Diag.Swallow("MainForm.cs:1964", dg6212_1); }
        }

        // ===== v10.0 智能口令 =====
        void ShowVoiceCmd()
        {
            Form f = new Form();
            f.Text = T("智能口令 · 小工具箱", "Smart Command · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(560, 306);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;

            Label ttl = new Label();
            ttl.Text = T("用一句话安排电脑", "One sentence, one action");
            ttl.ForeColor = UiKit.A(235, 242, 250);
            ttl.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("口令直接在本地解析，不联网、不调模型：说清楚「多久」+「做什么」就行。",
                "Parsed locally, no network, no model: say how long and what to do.");
            sub.ForeColor = UiKit.A(140, 152, 168);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(520, 20);
            f.Controls.Add(sub);

            TextBox tb = new TextBox();
            tb.Text = T("30分钟后关机", "shutdown in 30 minutes");
            tb.BackColor = UiKit.A(28, 33, 41);
            tb.ForeColor = UiKit.A(230, 238, 248);
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = new Font("Microsoft YaHei UI", 12F);
            tb.Location = new Point(20, 76);
            tb.Size = new Size(520, 34);
            f.Controls.Add(tb);

            Label eg = new Label();
            eg.Text = T("例：30分钟后关机 · 1小时30分后重启 · 45秒后睡眠 · 打开音乐下载 · 清理C盘 · 截图 · 静音 · 取消任务",
                "e.g. shutdown in 30m · restart in 1h30m · sleep in 45s · open music · clean C · screenshot · mute · cancel");
            eg.ForeColor = UiKit.A(120, 132, 148);
            eg.Font = new Font("Microsoft YaHei UI", 8F);
            eg.Location = new Point(20, 116);
            eg.Size = new Size(520, 40);
            f.Controls.Add(eg);

            Label st = new Label();
            st.Text = T("等待输入…", "Waiting…");
            st.ForeColor = UiKit.A(150, 200, 255);
            st.Font = new Font("Microsoft YaHei UI", 9F);
            st.Location = new Point(20, 162);
            st.Size = new Size(520, 40);
            f.Controls.Add(st);

            Button go = MakeBtn(T("执行", "Run"), UiKit.A(43, 95, 208), Color.White, 20, 210, 120, 38);
            go.Click += delegate
            {
                string res = RunVoice(tb.Text);
                st.Text = res;
                if (res.StartsWith("✖")) Sfx.Error(); else Sfx.Ok();
            };
            f.Controls.Add(go);

            Button close = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 152, 210, 110, 38);
            close.Click += delegate { f.Close(); };
            f.Controls.Add(close);

            tb.KeyDown += delegate (object s6, KeyEventArgs e6) { if (e6.KeyCode == Keys.Enter) { e6.SuppressKeyPress = true; go.PerformClick(); } };

            Label tip3 = new Label();
            tip3.Text = T("说明：定时类口令会自动选中「倒计时」模式并沿用当前「到点弹窗确认」设置。",
                "Note: timed commands pick the countdown mode and keep your confirm setting.");
            tip3.ForeColor = UiKit.A(112, 124, 140);
            tip3.Font = new Font("Microsoft YaHei UI", 8F);
            tip3.Location = new Point(20, 268);
            tip3.Size = new Size(520, 40);
            f.Controls.Add(tip3);

            ShowOrTrack(f);
            tb.Focus();
            tb.SelectAll();
        }

        // 口令解析：返回给用户看的一句话（✖ 开头表示没听懂/失败）
        string RunVoice(string raw)
        {
            string q = (raw ?? "").Trim();
            if (q.Length == 0) return T("✖ 先说一句，比如「30分钟后关机」", "✖ Say something like 'shutdown in 30 minutes'");

            // 1) 定时类：多久 + 动作
            double secs = 0;
            System.Text.RegularExpressions.Match mh = System.Text.RegularExpressions.Regex.Match(q, @"(\d+)\s*(个)?\s*(小时|钟头|h|hour)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            System.Text.RegularExpressions.Match mm = System.Text.RegularExpressions.Regex.Match(q, @"(\d+)\s*(分钟|分|m|min)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            System.Text.RegularExpressions.Match ms = System.Text.RegularExpressions.Regex.Match(q, @"(\d+)\s*(秒|s|sec)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mh.Success) secs += int.Parse(mh.Groups[1].Value) * 3600;
            if (mm.Success) secs += int.Parse(mm.Groups[1].Value) * 60;
            if (ms.Success) secs += int.Parse(ms.Groups[1].Value);
            if (secs > 0)
            {
                string act = null;
                if (q.Contains("重启") || q.ToLowerInvariant().Contains("reboot")) act = "重启";
                else if (q.Contains("睡眠") || q.Contains("休眠") || q.ToLowerInvariant().Contains("sleep")) act = "静音后睡眠";
                else if (q.Contains("锁") || q.ToLowerInvariant().Contains("lock")) act = "锁定";
                else if (q.Contains("关机") || q.ToLowerInvariant().Contains("shutdown")) act = "关机";
                if (act == null) return T("✖ 没听出要做什么：可以说 关机 / 重启 / 睡眠 / 锁定", "✖ Did not catch the action: shutdown / restart / sleep / lock");
                if (taskActive) return T("✖ 已有任务在进行，先「取消任务」再来", "✖ A task is already running — cancel it first");
                CloseVoiceAndStart(act, secs);
                return T("✔ 已开始：" + (int)(secs / 60) + " 分钟后" + act, "✔ Started: " + act + " in " + (int)(secs / 60) + " min");
            }

            // 2) 页面类
            string[] pageKeys = { "关机", "清理", "水印", "价值", "配置", "视频", "音乐", "成就", "重装" };
            int pi = -1;
            for (int i = 0; i < pageKeys.Length; i++) if (q.Contains(pageKeys[i])) { pi = i; break; }
            if (pi >= 0 && !q.Contains("后") && !q.Contains("点"))
            {
                SelectAction(pi);
                Sfx.Switch();
                return T("✔ 已切到「" + ACTIONS[pi] + "」", "✔ Switched to " + ACTIONS[pi]);
            }
            if (q.Contains("截图") || q.ToLowerInvariant().Contains("screenshot")) { ScreenshotNow(); return T("✔ 打开截图", "✔ Screenshot opened"); }
            if (q.Contains("静音") || q.ToLowerInvariant().Contains("mute")) { MuteNow(); Sfx.Switch(); return T("✔ 已切换静音", "✔ Muted (toggled)"); }
            if (q.Contains("取消") || q.ToLowerInvariant().Contains("cancel")) { CancelTask(); return T("✔ 已取消当前任务", "✔ Task cancelled"); }
            if (q.Contains("口令") || q.Contains("指令")) return T("✖ 这就是口令窗口本身 :)", "✖ This IS the command window :)");
            Sfx.Warn();
            return T("✖ 没听懂。「30分钟后关机」「1小时后重启」「打开音乐下载」「清理C盘」「截图」这样说就行。",
                     "✖ Not understood. Try 'shutdown in 30 minutes', 'restart in 1 hour', 'open music', 'clean C', 'screenshot'.");
        }

        void CloseVoiceAndStart(string act, double secs)
        {
            // 找到口令窗并关掉，避免它盖在倒计时上
            foreach (Form fr in Application.OpenForms)
            {
                if (fr != null && fr.Text != null && fr.Text.IndexOf("智能口令") >= 0) { try { fr.Close(); } catch (Exception dg6352_1) { Diag.Swallow("MainForm.cs:2104", dg6352_1); } }
            }
            try
            {
                rbCountdown.Checked = true;
                UpdateModeUI();
            }
            catch (Exception dg6359_1) { Diag.Swallow("MainForm.cs:2111", dg6359_1); }
            curAction = act;
            int h = (int)(secs / 3600), m2 = (int)((secs - h * 3600) / 60), s2 = (int)(secs - h * 3600 - m2 * 60);
            numH.Value = Math.Min(99, h);
            numM.Value = Math.Min(59, m2);
            numS.Value = Math.Min(59, s2);
            StartTask();
            Sfx.Launch();
        }

        void RunPlugin(string name, string cmd)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + cmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = BASE_DIR;
                Process.Start(psi);
                Sfx.Save();
                Log("[插件] " + name + " -> " + cmd);
                try { trayIcon.ShowBalloonTip(1200, "小工具箱", T("已执行插件：", "Plugin run: ") + name, ToolTipIcon.Info); } catch (Exception dg6380_1) { Diag.Swallow("MainForm.cs:2132", dg6380_1); }
            }
            catch (Exception ex)
            {
                Sfx.Error();
                MessageBox.Show(T("插件执行失败：", "Plugin failed: ") + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenPluginFolder()
        {
            try
            {
                string pdir = Path.Combine(BASE_DIR, "插件");
                if (!Directory.Exists(pdir)) Directory.CreateDirectory(pdir);
                string readme = Path.Combine(pdir, "说明.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "插件命令说明\r\n" +
                        "1) 本目录下的每个 .txt 都可以放若干行插件命令，每行格式： 名称|命令行\r\n" +
                        "2) 名称会出现在托盘菜单「插件命令」里，点击即用 cmd 执行后面那条命令行\r\n" +
                        "3) 命令行不需要写 cmd /c，直接写要执行的命令，例如：\r\n" +
                        "   打开记事本|start notepad\r\n" +
                        "   打开下载目录|start %USERPROFILE%\\Downloads\r\n" +
                        "   刷新 DNS|ipconfig /flushdns\r\n" +
                        "4) 需要管理员权限的命令请在名称后加 ! 前缀（如 ! 重启资源管理器）——程序不会自动提权，只做提示\r\n" +
                        "5) 改完保存即可，重新打开托盘菜单就会刷新\r\n",
                        new UTF8Encoding(true));
                Process.Start("explorer.exe", pdir);
                Sfx.Click();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // v10.0：迷你倒计时窗。无边框、置顶，只显示大号倒计时 + 当前动作，双击回主窗。
        class MiniForm : Form
        {
            System.Windows.Forms.Timer t;
            Label big, sub, tag;
            string[] lastTxt = new string[] { "--:--:--", "" };
            Func<string[]> getter;
            Action onOpen, onClose;
            bool drag; Point dragPt;
            public MiniForm(bool en, Func<DateTime, DateTime> dummy, Func<string[]> g, Action open, Action close)
            {
                getter = g; onOpen = open; onClose = close;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(236, 116);
                BackColor = UiKit.A(18, 22, 28);
                DoubleBuffered = true;
                try
                {
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    Location = new Point(wa.Right - 252, wa.Bottom - 132);
                }
                catch (Exception dg6438_1) { Diag.Swallow("MainForm.cs:2190", dg6438_1); }
                tag = new Label();
                tag.Text = "小工具箱 · " + (en ? "mini" : "迷你");
                tag.ForeColor = UiKit.A(108, 120, 136);
                tag.Font = new Font("Microsoft YaHei UI", 7.5F);
                tag.Location = new Point(14, 8);
                tag.AutoSize = true;
                Controls.Add(tag);

                big = new Label();
                big.Text = "--:--:--";
                big.ForeColor = UiKit.A(232, 240, 250);
                big.Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold);
                big.Location = new Point(10, 26);
                big.Size = new Size(216, 42);
                big.TextAlign = ContentAlignment.MiddleCenter;
                Controls.Add(big);

                sub = new Label();
                sub.Text = en ? "idle" : "空闲";
                sub.ForeColor = UiKit.A(146, 158, 174);
                sub.Font = new Font("Microsoft YaHei UI", 8.5F);
                sub.Location = new Point(10, 70);
                sub.Size = new Size(216, 18);
                sub.TextAlign = ContentAlignment.MiddleCenter;
                Controls.Add(sub);

                Label x = new Label();
                x.Text = "×";
                x.ForeColor = UiKit.A(120, 132, 148);
                x.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
                x.TextAlign = ContentAlignment.MiddleCenter;
                x.Size = new Size(20, 20);
                x.Location = new Point(ClientSize.Width - 24, 4);
                x.Cursor = Cursors.Hand;
                x.Click += delegate { close(); };
                Controls.Add(x);

                Paint += delegate (object s1, PaintEventArgs e1)
                {
                    try
                    {
                        Graphics gg = e1.Graphics;
                        gg.SmoothingMode = SmoothingMode.AntiAlias;
                        Rectangle rc = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                        Color ac = UiKit.ThemeColor;
                        using (GraphicsPath gp = UiKit.RoundedPath(rc, 14))
                        {
                            using (SolidBrush b1 = new SolidBrush(Color.FromArgb(250, 20, 25, 32))) gg.FillPath(b1, gp);
                            using (Pen pen = new Pen(Color.FromArgb(140, ac), 1.4f)) gg.DrawPath(pen, gp);
                        }
                        Rectangle bar = new Rectangle(14, ClientSize.Height - 12, ClientSize.Width - 28, 3);
                        using (GraphicsPath bp = UiKit.RoundedPath(bar, 2))
                        using (LinearGradientBrush lb = new LinearGradientBrush(bar, Color.FromArgb(200, ac), Color.FromArgb(40, ac), 0f))
                            gg.FillPath(lb, bp);
                    }
                    catch (Exception dg6494_1) { Diag.Swallow("MainForm.cs:2246", dg6494_1); }
                };

                MouseDown += delegate (object s2, MouseEventArgs e2)
                {
                    if (e2.Button == MouseButtons.Left) { drag = true; dragPt = e2.Location; }
                };
                MouseMove += delegate (object s2, MouseEventArgs e2) { if (drag) Location = new Point(Location.X + e2.X - dragPt.X, Location.Y + e2.Y - dragPt.Y); };
                MouseUp += delegate { drag = false; };
                DoubleClick += delegate { onOpen(); };

                t = new System.Windows.Forms.Timer();
                t.Interval = 350;
                t.Tick += delegate
                {
                    try
                    {
                        string[] v = getter();
                        if (v == null || v.Length < 2) return;
                        if (v[0] != lastTxt[0]) { big.Text = v[0]; lastTxt[0] = v[0]; }
                        if (v[1] != lastTxt[1]) { sub.Text = v[1]; lastTxt[1] = v[1]; }
                    }
                    catch (Exception dg6516_1) { Diag.Swallow("MainForm.cs:2268", dg6516_1); }
                };
                t.Start();
                FormClosed += delegate { try { t.Stop(); } catch (Exception dg6519_1) { Diag.Swallow("MainForm.cs:2271", dg6519_1); } };
            }
        }

        void ShowMain()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            uiTimer.Stop();
            KillEmbedded();
            cfg.Active = taskActive && !cfg.Periodic;
            cfg.Periodic = taskActive && cfg.Periodic;
            cfg.EndTime = endTime;
            cfg.TotalSec = totalSec;
            cfg.Desc = curDesc;
            Config.Save(BASE_DIR, cfg);
            trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyAcrylic(this.Handle);
            // v6.8 全局热键：句柄就绪后注册一次
            try
            {
                if (hkMgr == null)
                {
                    hkMgr = new HotkeyManager(this.Handle, Path.Combine(BASE_DIR, "hotkeys.json"));
                    hkMgr.OnFire = HotkeyAction;
                    hkMgr.Load();
                    hkMgr.RegisterAll();
                    if (hkMgr.LastError.Length > 0) Log("[热键] " + hkMgr.LastError);
                }
            }
            catch (Exception ex) { try { Log("[热键] 初始化失败：" + ex.Message); } catch (Exception dg6560_1) { Diag.Swallow("MainForm.cs:2312", dg6560_1); } }
            StartClipCapture();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)   // WM_HOTKEY
            {
                try { if (hkMgr != null) hkMgr.Fire(m.WParam.ToInt32()); } catch (Exception dg6568_1) { Diag.Swallow("MainForm.cs:2320", dg6568_1); }
                return;
            }
            if (m.Msg == 0x00A3)   // WM_NCLBUTTONDBLCLK：双击标题栏最大化 / 还原
            {
                Point sp = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF));
                try { if (header != null && header.Bounds.Contains(sp)) { ToggleMax(); return; } } catch (Exception dg6574_1) { Diag.Swallow("MainForm.cs:2326", dg6574_1); }
            }
            if (m.Msg == 0x004A)   // WM_COPYDATA：另一个实例发来的跳页请求
            {
                try
                {
                    COPYDATASTRUCT cds = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                    if (cds.cbData > 0 && cds.lpData != IntPtr.Zero)
                    {
                        string slot = Marshal.PtrToStringUni(cds.lpData);
                        if (!string.IsNullOrEmpty(slot)) JumpToSlot(slot);
                        m.Result = (IntPtr)1;
                        return;
                    }
                }
                catch (Exception dg6589_1) { Diag.Swallow("MainForm.cs:2341", dg6589_1); }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 任务进行中：关闭=最小化到托盘；空闲：正常退出
            if (taskActive && cfg.Tray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                trayIcon.ShowBalloonTip(2000, "小工具箱", "任务进行中，已最小化到托盘，到点仍会执行。", ToolTipIcon.Info);
                return;
            }
            trayIcon.Visible = false;
            cfg.Active = taskActive && !cfg.Periodic;
            cfg.Periodic = taskActive && cfg.Periodic;
            cfg.EndTime = endTime;
            cfg.TotalSec = totalSec;
            cfg.Desc = curDesc;
            Config.Save(BASE_DIR, cfg);
            KillEmbedded();
            base.OnFormClosing(e);
        }

        // ================= 状态 =================
        void InitState()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    chkAutorun.Checked = k != null && k.GetValue("DoubaoScheduledShutdown") != null;
            }
            catch { chkAutorun.Checked = false; }
        }

        void ApplyConfigToUI()
        {
            chkTray.Checked = cfg.Tray;
            chkConfirm.Checked = cfg.NeedConfirm;
            chkSound.Checked = cfg.Sound;
            chkAutorun.Checked = cfg.Autorun;
            numConfirm.Value = Math.Max(10, Math.Min(600, cfg.ConfirmSecs));
            numH.Value = Math.Max(0, Math.Min(999, cfg.Hours));
            numM.Value = Math.Max(0, Math.Min(59, cfg.Mins));
            numS.Value = Math.Max(0, Math.Min(59, cfg.Secs));
            dtpTime.Value = cfg.AtTime > DateTime.Now ? cfg.AtTime : DateTime.Now.AddHours(1);
            dtpDaily.Value = DateTime.Now.Date.Add(cfg.AtTime.TimeOfDay);
            dtpWeekly.Value = DateTime.Now.Date.Add(cfg.AtTime.TimeOfDay);
            for (int i = 0; i < 7; i++) weekCb[i].Checked = cfg.Week[i];
            switch (cfg.Mode)
            {
                case "datetime": rbDatetime.Checked = true; break;
                case "daily": rbDaily.Checked = true; break;
                case "weekly": rbWeekly.Checked = true; break;
                default: rbCountdown.Checked = true; break;
            }
            SelectAction(Array.IndexOf(ACTIONS, cfg.Action) >= 0 ? Array.IndexOf(ACTIONS, cfg.Action) : 0);
            UpdateModeUI();
        }

        void TryRestoreTask()
        {
            if (!cfg.Active && !cfg.Periodic) return;
            if (cfg.EndTime <= DateTime.Now) { cfg.Active = false; cfg.Periodic = false; return; }
            taskActive = true;
            endTime = cfg.EndTime;
            totalSec = Math.Max(cfg.TotalSec, 1);
            curDesc = cfg.Desc;
            confirmShown = false;
            lblTyping.Visible = true;
            lblStatus.Text = T("状态：任务进行中", "Status: Running");
            lblStatus.ForeColor = C_ON;
            lblDetail.Text = curDesc;
            lblDetail.ForeColor = C_TEXT;
            lblTimeSub.Text = cfg.Periodic ? T("周期任务 · 已恢复", "Periodic · Restored") : T("任务已恢复", "Task restored");
            Log("[恢复] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 恢复任务: " + curDesc);
        }

        void Log(string entry)
        {
            try { File.AppendAllText(logFile, entry + "\r\n"); } catch (Exception dg6671_1) { Diag.Swallow("MainForm.cs:2423", dg6671_1); }
        }

        void ToggleAutorun()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (chkAutorun.Checked)
                        k.SetValue("DoubaoScheduledShutdown", "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue("DoubaoScheduledShutdown", false);
                }
                cfg.Autorun = chkAutorun.Checked;
            }
            catch { MessageBox.Show("注册表操作失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ================= C盘清理：空间可视化 =================
        class CleanBlk
        {
            public string Name = "";
            public string Path = "";
            public long Size = 0;
            public Rectangle Rc = Rectangle.Empty;
            public List<CleanBlk> Kids = new List<CleanBlk>();
        }

        void CleanUi(MethodInvoker a)
        {
            if (this.InvokeRequired) { try { this.BeginInvoke(a); } catch (Exception dg6702_1) { Diag.Swallow("MainForm.cs:2454", dg6702_1); } }
            else a();
        }

        void CleanScanStart()
        {
            if (cleanScanning) return;
            cleanScanning = true;
            cleanStopFlag = false;
            cleanFileCount = 0;
            cleanFileBytes = 0;
            cleanBigTotal = 0;
            cleanRaw.Clear();
            cleanBlks.Clear();
            cleanBigRows.Clear();
            cleanHoverBlk = -1;
            if (lvCleanBig != null) lvCleanBig.Items.Clear();
            if (pnlTree != null) pnlTree.Invalidate();
            if (lblCleanScan != null)
            {
                lblCleanScan.ForeColor = UiKit.A(56, 189, 248);
                lblCleanScan.Text = T("正在扫描 C 盘…（可在扫描中随时点「停止」）", "Scanning C: ... (you can stop anytime)");
            }
            Thread th = new Thread(CleanScanWorker);
            th.IsBackground = true;
            th.Start();
            taskCleanScan = TaskHub.Start("清理扫描", "C 盘空间扫描", "枚举目录、缓存与大文件");
        }

        void CleanScanWorker()
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<CleanBlk> tops = new List<CleanBlk>();
            Dictionary<string, CleanBlk> lv2 = new Dictionary<string, CleanBlk>();
            List<string[]> bigs = new List<string[]>();
            // C:\ 根目录下的散落文件（pagefile / hiberfil / swapfile 等）
            try
            {
                CleanBlk rf = new CleanBlk();
                rf.Name = T("(根目录文件)", "(root files)");
                rf.Path = @"C:\";
                long rfSize = 0;
                foreach (string f in Directory.EnumerateFiles(@"C:\"))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        long sz = fi.Length;
                        cleanFileCount++;
                        if (sz <= 0) continue;
                        rfSize += sz;
                        cleanFileBytes += sz;
                        if (sz >= 104857600) bigs.Add(new string[] { sz.ToString(), fi.Name, @"C:\", fi.LastWriteTime.ToString("yyyy-MM-dd") });
                    }
                    catch (Exception dg6755_1) { Diag.Swallow("MainForm.cs:2507", dg6755_1); }
                }
                if (rfSize > 0) { rf.Size = rfSize; tops.Add(rf); }
            }
            catch (Exception dg6759_1) { Diag.Swallow("MainForm.cs:2511", dg6759_1); }
            try
            {
                foreach (string d in Directory.GetDirectories(@"C:\"))
                {
                    if (cleanStopFlag) break;
                    try
                    {
                        FileAttributes at = File.GetAttributes(d);
                        if ((at & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    CleanBlk b = new CleanBlk();
                    b.Name = Path.GetFileName(d);
                    b.Path = d;
                    tops.Add(b);
                    CleanWalk(d, b, lv2, bigs);
                }
            }
            catch (Exception dg6778_1) { Diag.Swallow("MainForm.cs:2530", dg6778_1); }
            bigs.Sort(delegate(string[] a, string[] b2) { return long.Parse(b2[0]).CompareTo(long.Parse(a[0])); });
            long bigSum = 0;
            foreach (string[] r in bigs) bigSum += long.Parse(r[0]);
            long bytes = cleanFileBytes;
            long files = cleanFileCount;
            double sec = sw.Elapsed.TotalSeconds;
            bool stopped = cleanStopFlag;
            CleanUi(delegate
            {
                cleanScanning = false;
                if (!stopped && !cleanAutoTest) Sfx.Ok();
                cleanRaw = tops;
                cleanBigRows = bigs;
                cleanBigTotal = bigSum;
                CleanLayoutTree(new Rectangle(4, 4, Math.Max(12, pnlTree.ClientSize.Width - 8), Math.Max(12, pnlTree.ClientSize.Height - 8)));
                pnlTree.Invalidate();
                CleanFillBigList();
                if (lblBigCap != null)
                {
                    lblBigCap.Text = FmtSize(bigSum);
                    lblBigSub.Text = T("超过 100MB 共 ", "files over 100MB: ") + bigs.Count + T(" 个", "");
                }
                if (lblJunkCap != null)
                {
                    lblJunkCap.Text = files.ToString("N0");
                    lblJunkSub.Text = T("合计 ", "total ") + FmtSize(bytes);
                }
                if (lblCleanScan != null)
                {
                    lastCleanScanBytes = bytes;
                    lastCleanScanFiles = (int)Math.Min(int.MaxValue, files);
                    lblCleanScan.ForeColor = C_SUB;
                    lblCleanScan.Text = T("扫描完成：", "Done: ") + tops.Count + T(" 个顶层目录 · ", " dirs · ")
                        + FmtSize(bytes) + T(" · 耗时 ", " · ") + sec.ToString("0.0") + "s"
                        + (stopped ? T("（已停止）", " (stopped)") : "")
                        + T(" · 鼠标悬停色块看明细，双击大文件可定位", " · hover a block for detail");
                    // v10.0：扫完记一条历史，空态卡片里就能看到"这周有没有变胖"
                    try { if (!stopped && bytes > 0) CleanHistory.Add(bytes, files); } catch (Exception dg6814_1) { Diag.Swallow("MainForm.cs:2566", dg6814_1); }
                    try
                    {
                        if (stopped) TaskHub.Cancel(taskCleanScan, "用户停止扫描");
                        else TaskHub.Done(taskCleanScan, "扫描 " + tops.Count + " 个目录 · " + FmtSize(bytes) + " · 耗时 " + sec.ToString("0.0") + "s");
                    }
                    catch (Exception dgTask1) { Diag.Swallow("MainForm.taskClean", dgTask1); }
                }
                if (cleanAutoTest)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("dirs=" + tops.Count + " files=" + files + " bytes=" + bytes + " sec=" + sec.ToString("0.0"));
                        sb.AppendLine("bigFiles=" + bigs.Count + " bigSum=" + bigSum + " blocks=" + cleanBlks.Count);
                        foreach (CleanBlk b in cleanBlks)
                            sb.AppendLine("  " + b.Name + " " + FmtSize(b.Size) + " kids=" + b.Kids.Count + " rc=" + b.Rc.X + "," + b.Rc.Y + "," + b.Rc.Width + "," + b.Rc.Height);
                        File.WriteAllText(Path.Combine(BASE_DIR, "_clean_test.txt"), sb.ToString(), Encoding.UTF8);
                    }
                    catch (Exception dg6827_1) { Diag.Swallow("MainForm.cs:2579", dg6827_1); }
                }
            });
        }

        void CleanWalk(string root, CleanBlk b1, Dictionary<string, CleanBlk> lv2, List<string[]> bigs)
        {
            Stack<string> st = new Stack<string>();
            st.Push(root);
            long cnt = 0;
            string seg = "";
            CleanBlk b2 = null;
            while (st.Count > 0)
            {
                if (cleanStopFlag) return;
                string cur = st.Pop();
                string rel = cur.Length > root.Length ? cur.Substring(root.Length).TrimStart('\\') : "";
                string curSeg = rel.Length == 0 ? "." : rel.Split('\\')[0];
                if (curSeg != seg)
                {
                    seg = curSeg;
                    string key = root + "|" + seg;
                    b2 = lv2.ContainsKey(key) ? lv2[key] : null;
                }
                try
                {
                    foreach (string f in Directory.EnumerateFiles(cur))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            long sz = fi.Length;
                            cleanFileCount++;
                            if (sz <= 0) continue;
                            b1.Size += sz;
                            cleanFileBytes += sz;
                            if (b2 == null)
                            {
                                b2 = new CleanBlk();
                                b2.Name = seg == "." ? T("(根文件)", "(root files)") : seg;
                                b2.Path = seg == "." ? root : Path.Combine(root, seg);
                                lv2[root + "|" + seg] = b2;
                                b1.Kids.Add(b2);
                            }
                            b2.Size += sz;
                            if (sz >= 104857600)
                            {
                                try
                                {
                                    bigs.Add(new string[] { sz.ToString(), fi.Name, Path.GetDirectoryName(f), fi.LastWriteTime.ToString("yyyy-MM-dd") });
                                }
                                catch (Exception dg6878_1) { Diag.Swallow("MainForm.cs:2630", dg6878_1); }
                            }
                        }
                        catch (Exception dg6881_1) { Diag.Swallow("MainForm.cs:2633", dg6881_1); }
                        cnt++;
                        if ((cnt % 16384) == 0)
                        {
                            string pv = T("正在扫描 ", "Scanning ") + b1.Name + T(" · 已处理 ", " · ") + cleanFileCount.ToString("N0") + T(" 个文件", " files");
                            CleanUi(delegate { if (lblCleanScan != null) lblCleanScan.Text = pv; });
                            if (cleanAutoTest && (cnt % 65536) == 0)
                            {
                                try { File.WriteAllText(Path.Combine(BASE_DIR, "_clean_progress.txt"), DateTime.Now.ToString("HH:mm:ss") + " files=" + cleanFileCount + " dir=" + b1.Name + " bytes=" + cleanFileBytes, Encoding.UTF8); } catch (Exception dg6889_1) { Diag.Swallow("MainForm.cs:2641", dg6889_1); }
                            }
                        }
                    }
                }
                catch (Exception dg6894_1) { Diag.Swallow("MainForm.cs:2646", dg6894_1); }
                try
                {
                    foreach (string sd in Directory.EnumerateDirectories(cur))
                    {
                        try
                        {
                            FileAttributes at = File.GetAttributes(sd);
                            if ((at & FileAttributes.ReparsePoint) != 0) continue;
                            int dep = sd.Split('\\').Length - root.Split('\\').Length;
                            if (dep <= 12) st.Push(sd);
                        }
                        catch (Exception dg6906_1) { Diag.Swallow("MainForm.cs:2658", dg6906_1); }
                    }
                }
                catch (Exception dg6909_1) { Diag.Swallow("MainForm.cs:2661", dg6909_1); }
            }
        }

        void CleanLayoutTree(Rectangle rc)
        {
            cleanLayoutRc = rc;
            if (cleanRaw == null || cleanRaw.Count == 0) { cleanBlks = new List<CleanBlk>(); return; }
            List<CleanBlk> top = new List<CleanBlk>();
            foreach (CleanBlk b in cleanRaw) if (b.Size > 0) top.Add(b);
            top.Sort(delegate(CleanBlk a, CleanBlk c) { return c.Size.CompareTo(a.Size); });
            if (top.Count > 12)
            {
                CleanBlk oth = new CleanBlk();
                oth.Name = T("其它", "Other");
                for (int i = 11; i < top.Count; i++) oth.Size += top[i].Size;
                top.RemoveRange(11, top.Count - 11);
                top.Add(oth);
            }
            CleanSlice(top, rc);
            foreach (CleanBlk b in top)
            {
                List<CleanBlk> kids = new List<CleanBlk>();
                foreach (CleanBlk k in b.Kids) if (k.Size > 0) kids.Add(k);
                kids.Sort(delegate(CleanBlk a, CleanBlk c) { return c.Size.CompareTo(a.Size); });
                if (kids.Count > 8)
                {
                    CleanBlk oth2 = new CleanBlk();
                    oth2.Name = T("其它", "Other");
                    for (int i = 7; i < kids.Count; i++) oth2.Size += kids[i].Size;
                    kids.RemoveRange(7, kids.Count - 7);
                    kids.Add(oth2);
                }
                if (b.Rc.Width >= 70 && b.Rc.Height >= 50 && kids.Count > 0)
                {
                    Rectangle inner = new Rectangle(b.Rc.X + 2, b.Rc.Y + 20, b.Rc.Width - 4, b.Rc.Height - 24);
                    if (inner.Width > 8 && inner.Height > 8)
                    {
                        CleanSlice(kids, inner);
                        b.Kids = kids;
                    }
                    else b.Kids = new List<CleanBlk>();
                }
                else b.Kids = new List<CleanBlk>();
            }
            cleanBlks = top;
        }

        void CleanSlice(List<CleanBlk> items, Rectangle rc)
        {
            if (items == null || items.Count == 0 || rc.Width < 4 || rc.Height < 4) return;
            if (items.Count == 1) { items[0].Rc = rc; return; }
            long sum = 0;
            foreach (CleanBlk b in items) sum += Math.Max(1, b.Size);
            bool horiz = rc.Width >= rc.Height;
            long half = sum / 2, acc = 0;
            int cut = 1;
            for (int i = 0; i < items.Count - 1; i++)
            {
                acc += Math.Max(1, items[i].Size);
                cut = i + 1;
                if (acc >= half) break;
            }
            if (cut < 1) cut = 1;
            if (cut > items.Count - 1) cut = items.Count - 1;
            List<CleanBlk> a = items.GetRange(0, cut);
            List<CleanBlk> bb = items.GetRange(cut, items.Count - cut);
            long sa = 0;
            foreach (CleanBlk x in a) sa += Math.Max(1, x.Size);
            if (horiz)
            {
                int wa = (int)Math.Round((double)rc.Width * sa / sum);
                if (wa < 6) wa = 6;
                if (wa > rc.Width - 6) wa = rc.Width - 6;
                CleanSlice(a, new Rectangle(rc.X, rc.Y, wa, rc.Height));
                CleanSlice(bb, new Rectangle(rc.X + wa, rc.Y, rc.Width - wa, rc.Height));
            }
            else
            {
                int ha = (int)Math.Round((double)rc.Height * sa / sum);
                if (ha < 6) ha = 6;
                if (ha > rc.Height - 6) ha = rc.Height - 6;
                CleanSlice(a, new Rectangle(rc.X, rc.Y, rc.Width, ha));
                CleanSlice(bb, new Rectangle(rc.X, rc.Y + ha, rc.Width, rc.Height - ha));
            }
        }

        void CleanPaintTree(Graphics g, Rectangle rc)
        {
            using (SolidBrush bbg = new SolidBrush(UiKit.Paper ? Color.FromArgb(250, 245, 233) : UiKit.A(20, 26, 36))) g.FillRectangle(bbg, rc);
            if (cleanBlks == null || cleanBlks.Count == 0)
            {
                // v9.2：空态不再"一张浮在中间的插画 + 底部一行字"（两块各说各的）。
                // 改成一张极淡的空卡片：上半插画、下半三行递短的骨架条、卡片底部一行提示，
                // 读起来是"热力图会在这块区域里长出来"，而不是"这里坏了"。
                Rectangle card = new Rectangle(rc.X + 10, rc.Y + 8, rc.Width - 20, rc.Height - 16);
                bool canCard = card.Width >= 240 && card.Height >= 150;
                if (canCard)
                {
                    using (GraphicsPath cp = UiKit.RoundedPath(card, 10))
                    {
                        using (SolidBrush cb = new SolidBrush(UiKit.AA(13, 32, 40, 54))) g.FillPath(cb, cp);
                        using (Pen cpn = new Pen(UiKit.AA(44, 108, 132, 168), 1f)) g.DrawPath(cpn, cp);
                    }
                }
                Rectangle artRc = canCard
                    ? new Rectangle(card.X, card.Y + 4, card.Width, Math.Max(96, card.Height - 92))
                    : new Rectangle(rc.X, rc.Y, rc.Width, Math.Max(0, rc.Height - 46));
                int hist = CleanHistory.Count;
                bool drawHist = canCard && hist >= 2 && artRc.Height >= 70;
                if (drawHist)
                {
                    // v10.0：有历史时这块不画插画，改画"近 N 次扫描"的迷你折线——一眼看出 C 盘在胖还是在瘦
                    int n = Math.Min(8, hist);
                    long mn = long.MaxValue, mx = 0;
                    for (int i = hist - n; i < hist; i++)
                    {
                        long v = CleanHistory.At(i);
                        if (v < mn) mn = v;
                        if (v > mx) mx = v;
                    }
                    if (mx <= 0) mx = 1;
                    if (mn > mx) mn = 0;
                    Rectangle pr = new Rectangle(artRc.X + 26, artRc.Y + 20, artRc.Width - 52, artRc.Height - 42);
                    if (pr.Width > 40 && pr.Height > 20)
                    {
                        using (SolidBrush bg2 = new SolidBrush(UiKit.AA(10, 120, 150, 190)))
                            g.FillRectangle(bg2, pr);
                        PointF[] pts = new PointF[n];
                        for (int i = 0; i < n; i++)
                        {
                            long v = CleanHistory.At(hist - n + i);
                            double f = (mx == mn) ? 0.5 : (v - (double)mn) / (mx - mn);
                            // 起点贴底、高点贴顶：数值越大画得越高
                            pts[i] = new PointF(pr.X + (float)(pr.Width * (n == 1 ? 0.5 : i / (double)(n - 1))),
                                pr.Bottom - (float)(f * pr.Height));
                        }
                        Color ac = UiKit.ThemeColor;
                        using (Pen lp = new Pen(Color.FromArgb(150, ac), 1.6f)) g.DrawLines(lp, pts);
                        using (Pen lp2 = new Pen(Color.FromArgb(240, ac), 2.4f))
                        {
                            if (n >= 2) g.DrawLine(lp2, pts[n - 2], pts[n - 1]);
                        }
                        using (SolidBrush db = new SolidBrush(Color.FromArgb(240, ac)))
                        {
                            for (int i = 0; i < n; i++)
                            {
                                float rr2 = (i == n - 1) ? 3.2f : 1.9f;
                                g.FillEllipse(db, pts[i].X - rr2, pts[i].Y - rr2, rr2 * 2, rr2 * 2);
                            }
                        }
                        using (Font fv = new Font("Microsoft YaHei UI", 7.5F))
                        using (SolidBrush fb = new SolidBrush(UiKit.A(128, 140, 158)))
                        {
                            g.DrawString(CleanHistory.WhenAt(hist - n), fv, fb, new PointF(pr.X, pr.Bottom + 4));
                            string lastT = (n > 1) ? CleanHistory.WhenAt(hist - 1) : "";
                            if (lastT.Length > 0)
                            {
                                SizeF sz2 = g.MeasureString(lastT, fv);
                                g.DrawString(lastT, fv, fb, new PointF(pr.Right - sz2.Width, pr.Bottom + 4));
                            }
                            g.DrawString(FmtSize(mx), fv, fb, new PointF(pr.X, pr.Y - 14));
                        }
                    }
                }
                else EmptyArt.CleanArt(g, artRc);
                if (canCard && !drawHist)
                {
                    int[] ws = { 44, 32, 22 };
                    int sy = artRc.Bottom + 8;
                    for (int k = 0; k < 3; k++)
                    {
                        if (sy + k * 13 > card.Bottom - 42) break;
                        int bw2 = card.Width * ws[k] / 100;
                        Rectangle sr = new Rectangle(card.X + (card.Width - bw2) / 2, sy + k * 13, bw2, 6);
                        using (GraphicsPath sp2 = UiKit.RoundedPath(sr, 3))
                        using (SolidBrush sb2 = new SolidBrush(Color.FromArgb(62 - k * 14, 130, 160, 200)))
                            g.FillPath(sb2, sp2);
                    }
                }
                // 卡片里的这句话要跟页面上已有的「尚未扫描 · 点右上角…」区分开，别同一句说三遍
                string mt;
                if (drawHist) mt = T("近 " + Math.Min(8, CleanHistory.Count) + " 次扫描的 C 盘占用走势（越靠右越新）",
                                     "C: usage over the last " + Math.Min(8, CleanHistory.Count) + " scans");
                else if (cleanScanning) mt = T("正在扫描 C 盘…", "Scanning...");
                else mt = T("扫描完成后，占用会在这里铺成热力图", "Usage fills this area after scanning");
                Rectangle trc = canCard
                    ? new Rectangle(card.X + 6, card.Bottom - 36, card.Width - 12, 36)
                    : new Rectangle(rc.X, rc.Y + rc.Height - 46, rc.Width, 46);
                using (Font mf = new Font("Microsoft YaHei UI", 9F))
                {
                    if (UiKit.Paper)
                    {
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        StringFormat tsf = new StringFormat();
                        tsf.Alignment = StringAlignment.Center;
                        tsf.LineAlignment = StringAlignment.Center;
                        tsf.FormatFlags = StringFormatFlags.NoWrap;
                        using (SolidBrush tbr = new SolidBrush(Color.FromArgb(96, 86, 72)))
                            g.DrawString(mt, mf, tbr, trc, tsf);
                    }
                    else
                        TextRenderer.DrawText(g, mt, mf, trc, C_SUB, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return;
            }
            Color[] pal = {
                UiKit.A(56, 132, 255), UiKit.A(56, 189, 248), UiKit.A(16, 185, 129),
                UiKit.A(250, 204, 21), UiKit.A(249, 115, 22), UiKit.A(236, 72, 153),
                UiKit.A(168, 85, 247), UiKit.A(45, 212, 191), UiKit.A(148, 163, 184),
                UiKit.A(244, 114, 182), UiKit.A(96, 165, 250), UiKit.A(34, 197, 94)
            };
            using (Font fB = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold))
            using (Font fS = new Font("Microsoft YaHei UI", 7.5F))
            {
                for (int i = 0; i < cleanBlks.Count; i++)
                {
                    CleanBlk b = cleanBlks[i];
                    Color c = pal[i % pal.Length];
                    if (b.Rc.Width < 3 || b.Rc.Height < 3) continue;
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(232, c))) g.FillRectangle(br, b.Rc);
                    foreach (CleanBlk k in b.Kids)
                    {
                        if (k.Rc.Width < 3 || k.Rc.Height < 3) continue;
                        Color kc = (cleanHoverBlk == i) ? UiKit.Lighten(c, 30) : UiKit.Darken(c, 24);
                        using (SolidBrush br2 = new SolidBrush(Color.FromArgb(210, kc))) g.FillRectangle(br2, k.Rc);
                    }
                    using (Pen p = new Pen(UiKit.AA(48, 255, 255, 255), 1f))
                        g.DrawRectangle(p, b.Rc.X, b.Rc.Y, b.Rc.Width - 1, b.Rc.Height - 1);
                    if (b.Rc.Width > 76 && b.Rc.Height > 24)
                    {
                        TextRenderer.DrawText(g, b.Name, fB, new Rectangle(b.Rc.X + 5, b.Rc.Y + 3, b.Rc.Width - 8, 16),
                            Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        if (b.Rc.Height > 40)
                            TextRenderer.DrawText(g, FmtSize(b.Size), fS, new Rectangle(b.Rc.X + 5, b.Rc.Y + 19, b.Rc.Width - 8, 15),
                                UiKit.A(236, 242, 252), TextFormatFlags.NoPrefix);
                    }
                }
            }
            if (cleanHoverBlk >= 0 && cleanHoverBlk < cleanBlks.Count)
            {
                CleanBlk hb = cleanBlks[cleanHoverBlk];
                string tip = hb.Name + " · " + FmtSize(hb.Size);
                using (SolidBrush tb = new SolidBrush(UiKit.AA(215, 18, 22, 28)))
                    g.FillRectangle(tb, 4, rc.Height - 24, rc.Width - 8, 20);
                using (Font tf = new Font("Microsoft YaHei UI", 8F))
                    TextRenderer.DrawText(g, tip, tf, new Rectangle(10, rc.Height - 24, rc.Width - 20, 20),
                        UiKit.A(224, 238, 252), TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        int CleanHitTest(Point p)
        {
            if (cleanBlks == null) return -1;
            for (int i = 0; i < cleanBlks.Count; i++)
                if (cleanBlks[i].Rc.Contains(p)) return i;
            return -1;
        }

        void CleanFillBigList()
        {
            if (lvCleanBig == null) return;
            lvCleanBig.Items.Clear();
            int n = Math.Min(60, cleanBigRows.Count);
            for (int i = 0; i < n; i++)
            {
                string[] r = cleanBigRows[i];
                ListViewItem it = new ListViewItem(FmtSize(long.Parse(r[0])));
                it.SubItems.Add(r[1]);
                it.SubItems.Add(r[2]);
                it.SubItems.Add(r[3]);
                lvCleanBig.Items.Add(it);
            }
        }

        void CleanOpenBig()
        {
            if (lvCleanBig == null || lvCleanBig.SelectedIndices.Count == 0) return;
            int idx = lvCleanBig.SelectedIndices[0];
            if (idx < 0 || idx >= cleanBigRows.Count) return;
            string[] r = cleanBigRows[idx];
            string full = Path.Combine(r[2], r[1]);
            try { Process.Start("explorer.exe", "/select," + full); } catch (Exception dg7178_1) { Diag.Swallow("MainForm.cs:2930", dg7178_1); }
        }

        /// <summary>导出 C 盘清理扫描结果为文本报告，并在资源管理器中定位到该文件。</summary>
        void CleanExportReport()
        {
            try
            {
                if (cleanDiskTotal <= 0)
                {
                    MessageBox.Show(T("还没有扫描数据，先点「扫描空间」。", "Nothing scanned yet — click Scan disk first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("小工具箱 · C盘清理报告");
                sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("C 盘已用 / 总计：" + FmtSize(cleanDiskUsed) + " / " + FmtSize(cleanDiskTotal)
                    + "（" + (cleanDiskTotal > 0 ? Math.Round((double)cleanDiskUsed * 100 / cleanDiskTotal) : 0) + "% 已用）");
                sb.AppendLine("大文件占用：" + (lblBigCap == null ? "--" : lblBigCap.Text) + "（" + (lblBigSub == null ? "" : lblBigSub.Text) + "）");
                sb.AppendLine("已索引文件：" + (lblJunkCap == null ? "--" : lblJunkCap.Text) + "（" + (lblJunkSub == null ? "" : lblJunkSub.Text) + "）");
                sb.AppendLine();
                sb.AppendLine("== 大文件明细（" + (lvCleanBig == null ? 0 : lvCleanBig.Items.Count) + " 条）==");
                sb.AppendLine("大小\t文件名\t所在目录\t修改时间");
                if (lvCleanBig != null)
                {
                    foreach (ListViewItem it in lvCleanBig.Items)
                    {
                        string l = "";
                        for (int i = 0; i < it.SubItems.Count; i++)
                            l += (i == 0 ? "" : "\t") + it.SubItems[i].Text;
                        sb.AppendLine(l);
                    }
                }
                sb.AppendLine();
                sb.AppendLine("说明：报告仅统计文件大小，不读取任何文件内容；清理只针对可再生的缓存与临时文件。");
                string path = Path.Combine(BASE_DIR, "C盘清理报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Log("[C盘清理] 报告已导出：" + path);
                MessageBox.Show(T("报告已导出：", "Report saved: ") + path, T("完成", "Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select," + path); } catch (Exception dg7217_1) { Diag.Swallow("MainForm.cs:2969", dg7217_1); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("导出失败：", "Export failed: ") + ex.Message, T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ================= C盘清理 =================
        void ShowCleanPage(bool show)
        {
            if (pageClean == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            pageClean.Visible = show;
            if (show) { lblPeer.Text = T("C盘清理", "C: Cleanup"); lblPeer.ForeColor = UiKit.A(56, 189, 248); }
            else RestoreTimerHeader();
        }

        void RunClean(string mode)
        {
            string dir = Path.Combine(BASE_DIR, "clean_tool");
            string script = (mode == "v8") ? "DeepClean_v8.ps1" : "DeepClean.ps1";
            string path = Path.Combine(dir, script);
            if (!File.Exists(path) || mode == "v8")
            {
                Cursor.Current = Cursors.WaitCursor;
                bool ok = UnpackResource("CleanZip", dir, "C盘清理");
                Cursor.Current = Cursors.Default;
                path = Path.Combine(dir, script);
                if (!ok && !File.Exists(path))
                {
                    MessageBox.Show(this, "清理组件缺失且自动部署失败，请重新安装本工具后再试。", "C盘清理", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            string desc = (mode == "v8") ? "平衡保护清理（白名单保护，推荐）" : "极致深度清理（全量扫描缓存）";
            string preview = lastCleanScanBytes > 0
                ? "\n\n本次预览：" + FmtSize(lastCleanScanBytes) + " · " + lastCleanScanFiles.ToString("N0") + " 个临时/缓存文件"
                : "\n\n当前还没有扫描结果，建议先点「开始扫描」看一眼占用再清理。";
            if (mode == "v8")
                preview += "\n推荐清理会把用户目录中的项目送入回收站，可在回收站还原；Windows 系统目录（Temp / Prefetch / SoftwareDistribution）仍为永久删除。";
            else
                preview += "\n极致清理为永久删除，不进入回收站。";
            if (MessageBox.Show(this, desc + "将删除系统与用户目录下的临时文件、缓存与日志。\n这些均为可再生数据，不会涉及个人文件。" + preview + "\n\n是否继续？", "C盘清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            Audit.Write("系统清理", "模式 " + mode + "（" + desc + "）");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe");
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\" -Silent";
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Process p = Process.Start(psi);
                Log("[清理] " + desc + " 已启动 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                if (p != null)
                {
                    string tid = TaskHub.Start("系统清理", desc, "提权清理进程 " + p.Id);
                    Thread tw = new Thread(delegate()
                    {
                        try
                        {
                            p.WaitForExit();
                            if (p.ExitCode == 0) TaskHub.Done(tid, "清理进程已退出（退出码 0）");
                            else TaskHub.Fail(tid, "清理进程退出码 " + p.ExitCode);
                        }
                        catch (Exception ex2) { TaskHub.Fail(tid, "等待清理进程失败：" + ex2.Message); }
                    });
                    tw.IsBackground = true;
                    tw.Start();
                }
            }
            catch
            {
                MessageBox.Show(this, "启动失败，可能未授予管理员权限。", "C盘清理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================= 外部组件嵌入（SetParent 集成，不另开窗口） =================
        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")]
        static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);
        const int LVM_SETBKCOLOR = 0x1001;
        const int LVM_SETTEXTBKCOLOR = 0x1026;
        const int LVM_ENSUREVISIBLE = 0x1013;
        const int GWL_STYLE = -16;
        const int WS_CAPTION = 0x00C00000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_MAXIMIZEBOX = 0x00010000;
        const int SW_SHOW = 5;

        IntPtr EmbedInto(Process p, Panel host)
        {
            if (p == null || p.HasExited || host == null) return IntPtr.Zero;
            try
            {
                p.Refresh();
                if (p.MainWindowHandle == IntPtr.Zero) return IntPtr.Zero;
                IntPtr h = p.MainWindowHandle;
                SetParent(h, host.Handle);
                int style = GetWindowLong(h, GWL_STYLE);
                SetWindowLong(h, GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX));
                MoveWindow(h, 0, 0, Math.Max(host.ClientSize.Width, 10), Math.Max(host.ClientSize.Height, 10), true);
                ShowWindow(h, SW_SHOW);
                // 只有真正的 SAM.Game 成就页窗口才作为「一键全解锁」目标，Picker 窗口不算
                bool isGame = false;
                try { isGame = string.Equals(p.ProcessName, "SAM.Game", StringComparison.OrdinalIgnoreCase); }
                catch (Exception dg7329_1) { Diag.Swallow("MainForm.cs:3081", dg7329_1); }
                if (isGame) samGameHwnd = h;
                return h;
            }
            catch { return IntPtr.Zero; }
        }

        void EmbedTick()
        {
            // SAM.Picker / SAM.Game：优先接管独立弹出的 SAM.Game（双击游戏后 Picker 不一定会退出）
            if (samHost != null)
            {
                try
                {
                    Process game = null;
                    Process[] gs = Process.GetProcessesByName("SAM.Game");
                    if (gs.Length > 0) game = gs[0];
                    if (game != null)
                    {
                        game.Refresh();
                        IntPtr gh = game.MainWindowHandle;
                        if (gh != IntPtr.Zero)
                        {
                            int cw = Math.Max(samHost.ClientSize.Width, 10);
                            int chh = Math.Max(samHost.ClientSize.Height, 10);
                            procSam = game;               // 先切换跟踪目标，EmbedInto 内才会记录 samGameHwnd
                            if (GetParent(gh) != samHost.Handle) EmbedInto(game, samHost);
                            else
                            {
                                samGameHwnd = gh;
                                MoveWindow(gh, 0, 0, cw, chh, true);
                                ShowWindow(gh, SW_SHOW);
                            }
                            // 隐藏 Picker 窗口，避免与成就页重叠
                            Process[] ps = Process.GetProcessesByName("SAM.Picker");
                            foreach (Process pp in ps)
                            {
                                try
                                {
                                    if (pp.Id == game.Id) continue;
                                    IntPtr ph = pp.MainWindowHandle;
                                    if (ph != IntPtr.Zero && GetParent(ph) == samHost.Handle) ShowWindow(ph, 0);
                                }
                                catch (Exception dg7372_1) { Diag.Swallow("MainForm.cs:3124", dg7372_1); }
                            }
                            samGamePending = false;
                        }
                    }
                    else if (procSam != null && !procSam.HasExited)
                    {
                        try
                        {
                            procSam.Refresh();
                            if (procSam.MainWindowHandle != IntPtr.Zero)
                            {
                                IntPtr h = procSam.MainWindowHandle;
                                if (GetParent(h) != samHost.Handle) EmbedInto(procSam, samHost);
                                else
                                {
                                    MoveWindow(h, 0, 0, Math.Max(samHost.ClientSize.Width, 10), Math.Max(samHost.ClientSize.Height, 10), true);
                                    ShowWindow(h, SW_SHOW);
                                }
                            }
                        }
                        catch (Exception dg7393_1) { Diag.Swallow("MainForm.cs:3145", dg7393_1); }
                    }
                    else if (procSam != null && procSam.HasExited)
                    {
                        if (samGamePending && game == null)
                        {
                            // 还在等 SAM.Game 启动，保持状态文案
                        }
                        else
                        {
                            if (lblSamState != null) lblSamState.Text = T("组件已退出，可点「重新启动」", "Component exited, press Restart");
                            procSam = null;
                            samGameHwnd = IntPtr.Zero;
                        }
                    }
                }
                catch (Exception dg7409_1) { Diag.Swallow("MainForm.cs:3161", dg7409_1); }
            }
        }

        void KillEmbedded()
        {
            foreach (Process p in new Process[] { procSam })
            {
                if (p != null && !p.HasExited)
                {
                    try { p.Kill(); } catch (Exception dg7419_1) { Diag.Swallow("MainForm.cs:3171", dg7419_1); }
                    try { p.WaitForExit(2000); } catch (Exception dg7420_1) { Diag.Swallow("MainForm.cs:3172", dg7420_1); }
                }
            }
            procSam = null;
            samGameHwnd = IntPtr.Zero;
        }

        void ShowSamPage(bool show)
        {
            if (pageSam == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageVideo != null) pageVideo.Visible = false;
            if (pageMusic != null) pageMusic.Visible = false;
            pageSam.Visible = show;
            if (show)
            {
                lblPeer.Text = T("Steam成就", "Steam Achievements");
                lblPeer.ForeColor = UiKit.A(103, 232, 249);
                // 一体式：进页只准备控制台形态，不再自动拉起成就管理器窗口
                SamShowConsoleMode();
                SamProgress(samBatchRunning ? directDone : directTotal, directTotal);
            }
            else RestoreTimerHeader();
        }

        void EnsureSamEmbedded()
        {
            if (procSam != null && !procSam.HasExited) return;
            if (Process.GetProcessesByName("steam").Length == 0)
            {
                if (lblSamState != null) lblSamState.Text = T("Steam 未运行：请先启动 Steam 并登录账号。", "Steam not running: start & sign in first.");
                SamConsoleSet("【旧路径】Steam 未运行：先启动 Steam 并登录，再点「进游戏页解锁（旧）」。");
                return;
            }
            string samDir = Path.Combine(BASE_DIR, "sam_tool");
            string picker = Path.Combine(samDir, "SAM.Picker.exe");
            if (!File.Exists(picker))
            {
                Cursor.Current = Cursors.WaitCursor;
                bool ok = UnpackResource("SamZip", samDir, "Steam 成就管理");
                Cursor.Current = Cursors.Default;
                if (!ok || !File.Exists(picker))
                {
                    SamConsoleSet("【旧路径】Steam 成就组件缺失且自动部署失败，请重新安装本工具后再试。");
                    return;
                }
            }
            try
            {
                procSam = Process.Start(new ProcessStartInfo(picker) { WorkingDirectory = samDir });
                samGamePending = true;
                if (lblSamState != null) lblSamState.Text = T("正在启动并在主界面打开…", "Launching & opening in main view…");
            }
            catch (Exception ex)
            {
                SamConsoleSet("【旧路径】启动 Steam 成就管理失败：" + ex.Message);
            }
        }

        // ================= SAM 一键解锁：先检查不可解锁项，再排除后解锁 =================
        // 原理：SAM.Game 的工具栏按钮（解锁全部 / 提交修改）没有独立 HWND，只能走
        // UIAutomation 的 InvokePattern 或对 ToolStrip 投递鼠标消息；
        // 成就列表是 owner-data 的 ListView，文本与勾选状态只能通过 MSAA(IAccessible) 读取。

        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        class SamAch
        {
            public string Name;
            public bool Unlocked;
        }

        /// <summary>导出本机 Steam 全库游戏清单（AppID / 名称 / 是否受保护），供核对覆盖范围。</summary>
        void SamExportLibraryList()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                List<SchemaGame> games = ScanSchemaGames();
                Cursor.Current = Cursors.Default;
                if (games == null || games.Count == 0)
                {
                    MessageBox.Show(T("没读到游戏清单，确认 Steam 已安装并至少登录过一次。", "No games found — check Steam is installed and signed in."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("\"小工具箱 · Steam 全库清单\"");
                sb.AppendLine("\"导出时间\",\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"");
                sb.AppendLine("\"游戏总数\",\"" + games.Count + "\"");
                sb.AppendLine("");
                sb.AppendLine("\"AppID\",\"游戏\",\"分类\"");
                for (int i = 0; i < games.Count; i++)
                {
                    SchemaGame g = games[i];
                    string nm = g.Name == null ? "" : g.Name.Replace("\"", "\"\"");
                    string kind = g.Protected > 0 ? T("受保护（写入会被拦）", "Protected") : T("可直连解锁", "Direct unlockable");
                    sb.AppendLine("\"" + g.AppId + "\",\"" + nm + "\",\"" + kind + "\"");
                }
                string path = Path.Combine(BASE_DIR, "Steam全库清单_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Log("[Steam成就] 全库清单已导出：" + games.Count + " 款 → " + path);
                MessageBox.Show(T("已导出 ", "Saved ") + games.Count + T(" 款游戏清单：", " games: ") + path, T("完成", "Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select," + path); } catch (Exception dg7526_1) { Diag.Swallow("MainForm.cs:3278", dg7526_1); }
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(T("导出失败：", "Export failed: ") + ex.Message, T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SamSetState(string cn, string en)
        {
            try
            {
                if (lblSamState == null) return;
                string txt = T(cn, en);
                if (lblSamState.InvokeRequired)
                    lblSamState.BeginInvoke(new Action(delegate() { lblSamState.Text = txt; }));
                else
                    lblSamState.Text = txt;
            }
            catch (Exception dg7546_1) { Diag.Swallow("MainForm.cs:3298", dg7546_1); }
        }

        // v19.8：1920 行已原样迁到 MainForm.Steam.cs（Steam 解锁与工作台），逻辑未改
        // ================= 视频下载 / 音乐下载 =================
        void RestoreTimerHeader()
        {
            if (lblPeer == null) return;
            lblPeer.Text = curAction;
            lblPeer.ForeColor = curActionColor;
        }

        // 页面切换动效：页面变为可见时做一次轻微上滑淡入（临时解除 Dock，用 SetBounds 做动画）
        void HookPageAnim()
        {
            try
            {
                foreach (Control c in content.Controls)
                {
                    if (!(c is PagePanel)) continue;
                    c.VisibleChanged += delegate(object s, EventArgs e)
                    {
                        Control pc = s as Control;
                        if (pc != null && pc.Visible) AnimatePageIn(pc);
                    };
                }
            }
            catch (Exception dg9492_1) { Diag.Swallow("MainForm.cs:3325", dg9492_1); }
        }

        void AnimatePageIn(Control page)
        {
            if (page == null || page.IsDisposed || !page.Visible) return;
            try
            {
                Rectangle rc = content.DisplayRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;
                page.Dock = DockStyle.None;
                page.SetBounds(rc.X, rc.Y + 16, rc.Width, rc.Height);
                int frame = 0, total = 9;
                System.Windows.Forms.Timer tt = new System.Windows.Forms.Timer();
                tt.Interval = 16;
                tt.Tick += delegate
                {
                    try
                    {
                        if (page.IsDisposed || !page.Visible) { tt.Stop(); tt.Dispose(); return; }
                        frame++;
                        if (frame >= total)
                        {
                            page.Dock = DockStyle.Fill;
                            tt.Stop(); tt.Dispose(); return;
                        }
                        double k = (double)frame / total;
                        double ease = 1 - Math.Pow(1 - k, 3);
                        Rectangle mr = content.DisplayRectangle;
                        page.SetBounds(mr.X, mr.Y + (int)(16 * (1 - ease)), mr.Width, mr.Height);
                    }
                    catch { try { tt.Stop(); tt.Dispose(); } catch (Exception dg9523_1) { Diag.Swallow("MainForm.cs:3356", dg9523_1); } }
                };
                tt.Start();
            }
            catch (Exception dg9527_1) { Diag.Swallow("MainForm.cs:3360", dg9527_1); }
        }

        /// <summary>把单选框/复选框的底色抹成透明，避免系统在深色卡片上糊一块浅色方块。</summary>
        void FlattenCheckBacks(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is CheckBox || c is RadioButton)
                {
                    try { c.BackColor = Color.Transparent; } catch (Exception dg9537_1) { Diag.Swallow("MainForm.cs:3370", dg9537_1); }
                }
                if (c.HasChildren) FlattenCheckBacks(c);
            }
        }

        // ===== 调试：把控件树（类型/位置/背景色）导出成文本，定位界面异常 =====
        void DumpUiFile()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FORM " + ClientSize.Width + "x" + ClientSize.Height);
                DumpUi(this, sb, 0);
                File.WriteAllText(Path.Combine(BASE_DIR, "_uidump.txt"), sb.ToString(), Encoding.UTF8);
                Application.Exit();
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(BASE_DIR, "_uidump_err.txt"), ex.ToString(), Encoding.UTF8); } catch (Exception dg9556_1) { Diag.Swallow("MainForm.cs:3389", dg9556_1); }
            }
        }

        // Steam 成就页几何核对：一体式布局（卡片 / 工具条 / 控制台 / 进度条 / 状态标签）是否越界或重叠
        void DumpSamUiFile()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FORM " + ClientSize.Width + "x" + ClientSize.Height);
                if (pageSam != null)
                {
                    sb.AppendLine("PAGE client=" + pageSam.ClientSize.Width + "x" + pageSam.ClientSize.Height
                        + " visible=" + pageSam.Visible);
                    foreach (Control ch in pageSam.Controls) DumpSamNode(ch, sb, 0);
                }
                File.WriteAllText(Path.Combine(BASE_DIR, "_samdump.txt"), sb.ToString(), Encoding.UTF8);
                Application.Exit();
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(BASE_DIR, "_samdump_err.txt"), ex.ToString(), Encoding.UTF8); } catch (Exception dg9578_1) { Diag.Swallow("MainForm.cs:3411", dg9578_1); }
            }
        }

        // 递归导出 Steam 页控件树（卡片是 Panel，主按钮在卡片内部，只看第一层会漏掉）
        void DumpSamNode(Control ch, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 3);
            string txt = ch.Text == null ? "" : ch.Text.Replace("\r", " ").Replace("\n", " ");
            if (txt.Length > 26) txt = txt.Substring(0, 26);
            sb.AppendLine(pad + ch.GetType().Name + " [" + ch.Bounds.X + "," + ch.Bounds.Y + " "
                + ch.Bounds.Width + "x" + ch.Bounds.Height + "]"
                + (ch.Visible ? "" : " HIDDEN")
                + " bottom=" + (ch.Bounds.Y + ch.Bounds.Height)
                + " \"" + txt + "\"");
            if (depth < 2)
                foreach (Control sub in ch.Controls) DumpSamNode(sub, sb, depth + 1);
        }

        // Steam 页真实渲染截图：把整页画进位图，用于核对按钮是否真的被画出来（几何对但不显示时靠它定位）
        void SamShotFile()
        {
            try
            {
                ShowSamPage(true);
                Application.DoEvents();
                System.Threading.Thread.Sleep(350);
                Application.DoEvents();
                if (pageSam != null && pageSam.Width > 0)
                {
                    using (Bitmap bmp = new Bitmap(pageSam.Width, pageSam.Height))
                    {
                        pageSam.DrawToBitmap(bmp, new Rectangle(0, 0, pageSam.Width, pageSam.Height));
                        bmp.Save(Path.Combine(BASE_DIR, "_samshot.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(BASE_DIR, "_samshot_err.txt"), ex.ToString(), Encoding.UTF8); } catch (Exception dg9617_1) { Diag.Swallow("MainForm.cs:3450", dg9617_1); }
            }
            Application.Exit();
        }

        void DumpUi(Control c, StringBuilder sb, int d)
        {
            try
            {
                string pad = new string(' ', d * 2);
                string t = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
                if (t.Length > 22) t = t.Substring(0, 22);
                string extra = "";
                if (c is ListView)
                {
                    ListView lv = (ListView)c;
                    extra = " VIEW=" + lv.View + " OwnerDraw=" + lv.OwnerDraw + " Handle=" + lv.IsHandleCreated;
                }
                sb.AppendLine(pad + c.GetType().Name + " [" + c.Bounds.X + "," + c.Bounds.Y + " "
                    + c.Bounds.Width + "x" + c.Bounds.Height + "] bg=" + c.BackColor.ToArgb().ToString("X8")
                    + (c.Visible ? "" : " HIDDEN") + (c.Enabled ? "" : " DISABLED") + extra + " \"" + t + "\"");
                foreach (Control ch in c.Controls) DumpUi(ch, sb, d + 1);
            }
            catch (Exception dg9640_1) { Diag.Swallow("MainForm.cs:3473", dg9640_1); }
        }

        // v19.8：1437 行已原样迁到 MainForm.HomeValue.cs（工作台首页与 Steam 价值），逻辑未改
        // v19.8：2787 行已原样迁到 MainForm.ToolPages.cs（电脑配置/去水印/下载/音乐），逻辑未改
        // ================= 一键重装 =================
        void ShowResetPage(bool show)
        {
            if (pageReset == null) return;
            pageTimer.Visible = !show;
            if (pageClean != null) pageClean.Visible = false;
            pageReset.Visible = show;
            if (show) { lblPeer.Text = T("一键重装", "Reinstall"); lblPeer.ForeColor = C_ON; }
            else RestoreTimerHeader();
        }

        /// <summary>检测 Windows 恢复环境（WinRE）是否可用，给出重装前结论。</summary>
        void CheckRecoveryEnv()
        {
            Cursor.Current = Cursors.WaitCursor;
            string info = "";
            string err = "";
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "reagentc.exe";
                p.StartInfo.Arguments = "/info";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                StringBuilder esb = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) esb.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                info = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch (Exception dg13898_1) { Diag.Swallow("MainForm.cs:3509", dg13898_1); } }
                err = esb.ToString();
            }
            catch (Exception ex) { err = ex.Message; }
            Cursor.Current = Cursors.Default;

            string all = (info + "\n" + err).Trim();
            bool enabled = all.IndexOf("已启用", StringComparison.Ordinal) >= 0
                        || all.ToLower().IndexOf("enabled") >= 0;
            bool hasImage = Directory.Exists(@"C:\Recovery\WindowsRE")
                         || all.ToLower().IndexOf("winre.wim") >= 0;

            string verdict;
            if (enabled && hasImage)
                verdict = T("恢复环境可用：可以正常使用「重启进入恢复环境」和系统重置。", "WinRE is ready: recovery restart and PC reset are available.");
            else if (enabled)
                verdict = T("恢复环境已启用，但未找到恢复镜像目录（C:\\Recovery\\WindowsRE）。建议先执行 reagentc /enable 再重装。", "WinRE enabled but the image folder is missing; run reagentc /enable first.");
            else
                verdict = T("恢复环境未启用。请以管理员身份在命令提示符执行 reagentc /enable，或用「打开重装向导 / 系统恢复设置」两条路线。", "WinRE is not enabled. Run reagentc /enable as admin, or use the wizard / recovery settings paths.");

            try
            {
                string log = Path.Combine(BASE_DIR, "重装环境检测_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(log, "小工具箱 · Windows 恢复环境检测\n时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n结论：\n" + verdict + "\n\nreagentc /info 原始输出：\n" + all, new UTF8Encoding(true));
                Log("[重装] 环境检测：" + verdict);
                Clipboard.SetText(verdict + "\n\n" + all);
                MessageBox.Show(verdict + "\n\n（完整输出已复制到剪贴板，并存到 " + log + "）", T("恢复环境检测", "WinRE check"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show(verdict, T("恢复环境检测", "WinRE check"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void ConfirmReset(string mode)
        {
            string msg = "";
            if (mode == "wizard") msg = "将打开 Windows 重置向导。\n可选择「保留我的文件」或「删除所有内容」进行系统重装。";
            else if (mode == "winre") msg = "系统将重启并进入恢复环境。\n可在此选择「重置此电脑」、修复启动或进入命令提示符。";
            else msg = "将打开 Windows「设置 → 系统 → 恢复」页面。";
            if (MessageBox.Show(this, msg + "\n\n操作将影响系统，请确认已备份重要文件。\n是否继续？", "一键重装系统", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            Audit.Write("一键重装", "模式 " + mode);
            try
            {
                if (mode == "wizard")
                {
                    Process.Start("systemreset.exe");
                    Log("[重装] 打开重置向导 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else if (mode == "winre")
                {
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /o /t 0") { UseShellExecute = false, CreateNoWindow = true });
                    Log("[重装] 重启进入恢复环境 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else
                {
                    Process.Start(new ProcessStartInfo("ms-settings:reset") { UseShellExecute = true });
                    Log("[重装] 打开恢复设置 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================= 操作选择 =================
        // 侧栏选中态（所有页面分支都会提前 return，所以必须先刷新）
        void SidebarSelect(int idx)
        {
            sidebarSel = idx;
            for (int i = 0; i < ACTIONS.Length; i++)
            {
                convItem[i].BackColor = UiKit.Paper ? Color.FromArgb(31, 29, 24) : C_SIDE;
                if (UiKit.Paper)
                    convName[i].BackColor = (i == idx) ? Color.FromArgb(44, 40, 33) : Color.FromArgb(31, 29, 24);
                convName[i].ForeColor = UiKit.Paper
                    ? (i == idx ? Color.FromArgb(255, 252, 243) : Color.FromArgb(224, 215, 196))
                    : ((i == idx) ? UiKit.Txt(Color.White) : C_TEXT);
                foreach (Control c in convItem[i].Controls)
                {
                    if (c is Panel && (c.Tag as string) == "bar") c.Visible = (i == idx);
                    else if (c is Label && c.Font != null && Math.Abs(c.Font.Size - 7F) < 0.1F)
                    {
                        c.ForeColor = UiKit.Paper
                            ? (i == idx ? Color.FromArgb(205, 192, 168) : Color.FromArgb(178, 168, 148))
                            : ((i == idx) ? UiKit.A(174, 190, 214) : C_SUB);
                        if (UiKit.Paper)
                            c.BackColor = (i == idx) ? Color.FromArgb(44, 40, 33) : Color.FromArgb(31, 29, 24);
                    }
                }
                convItem[i].Invalidate();
            }
            try { if (navSlideTimer != null) navSlideTimer.Start(); } catch (Exception dg13981_1) { Diag.Swallow("MainForm.cs:3592", dg13981_1); }
        }

        protected override void OnMouseUp(MouseEventArgs e2)
        {
            base.OnMouseUp(e2);
            try
            {
                if (e2.Button == MouseButtons.XButton1 && prevSelIdx >= 0 && prevSelIdx < ACTIONS.Length)
                {
                    int back = prevSelIdx;
                    prevSelIdx = -1;
                    SelectAction(back);
                }
            }
            catch (Exception dg13996_1) { Diag.Swallow("MainForm.cs:3607", dg13996_1); }
        }

        // 切页只更新 ui.txt 里的一行，其它偏好保持原样
        void SavePagePref()
        {
            try
            {
                int si = (sidebarSel >= 0 && sidebarSel < SLOT.Length) ? sidebarSel : 0;
                string p = Path.Combine(BASE_DIR, "ui.txt");
                string cur = File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "";
                string line = "page=" + SLOT[si];
                StringBuilder sb = new StringBuilder();
                bool done = false;
                foreach (string ln0 in cur.Replace("\r\n", "\n").Split('\n'))
                {
                    string ln = ln0.Trim();
                    if (ln.Length == 0) continue;
                    if (ln.StartsWith("page="))
                    {
                        if (!done) { sb.AppendLine(line); done = true; }
                        continue;
                    }
                    sb.AppendLine(ln);
                }
                if (!done) sb.AppendLine(line);
                File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception dg14024_1) { Diag.Swallow("MainForm.cs:3635", dg14024_1); }
        }

        // 找页面里第一个能输入的控件并聚焦：切完页不用再点一下输入框
        Control FindFirstInput(Control root, int depth)
        {
            if (root == null || depth > 4) return null;
            foreach (Control c in root.Controls)
            {
                if (!c.Visible || !c.Enabled || !c.TabStop) continue;
                if (c is TextBox) return c;
                Control r = FindFirstInput(c, depth + 1);
                if (r != null) return r;
            }
            return null;
        }

        void FocusFirstInput(Panel pg)
        {
            try
            {
                if (pg == null || !pg.Visible) return;
                Control tb = FindFirstInput(pg, 0);
                if (tb == null) return;
                tb.Focus();
                TextBox box = tb as TextBox;
                if (box != null && box.TextLength > 0) box.Select(box.TextLength, 0);
            }
            catch (Exception dg14052_1) { Diag.Swallow("MainForm.cs:3663", dg14052_1); }
        }

        void HandleNavKeys(object s2, KeyEventArgs e2)
        {
            try
            {
                if (e2.Alt && !e2.Control && e2.KeyCode >= Keys.D1 && e2.KeyCode <= Keys.D9)
                {
                    int idx = e2.KeyCode - Keys.D1;
                    if (idx >= 0 && idx < ACTIONS.Length)
                    {
                        e2.SuppressKeyPress = true; e2.Handled = true;
                        SelectAction(idx);
                        return;
                    }
                }
                if (e2.Control && e2.KeyCode == Keys.Tab)
                {
                    int n = ACTIONS.Length;
                    if (n > 0)
                    {
                        int dir = e2.Shift ? -1 : 1;
                        SelectAction(((sidebarSel + dir) % n + n) % n);
                        e2.SuppressKeyPress = true; e2.Handled = true;
                    }
                }
            }
            catch (Exception dg14080_1) { Diag.Swallow("MainForm.cs:3691", dg14080_1); }
        }

        // v17.9：功能槽位 → 对应的页面容器，切页后据此刷 Header 标题
        Panel PageForSlot(string k)
        {
            if (k == "home") return pageHome;
            if (k == "clean") return pageClean;
            if (k == "sam") return pageSam;
            if (k == "wm") return pageWatermark;
            if (k == "val") return pageValue;
            if (k == "sys") return pageSys;
            if (k == "video") return pageVideo;
            if (k == "music") return pageMusic;
            return pageTimer;
        }

        void SelectAction(int idx)        {
            // v19.9：企业策略可禁用指定页，跳转前拦截
            string gateKey = (idx >= 0 && idx < SLOT.Length) ? SLOT[idx] : "power";
            if (!Policy.PageAllowed(gateKey))
            {
                Diag.Warn("策略禁用页面：" + gateKey);
                string nm = (idx >= 0 && idx < ACTIONS.Length && ACTIONS[idx] != null) ? ACTIONS[idx] : gateKey;
                try { Ui2.Toast(this, T("「" + nm + "」已被管理员策略禁用", "\"" + nm + "\" is disabled by policy")); } catch (Exception dgGate) { Diag.Swallow("MainForm.cs:gate", dgGate); }
                return;
            }
            if (idx == sidebarSel && idx >= 0 && VisiblePage() != null) { Sfx.Click(); return; }
            if (idx != sidebarSel && idx >= 0) prevSelIdx = sidebarSel;
            Sfx.Switch();
            HideAllPages(null);
            SidebarSelect(idx);
            string k = (idx >= 0 && idx < SLOT.Length) ? SLOT[idx] : "power";
            Panel pg = PageForSlot(k);
            if (k == "home") ShowHomePage(true);
            else if (k == "clean") ShowCleanPage(true);
            else if (k == "sam") ShowSamPage(true);
            else if (k == "wm") ShowWmPage(true);
            else if (k == "val") ShowValuePage(true);
            else if (k == "sys") ShowSysPage(true);
            else if (k == "video") ShowVideoPage(true);
            else if (k == "music") ShowMusicPage(true);
            else
            {
                pageTimer.Visible = true;
                LayoutTimerPage();
                try { BeginInvoke(new MethodInvoker(delegate { try { LayoutTimerPage(); } catch (Exception dg14118_1) { Diag.Swallow("MainForm.cs:3729", dg14118_1); } })); } catch (Exception dg14118_2) { Diag.Swallow("MainForm.cs:3729", dg14118_2); }
                if (pageReset != null && pageReset.Visible) ShowResetPage(false);
                if (pageClean != null && pageClean.Visible) ShowCleanPage(false);
                if (pageSam != null && pageSam.Visible) ShowSamPage(false);
                if (pageVideo != null && pageVideo.Visible) ShowVideoPage(false);
                if (pageMusic != null && pageMusic.Visible) ShowMusicPage(false);
                curAction = ACTIONS[idx];
                curActionColor = ACTION_COLOR[idx];
                lblPeer.Text = ACTIONS[idx];
                lblPeer.ForeColor = curActionColor;
                if (!taskActive) { lblStatus.Text = T("状态：空闲", "Status: Idle"); lblStatus.ForeColor = C_SUB; }
            }
            // v17.9：页面换了，Header 必须跟着换。原先只有定时页自己刷，
            // 其它页走 ShowXxxPage 都不刷，标题就一直停在「工作台」。
            SyncHeader(pg);
            SavePagePref();
            FocusFirstInput(pg);
        }

        // ================= 任务 =================
        string CurrentMode()
        {
            if (rbDatetime.Checked) return "datetime";
            if (rbDaily.Checked) return "daily";
            if (rbWeekly.Checked) return "weekly";
            return "countdown";
        }

        /// <summary>快捷定时：设定分钟数后立即开始倒计时任务。</summary>
        void PresetCountdown(int mins)
        {
            if (taskActive)
            {
                MessageBox.Show(T("已有任务在进行中，请先取消。", "A task is already running. Cancel it first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            rbCountdown.Checked = true;
            numH.Value = Math.Min(numH.Maximum, mins / 60);
            numM.Value = Math.Min(numM.Maximum, mins % 60);
            numS.Value = 0;
            UpdateModeUI();
            Log("[快捷定时] " + mins + T(" 分钟", " min"));
            StartTask();
        }

        void StartTask()
        {
            if (taskActive) { MessageBox.Show(T("已有任务在进行中，请先取消。", "A task is already running. Cancel it first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string mode = CurrentMode();
            cfg.Mode = mode;
            cfg.Action = curAction;
            cfg.NeedConfirm = chkConfirm.Checked;
            cfg.Sound = chkSound.Checked;
            cfg.ConfirmSecs = (int)numConfirm.Value;
            cfg.Hours = (int)numH.Value; cfg.Mins = (int)numM.Value; cfg.Secs = (int)numS.Value;
            cfg.AtTime = dtpTime.Value;
            cfg.Week = new bool[7];
            for (int i = 0; i < 7; i++) cfg.Week[i] = weekCb[i].Checked;

            bool periodic = false;
            DateTime target;
            int total;
            string desc;
            switch (mode)
            {
                case "countdown":
                    total = cfg.Hours * 3600 + cfg.Mins * 60 + cfg.Secs;
                    if (total <= 0) { MessageBox.Show(T("倒计时时间必须大于 0。", "Countdown must be greater than 0."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    target = DateTime.Now.AddSeconds(total);
                    desc = "将在 " + Fmt(total) + " 后" + curAction;
                    break;
                case "datetime":
                    target = cfg.AtTime;
                    if (target <= DateTime.Now) { MessageBox.Show(T("指定的时间必须在当前时间之后。", "The time must be in the future."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    total = (int)(target - DateTime.Now).TotalSeconds;
                    desc = "将在 " + target.ToString("yyyy-MM-dd HH:mm:ss") + " 执行" + curAction;
                    break;
                case "daily":
                    periodic = true;
                    target = CalcNextDaily(dtpDaily.Value);
                    total = Math.Max((int)(target - DateTime.Now).TotalSeconds, 1);
                    desc = "每天 " + dtpDaily.Value.ToString("HH:mm") + " 自动" + curAction;
                    break;
                case "weekly":
                    periodic = true;
                    if (!AnyWeekChecked()) { MessageBox.Show(T("请至少选择一个星期。", "Select at least one weekday."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    target = CalcNextWeekly(dtpWeekly.Value, cfg.Week);
                    total = Math.Max((int)(target - DateTime.Now).TotalSeconds, 1);
                    desc = "每周" + WeekNames() + " " + dtpWeekly.Value.ToString("HH:mm") + " 自动" + curAction;
                    break;
                default:
                    return;
            }

            taskActive = true;
            endTime = target;
            totalSec = total;
            curDesc = desc;
            confirmShown = false;
            cfg.Active = !periodic;
            cfg.Periodic = periodic;
            lblTimeSub.Text = periodic ? T("周期任务运行中", "Periodic task running") : T("任务进行中", "Task running");
            lblTyping.Visible = true;
            lblDetail.Text = desc;
            lblDetail.ForeColor = C_TEXT;
            lblStatus.Text = T("状态：任务进行中", "Status: Running");
            lblStatus.ForeColor = C_ON;
            ringPanel.Invalidate();
            Config.Save(BASE_DIR, cfg);
            Log("[设置] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " -> " + desc);
            // v9.2：开始任务不再共用「成功」的 ok，换成一段上升双音，跟"点完成功"分开
            Sfx.Launch();
        }

        bool AnyWeekChecked()
        {
            foreach (CheckBox cb in weekCb) if (cb.Checked) return true;
            return false;
        }

        string WeekNames()
        {
            string[] wd = langEn ? new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" } : new[] { "一", "二", "三", "四", "五", "六", "日" };
            var sb = new StringBuilder();
            for (int i = 0; i < 7; i++) if (weekCb[i].Checked) sb.Append(wd[i]);
            return sb.ToString();
        }

        DateTime CalcNextDaily(DateTime at)
        {
            DateTime t = DateTime.Today.Add(at.TimeOfDay);
            if (t <= DateTime.Now) t = t.AddDays(1);
            return t;
        }

        DateTime CalcNextWeekly(DateTime at, bool[] week)
        {
            DateTime t = DateTime.Today.Add(at.TimeOfDay);
            for (int i = 0; i < 8; i++)
            {
                int dow = ((int)t.DayOfWeek + 6) % 7; // 周一=0
                if (week[dow] && t > DateTime.Now) return t;
                t = t.AddDays(1);
            }
            return t;
        }

        void CancelTask()
        {
            if (!taskActive) { MessageBox.Show(T("当前没有进行中的任务。", "No task is currently running."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            taskActive = false;
            totalSec = 0;
            curDesc = "";
            confirmShown = false;
            cfg.Active = false;
            cfg.Periodic = false;
            if (confirmWin != null && !confirmWin.IsDisposed) confirmWin.CancelByUser();
            lblTime.Text = "--:--:--";
            lblTimeSub.Text = T("空闲 · 已取消", "Idle · Cancelled");
            lblTyping.Visible = false;
            lblDetail.Text = T("任务已取消，可重新设置", "Task cancelled — set a new one");
            lblDetail.ForeColor = UiKit.A(150, 162, 182);
            lblStatus.Text = "状态：空闲";
            lblStatus.ForeColor = C_SUB;
            ringPanel.Invalidate();
            Config.Save(BASE_DIR, cfg);
            Log("[取消] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            ClearPresetMarks();
            Sfx.Stop();
        }

        void Immediate()
        {
            if (MessageBox.Show("确定要立即" + curAction + "吗？\n未保存的工作可能丢失。", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            bool ok = RunSystemAction(curAction);
            Log("[立即执行] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + curAction + (ok ? " 成功" : " 失败"));
            if (!ok) MessageBox.Show("操作执行失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ================= 计时 =================
        void Tick()
        {
            if (!taskActive) return;
            double rem = (endTime - DateTime.Now).TotalSeconds;
            if (rem <= 0)
            {
                // 到点
                if (cfg.Periodic)
                {
                    if (cfg.NeedConfirm && !confirmShown)
                    {
                        confirmShown = true;
                        ShowConfirm(true);
                    }
                    else if (!cfg.NeedConfirm)
                    {
                        ExecutePeriodic();
                    }
                }
                else
                {
                    taskActive = false;
                    if (cfg.NeedConfirm && !confirmShown)
                    {
                        confirmShown = true;
                        ShowConfirm(false);
                    }
                    else if (!cfg.NeedConfirm)
                    {
                        ExecuteOnce();
                    }
                }
                return;
            }
            lblTime.Text = Fmt((int)rem);
            lblTimeSub.Text = cfg.Periodic ? "下次执行 " + endTime.ToString("HH:mm:ss") : "任务进行中";
            // v9.1：最后 3 秒每次整秒轻滴一声，倒计时"有节奏地"落到 0
            if (rem > 3.6) lastTickBeep = 99;
            else if (cfg.Sound)
            {
                int rs = (int)Math.Ceiling(rem);
                if (rs >= 1 && rs <= 3 && rs != lastTickBeep) { lastTickBeep = rs; Sfx.Tick3(); }
            }
            ringPanel.Invalidate();
        }

        string Fmt(int s)
        {
            int hh = s / 3600, mm = (s % 3600) / 60, ss = s % 60;
            return hh.ToString("00") + ":" + mm.ToString("00") + ":" + ss.ToString("00");
        }

        void ShowConfirm(bool periodic)
        {
            if (cfg.Sound) { Sfx.Alarm(); }
            if (confirmWin != null && !confirmWin.IsDisposed) confirmWin.Close();
            confirmWin = new ConfirmForm(curAction, cfg.ConfirmSecs, langEn,
                () => { if (periodic) ExecutePeriodic(); else ExecuteOnce(); },
                () =>
                {
                    if (periodic)
                    {
                        // 跳过本次，计算下一次
                        confirmShown = false;
                        if (cfg.Mode == "daily") endTime = CalcNextDaily(dtpDaily.Value);
                        else endTime = CalcNextWeekly(dtpWeekly.Value, cfg.Week);
                        totalSec = Math.Max((int)(endTime - DateTime.Now).TotalSeconds, 1);
                        Log("[周期跳过] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 跳过本次" + curAction);
                    }
                    else
                    {
                        taskActive = false;
                        totalSec = 0;
                        curDesc = "";
                        confirmShown = false;
                        lblTime.Text = "--:--:--";
                        lblTimeSub.Text = "空闲 · 已取消";
                        lblTyping.Visible = false;
                        lblDetail.Text = "到点后你选择了取消";
                        lblDetail.ForeColor = UiKit.A(150, 162, 182);
                        lblStatus.Text = "状态：空闲";
                        lblStatus.ForeColor = C_SUB;
                        cfg.Active = false; cfg.Periodic = false;
                        Log("[到点取消] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 用户取消" + curAction);
                    }
                    ringPanel.Invalidate();
                    Config.Save(BASE_DIR, cfg);
                });
            confirmWin.ShowDialog(this);
        }

        void ExecuteOnce()
        {
            // v10.0：任务真正执行了才记一次用量（分钟数取本次设定的总时长）
            try { UsageStats.Add(Math.Max(1, totalSec / 60), curAction); } catch (Exception dg14392_1) { Diag.Swallow("MainForm.cs:4003", dg14392_1); }
            taskActive = false;
            totalSec = 0;
            curDesc = "";
            confirmShown = false;
            cfg.Active = false; cfg.Periodic = false;
            lblTime.Text = "--:--:--";
            lblTimeSub.Text = "空闲";
            lblTyping.Visible = false;
            lblDetail.Text = "已执行「" + curAction + "」";
            lblDetail.ForeColor = UiKit.A(150, 162, 182);
            lblStatus.Text = "状态：空闲";
            lblStatus.ForeColor = C_SUB;
            ringPanel.Invalidate();
            Config.Save(BASE_DIR, cfg);
            Log("[执行] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + curAction);
            // v10.0：先跑动作链（收尾动作），再执行主任务
            try { RunChain(); } catch (Exception dg14409_1) { Diag.Swallow("MainForm.cs:4020", dg14409_1); }
            RunSystemAction(curAction);
        }

        void ExecutePeriodic()
        {
            confirmShown = false;
            // 计算下一次
            if (cfg.Mode == "daily") endTime = CalcNextDaily(dtpDaily.Value);
            else endTime = CalcNextWeekly(dtpWeekly.Value, cfg.Week);
            totalSec = Math.Max((int)(endTime - DateTime.Now).TotalSeconds, 1);
            Log("[周期执行] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + curAction + "，下次 " + endTime.ToString("yyyy-MM-dd HH:mm:ss"));
            Config.Save(BASE_DIR, cfg);
            if (cfg.Sound) { Sfx.Bell(); }
            RunSystemAction(curAction);
        }

        // ================= 日志 / 关于 =================
        void ShowLog()
        {
            LogForm f = new LogForm(logFile, langEn);
            f.ShowDialog(this);
        }

        // ===== 定时提醒：1 秒巡检，到点弹窗（托盘/后台也照常提醒）=====
        System.Windows.Forms.Timer remTimer;
        void InitReminders()
        {
            ReminderStore.Load(BASE_DIR);
            remTimer = new System.Windows.Forms.Timer();
            remTimer.Interval = 1000;
            remTimer.Tick += delegate { ReminderTick(); };
            remTimer.Start();
        }

        void ReminderTick()
        {
            try
            {
                List<ReminderItem> due = ReminderStore.TakeDue();
                for (int i = 0; i < due.Count; i++)
                {
                    ReminderPopup pop = new ReminderPopup(due[i]);
                    pop.Show();
                }
                if (due.Count > 0) ReminderStore.Save(BASE_DIR);
            }
            catch (Exception dg14456_1) { Diag.Swallow("MainForm.cs:4067", dg14456_1); }
        }

        void ShowReminders()
        {
            ReminderForm f = new ReminderForm();
            f.ShowDialog(this);
        }

        // ================= 主界面背景 =================
        void ShowBgMenu(Control anchor)
        {
            var m = new ContextMenuStrip();
            m.BackColor = UiKit.A(28, 32, 39);
            m.ForeColor = UiKit.Txt(Color.White);
            m.ShowImageMargin = false;
            var itPick = new ToolStripMenuItem(T("选择背景图片…", "Choose background image…"));
            var itClear = new ToolStripMenuItem(T("恢复默认背景", "Restore default background"));
            itPick.Click += (s, e) => ChooseBackground();
            itClear.Click += (s, e) => ClearBackground();
            m.Items.Add(itPick);
            m.Items.Add(itClear);
            m.Items.Add(new ToolStripSeparator());
            // v17.9：内置动态壁纸（极光 / 星尘 / 流光），选中即时生效，不重启
            var itDyn = new ToolStripMenuItem(T("动态壁纸", "Animated wallpaper"));
            string[] dname = new string[] { T("关闭", "Off"), T("极光", "Aurora"), T("星尘", "Dust"), T("流光", "Ribbon") };
            for (int i = 0; i < dname.Length; i++)
            {
                int si2 = i;
                var sub = new ToolStripMenuItem(dname[i] + ((bgStyle == i) ? "  ✓" : ""));
                sub.Click += (s, e) => SetBgStyle(si2);
                itDyn.DropDownItems.Add(sub);
            }
            m.Items.Add(itDyn);
            var itAlpha = new ToolStripMenuItem(T("背景强度", "Wallpaper opacity"));
            int[] av = new int[] { 25, 40, 55, 70, 85 };
            for (int i = 0; i < av.Length; i++)
            {
                int ai = av[i];
                var sub = new ToolStripMenuItem(ai + "%" + ((bgAlpha == ai) ? "  ✓" : ""));
                sub.Click += (s, e) => SetBgAlpha(ai);
                itAlpha.DropDownItems.Add(sub);
            }
            m.Items.Add(itAlpha);
            m.Items.Add(new ToolStripSeparator());
            var itInfo = new ToolStripMenuItem(T("背景仅显示在主界面；静态图片自动缩放到最长边 1920 像素", "Main view only; static images auto-resize to 1920px"));
            itInfo.Enabled = false;
            m.Items.Add(itInfo);
            m.Show(anchor, new Point(0, anchor.Height + 4));
        }

        // v19.8：401 行已原样迁到 MainForm.Wallpaper.cs（壁纸与动效），逻辑未改
        // ================= v6.7 一键体检 =================
        bool checkupAutoDump = false;
        bool bootAutoDump = false;
        // ================= v11.0 手机遥控 / 配置同步 自检 =================
        // ================= v12.2 三件新功能自检：/fstest =================
        void FileSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.AppendLine("== 文件秒搜 ==");
                sb.AppendLine("roots=" + string.Join(",", FileIndex.Roots()));
                FileIndex.Build(null, null, new string[] { BASE_DIR });
                int w = 0;
                while (FileIndex.Building && w < 240) { Thread.Sleep(50); w++; }
                sb.AppendLine("index=" + FileIndex.Count + " wait_ms=" + (w * 50));
                DateTime t0 = DateTime.Now;
                List<int> hits = new List<int>();
                int total = FileIndex.Search("cs", 20, hits);
                sb.AppendLine("q[cs] total=" + total + " ms=" + (int)(DateTime.Now - t0).TotalMilliseconds + " shown=" + hits.Count);
                for (int i = 0; i < hits.Count && i < 3; i++) sb.AppendLine("  " + Sz(FileIndex.Sizes[hits[i]]) + "  " + FileIndex.Paths[hits[i]]);
                total = FileIndex.Search("*.zip >1m", 20, hits);
                sb.AppendLine("q[*.zip >1m] total=" + total);
                total = FileIndex.Search("zzz_never_9999", 20, hits);
                sb.AppendLine("q[missing] total=" + total);
            }
            catch (Exception ex) { sb.AppendLine("FS EX " + ex.Message); }

            sb.AppendLine("== 自动化触发器 ==");
            try
            {
                string dir = Path.Combine(BASE_DIR, "_trigprobe");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "probe.txt"), "probe " + DateTime.Now.ToString("HHmmss"), Encoding.UTF8);
                sb.AppendLine("folder_newer(last=0)=" + FolderNewer(dir, "*.txt", 0));
                sb.AppendLine("folder_newer(last=now)=" + FolderNewer(dir, "*.txt", DateTime.Now.Ticks));
                sb.AppendLine("win_appeared(explorer,-6h)=" + WinAppeared("explorer", DateTime.Now.AddHours(-6).Ticks));
                sb.AppendLine("win_appeared(nope)=" + WinAppeared("zzz_nope_proc", 0));
                int before = TriggerCfg.Items.Count;
                TriggerCfg.Trig tt = new TriggerCfg.Trig();
                tt.Name = "自测_zzz";
                tt.Kind = "interval";
                tt.Spec = "5";
                tt.Action = "notify";
                tt.On = false;
                TriggerCfg.Items.Add(tt);
                TriggerCfg.Save();
                TriggerCfg.Load();
                bool found = false;
                for (int i = 0; i < TriggerCfg.Items.Count; i++) if (TriggerCfg.Items[i].Name == "自测_zzz") found = true;
                sb.AppendLine("cfg roundtrip found=" + found + " count=" + TriggerCfg.Items.Count);
                TriggerCfg.Items.RemoveAll(delegate (TriggerCfg.Trig x) { return x.Name == "自测_zzz"; });
                TriggerCfg.Save();
                TriggerCfg.Load();
                sb.AppendLine("cfg cleaned count=" + TriggerCfg.Items.Count + " (was " + before + ")");
                try { Directory.Delete(dir, true); } catch (Exception dg14963_1) { Diag.Swallow("MainForm.cs:4174", dg14963_1); }
            }
            catch (Exception ex2) { sb.AppendLine("TRIG EX " + ex2.Message); }

            sb.AppendLine("== 电源方案 ==");
            try
            {
                string raw = PowerListRaw();
                sb.AppendLine("raw_lines=" + raw.Replace("\r\n", "\n").Split('\n').Length);
                sb.AppendLine("now=" + PowerNow());
                sb.AppendLine("guid high=" + PowerGuid(raw, new string[] { "高性能", "high performance" }));
                sb.AppendLine("guid balanced=" + PowerGuid(raw, new string[] { "平衡", "balanced" }));
                sb.AppendLine("guid saver=" + PowerGuid(raw, new string[] { "节能", "power saver" }));
                string ult = PowerGuid(raw, new string[] { "卓越", "ultimate" });
                sb.AppendLine("guid ultimate=" + (ult.Length == 0 ? "（未创建，点按钮时自动建）" : ult));
            }
            catch (Exception ex3) { sb.AppendLine("POWER EX " + ex3.Message); }

            sb.AppendLine("== 插件 ==");
            try
            {
                sb.AppendLine("dir=" + PluginCfg.Dir + " files=" + PluginCfg.Files().Length);
                string[] pf = PluginCfg.Files();
                for (int i = 0; i < pf.Length && i < 3; i++) sb.AppendLine("  " + Path.GetFileName(pf[i]) + " → " + PluginCfg.Title(pf[i]));
            }
            catch (Exception ex4) { sb.AppendLine("PLUGIN EX " + ex4.Message); }

            File.WriteAllText(Path.Combine(BASE_DIR, "_fs_test.txt"), sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        void RemoteSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            PhoneRemoteServer s = null;
            try
            {
                s = new PhoneRemoteServer();
                s.StatusJson = delegate { return "{\"active\":true,\"left\":3600,\"action\":\"guanji\",\"total\":3600}"; };
                s.Cmd = delegate (string c) { sb.AppendLine("cmd 收到 " + c); return c == "zzz" ? "unknown" : "ok"; };
                string err = s.Start();
                sb.AppendLine("start err=" + (err == null ? "none" : err) + " port=" + s.Port);
                if (err == null)
                {
                    string bu = "http://127.0.0.1:" + s.Port;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        string page = wc.DownloadString(bu + "/");
                        sb.AppendLine("GET / len=" + page.Length + " 有status接口=" + page.Contains("/api/status") + " 有cmd接口=" + page.Contains("/api/cmd") + " 有关机键=" + page.Contains("shutdown"));
                        string js = wc.DownloadString(bu + "/api/status");
                        sb.AppendLine("GET /api/status -> " + js);
                        sb.AppendLine("GET /api/cmd add60 -> " + wc.DownloadString(bu + "/api/cmd?c=add60"));
                        sb.AppendLine("GET /api/cmd zzz   -> " + wc.DownloadString(bu + "/api/cmd?c=zzz"));
                        // v12.2 文件互传：目录页、下载、上传三件
                        try
                        {
                            string sd = Path.Combine(BASE_DIR, "_sharetest");
                            if (!Directory.Exists(sd)) Directory.CreateDirectory(sd);
                            s.ShareDir = sd;
                            string sp = Path.Combine(sd, "发送");
                            if (!Directory.Exists(sp)) Directory.CreateDirectory(sp);
                            File.WriteAllText(Path.Combine(sp, "测试下载.txt"), "mtb download test 2026", Encoding.UTF8);
                            string fp = wc.DownloadString(bu + "/files");
                            sb.AppendLine("GET /files len=" + fp.Length + " 有上传框=" + fp.Contains("type='file'") + " 有下载链接=" + fp.Contains("/dl?f="));
                            byte[] dl = wc.DownloadData(bu + "/dl?f=" + Uri.EscapeDataString("测试下载.txt"));
                            sb.AppendLine("GET /dl bytes=" + dl.Length + " body=" + Encoding.UTF8.GetString(dl));
                            byte[] payload = Encoding.UTF8.GetBytes("upload body 0123456789");
                            byte[] resp = wc.UploadData(bu + "/up?name=" + Uri.EscapeDataString("手机上传.txt"), "POST", payload);
                            sb.AppendLine("POST /up -> " + Encoding.UTF8.GetString(resp));
                            string got2 = Path.Combine(sd, "接收", "手机上传.txt");
                            sb.AppendLine("upload 落盘=" + (File.Exists(got2) ? (new FileInfo(got2).Length + "B " + File.ReadAllText(got2, Encoding.UTF8)) : "MISSING"));
                            try { Directory.Delete(sd, true); } catch (Exception dg15035_1) { Diag.Swallow("MainForm.cs:4246", dg15035_1); }
                        }
                        catch (Exception xex) { sb.AppendLine("互传 EX " + xex.Message); }
                    }
                    string[] urls = s.Urls();
                    sb.AppendLine("urls=" + (urls.Length > 0 ? urls[0] : "none"));
                }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.GetType().Name + " " + ex.Message); }
            finally { if (s != null) s.Stop(); }

            // 配置同步：打包 → base64 → 还原，验证条目一致
            try
            {
                string[] files = new string[] { "config.txt", "hotkeys.json", "主题色.txt", "动作链.txt", "每日一句.txt", "便签.dat" };
                using (MemoryStream ms = new MemoryStream())
                {
                    int put = 0;
                    using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        foreach (string fn in files)
                        {
                            string src = Path.Combine(BASE_DIR, fn);
                            if (!File.Exists(src)) continue;
                            ZipArchiveEntry ze = za.CreateEntry(fn, CompressionLevel.Optimal);
                            using (Stream es = ze.Open())
                            using (FileStream fs = File.OpenRead(src))
                                fs.CopyTo(es);
                            put++;
                        }
                    }
                    string code = "MTB1:" + Convert.ToBase64String(ms.ToArray());
                    sb.AppendLine("sync 打包文件=" + put + " 同步码长度=" + code.Length);
                    byte[] raw = Convert.FromBase64String(code.Substring(5));
                    int got = 0;
                    using (MemoryStream ms2 = new MemoryStream(raw))
                    using (ZipArchive za2 = new ZipArchive(ms2, ZipArchiveMode.Read))
                        foreach (ZipArchiveEntry ze in za2.Entries) { got++; sb.AppendLine("  sync 还原 " + ze.FullName + " " + ze.Length + "B"); }
                    sb.AppendLine("sync 还原条目=" + got + " 一致=" + (got == put));
                }
            }
            catch (Exception ex2) { sb.AppendLine("SYNC EX " + ex2.Message); }

            File.WriteAllText(Path.Combine(BASE_DIR, "_remote_test.txt"), sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        void XferSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            LanXferServer s = null;
            try
            {
                string inDir = Path.Combine(BASE_DIR, "_xfertest_in");
                string outDir = Path.Combine(BASE_DIR, "_xfertest_out");
                if (Directory.Exists(inDir)) Directory.Delete(inDir, true);
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(inDir);
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "从电脑下载.txt"), "hello phone", Encoding.UTF8);

                s = new LanXferServer(inDir, outDir);
                string err = s.Start();
                sb.AppendLine("start=" + (err == null ? "ok port=" + s.Port : "fail " + err));
                if (err == null)
                {
                    int p = s.Port;
                    string html;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "MiniToolbox/test";
                        html = wc.DownloadString("http://127.0.0.1:" + p + "/");
                    }
                    sb.AppendLine("GET / len=" + html.Length
                        + " uploadForm=" + html.Contains("action=\"/up\"")
                        + " outboxLink=" + html.Contains(Uri.EscapeDataString("从电脑下载.txt")));

                    string boundary = "----mtb" + Guid.NewGuid().ToString("N");
                    byte[] fileData = Encoding.UTF8.GetBytes("手机上传测试内容 12345");
                    StringBuilder body = new StringBuilder();
                    body.Append("--").Append(boundary).Append("\r\n");
                    body.Append("Content-Disposition: form-data; name=\"f\"; filename=\"手机上传测试.txt\"\r\n");
                    body.Append("Content-Type: application/octet-stream\r\n\r\n");
                    byte[] head = Encoding.UTF8.GetBytes(body.ToString());
                    byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
                    byte[] full = new byte[head.Length + fileData.Length + tail.Length];
                    Buffer.BlockCopy(head, 0, full, 0, head.Length);
                    Buffer.BlockCopy(fileData, 0, full, head.Length, fileData.Length);
                    Buffer.BlockCopy(tail, 0, full, head.Length + fileData.Length, tail.Length);

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + p + "/up");
                    req.Method = "POST";
                    req.ContentType = "multipart/form-data; boundary=" + boundary;
                    req.ContentLength = full.Length;
                    req.Timeout = 15000;
                    req.UserAgent = "MiniToolbox/test";
                    using (Stream rq = req.GetRequestStream()) rq.Write(full, 0, full.Length);
                    string respBody;
                    int code;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        code = (int)resp.StatusCode;
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            respBody = sr.ReadToEnd();
                    }
                    string savedPath = Path.Combine(inDir, "手机上传测试.txt");
                    sb.AppendLine("POST /up code=" + code + " respLen=" + respBody.Length + " saved=" + File.Exists(savedPath)
                        + (File.Exists(savedPath) ? " size=" + new FileInfo(savedPath).Length : ""));

                    byte[] dl;
                    using (WebClient wc2 = new WebClient()) dl = wc2.DownloadData("http://127.0.0.1:" + p + "/d/" + Uri.EscapeDataString("从电脑下载.txt"));
                    sb.AppendLine("GET /d/ bytes=" + (dl == null ? -1 : dl.Length));

                    byte[] miss;
                    try
                    {
                        using (WebClient wc3 = new WebClient()) miss = wc3.DownloadData("http://127.0.0.1:" + p + "/d/none.txt");
                        sb.AppendLine("GET 404 处理异常（应抛 404）");
                    }
                    catch (WebException wex)
                    {
                        HttpWebResponse hr = wex.Response as HttpWebResponse;
                        sb.AppendLine("GET 404 ok=" + (hr != null && hr.StatusCode == HttpStatusCode.NotFound));
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.GetType().Name + " " + ex.Message); }
            finally { if (s != null) s.Stop(); }
            File.WriteAllText(Path.Combine(BASE_DIR, "_xfer_test.txt"), sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        void BootSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                System.Collections.Generic.List<StartupEntry> list = CollectStartupItems();
                sb.AppendLine("count=" + list.Count);
                foreach (StartupEntry e in list)
                    sb.AppendLine(e.Kind + " | " + e.Source + " | " + e.Name + " | " + (e.Command ?? "").Substring(0, Math.Min(90, (e.Command ?? "").Length)));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
            File.WriteAllText(Path.Combine(BASE_DIR, "_boot_test.txt"), sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        static string ResNameOf(string key)
        {
            switch (key)
            {
                case "sam": return "SamZip";
                case "clean": return "CleanZip";
                case "video": return "VideoZip";
                case "ffmpeg": return "FfmpegZip";
            }
            return "";
        }

        string DirOf(string key)
        {
            switch (key)
            {
                case "sam": return Path.Combine(BASE_DIR, "sam_tool");
                case "clean": return Path.Combine(BASE_DIR, "clean_tool");
                case "video": return VideoToolDir();
                case "ffmpeg": return Path.Combine(BASE_DIR, "ffmpeg_tool");
            }
            return BASE_DIR;
        }

        int CountFiles(string dir)
        {
            try { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length; }
            catch { return -1; }
        }

        bool HttpOk(string url, int ms, out string err)
        {
            err = "";
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(0xF00 | 0xC00); } catch (Exception dg15218_1) { Diag.Swallow("MainForm.cs:4429", dg15218_1); }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = ms;
                req.ReadWriteTimeout = ms;
                req.UserAgent = "MiniToolbox/" + UiKit.VER;
                using (HttpWebResponse rs = (HttpWebResponse)req.GetResponse())
                    return rs.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        // ================= v6.8 增强工具中心 =================

        HotkeyManager hkMgr;
        bool v68AutoDump = false;
        List<Form> v68Forms = new List<Form>();

        // ===================== v11.0 全局搜索（Ctrl+K）=====================

        // 把「功能/页面」「热键」「便签」「剪贴板」「口令历史」揉成一份可搜索条目，
        // 复用指令舱那套条目结构与拼音首字母，Ctrl+K 直接搜全机。
        List<PaletteItem> BuildGlobalItems()
        {
            List<PaletteItem> list = new List<PaletteItem>();
            try
            {
                foreach (PaletteItem pi in BuildPaletteItems())
                {
                    PaletteItem c2 = new PaletteItem();
                    c2.Zh = pi.Zh; c2.En = pi.En; c2.Keys = pi.Keys;
                    c2.Hay = pi.Hay + " gongneng";
                    c2.Run = pi.Run;
                    list.Add(c2);
                }
            }
            catch (Exception ex) { Log("[全局搜索] 功能项 " + ex.Message); }
            try
            {
                if (hkMgr != null)
                {
                    foreach (HotkeyManager.Item it in hkMgr.Items)
                    {
                        if (it.Mods == 0) continue;
                        HotkeyManager.Item hit = it;
                        AddPal(list, T("热键：" + it.NameZh, "Hotkey: " + it.NameEn), "Hotkey: " + it.NameEn,
                            HotkeyManager.Display(it.Mods, it.Vk),
                            (it.NameZh + " " + it.NameEn + " rejian hotkey").ToLowerInvariant(),
                            delegate { DoHotkeyAction(hit.Key); });
                    }
                }
            }
            catch (Exception ex) { Log("[全局搜索] 热键 " + ex.Message); }
            try
            {
                string nf = Path.Combine(BASE_DIR, "便签.dat");
                if (File.Exists(nf))
                {
                    string[] nl = File.ReadAllLines(nf, Encoding.UTF8);
                    int ni = 0;
                    foreach (string ln in nl)
                    {
                        string txt = ln.Trim();
                        if (txt.Length == 0) continue;
                        ni++;
                        if (ni > 30) break;
                        string title = T("便签 " + ni + "：" + ClipHistStore.Preview(txt, 30), "Note " + ni + ": " + ClipHistStore.Preview(txt, 30));
                        string body = txt;
                        AddPal(list, title, title, T("便签", "note"), txt.ToLowerInvariant() + " bianqian note",
                            delegate { try { Clipboard.SetText(body); Sfx.Copy(); } catch (Exception dg15287_1) { Diag.Swallow("MainForm.cs:4498", dg15287_1); } });
                    }
                }
            }
            catch (Exception ex) { Log("[全局搜索] 便签 " + ex.Message); }
            try
            {
                if (clipHist != null)
                {
                    int ci = 0;
                    for (int i = 0; i < clipHist.Items.Count && ci < 40; i++)
                    {
                        ClipHistStore.Entry e = clipHist.Items[i];
                        ci++;
                        string body = e.Text;
                        string pv = ClipHistStore.Preview(e.Text, 26);
                        AddPal(list, T("剪贴板：" + pv, "Clip: " + pv), "Clip: " + pv, e.Time,
                            e.Text.ToLowerInvariant() + " jiantieban clipboard",
                            delegate { try { Clipboard.SetText(body); Sfx.Copy(); } catch (Exception dg15305_1) { Diag.Swallow("MainForm.cs:4516", dg15305_1); } });
                    }
                }
            }
            catch (Exception ex) { Log("[全局搜索] 剪贴板 " + ex.Message); }
            try
            {
                string hf = Path.Combine(BASE_DIR, "指令历史.txt");
                if (File.Exists(hf))
                {
                    string[] hl = File.ReadAllLines(hf, Encoding.UTF8);
                    int hi = 0;
                    for (int i = hl.Length - 1; i >= 0 && hi < 20; i--)
                    {
                        string ln = hl[i].Trim();
                        if (ln.Length == 0) continue;
                        hi++;
                        string q = ln;
                        AddPal(list, T("口令：" + q, "Command: " + q), "Command: " + q, T("历史", "hist"),
                            q.ToLowerInvariant() + " kouling command",
                            delegate { ShowVoiceCmd(); });
                    }
                }
            }
            catch (Exception ex) { Log("[全局搜索] 口令 " + ex.Message); }
            return list;
        }

        // ================= v12.2 文件秒搜 =================
        static string Sz(long n)
        {
            if (n < 1024) return n + " B";
            if (n < 1024L * 1024) return (n / 1024.0).ToString("0.0") + " KB";
            if (n < 1024L * 1024 * 1024) return (n / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (n / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        // ================= v12.2 触发器引擎：每 15 秒扫一遍 =================
        void StartTriggerEngine()
        {
            if (trigTimer != null) return;
            trigTimer = new System.Windows.Forms.Timer();
            trigTimer.Interval = 15000;
            trigTimer.Tick += delegate { TriggerTick(); };
            trigTimer.Start();
        }

        void TriggerTick()
        {
            try
            {
                if (TriggerCfg.Items.Count == 0) return;
                DateTime now = DateTime.Now;
                bool changed = false;
                for (int i = 0; i < TriggerCfg.Items.Count; i++)
                {
                    TriggerCfg.Trig t = TriggerCfg.Items[i];
                    if (!t.On) continue;
                    string kind = t.Kind == null ? "" : t.Kind.Trim().ToLowerInvariant();
                    string spec = t.Spec == null ? "" : t.Spec.Trim();
                    bool fire = false;
                    if (kind == "time")
                    {
                        if (spec == now.ToString("HH:mm"))
                        {
                            DateTime lastD = t.LastTicks > 0 ? new DateTime(t.LastTicks) : DateTime.MinValue;
                            fire = lastD.Date != now.Date;
                        }
                    }
                    else if (kind == "interval")
                    {
                        int mins = 30;
                        try { mins = int.Parse(spec); } catch (Exception dg15377_1) { Diag.Swallow("MainForm.cs:4588", dg15377_1); }
                        if (mins < 1) mins = 1;
                        if (t.LastTicks == 0) fire = true;
                        else if ((now - new DateTime(t.LastTicks)).TotalMinutes >= mins) fire = true;
                    }
                    else if (kind == "boot")
                    {
                        if (t.LastTicks == 0 && (now - bootTime).TotalSeconds > 60) fire = true;
                    }
                    else if (kind == "folder")
                    {
                        string dir = spec;
                        string pat = "*.*";
                        int bar = dir.IndexOf('|');
                        if (bar >= 0) { pat = dir.Substring(bar + 1).Trim(); dir = dir.Substring(0, bar).Trim(); }
                        fire = FolderNewer(dir, pat, t.LastTicks);
                    }
                    else if (kind == "window")
                    {
                        fire = WinAppeared(spec, t.LastTicks);
                    }
                    if (!fire) continue;
                    t.LastTicks = now.Ticks;
                    changed = true;
                    RunTriggerAction(t.Action, t.Name);
                }
                if (changed) TriggerCfg.Save();
            }
            catch (Exception ex) { try { Log("[触发器] " + ex.Message); } catch (Exception dg15405_1) { Diag.Swallow("MainForm.cs:4616", dg15405_1); } }
        }

        void ShowFileSearch()
        {
            Form f = new Form();
            f.Text = T("文件秒搜 · 小工具箱", "File search · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(1060, 664);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;

            Label ttl = new Label();
            ttl.Text = T("文件秒搜", "File search");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("只搜文件名与路径 · 支持 *.mp4、>100M、<10K 过滤 · 索引落在 文件索引.dat",
                        "Filename and path only · supports *.mp4 / >100M / <10K");
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(1020, 20);
            f.Controls.Add(sub);

            Panel pane = new Panel();
            pane.Location = new Point(20, 76);
            pane.Size = new Size(1020, 40);
            pane.BackColor = UiKit.A(30, 36, 46);
            f.Controls.Add(pane);

            TextBox box = new TextBox();
            box.Location = new Point(10, 10);
            box.Size = new Size(996, 22);
            box.BackColor = UiKit.A(30, 36, 46);
            box.ForeColor = UiKit.Txt(Color.White);
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Microsoft YaHei UI", 10.5F);
            pane.Controls.Add(box);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = C_SUB;
            st.Location = new Point(20, 124);
            st.Size = new Size(1020, 18);
            f.Controls.Add(st);

            ListView lv = MakeDarkList(new string[] { T("名称", "Name"), T("大小", "Size"), T("修改时间", "Modified"), T("所在目录", "Folder") },
                new int[] { 250, 90, 130, 530 }, 20, 148, 1020, 440);
            f.Controls.Add(lv);

            Button bIdx = MakeBtn(T("重建索引", "Rebuild index"), C_BUBBLE_ALT(), Color.White, 20, 604, 130, 34);
            Button bOpen = MakeBtn(T("打开目录", "Open folder"), C_BUBBLE_ALT(), Color.White, 160, 604, 120, 34);
            Button bCopy = MakeBtn(T("复制路径", "Copy path"), C_BUBBLE_ALT(), Color.White, 290, 604, 110, 34);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 920, 604, 120, 34);
            f.Controls.Add(bIdx);
            f.Controls.Add(bOpen);
            f.Controls.Add(bCopy);
            f.Controls.Add(bClose);

            List<int> hits = new List<int>();

            Action idxState = delegate
            {
                try
                {
                    if (FileIndex.Building) { st.Text = T("索引正在建立…（第一次约 1-3 分钟）", "Building index…"); return; }
                    if (FileIndex.Count == 0) { st.Text = T("索引为空，点「重建索引」开始扫盘", "Index empty — press Rebuild index"); return; }
                    st.Text = T("索引 " + FileIndex.Count + " 个文件 · 建于 " + FileIndex.Built.ToString("MM-dd HH:mm"),
                                "Index: " + FileIndex.Count + " files · " + FileIndex.Built.ToString("MM-dd HH:mm"));
                }
                catch (Exception dg15483_1) { Diag.Swallow("MainForm.cs:4694", dg15483_1); }
            };

            Action doSearch = null;
            doSearch = delegate
            {
                if (FileIndex.Count == 0) { idxState(); return; }
                string q = box.Text.Trim();
                if (q.Length == 0) { lv.Items.Clear(); idxState(); return; }
                DateTime t0 = DateTime.Now;
                int total = FileIndex.Search(q, 400, hits);
                lv.BeginUpdate();
                lv.Items.Clear();
                for (int i = 0; i < hits.Count; i++)
                {
                    int ix = hits[i];
                    string pth = FileIndex.Paths[ix];
                    ListViewItem it = new ListViewItem(Path.GetFileName(pth));
                    it.SubItems.Add(Sz(FileIndex.Sizes[ix]));
                    string dt = "";
                    try { dt = File.GetLastWriteTime(pth).ToString("yyyy-MM-dd HH:mm"); } catch (Exception dg15503_1) { Diag.Swallow("MainForm.cs:4714", dg15503_1); }
                    it.SubItems.Add(dt);
                    it.SubItems.Add(Path.GetDirectoryName(pth));
                    it.Tag = pth;
                    lv.Items.Add(it);
                }
                lv.EndUpdate();
                int ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                st.Text = T("命中 " + total + " 条 · 显示前 " + hits.Count + " 条 · " + ms + " ms · 索引 " + FileIndex.Count + " 个文件",
                            "Hits " + total + " · showing " + hits.Count + " · " + ms + " ms · index " + FileIndex.Count);
            };

            Action doBuild = delegate
            {
                if (FileIndex.Building) { st.Text = T("正在建立，请稍等…", "Building…"); return; }
                st.Text = T("正在扫盘建立索引…", "Scanning disks…");
                FileIndex.Build(
                    delegate (int n) { try { st.Text = T("已扫 " + n + " 个文件…", "Scanned " + n); } catch (Exception dg15520_1) { Diag.Swallow("MainForm.cs:4731", dg15520_1); } },
                    delegate (bool ok)
                    {
                        try
                        {
                            st.Text = T("索引完成：" + FileIndex.Count + " 个文件", "Done: " + FileIndex.Count);
                            if (box.Text.Trim().Length > 0) doSearch();
                        }
                        catch (Exception dg15528_1) { Diag.Swallow("MainForm.cs:4739", dg15528_1); }
                    }, null);
            };

            System.Windows.Forms.Timer db = new System.Windows.Forms.Timer();
            db.Interval = 240;
            db.Tick += delegate { db.Stop(); doSearch(); };
            box.TextChanged += delegate { db.Stop(); db.Start(); };
            f.FormClosed += delegate { try { db.Stop(); db.Dispose(); } catch (Exception dg15536_1) { Diag.Swallow("MainForm.cs:4747", dg15536_1); } };

            bIdx.Click += delegate { doBuild(); };
            bOpen.Click += delegate
            {
                try
                {
                    if (lv.SelectedItems.Count == 0) return;
                    string p2 = (string)lv.SelectedItems[0].Tag;
                    if (p2 == null || p2.Length == 0) return;
                    Process.Start("explorer.exe", "/select,\"" + p2 + "\"");
                }
                catch (Exception dg15548_1) { Diag.Swallow("MainForm.cs:4759", dg15548_1); }
            };
            bCopy.Click += delegate
            {
                try
                {
                    if (lv.SelectedItems.Count == 0) return;
                    Clipboard.SetText((string)lv.SelectedItems[0].Tag);
                    st.Text = T("路径已复制", "Path copied");
                }
                catch (Exception dg15558_1) { Diag.Swallow("MainForm.cs:4769", dg15558_1); }
            };
            lv.DoubleClick += delegate
            {
                try
                {
                    if (lv.SelectedItems.Count == 0) return;
                    string p2 = (string)lv.SelectedItems[0].Tag;
                    if (File.Exists(p2)) Process.Start("\"" + p2 + "\"");
                }
                catch (Exception dg15568_1) { Diag.Swallow("MainForm.cs:4779", dg15568_1); }
            };
            bClose.Click += delegate { f.Close(); };

            f.Shown += delegate
            {
                string[] av = Environment.GetCommandLineArgs();
                bool devRun = av.Length > 1 && av[1] != null && av[1].StartsWith("/");
                if (FileIndex.Count == 0 && !FileIndex.Building && !devRun) doBuild();
                else idxState();
            };
            ShowOrTrack(f);
            f.Show();
        }

        // ================= v12.2 自动化触发器 =================
        static readonly Dictionary<string, DateTime> seenWin = new Dictionary<string, DateTime>();

        static string KindZh(string k)
        {
            switch (k)
            {
                case "time": return "时间";
                case "interval": return "间隔";
                case "boot": return "开机";
                case "folder": return "文件夹";
                case "window": return "窗口";
            }
            return k;
        }

        static string ActZh(string a)
        {
            switch (a)
            {
                case "chain": return "动作链";
                case "lock": return "锁定";
                case "mute": return "静音";
                case "temp": return "清临时";
                case "notify": return "通知";
                case "shutdown": return "关机";
                case "restart": return "重启";
            }
            return a;
        }

        void EditTriggerDlg(TriggerCfg.Trig src, bool isNew, Form owner)
        {
            Form d = new Form();
            d.Text = T(isNew ? "新增触发器 · 小工具箱" : "编辑触发器 · 小工具箱", "Trigger · Mini Toolbox");
            d.BackColor = UiKit.A(20, 24, 30);
            d.StartPosition = FormStartPosition.CenterParent;
            d.ClientSize = new Size(560, 344);
            d.ShowIcon = false;
            d.Font = new Font("Microsoft YaHei UI", 9F);
            d.FormBorderStyle = FormBorderStyle.FixedSingle;
            d.MaximizeBox = false;

            Label ttl = new Label();
            ttl.Text = T("触发器", "Trigger");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            d.Controls.Add(ttl);

            Label lName = new Label(); lName.Text = T("名称", "Name"); lName.ForeColor = C_SUB; lName.Location = new Point(20, 54); lName.Size = new Size(80, 18); d.Controls.Add(lName);
            Panel pName = new Panel(); pName.BackColor = UiKit.A(30, 36, 46); pName.Location = new Point(110, 50); pName.Size = new Size(420, 32); d.Controls.Add(pName);
            TextBox tbName = new TextBox(); tbName.Text = src == null ? "" : src.Name; tbName.BackColor = UiKit.A(30, 36, 46); tbName.ForeColor = UiKit.Txt(Color.White); tbName.BorderStyle = BorderStyle.None; tbName.Location = new Point(9, 7); tbName.Size = new Size(400, 22); tbName.Font = new Font("Microsoft YaHei UI", 10F); pName.Controls.Add(tbName);

            Label lKind = new Label(); lKind.Text = T("类型", "Kind"); lKind.ForeColor = C_SUB; lKind.Location = new Point(20, 96); lKind.Size = new Size(80, 18); d.Controls.Add(lKind);
            Button bKind = MakeBtn(KindZh(src == null ? "time" : src.Kind), UiKit.A(38, 46, 60), Color.White, 110, 92, 130, 34);
            d.Controls.Add(bKind);

            Label lSpec = new Label(); lSpec.Text = T("参数", "Spec"); lSpec.ForeColor = C_SUB; lSpec.Location = new Point(20, 138); lSpec.Size = new Size(80, 18); d.Controls.Add(lSpec);
            Panel pSpec = new Panel(); pSpec.BackColor = UiKit.A(30, 36, 46); pSpec.Location = new Point(110, 134); pSpec.Size = new Size(420, 32); d.Controls.Add(pSpec);
            TextBox tbSpec = new TextBox(); tbSpec.Text = src == null ? "" : src.Spec; tbSpec.BackColor = UiKit.A(30, 36, 46); tbSpec.ForeColor = UiKit.Txt(Color.White); tbSpec.BorderStyle = BorderStyle.None; tbSpec.Location = new Point(9, 7); tbSpec.Size = new Size(400, 22); tbSpec.Font = new Font("Microsoft YaHei UI", 10F); pSpec.Controls.Add(tbSpec);

            Label lHint = new Label();
            lHint.Text = T("时间填 09:30 · 间隔填分钟数 · 开机填 0 · 文件夹填 D:\\下载|*.exe · 窗口填 chrome",
                          "time 09:30 · interval minutes · boot 0 · folder D:\\dl|*.exe · window chrome");
            lHint.ForeColor = UiKit.A(140, 152, 172);
            lHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            lHint.Location = new Point(20, 176);
            lHint.Size = new Size(510, 20);
            d.Controls.Add(lHint);

            Label lAct = new Label(); lAct.Text = T("动作", "Action"); lAct.ForeColor = C_SUB; lAct.Location = new Point(20, 206); lAct.Size = new Size(80, 18); d.Controls.Add(lAct);
            Button bAct = MakeBtn(ActZh(src == null ? "chain" : src.Action), UiKit.A(38, 46, 60), Color.White, 110, 202, 130, 34);
            d.Controls.Add(bAct);

            string[] kinds = new string[] { "time", "interval", "boot", "folder", "window" };
            string[] acts = new string[] { "chain", "lock", "mute", "temp", "notify", "shutdown", "restart" };
            int ki = 0, ai = 0;
            string k0 = src == null ? "time" : src.Kind;
            string a0 = src == null ? "chain" : src.Action;
            for (int i = 0; i < kinds.Length; i++) if (kinds[i] == k0) ki = i;
            for (int i = 0; i < acts.Length; i++) if (acts[i] == a0) ai = i;

            bKind.Click += delegate { ki = (ki + 1) % kinds.Length; bKind.Text = KindZh(kinds[ki]); Sfx.Click(); };
            bAct.Click += delegate { ai = (ai + 1) % acts.Length; bAct.Text = ActZh(acts[ai]); Sfx.Click(); };

            Button bOk = MakeBtn(T("保存", "Save"), UiKit.ThemeColor, Color.White, 300, 264, 110, 34);
            Button bCancel = MakeBtn(T("取消", "Cancel"), C_BUBBLE_ALT(), Color.White, 420, 264, 110, 34);
            d.Controls.Add(bOk);
            d.Controls.Add(bCancel);
            bCancel.Click += delegate { d.DialogResult = DialogResult.Cancel; d.Close(); };
            bOk.Click += delegate
            {
                string nm = tbName.Text.Trim();
                if (nm.Length == 0) nm = KindZh(kinds[ki]) + " " + tbSpec.Text.Trim();
                TriggerCfg.Trig t = src;
                if (t == null) { t = new TriggerCfg.Trig(); t.On = true; }
                t.Name = nm;
                t.Kind = kinds[ki];
                t.Spec = tbSpec.Text.Trim();
                t.Action = acts[ai];
                TriggerCfg.Save();
                d.DialogResult = DialogResult.OK;
                d.Close();
            };
            d.ShowDialog(owner);
        }

        void ShowTriggers()
        {
            Form f = new Form();
            f.Text = T("自动化触发器 · 小工具箱", "Triggers · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(920, 592);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;

            Label ttl = new Label();
            ttl.Text = T("自动化触发器", "Triggers");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("到点 / 每隔 N 分钟 / 开机后 / 文件夹有新增 / 窗口出现 → 自动执行一个动作。每 15 秒检查一次。",
                        "On a time, every N minutes, after boot, new file in a folder, or an app appearing — run an action. Checked every 15s.");
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(880, 32);
            f.Controls.Add(sub);

            ListView lv = MakeDarkList(new string[] { T("开", "On"), T("名称", "Name"), T("类型", "Kind"), T("参数", "Spec"), T("动作", "Action"), T("上次触发", "Last") },
                new int[] { 50, 180, 88, 250, 110, 160 }, 20, 92, 880, 372);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = C_SUB;
            st.Location = new Point(20, 474);
            st.Size = new Size(880, 18);
            f.Controls.Add(st);

            Action refresh = null;
            refresh = delegate
            {
                lv.BeginUpdate();
                lv.Items.Clear();
                for (int i = 0; i < TriggerCfg.Items.Count; i++)
                {
                    TriggerCfg.Trig t = TriggerCfg.Items[i];
                    ListViewItem it = new ListViewItem(t.On ? "✓" : "—");
                    it.SubItems.Add(t.Name);
                    it.SubItems.Add(KindZh(t.Kind));
                    it.SubItems.Add(t.Spec.Length == 0 ? "—" : t.Spec);
                    it.SubItems.Add(ActZh(t.Action));
                    string last = t.LastTicks > 0 ? new DateTime(t.LastTicks).ToString("MM-dd HH:mm:ss") : "—";
                    it.SubItems.Add(last);
                    it.Tag = i;
                    lv.Items.Add(it);
                }
                lv.EndUpdate();
                st.Text = T("共 " + TriggerCfg.Items.Count + " 条 · 配置文件 触发器.txt", "Total " + TriggerCfg.Items.Count + " · 触发器.txt");
            };

            Button bAdd = MakeBtn(T("新增", "Add"), UiKit.ThemeColor, Color.White, 20, 508, 100, 34);
            Button bEdit = MakeBtn(T("编辑", "Edit"), C_BUBBLE_ALT(), Color.White, 130, 508, 100, 34);
            Button bDel = MakeBtn(T("删除", "Delete"), C_BUBBLE_ALT(), Color.White, 240, 508, 100, 34);
            Button bTog = MakeBtn(T("启用/停用", "Toggle"), C_BUBBLE_ALT(), Color.White, 350, 508, 120, 34);
            Button bTest = MakeBtn(T("立刻执行", "Run now"), C_BUBBLE_ALT(), Color.White, 480, 508, 120, 34);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 790, 508, 110, 34);
            f.Controls.Add(bAdd);
            f.Controls.Add(bEdit);
            f.Controls.Add(bDel);
            f.Controls.Add(bTog);
            f.Controls.Add(bTest);
            f.Controls.Add(bClose);

            bAdd.Click += delegate
            {
                TriggerCfg.Trig t = new TriggerCfg.Trig();
                t.Name = "";
                EditTriggerDlg(t, true, f);
                if (t.Name.Length > 0) { TriggerCfg.Items.Add(t); TriggerCfg.Save(); Sfx.Click(); refresh(); }
            };
            bEdit.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int ix = (int)lv.SelectedItems[0].Tag;
                if (ix < 0 || ix >= TriggerCfg.Items.Count) return;
                EditTriggerDlg(TriggerCfg.Items[ix], false, f);
                TriggerCfg.Save();
                refresh();
            };
            bDel.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int ix = (int)lv.SelectedItems[0].Tag;
                if (ix < 0 || ix >= TriggerCfg.Items.Count) return;
                TriggerCfg.Items.RemoveAt(ix);
                TriggerCfg.Save();
                Sfx.Click();
                refresh();
            };
            bTog.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int ix = (int)lv.SelectedItems[0].Tag;
                if (ix < 0 || ix >= TriggerCfg.Items.Count) return;
                TriggerCfg.Items[ix].On = !TriggerCfg.Items[ix].On;
                TriggerCfg.Save();
                Sfx.Click();
                refresh();
            };
            bTest.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int ix = (int)lv.SelectedItems[0].Tag;
                if (ix < 0 || ix >= TriggerCfg.Items.Count) return;
                TriggerCfg.Trig t = TriggerCfg.Items[ix];
                RunTriggerAction(t.Action, t.Name);
                st.Text = T("已执行：" + t.Name + " → " + ActZh(t.Action), "Ran: " + t.Name);
            };
            bClose.Click += delegate { f.Close(); };

            refresh();
            ShowOrTrack(f);
            f.Show();
        }

        bool FolderNewer(string dir, string pat, long lastTicks)
        {
            try
            {
                if (dir == null || dir.Length == 0 || !Directory.Exists(dir)) return false;
                if (pat == null || pat.Trim().Length == 0) pat = "*.*";
                DateTime last = lastTicks > 0 ? new DateTime(lastTicks) : DateTime.MinValue;
                string[] fs = Directory.GetFiles(dir, pat.Trim(), SearchOption.TopDirectoryOnly);
                for (int i = 0; i < fs.Length; i++)
                {
                    try { if (File.GetLastWriteTime(fs[i]) > last) return true; } catch (Exception dg15829_1) { Diag.Swallow("MainForm.cs:5040", dg15829_1); }
                }
            }
            catch (Exception dg15832_1) { Diag.Swallow("MainForm.cs:5043", dg15832_1); }
            return false;
        }

        bool WinAppeared(string spec, long lastTicks)
        {
            try
            {
                string nm = spec == null ? "" : spec.Trim().ToLowerInvariant();
                if (nm.EndsWith(".exe")) nm = nm.Substring(0, nm.Length - 4);
                if (nm.Length == 0) return false;
                DateTime last = lastTicks > 0 ? new DateTime(lastTicks) : DateTime.MinValue;
                Process[] ps = Process.GetProcessesByName(nm);
                for (int i = 0; i < ps.Length; i++)
                {
                    try { if (ps[i].StartTime > last) return true; } catch (Exception dg15847_1) { Diag.Swallow("MainForm.cs:5058", dg15847_1); }
                }
            }
            catch (Exception dg15850_1) { Diag.Swallow("MainForm.cs:5061", dg15850_1); }
            return false;
        }

        void RunTriggerAction(string a, string name)
        {
            try
            {
                string k = (a == null ? "" : a.Trim().ToLowerInvariant());
                if (k.StartsWith("cmd:"))
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c " + a.Substring(4)) { CreateNoWindow = true, UseShellExecute = false });
                }
                else if (k.StartsWith("open:"))
                {
                    Process.Start(a.Substring(5));
                }
                else if (k == "chain") RunChain();
                else if (k == "lock") RunAnyAction("锁定");
                else if (k == "mute") MuteNow();
                else if (k == "notify")
                {
                    try { trayIcon.ShowBalloonTip(2600, "小工具箱", T("触发器：" + name, "Trigger: " + name), ToolTipIcon.Info); } catch (Exception dg15872_1) { Diag.Swallow("MainForm.cs:5083", dg15872_1); }
                }
                else if (k == "temp")
                {
                    try
                    {
                        string tmp = Path.GetTempPath();
                        DateTime keep = DateTime.Now.AddDays(-1);
                        foreach (string fp in Directory.GetFiles(tmp))
                        {
                            try { if (File.GetLastWriteTime(fp) < keep) File.Delete(fp); } catch (Exception dg15882_1) { Diag.Swallow("MainForm.cs:5093", dg15882_1); }
                        }
                    }
                    catch (Exception dg15885_1) { Diag.Swallow("MainForm.cs:5096", dg15885_1); }
                }
                else if (k == "shutdown") RunAnyAction("关机");
                else if (k == "restart") RunAnyAction("重启");
                Log("[触发器] " + name + " → " + a);
            }
            catch (Exception ex) { try { Log("[触发器] 执行失败：" + ex.Message); } catch (Exception dg15891_1) { Diag.Swallow("MainForm.cs:5102", dg15891_1); } }
        }

        string PowerListRaw()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg", "/list");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.Default;
                Process p2 = Process.Start(psi);
                string o = p2.StandardOutput.ReadToEnd();
                p2.WaitForExit(5000);
                return o == null ? "" : o;
            }
            catch { return ""; }
        }

        static string PowerGuid(string raw, string[] keys)
        {
            if (raw == null) return "";
            string[] lines = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l2 = lines[i].ToLowerInvariant();
                for (int k = 0; k < keys.Length; k++)
                {
                    if (l2.IndexOf(keys[k].ToLowerInvariant(), StringComparison.Ordinal) < 0) continue;
                    int g = l2.IndexOf("guid");
                    if (g < 0) continue;
                    string rest = lines[i].Substring(g + 4);
                    rest = rest.TrimStart(':', '：', ' ');
                    int sp = rest.IndexOf(' ');
                    string gd = sp > 0 ? rest.Substring(0, sp) : rest;
                    if (gd.Length >= 32) return gd.Trim();
                }
            }
            return "";
        }

        string PowerNow()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg", "/getactivescheme");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.Default;
                Process p2 = Process.Start(psi);
                string o = p2.StandardOutput.ReadToEnd();
                p2.WaitForExit(4000);
                if (o == null) return "";
                int a = o.IndexOf('(');
                int b = o.LastIndexOf(')');
                if (a >= 0 && b > a) return o.Substring(a + 1, b - a - 1).Trim();
                return o.Trim();
            }
            catch { return ""; }
        }

        bool SetPowerPlan(string kind)
        {
            string[] keys;
            if (kind == "high") keys = new string[] { "高性能", "high performance" };
            else if (kind == "saver") keys = new string[] { "节能", "power saver" };
            else if (kind == "ultimate") keys = new string[] { "卓越", "ultimate" };
            else keys = new string[] { "平衡", "balanced" };
            string raw = PowerListRaw();
            string gd = PowerGuid(raw, keys);
            if (gd.Length == 0 && kind == "ultimate")
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    Process p2 = Process.Start(psi);
                    p2.StandardOutput.ReadToEnd();
                    p2.WaitForExit(5000);
                }
                catch (Exception dg15976_1) { Diag.Swallow("MainForm.cs:5187", dg15976_1); }
                gd = PowerGuid(PowerListRaw(), keys);
            }
            if (gd.Length == 0) return false;
            try
            {
                ProcessStartInfo psi2 = new ProcessStartInfo("powercfg", "/setactive " + gd);
                psi2.CreateNoWindow = true;
                psi2.UseShellExecute = false;
                Process p3 = Process.Start(psi2);
                p3.WaitForExit(6000);
                return p3.ExitCode == 0;
            }
            catch { return false; }
        }

        void ShowPowerPlan()
        {
            Form f = new Form();
            f.Text = T("电源方案 · 小工具箱", "Power plan · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(560, 316);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;

            Label ttl = new Label();
            ttl.Text = T("电源方案", "Power plan");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = C_SUB;
            st.Location = new Point(20, 48);
            st.Size = new Size(520, 22);
            f.Controls.Add(st);

            Label tip = new Label();
            tip.Text = T("高性能：不降频，游戏/导出用；平衡：日常；节能：待机省电；卓越性能：隐藏档，首次点会自动创建。",
                        "High performance for gaming, Balanced for daily, Power saver for idle, Ultimate is hidden by default.");
            tip.ForeColor = UiKit.A(140, 152, 172);
            tip.Font = new Font("Microsoft YaHei UI", 8.5F);
            tip.Location = new Point(20, 76);
            tip.Size = new Size(520, 34);
            f.Controls.Add(tip);

            Button bHigh = MakeBtn(T("高性能", "High"), UiKit.A(38, 46, 60), Color.White, 20, 120, 120, 40);
            Button bBal = MakeBtn(T("平衡", "Balanced"), UiKit.A(38, 46, 60), Color.White, 150, 120, 120, 40);
            Button bSav = MakeBtn(T("节能", "Saver"), UiKit.A(38, 46, 60), Color.White, 280, 120, 120, 40);
            Button bUlt = MakeBtn(T("卓越性能", "Ultimate"), UiKit.A(38, 46, 60), Color.White, 410, 120, 120, 40);
            f.Controls.Add(bHigh);
            f.Controls.Add(bBal);
            f.Controls.Add(bSav);
            f.Controls.Add(bUlt);

            Action say = delegate { st.Text = T("当前：" + PowerNow(), "Now: " + PowerNow()); };
            say();

            bHigh.Click += delegate { bool ok = SetPowerPlan("high"); Sfx.Click(); say(); if (!ok) st.Text = T("切换失败：系统里没有这个方案", "Failed: plan missing"); };
            bBal.Click += delegate { bool ok = SetPowerPlan("balanced"); Sfx.Click(); say(); if (!ok) st.Text = T("切换失败", "Failed"); };
            bSav.Click += delegate { bool ok = SetPowerPlan("saver"); Sfx.Click(); say(); if (!ok) st.Text = T("切换失败", "Failed"); };
            bUlt.Click += delegate { bool ok = SetPowerPlan("ultimate"); Sfx.Click(); say(); if (!ok) st.Text = T("切换失败：卓越性能需要管理员或系统不支持", "Failed: Ultimate needs admin"); };

            Button bCpl = MakeBtn(T("打开电源设置", "Power settings"), C_BUBBLE_ALT(), Color.White, 20, 190, 140, 34);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 430, 190, 110, 34);
            f.Controls.Add(bCpl);
            f.Controls.Add(bClose);
            bCpl.Click += delegate { try { Process.Start("control.exe", "powercfg.cpl"); } catch (Exception dg16049_1) { Diag.Swallow("MainForm.cs:5260", dg16049_1); } };
            bClose.Click += delegate { f.Close(); };

            ShowOrTrack(f);
            f.Show();
        }

        void ShowGlobalSearch()
        {
            try
            {
                List<PaletteItem> items = BuildGlobalItems();
                PaletteForm f = new PaletteForm(items, langEn);
                try { f.SetHint(T("全局搜索：功能 / 页面 / 热键 / 便签 / 剪贴板 / 口令…", "Global search: features / pages / hotkeys / notes / clipboard…")); } catch (Exception dg16062_1) { Diag.Swallow("MainForm.cs:5273", dg16062_1); }
                ShowOrTrack(f);
                f.Show();
                Log("[全局搜索] 条目 " + items.Count);
            }
            catch (Exception ex)
            {
                Log("[全局搜索] " + ex.Message);
                MessageBox.Show(this, T("全局搜索打开失败：" + ex.Message, "Global search failed: " + ex.Message), "全局搜索", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ===================== v11.0 视频批量队列 =====================

        void ShowVideoQueue()
        {
            Form f = new Form();
            f.Text = T("任务队列 · 小工具箱", "Batch Queue · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(760, 520);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("任务队列", "Batch queue");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("一行一条链接，全部排队解析并下载；单条失败自动跳过，队列继续往下跑。队列会自动保存。",
                        "One link per line; each is parsed and downloaded in turn. A failed item is skipped and the queue continues. Queue is saved automatically.");
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 44);
            sub.Size = new Size(720, 20);
            f.Controls.Add(sub);

            // 输入区做成「底槽 + 无边框多行文本」，空着的时候也是一块能看出来的输入区
            Panel paneIn = new Panel();
            paneIn.Location = new Point(20, 70);
            paneIn.Size = new Size(720, 104);
            paneIn.BackColor = UiKit.A(30, 36, 46);
            f.Controls.Add(paneIn);

            TextBox tb = new TextBox();
            tb.Multiline = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.Location = new Point(9, 6);
            tb.Size = new Size(702, 92);
            tb.BackColor = UiKit.A(30, 36, 46);
            tb.ForeColor = UiKit.Txt(Color.White);
            tb.BorderStyle = BorderStyle.None;
            paneIn.Controls.Add(tb);
            string queueFile = Path.Combine(BASE_DIR, "视频队列.txt");
            try { if (File.Exists(queueFile)) tb.Text = File.ReadAllText(queueFile, Encoding.UTF8); } catch (Exception dgQ1) { Diag.Swallow("MainForm.videoQueueLoad", dgQ1); }

            ListView lv = MakeDarkList(new string[] { T("链接", "Link"), T("状态", "State"), T("进度", "Progress"), T("保存文件", "File") },
                new int[] { 330, 110, 80, 210 }, 20, 228, 720, 236);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = C_SUB;
            st.Location = new Point(20, 474);
            st.Size = new Size(720, 20);
            f.Controls.Add(st);

            bool running = false;
            bool stopFlag = false;
            List<string> failedUrls = new List<string>();

            Button bGo = MakeBtn(T("开始队列", "Start"), UiKit.ThemeColor, Color.White, 20, 186, 110, 34);
            Button bStop = MakeBtn(T("停止", "Stop"), C_BUBBLE_ALT(), Color.White, 140, 186, 90, 34);
            Button bOpen = MakeBtn(T("打开目录", "Open folder"), C_BUBBLE_ALT(), Color.White, 240, 186, 110, 34);
            Button bRetry = MakeBtn(T("重试失败", "Retry failed"), C_BUBBLE_ALT(), Color.White, 360, 186, 110, 34);
            bStop.Enabled = false;
            bRetry.Enabled = false;
            f.Controls.Add(bGo);
            f.Controls.Add(bStop);
            f.Controls.Add(bOpen);
            f.Controls.Add(bRetry);

            bOpen.Click += delegate
            {
                try
                {
                    if (!Directory.Exists(vOutDir)) Directory.CreateDirectory(vOutDir);
                    Process.Start("explorer.exe", "\"" + vOutDir + "\"");
                }
                catch (Exception dg16149_1) { Diag.Swallow("MainForm.cs:5360", dg16149_1); }
                Sfx.Click();
            };

            bRetry.Click += delegate
            {
                try
                {
                    if (failedUrls.Count == 0) return;
                    tb.Text = string.Join("\r\n", failedUrls.ToArray());
                    st.Text = T("已把 " + failedUrls.Count + " 条失败链接放回输入区，点「开始队列」重试。",
                                "Put " + failedUrls.Count + " failed links back; press Start to retry.");
                    bRetry.Enabled = false;
                    Sfx.Copy();
                }
                catch (Exception exR) { Diag.Swallow("MainForm.videoQueueRetry", exR); }
            };
            Action<Action> ui = delegate (Action a)
            {
                try { if (!f.IsDisposed) f.BeginInvoke(a); } catch (Exception dg16155_1) { Diag.Swallow("MainForm.cs:5366", dg16155_1); }
            };

            bStop.Click += delegate
            {
                stopFlag = true;
                bStop.Enabled = false;
                st.Text = T("正在停止（当前这一条下完就收工）…", "Stopping after the current item…");
                Sfx.Click();
            };

            bGo.Click += delegate
            {
                if (running) return;
                string[] raw = tb.Text.Replace("\r", "").Split('\n');
                List<string> q = new List<string>();
                foreach (string line in raw)
                {
                    string u = line.Trim();
                    if (u.Length < 8) continue;
                    if (u.IndexOf("http", StringComparison.OrdinalIgnoreCase) != 0 && u.IndexOf("www.") != 0) continue;
                    q.Add(u);
                }
                if (q.Count == 0)
                {
                    MessageBox.Show(f, T("请先粘贴至少一条 http 链接（一行一条）。", "Paste at least one http link, one per line."),
                        T("任务队列", "Queue"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!EnsureVideoTool()) return;
                running = true; stopFlag = false;
                failedUrls.Clear();
                bRetry.Enabled = false;
                try { File.WriteAllText(queueFile, string.Join("\r\n", q.ToArray()), new UTF8Encoding(false)); } catch (Exception dgQ2) { Diag.Swallow("MainForm.videoQueueSave", dgQ2); }
                bGo.Enabled = false; bStop.Enabled = true;
                lv.Items.Clear();
                for (int i = 0; i < q.Count; i++)
                {
                    ListViewItem li2 = new ListViewItem(new string[] { q[i], T("排队中", "queued"), "", "" });
                    lv.Items.Add(li2);
                }
                st.Text = T("队列共 " + q.Count + " 条，开始执行…", "Queue: " + q.Count + " items, running…");
                Sfx.Click();
                string tidQ = TaskHub.Start("视频下载", "视频批量队列", q.Count + " 条链接");

                Thread th = new Thread(delegate ()
                {
                    int done = 0, failed = 0;
                    for (int i = 0; i < q.Count; i++)
                    {
                        if (stopFlag) break;
                        string url = q[i];
                        int row = i;
                        ui(delegate
                        {
                            if (row < lv.Items.Count)
                            {
                                lv.Items[row].SubItems[1].Text = T("下载中", "downloading");
                                lv.Items[row].SubItems[2].Text = "0%";
                            }
                        });
                        string dir = vOutDir;
                        try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch (Exception dg16213_1) { Diag.Swallow("MainForm.cs:5424", dg16213_1); }
                        string args = "--yes-playlist --continue --windows-filenames --no-check-certificate --newline --retries 10 --fragment-retries 10 --concurrent-fragments 4"
                            + ProxyArgs()
                            + " -f \"bv*+ba/b\" -o \"" + Path.Combine(dir, "%(title)s.%(ext)s") + "\""
                            + " --progress-template \"download:DQ|%(progress._percent_str)s|%(progress._filename)s\""
                            + " \"" + url + "\"";
                        bool ok = false;
                        string lastName = "";
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(VideoToolExe(), args);
                            psi.CreateNoWindow = true;
                            psi.UseShellExecute = false;
                            psi.RedirectStandardOutput = true;
                            psi.RedirectStandardError = true;
                            psi.StandardOutputEncoding = Encoding.UTF8;
                            psi.StandardErrorEncoding = Encoding.UTF8;
                            Process p = Process.Start(psi);

                            p.OutputDataReceived += delegate (object s5, DataReceivedEventArgs e5)
                            {
                                if (e5.Data == null) return;
                                string ln = e5.Data;
                                if (ln.StartsWith("DQ|"))
                                {
                                    string[] a5 = ln.Split('|');
                                    if (a5.Length >= 3)
                                    {
                                        string pct = a5[1].Trim();
                                        string fn5 = a5[2].Trim();
                                        int r5 = row;
                                        ui(delegate
                                        {
                                            if (r5 < lv.Items.Count)
                                            {
                                                lv.Items[r5].SubItems[2].Text = pct;
                                                if (fn5.Length > 0) lv.Items[r5].SubItems[3].Text = Path.GetFileName(fn5);
                                            }
                                        });
                                    }
                                }
                                else
                                {
                                    int k5 = ln.IndexOf("Destination:");
                                    if (k5 >= 0) lastName = ln.Substring(k5 + 12).Trim();
                                }
                            };
                            p.ErrorDataReceived += delegate (object s6, DataReceivedEventArgs e6) { string dummy6 = e6.Data; };
                            p.BeginOutputReadLine();
                            p.BeginErrorReadLine();
                            p.WaitForExit();
                            ok = (p.ExitCode == 0);

                        }
                        catch (Exception ex2)
                        {
                            Log("[队列] " + url + " 启动失败：" + ex2.Message);
                            ok = false;
                        }
                        string stateTxt = ok ? T("完成", "done") : T("失败", "failed");
                        string fileTxt = lastName.Length > 0 ? Path.GetFileName(lastName) : "";
                        ui(delegate
                        {
                            if (row < lv.Items.Count)
                            {
                                lv.Items[row].SubItems[1].Text = stateTxt;
                                if (ok) lv.Items[row].SubItems[2].Text = "100%";
                                if (fileTxt.Length > 0) lv.Items[row].SubItems[3].Text = fileTxt;
                                if (!ok) lv.Items[row].ForeColor = UiKit.A(250, 130, 120);
                            }
                            st.Text = T("已完成 " + done + " / " + q.Count + T("，失败 ", ", failed ") + failed,
                                        "Done " + done + " / " + q.Count + ", failed " + failed);
                        });
                        if (ok) done++; else failed++;
                        if (!ok) { lock (failedUrls) failedUrls.Add(url); }
                        ui(delegate
                        {
                            st.Text = T("已完成 " + done + " / " + q.Count + T("，失败 ", ", failed ") + failed,
                                        "Done " + done + " / " + q.Count + ", failed " + failed);
                        });
                    }
                    ui(delegate
                    {
                        running = false;
                        bGo.Enabled = true;
                        bStop.Enabled = false;
                        bRetry.Enabled = failedUrls.Count > 0;
                        st.Text = stopFlag
                            ? T("已停止：完成 " + done + " 条，失败 " + failed + " 条", "Stopped: " + done + " done, " + failed + " failed")
                            : T("队列结束：完成 " + done + " 条，失败 " + failed + " 条", "Queue finished: " + done + " done, " + failed + " failed");
                        if (done + failed > 0) Sfx.Copy();
                        if (stopFlag) TaskHub.Cancel(tidQ, "完成 " + done + " 条，失败 " + failed + " 条");
                        else if (failed > 0) TaskHub.Fail(tidQ, "完成 " + done + " 条，失败 " + failed + " 条");
                        else TaskHub.Done(tidQ, "完成 " + done + " 条");
                    });
                });
                th.IsBackground = true;
                th.Start();
            };

            f.FormClosed += delegate
            {
                stopFlag = true;
                try { File.WriteAllText(queueFile, tb.Text, new UTF8Encoding(false)); } catch (Exception dgQ3) { Diag.Swallow("MainForm.videoQueueClose", dgQ3); }
            };
            ShowOrTrack(f);
            f.ShowDialog(this);
        }

        // ===================== v11.0 手机遥控 =====================

        string RemoteStatus()
        {
            bool act = false;
            int left = 0;
            string actName = "";
            try
            {
                if (taskActive)
                {
                    act = true;
                    left = (int)Math.Max(0, (endTime - DateTime.Now).TotalSeconds);
                    actName = curAction;
                }
            }
            catch (Exception dg16329_1) { Diag.Swallow("MainForm.cs:5540", dg16329_1); }
            return "{\"active\":" + (act ? "true" : "false") + ",\"left\":" + left
                + ",\"action\":\"" + actName + "\",\"total\":" + totalSec + "}";
        }

        string RemoteCmd(string c)
        {
            try
            {
                switch (c)
                {
                    case "cancel":
                        CancelTask();
                        return "ok";
                    case "add60":
                        if (taskActive) { endTime = endTime.AddSeconds(60); totalSec += 60; }
                        return "ok";
                    case "lock":
                        Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                        return "ok";
                    case "sleep":
                        Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true });
                        return "ok";
                    case "shutdown":
                        Process.Start(new ProcessStartInfo("shutdown", "/s /t 30 /f") { CreateNoWindow = true });
                        return "ok";
                    case "restart":
                        Process.Start(new ProcessStartInfo("shutdown", "/r /t 30 /f") { CreateNoWindow = true });
                        return "ok";
                }
            }
            catch (Exception ex) { return "err:" + ex.Message; }
            return "unknown";
        }

        void ShowPhoneRemote()
        {
            Form f = new Form();
            f.Text = T("手机遥控 · 小工具箱", "Phone Remote · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(560, 396);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("手机遥控", "Phone remote");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("手机连同一个 Wi-Fi，扫码或输网址即可远程看倒计时、加时、取消、关机 / 重启 / 锁屏。",
                        "Same Wi-Fi on your phone: scan the QR or type the URL to watch the countdown and control the PC.");
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(520, 36);
            f.Controls.Add(sub);

            PictureBox qr = new PictureBox();
            qr.Location = new Point(20, 92);
            qr.Size = new Size(176, 176);
            qr.BackColor = UiKit.A(30, 36, 46);
            qr.SizeMode = PictureBoxSizeMode.Zoom;
            f.Controls.Add(qr);

            // 未启动时这块不能是白板：给一句说明，启动出二维码后自动隐藏
            Label qrHint = new Label();
            qrHint.Text = T("启动后\n这里显示二维码", "QR code\nappears here");
            qrHint.ForeColor = UiKit.A(140, 152, 172);
            qrHint.BackColor = Color.Transparent;
            qrHint.Dock = DockStyle.Fill;
            qrHint.TextAlign = ContentAlignment.MiddleCenter;
            qrHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            qr.Controls.Add(qrHint);

            Label urlLbl = new Label();
            urlLbl.Text = T("未启动", "not running");
            urlLbl.ForeColor = C_TEXT;
            urlLbl.Font = new Font("Microsoft YaHei UI", 10F);
            urlLbl.Location = new Point(216, 96);
            urlLbl.Size = new Size(320, 22);
            f.Controls.Add(urlLbl);

            Label st = new Label();
            st.Text = T("点「启动服务」开始监听局域网端口。", "Press Start to listen on the local network.");
            st.ForeColor = C_SUB;
            st.Location = new Point(216, 126);
            st.Size = new Size(320, 56);
            f.Controls.Add(st);

            Button bStart = MakeBtn(T("启动服务", "Start"), UiKit.ThemeColor, Color.White, 216, 196, 120, 34);
            Button bStop = MakeBtn(T("停止", "Stop"), C_BUBBLE_ALT(), Color.White, 348, 196, 96, 34);
            Button bCopy = MakeBtn(T("复制网址", "Copy URL"), C_BUBBLE_ALT(), Color.White, 216, 240, 120, 34);
            Button bWeb = MakeBtn(T("本机打开", "Open here"), C_BUBBLE_ALT(), Color.White, 348, 240, 96, 34);
            bStop.Enabled = false;
            bCopy.Enabled = false;
            bWeb.Enabled = false;
            f.Controls.Add(bStart);
            f.Controls.Add(bStop);
            f.Controls.Add(bCopy);
            f.Controls.Add(bWeb);

            Label tip = new Label();
            tip.Text = T("提示：首次可能弹防火墙授权，允许「专用网络」即可；只在你按了启动时对外开端口。",
                        "Tip: Windows may ask for firewall permission — allow private networks. The port opens only while started.");
            tip.ForeColor = UiKit.A(240, 160, 90);
            tip.Font = new Font("Microsoft YaHei UI", 8.5F);
            tip.Location = new Point(20, 282);
            tip.Size = new Size(520, 44);
            f.Controls.Add(tip);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 20, 340, 100, 34);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            // v12.2：文件互传入口（网页里可选文件上传、点文件名下载）
            Button bXferDir = MakeBtn(T("打开互传目录", "Share folder"), C_BUBBLE_ALT(), Color.White, 140, 340, 150, 34);
            bXferDir.Click += delegate
            {
                try
                {
                    string xd = Path.Combine(BASE_DIR, "手机工具");
                    if (!Directory.Exists(xd)) Directory.CreateDirectory(xd);
                    Process.Start("explorer.exe", "\"" + xd + "\"");
                }
                catch (Exception dg16458_1) { Diag.Swallow("MainForm.cs:5669", dg16458_1); }
            };
            f.Controls.Add(bXferDir);

            string curUrl = "";

            Action refreshQr = delegate
            {
                try
                {
                    if (curUrl.Length == 0 || remoteSrv == null) return;
                    string qerr = "";
                    Bitmap qb = QrCore.Render(curUrl, 6, UiKit.A(20, 24, 30), Color.White, out qerr);
                    if (qb != null) { qr.Image = qb; qrHint.Visible = false; }
                }
                catch (Exception exq) { st.Text = T("二维码生成失败：", "QR failed: ") + exq.Message; }
            };

            bStart.Click += delegate
            {
                try
                {
                    if (remoteSrv == null) remoteSrv = new PhoneRemoteServer();
                    remoteSrv.StatusJson = RemoteStatus;
                    remoteSrv.Cmd = RemoteCmd;
                    try
                    {
                        string xf = Path.Combine(BASE_DIR, "手机工具");
                        if (!Directory.Exists(Path.Combine(xf, "发送"))) Directory.CreateDirectory(Path.Combine(xf, "发送"));
                        if (!Directory.Exists(Path.Combine(xf, "接收"))) Directory.CreateDirectory(Path.Combine(xf, "接收"));
                        remoteSrv.ShareDir = xf;
                    }
                    catch (Exception dg16490_1) { Diag.Swallow("MainForm.cs:5701", dg16490_1); }
                    string err = remoteSrv.Start();
                    if (err != null) { st.Text = T("启动失败：" + err, "Failed: " + err); return; }
                    string[] urls = remoteSrv.Urls();
                    curUrl = urls.Length > 0 ? urls[0] : "";
                    urlLbl.Text = curUrl;
                    st.Text = T("运行中，端口 " + remoteSrv.Port + "。手机打开上面网址即可。", "Running on port " + remoteSrv.Port + ".");
                    refreshQr();
                    bStart.Enabled = false; bStop.Enabled = true; bCopy.Enabled = true; bWeb.Enabled = true;
                    Log("[遥控] 已启动 " + curUrl);
                    Sfx.Click();
                }
                catch (Exception ex) { st.Text = T("启动异常：" + ex.Message, "Error: " + ex.Message); }
            };

            bStop.Click += delegate
            {
                try { if (remoteSrv != null) remoteSrv.Stop(); } catch (Exception dg16507_1) { Diag.Swallow("MainForm.cs:5718", dg16507_1); }
                st.Text = T("已停止，端口关闭。", "Stopped, port closed.");
                bStart.Enabled = true; bStop.Enabled = false; bCopy.Enabled = false; bWeb.Enabled = false;
                Sfx.Click();
            };

            bCopy.Click += delegate
            {
                try { if (curUrl.Length > 0) { Clipboard.SetText(curUrl); st.Text = T("网址已复制，发到手机即可。", "URL copied."); Sfx.Copy(); } } catch (Exception dg16515_1) { Diag.Swallow("MainForm.cs:5726", dg16515_1); }
            };

            bWeb.Click += delegate
            {
                try { if (curUrl.Length > 0) Process.Start(curUrl); } catch (Exception dg16520_1) { Diag.Swallow("MainForm.cs:5731", dg16520_1); }
            };

            f.FormClosed += delegate { try { if (remoteSrv != null) remoteSrv.Stop(); } catch (Exception dg16523_1) { Diag.Swallow("MainForm.cs:5734", dg16523_1); } };
            ShowOrTrack(f);
            f.ShowDialog(this);
        }

        // ===================== v11.0 配置同步 =====================

        void ShowSyncCfg()
        {
            string[] files = new string[] { "config.txt", "hotkeys.json", "主题色.txt", "动作链.txt", "每日一句.txt", "便签.dat" };
            Form f = new Form();
            f.Text = T("配置同步 · 小工具箱", "Config Sync · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(700, 432);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("配置同步", "Config sync");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("把 配置 / 热键 / 主题色 / 动作链 / 每日一句 / 便签 打包成一段同步码；换机器粘过去就能复刻。",
                        "Bundle config, hotkeys, theme, action chain, quotes and notes into one sync code — paste it on another machine.");
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(660, 36);
            f.Controls.Add(sub);

            Panel paneTb = new Panel();
            paneTb.Location = new Point(20, 84);
            paneTb.Size = new Size(660, 212);
            paneTb.BackColor = UiKit.A(30, 36, 46);
            f.Controls.Add(paneTb);

            TextBox tb = new TextBox();
            tb.Multiline = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.Location = new Point(9, 6);
            tb.Size = new Size(642, 200);
            tb.BackColor = UiKit.A(30, 36, 46);
            tb.ForeColor = UiKit.A(220, 230, 240);
            tb.BorderStyle = BorderStyle.None;
            paneTb.Controls.Add(tb);

            // 说明文字放在框外（Label 盖在 TextBox 上会被文本框刷掉，实测无效）
            Label phSync = new Label();
            phSync.Text = T("点「生成同步码」后会出现一段 MTB1: 开头的代码；也可以把别台机器的代码粘进框里，再点「从同步码导入」。",
                            "Press Make code for an MTB1:… string; or paste a code from another machine and press Import code.");
            phSync.ForeColor = UiKit.A(140, 152, 172);
            phSync.Font = new Font("Microsoft YaHei UI", 8.5F);
            phSync.Location = new Point(20, 302);
            phSync.Size = new Size(660, 20);
            f.Controls.Add(phSync);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = C_SUB;
            st.Location = new Point(20, 334);
            st.Size = new Size(660, 20);
            f.Controls.Add(st);

            Button bExp = MakeBtn(T("生成同步码", "Make code"), UiKit.ThemeColor, Color.White, 20, 366, 130, 34);
            Button bSave = MakeBtn(T("存成文件", "Save file"), C_BUBBLE_ALT(), Color.White, 160, 366, 110, 34);
            Button bImp = MakeBtn(T("从同步码导入", "Import code"), C_BUBBLE_ALT(), Color.White, 280, 366, 140, 34);
            Button bImpF = MakeBtn(T("从文件导入", "Import file"), C_BUBBLE_ALT(), Color.White, 430, 366, 120, 34);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 560, 366, 100, 34);
            f.Controls.Add(bExp);
            f.Controls.Add(bSave);
            f.Controls.Add(bImp);
            f.Controls.Add(bImpF);
            f.Controls.Add(bClose);
            bClose.Click += delegate { f.Close(); };

            Action<byte[]> applyZip = delegate (byte[] raw)
            {
                int n = 0;
                using (MemoryStream ms2 = new MemoryStream(raw))
                using (ZipArchive za = new ZipArchive(ms2, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry ze in za.Entries)
                    {
                        string nm = Path.GetFileName(ze.FullName);
                        if (nm.Length == 0) continue;
                        using (Stream es = ze.Open())
                        using (FileStream fs = File.Create(Path.Combine(BASE_DIR, nm)))
                            es.CopyTo(fs);
                        n++;
                    }
                }
                st.Text = T("已导入 " + n + " 个配置项，重启后生效。", "Imported " + n + " files, restart to apply.");
                Sfx.Copy();
                if (MessageBox.Show(f, T("配置已写入，重启应用生效，现在重启吗？", "Written. Restart now?"),
                    T("配置同步", "Config sync"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    RestartApp();
            };

            bExp.Click += delegate
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        {
                            foreach (string fn in files)
                            {
                                string src2 = Path.Combine(BASE_DIR, fn);
                                if (!File.Exists(src2)) continue;
                                ZipArchiveEntry ze = za.CreateEntry(fn, CompressionLevel.Optimal);
                                using (Stream es = ze.Open())
                                using (FileStream fs = File.OpenRead(src2))
                                    fs.CopyTo(es);
                            }
                        }
                        tb.Text = "MTB1:" + Convert.ToBase64String(ms.ToArray());
                        st.Text = T("同步码已生成，可复制发给自己或另存文件。", "Code generated.");
                        Sfx.Click();
                    }
                }
                catch (Exception ex) { st.Text = T("生成失败：" + ex.Message, "Failed: " + ex.Message); }
            };

            bSave.Click += delegate
            {
                try
                {
                    if (tb.Text.Trim().Length < 10) { st.Text = T("先点「生成同步码」。", "Generate the code first."); return; }
                    string outp = Path.Combine(BASE_DIR, "配置同步_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".kbx");
                    File.WriteAllText(outp, tb.Text.Trim(), Encoding.UTF8);
                    st.Text = T("已保存：" + outp, "Saved: " + outp);
                    Sfx.Copy();
                }
                catch (Exception ex) { st.Text = T("保存失败：" + ex.Message, "Failed: " + ex.Message); }
            };

            bImp.Click += delegate
            {
                try
                {
                    string code = tb.Text.Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
                    if (!code.StartsWith("MTB1:")) { st.Text = T("这段不是同步码（应以 MTB1: 开头）。", "Not a sync code."); return; }
                    byte[] raw = Convert.FromBase64String(code.Substring(5));
                    applyZip(raw);
                }
                catch (Exception ex) { st.Text = T("导入失败：" + ex.Message, "Import failed: " + ex.Message); }
            };

            bImpF.Click += delegate
            {
                try
                {
                    OpenFileDialog ofd = new OpenFileDialog();
                    ofd.Filter = T("同步码文件 (*.kbx;*.txt)|*.kbx;*.txt|所有文件 (*.*)|*.*", "Sync files (*.kbx;*.txt)|*.kbx;*.txt|All files (*.*)|*.*");
                    if (ofd.ShowDialog(f) != DialogResult.OK) return;
                    string code = File.ReadAllText(ofd.FileName, Encoding.UTF8).Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
                    if (!code.StartsWith("MTB1:")) { st.Text = T("文件内容不是同步码。", "File is not a sync code."); return; }
                    tb.Text = code;
                    applyZip(Convert.FromBase64String(code.Substring(5)));
                }
                catch (Exception ex) { st.Text = T("导入失败：" + ex.Message, "Import failed: " + ex.Message); }
            };

            ShowOrTrack(f);
            f.ShowDialog(this);
        }

        void ShowOrTrack(Form f)
        {
            // 所有弹窗都从这里出去，深色标题栏统一在这里挂
            try { f.HandleCreated += delegate { UiKit.DarkCaption(f); }; } catch (Exception dg16699_1) { Diag.Swallow("MainForm.cs:5910", dg16699_1); }
            // v18.2：Esc 一律关闭当前弹窗，键盘不离手也能退出来
            try
            {
                f.KeyPreview = true;
                f.KeyDown += delegate (object s9, KeyEventArgs e9)
                {
                    if (e9.KeyCode != Keys.Escape) return;
                    e9.SuppressKeyPress = true;
                    try { if (f.AcceptButton != null) f.AcceptButton = null; } catch (Exception dg16708_1) { Diag.Swallow("MainForm.cs:5919", dg16708_1); }
                    try { if (f.DialogResult == DialogResult.None) f.DialogResult = DialogResult.Cancel; } catch (Exception dg16709_1) { Diag.Swallow("MainForm.cs:5920", dg16709_1); }
                    try { f.Close(); } catch (Exception dg16710_1) { Diag.Swallow("MainForm.cs:5921", dg16710_1); }
                };
            }
            catch (Exception dg16713_1) { Diag.Swallow("MainForm.cs:5924", dg16713_1); }
            try { if (f.IsHandleCreated) UiKit.DarkCaption(f); } catch (Exception dg16714_1) { Diag.Swallow("MainForm.cs:5925", dg16714_1); }
            if (v68AutoDump) { v68Forms.Add(f); f.Show(); }
            else f.ShowDialog(this);
        }

        // v10.0 自测：把新增窗口挨个开一遍（模态窗用定时器在它的消息循环里关掉），
        // 拍照 + 记录异常，最后退出。只在 /v10self 参数下跑，正常启动完全不走这里。
        void SnapForm(Form fm, string path, StringBuilder r)
        {
            try
            {
                Rectangle br = fm.Bounds;
                if (br.Width < 20 || br.Height < 20) { r.AppendLine("skip tiny " + br.Width + "x" + br.Height); return; }
                using (Bitmap bmp = new Bitmap(br.Width, br.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { PrintWindow(fm.Handle, hdc, 2); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                r.AppendLine("shot " + Path.GetFileName(path) + " " + br.Width + "x" + br.Height);
            }
            catch (Exception ex) { r.AppendLine("shot ex " + ex.Message); }
        }

        void V10SelfTest()
        {
            StringBuilder r = new StringBuilder();
            string[] names = { "mini", "today", "voice", "chain", "clip", "hotkeys", "queue", "remote", "sync", "search", "audio", "port", "filesearch", "triggers", "power" };
            int step = 0, guard = 0;
            List<Form> handled = new List<Form>();
            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
            tm.Interval = 650;
            tm.Tick += delegate
            {
                guard++;
                if (guard > 130)
                {
                    tm.Stop();
                    r.AppendLine("TIMEOUT at step " + step);
                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_v10self.txt"), r.ToString(), Encoding.UTF8); } catch (Exception dg16757_1) { Diag.Swallow("MainForm.cs:5968", dg16757_1); }
                    Application.Exit();
                    return;
                }
                Form open = null;
                try { foreach (Form fm in Application.OpenForms) { if (fm != this && fm.Visible && !handled.Contains(fm)) { open = fm; break; } } } catch (Exception dg16762_1) { Diag.Swallow("MainForm.cs:5973", dg16762_1); }
                if (open != null)
                {
                    handled.Add(open);
                    r.AppendLine("open " + step + " text=" + open.Text + " size=" + open.Width + "x" + open.Height);
                    SnapForm(open, Path.Combine(BASE_DIR, "_v10_" + (step < names.Length ? names[step] : "s" + step) + ".png"), r);
                    try { open.Close(); } catch (Exception ex2) { r.AppendLine("CLOSE EX " + ex2.Message); }
                    step++;
                    return;
                }
                try
                {
                    switch (step)
                    {
                        case 0: r.AppendLine("call mini"); ToggleMini(); break;
                        case 1: r.AppendLine("call today"); ShowToday(); break;
                        case 2: r.AppendLine("call voice"); ShowVoiceCmd(); break;
                        case 3: r.AppendLine("call chain"); ShowChainDlg(); break;
                        case 4: r.AppendLine("call clip"); ShowClipHist(); break;
                        case 5: r.AppendLine("call hotkeys"); ShowHotkeys(); break;
                        case 6: r.AppendLine("call queue"); ShowVideoQueue(); break;
                        case 7: r.AppendLine("call remote"); ShowPhoneRemote(); break;
                        case 8: r.AppendLine("call sync"); ShowSyncCfg(); break;
                        case 9: r.AppendLine("call search"); ShowGlobalSearch(); break;
                        case 10: r.AppendLine("call audio"); ShowAudioTool(); break;
                        case 11: r.AppendLine("call port"); ShowPortTool(); break;
                        case 12: r.AppendLine("call filesearch"); ShowFileSearch(); break;
                        case 13: r.AppendLine("call triggers"); ShowTriggers(); break;
                        case 14: r.AppendLine("call power"); ShowPowerPlan(); break;
                        default:
                            tm.Stop();
                            r.AppendLine("done steps=" + step);
                            try { File.WriteAllText(Path.Combine(BASE_DIR, "_v10self.txt"), r.ToString(), Encoding.UTF8); } catch (Exception dg16794_1) { Diag.Swallow("MainForm.cs:6005", dg16794_1); }
                            Application.Exit();
                            break;
                    }
                }
                catch (Exception ex) { r.AppendLine("STEP EX " + step + " " + ex.Message + " | " + ex.StackTrace); step++; }
            };
            tm.Start();
        }

        void HotkeyAction(string key)
        {
            // v10.0：先记触发时间（热键中心要显示），再排队执行
            try { hkLastFire[key] = DateTime.Now; } catch (Exception dg16807_1) { Diag.Swallow("MainForm.cs:6018", dg16807_1); }
            // 热键回调在 WndProc 里执行，用 BeginInvoke 挪到消息循环之后，避免在消息处理中弹模态窗
            try { this.BeginInvoke((Action)delegate { DoHotkeyAction(key); }); }
            catch { DoHotkeyAction(key); }
        }

        void DoHotkeyAction(string key)
        {
            try
            {
                switch (key)
                {
                    case "shot": ScreenshotNow(); break;
                    case "main": ShowMain(); break;
                    case "xfer": ShowTransfer(); break;
                    case "plus": ShowPlusCenter(); break;
                    case "checkup": ShowCheckup(); break;
                    case "boost": ShowGameBoost(); break;
                    case "desk": ShowDeskSort(); break;
                    case "dupe": ShowDupClean(); break;
                    case "cliphist": ShowClipHist(); break;
                    case "record": ToggleRecord(); break;
                    case "palette": ShowPalette(); break;
                    case "gsearch": ShowGlobalSearch(); break;
                    case "shutdown60":
                        // v8.1：与指令舱一致，热键关机也直接执行，不再问许可
                        try { Process.Start(new ProcessStartInfo("shutdown", "/s /t 60 /f") { CreateNoWindow = true }); Log("[热键] 60 秒后关机"); }
                        catch (Exception ex) { Log("[热键] 关机失败：" + ex.Message); }
                        break;
                }
            }
            catch (Exception ex) { Log("[热键] " + key + " 执行失败：" + ex.Message); }
        }

        void ScreenshotNow()
        {
            Rectangle r = SystemInformation.VirtualScreen;
            try
            {
                using (Bitmap bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
                    ShotEditForm sef = new ShotEditForm(bmp, langEn);
                    sef.ShowDialog(this);
                    if (sef.Saved && sef.Result != null)
                    {
                        string dir = Path.Combine(BASE_DIR, "截图");
                        try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch (Exception dg16854_1) { Diag.Swallow("MainForm.cs:6065", dg16854_1); }
                        string file = Path.Combine(dir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                        try { sef.Result.Save(file, System.Drawing.Imaging.ImageFormat.Png); }
                        catch (Exception ex) { Log("[截图] 保存失败：" + ex.Message); }
                        try { if (trayIcon != null && trayIcon.Visible) trayIcon.ShowBalloonTip(2000, T("已截图", "Screenshot saved"), file, ToolTipIcon.Info); } catch (Exception dg16858_1) { Diag.Swallow("MainForm.cs:6069", dg16858_1); }
                        Log("[截图] 已保存 " + file);
                    }
                    else Log("[截图] 标注器已关闭（未保存到文件）");
                }
            }
            catch (Exception ex) { Log("[截图] 失败：" + ex.Message); }
        }

        // v12.0：延时截图（市面级截图工具的标配）——先把主窗藏起来，等 N 秒再抓，避免截到自己
        void ScreenshotDelayed(int sec)
        {
            bool wasVisible = this.Visible;
            try { if (wasVisible) this.Hide(); } catch (Exception dg16871_1) { Diag.Swallow("MainForm.cs:6082", dg16871_1); }
            System.Windows.Forms.Timer tmr = new System.Windows.Forms.Timer();
            tmr.Interval = Math.Max(1, sec) * 1000;
            tmr.Tick += delegate
            {
                tmr.Stop();
                tmr.Dispose();
                try { ScreenshotNow(); }
                catch (Exception ex) { Log("[截图] 延时失败：" + ex.Message); }
                finally
                {
                    try { if (wasVisible) { this.Show(); this.WindowState = FormWindowState.Normal; this.Activate(); } } catch (Exception dg16882_1) { Diag.Swallow("MainForm.cs:6093", dg16882_1); }
                }
            };
            tmr.Start();
            Log("[截图] 已排队 " + sec + " 秒后截全屏");
        }


        Panel PlusCard(int x, int y, int w, int h, string title, string desc, Color accent, EventHandler onClick)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(w, h);
            p.BackColor = Color.Transparent;
            p.Cursor = Cursors.Hand;
            p.Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rc = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
                using (GraphicsPath path = UiKit.RoundedPath(rc, 12))
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(rc, UiKit.A(40, 46, 56), UiKit.A(28, 33, 41), 90F))
                        e.Graphics.FillPath(lb, path);
                    using (Pen pen = new Pen(UiKit.A(60, 70, 85)))
                        e.Graphics.DrawPath(pen, path);
                }
                using (SolidBrush bar = new SolidBrush(accent))
                    e.Graphics.FillRectangle(bar, 0, 14, 5, p.Height - 28);
                using (Font ft = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold))
                using (SolidBrush fb = new SolidBrush(UiKit.A(235, 242, 250)))
                    e.Graphics.DrawString(title, ft, fb, new PointF(18, 14));
                using (Font fd = new Font("Microsoft YaHei UI", 8.5F))
                using (SolidBrush db = new SolidBrush(UiKit.A(140, 152, 168)))
                    e.Graphics.DrawString(desc, fd, db, new RectangleF(18, 44, p.Width - 32, p.Height - 50));
            };
            p.Click += onClick;
            return p;
        }


        // ================= v12.4 ① 本地 AI 助手 =================

        // 意图 → 可执行动作
        Action AiMakeRun(AiIntent.Hit h)
        {
            if (h == null) return null;
            int sec = h.Sec;
            switch (h.Id)
            {
                case "shutdown":
                    return delegate { PaletteShutdownIn(sec > 0 ? sec : 60); };
                case "restart":
                    return delegate { AiShutdownCmd("/r", sec > 0 ? sec : 3, "重启"); };
                case "hibernate":
                    return delegate
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("shutdown", "/h") { CreateNoWindow = true });
                            Log("[AI] 休眠");
                            Ui2.Toast(this, T("正在进入休眠", "Hibernating"));
                        }
                        catch (Exception ex) { Log("[AI] 休眠失败：" + ex.Message); Sfx.Error(); }
                    };
                case "sleep":
                    return delegate
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true });
                            Log("[AI] 睡眠");
                            Ui2.Toast(this, T("正在进入睡眠", "Sleeping"));
                        }
                        catch (Exception ex) { Log("[AI] 睡眠失败：" + ex.Message); Sfx.Error(); }
                    };
                case "lock":
                    return delegate
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                            Log("[AI] 锁屏");
                        }
                        catch (Exception dg16965_1) { Diag.Swallow("MainForm.cs:6176", dg16965_1); }
                    };
                case "cancel":
                    return delegate
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true });
                            Log("[AI] 取消关机");
                            Ui2.Toast(this, T("已取消关机", "Shutdown cancelled"));
                        }
                        catch (Exception dg16976_1) { Diag.Swallow("MainForm.cs:6187", dg16976_1); }
                    };
                case "shot":
                    return delegate { PaletteShotIn(sec > 0 ? sec : 3); };
                case "clean":
                    return delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "clean")); };
                case "mute":
                    return delegate { AiIntent.Volume("mute"); };
                case "volup":
                    return delegate { AiIntent.Volume("up"); };
                case "voldn":
                    return delegate { AiIntent.Volume("down"); };
            }
            return null;
        }

        void AiShutdownCmd(string arg, int sec, string label)
        {
            try
            {
                if (sec < 3) sec = 3;
                Process.Start(new ProcessStartInfo("shutdown", arg + " /t " + sec) { CreateNoWindow = true });
                Log("[AI] " + label + " " + sec + " 秒");
                Ui2.Toast(this, T(sec >= 60 ? "已安排 " + (sec / 60) + " 分钟后" + label : "已安排 " + sec + " 秒后" + label,
                                  label + " scheduled"));
            }
            catch (Exception ex) { Log("[AI] " + label + "失败：" + ex.Message); Sfx.Error(); }
        }

        // 「打开视频下载」这类词，直接拿现成的指令舱条目按下
        PaletteItem AiFindOpen(string kw)
        {
            if (kw == null) return null;
            string k = kw.Trim().ToLowerInvariant();
            if (k.Length == 0) return null;
            PaletteItem best = null;
            int bestScore = 0;
            List<PaletteItem> all = BuildPaletteItems();
            for (int i = 0; i < all.Count; i++)
            {
                PaletteItem it = all[i];
                string hay = (it.Hay ?? "").ToLowerInvariant();
                string nm = (it.Zh ?? "").ToLowerInvariant();
                int sc = 0;
                if (nm.IndexOf(k, StringComparison.Ordinal) >= 0) sc = 90 - Math.Min(40, nm.IndexOf(k, StringComparison.Ordinal));
                else if (hay.IndexOf(k, StringComparison.Ordinal) >= 0) sc = 60 - Math.Min(30, hay.IndexOf(k, StringComparison.Ordinal));
                else continue;
                if (nm.StartsWith(k, StringComparison.Ordinal)) sc += 20;
                if (sc > bestScore) { bestScore = sc; best = it; }
            }
            return best;
        }

        // v12.6：改一行字的小窗（语音纠错、宏口令改名都用它）
        string AskLine(string title, string init)
        {
            using (Form d = new Form())
            {
                d.Text = title;
                d.BackColor = UiKit.A(20, 24, 30);
                d.StartPosition = FormStartPosition.CenterParent;
                d.ClientSize = new Size(560, 150);
                d.FormBorderStyle = FormBorderStyle.FixedDialog;
                d.MaximizeBox = false;
                d.MinimizeBox = false;
                d.ShowIcon = false;
                d.Font = new Font("Microsoft YaHei UI", 9F);

                Label lb = new Label();
                lb.Text = T("确认这句话（回车即保存）：", "Confirm the sentence (Enter to save):");
                lb.ForeColor = UiKit.A(150, 162, 182);
                lb.Font = new Font("Microsoft YaHei UI", 8.5F);
                lb.Location = new Point(20, 18);
                lb.AutoSize = true;
                d.Controls.Add(lb);

                TextBox t = MakeInput(20, 44, 520, 32, init);
                t.Text = init == null ? "" : init;
                d.Controls.Add(t);

                Button ok = MakeBtn(T("保存", "Save"), UiKit.ThemeColor, Color.White, 300, 96, 110, 32);
                Button no = MakeBtn(T("取消", "Cancel"), C_BUBBLE_ALT(), Color.White, 420, 96, 110, 32);
                d.Controls.Add(ok);
                d.Controls.Add(no);

                string res = null;
                ok.Click += delegate { res = t.Text == null ? "" : t.Text.Trim(); d.Close(); };
                no.Click += delegate { d.Close(); };
                t.KeyDown += delegate (object o3, KeyEventArgs e3)
                {
                    if (e3.KeyCode == Keys.Enter) { e3.SuppressKeyPress = true; res = t.Text == null ? "" : t.Text.Trim(); d.Close(); }
                };
                d.AcceptButton = ok;
                d.ShowDialog(this);
                return res;
            }
        }

        // ================= v12.4 ② 插件仓库 =================

        void ShowPluginStore()
        {
            Form f = new Form();
            f.Text = T("插件仓库 · 小工具箱", "Plugin Store · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(920, 620);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;

            Label ttl = new Label();
            ttl.Text = T("插件仓库", "Plugin store");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 12);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("内置 4 个示例插件（离线可装）；也可加远程仓库地址或从链接安装 zip / 脚本。装好后自动出现在「插件命令」里。",
                        "4 built-in plugins install offline; remote sources and zip/script links supported. Installed plugins show up under Plugins.");
            sub.ForeColor = C_SUB;
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(880, 34);
            f.Controls.Add(sub);

            TextBox tbSearch = MakeInput(20, 86, 420, 30, T("按名字 / 说明过滤", "Filter by name or description"));
            f.Controls.Add(tbSearch);
            Button bRefresh = MakeBtn(T("刷新", "Refresh"), C_BUBBLE_ALT(), Color.White, 452, 86, 90, 30);
            f.Controls.Add(bRefresh);

            ListView lv = MakeDarkList(new string[] { T("插件", "Plugin"), T("版本", "Ver"), T("作者", "Author"), T("说明", "Description"), T("状态", "State"), T("来源", "Source") },
                new int[] { 190, 60, 90, 350, 90, 90 }, 20, 130, 880, 330);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = C_SUB;
            st.Font = new Font("Microsoft YaHei UI", 8.5F);
            st.Location = new Point(20, 470);
            st.Size = new Size(880, 20);
            f.Controls.Add(st);

            List<PluginStore.Entry> entries = new List<PluginStore.Entry>();

            Action reload = delegate
            {
                string kw = tbSearch.Text.Trim().ToLowerInvariant();
                lv.Items.Clear();
                entries.Clear();
                List<PluginStore.Entry> all = PluginStore.Load();
                int shown = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    PluginStore.Entry e = all[i];
                    if (kw.Length > 0)
                    {
                        string hay = (e.Name + " " + e.Desc + " " + e.Author).ToLowerInvariant();
                        if (hay.IndexOf(kw, StringComparison.Ordinal) < 0) continue;
                    }
                    string state = PluginStore.StateText(e);
                    if (PluginStore.CanUpdate(e)) state = T(state, state);
                    lv.Items.Add(new ListViewItem(new string[] { e.Name, e.Ver, e.Author, e.Desc, state, e.Source }));
                    entries.Add(e);
                    shown++;
                }
                int srcN = PluginStore.Sources().Length;
                string dirN = PluginCfg.Dir;
                int files = 0;
                try { files = PluginCfg.Files().Length; } catch (Exception dg17148_1) { Diag.Swallow("MainForm.cs:6359", dg17148_1); }
                st.Text = T("仓库 " + shown + " 项 · 远程源 " + srcN + " 个 / 本地缓存 " + PluginStore.CacheCount()
                    + " 个 · 本次联网 " + PluginStore.LastNetHits + "、离线回退 " + PluginStore.LastCacheHits
                    + " · 插件目录现有 " + files + " 个文件",
                            shown + " items (sources " + srcN + ", cached " + PluginStore.CacheCount() + ") · " + files + " files");
                st.ForeColor = C_SUB;
            };

            Button bInstall = MakeBtn(T("安装 / 覆盖", "Install"), UiKit.ThemeColor, Color.White, 20, 500, 130, 34);
            Button bUn = MakeBtn(T("卸载", "Uninstall"), C_BUBBLE_ALT(), Color.White, 160, 500, 100, 34);
            Button bLink = MakeBtn(T("从链接安装", "Install from URL"), C_BUBBLE_ALT(), Color.White, 270, 500, 140, 34);
            Button bSrc = MakeBtn(T("加远程源", "Add source"), C_BUBBLE_ALT(), Color.White, 420, 500, 120, 34);
            Button bDir = MakeBtn(T("打开插件目录", "Open folder"), C_BUBBLE_ALT(), Color.White, 550, 500, 140, 34);
            Button bUpd = MakeBtn(T("一键更新", "Update installed"), UiKit.A(88, 208, 138), Color.White, 20, 542, 128, 32);
            Button bCc = MakeBtn(T("清缓存", "Clear cache"), C_BUBBLE_ALT(), Color.White, 700, 500, 82, 34);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 790, 500, 110, 34);
            f.Controls.Add(bInstall);
            f.Controls.Add(bUn);
            f.Controls.Add(bLink);
            f.Controls.Add(bSrc);
            f.Controls.Add(bDir);
            f.Controls.Add(bUpd);
            f.Controls.Add(bCc);
            f.Controls.Add(bClose);

            Label hint = new Label();
            hint.Text = T("提示：插件支持 .ps1 / .py / .cmd，文件名即功能名，首行注释会变成面板标题。",
                        "Hint: .ps1 / .py / .cmd files work; the filename is the command, a header comment becomes the label.");
            hint.ForeColor = UiKit.A(120, 132, 148);
            hint.Font = new Font("Microsoft YaHei UI", 8.5F);
            hint.Location = new Point(160, 550);
            hint.Size = new Size(880, 18);
            f.Controls.Add(hint);

            bRefresh.Click += delegate { Sfx.Click(); reload(); };
            tbSearch.TextChanged += delegate { reload(); };
            bInstall.Click += delegate
            {
                int idx = lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] : -1;
                if (idx < 0 || idx >= entries.Count) { st.Text = T("先选中一行。", "Pick a row first."); st.ForeColor = UiKit.A(240, 180, 70); return; }
                string msg = PluginStore.Install(entries[idx]);
                Sfx.Ok();
                reload();
                st.Text = msg;
                st.ForeColor = UiKit.A(88, 208, 138);
            };
            bUn.Click += delegate
            {
                int idx = lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] : -1;
                if (idx < 0 || idx >= entries.Count) { st.Text = T("先选中一行。", "Pick a row first."); return; }
                string msg = PluginStore.Uninstall(entries[idx]);
                reload();
                st.Text = msg;
            };
            bLink.Click += delegate
            {
                string url = Microsoft.VisualBasic.Interaction.InputBox(
                    T("填入插件地址（.zip 或单个脚本的直链）", "Plugin URL (.zip or a single script)"),
                    T("从链接安装", "Install from URL"), "https://");
                if (url == null || url.Trim().Length == 0 || url.Trim() == "https://") return;
                PluginStore.Entry e = new PluginStore.Entry();
                e.Name = Path.GetFileName(url.Split('?')[0]);
                if (e.Name.Length == 0) e.Name = "远程插件";
                e.File = e.Name;
                e.Kind = e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "url";
                e.Payload = url.Trim();
                e.Source = "手动";
                string msg = PluginStore.Install(e);
                Sfx.Ok();
                reload();
                st.Text = msg;
            };
            bSrc.Click += delegate
            {
                string url = Microsoft.VisualBasic.Interaction.InputBox(
                    T("填入仓库 JSON 地址（内容为 {\"plugins\":[...]}）", "Repo JSON URL ({\"plugins\":[...]})"),
                    T("加远程源", "Add source"), "https://");
                if (url == null || !url.Trim().StartsWith("http")) return;
                PluginStore.AddSource(url.Trim());
                reload();
                st.Text = T("已加入远程源，下次刷新会拉取。", "Source added.");
            };
            bUpd.Click += delegate
            {
                int tried = 0;
                int ok2 = PluginStore.UpdateAll(out tried);
                Sfx.Click();
                reload();
                if (tried == 0) st.Text = T("没有可更新的插件（都是最新版）。", "Nothing to update.");
                else st.Text = T("一键更新完成：" + ok2 + " / " + tried + " 个插件已升到最新版。", "Updated " + ok2 + "/" + tried);
                st.ForeColor = tried == 0 ? C_SUB : UiKit.A(88, 208, 138);
            };
            bCc.Click += delegate
            {
                string m2 = PluginStore.ClearCache();
                Sfx.Click();
                reload();
                st.Text = m2;
            };
            bDir.Click += delegate { try { Process.Start("explorer.exe", PluginCfg.Dir); } catch (Exception dg17247_1) { Diag.Swallow("MainForm.cs:6458", dg17247_1); } };
            bClose.Click += delegate { f.Close(); };

            reload();
            ShowOrTrack(f);
            f.Show();
        }

        // ---------- 自测：意图解析 ----------
        void AiSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string[] q = { "半小时后关电脑", "10分钟后关机", "晚上11点关机", "两小时二十分钟后关机", "45秒后关机",
                           "一个半小时后关机", "重启电脑", "取消关机", "锁屏", "休眠", "截图", "清理C盘垃圾", "静音", "打开视频下载", "定时关机" };
            string[] want = { "shutdown", "shutdown", "shutdown", "shutdown", "shutdown",
                              "shutdown", "restart", "cancel", "lock", "hibernate", "shot", "clean", "mute", "open", "" };
            int[] secLow = { 1790, 590, 0, 8380, 44, 5390, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int[] secHigh = { 1810, 610, 86400, 8420, 46, 5410, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int fail = 0, hit0 = 0;
            for (int i = 0; i < q.Length; i++)
            {
                List<AiIntent.Hit> hs = AiIntent.Parse(q[i]);
                string gotId = hs.Count > 0 ? hs[0].Id : "(none)";
                int gotSec = hs.Count > 0 ? hs[0].Sec : -1;
                bool ok = true;
                if (want[i].Length == 0) ok = (hs.Count == 0);
                else
                {
                    bool found = false;
                    for (int k = 0; k < hs.Count; k++) if (hs[k].Id == want[i]) { found = true; gotSec = hs[k].Sec; break; }
                    ok = found;
                    if (ok && secLow[i] > 0 && (gotSec < secLow[i] || gotSec > secHigh[i])) ok = false;
                }
                if (!ok) fail++;
                sb.AppendLine((ok ? "OK   " : "FAIL ") + q[i] + "  -> id=" + gotId + " sec=" + gotSec
                    + " hits=" + hs.Count + " want=" + (want[i].Length == 0 ? "(none)" : want[i]));
                if (hs.Count == 0) hit0++;
            }
            sb.AppendLine("ai_model_ready=" + AiLocal.Ready);
            sb.AppendLine("ai_model_status=" + AiLocal.Status());
            sb.AppendLine("fail=" + fail + " total=" + q.Length);
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_aitest.txt"), sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg17288_1) { Diag.Swallow("MainForm.cs:6499", dg17288_1); }
            Application.Exit();
        }

        // ---------- 自测：插件仓库 ----------
        // v12.4 自测：三个新/改窗口各抓一张（模态窗口用定时器在对话框内抓图后关闭）
        void GrabAnyForm(string file, string titleKey, StringBuilder log)
        {
            Form target = null;
            try
            {
                foreach (Form of in Application.OpenForms)
                {
                    if (of != null && of.Text != null && of.Text.IndexOf(titleKey, StringComparison.Ordinal) >= 0) { target = of; break; }
                }
            }
            catch (Exception dg17304_1) { Diag.Swallow("MainForm.cs:6515", dg17304_1); }
            if (target == null) { log.AppendLine(file + " MISS form " + titleKey); return; }
            try
            {
                target.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(240);
                Application.DoEvents();
                Rectangle rc = target.Bounds;
                using (Bitmap bmp = new Bitmap(rc.Width, rc.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        bool ok = false;
                        IntPtr hdc = g.GetHdc();
                        try { ok = PrintWindow(target.Handle, hdc, 2); }
                        finally { g.ReleaseHdc(hdc); }
                        if (!ok)
                        {
                            IntPtr h2 = g.GetHdc();
                            try { ok = PrintWindow(target.Handle, h2, 0); }
                            finally { g.ReleaseHdc(h2); }
                        }
                        if (!ok) g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    }
                    bmp.Save(Path.Combine(BASE_DIR, file), System.Drawing.Imaging.ImageFormat.Png);
                }
                log.AppendLine(file + "=" + new FileInfo(Path.Combine(BASE_DIR, file)).Length + "B " + rc.Width + "x" + rc.Height);
            }
            catch (Exception ex) { log.AppendLine(file + " EX " + ex.Message); }
            try { if (!keepFormAfterGrab) target.Close(); } catch (Exception dg17334_1) { Diag.Swallow("MainForm.cs:6545", dg17334_1); }
        }

        void PluginSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                PluginStore.Init(BASE_DIR);
                sb.AppendLine("dir=" + PluginStore.Dir);
                sb.AppendLine("index_exists=" + File.Exists(PluginStore.IndexFile) + " sources=" + PluginStore.Sources().Length);
                List<PluginStore.Entry> all = PluginStore.Load();
                sb.AppendLine("entries=" + all.Count);
                int builtin = 0, remote = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Kind == "builtin") builtin++; else remote++;
                }
                sb.AppendLine("builtin=" + builtin + " remote_or_url=" + remote);
                int done = 0, fail = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    PluginStore.Entry e = all[i];
                    if (e.Kind != "builtin") continue;
                    string msg = PluginStore.Install(e);
                    string target = PluginStore.Target(e);
                    bool exists = File.Exists(target);
                    string title = exists ? PluginCfg.Title(target) : "";
                    long len = exists ? new FileInfo(target).Length : 0;
                    sb.AppendLine("install " + e.Name + " -> " + msg + "  bytes=" + len + " title=" + title);
                    if (exists && len > 40) done++; else fail++;
                    string un = PluginStore.Uninstall(e);
                    sb.AppendLine("uninstall " + e.Name + " -> " + un + "  exists_after=" + File.Exists(target));
                    if (File.Exists(target)) fail++;
                }
                sb.AppendLine("builtin_ok=" + done + " fail=" + fail);
                // 解析器健壮性：一段手写 JSON（含 plugins 数组）
                string js1 = "{\"plugins\":[{\"name\":\"X\",\"file\":\"X.ps1\",\"ver\":\"0.9\",\"author\":\"a\",\"desc\":\"d\",\"kind\":\"url\",\"payload\":\"https://x/y.ps1\"}]}";
                string js2 = "[{\"name\":\"Y\",\"file\":\"Y.ps1\",\"kind\":\"builtin\",\"payload\":\"disk\"},{\"name\":\"Z\",\"file\":\"Z.zip\",\"kind\":\"zip\",\"payload\":\"https://x/z.zip\"}]";
                List<PluginStore.Entry> p1 = PluginStore.Parse(js1, "test1");
                List<PluginStore.Entry> p2 = PluginStore.Parse(js2, "test2");
                sb.AppendLine("parse_test count=" + p1.Count + (p1.Count > 0 ? " first=" + p1[0].Name + "/" + p1[0].Kind : ""));
                sb.AppendLine("parse_array count=" + p2.Count + (p2.Count > 0 ? " names=" + p2[0].Name + "," + (p2.Count > 1 ? p2[1].Name : "") : ""));
                if (p1.Count != 1) fail++;
                if (p2.Count != 2) fail++;
                sb.AppendLine("cache_dir=" + PluginStore.CacheDir);
                sb.AppendLine("cached_before=" + PluginStore.CacheCount());
                sb.AppendLine("cache_clear=" + PluginStore.ClearCache());
                sb.AppendLine("fail_total=" + fail);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_plugstore_test.txt"), sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg17385_1) { Diag.Swallow("MainForm.cs:6596", dg17385_1); }
            Application.Exit();
        }

        // ---------- 自测：短语记忆（用临时目录，不碰用户的 AI\短语.txt） ----------
        void PhraseSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            int fail = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "mtb_phrase_" + DateTime.Now.Ticks);
            try
            {
                AiPhrase.Init(tmp);
                sb.AppendLine("file=" + AiPhrase.PhraseFile);
                sb.AppendLine("remember1=" + AiPhrase.Remember("半小时后关电脑"));
                sb.AppendLine("remember2=" + AiPhrase.Remember("晚上11点关机"));
                sb.AppendLine("remember3=" + AiPhrase.Add("清一下C盘垃圾", "clean", -1));
                sb.AppendLine("remember_bad=" + (AiPhrase.Remember("随便说点什么").Length == 0));
                List<AiPhrase.P> all = AiPhrase.Load();
                sb.AppendLine("total=" + all.Count);
                if (all.Count < 3) fail++;
                bool hasShut = false;
                for (int i = 0; i < all.Count; i++) if (all[i].Id == "shutdown") hasShut = true;
                if (!hasShut) fail++;
                List<AiPhrase.P> m = AiPhrase.Match("关");
                sb.AppendLine("match_guan=" + m.Count + (m.Count > 0 ? " first=" + m[0].Text + "/" + m[0].Id : ""));
                if (m.Count < 1) fail++;
                AiPhrase.Bump("半小时后关电脑");
                List<AiPhrase.P> m2 = AiPhrase.Match("关");
                sb.AppendLine("after_bump_hits=" + (m2.Count > 0 ? m2[0].Hits : -1));
                if (m2.Count == 0 || m2[0].Hits < 2) fail++;
                List<AiPhrase.P> m3 = AiPhrase.Match("");
                sb.AppendLine("match_all=" + m3.Count);
                bool rm = AiPhrase.Remove("清一下C盘垃圾");
                sb.AppendLine("remove=" + rm + " left=" + AiPhrase.Load().Count);
                if (!rm) fail++;
                List<AiIntent.Hit> hs = AiIntent.Parse("半小时后关电脑");
                sb.AppendLine("replay=" + (hs.Count > 0 ? hs[0].Id + "/" + hs[0].Sec : "(none)"));
                if (hs.Count == 0 || hs[0].Id != "shutdown") fail++;
                sb.AppendLine("file_exists=" + System.IO.File.Exists(AiPhrase.PhraseFile));
                string[] lines = System.IO.File.ReadAllLines(AiPhrase.PhraseFile, Encoding.UTF8);
                sb.AppendLine("file_lines=" + lines.Length);
                if (lines.Length < 3) fail++;
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.Message); fail++; }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception dg17432_1) { Diag.Swallow("MainForm.cs:6643", dg17432_1); }
                try { AiPhrase.Init(BASE_DIR); } catch (Exception dg17433_1) { Diag.Swallow("MainForm.cs:6644", dg17433_1); }
            }
            sb.AppendLine("fail=" + fail);
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_phrasetest.txt"), sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg17436_1) { Diag.Swallow("MainForm.cs:6647", dg17436_1); }
            Application.Exit();
        }

        // ---------- 自测：离线中文语音引擎 ----------
        void VoiceSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            int fail = 0;
            try
            {
                AiVoice.Probe();
                sb.AppendLine("available=" + AiVoice.Available);
                sb.AppendLine("culture=" + AiVoice.Culture);
                sb.AppendLine("engine=" + AiVoice.Engine);
                sb.AppendLine("status=" + AiVoice.Status());
                if (!AiVoice.Available) fail++;
                if (!AiVoice.Culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) fail++;
                try
                {
                    System.Speech.Recognition.SpeechRecognitionEngine eng =
                        new System.Speech.Recognition.SpeechRecognitionEngine(new System.Globalization.CultureInfo(AiVoice.Culture));
                    eng.LoadGrammar(new System.Speech.Recognition.DictationGrammar());
                    eng.SetInputToDefaultAudioDevice();
                    sb.AppendLine("grammar=ok mic=ok");
                    eng.Dispose();
                }
                catch (Exception ex) { sb.AppendLine("engine_open_fail=" + ex.Message); fail++; }
                int n = System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers().Count;
                sb.AppendLine("recognizers=" + n);
                if (n < 1) fail++;
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.Message); fail++; }
            sb.AppendLine("fail=" + fail);
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_voicetest.txt"), sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg17470_1) { Diag.Swallow("MainForm.cs:6681", dg17470_1); }
            Application.Exit();
        }


        // ---------- 自测：v12.6 宏口令 + 记忆打包 + 插件版本 ----------
        void MacroPhraseSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            int fail = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "mtb_macro_" + DateTime.Now.Ticks);
            try { Directory.CreateDirectory(tmp); } catch (Exception dg17481_1) { Diag.Swallow("MainForm.cs:6692", dg17481_1); }
            AiPhrase.Init(tmp);

            string m1 = AiPhrase.BuildMacro("半小时后关电脑然后锁屏再静音");
            sb.AppendLine("macro_build=" + (m1.Length > 0 ? m1 : "(空)"));
            if (m1.Length == 0) fail++;
            sb.AppendLine("is_macro=" + AiPhrase.IsMacro(m1));
            if (!AiPhrase.IsMacro(m1)) fail++;
            sb.AppendLine("macro_steps=" + AiPhrase.MacroSteps(m1));
            if (AiPhrase.MacroSteps(m1) != 3) fail++;
            sb.AppendLine("macro_parts=" + AiPhrase.MacroParts(m1).Length);
            if (AiPhrase.MacroParts(m1).Length != 6) fail++;
            sb.AppendLine("macro_label=" + AiPhrase.MacroLabel(m1));

            string m2 = AiPhrase.BuildMacro("半小时后关电脑");
            sb.AppendLine("single_not_macro=" + (m2.Length == 0));
            if (m2.Length != 0) fail++;

            string addMsg = AiPhrase.Add("下班", m1, -1);
            sb.AppendLine("add_macro=" + addMsg);
            string addMsg2 = AiPhrase.Add("清一下", "clean", -1);
            sb.AppendLine("add_single=" + addMsg2);
            sb.AppendLine("phrase_total=" + AiPhrase.Count());
            if (AiPhrase.Count() != 2) fail++;

            // 记忆打包：导出 → 清空 → 导入
            string file = AiMemory.Export(tmp);
            sb.AppendLine("export=" + file);
            if (!System.IO.File.Exists(file)) fail++;
            long fsize = 0;
            try { fsize = new FileInfo(file).Length; } catch (Exception dg17511_1) { Diag.Swallow("MainForm.cs:6722", dg17511_1); }
            sb.AppendLine("export_bytes=" + fsize);
            if (fsize <= 0) fail++;
            try { AiPhrase.Save(new List<AiPhrase.P>()); } catch (Exception dg17514_1) { Diag.Swallow("MainForm.cs:6725", dg17514_1); }
            sb.AppendLine("after_clear=" + AiPhrase.Count());
            string imp = AiMemory.Import(file);
            sb.AppendLine("import=" + imp);
            sb.AppendLine("after_import=" + AiPhrase.Count());
            if (AiPhrase.Count() != 2) fail++;
            bool macroKept = false;
            List<AiPhrase.P> chk = AiPhrase.Load();
            for (int i = 0; i < chk.Count; i++) if (chk[i].Text == "下班" && AiPhrase.IsMacro(chk[i].Id)) macroKept = true;
            sb.AppendLine("macro_kept=" + macroKept);
            if (!macroKept) fail++;

            // 插件版本比较与状态
            PluginStore.Entry fe = new PluginStore.Entry();
            fe.Name = "测试插件"; fe.File = "_mactest_plugin.ps1"; fe.Ver = "1.1";
            string tf = Path.Combine(PluginStore.Dir, fe.File);
            try { File.WriteAllText(tf, "# test", new UTF8Encoding(false)); } catch (Exception dg17530_1) { Diag.Swallow("MainForm.cs:6741", dg17530_1); }
            try { File.WriteAllText(PluginStore.VerFile(fe), "1.0", new UTF8Encoding(false)); } catch (Exception dg17531_1) { Diag.Swallow("MainForm.cs:6742", dg17531_1); }
            sb.AppendLine("can_update=" + PluginStore.CanUpdate(fe));
            if (!PluginStore.CanUpdate(fe)) fail++;
            sb.AppendLine("state_text=" + PluginStore.StateText(fe));
            if (!PluginStore.StateText(fe).Contains("可更新")) fail++;
            sb.AppendLine("cmp_10_11=" + PluginStore.CmpVer("1.0", "1.1"));
            if (PluginStore.CmpVer("1.0", "1.1") != -1) fail++;
            sb.AppendLine("cmp_equal=" + PluginStore.CmpVer("2.0", "2.0"));
            if (PluginStore.CmpVer("2.0", "2.0") != 0) fail++;
            try { File.Delete(tf); } catch (Exception dg17540_1) { Diag.Swallow("MainForm.cs:6751", dg17540_1); }
            try { File.Delete(PluginStore.VerFile(fe)); } catch (Exception dg17541_1) { Diag.Swallow("MainForm.cs:6752", dg17541_1); }

            AiPhrase.Init(BASE_DIR);
            try { Directory.Delete(tmp, true); } catch (Exception dg17544_1) { Diag.Swallow("MainForm.cs:6755", dg17544_1); }
            sb.AppendLine("ver=" + UiKit.VER);
            sb.AppendLine("fail=" + fail);
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_mactest.txt"), sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg17547_1) { Diag.Swallow("MainForm.cs:6758", dg17547_1); }
            Application.Exit();
        }

        void ShowPlusCenter()
        {
            Form f = new Form();
            f.Text = T("增强工具中心 · 小工具箱", "Power Tools · Mini Toolbox");
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(1184, 660);
            f.MinimumSize = new Size(1184, 660);
            f.ShowIcon = false;
            f.MaximizeBox = false;
            f.KeyPreview = true;
            f.Font = new Font(Ui2.FamilyName, 9F);

            Color cHead = UiKit.A(21, 27, 37);
            Color cFoot = UiKit.A(11, 14, 20);

            Font fTtl = new Font(Ui2.FamilyName, 17F, FontStyle.Bold);
            Font fSub = new Font(Ui2.FamilyName, 8.5F);
            Font fCardT = new Font(Ui2.FamilyName, 11.5F, FontStyle.Bold);
            Font fCardD = new Font(Ui2.FamilyName, 8.5F);
            Font fNum = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            Font fTip = new Font(Ui2.FamilyName, 8.5F);

            // ---- 底板：整窗渐变 + 实心标题带 + 顶部光带
            Panel bg = new Panel();
            bg.Dock = DockStyle.Fill;
            bg.BackColor = UiKit.A(13, 17, 23);
            bg.Paint += delegate (object s0, PaintEventArgs e0)
            {
                Graphics g = e0.Graphics;
                Rectangle rc = bg.ClientRectangle;
                using (LinearGradientBrush lb = new LinearGradientBrush(rc, UiKit.A(19, 24, 33), UiKit.A(9, 12, 17), 90F))
                    g.FillRectangle(lb, rc);
                using (SolidBrush hb = new SolidBrush(cHead))
                    g.FillRectangle(hb, 0, 0, rc.Width, 92);
                using (SolidBrush fb = new SolidBrush(cFoot))
                    g.FillRectangle(fb, 0, 600, rc.Width, rc.Height - 600);
                using (LinearGradientBrush tb = new LinearGradientBrush(new Rectangle(0, 0, rc.Width, 3),
                    UiKit.A(96, 165, 250), UiKit.A(45, 212, 191), 0F))
                    g.FillRectangle(tb, 0, 0, rc.Width, 3);
                using (Pen hp = new Pen(UiKit.AA(44, 96, 165, 250), 1f))
                    g.DrawLine(hp, 0, 91, rc.Width, 91);
                using (Pen fp = new Pen(UiKit.AA(26, 255, 255, 255), 1f))
                    g.DrawLine(fp, 0, 600, rc.Width, 600);
            };
            f.Controls.Add(bg);

            Label ttl = new Label();
            ttl.Text = T("增强工具中心", "Power Tools");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = fTtl;
            ttl.BackColor = cHead;
            ttl.Location = new Point(26, 16);
            ttl.AutoSize = true;
            bg.Controls.Add(ttl);

            Label cnt = new Label();
            cnt.Text = T("16 项", "16 tools");
            cnt.ForeColor = UiKit.A(147, 197, 253);
            cnt.Font = fNum;
            cnt.BackColor = cHead;
            cnt.Location = new Point(26, 56);
            cnt.Size = new Size(46, 20);
            bg.Controls.Add(cnt);

            Label sub = new Label();
            sub.Text = T("热键 · 剪贴板 · 查重 · 录屏 · 加密 · 更新 · 批量转换 · 插件仓库",
                "Hotkeys, clipboard, duplicates, recorder, encrypt, update, batch, plugins");
            sub.ForeColor = UiKit.A(150, 164, 184);
            sub.Font = fSub;
            sub.BackColor = cHead;
            sub.Location = new Point(78, 58);
            sub.Size = new Size(700, 20);
            bg.Controls.Add(sub);

            // ---- 搜索框
            Panel sbox = new Panel();
            sbox.Location = new Point(796, 27);
            sbox.Size = new Size(364, 38);
            sbox.BackColor = UiKit.A(25, 31, 40);
            sbox.Paint += delegate (object s0, PaintEventArgs e0)
            {
                Graphics g = e0.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rc = new Rectangle(0, 0, sbox.Width - 1, sbox.Height - 1);
                using (GraphicsPath p = UiKit.RoundedPath(rc, 10))
                {
                    using (SolidBrush sb2 = new SolidBrush(UiKit.A(25, 31, 40))) g.FillPath(sb2, p);
                    using (Pen pen = new Pen(UiKit.A(52, 63, 80))) g.DrawPath(pen, p);
                }
                using (Pen sp = new Pen(UiKit.A(118, 134, 156), 1.6f))
                {
                    g.DrawEllipse(sp, 13, 11, 13, 13);
                    g.DrawLine(sp, 25, 24, 30, 29);
                }
            };
            bg.Controls.Add(sbox);

            TextBox search = new TextBox();
            search.BorderStyle = BorderStyle.None;
            search.BackColor = UiKit.A(25, 31, 40);
            search.ForeColor = UiKit.Txt(Color.White);
            search.Font = new Font(Ui2.FamilyName, 9.5F);
            search.Location = new Point(38, 9);
            search.Size = new Size(312, 22);
            sbox.Controls.Add(search);

            string phText = T("搜索：热键 / 剪贴板 / 录屏 / 插件 …", "Search: hotkey, clipboard, recorder …");
            Label ph = new Label();
            ph.Text = phText;
            ph.ForeColor = UiKit.A(112, 126, 146);
            ph.BackColor = UiKit.A(25, 31, 40);
            ph.Font = new Font(Ui2.FamilyName, 9F);
            ph.AutoSize = true;
            ph.Location = new Point(38, 11);
            ph.Cursor = Cursors.IBeam;
            sbox.Controls.Add(ph);

            // ---- 卡片数据
            string[] TT = new string[]
            {
                T("全局热键中心", "Global hotkeys"),
                T("局域网剪贴板", "LAN clipboard"),
                T("重复文件清理", "Duplicate finder"),
                T("剪贴板历史", "Clipboard history"),
                T("录屏 / GIF 工坊", "Screen recorder"),
                T("单文件加密", "File encrypt"),
                T("检查更新", "Update check"),
                T("快捷指令面板", "Command palette"),
                T("批量重命名 / 格式转换", "Batch rename & convert"),
                T("端口映射 / 开服助手", "UPnP port mapping"),
                T("延迟优选", "Latency picker"),
                T("Wi-Fi 信号分析", "Wi-Fi analyzer"),
                T("插件仓库", "Plugin store"),
            };
            string[] DD = new string[]
            {
                T("给截图、互传、录屏、加密等动作绑组合键，游戏中也能触发", "Bind shortcuts to actions, even in games"),
                T("手机浏览器读写电脑剪贴板，不用发微信", "Phone reads/writes the PC clipboard"),
                T("大小 + 指纹 + MD5 三重比对，删进回收站", "Size + fingerprint + MD5, to Recycle Bin"),
                T("自动记录复制过的文字，Ctrl+Alt+V 随时翻", "Auto-recorded, flip with Ctrl+Alt+V"),
                T("抓屏录 MP4，Ctrl+Alt+R 启停，一键压 GIF", "Record MP4, Ctrl+Alt+R, one-click GIF"),
                T("文件拖进来加密成 .mtbx，密码解回", "Encrypt any file to .mtbx with a password"),
                T("连 GitHub 清单对比版本，可一键下载替换", "Compare with GitHub manifest and update"),
                T("Ctrl+Alt+Space 唤出，输几个字跳到任意功能", "Ctrl+Alt+Space, type a word, jump anywhere"),
                T("整个文件夹改名 + 图片 / 音频 / 视频 / GIF 一键转", "Rename a folder, convert media in one go"),
                T("用路由器 UPnP 把本机端口映射到外网，开私服不用登后台", "Map a local port via router UPnP"),
                T("Steam / Epic / WeGame 各节点 TCP 测速，挑最快下载区", "TCP timing across Steam, Epic, WeGame"),
                T("周围 AP 的信号与信道占用，算出最空信道固定过去", "Nearby AP signal and channel load"),
                T("4 个示例插件 + 远程源 + 本地缓存，断网也能浏览安装", "Built-ins, sources, offline cache"),
            };
            Color[] AC = new Color[]
            {
                UiKit.A(96, 165, 250), UiKit.A(14, 165, 233), UiKit.A(232, 82, 61), UiKit.A(250, 190, 90),
                UiKit.A(56, 189, 248), UiKit.A(167, 139, 250), UiKit.A(72, 208, 130), UiKit.A(96, 165, 250),
                UiKit.A(251, 146, 60), UiKit.A(232, 82, 61), UiKit.A(72, 208, 130), UiKit.A(96, 165, 250),
                UiKit.A(34, 197, 94), UiKit.A(249, 115, 22), UiKit.A(14, 165, 233), UiKit.A(45, 212, 191),
            };
            EventHandler[] ACT = new EventHandler[]
            {
                delegate { ShowHotkeys(); },
                delegate { ShowTransfer(); },
                delegate { ShowDupClean(); },
                delegate { ShowClipHist(); },
                delegate { ShowRecorder(); },
                delegate { ShowFileCrypto(); },
                delegate { ShowUpdateCenter(); },
                delegate { ShowPalette(); },
                delegate { ShowBatchTool(); },
                delegate { ShowUpnpTool(); },
                delegate { ShowLatencyTool(); },
                delegate { ShowWifiTool(); },
                delegate { ShowPluginStore(); },
            };

            int cw = 272, chh = 108, gap = 15, x0 = 24, y0 = 110;
            Panel[] cards = new Panel[TT.Length];
            string[] keys = new string[TT.Length];
            for (int i = 0; i < TT.Length; i++) keys[i] = (TT[i] + " " + DD[i]).ToLower();

            for (int i = 0; i < TT.Length; i++)
            {
                int ni = i + 1;
                string t1 = TT[i], t2 = DD[i];
                Color ac = AC[i];
                Panel p = new Panel();
                p.BackColor = UiKit.A(30, 36, 47);
                p.Cursor = Cursors.Hand;
                bool[] hot = new bool[1];
                p.Paint += delegate (object s0, PaintEventArgs e0)
                {
                    Graphics g = e0.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle rc = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
                    using (GraphicsPath path = UiKit.RoundedPath(rc, 14))
                    {
                        using (LinearGradientBrush lb = new LinearGradientBrush(rc,
                            hot[0] ? UiKit.A(46, 55, 70) : UiKit.A(33, 40, 52),
                            hot[0] ? UiKit.A(31, 37, 48) : UiKit.A(24, 29, 38), 90F))
                            g.FillPath(lb, path);
                        using (Pen pen = new Pen(hot[0] ? Color.FromArgb(200, ac) : UiKit.A(48, 58, 74), hot[0] ? 2f : 1f))
                            g.DrawPath(pen, path);
                    }
                    using (LinearGradientBrush ab = new LinearGradientBrush(new Rectangle(0, 0, 5, p.Height),
                        Color.FromArgb(235, ac), Color.FromArgb(60, ac), 90F))
                        g.FillRectangle(ab, 1, 18, 4, p.Height - 36);
                    TextRenderer.DrawText(g, "0" + ni, fNum, new Rectangle(p.Width - 48, 10, 34, 18),
                        Color.FromArgb(hot[0] ? 240 : 148, ac), TextFormatFlags.NoPadding | TextFormatFlags.Right);
                    TextRenderer.DrawText(g, t1, fCardT, new Rectangle(22, 12, p.Width - 76, 24), UiKit.A(238, 244, 252),
                        TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, t2, fCardD, new Rectangle(22, 42, p.Width - 44, p.Height - 66),
                        hot[0] ? UiKit.A(182, 196, 214) : UiKit.A(137, 149, 167),
                        TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
                    TextRenderer.DrawText(g, "打开 ›", fTip, new Rectangle(0, p.Height - 28, p.Width - 22, 20),
                        Color.FromArgb(hot[0] ? 240 : 120, ac), TextFormatFlags.NoPadding | TextFormatFlags.Right);
                };
                p.MouseEnter += delegate { hot[0] = true; p.Invalidate(); };
                p.MouseLeave += delegate { hot[0] = false; p.Invalidate(); };
                p.Click += ACT[i];
                bg.Controls.Add(p);
                cards[i] = p;
            }

            Action relayout = delegate
            {
                string q = (search.Text ?? "").Trim().ToLower();
                if (q.Length > 0 && q == phText.ToLower()) q = "";
                bool all = (q.Length == 0);
                int shown = 0;
                for (int i = 0; i < cards.Length; i++)
                {
                    bool ok = all || keys[i].Contains(q);
                    cards[i].Visible = ok;
                    if (!ok) continue;
                    int col = shown % 4, row = shown / 4;
                    cards[i].Location = new Point(x0 + col * (cw + gap), y0 + row * (chh + gap));
                    cards[i].Size = new Size(cw, chh);
                    if (cards[i].Region == null)
                    {
                        using (GraphicsPath gp = UiKit.RoundedPath(new Rectangle(0, 0, cw - 1, chh - 1), 14))
                            cards[i].Region = new Region(gp);
                    }
                    shown++;
                }
                cnt.Text = shown == 0 ? T("无匹配", "No match") : T(shown + " 项", shown + " tools");
            };

            search.TextChanged += delegate { try { relayout(); } catch (Exception dg17797_1) { Diag.Swallow("MainForm.cs:7008", dg17797_1); } };
            ph.Click += delegate { search.Focus(); };
            search.GotFocus += delegate { if (search.Text == phText) search.Text = ""; ph.Visible = false; };
            search.LostFocus += delegate { if (search.Text.Trim().Length == 0) { search.Text = ""; ph.Visible = true; } };
            search.KeyDown += delegate (object s0, KeyEventArgs e0)
            {
                if (e0.KeyCode == Keys.Escape) { f.Close(); return; }
                if (e0.KeyCode == Keys.Enter)
                {
                    for (int i = 0; i < cards.Length; i++)
                    {
                        if (cards[i].Visible) { ACT[i](null, EventArgs.Empty); break; }
                    }
                }
            };
            search.Text = "";

            Label hint = new Label();
            hint.Text = T("提示：截图（Ctrl+Alt+S）后直接打开标注器；热键默认 S 截图 / H 主窗口 / T 互传 / G 本窗口 / V 剪贴板历史 / R 录屏 / 空格 快捷指令，均可在 01 里改。",
                "Tip: Ctrl+Alt+S opens the editor; S/H/T/G/V/R/Space are editable in 01.");
            hint.ForeColor = UiKit.A(118, 130, 146);
            hint.Font = fTip;
            hint.BackColor = cFoot;
            hint.Location = new Point(24, 614);
            hint.Size = new Size(960, 24);
            bg.Controls.Add(hint);

            Button close = MakeBtn(T("关闭", "Close"), UiKit.A(38, 45, 58), Color.White, 1054, 607, 104, 34);
            close.Click += delegate { f.Close(); };
            bg.Controls.Add(close);

            f.KeyDown += delegate (object s0, KeyEventArgs e0)
            {
                if (e0.KeyCode == Keys.Escape) f.Close();
                if (e0.Control && e0.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); }
            };

            relayout();
            ShowOrTrack(f);
        }

        void ShowHotkeys()
        {
            Form f = new Form();
            f.Text = T("全局热键 · 小工具箱", "Global Hotkeys · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(760, 520);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("全局热键中心", "Global Hotkeys");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("双击一行即可按下新的组合键；必须带 Ctrl / Alt / Shift / Win 其中一个。设置立即生效并存到 hotkeys.json。",
                "Double-click a row and press a new shortcut (needs Ctrl/Alt/Shift/Win). Saved to hotkeys.json and active immediately.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 48);
            sub.Size = new Size(720, 24);
            f.Controls.Add(sub);

            ListView lv = MakeDarkList(new string[] { T("动作", "Action"), T("快捷键", "Shortcut"), T("状态", "State"), T("上次触发", "Last fired") },
                new int[] { 224, 128, 150, 152 } /* = 654 */, 20, 82, 674, 330);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 468);
            st.Size = new Size(720, 22);
            f.Controls.Add(st);

            Action refresh = delegate
            {
                lv.Items.Clear();
                if (hkMgr == null) return;
                foreach (HotkeyManager.Item it in hkMgr.Items)
                {
                    string disp = HotkeyManager.Display(it.Mods, it.Vk);
                    string state = it.Mods == 0 ? T("未设置", "not set") : T("已启用", "active");
                    // 两个动作撞了同一个组合键 = 一定有一个抢不到，直接在这里点出来
                    if (it.Mods != 0)
                    {
                        foreach (HotkeyManager.Item o in hkMgr.Items)
                        {
                            if (o == it || o.Mods == 0) continue;
                            if (o.Mods == it.Mods && o.Vk == it.Vk)
                            {
                                state = T("冲突：与「" + o.NameZh + "」相同", "conflict with " + o.NameEn);
                                break;
                            }
                        }
                    }
                    string lastTxt = "";
                    DateTime lt;
                    if (hkLastFire.TryGetValue(it.Key, out lt))
                        lastTxt = lt.ToString("MM-dd HH:mm:ss");
                    else lastTxt = it.Mods == 0 ? "" : T("还没按过", "never");
                    ListViewItem li = new ListViewItem(new string[] { T(it.NameZh, it.NameEn), disp, state, lastTxt });
                    li.Tag = it.Key;
                    if (it.Mods == 0) li.ForeColor = UiKit.A(140, 150, 165);
                    lv.Items.Add(li);
                }
                lv.Invalidate();
                int act2 = 0;
                for (int i3 = 0; i3 < hkMgr.Items.Count; i3++) if (hkMgr.Items[i3].Mods != 0) act2++;
                st.Text = T("共 " + hkMgr.Items.Count + " 项，其中 " + act2 + " 项已绑定组合键；双击行可改键。",
                            hkMgr.Items.Count + " entries, " + act2 + " bound. Double-click to rebind.");
            };

            // 窗口开着时每 2 秒刷一次，「上次触发」和冲突状态不用关窗再看
            System.Windows.Forms.Timer hkTimer = new System.Windows.Forms.Timer();
            hkTimer.Interval = 2000;
            hkTimer.Tick += delegate { try { refresh(); } catch (Exception dg17917_1) { Diag.Swallow("MainForm.cs:7128", dg17917_1); } };
            f.Shown += delegate { try { hkTimer.Start(); } catch (Exception dg17918_1) { Diag.Swallow("MainForm.cs:7129", dg17918_1); } };
            f.FormClosed += delegate { try { hkTimer.Stop(); hkTimer.Dispose(); } catch (Exception dg17919_1) { Diag.Swallow("MainForm.cs:7130", dg17919_1); } };

            Action<string> apply = delegate (string msg)
            {
                if (hkMgr == null) return;
                hkMgr.Save();
                hkMgr.RegisterAll();
                st.Text = msg + (hkMgr.LastError.Length > 0 ? "  " + hkMgr.LastError : T("  生效热键 ", "  active: ") + hkMgr.BindCount + T(" 个", ""));
                st.ForeColor = hkMgr.LastError.Length > 0 ? UiKit.A(249, 115, 22) : UiKit.A(120, 220, 160);
                refresh();
            };

            lv.DoubleClick += delegate
            {
                if (lv.SelectedItems.Count == 0 || hkMgr == null) return;
                string key = Convert.ToString(lv.SelectedItems[0].Tag);
                HotkeyManager.Item it = hkMgr.Get(key);
                if (it == null) return;
                HotkeyCaptureForm cf = new HotkeyCaptureForm(T(it.NameZh, it.NameEn), langEn);
                if (cf.ShowDialog(f) == DialogResult.OK)
                {
                    it.Mods = cf.Mods; it.Vk = cf.Vk;
                    string conf = hkMgr.ConflictOf(it);
                    if (conf != null) st.Text = T("注意：和「", "Note: same as ") + conf + T("」重复了", "");
                    apply(T("已设置：", "Set: ") + HotkeyManager.Display(it.Mods, it.Vk));
                }
            };

            Button bSet = MakeBtn(T("设置组合键", "Set"), UiKit.A(59, 130, 246), Color.White, 20, 424, 130, 34);
            bSet.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) { st.Text = T("先选中一行", "Select a row first"); return; }
                string key = Convert.ToString(lv.SelectedItems[0].Tag);
                HotkeyManager.Item it = hkMgr.Get(key);
                if (it == null) return;
                HotkeyCaptureForm cf = new HotkeyCaptureForm(T(it.NameZh, it.NameEn), langEn);
                if (cf.ShowDialog(f) == DialogResult.OK)
                {
                    it.Mods = cf.Mods; it.Vk = cf.Vk;
                    apply(T("已设置：", "Set: ") + HotkeyManager.Display(it.Mods, it.Vk));
                }
            };
            f.Controls.Add(bSet);

            Button bClr = MakeBtn(T("清除", "Clear"), C_BUBBLE_ALT(), Color.White, 160, 424, 100, 34);
            bClr.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                string key = Convert.ToString(lv.SelectedItems[0].Tag);
                HotkeyManager.Item it = hkMgr.Get(key);
                if (it == null) return;
                it.Mods = 0; it.Vk = 0;
                apply(T("已清除", "Cleared"));
            };
            f.Controls.Add(bClr);

            Button bDef = MakeBtn(T("恢复默认", "Defaults"), C_BUBBLE_ALT(), Color.White, 270, 424, 110, 34);
            bDef.Click += delegate
            {
                if (hkMgr == null) return;
                string[] dk = { "shot", "main", "xfer", "plus", "cliphist", "record", "palette" };
                int[] dm = { 3, 3, 3, 3, 3, 3, 3 };
                int[] dv = { 83, 72, 84, 71, 86, 82, 32 };
                foreach (HotkeyManager.Item it in hkMgr.Items) { it.Mods = 0; it.Vk = 0; }
                for (int i = 0; i < dk.Length; i++)
                {
                    HotkeyManager.Item it = hkMgr.Get(dk[i]);
                    if (it != null) { it.Mods = dm[i]; it.Vk = dv[i]; }
                }
                apply(T("已恢复默认", "Defaults restored"));
            };
            f.Controls.Add(bDef);

            Button bRe = MakeBtn(T("重新注册", "Re-register"), UiKit.A(14, 165, 233), Color.White, 390, 424, 120, 34);
            bRe.Click += delegate { apply(T("已重新注册", "Re-registered")); };
            f.Controls.Add(bRe);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 640, 424, 100, 34);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            f.Shown += delegate { refresh(); apply(""); };
            ShowOrTrack(f);
        }

        void ShowGameBoost()
        {
            Form f = new Form();
            f.Text = T("一键游戏加速 · 小工具箱", "Game Boost · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 640);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("一键游戏加速", "One-click Game Boost");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("勾选要提升优先级的游戏进程，然后把后台程序让路、切高性能电源、刷 DNS；全程写备份，随时可一键还原。不关进程、不动数据。",
                "Tick the game process, let background apps step back, switch to high power, flush DNS. All backed up and reversible.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 48);
            sub.Size = new Size(850, 40);
            f.Controls.Add(sub);

            Label lt = new Label();
            lt.Text = T("当前前台程序（勾选要加速的游戏）", "Foreground processes (tick your game)");
            lt.ForeColor = UiKit.A(180, 190, 205);
            lt.Location = new Point(20, 88);
            lt.Size = new Size(430, 20);
            f.Controls.Add(lt);

            ListView lv = MakeDarkList(new string[] { T("进程", "Process"), T("说明", "Info") }, new int[] { 200, 210 }, 20, 112, 430, 300);
            lv.CheckBoxes = true;
            f.Controls.Add(lv);

            CheckBox ckProc = new RoundCheck();
            ckProc.Text = T("提升游戏进程优先级为「高」", "Raise game process priority to High");
            ckProc.ForeColor = UiKit.A(220, 230, 240);
            ckProc.Location = new Point(470, 112);
            ckProc.Size = new Size(400, 22);
            ckProc.Checked = true;
            f.Controls.Add(ckProc);

            CheckBox ckPower = new RoundCheck();
            ckPower.Text = T("切换到「高性能」电源计划", "Switch to High performance power plan");
            ckPower.ForeColor = UiKit.A(220, 230, 240);
            ckPower.Location = new Point(470, 142);
            ckPower.Size = new Size(400, 22);
            ckPower.Checked = true;
            f.Controls.Add(ckPower);

            CheckBox ckDns = new RoundCheck();
            ckDns.Text = T("刷新 DNS 缓存", "Flush DNS cache");
            ckDns.ForeColor = UiKit.A(220, 230, 240);
            ckDns.Location = new Point(470, 172);
            ckDns.Size = new Size(400, 22);
            ckDns.Checked = true;
            f.Controls.Add(ckDns);

            CheckBox ckLow = new RoundCheck();
            ckLow.Text = T("其他前台程序降到「低于正常」优先级（不关进程）", "Lower other apps to BelowNormal (no killing)");
            ckLow.ForeColor = UiKit.A(220, 230, 240);
            ckLow.Location = new Point(470, 202);
            ckLow.Size = new Size(400, 22);
            ckLow.Checked = true;
            f.Controls.Add(ckLow);

            CheckBox ckSvc = new RoundCheck();
            ckSvc.Text = T("暂停 Windows Search 索引（需管理员）", "Pause Windows Search index (needs admin)");
            ckSvc.ForeColor = UiKit.A(220, 230, 240);
            ckSvc.Location = new Point(470, 232);
            ckSvc.Size = new Size(400, 22);
            f.Controls.Add(ckSvc);

            Label lman = new Label();
            lman.Text = T("或手动填进程名（逗号分隔，如 Game.exe,cs2.exe）", "Or type process names (comma separated)");
            lman.ForeColor = UiKit.A(150, 160, 176);
            lman.Location = new Point(470, 266);
            lman.Size = new Size(400, 20);
            f.Controls.Add(lman);

            TextBox tbMan = new TextBox();
            tbMan.BackColor = UiKit.A(32, 38, 46);
            tbMan.ForeColor = UiKit.Txt(Color.White);
            tbMan.BorderStyle = BorderStyle.FixedSingle;
            tbMan.Location = new Point(470, 288);
            tbMan.Size = new Size(400, 26);
            tbMan.Text = "";
            f.Controls.Add(tbMan);

            Label lnote = new Label();
            lnote.Text = T("加速不会结束任何进程，也不动文件；「一键还原」按备份把电源计划、优先级、服务全部改回去。",
                "Nothing is killed and no file is touched; Restore puts power plan, priorities and services back.");
            lnote.ForeColor = UiKit.A(120, 132, 148);
            lnote.Font = new Font("Microsoft YaHei UI", 8.5F);
            lnote.Location = new Point(470, 324);
            lnote.Size = new Size(400, 80);
            f.Controls.Add(lnote);

            TextBox log = new TextBox();
            log.BackColor = UiKit.A(16, 20, 26);
            log.ForeColor = UiKit.A(190, 205, 220);
            log.BorderStyle = BorderStyle.FixedSingle;
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.ReadOnly = true;
            log.Font = new Font("Consolas", 9F);
            log.Location = new Point(20, 470);
            log.Size = new Size(860, 120);
            f.Controls.Add(log);

            Button bRun = MakeBtn(T("一键加速", "Boost now"), UiKit.A(249, 115, 22), Color.White, 20, 424, 150, 38);
            bRun.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            f.Controls.Add(bRun);

            Button bBack = MakeBtn(T("一键还原", "Restore"), UiKit.A(16, 185, 129), Color.White, 185, 424, 150, 38);
            f.Controls.Add(bBack);

            Button bBk = MakeBtn(T("查看备份", "Show backup"), C_BUBBLE_ALT(), Color.White, 350, 424, 120, 38);
            f.Controls.Add(bBk);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 424, 100, 38);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            string bkPath = Path.Combine(BASE_DIR, "加速备份.tsv");

            Action fill = delegate
            {
                lv.Items.Clear();
                List<string> tops = BoostCore.TopProcesses(18);
                foreach (string nm in tops)
                {
                    ListViewItem li = new ListViewItem(new string[] { nm, T("前台窗口程序", "has a window") });
                    li.Tag = nm;
                    lv.Items.Add(li);
                }
                if (lv.Items.Count == 0)
                {
                    ListViewItem li2 = new ListViewItem(new string[] { T("没有可列举的前台程序", "nothing listed"), "" });
                    li2.ForeColor = UiKit.A(120, 130, 145);
                    lv.Items.Add(li2);
                }
                lv.Invalidate();
            };

            Action<string> doRun = delegate (string mode)
            {
                List<string> names = new List<string>();
                foreach (ListViewItem li in lv.CheckedItems)
                {
                    string nm = Convert.ToString(li.Tag);
                    if (nm != null && nm.Length > 0) names.Add(nm);
                }
                string manual = tbMan.Text;
                foreach (string part in manual.Split(',')) if (part.Trim().Length > 0) names.Add(part.Trim());
                if (names.Count == 0) { log.Text = T("请先勾选一个游戏进程，或在下面手动填进程名。", "Tick a process or type a name first."); return; }

                if (mode == "run")
                {
                    log.Text = T("正在加速……\n", "Boosting...\n");
                    Application.DoEvents();
                    string rep = BoostCore.Run(bkPath, false, names, ckProc.Checked, ckPower.Checked, ckDns.Checked, ckLow.Checked, ckSvc.Checked);
                    log.Text = rep;
                    Log("[加速] " + names.Count + " 个进程");
                }
                else
                {
                    string rep = BoostCore.Restore(bkPath, false);
                    log.Text = rep;
                    Log("[加速] 已还原");
                }
            };

            bRun.Click += delegate { doRun("run"); };
            bBack.Click += delegate { doRun("back"); };
            bBk.Click += delegate
            {
                if (!File.Exists(bkPath)) { log.Text = T("还没有备份文件（先做一次加速）。", "No backup yet."); return; }
                try { log.Text = File.ReadAllText(bkPath, Encoding.UTF8); } catch (Exception ex) { log.Text = ex.Message; }
            };

            f.Shown += delegate
            {
                fill();
                if (!BoostCore.IsAdmin()) log.Text = T("提示：当前不是管理员权限，「暂停索引服务」会被跳过，其余项照常。\r\n",
                    "Note: not running as admin; the search-service step will be skipped.\r\n");
            };
            ShowOrTrack(f);
        }

        void ShowDeskSort()
        {
            Form f = new Form();
            f.Text = T("桌面自动整理 · 小工具箱", "Desktop Tidy · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 620);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("桌面自动整理", "Desktop Tidy");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("按「图片 / 视频 / 音频 / 文档 / 压缩包 / 安装包 / 其他」把桌面文件收进「整理-XX」文件夹；快捷方式不动，每次整理都留记录，可一键撤销。",
                "Group desktop files into 整理-XX folders by type; shortcuts untouched, every run recorded and reversible.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 48);
            sub.Size = new Size(850, 40);
            f.Controls.Add(sub);

            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string recDir = Path.Combine(BASE_DIR, "整理记录");

            TextBox tbDir = new TextBox();
            tbDir.BackColor = UiKit.A(32, 38, 46);
            tbDir.ForeColor = UiKit.Txt(Color.White);
            tbDir.BorderStyle = BorderStyle.FixedSingle;
            tbDir.Location = new Point(20, 94);
            tbDir.Size = new Size(590, 26);
            tbDir.Text = desk;
            f.Controls.Add(tbDir);

            Button bBrowse = MakeBtn(T("选文件夹", "Browse"), C_BUBBLE_ALT(), Color.White, 620, 92, 100, 30);
            f.Controls.Add(bBrowse);
            Button bDef = MakeBtn(T("回到桌面", "Use Desktop"), C_BUBBLE_ALT(), Color.White, 730, 92, 110, 30);
            f.Controls.Add(bDef);

            ListView lv = MakeDarkList(new string[] { T("文件", "File"), T("类型", "Type"), T("将移动到", "Move to"), T("大小", "Size") },
                new int[] { 320, 100, 300, 110 }, 20, 176, 860, 330);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 520);
            st.Size = new Size(860, 44);
            f.Controls.Add(st);

            Button bScan = MakeBtn(T("扫描预览", "Preview"), UiKit.A(14, 165, 233), Color.White, 20, 132, 120, 34);
            f.Controls.Add(bScan);
            Button bGo = MakeBtn(T("执行整理", "Tidy now"), UiKit.A(16, 185, 129), Color.White, 150, 132, 120, 34);
            f.Controls.Add(bGo);
            Button bUndo = MakeBtn(T("撤销上次整理", "Undo last"), UiKit.A(249, 115, 22), Color.White, 280, 132, 140, 34);
            f.Controls.Add(bUndo);
            Button bOpen = MakeBtn(T("打开该文件夹", "Open folder"), C_BUBBLE_ALT(), Color.White, 430, 132, 130, 34);
            f.Controls.Add(bOpen);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 568, 100, 30);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            List<string[]> plan = new List<string[]>();

            Action scan = delegate
            {
                string dir = tbDir.Text.Trim();
                if (dir.Length == 0 || !Directory.Exists(dir)) { st.Text = T("目录不存在。", "Folder not found."); return; }
                plan = DeskSortCore.Plan(dir);
                lv.Items.Clear();
                foreach (string[] p in plan)
                {
                    string sz = "";
                    try { sz = FmtSize(new FileInfo(p[0]).Length); } catch (Exception dg18276_1) { Diag.Swallow("MainForm.cs:7487", dg18276_1); }
                    ListViewItem li = new ListViewItem(new string[] { p[1], p[2], "整理-" + p[2], sz });
                    li.Tag = p[0];
                    lv.Items.Add(li);
                }
                if (lv.Items.Count == 0)
                {
                    ListViewItem li2 = new ListViewItem(new string[] { T("这个文件夹里没有可整理的文件", "nothing to tidy here"), "", "", "" });
                    li2.ForeColor = UiKit.A(120, 130, 145);
                    lv.Items.Add(li2);
                }
                st.ForeColor = UiKit.A(120, 220, 160);
                st.Text = T("预览完成：共 ", "Preview: ") + plan.Count + T(" 个文件待整理（.lnk 快捷方式会自动跳过）。", " files (shortcuts skipped).");
                lv.Invalidate();
            };

            bScan.Click += delegate { scan(); };

            bGo.Click += delegate
            {
                if (plan.Count == 0) scan();
                if (plan.Count == 0) { st.Text = T("没有可整理的文件。", "Nothing to tidy."); return; }
                if (MessageBox.Show(f, T("将把 ", "Move ") + plan.Count + T(" 个文件移到「整理-XX」子文件夹，随时可撤销。继续？", " files into 整理-XX folders (reversible). Continue?"),
                    "小工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                string rep = DeskSortCore.Execute(plan, recDir, false);
                st.ForeColor = UiKit.A(120, 220, 160);
                st.Text = rep.Replace("\r\n", "  ");
                Log("[整理] " + rep.Replace("\r\n", " "));
                plan = new List<string[]>();
                lv.Items.Clear();
            };

            bUndo.Click += delegate
            {
                string rec = DeskSortCore.LatestRecord(recDir);
                if (rec == null) { st.ForeColor = UiKit.A(249, 115, 22); st.Text = T("没有整理记录，无法撤销。", "No record to undo."); return; }
                string rep = DeskSortCore.Undo(rec, false);
                st.ForeColor = UiKit.A(120, 220, 160);
                st.Text = rep.Replace("\r\n", "  ");
                Log("[整理] 撤销 " + Path.GetFileName(rec));
                scan();
            };

            bBrowse.Click += delegate
            {
                FolderBrowserDialog fb = new FolderBrowserDialog();
                fb.Description = T("选择要整理的文件夹（默认是桌面）", "Pick a folder to tidy (Desktop by default)");
                if (fb.ShowDialog(f) == DialogResult.OK) { tbDir.Text = fb.SelectedPath; scan(); }
            };

            bDef.Click += delegate { tbDir.Text = desk; scan(); };

            bOpen.Click += delegate
            {
                string dir = tbDir.Text.Trim();
                try { if (Directory.Exists(dir)) Process.Start("explorer.exe", dir); } catch (Exception dg18331_1) { Diag.Swallow("MainForm.cs:7542", dg18331_1); }
            };

            f.Shown += delegate { scan(); };
            ShowOrTrack(f);
        }

        void ShowDupClean()
        {
            Form f = new Form();
            f.Text = T("重复文件清理 · 小工具箱", "Duplicate Finder · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 640);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("重复文件清理", "Duplicate Finder");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("按「大小 → 首 8KB 指纹 → 全文 MD5」三重比对，同组只留一个；删除走回收站，随时能还原。",
                "Size → 8KB fingerprint → full MD5. One copy per group is kept; deletes go to the Recycle Bin.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 48);
            sub.Size = new Size(850, 24);
            f.Controls.Add(sub);

            TextBox tbDir = new TextBox();
            tbDir.BackColor = UiKit.A(32, 38, 46);
            tbDir.ForeColor = UiKit.Txt(Color.White);
            tbDir.BorderStyle = BorderStyle.FixedSingle;
            tbDir.Location = new Point(20, 84);
            tbDir.Size = new Size(560, 26);
            tbDir.Text = "C:\\";
            f.Controls.Add(tbDir);

            Button bBrowse = MakeBtn(T("选目录", "Browse"), C_BUBBLE_ALT(), Color.White, 590, 82, 90, 30);
            f.Controls.Add(bBrowse);

            ComboBox cbMin = new ComboBox();
            cbMin.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMin.BackColor = UiKit.A(32, 38, 46);
            cbMin.ForeColor = UiKit.Txt(Color.White);
            cbMin.FlatStyle = FlatStyle.Standard;
            cbMin.Location = new Point(690, 84);
            cbMin.Size = new Size(90, 26);
            cbMin.Items.AddRange(new object[] { "1 KB", "100 KB", "1 MB", "10 MB" });
            cbMin.SelectedIndex = 2;
            f.Controls.Add(cbMin);

            Label lmin = new Label();
            lmin.Text = T("最小", "Min");
            lmin.ForeColor = UiKit.A(150, 160, 176);
            lmin.Location = new Point(790, 88);
            lmin.Size = new Size(80, 20);
            f.Controls.Add(lmin);

            ListView lv = MakeDarkList(new string[] { T("组", "Group"), T("文件", "File"), T("大小", "Size"), T("所在目录", "Folder") },
                new int[] { 50, 260, 90, 440 }, 20, 168, 860, 370);
            lv.CheckBoxes = true;
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 556);
            st.Size = new Size(860, 44);
            f.Controls.Add(st);

            Button bScan = MakeBtn(T("开始扫描", "Scan"), UiKit.A(14, 165, 233), Color.White, 20, 122, 120, 34);
            f.Controls.Add(bScan);
            Button bStop = MakeBtn(T("停止", "Stop"), UiKit.A(232, 82, 61), Color.White, 150, 122, 90, 34);
            bStop.Enabled = false;
            f.Controls.Add(bStop);
            Button bAll = MakeBtn(T("勾选冗余副本", "Tick extras"), C_BUBBLE_ALT(), Color.White, 250, 122, 130, 34);
            f.Controls.Add(bAll);
            Button bDel = MakeBtn(T("移入回收站", "To Recycle Bin"), UiKit.A(249, 115, 22), Color.White, 390, 122, 140, 34);
            f.Controls.Add(bDel);
            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 588, 100, 30);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            bool cancel = false;
            int groupNo = 0;
            List<string> keepList = new List<string>();

            Action clearList = delegate { lv.Items.Clear(); lv.Invalidate(); groupNo = 0; keepList.Clear(); };

            bScan.Click += delegate
            {
                string dir = tbDir.Text.Trim();
                if (dir.Length == 0 || !Directory.Exists(dir)) { st.Text = T("目录不存在。", "Folder not found."); return; }
                long minSize = 1024L * 1024L;
                if (cbMin.SelectedIndex == 0) minSize = 1024L;
                else if (cbMin.SelectedIndex == 1) minSize = 100L * 1024L;
                else if (cbMin.SelectedIndex == 2) minSize = 1024L * 1024L;
                else if (cbMin.SelectedIndex == 3) minSize = 10L * 1024L * 1024L;

                clearList();
                cancel = false;
                bScan.Enabled = false; bStop.Enabled = true;
                st.ForeColor = UiKit.A(96, 165, 250);
                st.Text = T("扫描中……", "Scanning...");

                Thread th = new Thread(delegate ()
                {
                    try
                    {
                        List<string> files = DupCore.Collect(dir, minSize,
                            delegate (string msg) { try { f.BeginInvoke((Action)delegate { st.Text = msg; }); } catch (Exception dg18447_1) { Diag.Swallow("MainForm.cs:7658", dg18447_1); } },
                            delegate () { return cancel; });
                        if (files == null) { try { f.BeginInvoke((Action)delegate { st.ForeColor = UiKit.A(249, 115, 22); st.Text = T("已停止。", "Stopped."); bScan.Enabled = true; bStop.Enabled = false; }); } catch (Exception dg18449_1) { Diag.Swallow("MainForm.cs:7660", dg18449_1); } return; }

                        List<List<string>> groups = DupCore.Group(files,
                            delegate (string msg) { try { f.BeginInvoke((Action)delegate { st.Text = msg; }); } catch (Exception dg18452_1) { Diag.Swallow("MainForm.cs:7663", dg18452_1); } },
                            delegate () { return cancel; });

                        f.BeginInvoke((Action)delegate
                        {
                            groupNo = 0;
                            long wasted = 0;
                            keepList.Clear();
                            foreach (List<string> g in groups)
                            {
                                groupNo++;
                                long sz = 0;
                                try { sz = new FileInfo(g[0]).Length; } catch (Exception dg18464_1) { Diag.Swallow("MainForm.cs:7675", dg18464_1); }
                                string szs = FmtSize(sz);
                                for (int i = 0; i < g.Count; i++)
                                {
                                    string p = g[i];
                                    ListViewItem li = new ListViewItem(new string[] { groupNo.ToString(), Path.GetFileName(p), szs, Path.GetDirectoryName(p) });
                                    li.Tag = p;
                                    if (i == 0) { li.Checked = false; keepList.Add(p); li.ForeColor = UiKit.A(150, 200, 160); }
                                    else { li.Checked = true; wasted += sz; }
                                    lv.Items.Add(li);
                                }
                            }
                            lv.Invalidate();
                            bScan.Enabled = true; bStop.Enabled = false;
                            st.ForeColor = UiKit.A(120, 220, 160);
                            st.Text = T("扫完：", "Done: ") + groupNo + T(" 组重复，可释放约 ", " groups, about ") + FmtSize(wasted)
                                + T("（默认只勾选冗余副本，每组保留第一个）", " (one copy per group kept)");
                        });
                    }
                    catch (Exception ex)
                    {
                        try { f.BeginInvoke((Action)delegate { bScan.Enabled = true; bStop.Enabled = false; st.Text = "EX " + ex.Message; }); } catch (Exception dg18485_1) { Diag.Swallow("MainForm.cs:7696", dg18485_1); }
                    }
                });
                th.IsBackground = true;
                th.Start();
            };

            bStop.Click += delegate { cancel = true; st.Text = T("正在停止……", "Stopping..."); };

            bAll.Click += delegate
            {
                foreach (ListViewItem li in lv.Items)
                {
                    string p = Convert.ToString(li.Tag);
                    li.Checked = !keepList.Contains(p);
                }
                lv.Invalidate();
            };

            bDel.Click += delegate
            {
                List<string> sel = new List<string>();
                long total = 0;
                foreach (ListViewItem li in lv.CheckedItems)
                {
                    string p = Convert.ToString(li.Tag);
                    if (p == null || p.Length == 0) continue;
                    if (keepList.Contains(p)) continue;
                    sel.Add(p);
                    try { total += new FileInfo(p).Length; } catch (Exception dg18514_1) { Diag.Swallow("MainForm.cs:7725", dg18514_1); }
                }
                if (sel.Count == 0) { st.ForeColor = UiKit.A(249, 115, 22); st.Text = T("没有勾选任何文件。", "Nothing ticked."); return; }
                if (MessageBox.Show(f, T("将把 ", "Move ") + sel.Count + T(" 个文件（", " files (") + FmtSize(total) + T("）移入回收站，可在回收站还原。继续？", ") to the Recycle Bin. Continue?"),
                    "小工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

                int ok = 0, bad = 0;
                StringBuilder rec = new StringBuilder();
                bool first = true;
                rec.Append("{\"time\":\"").Append(VaultStore.Esc(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append("\",\"items\":[");
                    foreach (string p in sel)
                {
                    if (RecycleHelper.Delete(p))
                    {
                        ok++;
                        if (!first) rec.Append(",");
                        rec.Append("\"").Append(VaultStore.Esc(p)).Append("\"");
                    }
                    else bad++;
                }
                rec.Append("]}");
                try
                {
                    string rdir = Path.Combine(BASE_DIR, "查重记录");
                    if (!Directory.Exists(rdir)) Directory.CreateDirectory(rdir);
                    File.WriteAllText(Path.Combine(rdir, "删除_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"), rec.ToString(), Encoding.UTF8);
                }
                catch (Exception dg18541_1) { Diag.Swallow("MainForm.cs:7752", dg18541_1); }
                st.ForeColor = UiKit.A(120, 220, 160);
                st.Text = T("已移入回收站 ", "Recycled ") + ok + T(" 个，释放约 ", ", freed about ") + FmtSize(total) + (bad > 0 ? T("，失败 ", ", failed ") + bad + T(" 个", "") : "");
                Log("[查重] 回收 " + ok + " 个文件 " + FmtSize(total));
            };

            bBrowse.Click += delegate
            {
                FolderBrowserDialog fb = new FolderBrowserDialog();
                fb.Description = T("选择要查重的目录", "Pick a folder to scan");
                if (fb.ShowDialog(f) == DialogResult.OK) tbDir.Text = fb.SelectedPath;
            };

            ShowOrTrack(f);
        }

        // ================= v6.8 自测通道 =================

        string MiniHttp(string url, string postBody)
        {
            try
            {
                Uri u = new Uri(url);
                using (TcpClient c = new TcpClient())
                {
                    c.Connect(u.Host, u.Port);
                    c.ReceiveTimeout = 20000;
                    NetworkStream ns = c.GetStream();
                    string path = u.PathAndQuery;
                    StringBuilder req = new StringBuilder();
                    req.Append(postBody == null ? "GET " : "POST ").Append(path).Append(" HTTP/1.1\r\n");
                    req.Append("Host: ").Append(u.Host).Append("\r\nConnection: close\r\n");
                    req.Append("User-Agent: MiniToolboxTest\r\n");
                    if (postBody == null)
                    {
                        req.Append("\r\n");
                        byte[] hb0 = Encoding.UTF8.GetBytes(req.ToString());
                        ns.Write(hb0, 0, hb0.Length);
                    }
                    else
                    {
                        byte[] bb = Encoding.UTF8.GetBytes(postBody);
                        req.Append("Content-Type: application/x-www-form-urlencoded\r\n");
                        req.Append("Content-Length: ").Append(bb.Length).Append("\r\n\r\n");
                        byte[] hb = Encoding.UTF8.GetBytes(req.ToString());
                        ns.Write(hb, 0, hb.Length);
                        ns.Write(bb, 0, bb.Length);
                    }
                    MemoryStream ms = new MemoryStream();
                    byte[] buf = new byte[4096];
                    while (true)
                    {
                        int n = ns.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                        if (ms.Length > 3 * 1024 * 1024) break;
                    }
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch (Exception ex) { return "EX " + ex.Message; }
        }

        void DumpTest(string name, string content)
        {
            try { File.WriteAllText(Path.Combine(BASE_DIR, name), content, Encoding.UTF8); } catch (Exception dg18606_1) { Diag.Swallow("MainForm.cs:7817", dg18606_1); }
        }

        void HkSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                if (hkMgr == null)
                {
                    hkMgr = new HotkeyManager(this.Handle, Path.Combine(BASE_DIR, "hotkeys.json"));
                    hkMgr.OnFire = HotkeyAction;
                    hkMgr.Load();
                }
                hkMgr.Save();
                hkMgr.RegisterAll();
                sb.AppendLine("bind=" + hkMgr.BindCount);
                sb.AppendLine("error=" + (hkMgr.LastError.Length == 0 ? "none" : hkMgr.LastError));
                sb.AppendLine("cfg=" + (File.Exists(Path.Combine(BASE_DIR, "hotkeys.json")) ? "written" : "missing"));
                foreach (HotkeyManager.Item it in hkMgr.Items)
                    sb.AppendLine(it.Key + " | " + it.NameZh + " | " + HotkeyManager.Display(it.Mods, it.Vk) + (it.Mods == 0 ? " | unset" : ""));
                sb.AppendLine("display=" + HotkeyManager.Display(3, 83) + " / " + HotkeyManager.Display(5, 112) + " / " + HotkeyManager.Display(9, 48));
                sb.AppendLine("conflict=" + (hkMgr.ConflictOf(hkMgr.Get("shot")) == null ? "none" : "hit"));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_hk_test.txt", sb.ToString());
            Application.Exit();
        }

        void ClipSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string dir = Path.Combine(BASE_DIR, "_cliptest");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg18639_1) { Diag.Swallow("MainForm.cs:7850", dg18639_1); }
            try { Directory.CreateDirectory(Path.Combine(dir, "in")); Directory.CreateDirectory(Path.Combine(dir, "out")); } catch (Exception dg18640_1) { Diag.Swallow("MainForm.cs:7851", dg18640_1); }
            LanXferServer srv = new LanXferServer(Path.Combine(dir, "in"), Path.Combine(dir, "out"));
            string got = "";
            srv.ClipGet = delegate () { return "剪贴板内容ABC"; };
            srv.ClipSet = delegate (string t) { got = t; };
            string err = srv.Start();
            sb.AppendLine("start=" + (err == null ? "ok port=" + srv.Port : "ERR " + err));
            if (err == null)
            {
                string baseUrl = "http://127.0.0.1:" + srv.Port;
                string g1 = MiniHttp(baseUrl + "/clip", null);
                sb.AppendLine("GET /clip code=" + (g1.StartsWith("HTTP/1.1 200") ? "200" : "bad") + " hasText=" + g1.Contains("剪贴板内容ABC") + " hasBox=" + g1.Contains("<textarea"));
                string g2 = MiniHttp(baseUrl + "/clip?fmt=json", null);
                sb.AppendLine("GET /clip?fmt=json hasJson=" + g2.Contains("\"text\""));
                string p1 = MiniHttp(baseUrl + "/clip", "text=" + Uri.EscapeDataString("手机写回的内容 123"));
                sb.AppendLine("POST /clip code=" + (p1.StartsWith("HTTP/1.1 200") ? "200" : "bad") + " setGot=" + got);
                string page = MiniHttp(baseUrl + "/", null);
                sb.AppendLine("GET / page=" + (page.StartsWith("HTTP/1.1 200") ? "200" : "bad") + " clipLink=" + page.Contains("/clip"));
            }
            srv.Stop();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg18660_1) { Diag.Swallow("MainForm.cs:7871", dg18660_1); }
            DumpTest("_clip_test.txt", sb.ToString());
            Application.Exit();
        }

        void BoostSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.AppendLine("admin=" + BoostCore.IsAdmin());
                sb.AppendLine("powerNow=" + BoostCore.CurrentPowerGuid());
                List<string> names = new List<string>();
                names.Add("explorer");
                string rep = BoostCore.Run(Path.Combine(BASE_DIR, "_boost_test.tsv"), true, names, true, true, true, true, true);
                sb.AppendLine("--- dry run ---");
                sb.AppendLine(rep);
                string bkTest = Path.Combine(BASE_DIR, "_boost_bk_test.tsv");
                File.WriteAllText(bkTest, "# test\r\nPOWER\t381b4222-f694-41f0-9685-ff5bb260df2e\r\nLOW\t" + Process.GetCurrentProcess().Id + "\t小工具箱\r\nSVC\tWSearch\tauto\r\n", Encoding.UTF8);
                sb.AppendLine("--- restore parse (dry) ---");
                sb.AppendLine(BoostCore.Restore(bkTest, true));
                File.Delete(bkTest);
                sb.AppendLine("top=" + string.Join(",", BoostCore.TopProcesses(5).ToArray()));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_boost_test.txt", sb.ToString());
            Application.Exit();
        }

        void DeskSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string dir = Path.Combine(BASE_DIR, "_desktest");
            string recDir = Path.Combine(BASE_DIR, "_desktest_rec");
            try
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg18696_1) { Diag.Swallow("MainForm.cs:7907", dg18696_1); }
                try { if (Directory.Exists(recDir)) Directory.Delete(recDir, true); } catch (Exception dg18697_1) { Diag.Swallow("MainForm.cs:7908", dg18697_1); }
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "截图测试.png"), "x");
                File.WriteAllText(Path.Combine(dir, "压缩包.zip"), "xx");
                File.WriteAllText(Path.Combine(dir, "说明.txt"), "xxx");
                File.WriteAllText(Path.Combine(dir, "安装.exe"), "xxxx");
                File.WriteAllText(Path.Combine(dir, "某个快捷方式.lnk"), "lnk");
                File.WriteAllText(Path.Combine(dir, "视频.mp4"), "v");
                sb.AppendLine("type_of=" + DeskSortCore.TypeOf("a.png") + "/" + DeskSortCore.TypeOf("b.zip") + "/" + DeskSortCore.TypeOf("c.txt") + "/" + DeskSortCore.TypeOf("d.exe") + "/" + DeskSortCore.TypeOf("e.mp4") + "/" + DeskSortCore.TypeOf("f.bin"));

                List<string[]> plan = DeskSortCore.Plan(dir);
                sb.AppendLine("plan=" + plan.Count + " (lnk 应被跳过)");
                sb.AppendLine(DeskSortCore.Execute(plan, recDir, true));

                string rep = DeskSortCore.Execute(plan, recDir, false);
                sb.AppendLine(rep.Replace("\r\n", " | "));
                string rec = DeskSortCore.LatestRecord(recDir);
                sb.AppendLine("record=" + (rec == null ? "none" : Path.GetFileName(rec)));
                sb.AppendLine("desktop_files_left=" + Directory.GetFiles(dir).Length);
                sb.AppendLine("整理-图片 has=" + (Directory.Exists(Path.Combine(dir, "整理-图片")) && Directory.GetFiles(Path.Combine(dir, "整理-图片")).Length > 0));
                string undo = DeskSortCore.Undo(rec, false);
                sb.AppendLine("undo=" + undo.Replace("\r\n", " | "));
                sb.AppendLine("after_undo_files=" + Directory.GetFiles(dir).Length);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg18722_1) { Diag.Swallow("MainForm.cs:7933", dg18722_1); }
            try { if (Directory.Exists(recDir)) Directory.Delete(recDir, true); } catch (Exception dg18723_1) { Diag.Swallow("MainForm.cs:7934", dg18723_1); }
            DumpTest("_desk_test.txt", sb.ToString());
            Application.Exit();
        }

        void VaultSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string box = Path.Combine(BASE_DIR, "_vault_test.box");
            try
            {
                VaultStore v = new VaultStore();
                VaultStore.Entry e1 = new VaultStore.Entry();
                e1.id = "1"; e1.cat = "游戏"; e1.title = "Steam 账号 \"主号\""; e1.user = "abc@qq.com";
                e1.pass = "P@ssw0rd!中文"; e1.note = "多行备注\r\n第二行 + 引号\"x\""; e1.upd = "2026-11-16 10:00";
                v.Entries.Add(e1);
                VaultStore.Entry e2 = new VaultStore.Entry();
                e2.id = "2"; e2.cat = "网站"; e2.title = "某论坛"; e2.user = "user2"; e2.pass = "123456"; e2.note = ""; e2.upd = "2026-11-16 10:01";
                v.Entries.Add(e2);
                string err;
                bool ok = v.Save(box, "test123", out err);
                sb.AppendLine("save=" + ok + (ok ? "" : " " + err));

                byte[] raw = File.ReadAllBytes(box);
                sb.AppendLine("header=" + (char)raw[0] + (char)raw[1] + (char)raw[2] + (char)raw[3] + (char)raw[4] + " len=" + raw.Length);
                string whole = Encoding.UTF8.GetString(raw);
                sb.AppendLine("plain_leak=" + (whole.Contains("P@ssw0rd") || whole.Contains("abc@qq.com")));

                VaultStore v2 = new VaultStore();
                string err2;
                bool bad = v2.Load(box, "wrongpwd", out err2);
                sb.AppendLine("wrong_pwd=" + (bad ? "UNEXPECTED-OK" : "rejected(" + err2 + ")"));

                VaultStore v3 = new VaultStore();
                string err3;
                bool ok3 = v3.Load(box, "test123", out err3);
                sb.AppendLine("right_pwd=" + ok3 + " entries=" + v3.Entries.Count);
                if (ok3 && v3.Entries.Count >= 2)
                {
                    sb.AppendLine("e1=" + v3.Entries[0].cat + "/" + v3.Entries[0].title + "/" + v3.Entries[0].user + "/" + (v3.Entries[0].pass == "P@ssw0rd!中文" ? "pass_ok" : "pass_BAD"));
                    sb.AppendLine("e1note_lines=" + v3.Entries[0].note.Split('\n').Length);
                    sb.AppendLine("e2=" + v3.Entries[1].title + "/" + v3.Entries[1].pass);
                }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { if (File.Exists(box)) File.Delete(box); } catch (Exception dg18768_1) { Diag.Swallow("MainForm.cs:7979", dg18768_1); }
            DumpTest("_vault_test.txt", sb.ToString());
            Application.Exit();
        }

        void DupSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string dir = Path.Combine(BASE_DIR, "_duptest");
            try
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg18779_1) { Diag.Swallow("MainForm.cs:7990", dg18779_1); }
                Directory.CreateDirectory(dir);
                Directory.CreateDirectory(Path.Combine(dir, "sub"));
                string big = new string('A', 2000);
                string big2 = new string('B', 2000);
                File.WriteAllText(Path.Combine(dir, "a1.txt"), big);
                File.WriteAllText(Path.Combine(dir, "a2.txt"), big);
                File.WriteAllText(Path.Combine(dir, "sub", "a3.txt"), big);
                File.WriteAllText(Path.Combine(dir, "b1.txt"), big2);
                File.WriteAllText(Path.Combine(dir, "c.txt"), "unique-" + DateTime.Now.Ticks);
                Directory.CreateDirectory(Path.Combine(dir, "emptydir"));

                List<string> files = DupCore.Collect(dir, 0, null, null);
                sb.AppendLine("collect=" + (files == null ? "null" : files.Count.ToString()));
                List<List<string>> groups = DupCore.Group(files, null, null);
                sb.AppendLine("groups=" + groups.Count);
                foreach (List<string> g in groups)
                {
                    sb.AppendLine("  group(" + g.Count + ")=" + Path.GetFileName(g[0]) + " size=" + new FileInfo(g[0]).Length);
                }
                sb.AppendLine("md5_same=" + (DupCore.Md5Of(Path.Combine(dir, "a1.txt"), 0) == DupCore.Md5Of(Path.Combine(dir, "a2.txt"), 0)));
                sb.AppendLine("md5_diff=" + (DupCore.Md5Of(Path.Combine(dir, "a1.txt"), 0) != DupCore.Md5Of(Path.Combine(dir, "b1.txt"), 0)));
                sb.AppendLine("recycle_api=" + (RecycleHelper.Delete(Path.Combine(dir, "c.txt")) ? "ok" : "failed"));
                sb.AppendLine("after_recycle_exists=" + File.Exists(Path.Combine(dir, "c.txt")));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg18805_1) { Diag.Swallow("MainForm.cs:8016", dg18805_1); }
            DumpTest("_dup_test.txt", sb.ToString());
            Application.Exit();
        }

        void V68UiDump()
        {
            v68AutoDump = true;
            StringBuilder sb = new StringBuilder();
            try
            {
                ShowPlusCenter();
                ShowHotkeys();
                ShowGameBoost();
                ShowDeskSort();
                ShowDupClean();
                sb.AppendLine("opened=" + v68Forms.Count);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }

            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
            tm.Interval = 1600;
            tm.Tick += delegate
            {
                tm.Stop(); tm.Dispose();
                foreach (Form ff in v68Forms)
                {
                    try { sb.AppendLine("[" + ff.Text + "] controls=" + ff.Controls.Count + " size=" + ff.ClientSize.Width + "x" + ff.ClientSize.Height); }
                    catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
                }
                foreach (Form ff in v68Forms) { try { ff.Close(); } catch (Exception dg18835_1) { Diag.Swallow("MainForm.cs:8046", dg18835_1); } }
                DumpTest("_v68_ui_test.txt", sb.ToString());
                Application.Exit();
            };
            tm.Start();
        }



        // ===================== v6.9 新增功能窗口 =====================

        ClipHistStore clipHist;
        PhoneRemoteServer remoteSrv;   // v11.0 手机遥控（局域网网页）
        System.Windows.Forms.Timer clipTimer;
        System.Windows.Forms.Timer trigTimer;      // v12.2 自动化触发器引擎
        DateTime bootTime = DateTime.Now;          // v12.2 开机触发基准
        DateTime recStart = DateTime.Now;
        string recLast = "";
        bool recFocusBefore = false;
        bool recFocusApplied = false;

        void StartClipCapture()
        {
            try
            {
                if (clipHist == null) clipHist = new ClipHistStore(Path.Combine(BASE_DIR, "剪贴板历史.dat"));
                if (clipTimer != null) return;
                clipTimer = new System.Windows.Forms.Timer();
                clipTimer.Interval = 1800;
                clipTimer.Tick += delegate { try { clipHist.Capture(); } catch (Exception dg18862_1) { Diag.Swallow("MainForm.cs:8073", dg18862_1); } };
                clipTimer.Start();
            }
            catch (Exception ex) { Log("[剪贴板历史] 启动失败：" + ex.Message); }
        }

        bool clipDiagOn = false;
        bool keepFormAfterGrab = false;
        void ShowClipHist()
        {
            StartClipCapture();
            Form f = new Form();
            f.Text = T("剪贴板历史 · 小工具箱", "Clipboard History · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 682);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("剪贴板历史", "Clipboard History");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("自动记录复制的文字与图片（文字 200 条 / 图片 30 张，收藏不删）；图片存 剪贴板图片\\ 目录",
                "Records copied text and images (200 texts / 30 images), images saved under 剪贴板图片\\");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(860, 22);
            f.Controls.Add(sub);

            // v11.0：搜索行改成「行首小标题 + 加宽输入框 + 只看收藏 + 收藏计数」，
            // 不再用贴在右侧的提示文字，视线从左往右一条线，控件也不再稀疏。
            Label lbSr = new Label();
            lbSr.Text = T("搜索", "Search");
            lbSr.ForeColor = C_SUB;
            lbSr.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lbSr.Location = new Point(20, 77);
            lbSr.Size = new Size(46, 18);
            f.Controls.Add(lbSr);

            // v11.0：输入框改成「深色底槽 + 无边框文本」的组合，高度可控（单行 TextBox 高度会被字体压成 17px）
            Panel sbSearch = new Panel();
            sbSearch.Location = new Point(68, 70);
            sbSearch.Size = new Size(360, 28);
            sbSearch.BackColor = UiKit.A(28, 33, 41);
            sbSearch.Paint += delegate (object s6, PaintEventArgs e6)
            {
                try
                {
                    e6.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Pen p6 = new Pen(UiKit.A(139, 152, 172), 1.6f))
                    {
                        e6.Graphics.DrawEllipse(p6, 10, 9, 9, 9);
                        e6.Graphics.DrawLine(p6, 18.5f, 17.6f, 23f, 22f);
                    }
                }
                catch (Exception dg18924_1) { Diag.Swallow("MainForm.cs:8135", dg18924_1); }
            };
            f.Controls.Add(sbSearch);

            TextBox tbSearch = new TextBox();
            tbSearch.BorderStyle = BorderStyle.None;
            tbSearch.BackColor = UiKit.A(28, 33, 41);
            tbSearch.ForeColor = UiKit.Txt(Color.White);
            tbSearch.Font = new Font("Microsoft YaHei UI", 9.5F);
            tbSearch.Location = new Point(30, Math.Max(0, (28 - tbSearch.PreferredHeight) / 2));
            tbSearch.Width = 322;
            sbSearch.Controls.Add(tbSearch);

            // v12.8：搜索框加占位提示（原来空框没有任何提示，不知道能输入什么）
            Label phSearch = new Label();
            phSearch.Text = T("输入关键词筛选（内容、时间都行）", "Type to filter…");
            phSearch.ForeColor = UiKit.A(128, 140, 160);
            phSearch.Font = new Font("Microsoft YaHei UI", 9F);
            phSearch.BackColor = Color.Transparent;
            phSearch.AutoSize = false;
            phSearch.TextAlign = ContentAlignment.MiddleLeft;
            phSearch.Location = new Point(30, (28 - 18) / 2);
            phSearch.Size = new Size(308, 18);
            phSearch.Cursor = Cursors.IBeam;
            phSearch.Tag = "placeholder";
            phSearch.Click += delegate { tbSearch.Focus(); };
            tbSearch.TextChanged += delegate { phSearch.Visible = (tbSearch.TextLength == 0); };
            tbSearch.GotFocus += delegate { phSearch.Visible = false; };
            tbSearch.LostFocus += delegate { phSearch.Visible = (tbSearch.TextLength == 0); };
            sbSearch.Controls.Add(phSearch);
            phSearch.BringToFront();

            ListView lv = MakeDarkList(new string[] { T("时间", "Time"), T("类型", "Kind"), T("内容摘要", "Preview"), T("钉住", "Pinned") },
                new int[] { 118, 72, 556, 52 }, 20, 140, 842, 272);
            f.Controls.Add(lv);
            DarkScrollBar.HideHorz(lv);

            Label lp = new Label();
            lp.Text = T("完整内容：", "Full text:");
            lp.ForeColor = UiKit.A(140, 150, 165);
            lp.Location = new Point(20, 428);
            lp.Size = new Size(78, 18);
            f.Controls.Add(lp);

            // v14.0：复制时脱敏（手机号 / 身份证 / 卡号 / 邮箱 / IP），开关落盘
            RoundCheck chkMask = new RoundCheck();
            chkMask.Text = T("复制时脱敏（手机号 / 身份证 / 卡号 / 邮箱 / IP）", "Mask on copy (phone / ID / card / email / IP)");
            chkMask.ForeColor = UiKit.A(186, 196, 210);
            chkMask.Font = new Font("Microsoft YaHei UI", 8.5F);
            chkMask.AutoSize = false;
            chkMask.Location = new Point(104, 426);
            chkMask.Size = new Size(330, 20);
            f.Controls.Add(chkMask);
            string maskFile = Path.Combine(BASE_DIR, "脱敏复制.txt");
            try { chkMask.Checked = File.Exists(maskFile) && File.ReadAllText(maskFile).Trim() == "1"; } catch (Exception dg18978_1) { Diag.Swallow("MainForm.cs:8189", dg18978_1); }
            chkMask.CheckedChanged += delegate
            {
                try { File.WriteAllText(maskFile, chkMask.Checked ? "1" : "0"); } catch (Exception dg18981_1) { Diag.Swallow("MainForm.cs:8192", dg18981_1); }
                Sfx.Click();
            };

            // v12.7：原来只放一个 FixedSingle 的 TextBox，深底深边，看上去像"这块什么都没有"。
            // 改成自绘凹槽（1px 冷色描边 + 内衬底色），一眼能看出这是"完整内容"的框
            Panel pv = new Panel();
            pv.Location = new Point(20, 448);
            pv.Size = new Size(860, 110);
            pv.BackColor = UiKit.A(26, 31, 39);
            pv.Paint += delegate (object s9, PaintEventArgs e9)
            {
                using (Pen p9 = new Pen(UiKit.A(58, 70, 88)))
                    e9.Graphics.DrawRectangle(p9, 0, 0, pv.Width - 1, pv.Height - 1);
            };
            f.Controls.Add(pv);

            TextBox tbPrev = new TextBox();
            tbPrev.Location = new Point(9, 9);
            tbPrev.Size = new Size(842, 92);
            tbPrev.Multiline = true;
            tbPrev.ScrollBars = ScrollBars.Vertical;
            tbPrev.ReadOnly = true;
            tbPrev.BackColor = Ui2.CtlInk(UiKit.A(31, 37, 47));
            tbPrev.ForeColor = UiKit.A(226, 234, 244);
            tbPrev.BorderStyle = BorderStyle.None;
            StylePrevBox(tbPrev);
            pv.Controls.Add(tbPrev);

            // v14.0：图片项在预览凹槽里直接看到图，不再只写一行字
            PictureBox pimg = new PictureBox();
            pimg.Location = new Point(9, 9);
            pimg.Size = new Size(842, 92);
            pimg.SizeMode = PictureBoxSizeMode.Zoom;
            pimg.BackColor = Ui2.CtlInk(UiKit.A(31, 37, 47));
            pimg.Visible = false;
            pv.Controls.Add(pimg);

            Panel sep = new Panel();
            sep.Location = new Point(20, 626);
            sep.Size = new Size(860, 1);
            sep.BackColor = UiKit.A(46, 55, 70);
            f.Controls.Add(sep);

            // v12.8：底部状态行加一枚状态灯（绿=记录中 / 橙=已暂停），一眼看出当前抓不抓
            Label dot = new Label();
            dot.Text = "";
            dot.BackColor = Color.Transparent;
            dot.Location = new Point(20, 637);
            dot.Size = new Size(14, 14);
            dot.Paint += delegate (object s8, PaintEventArgs e8)
            {
                try
                {
                    e8.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush b8 = new SolidBrush(clipHist.Enabled ? UiKit.A(84, 224, 146) : UiKit.A(246, 186, 72)))
                        e8.Graphics.FillEllipse(b8, 1, 1, 11, 11);
                    using (Pen p8 = new Pen(UiKit.A(196, 235, 214), 1f))
                        e8.Graphics.DrawEllipse(p8, 1, 1, 11, 11);
                }
                catch (Exception dg19041_1) { Diag.Swallow("MainForm.cs:8252", dg19041_1); }
            };
            f.Controls.Add(dot);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(40, 636);
            st.Size = new Size(840, 20);
            f.Controls.Add(st);

            // v10.0：只看收藏 —— 用自家 RoundBtn 做成"可切换的胶囊"，比系统勾选框贴这套深色界面
            bool pinOnly = false;
            string kindFilter = "";   // v11.0 类型筛选：空 = 全部
            RoundBtn pinBtn = new RoundBtn(UiKit.A(43, 58, 80), Color.White);
            pinBtn.Text = T("只看收藏", "Pinned only");
            pinBtn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            pinBtn.Location = new Point(444, 72);
            pinBtn.Size = new Size(104, 26);
            f.Controls.Add(pinBtn);

            Label lbCnt = new Label();
            lbCnt.Text = "";
            lbCnt.ForeColor = UiKit.A(250, 190, 90);
            lbCnt.Font = new Font("Microsoft YaHei UI", 8.5F);
            lbCnt.Location = new Point(690, 78);
            lbCnt.Size = new Size(170, 20);
            lbCnt.TextAlign = ContentAlignment.MiddleRight;
            f.Controls.Add(lbCnt);

            Action refresh = delegate
            {
                // v12.7：重建前记住选中的那条（按摘要文本找回），重建期间整表冻结，避免「点一下就白一片」
                string keepSel = "";
                if (lv.SelectedItems.Count > 0) keepSel = lv.SelectedItems[0].SubItems[2].Text;
                bool hadSel = lv.SelectedItems.Count > 0;
                lv.BeginUpdate();
                lv.Items.Clear();
                string kw = tbSearch.Text.Trim();
                for (int i = 0; i < clipHist.Items.Count; i++)
                {
                    ClipHistStore.Entry e = clipHist.Items[i];
                    if (pinOnly && !e.Pinned) continue;
                    string kd = (e.Kind == "i") ? "img" : ClipKind.Of(e.Text);
                    if (kindFilter.Length > 0 && kd != kindFilter) continue;
                    if (kw.Length > 0 && e.Text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    ListViewItem li = new ListViewItem(new string[] { e.Time,
                        kd == "img" ? T("图片", "Image") : ClipKind.Label(kd, langEn),
                        ClipHistStore.Preview(e.Text, 110), e.Pinned ? "★" : "" });
                    li.SubItems[1].ForeColor = (kd == "img") ? UiKit.A(140, 210, 255) : ClipKind.Tint(kd);
                    li.Tag = i;
                    if (e.Pinned) li.ForeColor = UiKit.A(250, 190, 90);
                    lv.Items.Add(li);
                }
                lv.EndUpdate();
                if (hadSel && keepSel.Length > 0)
                {
                    try { lv.SelectedItems.Clear(); } catch (Exception dg19098_1) { Diag.Swallow("MainForm.cs:8309", dg19098_1); }
                    for (int si = 0; si < lv.Items.Count; si++)
                        if (lv.Items[si].SubItems[2].Text == keepSel)
                        { lv.Items[si].Selected = true; lv.Items[si].Focused = true; try { lv.Items[si].EnsureVisible(); } catch (Exception dg19101_1) { Diag.Swallow("MainForm.cs:8312", dg19101_1); } break; }
                }
                try { lv.Invalidate(); } catch (Exception dg19103_1) { Diag.Swallow("MainForm.cs:8314", dg19103_1); }
                int pins = 0;
                for (int i2 = 0; i2 < clipHist.Items.Count; i2++) if (clipHist.Items[i2].Pinned) pins++;
                lbCnt.Text = T("★ 收藏 ", "★ pinned ") + pins + T(" 条", "");
                st.Text = T("共 ", "Total ") + clipHist.Items.Count + T(" 条，记录状态：", " items, capture: ")
                    + (clipHist.Enabled ? T("开启", "on") : T("已暂停", "paused"));
                if (pinOnly || kw.Length > 0 || kindFilter.Length > 0) st.Text = st.Text + T("（筛选出 ", " (showing ") + lv.Items.Count + T(" 条）", ")");
                try { dot.Invalidate(); } catch (Exception dg19110_1) { Diag.Swallow("MainForm.cs:8321", dg19110_1); }
            };

            pinBtn.Click += delegate
            {
                pinOnly = !pinOnly;
                pinBtn.Mark = pinOnly;
                pinBtn.Text = pinOnly ? T("只看收藏 ✓", "Pinned ✓") : T("只看收藏", "Pinned only");
                pinBtn.Invalidate();
                Sfx.Click();
                refresh();
            };

            tbSearch.TextChanged += delegate { refresh(); };
            // v11.0：类型筛选胶囊（全部 / 链接 / 代码 / 路径）+ 一键复制所有链接
            string[] kk = new string[] { "", "link", "code", "img", "path" };
            string[] kz = new string[] { T("全部", "All"), T("链接", "Links"), T("代码", "Code"), T("图片", "Image"), T("路径", "Paths") };
            RoundBtn[] kb = new RoundBtn[kk.Length];
            for (int ki = 0; ki < kk.Length; ki++)
            {
                RoundBtn b = new RoundBtn(UiKit.A(43, 58, 80), Color.White);
                b.Text = kz[ki];
                b.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                b.Location = new Point(20 + ki * 66, 104);
                b.Size = new Size(62, 24);
                b.Mark = (ki == 0);
                f.Controls.Add(b);
                kb[ki] = b;
                string kfv = kk[ki];
                int kix = ki;
                b.Click += delegate
                {
                    kindFilter = kfv;
                    for (int kj = 0; kj < kb.Length; kj++)
                    {
                        if (kb[kj] == null) continue;
                        kb[kj].Mark = (kj == kix);
                        kb[kj].Invalidate();
                    }
                    Sfx.Click();
                    refresh();
                };
            }

            RoundBtn bLinks = new RoundBtn(C_BUBBLE_ALT(), Color.White);
            bLinks.Text = T("复制全部链接", "Copy all links");
            bLinks.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            bLinks.Location = new Point(700, 104);
            bLinks.Size = new Size(120, 24);
            f.Controls.Add(bLinks);
            bLinks.Click += delegate
            {
                List<string> lk = new List<string>();
                for (int i = 0; i < clipHist.Items.Count; i++)
                {
                    string tx = clipHist.Items[i].Text;
                    if (ClipKind.Of(tx) != "link") continue;
                    if (!lk.Contains(tx)) lk.Add(tx);
                }
                if (lk.Count == 0) { st.Text = T("没有识别到链接。", "No links found."); Sfx.Click(); return; }
                try
                {
                    Clipboard.SetText(string.Join("\r\n", lk.ToArray()));
                    st.Text = T("已复制 " + lk.Count + " 条链接。", "Copied " + lk.Count + " links.");
                    Sfx.Copy();
                }
                catch (Exception exl) { st.Text = T("复制失败：" + exl.Message, "Failed: " + exl.Message); }
            };


            Action<ClipHistStore.Entry> copyEntry = null;   // v14.0：文本 / 图片共用一条复制路径
            lv.DoubleClick += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int idx2 = Convert.ToInt32(lv.SelectedItems[0].Tag);
                if (idx2 >= 0 && idx2 < clipHist.Items.Count && copyEntry != null) copyEntry(clipHist.Items[idx2]);
            };
            f.KeyPreview = true;
            f.KeyDown += delegate (object s7, KeyEventArgs e7)
            {
                if (e7.Control && e7.KeyCode == Keys.F) { tbSearch.Focus(); tbSearch.SelectAll(); e7.SuppressKeyPress = true; }
            };

            if (clipDiagOn)
            {
                StringBuilder rep = new StringBuilder();
                System.Windows.Forms.Timer dt = new System.Windows.Forms.Timer();
                dt.Interval = 1000;
                int step = 0;
                dt.Tick += delegate
                {
                    dt.Stop();
                    step++;
                    rep.AppendLine("--- step " + step + " ---");
                    rep.AppendLine("items=" + lv.Items.Count + " sel=" + lv.SelectedItems.Count + " prev_len=" + tbPrev.TextLength + " w=" + lv.Width + " h=" + lv.Height);
                    for (int q = 0; q < Math.Min(8, lv.Items.Count); q++)
                    {
                        StringBuilder row = new StringBuilder();
                        row.Append("row").Append(q).Append(" subs=").Append(lv.Items[q].SubItems.Count);
                        for (int sq = 0; sq < lv.Items[q].SubItems.Count; sq++)
                            row.Append(" |").Append(sq).Append("=").Append(lv.Items[q].SubItems[sq].Text);
                        rep.AppendLine(row.ToString());
                    }
                    GrabAnyForm("_clip_s" + step + ".png", "剪贴板历史", rep);
                    if (step == 1) { tbSearch.Text = "开"; dt.Start(); return; }
                    if (step == 2) { tbSearch.Text = "开始完成整体"; dt.Start(); return; }
                    if (step == 3) { tbSearch.Text = ""; dt.Start(); return; }
                    if (step == 4) { if (lv.Items.Count > 0) { lv.Items[0].Selected = true; lv.Items[0].Focused = true; } dt.Start(); return; }
                    if (step == 5) { if (lv.Items.Count > 1) { lv.Items[1].Selected = true; lv.Items[1].Focused = true; } dt.Start(); return; }
                    try { File.WriteAllText(Path.Combine(BASE_DIR, "_clipdbg.txt"), rep.ToString(), new UTF8Encoding(false)); } catch (Exception dg19219_1) { Diag.Swallow("MainForm.cs:8430", dg19219_1); }
                    Application.Exit();
                };
                dt.Start();
            }




            // v14.0 自检：/imgtest 在程序内自己放一张图到剪贴板，验证 捕获→入库→列表→预览→复制回 整条链路
            if (Environment.GetCommandLineArgs().Length > 1 && Array.IndexOf(Environment.GetCommandLineArgs(), "/imgtest") >= 0)
            {
                System.Windows.Forms.Timer t2 = new System.Windows.Forms.Timer();
                t2.Interval = 900;
                int step2 = 0;
                StringBuilder rep2 = new StringBuilder();
                t2.Tick += delegate
                {
                    t2.Stop();
                    step2++;
                    try
                    {
                        if (step2 == 1)
                        {
                            Bitmap tb2 = new Bitmap(640, 360);
                            using (Graphics g2 = Graphics.FromImage(tb2))
                            {
                                using (System.Drawing.Drawing2D.LinearGradientBrush lb2 = new System.Drawing.Drawing2D.LinearGradientBrush(
                                    new Rectangle(0, 0, 640, 360), UiKit.A(20, 90, 150), UiKit.A(120, 40, 140), 35f))
                                    g2.FillRectangle(lb2, 0, 0, 640, 360);
                                g2.SmoothingMode = SmoothingMode.AntiAlias;
                                using (Font f2 = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold))
                                using (SolidBrush b2 = new SolidBrush(UiKit.Txt(Color.White)))
                                    g2.DrawString(T("图片剪贴板测试", "Image clipboard test"), f2, b2, 160, 150);
                            }
                            Clipboard.SetImage(tb2);
                            tb2.Dispose();
                            rep2.AppendLine("step1 setimage ok contains=" + Clipboard.ContainsImage());
                            t2.Start();
                            return;
                        }
                        if (step2 == 2)
                        {
                            rep2.AppendLine("step2 items=" + clipHist.Items.Count);
                            if (clipHist.Items.Count > 0)
                                rep2.AppendLine("  first kind=" + clipHist.Items[0].Kind + " text=" + clipHist.Items[0].Text
                                    + " img=" + clipHist.Items[0].Img + " exists=" + File.Exists(clipHist.ImgFull(clipHist.Items[0])));
                            refresh();
                            if (lv.Items.Count > 0) { lv.Items[0].Selected = true; lv.Items[0].Focused = true; }
                            t2.Start();
                            return;
                        }
                        if (step2 == 3)
                        {
                            rep2.AppendLine("step3 lvrows=" + lv.Items.Count + " preview_visible=" + pimg.Visible
                                + " preview_has_image=" + (pimg.Image != null));
                            GrabAnyForm("_clipimg.png", "剪贴板历史", rep2);
                            if (lv.Items.Count > 0 && copyEntry != null)
                            {
                                int i3 = Convert.ToInt32(lv.Items[0].Tag);
                                copyEntry(clipHist.Items[i3]);
                                rep2.AppendLine("step3 copied_back contains_image=" + Clipboard.ContainsImage());
                            }
                            File.WriteAllText(Path.Combine(BASE_DIR, "_clipimg.txt"), rep2.ToString(), new UTF8Encoding(false));
                            Application.Exit();
                            return;
                        }
                    }
                    catch (Exception ex9) { rep2.AppendLine("ERR " + ex9.Message); }
                    File.WriteAllText(Path.Combine(BASE_DIR, "_clipimg.txt"), rep2.ToString(), new UTF8Encoding(false));
                    t2.Start();
                };
                t2.Start();
            }

            lv.SelectedIndexChanged += delegate
            {
                // v12.7：列表重建会把选中清空，这一瞬间不要跟着把下面的完整内容抹掉
                if (lv.SelectedItems.Count == 0) return;
                int idx = Convert.ToInt32(lv.SelectedItems[0].Tag);
                if (idx < 0 || idx >= clipHist.Items.Count) return;
                ClipHistStore.Entry se = clipHist.Items[idx];
                if (se.Kind == "i")
                {
                    string sf = clipHist.ImgFull(se);
                    Image old = pimg.Image;
                    pimg.Image = null;
                    try { if (old != null) old.Dispose(); } catch (Exception dg19306_1) { Diag.Swallow("MainForm.cs:8517", dg19306_1); }
                    try
                    {
                        if (File.Exists(sf))
                            using (FileStream fs2 = new FileStream(sf, FileMode.Open, FileAccess.Read))
                            using (Image im2 = Image.FromStream(fs2))
                                pimg.Image = new Bitmap(im2);
                    }
                    catch (Exception dg19314_1) { Diag.Swallow("MainForm.cs:8525", dg19314_1); }
                    pimg.Visible = true;
                    tbPrev.Visible = false;
                    tbPrev.Text = se.Text;
                }
                else
                {
                    pimg.Visible = false;
                    tbPrev.Visible = true;
                    tbPrev.Text = se.Text;
                }
            };

            Action<ClipHistStore.Entry> copyIt = delegate (ClipHistStore.Entry e)
            {
                if (e.Kind == "i")
                {
                    string f2 = clipHist.ImgFull(e);
                    if (!File.Exists(f2)) { st.Text = T("图片文件已丢失（可能被清理）", "Image file is gone"); return; }
                    try
                    {
                        using (FileStream fs3 = new FileStream(f2, FileMode.Open, FileAccess.Read))
                        using (Image im3 = Image.FromStream(fs3))
                            Clipboard.SetImage(new Bitmap(im3));
                        st.Text = T("已复制图片到剪贴板", "Image copied");
                        Sfx.Copy();
                    }
                    catch (Exception ex2) { st.Text = ex2.Message; }
                    return;
                }
                try
                {
                    string outText = e.Text;
                    int hits = 0;
                    if (chkMask.Checked) { outText = ClipMask.Text(e.Text); hits = ClipMask.Hits(e.Text); }
                    Clipboard.SetText(outText);
                    st.Text = hits > 0
                        ? T("已复制（脱敏 " + hits + " 处）", "Copied (" + hits + " masked)")
                        : T("已复制到剪贴板", "Copied to clipboard");
                    if (chkMask.Checked) { tbPrev.Text = outText; }
                    Sfx.Copy();
                }
                catch (Exception ex) { st.Text = ex.Message; }
            };
            copyEntry = copyIt;

            Button bCopy = MakeBtn(T("复制", "Copy"), UiKit.A(14, 165, 233), Color.White, 20, 576, 100, 34);
            bCopy.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) { st.Text = T("先选一条", "Select an item"); return; }
                int idx = Convert.ToInt32(lv.SelectedItems[0].Tag);
                if (idx >= 0 && idx < clipHist.Items.Count) copyIt(clipHist.Items[idx]);
            };
            f.Controls.Add(bCopy);

            Button bPin = MakeBtn(T("钉住", "Pin"), C_BUBBLE_ALT(), Color.White, 130, 576, 100, 34);
            bPin.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int idx = Convert.ToInt32(lv.SelectedItems[0].Tag);
                if (idx >= 0 && idx < clipHist.Items.Count)
                {
                    clipHist.Items[idx].Pinned = !clipHist.Items[idx].Pinned;
                    clipHist.Save();
                    refresh();
                }
            };
            f.Controls.Add(bPin);

            Button bDel = MakeBtn(T("删除", "Delete"), C_BUBBLE_ALT(), Color.White, 240, 576, 100, 34);
            bDel.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int idx = Convert.ToInt32(lv.SelectedItems[0].Tag);
                if (idx >= 0 && idx < clipHist.Items.Count)
                {
                    string df = clipHist.ImgFull(clipHist.Items[idx]);
                    if (clipHist.Items[idx].Kind == "i" && df.Length > 0) { try { if (File.Exists(df)) File.Delete(df); } catch (Exception dg19391_1) { Diag.Swallow("MainForm.cs:8602", dg19391_1); } }
                    clipHist.Items.RemoveAt(idx);
                    clipHist.Save();
                    tbPrev.Text = "";
                    pimg.Visible = false;
                    try { if (pimg.Image != null) { pimg.Image.Dispose(); pimg.Image = null; } } catch (Exception dg19396_1) { Diag.Swallow("MainForm.cs:8607", dg19396_1); }
                    refresh();
                }
            };
            f.Controls.Add(bDel);

            Button bClear = MakeBtn(T("清空未钉住", "Clear unpinned"), C_BUBBLE_ALT(), Color.White, 350, 576, 130, 34);
            bClear.Click += delegate
            {
                if (MessageBox.Show(f, T("清空所有没钉住的记录？", "Clear all unpinned entries?"), "小工具箱",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                clipHist.ClearUnpinned();
                tbPrev.Text = "";
                refresh();
            };
            f.Controls.Add(bClear);

            Button bPause = MakeBtn(clipHist.Enabled ? T("暂停记录", "Pause") : T("继续记录", "Resume"), UiKit.A(176, 80, 16), Color.White, 490, 576, 120, 34);
            bPause.Click += delegate
            {
                clipHist.Enabled = !clipHist.Enabled;
                bPause.Text = clipHist.Enabled ? T("暂停记录", "Pause") : T("继续记录", "Resume");
                refresh();
            };
            f.Controls.Add(bPause);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 576, 100, 34);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            f.Shown += delegate { refresh(); };
            ShowOrTrack(f);
        }

        string RecordDir()
        {
            string d = Path.Combine(BASE_DIR, "录屏");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg19433_1) { Diag.Swallow("MainForm.cs:8644", dg19433_1); }
            return d;
        }

        void ToggleRecord()
        {
            if (RecorderCore.Running)
            {
                string err = "";
                RecorderCore.Stop(out err);
                if (recFocusApplied) { try { Sfx.SetFocus(recFocusBefore); } catch { } recFocusApplied = false; }
                string file = RecorderCore.LastFile;
                Log("[录屏] 已停止 " + file + (err.Length > 0 ? "（" + err + "）" : ""));
                try { if (trayIcon != null && trayIcon.Visible) trayIcon.ShowBalloonTip(2500, T("录屏已停止", "Recording stopped"), file, ToolTipIcon.Info); } catch (Exception dg19445_1) { Diag.Swallow("MainForm.cs:8656", dg19445_1); }
                return;
            }
            string ff = WmFfmpegPath();
            if (!File.Exists(ff))
            {
                MessageBox.Show(this, T("没找到 ffmpeg.exe，请先在「视频下载」页点一次「部署 FFmpeg」。", "ffmpeg.exe not found. Deploy FFmpeg on the Video page first."),
                    "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Rectangle scr = SystemInformation.VirtualScreen;
            string nfile = Path.Combine(RecordDir(), "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
            string nerr = "";
            bool ok = RecorderCore.Start(WmShortPathOr(ff), scr.Left, scr.Top, scr.Width, scr.Height, 15, WmShortPathOr(nfile), out nerr);
            if (ok)
            {
                recLast = nfile;
                recStart = DateTime.Now;
                recFocusBefore = Sfx.Focus;
                Sfx.SetFocus(true);
                recFocusApplied = true;
                Log("[录屏] 开始录制 " + nfile);
                try { if (trayIcon != null && trayIcon.Visible) trayIcon.ShowBalloonTip(2000, T("开始录屏", "Recording"), T("按 Ctrl+Alt+R 或再点一次热键停止", "Press Ctrl+Alt+R again to stop"), ToolTipIcon.Info); } catch (Exception dg19464_1) { Diag.Swallow("MainForm.cs:8675", dg19464_1); }
            }
            else Log("[录屏] 启动失败：" + nerr);
        }

        // ffmpeg 对中文路径不友好：能取到短路径就用短路径，否则原样传
        string WmShortPathOr(string p)
        {
            try
            {
                string s = WmShortPath(p);
                if (s != null && s.Length > 0) return s;
            }
            catch (Exception dg19477_1) { Diag.Swallow("MainForm.cs:8688", dg19477_1); }
            return p;
        }

        void ShowRecorder()
        {
            Form f = new Form();
            f.Text = T("录屏 / GIF 工坊 · 小工具箱", "Screen Recorder / GIF · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 660);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("录屏 / GIF 工坊", "Screen Recorder / GIF");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("用内嵌 FFmpeg 抓屏录制 MP4，录完可一键压成 GIF；也能用 Ctrl+Alt+R 边玩边录。",
                "Capture the screen to MP4 with bundled FFmpeg and turn it into a GIF; Ctrl+Alt+R works in-game.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(860, 22);
            f.Controls.Add(sub);

            CheckBox ckAll = new RoundCheck();
            ckAll.Text = T("全屏录制（不勾选则按下面区域）", "Full screen (or use the region below)");
            ckAll.ForeColor = UiKit.A(220, 230, 240);
            ckAll.Location = new Point(20, 84);
            ckAll.Size = new Size(320, 22);
            ckAll.Checked = true;
            f.Controls.Add(ckAll);

            Label lf = new Label();
            lf.Text = T("帧率", "FPS");
            lf.ForeColor = UiKit.A(180, 190, 205);
            lf.Location = new Point(620, 86);
            lf.Size = new Size(40, 18);
            f.Controls.Add(lf);

            ComboBox cbFps = new ComboBox();
            cbFps.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFps.Location = new Point(662, 82);
            cbFps.Size = new Size(80, 24);
            cbFps.BackColor = UiKit.A(28, 33, 41);
            cbFps.ForeColor = UiKit.Txt(Color.White);
            cbFps.Items.AddRange(new object[] { "10", "15", "20", "30", "60" });
            cbFps.SelectedIndex = 1;
            f.Controls.Add(cbFps);

            string[] xywh = new string[] { "X", "Y", "宽", "高" };
            TextBox[] tbXYWH = new TextBox[4];
            for (int i = 0; i < 4; i++)
            {
                Label lb = new Label();
                lb.Text = xywh[i];
                lb.ForeColor = UiKit.A(160, 170, 185);
                lb.Location = new Point(20 + i * 110, 118);
                lb.Size = new Size(60, 18);
                f.Controls.Add(lb);

                TextBox tb = new TextBox();
                tb.Location = new Point(20 + i * 110, 138);
                tb.Size = new Size(90, 24);
                tb.BackColor = UiKit.A(28, 33, 41);
                tb.ForeColor = UiKit.Txt(Color.White);
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.Text = i == 2 ? "1280" : (i == 3 ? "720" : "0");
                tbXYWH[i] = tb;
                f.Controls.Add(tb);
            }

            Label lo = new Label();
            lo.Text = T("输出目录", "Output folder");
            lo.ForeColor = UiKit.A(160, 170, 185);
            lo.Location = new Point(480, 118);
            lo.Size = new Size(120, 18);
            f.Controls.Add(lo);

            TextBox tbOut = new TextBox();
            tbOut.Location = new Point(480, 138);
            tbOut.Size = new Size(300, 24);
            tbOut.BackColor = UiKit.A(28, 33, 41);
            tbOut.ForeColor = UiKit.Txt(Color.White);
            tbOut.BorderStyle = BorderStyle.FixedSingle;
            tbOut.Text = RecordDir();
            f.Controls.Add(tbOut);

            Button bPick = MakeBtn(T("选择目录", "Browse"), C_BUBBLE_ALT(), Color.White, 790, 136, 90, 28);
            bPick.Click += delegate
            {
                FolderBrowserDialog fd = new FolderBrowserDialog();
                fd.SelectedPath = tbOut.Text;
                if (fd.ShowDialog(f) == DialogResult.OK) tbOut.Text = fd.SelectedPath;
            };
            f.Controls.Add(bPick);

            TextBox tbLog = new TextBox();
            tbLog.Location = new Point(20, 344);
            tbLog.Size = new Size(860, 262);
            tbLog.Multiline = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.ReadOnly = true;
            tbLog.BackColor = UiKit.A(24, 29, 36);
            tbLog.ForeColor = UiKit.A(210, 220, 232);
            tbLog.BorderStyle = BorderStyle.FixedSingle;
            f.Controls.Add(tbLog);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 616);
            st.Size = new Size(860, 20);
            f.Controls.Add(st);

            Action<string> lg = delegate (string s)
            {
                tbLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\r\n");
            };

            System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate
            {
                if (!RecorderCore.Running) { tick.Stop(); return; }
                TimeSpan ts = DateTime.Now - recStart;
                st.Text = T("录制中 ", "Recording ") + ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00")
                    + T(" · 再点「停止并保存」结束", " · press Stop to finish");
                st.ForeColor = UiKit.A(235, 120, 100);
            };

            Button bStart = MakeBtn(T("开始录制", "Start"), UiKit.A(232, 82, 61), Color.White, 20, 254, 130, 34);
            bStart.Click += delegate
            {
                if (RecorderCore.Running) { lg(T("已经在录制中", "Already recording")); return; }
                string ff = WmFfmpegPath();
                if (!File.Exists(ff)) { lg(T("缺少 ffmpeg.exe，请先在「视频下载」页部署", "ffmpeg.exe missing — deploy it on the Video page")); return; }
                string od = tbOut.Text.Trim();
                if (od.Length == 0) od = RecordDir();
                try { if (!Directory.Exists(od)) Directory.CreateDirectory(od); } catch (Exception dg19622_1) { Diag.Swallow("MainForm.cs:8833", dg19622_1); }
                string file = Path.Combine(od, "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
                int x = 0, y = 0, w = 0, h = 0;
                try
                {
                    x = Convert.ToInt32(tbXYWH[0].Text.Trim());
                    y = Convert.ToInt32(tbXYWH[1].Text.Trim());
                    w = Convert.ToInt32(tbXYWH[2].Text.Trim());
                    h = Convert.ToInt32(tbXYWH[3].Text.Trim());
                }
                catch (Exception dg19632_1) { Diag.Swallow("MainForm.cs:8843", dg19632_1); }
                if (ckAll.Checked)
                {
                    Rectangle scr = SystemInformation.VirtualScreen;
                    x = scr.Left; y = scr.Top; w = scr.Width; h = scr.Height;
                }
                int fps = 15;
                try { fps = Convert.ToInt32(cbFps.SelectedItem); } catch (Exception dg19639_1) { Diag.Swallow("MainForm.cs:8850", dg19639_1); }
                string err = "";
                string pff = WmShortPath(ff); if (pff.Length == 0) pff = ff;
                string pf = WmShortPathOr(file);
                if (RecorderCore.Start(pff, x, y, w, h, fps, pf, out err))
                {
                    recLast = file;
                    recStart = DateTime.Now;
                    lg(T("开始录制：", "Recording: ") + file + "  " + w + "x" + h + " @" + fps + "fps");
                    st.ForeColor = UiKit.A(235, 120, 100);
                    tick.Start();
                }
                else lg(T("启动失败：", "Failed: ") + err);
            };
            f.Controls.Add(bStart);

            Button bStop = MakeBtn(T("停止并保存", "Stop"), UiKit.A(59, 130, 246), Color.White, 160, 254, 130, 34);
            bStop.Click += delegate
            {
                string err = "";
                if (!RecorderCore.Stop(out err)) { lg(T("没有在录制", "Not recording")); return; }
                tick.Stop();
                st.Text = T("已保存：", "Saved: ") + recLast + (err.Length > 0 ? "（" + err + "）" : "");
                st.ForeColor = UiKit.A(72, 208, 130);
                lg(T("已停止，文件：", "Stopped: ") + recLast + (err.Length > 0 ? "  " + err : ""));
                try
                {
                    if (File.Exists(recLast))
                        lg(T("文件大小：", "Size: ") + FmtSize(new FileInfo(recLast).Length));
                }
                catch (Exception dg19669_1) { Diag.Swallow("MainForm.cs:8880", dg19669_1); }
            };
            f.Controls.Add(bStop);

            Button bGif = MakeBtn(T("转成 GIF", "Make GIF"), UiKit.A(16, 185, 129), Color.White, 300, 254, 120, 34);
            bGif.Click += delegate
            {
                string srcFile = recLast;
                if (srcFile == null || srcFile.Length == 0 || !File.Exists(srcFile))
                {
                    OpenFileDialog od = new OpenFileDialog();
                    od.Filter = "MP4 视频|*.mp4";
                    try { od.InitialDirectory = RecordDir(); } catch (Exception dg19681_1) { Diag.Swallow("MainForm.cs:8892", dg19681_1); }
                    if (od.ShowDialog(f) != DialogResult.OK) return;
                    srcFile = od.FileName;
                }
                string gif = Path.Combine(Path.GetDirectoryName(srcFile), Path.GetFileNameWithoutExtension(srcFile) + ".gif");
                int gifFps = 10;
                int width = 640;
                lg(T("开始转 GIF…", "Converting to GIF…"));
                Application.DoEvents();
                string err = "";
                string pff = WmShortPath(WmFfmpegPath());
                if (pff.Length == 0) pff = WmFfmpegPath();
                if (RecorderCore.ToGif(pff, WmShortPathOr(srcFile), WmShortPathOr(gif), gifFps, width, out err))
                {
                    lg(T("GIF 已生成：", "GIF created: ") + gif);
                    st.Text = T("GIF：", "GIF: ") + gif;
                    st.ForeColor = UiKit.A(72, 208, 130);
                }
                else lg(T("转 GIF 失败：", "GIF failed: ") + err);
            };
            f.Controls.Add(bGif);

            Button bOpen = MakeBtn(T("打开录屏目录", "Open folder"), C_BUBBLE_ALT(), Color.White, 430, 254, 140, 34);
            bOpen.Click += delegate { try { Process.Start("explorer.exe", "\"" + RecordDir() + "\""); } catch (Exception dg19704_1) { Diag.Swallow("MainForm.cs:8915", dg19704_1); } };
            f.Controls.Add(bOpen);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 254, 100, 34);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            f.FormClosing += delegate
            {
                if (RecorderCore.Running)
                {
                    string err = "";
                    RecorderCore.Stop(out err);
                    lg(T("窗口关闭前已停止录制：", "Stopped on close: ") + recLast);
                }
            };

            ShowOrTrack(f);
        }

        string BackupDir()
        {
            string d = Path.Combine(BASE_DIR, "备份");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg19727_1) { Diag.Swallow("MainForm.cs:8938", dg19727_1); }
            return d;
        }

        void ShowBackup()
        {
            Form f = new Form();
            f.Text = T("配置备份 / 还原 · 小工具箱", "Backup & Restore · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 600);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("配置数据一键备份 / 还原", "Config Backup & Restore");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("把热键、加速备份、剪贴板历史、整理/查重记录打包成 zip；换电脑时解回来即可。",
                "Packs hotkeys, vault, boost backup, clipboard history and records into a zip; restore on a new PC.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(860, 22);
            f.Controls.Add(sub);

            ListView lv = MakeDarkList(new string[] { T("项目", "Item"), T("大小", "Size"), T("类型", "Type") },
                new int[] { 380, 160, 140 }, 20, 78, 860, 240);
            f.Controls.Add(lv);

            Label ld = new Label();
            ld.Text = T("备份存放目录", "Backup folder");
            ld.ForeColor = UiKit.A(160, 170, 185);
            ld.Location = new Point(20, 330);
            ld.Size = new Size(200, 18);
            f.Controls.Add(ld);

            TextBox tbDir = new TextBox();
            tbDir.Location = new Point(20, 350);
            tbDir.Size = new Size(660, 24);
            tbDir.BackColor = UiKit.A(28, 33, 41);
            tbDir.ForeColor = UiKit.Txt(Color.White);
            tbDir.BorderStyle = BorderStyle.FixedSingle;
            tbDir.Text = BackupDir();
            f.Controls.Add(tbDir);

            Button bPick = MakeBtn(T("选择目录", "Browse"), C_BUBBLE_ALT(), Color.White, 690, 348, 90, 28);
            bPick.Click += delegate
            {
                FolderBrowserDialog fd = new FolderBrowserDialog();
                fd.SelectedPath = tbDir.Text;
                if (fd.ShowDialog(f) == DialogResult.OK) tbDir.Text = fd.SelectedPath;
            };
            f.Controls.Add(bPick);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 500);
            st.Size = new Size(860, 60);
            f.Controls.Add(st);

            Action refresh = delegate
            {
                lv.Items.Clear();
                string probeDir = Path.Combine(BASE_DIR, "整理记录");
                string probeFile = Path.Combine(probeDir, "_bk_probe.json");
                bool mkProbe = !Directory.Exists(probeDir);
                try { if (!Directory.Exists(probeDir)) Directory.CreateDirectory(probeDir); File.WriteAllText(probeFile, "{}"); } catch (Exception dg19800_1) { Diag.Swallow("MainForm.cs:9011", dg19800_1); }
                List<BackupCore.Item> items = BackupCore.Collect(BASE_DIR);
                long total = 0;
                foreach (BackupCore.Item it in items)
                {
                    lv.Items.Add(new ListViewItem(new string[] { it.Name, FmtSize(it.Size), it.IsDir ? T("目录", "Folder") : T("文件", "File") }));
                    total += it.Size;
                }
                st.Text = T("共 ", "Total ") + items.Count + T(" 项，约 ", " items, ~") + FmtSize(total)
                    + T("；已有备份 ", " backups: ") + BackupCore.ListBackups(tbDir.Text.Trim()).Count + T(" 个", "");
            };

            Button bBk = MakeBtn(T("立即备份", "Backup now"), UiKit.A(16, 185, 129), Color.White, 20, 450, 130, 34);
            bBk.Click += delegate
            {
                string dir = tbDir.Text.Trim();
                if (dir.Length == 0) dir = BackupDir();
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch (Exception dg19817_1) { Diag.Swallow("MainForm.cs:9028", dg19817_1); }
                string zip = Path.Combine(dir, "备份_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
                string err = "";
                string probeDir = Path.Combine(BASE_DIR, "整理记录");
                string probeFile = Path.Combine(probeDir, "_bk_probe.json");
                bool mkProbe = !Directory.Exists(probeDir);
                try { if (!Directory.Exists(probeDir)) Directory.CreateDirectory(probeDir); File.WriteAllText(probeFile, "{}"); } catch (Exception dg19823_1) { Diag.Swallow("MainForm.cs:9034", dg19823_1); }
                List<BackupCore.Item> items = BackupCore.Collect(BASE_DIR);
                if (items.Count == 0) { st.Text = T("没有可备份的数据", "Nothing to back up"); return; }
                if (BackupCore.Create(zip, items, out err))
                {
                    long sz = 0;
                    try { sz = new FileInfo(zip).Length; } catch (Exception dg19829_1) { Diag.Swallow("MainForm.cs:9040", dg19829_1); }
                    st.ForeColor = UiKit.A(72, 208, 130);
                    st.Text = T("备份完成：", "Backed up: ") + zip + "  (" + FmtSize(sz) + ")";
                    refresh();
                }
                else { st.ForeColor = UiKit.A(255, 120, 120); st.Text = T("备份失败：", "Backup failed: ") + err; }
            };
            f.Controls.Add(bBk);

            Button bRs = MakeBtn(T("从备份还原", "Restore"), UiKit.A(249, 115, 22), Color.White, 160, 450, 140, 34);
            bRs.Click += delegate
            {
                OpenFileDialog od = new OpenFileDialog();
                od.Filter = "备份包|*.zip";
                try { od.InitialDirectory = tbDir.Text.Trim(); } catch (Exception dg19843_1) { Diag.Swallow("MainForm.cs:9054", dg19843_1); }
                if (od.ShowDialog(f) != DialogResult.OK) return;
                if (MessageBox.Show(f, T("还原会用备份里的内容覆盖当前配置（旧的会自动留 .bak 副本）。继续？",
                    "Restore overwrites current config (old files are kept as .bak). Continue?"),
                    "小工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                string err = "";
                string report = "";
                if (BackupCore.Restore(od.FileName, BASE_DIR, out err, out report))
                {
                    st.ForeColor = UiKit.A(72, 208, 130);
                    st.Text = T("还原完成：", "Restore done: ") + "\r\n" + report;
                    refresh();
                }
                else { st.ForeColor = UiKit.A(255, 120, 120); st.Text = T("还原失败：", "Restore failed: ") + err; }
            };
            f.Controls.Add(bRs);

            Button bOpen = MakeBtn(T("打开备份目录", "Open folder"), C_BUBBLE_ALT(), Color.White, 310, 450, 140, 34);
            bOpen.Click += delegate
            {
                string dir = tbDir.Text.Trim();
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); } catch (Exception dg19864_1) { Diag.Swallow("MainForm.cs:9075", dg19864_1); }
            };
            f.Controls.Add(bOpen);

            Button bClean = MakeBtn(T("清理旧备份", "Clean old"), C_BUBBLE_ALT(), Color.White, 460, 450, 130, 34);
            bClean.Click += delegate
            {
                List<string> list = BackupCore.ListBackups(tbDir.Text.Trim());
                if (list.Count <= 5) { st.Text = T("备份不足 5 个，无需清理", "5 or fewer backups, nothing to clean"); return; }
                int n = 0;
                for (int i = 5; i < list.Count; i++)
                {
                    try { File.Delete(list[i]); n++; } catch (Exception dg19876_1) { Diag.Swallow("MainForm.cs:9087", dg19876_1); }
                }
                st.ForeColor = UiKit.A(72, 208, 130);
                st.Text = T("已删除 ", "Deleted ") + n + T(" 个旧备份（保留最近 5 个）", " old backups (kept latest 5)");
                refresh();
            };
            f.Controls.Add(bClean);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 450, 100, 34);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            f.Shown += delegate { refresh(); };
            ShowOrTrack(f);
        }

        void ShowFileCrypto()
        {
            Form f = new Form();
            f.Text = T("单文件加密 / 解密 · 小工具箱", "File Encrypt / Decrypt · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 600);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("单文件加密 / 解密", "File Encrypt / Decrypt");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("AES-256 加密成 .mtbx（文件名也一起存进去），换个盘或发给别人只要密码就能解回来。",
                "AES-256 into .mtbx (original name embedded); decrypt anywhere with the password.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(860, 22);
            f.Controls.Add(sub);

            ListView lv = MakeDarkList(new string[] { T("文件", "File"), T("大小", "Size"), T("状态", "State") },
                new int[] { 560, 130, 140 }, 20, 78, 860, 240);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = "";
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 566);
            st.Size = new Size(860, 24);
            f.Controls.Add(st);

            Action<string> log = delegate (string s)
            {
                st.Text = s;
                st.ForeColor = UiKit.A(180, 195, 210);
            };

            Action<Color, string> ok = delegate (Color c, string s) { st.ForeColor = c; st.Text = s; };

            Button bAdd = MakeBtn(T("添加文件", "Add files"), UiKit.A(59, 130, 246), Color.White, 20, 330, 120, 34);
            bAdd.Click += delegate
            {
                OpenFileDialog od = new OpenFileDialog();
                od.Multiselect = true;
                od.Filter = "所有文件|*.*";
                if (od.ShowDialog(f) != DialogResult.OK) return;
                foreach (string p in od.FileNames)
                {
                    long sz = 0;
                    try { sz = new FileInfo(p).Length; } catch (Exception dg19948_1) { Diag.Swallow("MainForm.cs:9159", dg19948_1); }
                    ListViewItem li = new ListViewItem(new string[] { p, FmtSize(sz), p.EndsWith(".mtbx", StringComparison.OrdinalIgnoreCase) ? T("待解密", "to decrypt") : T("待加密", "to encrypt") });
                    li.Tag = p;
                    lv.Items.Add(li);
                }
                log(T("已添加 ", "Added ") + od.FileNames.Length + T(" 个文件", " files"));
            };
            f.Controls.Add(bAdd);

            Button bEnc = MakeBtn(T("加密", "Encrypt"), UiKit.A(16, 185, 129), Color.White, 150, 330, 100, 34);
            bEnc.Click += delegate
            {
                if (lv.Items.Count == 0) { log(T("先添加文件", "Add files first")); return; }
                string pwd = Microsoft.VisualBasic.Interaction.InputBox(T("设置一个密码（至少 4 位）：", "Password (min 4 chars):"), T("单文件加密", "Encrypt"), "");
                if (pwd == null || pwd.Length < 4) { ok(UiKit.A(255, 120, 120), T("密码太短，已取消", "Password too short")); return; }
                int done = 0;
                foreach (ListViewItem li in lv.Items)
                {
                    string p = Convert.ToString(li.Tag);
                    if (p.EndsWith(".mtbx", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(p)) { li.SubItems[2].Text = T("不存在", "missing"); continue; }
                    string dst = p + ".mtbx";
                    string err = "";
                    if (FileCryptoCore.Encrypt(p, dst, pwd, out err))
                    {
                        li.SubItems[2].Text = T("已加密", "encrypted");
                        li.ForeColor = UiKit.A(72, 208, 130);
                        done++;
                    }
                    else li.SubItems[2].Text = T("失败", "failed");
                }
                ok(UiKit.A(72, 208, 130), T("完成：加密 ", "Encrypted ") + done + T(" 个文件（原文件保留，可自行删除）", " files (originals kept)"));
            };
            f.Controls.Add(bEnc);

            Button bDec = MakeBtn(T("解密", "Decrypt"), UiKit.A(249, 115, 22), Color.White, 260, 330, 100, 34);
            bDec.Click += delegate
            {
                if (lv.Items.Count == 0) { log(T("先添加 .mtbx 文件", "Add .mtbx files first")); return; }
                string pwd = Microsoft.VisualBasic.Interaction.InputBox(T("输入解密密码：", "Password:"), T("单文件解密", "Decrypt"), "");
                if (pwd == null || pwd.Length == 0) return;
                int done = 0;
                foreach (ListViewItem li in lv.Items)
                {
                    string p = Convert.ToString(li.Tag);
                    if (!p.EndsWith(".mtbx", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(p)) { li.SubItems[2].Text = T("不存在", "missing"); continue; }
                    string name = FileCryptoCore.OriginalName(p);
                    if (name.Length == 0) name = Path.GetFileNameWithoutExtension(p);
                    string dir = Path.GetDirectoryName(p);
                    string dst = Path.Combine(dir, name);
                    if (File.Exists(dst))
                    {
                        string pure = Path.GetFileNameWithoutExtension(name);
                        string ext = Path.GetExtension(name);
                        dst = Path.Combine(dir, pure + "_" + DateTime.Now.ToString("HHmmss") + ext);
                    }
                    string err = "";
                    if (FileCryptoCore.Decrypt(p, dst, pwd, out err))
                    {
                        li.SubItems[2].Text = T("已解密", "decrypted");
                        li.ForeColor = UiKit.A(72, 208, 130);
                        done++;
                    }
                    else li.SubItems[2].Text = T("密码错/损坏", "bad password");
                }
                ok(done > 0 ? UiKit.A(72, 208, 130) : UiKit.A(255, 120, 120), T("完成：解密 ", "Decrypted ") + done + T(" 个文件", " files"));
            };
            f.Controls.Add(bDec);

            Button bRem = MakeBtn(T("移出列表", "Remove"), C_BUBBLE_ALT(), Color.White, 380, 330, 110, 34);
            bRem.Click += delegate
            {
                while (lv.SelectedItems.Count > 0) lv.Items.Remove(lv.SelectedItems[0]);
            };
            f.Controls.Add(bRem);

            Button bDir = MakeBtn(T("打开所在目录", "Open folder"), C_BUBBLE_ALT(), Color.White, 500, 330, 140, 34);
            bDir.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                string p = Convert.ToString(lv.SelectedItems[0].Tag);
                try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch (Exception dg20030_1) { Diag.Swallow("MainForm.cs:9241", dg20030_1); }
            };
            f.Controls.Add(bDir);

            Button bClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 330, 100, 34);
            bClose.Click += delegate { f.Close(); };
            f.Controls.Add(bClose);

            ShowOrTrack(f);
        }

        void ShowUpdateCenter()
        {
            try
            {
                UpdateForm uf = new UpdateForm(langEn);
                uf.ShowDialog(this);
                ApplyPendingJump();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("打开更新窗口失败：", "Failed to open updater: ") + ex.Message, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================= v19.14 控制中心入口 =================
        internal void OpenControlCenter()
        {
            try
            {
                using (ControlCenterForm f = new ControlCenterForm(this, BASE_DIR))
                {
                    Sfx.Launch();
                    f.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Diag.Warn("打开控制中心失败：" + ex.Message);
                MessageBox.Show(this, "打开控制中心失败：" + ex.Message, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal void CcOpenPage(string slot)
        {
            try
            {
                for (int i = 0; i < SLOT.Length; i++)
                {
                    if (SLOT[i] == slot) { SelectAction(i); return; }
                }
            }
            catch (Exception ex) { Diag.Swallow("MainForm.CcOpenPage", ex); }
        }

        internal void CcRunRecommendedClean() { try { RunClean("v8"); } catch (Exception ex) { Diag.Swallow("MainForm.CcRunClean", ex); } }
        internal void CcOpenUpdate() { try { ShowUpdateCenter(); } catch (Exception ex) { Diag.Swallow("MainForm.CcUpdate", ex); } }
        internal void CcOpenLogs() { try { ShowLog(); } catch (Exception ex) { Diag.Swallow("MainForm.CcLogs", ex); } }
        internal void CcOpenPluginStore() { try { ShowPluginStore(); } catch (Exception ex) { Diag.Swallow("MainForm.CcPlugins", ex); } }
        internal void CcOpenBackup() { try { ShowBackup(); } catch (Exception ex) { Diag.Swallow("MainForm.CcBackup", ex); } }
        internal void CcRestartApp() { try { RestartApp(); } catch (Exception ex) { Diag.Swallow("MainForm.CcRestart", ex); } }

        internal void CcOpenWatermarkFile(string path)
        {
            try
            {
                CcOpenPage("wm");
                WmLoadFile(path);
            }
            catch (Exception ex) { Diag.Swallow("MainForm.CcWmFile", ex); }
        }

        // ================= v19.14 全局拖文件 =================
        // 拖到窗口任意空白区域都能认出文件；图片/视频直接推荐去水印并载入，
        // 其它类型给出打开、定位、复制路径三个稳妥动作。不会自动执行删除或上传。
        void EnableFileDrop(Control root)
        {
            if (root == null) return;
            try
            {
                bool skip = (root is TextBoxBase) || (root is RichTextBox) || (root is ComboBox)
                    || (root is ListView) || (root is TreeView) || (root is DataGridView);
                if (!skip)
                {
                    root.AllowDrop = true;
                    root.DragEnter += FileDragEnter;
                    root.DragDrop += FileDragDrop;
                }
                for (int i = 0; i < root.Controls.Count; i++) EnableFileDrop(root.Controls[i]);
            }
            catch (Exception ex) { Diag.Swallow("MainForm.EnableFileDrop", ex); }
        }

        void FileDragEnter(object s, DragEventArgs e)
        {
            try
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
                else e.Effect = DragDropEffects.None;
            }
            catch { e.Effect = DragDropEffects.None; }
        }

        void FileDragDrop(object s, DragEventArgs e)
        {
            try
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                string first = files[0];
                string ext = Path.GetExtension(first).ToLowerInvariant();
                bool media = ext == ".mp4" || ext == ".mkv" || ext == ".mov" || ext == ".avi" || ext == ".flv"
                    || ext == ".wmv" || ext == ".webm" || ext == ".m4v" || ext == ".ts"
                    || ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp" || ext == ".gif";

                ContextMenuStrip m = new ContextMenuStrip();
                if (media)
                {
                    m.Items.Add(T("去水印并载入：", "Remove watermark: ") + Path.GetFileName(first), null, delegate
                    {
                        CcOpenWatermarkFile(first);
                    });
                }
                m.Items.Add(T("用默认程序打开", "Open with default app"), null, delegate
                {
                    try { Process.Start(new ProcessStartInfo(first) { UseShellExecute = true }); } catch (Exception ex) { Diag.Swallow("MainForm.dropOpen", ex); }
                });
                m.Items.Add(T("在资源管理器中显示", "Show in Explorer"), null, delegate
                {
                    try { Process.Start("explorer.exe", "/select,\"" + first + "\""); } catch (Exception ex) { Diag.Swallow("MainForm.dropShow", ex); }
                });
                m.Items.Add(T("复制路径", "Copy path"), null, delegate
                {
                    try { Clipboard.SetText(first); Sfx.Copy(); } catch (Exception ex) { Diag.Swallow("MainForm.dropCopy", ex); }
                });
                if (files.Length > 1) m.Items.Add(T("共 " + files.Length + " 个文件，已按第一个推荐", files.Length + " files dropped"), null, null).Enabled = false;
                m.Show(Cursor.Position);
            }
            catch (Exception ex) { Diag.Swallow("MainForm.FileDragDrop", ex); }
        }

        // ===================== v6.9 自测 =====================

        string V69Temp(string name)
        {
            string d = Path.Combine(BASE_DIR, "_v69test");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg20060_1) { Diag.Swallow("MainForm.cs:9271", dg20060_1); }
            return Path.Combine(d, name);
        }

        void HistSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string file = V69Temp("chip.dat");
                try { File.Delete(file); } catch (Exception dg20070_1) { Diag.Swallow("MainForm.cs:9281", dg20070_1); }
                ClipHistStore s1 = new ClipHistStore(file);
                s1.Add("hello-clip-1");
                s1.Add("第二段中文内容 line2");
                s1.Items[0].Pinned = true;
                s1.Save();

                string raw = File.ReadAllText(file, Encoding.UTF8);
                sb.AppendLine("file_len=" + raw.Length);
                sb.AppendLine("plain_leak=" + (raw.IndexOf("hello-clip-1") >= 0 || raw.IndexOf("第二段中文内容") >= 0));
                sb.AppendLine("preview=" + ClipHistStore.Preview("a\r\nb\tc    d", 20));

                s1.Items.Insert(0, new ClipHistStore.Entry { Text = "临时不钉的记录" });
                s1.ClearUnpinned();
                sb.AppendLine("after_clear_count=" + s1.Items.Count + " kept_pinned=" + (s1.Items.Count > 0 && s1.Items[0].Pinned));

                ClipHistStore s2 = new ClipHistStore(file);
                sb.AppendLine("load_count=" + s2.Items.Count);
                if (s2.Items.Count > 0) sb.AppendLine("roundtrip_text=" + (s2.Items[0].Text == "第二段中文内容 line2" ? "ok" : "FAIL:" + s2.Items[0].Text));
                if (s2.Items.Count > 1) sb.AppendLine("roundtrip_cn=" + (s2.Items[1].Text.IndexOf("中文") > 0 ? "ok" : "FAIL"));
                sb.AppendLine("empty_capture=" + "skip");
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_chip_test.txt", sb.ToString());
            Application.Exit();
        }

        void RecSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string ff = WmFfmpegPath();
                sb.AppendLine("ffmpeg_exist=" + File.Exists(ff) + " size=" + (File.Exists(ff) ? new FileInfo(ff).Length : 0));
                string outFile = V69Temp("rec.mp4");
                try { File.Delete(outFile); } catch (Exception dg20105_1) { Diag.Swallow("MainForm.cs:9316", dg20105_1); }
                Rectangle scr = SystemInformation.VirtualScreen;
                string err = "";
                bool ok = RecorderCore.Start(WmShortPathOr(ff), scr.Left, scr.Top, 1200, 700, 15, WmShortPathOr(outFile), out err);
                sb.AppendLine("start=" + ok + " err=" + err);
                if (ok)
                {
                    Thread.Sleep(3000);
                    string err2 = "";
                    bool stopped = RecorderCore.Stop(out err2);
                    sb.AppendLine("stop=" + stopped + " err=" + err2);
                    Thread.Sleep(300);
                    long len = File.Exists(outFile) ? new FileInfo(outFile).Length : 0;
                    sb.AppendLine("mp4_size=" + len + " valid=" + (len > 20000));
                    if (len > 20000)
                    {
                        string gif = V69Temp("rec.gif");
                        try { File.Delete(gif); } catch (Exception dg20122_1) { Diag.Swallow("MainForm.cs:9333", dg20122_1); }
                        string err3 = "";
                        bool gok = RecorderCore.ToGif(WmShortPathOr(ff), WmShortPathOr(outFile), WmShortPathOr(gif), 10, 480, out err3);
                        long glen = File.Exists(gif) ? new FileInfo(gif).Length : 0;
                        sb.AppendLine("gif=" + gok + " size=" + glen + " err=" + err3);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_rec_test.txt", sb.ToString());
            Application.Exit();
        }

        void BkSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string probeDir = Path.Combine(BASE_DIR, "整理记录");
                string probeFile = Path.Combine(probeDir, "_bk_probe.json");
                bool mkProbe = !Directory.Exists(probeDir);
                try { if (!Directory.Exists(probeDir)) Directory.CreateDirectory(probeDir); File.WriteAllText(probeFile, "{}"); } catch (Exception dg20143_1) { Diag.Swallow("MainForm.cs:9354", dg20143_1); }
                List<BackupCore.Item> items = BackupCore.Collect(BASE_DIR);
                sb.AppendLine("items=" + items.Count);
                foreach (BackupCore.Item it in items) sb.AppendLine("  " + it.Name + " size=" + it.Size + " dir=" + it.IsDir);
                string zip = V69Temp("bk.zip");
                try { File.Delete(zip); } catch (Exception dg20148_1) { Diag.Swallow("MainForm.cs:9359", dg20148_1); }
                string err = "";
                bool ok = BackupCore.Create(zip, items, out err);
                sb.AppendLine("create=" + ok + " err=" + err + " zip_size=" + (File.Exists(zip) ? new FileInfo(zip).Length : 0));
                string[] ents = BackupCore.Entries(zip);
                sb.AppendLine("entries=" + ents.Length);
                for (int i = 0; i < ents.Length && i < 10; i++) sb.AppendLine("  " + ents[i]);

                // 还原到临时目录验证
                string dst = V69Temp("restore");
                try { if (Directory.Exists(dst)) Directory.Delete(dst, true); } catch (Exception dg20158_1) { Diag.Swallow("MainForm.cs:9369", dg20158_1); }
                Directory.CreateDirectory(dst);
                string err2 = ""; string rep = "";
                bool rok = BackupCore.Restore(zip, dst, out err2, out rep);
                sb.AppendLine("restore=" + rok + " err=" + err2);
                sb.AppendLine(rep.Replace("\r\n", " | "));
                foreach (string f in Directory.GetFiles(dst)) sb.AppendLine("  restored: " + Path.GetFileName(f) + " " + new FileInfo(f).Length);
                sb.AppendLine("backup_list=" + BackupCore.ListBackups(Path.Combine(BASE_DIR, "备份")).Count);
                try { File.Delete(probeFile); if (mkProbe) Directory.Delete(probeDir, true); } catch (Exception dg20166_1) { Diag.Swallow("MainForm.cs:9377", dg20166_1); }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_bk_test.txt", sb.ToString());
            Application.Exit();
        }

        void FcSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string src = V69Temp("secret.txt");
                File.WriteAllText(src, "这是一段中文与符号 ~!@# 的内容 line2\r\n第二行", Encoding.UTF8);
                long orig = new FileInfo(src).Length;
                string box = V69Temp("secret.txt.mtbx");
                try { File.Delete(box); } catch (Exception dg20182_1) { Diag.Swallow("MainForm.cs:9393", dg20182_1); }
                string err = "";
                bool e = FileCryptoCore.Encrypt(src, box, "pass1234", out err);
                sb.AppendLine("encrypt=" + e + " err=" + err);
                sb.AppendLine("box_size=" + (File.Exists(box) ? new FileInfo(box).Length : 0) + " orig=" + orig);
                byte[] head = new byte[5];
                using (FileStream fs = new FileStream(box, FileMode.Open, FileAccess.Read)) fs.Read(head, 0, 5);
                sb.AppendLine("magic=" + Encoding.ASCII.GetString(head));
                string body = File.ReadAllText(box, Encoding.UTF8);
                sb.AppendLine("plain_leak=" + (body.IndexOf("这是一段中文") >= 0 || body.IndexOf("line2") >= 0));
                sb.AppendLine("orig_name=" + FileCryptoCore.OriginalName(box));

                string outFile = V69Temp("dec_out.txt");
                try { File.Delete(outFile); } catch (Exception dg20195_1) { Diag.Swallow("MainForm.cs:9406", dg20195_1); }
                string err2 = "";
                bool bad = FileCryptoCore.Decrypt(box, outFile, "wrongpwd", out err2);
                sb.AppendLine("wrong_pwd=" + (bad ? "FAIL" : "rejected(" + err2 + ")") + " dst_exists=" + File.Exists(outFile));

                string err3 = "";
                bool d = FileCryptoCore.Decrypt(box, outFile, "pass1234", out err3);
                sb.AppendLine("decrypt=" + d + " err=" + err3);
                if (d)
                {
                    string content = File.ReadAllText(outFile, Encoding.UTF8);
                    sb.AppendLine("roundtrip=" + (content == File.ReadAllText(src, Encoding.UTF8) ? "ok" : "FAIL"));
                    sb.AppendLine("dec_size=" + new FileInfo(outFile).Length);
                }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_fc_test.txt", sb.ToString());
            Application.Exit();
        }

        void AnnSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Bitmap bmp = new Bitmap(400, 300);
                using (Graphics g = Graphics.FromImage(bmp))
                using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(0, 0, 400, 300), UiKit.A(30, 60, 120), UiKit.A(200, 120, 40), 45F))
                    g.FillRectangle(lb, 0, 0, 400, 300);
                List<ShotAnn> list = new List<ShotAnn>();
                ShotAnn a1 = new ShotAnn(); a1.Tool = 0; a1.A = new Point(20, 20); a1.B = new Point(180, 90); list.Add(a1);
                ShotAnn a2 = new ShotAnn(); a2.Tool = 1; a2.R = new Rectangle(200, 30, 120, 80); list.Add(a2);
                ShotAnn a3 = new ShotAnn(); a3.Tool = 2; a3.R = new Rectangle(40, 160, 120, 80); list.Add(a3);
                ShotAnn a4 = new ShotAnn(); a4.Tool = 3; a4.Text = "文字标注 Test"; a4.R = new Rectangle(190, 150, 10, 10); list.Add(a4);
                ShotAnn a5 = new ShotAnn(); a5.Tool = 4; a5.Num = 1; a5.R = new Rectangle(330, 240, 4, 4); list.Add(a5);

                Bitmap outb = ShotEditForm.Compose(bmp, list);
                outb.Save(V69Temp("ann.png"), System.Drawing.Imaging.ImageFormat.Png);
                int nonWhite = 0;
                for (int y = 0; y < outb.Height; y += 3)
                    for (int x = 0; x < outb.Width; x += 3)
                    {
                        Color c = outb.GetPixel(x, y);
                        if (c.R < 245 || c.G < 245 || c.B < 245) nonWhite++;
                    }
                sb.AppendLine("anns=5 composed=" + outb.Width + "x" + outb.Height);
                sb.AppendLine("non_white_samples=" + nonWhite + " ok=" + (nonWhite > 300));
                // 马赛克区域平均色应偏离纯白
                Color mc = outb.GetPixel(80, 200);
                sb.AppendLine("mosaic_changed=" + (mc != bmp.GetPixel(80, 200)));
                sb.AppendLine("mosaic_px=" + mc.R + "," + mc.G + "," + mc.B);
                sb.AppendLine("norm=" + ShotEditForm.Norm(new Point(100, 90), new Point(20, 30)).ToString());
                sb.AppendLine("png=" + (File.Exists(V69Temp("ann.png")) ? new FileInfo(V69Temp("ann.png")).Length : 0));
                bmp.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_ann_test.txt", sb.ToString());
            Application.Exit();
        }

        void V69UiDump()
        {
            v68AutoDump = true;
            StringBuilder sb = new StringBuilder();
            try
            {
                ShowPlusCenter();
                ShowClipHist();
                ShowRecorder();
                ShowBackup();
                ShowFileCrypto();
                // 更新窗是模态的，dump 时只构建实例、不 Show，避免阻塞消息循环
                UpdateForm uf = new UpdateForm(langEn);
                v68Forms.Add(uf);
                sb.AppendLine("update_form=built");
                sb.AppendLine("opened=" + v68Forms.Count);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }

            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
            tm.Interval = 2200;
            tm.Tick += delegate
            {
                tm.Stop();
                tm.Dispose();
                foreach (Form ff in v68Forms)
                {
                    try { sb.AppendLine("[" + ff.Text + "] controls=" + ff.Controls.Count + " size=" + ff.ClientSize.Width + "x" + ff.ClientSize.Height); }
                    catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
                }
                foreach (Form ff in v68Forms) { try { ff.Close(); } catch (Exception dg20285_1) { Diag.Swallow("MainForm.cs:9496", dg20285_1); } }
                DumpTest("_v69_ui_test.txt", sb.ToString());
                Application.Exit();
            };
            tm.Start();
        }

        // ===================== v7.0 六项新功能入口 =====================

        DeskBoard v70Board;

        void V70OpenBoard()
        {
            try
            {
                if (v70Board == null || v70Board.IsDisposed)
                {
                    v70Board = new DeskBoard();
                    v70Board.Show();
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    v70Board.Location = new Point(Math.Max(0, wa.Right - v70Board.Width - 24), Math.Max(0, wa.Bottom - v70Board.Height - 24));
                }
                else
                {
                    v70Board.Visible = true;
                    v70Board.BringToFront();
                }
            }
            catch (Exception ex) { Log("[看板] " + ex.Message); }
        }

        void AddPal(List<PaletteItem> list, string zh, string en, string keys, string py, Action run)
        {
            PaletteItem it = new PaletteItem();
            it.Zh = zh;
            it.En = en;
            it.Keys = keys;
            it.Hay = (zh + " " + en + " " + keys + " " + py).ToLower();
            it.Run = run;
            if (zh != null && zh.StartsWith("跳转：")) it.Kind = "page";
            else if (zh != null && zh.StartsWith("打开") && zh.EndsWith("目录")) it.Kind = "open";
            else it.Kind = "act";
            list.Add(it);
        }

        List<PaletteItem> BuildPaletteItems()
        {
            List<PaletteItem> list = new List<PaletteItem>();
            // v8.7：九个页面也能从指令舱直达——输「视频」「清理」「成就」就跳过去
            AddPal(list, "跳转：工作台", "Go: dashboard", "Ctrl+K", "tiaozhuan gongzuotai gzt", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "home")); });
            AddPal(list, "跳转：定时关机", "Go: timer", "Ctrl+K", "tiaozhuan shoudong guanji td", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "power")); });
            AddPal(list, "跳转：C 盘清理", "Go: disk clean", "Ctrl+K", "tiaozhuan cpan qingli qpql", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "clean")); });
            AddPal(list, "跳转：去水印", "Go: watermark", "Ctrl+K", "tiaozhuan qushuiyin qsyy", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "wm")); });
            AddPal(list, "跳转：Steam 账号价值", "Go: account value", "Ctrl+K", "tiaozhuan zhanghao jiazhi zhjz", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "val")); });
            AddPal(list, "跳转：电脑配置", "Go: system info", "Ctrl+K", "tiaozhuan diannao peizhi dnzp", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "sys")); });
            AddPal(list, "跳转：视频下载", "Go: video", "Ctrl+K", "tiaozhuan shipin xiazai spxz", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "video")); });
            AddPal(list, "跳转：音乐下载", "Go: music", "Ctrl+K", "tiaozhuan yinyue xiazai yyxz", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "music")); });
            AddPal(list, "跳转：Steam 成就", "Go: achievements", "Ctrl+K", "tiaozhuan chengjiu cjj", delegate { ShowMain(); SelectAction(Array.IndexOf(SLOT, "sam")); });
            AddPal(list, "全屏截图 / 标注", "Screenshot", "Ctrl+Alt+S", "jietu quanping biaozhu jt qp bz", delegate { ScreenshotNow(); });
            AddPal(list, "延时截图 5 秒", "Screenshot in 5s", "", "jietu yanshi wumiao jtys", delegate { ScreenshotDelayed(5); });
            AddPal(list, "延时截图 10 秒", "Screenshot in 10s", "", "jietu yanshi shimiao jtys", delegate { ScreenshotDelayed(10); });
            AddPal(list, "唤出主窗口", "Show main window", "Ctrl+Alt+H", "zhuchuangkou zck", delegate { ShowMain(); });
            AddPal(list, "手机互传", "Phone transfer", "Ctrl+Alt+T", "shouji huchuan sjhc", delegate { ShowTransfer(); });
            AddPal(list, "手机遥控", "Phone remote", "", "shouji yaokong sjk web phone remote kongzhi", delegate { ShowPhoneRemote(); });
            AddPal(list, "配置同步", "Config sync", "", "peizhi tongbu pztb sync config mtb1", delegate { ShowSyncCfg(); });
            AddPal(list, "视频任务队列", "Video queue", "", "shipin renwu duilie spdl queue batch piliang", delegate { ShowVideoQueue(); });
            AddPal(list, "控制中心", "Control center", "", "kongzhi zhongxin kzzx control center dashboard task", delegate { OpenControlCenter(); });
            AddPal(list, "全局搜索", "Global search", "Ctrl+K", "quanju sousuo qjss global search quanji", delegate { ShowGlobalSearch(); });
            AddPal(list, "文件秒搜", "File search", "", "wenjian miaosou wjms everything search", delegate { ShowFileSearch(); });
            AddPal(list, "自动化触发器", "Triggers", "", "zidonghua chufaqi zdhcfq trigger auto automation", delegate { ShowTriggers(); });
            AddPal(list, "电源方案", "Power plan", "", "dianyuan fangan dyfa power plan dianyuan", delegate { ShowPowerPlan(); });
            AddPal(list, "增强工具中心", "Power tools center", "Ctrl+Alt+G", "zengqiang gongju zqgj", delegate { ShowPlusCenter(); });
            AddPal(list, "剪贴板历史", "Clipboard history", "Ctrl+Alt+V", "jiantieban lishi jtb", delegate { ShowClipHist(); });
            AddPal(list, "录屏 / 停止录屏", "Recorder toggle", "Ctrl+Alt+R", "luping tingzhi lp", delegate { ToggleRecord(); });
            AddPal(list, "一键体检", "One-click checkup", "", "yijian tijian yjtj", delegate { ShowCheckup(); });
            AddPal(list, "一键游戏加速", "Game boost", "", "youxi jiasu yxjs", delegate { ShowGameBoost(); });
            AddPal(list, "桌面自动整理", "Desktop tidy", "", "zhuomian zhengli zmzl", delegate { ShowDeskSort(); });
            AddPal(list, "重复文件清理", "Duplicate finder", "", "chongfu wenjian qingli cfwj", delegate { ShowDupClean(); });
            AddPal(list, "配置备份还原", "Backup and restore", "", "peizhi beifen hy pb", delegate { ShowBackup(); });
            AddPal(list, "单文件加密", "File encrypt", "", "danwenjian jiami dwj", delegate { ShowFileCrypto(); });
            AddPal(list, "检查更新", "Check update", "", "jiancha gengxin gx", delegate { ShowUpdateCenter(); });
            AddPal(list, "全局热键设置", "Hotkey settings", "", "rejian shezhi rjsz rj", delegate { ShowHotkeys(); });
            AddPal(list, "外观设置：皮肤与缩放", "Appearance: skin and scale", "", "waiguan pifu suofang wgpf sf", delegate { using (AppearanceForm af = new AppearanceForm()) af.ShowDialog(this); });
            AddPal(list, "快捷指令面板", "Command palette", "Ctrl+Alt+Space", "kuaijie zhiling mianban kzzl kzml", delegate { ShowPalette(); });
            AddPal(list, "系统监控悬浮条", "System HUD", "", "xitong jiankong xuantiaotiao xtjk", delegate { ShowSysHud(); });
            AddPal(list, "批量重命名 / 格式转换", "Batch rename / convert", "", "piliang chongmingming geshi zhuanhuan plcm gszh", delegate { ShowBatchTool(); });
            AddPal(list, "插件仓库", "Plugin store", "", "chajian cangku cangku pluginstore plugin store", delegate { ShowPluginStore(); });
            AddPal(list, "键鼠宏录制", "Macro recorder", "", "jianshu hong luzhi jshl hong", delegate { ShowMacroRecorder(); });
            AddPal(list, "离线密码本 / 两步验证", "Password book and 2FA", "", "mimaben liangbu yanzheng mmb 2fa totp", delegate { ShowPwdBook(); });

            AddPal(list, "桌面看板", "Desktop dashboard", "", "kanban zhuomian kb", delegate { V70OpenBoard(); });
            AddPal(list, "打开程序目录", "Open app folder", "", "dakai chengxu mulu dkml", delegate
            {
                try { Process.Start("explorer.exe", BASE_DIR); } catch (Exception dg20377_1) { Diag.Swallow("MainForm.cs:9588", dg20377_1); }
            });
            AddPal(list, "打开录屏目录", "Open recording folder", "", "dakai luping mulu dklp", delegate
            {
                try
                {
                    string d = Path.Combine(BASE_DIR, "录屏");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    Process.Start("explorer.exe", d);
                }
                catch (Exception dg20387_1) { Diag.Swallow("MainForm.cs:9598", dg20387_1); }
            });
            AddPal(list, "打开宏目录", "Open macro folder", "", "dakai hong mulu dkh", delegate
            {
                try
                {
                    string d = Path.Combine(BASE_DIR, "宏");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    Process.Start("explorer.exe", d);
                }
                catch (Exception dg20397_1) { Diag.Swallow("MainForm.cs:9608", dg20397_1); }
            });
            AddPal(list, "60 秒后关机", "Shutdown in 60s", "", "guanji yanshi gj", delegate { DoHotkeyAction("shutdown60"); });
            AddPal(list, "取消关机", "Cancel shutdown", "", "quxiao guanji qxgj", delegate
            {
                try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true }); Log("[指令] 已取消关机"); } catch (Exception dg20402_1) { Diag.Swallow("MainForm.cs:9613", dg20402_1); }
            });
            AddPal(list, "窗口管家", "Window keeper", "", "chuangkou guanjia ckgj window", delegate { ShowWindowMgr(); });
            AddPal(list, "启动项 / 计划任务", "Startup tasks", "", "qidongxi jihua renwu schtasks startup", delegate { ShowStartupTasks(); });
            AddPal(list, "网络工具箱", "Network kit", "", "wangluo gongjuxiang wlgjx dns hosts", delegate { ShowNetTools(); });
            AddPal(list, "Steam 进阶", "Steam extras", "", "steam jinjie ku banjia cedi", delegate { ShowSteamPro(); });
            AddPal(list, "图片压缩 / 二维码", "Image & QR", "", "tupian yasuo erweima qr compress", delegate { ShowImgQr(); });
            AddPal(list, "视频进阶", "Video pro", "", "shipin jinjie yasuo pinjie zimiao video", delegate { ShowVideoPro(); });
            AddPal(list, "桌面便签", "Sticky notes", "", "bianqian zhuomian note sticky", delegate { ShowStickyBoard(); });
            AddPal(list, "番茄钟", "Pomodoro", "", "fanqiezhong zhuanzhu pomodoro", delegate { ShowStickyBoard(); });
            AddPal(list, "端口与进程管家", "Ports & processes", "", "duankou jincheng netstat port pid", delegate { ShowPortTool(); });
            AddPal(list, "高清录屏 MP4", "Screen recorder", "", "luping biaogao lu ping screen record mp4", delegate { ShowScreenRec(); });
            AddPal(list, "音频工具箱", "Audio tools", "", "yinpin zhuanma jiangzao hebing audio", delegate { ShowAudioTool(); });
            AddPal(list, "代理节点管理", "Proxy nodes", "", "daili jiedian cedi proxy v2ray clash", delegate { ShowProxyTool(); });
            AddPal(list, "ADB 手机工具箱", "ADB phone kit", "", "shouji adb anzhuang apk logcat", delegate { ShowAdbTool(); });
            AddPal(list, "端口映射 / 开服", "UPnP port mapping", "", "duankou yingshe kaifu upnp mapping port", delegate { ShowUpnpTool(); });
            AddPal(list, "延迟优选", "Latency picker", "", "yanshi youxuan youxi xiazai latency ping", delegate { ShowLatencyTool(); });
            AddPal(list, "Wi-Fi 信号分析", "Wi-Fi analyzer", "", "wifi xinhao xindao fenxi channel", delegate { ShowWifiTool(); });
            // v12.2 插件：插件\ 目录里的脚本自动成为功能条目
            try
            {
                string[] pf = PluginCfg.Files();
                for (int i = 0; i < pf.Length; i++)
                {
                    string fp = pf[i];
                    string ext = Path.GetExtension(fp).ToLowerInvariant();
                    if (ext != ".ps1" && ext != ".py" && ext != ".cmd" && ext != ".bat") continue;
                    string nm = PluginCfg.Title(fp);
                    AddPal(list, T("插件：" + nm, "Plugin: " + nm), "Plugin: " + Path.GetFileName(fp), "",
                        ("chajian plugin " + nm + " " + Path.GetFileNameWithoutExtension(fp)).ToLowerInvariant(),
                        delegate { PluginCfg.Run(fp, BASE_DIR); });
                }
            }
            catch (Exception dg20435_1) { Diag.Swallow("MainForm.cs:9646", dg20435_1); }
            return list;
        }

        // ================= v8.0 输入框的“意图层” =================
        // 把一句人话（“关机 30”“清理c盘”“延迟 5 秒截图”）即时翻译成一条可直接执行的条目，置顶在结果最上面。

        static int FirstInt(string t, out int after)
        {
            after = -1;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] >= '0' && t[i] <= '9')
                {
                    int j = i;
                    while (j < t.Length && t[j] >= '0' && t[j] <= '9') j++;
                    int v = 0;
                    try { v = int.Parse(t.Substring(i, j - i)); } catch { v = 0; }
                    after = j;
                    return v;
                }
            }
            return -1;
        }

        string UnitAfter(string t, int at, out int kind)
        {
            // kind: 0 无 1 分钟 2 秒 3 小时
            kind = 0;
            if (at < 0 || at >= t.Length) return "";
            string tail = t.Substring(at, Math.Min(4, t.Length - at));
            if (tail.StartsWith("小时") || tail.StartsWith("h") || tail.StartsWith("时")) { kind = 3; return "小时"; }
            if (tail.StartsWith("分钟") || tail.StartsWith("分") || tail.StartsWith("min") || tail.StartsWith("m")) { kind = 1; return "分钟"; }
            if (tail.StartsWith("秒") || tail.StartsWith("s") || tail.StartsWith("sec")) { kind = 2; return "秒"; }
            return "";
        }

        void PaletteShutdownIn(int seconds)
        {
            try
            {
                if (seconds < 5) seconds = 5;
                if (seconds > 86400) seconds = 86400;
                // v8.1：按用户要求，指令舱的关机指令直接执行，不再弹确认框（系统侧仍可用 shutdown /a 撤销）
                Process.Start(new ProcessStartInfo("shutdown", "/s /t " + seconds + " /f") { CreateNoWindow = true });
                Log("[指令] " + seconds + " 秒后关机");
                Ui2.Toast(this, T("已安排 " + (seconds >= 60 ? (seconds / 60) + " 分钟后" : seconds + " 秒后") + "关机", "Shutdown scheduled"));
            }
            catch (Exception ex) { Log("[指令] 关机失败：" + ex.Message); Sfx.Error(); }
        }

        void PaletteShotIn(int seconds)
        {
            try
            {
                if (seconds < 1) seconds = 1;
                if (seconds > 60) seconds = 60;
                System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
                tm.Interval = seconds * 1000;
                tm.Tick += delegate
                {
                    tm.Stop();
                    tm.Dispose();
                    ScreenshotNow();
                };
                tm.Start();
                Log("[指令] " + seconds + " 秒后截图");
                Ui2.Toast(this, T(seconds + " 秒后自动截图", "Screenshot in " + seconds + "s"));
            }
            catch (Exception ex) { Log("[指令] 延迟截图失败：" + ex.Message); }
        }

        // v12.4：意图解析交给离线语义引擎（时间表达式 + 动作同义词 + 页面/工具直达）
        PaletteItem[] PaletteIntent(string raw)
        {
            List<PaletteItem> list = new List<PaletteItem>();
            if (raw == null) return list.ToArray();
            List<AiIntent.Hit> hits = AiIntent.Parse(raw);
            for (int i = 0; i < hits.Count; i++)
            {
                AiIntent.Hit h = hits[i];
                PaletteItem it = new PaletteItem();
                Action run = null;
                if (h.Id == "open")
                {
                    PaletteItem tgt = AiFindOpen(h.Open);
                    if (tgt == null || tgt.Run == null) continue;
                    it.Zh = T("打开：" + tgt.Zh, "Open: " + tgt.En);
                    it.En = "Open: " + tgt.En;
                    run = tgt.Run;
                }
                else
                {
                    run = AiMakeRun(h);
                    if (run == null) continue;
                    it.Zh = h.Zh;
                    it.En = h.En;
                }
                it.Hay = (h.Hay == null ? "" : h.Hay) + " " + (h.Id == null ? "" : h.Id);
                it.Run = run;
                list.Add(it);
            }
            return list.ToArray();
        }

        List<string> PaletteRecent()
        {
            List<string> list = new List<string>();
            try
            {
                string fp = Path.Combine(BASE_DIR, "指令历史.txt");
                if (File.Exists(fp))
                {
                    string[] ln = File.ReadAllLines(fp, Encoding.UTF8);
                    for (int i = 0; i < ln.Length && list.Count < 6; i++)
                    {
                        string x = ln[i] == null ? "" : ln[i].Trim();
                        if (x.Length > 0 && !list.Contains(x)) list.Add(x);
                    }
                }
            }
            catch (Exception dg20556_1) { Diag.Swallow("MainForm.cs:9767", dg20556_1); }
            return list;
        }

        void PaletteRemember(PaletteItem it)
        {
            try
            {
                if (it == null || it.Intent) return;
                string name = langEn ? it.En : it.Zh;
                if (name == null || name.Length == 0) return;
                string fp = Path.Combine(BASE_DIR, "指令历史.txt");
                List<string> all = new List<string>();
                if (File.Exists(fp))
                {
                    string[] ln = File.ReadAllLines(fp, Encoding.UTF8);
                    for (int i = 0; i < ln.Length; i++)
                    {
                        string x = ln[i] == null ? "" : ln[i].Trim();
                        if (x.Length > 0 && !all.Contains(x)) all.Add(x);
                    }
                }
                all.Remove(name);
                all.Insert(0, name);
                while (all.Count > 8) all.RemoveAt(all.Count - 1);
                File.WriteAllLines(fp, all.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception dg20583_1) { Diag.Swallow("MainForm.cs:9794", dg20583_1); }
        }

        PaletteForm ShowPalette()
        {
            PaletteForm f = null;
            try
            {
                f = new PaletteForm(BuildPaletteItems(), langEn);
                f.BuildIntent = PaletteIntent;
                f.RecentProvider = PaletteRecent;
                f.AfterRun = PaletteRemember;
                if (v68AutoDump) { v68Forms.Add(f); return f; }
                f.Show();
                f.Activate();
            }
            catch (Exception ex) { Log("[快捷指令] " + ex.Message); }
            return f;
        }

        void ShowSysHud()
        {
            try
            {
                if (V70State.Hud != null && !V70State.Hud.IsDisposed)
                {
                    V70State.Hud.Show();
                    V70State.Hud.BringToFront();
                    return;
                }
                SysHudForm hud = new SysHudForm(langEn);
                hud.OnOpenBoard = delegate { V70OpenBoard(); };
                V70State.Hud = hud;
                if (v68AutoDump) { v68Forms.Add(hud); return; }
                hud.Show();
            }
            catch (Exception ex) { Log("[悬浮条] " + ex.Message); }
        }

        void ShowBatchTool()
        {
            try { ShowOrTrack(new BatchToolForm(langEn)); }
            catch (Exception ex) { Log("[批量工具] " + ex.Message); }
        }

        void ShowMacroRecorder()
        {
            try { ShowOrTrack(new MacroForm(langEn)); }
            catch (Exception ex) { Log("[键鼠宏] " + ex.Message); }
        }

        void ShowPwdBook()
        {
            try { ShowOrTrack(new PwdBookForm(langEn)); }
            catch (Exception ex) { Log("[密码本] " + ex.Message); }
        }

        void ShowPrivacy()
        {
            try { ShowOrTrack(new PrivacyForm(langEn)); }
            catch (Exception ex) { Log("[隐私清理] " + ex.Message); }
        }

        // ===================== v7.0 自测 =====================

        void PalSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                List<PaletteItem> items = BuildPaletteItems();
                sb.AppendLine("commands=" + items.Count.ToString());
                int empty = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Hay == null || items[i].Hay.Length == 0) empty++;
                    if (items[i].Run == null) sb.AppendLine("null run: " + items[i].Zh);
                }
                sb.AppendLine("empty_hay=" + empty.ToString());
                string q = "截";
                int hit = 0;
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Hay.IndexOf(q) >= 0)
                    {
                        hit++;
                        sb.AppendLine("filter[" + q + "] -> " + items[i].Zh);
                    }
                sb.AppendLine("hit=" + hit.ToString());
                int hit2 = 0;
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Hay.IndexOf("pwd") >= 0 || items[i].Hay.IndexOf("mmb") >= 0) hit2++;
                sb.AppendLine("pinyin_mmb_hits=" + hit2.ToString());
                int hit3 = 0;
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Hay.IndexOf("jianshu") >= 0) hit3++;
                sb.AppendLine("pinyin_jianshu_hits=" + hit3.ToString());
                // v8.0：意图层——一句人话能翻成一条可执行条目
                sb.AppendLine("== v8.0 意图解析 ==");
                string[] iq = new string[] { "关机 30", "关机1小时", "关机 90秒", "关机", "清理c盘", "截图 5秒", "截", "mmb", "" };
                for (int i = 0; i < iq.Length; i++)
                {
                    PaletteItem[] rit = PaletteIntent(iq[i]);
                    sb.Append("intent[" + (iq[i].Length == 0 ? "(empty)" : iq[i]) + "] n=" + rit.Length);
                    for (int k = 0; k < rit.Length; k++) sb.Append(" | " + rit[k].Zh);
                    sb.AppendLine();
                }

                // 历史落盘往返：用一个假条目试，测完擦掉，不污染真实历史
                try
                {
                    string hp = Path.Combine(BASE_DIR, "指令历史.txt");
                    int before = PaletteRecent().Count;
                    PaletteItem dum = new PaletteItem();
                    dum.Zh = "自测条目_zzz";
                    dum.En = "selftest_zzz";
                    PaletteRemember(dum);
                    List<string> after = PaletteRecent();
                    sb.AppendLine("hist file=" + hp + " before=" + before + " after=" + after.Count + " top=" + (after.Count > 0 ? after[0] : "(none)"));
                    if (File.Exists(hp))
                    {
                        List<string> keep = new List<string>();
                        string[] ln = File.ReadAllLines(hp, Encoding.UTF8);
                        for (int i = 0; i < ln.Length; i++)
                        {
                            string x = ln[i] == null ? "" : ln[i].Trim();
                            if (x.Length > 0 && x != dum.Zh && !keep.Contains(x)) keep.Add(x);
                        }
                        if (keep.Count == 0) File.Delete(hp);
                        else File.WriteAllLines(hp, keep.ToArray(), new UTF8Encoding(false));
                    }
                    sb.AppendLine("hist cleaned=" + (File.Exists(hp) ? "kept " + keep_Count(hp) : "removed"));
                }
                catch (Exception hex) { sb.AppendLine("hist EX " + hex.Message); }

                PaletteForm pf = new PaletteForm(items, langEn);
                pf.BuildIntent = PaletteIntent;
                pf.RecentProvider = PaletteRecent;
                pf.AfterRun = PaletteRemember;
                sb.AppendLine("form_size=" + pf.ClientSize.Width + "x" + pf.ClientSize.Height + " topmost=" + pf.TopMost + " client=" + pf.ClientRectangle.Width + "x" + pf.ClientRectangle.Height);
                string[] tq = new string[] { "", "关机 30", "截", "zzz不存在" };
                for (int i = 0; i < tq.Length; i++)
                {
                    pf.SetQueryForTest(tq[i]);
                    sb.AppendLine("q[" + (tq[i].Length == 0 ? "(empty)" : tq[i]) + "] items=" + pf.ItemCount + " cnt=" + pf.CountText);
                }
                sb.AppendLine("== v12 功能可搜性（每条必须有命中，否则 FAIL）==");
                string[] mustZh = new string[] {
                    "手机遥控", "配置同步", "视频任务队列", "全局搜索", "延时截图",
                    "端口与进程管家", "音频工具箱", "剪贴板历史", "重复文件清理", "窗口管家",
                    "文件秒搜", "自动化触发器", "电源方案" };
                int mustFail = 0;
                for (int i = 0; i < mustZh.Length; i++)
                {
                    pf.SetQueryForTest(mustZh[i]);
                    if (pf.ItemCount <= 0) mustFail++;
                    sb.AppendLine("must[" + mustZh[i] + "] items=" + pf.ItemCount + " " + (pf.ItemCount > 0 ? "PASS" : "FAIL"));
                }
                string[] mustPy = new string[] { "qdql", "sjk", "pztb", "spdl", "jtys", "duankou", "yinpin" };
                int pyFail = 0;
                for (int i = 0; i < mustPy.Length; i++)
                {
                    pf.SetQueryForTest(mustPy[i]);
                    if (pf.ItemCount <= 0) pyFail++;
                    sb.AppendLine("fuzzy[" + mustPy[i] + "] items=" + pf.ItemCount + " " + (pf.ItemCount > 0 ? "PASS" : "FAIL"));
                }
                sb.AppendLine("search_assert must_fail=" + mustFail + " fuzzy_fail=" + pyFail);
                pf.SetQueryForTest("");
                sb.AppendLine("geom " + pf.DumpGeometry());
                pf.Close();
                pf.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_pal_test.txt", sb.ToString());
            Application.Exit();
        }

        int keep_Count(string fp)
        {
            try
            {
                string[] ln = File.ReadAllLines(fp, Encoding.UTF8);
                int n = 0;
                for (int i = 0; i < ln.Length; i++) if (ln[i] != null && ln[i].Trim().Length > 0) n++;
                return n;
            }
            catch { return 0; }
        }

        void HudSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            SysHudForm hud = null;
            try
            {
                hud = new SysHudForm(langEn);
                hud.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(1800);
                Application.DoEvents();
                sb.AppendLine("size=" + hud.ClientSize.Width + "x" + hud.ClientSize.Height);
                sb.AppendLine("loc=" + hud.Left + "," + hud.Top + " topmost=" + hud.TopMost + " opacity=" + hud.Opacity);
                sb.AppendLine("sample=" + hud.DumpLine());
                hud.StopTimer();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { if (hud != null) { hud.Close(); hud.Dispose(); } } catch (Exception dg20788_1) { Diag.Swallow("MainForm.cs:9999", dg20788_1); }
            DumpTest("_hud_test.txt", sb.ToString());
            Application.Exit();
        }

        void BatchSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string dir = Path.Combine(BASE_DIR, "_bt_test");
            BatchToolForm bf = null;
            try
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg20800_1) { Diag.Swallow("MainForm.cs:10011", dg20800_1); }
                Directory.CreateDirectory(dir);
                for (int i = 1; i <= 5; i++)
                    File.WriteAllText(Path.Combine(dir, "样本" + i.ToString() + ".txt"), "x" + i.ToString(), Encoding.UTF8);

                bf = new BatchToolForm(langEn);
                bf.TestSetMode(0, "已改_", "");
                bf.TestLoadFolder(dir);
                sb.AppendLine("--- preview ---");
                sb.Append(bf.TestPreview());
                bf.TestApply();
                string[] after = Directory.GetFiles(dir);
                sb.AppendLine("count_after=" + after.Length.ToString());
                for (int i = 0; i < after.Length; i++) sb.AppendLine("file=" + Path.GetFileName(after[i]));
                bf.TestUndo();
                string[] back = Directory.GetFiles(dir);
                sb.AppendLine("count_back=" + back.Length.ToString());
                for (int i = 0; i < back.Length; i++) sb.AppendLine("back=" + Path.GetFileName(back[i]));

                // 图片转换 png -> jpg
                string png = Path.Combine(dir, "图.png");
                try
                {
                    using (Bitmap b = new Bitmap(64, 48))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.Clear(UiKit.A(30, 120, 220));
                            g.DrawString("MT", new Font("Arial", 14F), Brushes.White, 8, 8);
                        }
                        b.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    bf.TestAddConv(png);
                    bf.TestSetTarget(1);   // JPG
                    bf.TestConvert();
                    sb.AppendLine("png_to_jpg=" + File.Exists(Path.Combine(dir, "图_conv.jpg")));
                    bf.TestSetTarget(0);   // PNG
                    bf.TestConvert();
                    sb.AppendLine("png_to_png=" + File.Exists(Path.Combine(dir, "图_conv.png")));
                }
                catch (Exception ex2) { sb.AppendLine("imgEX " + ex2.Message); }

                // 视频链路：用内嵌 FFmpeg 现场生成 2 秒测试片，再转 GIF / MP3 / MP4
                string ff = Path.Combine(Path.Combine(BASE_DIR, "video_tool"), "ffmpeg.exe");
                sb.AppendLine("ffmpeg_exists=" + File.Exists(ff));
                string mp4 = Path.Combine(dir, "测试视频.mp4");
                string genErr = "";
                bool gen = false;
                if (File.Exists(ff))
                {
                    gen = MediaCore.Run(ff, "-y -hide_banner -loglevel error -f lavfi -i testsrc=size=320x240:rate=10 -f lavfi -i sine=frequency=440:duration=2 -t 2 -pix_fmt yuv420p -c:a aac \"" + mp4 + "\"", 120000, out genErr);
                }
                sb.AppendLine("gen_mp4=" + gen + (gen ? "" : " " + genErr));
                if (gen && File.Exists(mp4))
                {
                    bf.TestClearConv();
                    bf.TestAddConv(mp4);
                    bf.TestSetTarget(6);
                    bf.TestConvert();
                    sb.AppendLine("mp4_to_gif=" + File.Exists(Path.Combine(dir, "测试视频_conv.gif")));

                    bf.TestClearConv();
                    bf.TestAddConv(mp4);
                    bf.TestSetTarget(5);
                    bf.TestConvert();
                    sb.AppendLine("mp4_to_mp3=" + File.Exists(Path.Combine(dir, "测试视频_audio.mp3")));

                    bf.TestClearConv();
                    bf.TestAddConv(mp4);
                    bf.TestSetTarget(4);
                    bf.TestConvert();
                    sb.AppendLine("mp4_to_mp4=" + File.Exists(Path.Combine(dir, "测试视频_conv.mp4")));
                }
                sb.AppendLine("--- log tail ---");
                string lg = bf.TestLog();
                if (lg.Length > 3000) lg = lg.Substring(lg.Length - 3000);
                sb.AppendLine(lg);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { if (bf != null) { bf.Close(); bf.Dispose(); } } catch (Exception dg20879_1) { Diag.Swallow("MainForm.cs:10090", dg20879_1); }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception dg20880_1) { Diag.Swallow("MainForm.cs:10091", dg20880_1); }
            DumpTest("_bt_test.txt", sb.ToString());
            Application.Exit();
        }

        void MacroSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                List<MacroEvent> src = new List<MacroEvent>();
                int[] types = { 1, 2, 3, 4, 5, 6, 7, 8 };
                for (int i = 0; i < types.Length; i++)
                {
                    MacroEvent e = new MacroEvent();
                    e.T = types[i];
                    e.Vk = 65 + i;
                    e.X = 100 + i * 10;
                    e.Y = 200 + i * 5;
                    e.Data = 120;
                    e.Delay = 40 + i;
                    src.Add(e);
                }
                string txt = MacroCore.Serialize(src);
                sb.AppendLine("serialized_lines=" + txt.Replace("\r", "").Split('\n').Length);
                List<MacroEvent> back = MacroCore.Deserialize(txt);
                sb.AppendLine("parsed=" + back.Count.ToString());
                bool same = back.Count == src.Count;
                for (int i = 0; i < back.Count && same; i++)
                {
                    if (back[i].T != src[i].T || back[i].Vk != src[i].Vk || back[i].X != src[i].X
                        || back[i].Y != src[i].Y || back[i].Data != src[i].Data || back[i].Delay != src[i].Delay) same = false;
                }
                sb.AppendLine("roundtrip=" + (same ? "OK" : "FAIL"));

                string err = "";
                bool ok = MacroCore.StartRecord(out err);
                sb.AppendLine("hook_install=" + (ok ? "ok" : "ERR " + err));
                System.Threading.Thread.Sleep(200);
                Application.DoEvents();
                List<MacroEvent> got = MacroCore.StopRecord();
                sb.AppendLine("record_stop=ok events=" + got.Count.ToString());
                sb.AppendLine("hook_released=" + (!MacroCore.Recording));

                string macDir = Path.Combine(BASE_DIR, "宏");
                if (!Directory.Exists(macDir)) Directory.CreateDirectory(macDir);
                string file = Path.Combine(macDir, "_selftest.mac");
                File.WriteAllText(file, txt, Encoding.UTF8);
                sb.AppendLine("file_written=" + new FileInfo(file).Length);
                string rd = File.ReadAllText(file, Encoding.UTF8);
                sb.AppendLine("file_reparse=" + MacroCore.Deserialize(rd).Count.ToString());
                File.Delete(file);

                MacroForm mf = new MacroForm(langEn);
                sb.AppendLine("form_size=" + mf.ClientSize.Width + "x" + mf.ClientSize.Height + " keypreview=" + mf.KeyPreview);
                mf.Close();
                mf.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_mac_test.txt", sb.ToString());
            Application.Exit();
        }

        void PwbSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string file = Path.Combine(BASE_DIR, "_pwb_test.pwb");
            try
            {
                // RFC6238 官方向量：secret 为 20 字节 "12345678901234567890" 的 Base32，t=59 -> 287082
                string rfcKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
                string code = PwdBookCore.Totp(rfcKey, new DateTime(1970, 1, 1, 0, 0, 59, DateTimeKind.Utc), 6, 30);
                sb.AppendLine("totp_t59=" + code + " expect=287082 " + (code == "287082" ? "OK" : "FAIL"));
                string code2 = PwdBookCore.Totp(rfcKey, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(1111111109), 8, 30);
                sb.AppendLine("totp_t1111111109=" + code2 + " expect=07081804 " + (code2 == "07081804" ? "OK" : "FAIL"));
                string shortKey = PwdBookCore.Totp("GEZDGNBVGY3TQOJQ", new DateTime(1970, 1, 1, 0, 0, 59, DateTimeKind.Utc), 6, 30);
                sb.AppendLine("totp_10byte_key_t59=" + shortKey + " expect=263420 " + (shortKey == "263420" ? "OK" : "FAIL"));
                sb.AppendLine("b32_len=" + PwdBookCore.B32("GEZDGNBVGY3TQOJQ").Length + " expect=10");
                sb.AppendLine("b32_len20=" + PwdBookCore.B32(rfcKey).Length + " expect=20");
                sb.AppendLine("remain=" + PwdBookCore.RemainSeconds(30));

                List<PwdEntry> list = new List<PwdEntry>();
                PwdEntry a = new PwdEntry();
                a.Site = "github.com";
                a.User = "tester";
                a.Pwd = "P@ssw0rd-中文";
                a.Note = "主账号";
                a.Secret = "GEZDGNBVGY3TQOJQ";
                list.Add(a);
                PwdEntry b = new PwdEntry();
                b.Site = "bilibili";
                b.User = "u2";
                b.Pwd = "123456";
                list.Add(b);

                string err = "";
                bool ok = PwdBookCore.Save(file, "masterPwd", list, out err);
                sb.AppendLine("save=" + (ok ? "ok" : "ERR " + err));
                byte[] raw = File.ReadAllBytes(file);
                sb.AppendLine("file_bytes=" + raw.Length.ToString() + " magic=" + Encoding.ASCII.GetString(raw, 0, 5));
                string plainScan = Encoding.UTF8.GetString(raw);
                sb.AppendLine("no_plaintext=" + (!plainScan.Contains("github.com") && !plainScan.Contains("P@ssw0rd")));

                string err2 = "";
                bool wrong = false;
                List<PwdEntry> got = PwdBookCore.Load(file, "masterPwd", out err2, out wrong);
                sb.AppendLine("load=" + got.Count.ToString() + " wrong=" + wrong + " err=" + err2);
                if (got.Count > 0) sb.AppendLine("entry0=" + got[0].Site + "/" + got[0].User + "/" + got[0].Pwd + "/" + got[0].Note + "/" + got[0].Secret);

                string err3 = "";
                bool wrong3 = false;
                List<PwdEntry> bad = PwdBookCore.Load(file, "badPwd", out err3, out wrong3);
                sb.AppendLine("wrong_pwd_detected=" + wrong3.ToString() + " count=" + bad.Count.ToString());

                PwdBookForm f = new PwdBookForm(langEn);
                sb.AppendLine("form_size=" + f.ClientSize.Width + "x" + f.ClientSize.Height);
                f.Close();
                f.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception dg21000_1) { Diag.Swallow("MainForm.cs:10211", dg21000_1); }
            DumpTest("_pwb_test.txt", sb.ToString());
            Application.Exit();
        }

        void PrivacySelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                List<PrivItem> items = PrivacyCore.Scan();
                long total = 0;
                sb.AppendLine("items=" + items.Count.ToString());
                for (int i = 0; i < items.Count; i++)
                {
                    PrivItem it = items[i];
                    total += it.Size;
                    sb.AppendLine(it.Kind + " | " + it.Zh + " | " + it.Size.ToString() + " | " + it.Target);
                }
                sb.AppendLine("total_bytes=" + total.ToString());
                PrivacyForm pf = new PrivacyForm(langEn);
                pf.ScanForTest();
                sb.AppendLine("form_size=" + pf.ClientSize.Width + "x" + pf.ClientSize.Height);
                pf.Close();
                pf.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_prv_test.txt", sb.ToString());
            Application.Exit();
        }

        void V70UiDump()
        {
            v68AutoDump = true;
            StringBuilder sb = new StringBuilder();
            try
            {
                ShowPalette();
                ShowSysHud();
                ShowBatchTool();
                ShowMacroRecorder();
                ShowPwdBook();
                ShowPrivacy();
                ShowPlusCenter();
                sb.AppendLine("opened=" + v68Forms.Count);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }

            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
            tm.Interval = 2400;
            tm.Tick += delegate
            {
                tm.Stop();
                tm.Dispose();
                for (int i = 0; i < v68Forms.Count; i++)
                {
                    Form ff = v68Forms[i];
                    try { sb.AppendLine("[" + ff.Text + "] controls=" + ff.Controls.Count + " size=" + ff.ClientSize.Width + "x" + ff.ClientSize.Height); }
                    catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
                }
                for (int i = 0; i < v68Forms.Count; i++) { try { v68Forms[i].Close(); } catch (Exception dg21060_1) { Diag.Swallow("MainForm.cs:10271", dg21060_1); } }
                DumpTest("_v70_ui_test.txt", sb.ToString());
                Application.Exit();
            };
            tm.Start();
        }

        // ================= v7.1 ② 窗口管家 =================
        void ShowWindowMgr()
        {
            WinMgrForm wf = new WinMgrForm(langEn, BASE_DIR);
            ShowOrTrack(wf);
        }

        void WinMgrSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                int dummy = 0;
                List<WinCore.Item> ws = WinCore.Collect(out dummy);
                sb.AppendLine("items=" + ws.Count);
                int shown = 0;
                foreach (WinCore.Item it in ws)
                {
                    if (shown++ >= 8) break;
                    sb.AppendLine(it.Pid + " | top=" + it.Topmost + " | min=" + it.Min + " | " + it.Title);
                }
                sb.AppendLine("minimal=" + WinCore.MinimizeAllCount());
                sb.AppendLine("layout_ok=" + WinCore.LayoutTest(BASE_DIR));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_winmgr_test.txt", sb.ToString());
            Application.Exit();
        }

        // ================= v7.1 七项新工具入口 =================
        void ShowV71Center()
        {
            Form f = new Form();
            f.Text = T("v7.1 新工具 · 小工具箱", "v7.1 New Tools · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(1140, 470);
            f.MinimumSize = new Size(1140, 470);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("v7.1 新工具 · 七项", "v7.1 New Tools · Seven");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("窗口管家、启动项（计划任务）、网络工具箱、Steam 进阶三件套、图片压缩 + 二维码、视频进阶、桌面便签 + 番茄钟",
                "Window keeper, scheduled tasks, network kit, Steam extras, image + QR, video pro, sticky notes + pomodoro");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 50);
            sub.Size = new Size(1100, 24);
            f.Controls.Add(sub);

            int cw = 350, chh = 88;
            int cx1 = 20, cx2 = 390, cx3 = 760;
            int cy1 = 92, cy2 = 188, cy3 = 284;

            f.Controls.Add(PlusCard(cx1, cy1, cw, chh, T("① 窗口管家", "① Window keeper"),
                T("列出所有窗口，置顶 / 最小化 / 关掉；一键记住并还原窗口布局", "List windows: pin, minimize, close; save & restore layouts"),
                UiKit.A(96, 165, 250), delegate { ShowWindowMgr(); }));

            f.Controls.Add(PlusCard(cx2, cy1, cw, chh, T("② 启动项 · 计划任务", "② Scheduled tasks"),
                T("补上启动项管理缺的那块：计划任务启停、立即运行、新建开机自启", "Enable/disable/run scheduled tasks, create on-logon tasks"),
                UiKit.A(14, 165, 233), delegate { ShowStartupTasks(); }));

            f.Controls.Add(PlusCard(cx3, cy1, cw, chh, T("③ 网络工具箱", "③ Network kit"),
                T("DNS 一键切换、hosts 编辑、局域网扫描、网速实测、网络复位", "DNS switch, hosts editor, LAN scan, speed test, reset"),
                UiKit.A(34, 197, 94), delegate { ShowNetTools(); }));

            f.Controls.Add(PlusCard(cx1, cy2, cw, chh, T("④ Steam 进阶三件套", "④ Steam extras"),
                T("多库盘点、库搬家、节点测速；成就那套照旧", "Libraries, move games between drives, node latency"),
                UiKit.A(250, 190, 90), delegate { ShowSteamPro(); }));

            f.Controls.Add(PlusCard(cx2, cy2, cw, chh, T("⑤ 图片压缩 + 二维码", "⑤ Image & QR"),
                T("按目标体积批量压图，另存不覆盖；离线生成二维码不联网", "Batch squeeze images to a target size; offline QR codes"),
                UiKit.A(244, 114, 182), delegate { ShowImgQr(); }));

            f.Controls.Add(PlusCard(cx3, cy2, cw, chh, T("⑥ 视频进阶", "⑥ Video pro"),
                T("压到指定体积、多段无损拼接、抽内嵌字幕 / 音轨", "Compress to a size, join clips, pull subtitles or audio"),
                UiKit.A(251, 146, 60), delegate { ShowVideoPro(); }));

            f.Controls.Add(PlusCard(cx1, cy3, cw, chh, T("⑦ 桌面便签 + 番茄钟", "⑦ Notes & pomodoro"),
                T("便签常驻桌面可置顶，番茄钟自动轮转并存统计", "Sticky windows that stay on top; 25/5 pomodoro with stats"),
                UiKit.A(139, 92, 246), delegate { ShowStickyBoard(); }));

            Label hint = new Label();
            hint.Text = T("提示：这七项都在「增强工具中心」第 ⑪ 张卡进入；快捷指令里直接输「窗口」「启动项」「网络」「steam」「二维码」「视频进阶」「便签」也能跳。",
                "Tip: all seven open from card ⑱ of Power Tools; the palette also jumps on those keywords.");
            hint.ForeColor = UiKit.A(120, 132, 148);
            hint.Font = new Font("Microsoft YaHei UI", 8.5F);
            hint.Location = new Point(20, 378);
            hint.Size = new Size(1000, 40);
            f.Controls.Add(hint);

            Button close = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 1000, 412, 110, 32);
            close.Click += delegate { f.Close(); };
            f.Controls.Add(close);

            ShowOrTrack(f);
        }

        void ShowStartupTasks()
        {
            StartupTaskForm sf = new StartupTaskForm(langEn, BASE_DIR);
            ShowOrTrack(sf);
        }

        void ShowNetTools()
        {
            NetToolForm nf = new NetToolForm(langEn, BASE_DIR);
            ShowOrTrack(nf);
        }

        void ShowSteamPro()
        {
            SteamProForm sf = new SteamProForm(langEn, BASE_DIR);
            ShowOrTrack(sf);
        }

        void ShowImgQr()
        {
            ImgQrForm f = new ImgQrForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowVideoPro()
        {
            VideoProForm f = new VideoProForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowStickyBoard()
        {
            StickyForm f = new StickyForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        // ---- v7.1 自测 ----
        void StartupTaskSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string err = "";
                List<StartupTaskCore.TaskItem> ts = StartupTaskCore.List(false, out err);
                sb.AppendLine("tasks=" + ts.Count + " err=" + err);
                int n = 0;
                foreach (StartupTaskCore.TaskItem it in ts)
                {
                    if (n++ >= 10) break;
                    sb.AppendLine(it.Name + " | " + it.State + " | " + it.When + " | " + it.Run);
                }
                sb.AppendLine("csv_ok=" + StartupTaskCore.SplitCsvTest());
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_startup_test.txt", sb.ToString());
            Application.Exit();
        }

        void VideoProSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string ff = VideoProCore.Ffmpeg(BASE_DIR);
                sb.AppendLine("ffmpeg=" + (ff.Length > 0 ? ff : "missing"));
                if (ff.Length > 0)
                {
                    double d = VideoProCore.ParseDuration("  Duration: 00:01:23.45, start: 0.000000, bitrate: 1234 kb/s");
                    sb.AppendLine("parse_duration=" + d.ToString("0.00"));
                    string tmp = Path.Combine(Path.GetTempPath(), "mtb_wmtest.mp4");
                    string args = "-hide_banner -y -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=frequency=440:duration=2 -c:v libx264 -preset ultrafast -c:a aac -shortest \"" + tmp + "\"";
                    string o = VideoProCore.Run(ff, args, 60000, null);
                    sb.AppendLine("gen_test_clip=" + File.Exists(tmp) + (File.Exists(tmp) ? (" size=" + new FileInfo(tmp).Length) : ""));
                    if (File.Exists(tmp))
                    {
                        double dur = VideoProCore.Duration(BASE_DIR, tmp);
                        sb.AppendLine("duration_of_test=" + dur.ToString("0.00"));
                        string outp = "";
                        string err = "";
                        bool okC = VideoProCore.Compress(BASE_DIR, tmp, 1, out outp, out err, null);
                        sb.AppendLine("compress_ok=" + okC + " " + (okC ? outp + " " + new FileInfo(outp).Length : err));
                        bool okA = VideoProCore.ExtractAudio(BASE_DIR, tmp, out outp, out err);
                        sb.AppendLine("audio_ok=" + okA + " " + (okA ? Path.GetFileName(outp) : err));
                        try { File.Delete(tmp); } catch (Exception dg21256_1) { Diag.Swallow("MainForm.cs:10467", dg21256_1); }
                        if (okC && File.Exists(outp)) { try { File.Delete(outp); } catch (Exception dg21257_1) { Diag.Swallow("MainForm.cs:10468", dg21257_1); } }
                    }
                    else sb.AppendLine("tail=" + (o.Length > 400 ? o.Substring(o.Length - 400) : o));
                }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_videopro_test.txt", sb.ToString());
            Application.Exit();
        }

        void ImgQrSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string tmpDir = Path.Combine(Path.GetTempPath(), "mtb_imgtest");
                if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
                using (Bitmap b = new Bitmap(1600, 1200))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.SteelBlue);
                        g.FillEllipse(Brushes.Orange, 100, 100, 800, 600);
                    }
                    b.Save(Path.Combine(tmpDir, "t1.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                string outDir = Path.Combine(tmpDir, "out");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                ImgQrCore.Job j = ImgQrCore.CompressOne(Path.Combine(tmpDir, "t1.png"), outDir, 100, 1080, "jpg", true);
                sb.AppendLine("compress_ok=" + j.Ok + " before=" + j.Before + " after=" + j.After + " msg=" + j.Msg);
                sb.AppendLine("qr_max_capacity=" + QrCore.MaxCapacity());
                foreach (string s in new string[] { "HELLO MTB 123", "小工具箱 v7.1", ImgQrCore.WifiPayload("MyWiFi", "12345678", "WPA") })
                {
                    string err = "";
                    int ver = 0;
                    int[,] m = QrCore.Matrix(s, out err, out ver);
                    sb.AppendLine("qr len=" + Encoding.UTF8.GetByteCount(s) + " ver=" + ver + " ok=" + (m != null) + " " + err);
                }
                string too = new string('A', 300);
                string e2 = "";
                int v2 = 0;
                int[,] m2 = QrCore.Matrix(too, out e2, out v2);
                sb.AppendLine("qr_overcap ok=" + (m2 == null) + " msg=" + e2);
                try { Directory.Delete(tmpDir, true); } catch (Exception dg21300_1) { Diag.Swallow("MainForm.cs:10511", dg21300_1); }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_imgqr_test.txt", sb.ToString());
            Application.Exit();
        }

        void StickySelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                List<StickyCore.Note> ns = new List<StickyCore.Note>();
                StickyCore.Note n = new StickyCore.Note();
                n.Id = 1;
                n.Title = "自测便签";
                n.Body = "第一行\r\n第二行\tTab";
                n.X = 100; n.Y = 120; n.W = 280; n.H = 220;
                n.Top = true;
                ns.Add(n);
                string tmpBase = Path.Combine(Path.GetTempPath(), "mtb_stickytest");
                if (!Directory.Exists(tmpBase)) Directory.CreateDirectory(tmpBase);
                StickyCore.Save(tmpBase, ns);
                List<StickyCore.Note> back = StickyCore.Load(tmpBase);
                sb.AppendLine("count=" + back.Count);
                if (back.Count == 1)
                {
                    sb.AppendLine("title_ok=" + (back[0].Title == n.Title));
                    sb.AppendLine("body_ok=" + (back[0].Body == n.Body));
                    sb.AppendLine("rect_ok=" + (back[0].X == n.X && back[0].Y == n.Y && back[0].W == n.W && back[0].H == n.H));
                    sb.AppendLine("top_ok=" + (back[0].Top == n.Top));
                }
                try { Directory.Delete(tmpBase, true); } catch (Exception dg21332_1) { Diag.Swallow("MainForm.cs:10543", dg21332_1); }
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_sticky_test.txt", sb.ToString());
            Application.Exit();
        }

        void V71UiDump()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.AppendLine("VER=" + UiKit.VER);
                sb.AppendLine("palette_items=" + BuildPaletteItems().Count);
                Form[] fs = new Form[] {
                    new WinMgrForm(langEn, BASE_DIR),
                    new StartupTaskForm(langEn, BASE_DIR),
                    new NetToolForm(langEn, BASE_DIR),
                    new SteamProForm(langEn, BASE_DIR),
                    new ImgQrForm(langEn, BASE_DIR),
                    new VideoProForm(langEn, BASE_DIR),
                    new StickyForm(langEn, BASE_DIR),
                    new PomodoroForm(langEn, BASE_DIR)
                };
                string[] names = new string[] { "WinMgrForm", "StartupTaskForm", "NetToolForm", "SteamProForm", "ImgQrForm", "VideoProForm", "StickyForm", "PomodoroForm" };
                for (int i = 0; i < fs.Length; i++)
                {
                    Form f = fs[i];
                    sb.AppendLine(names[i] + " client=" + f.ClientSize.Width + "x" + f.ClientSize.Height + " controls=" + f.Controls.Count + " text=" + f.Text);
                    int btn = 0, lv = 0, lb = 0, txt = 0;
                    foreach (Control c in f.Controls)
                    {
                        if (c is Button) btn++;
                        else if (c is ListView) lv++;
                        else if (c is Label) lb++;
                        else if (c is TextBox) txt++;
                    }
                    sb.AppendLine("   btn=" + btn + " list=" + lv + " label=" + lb + " textbox=" + txt);
                    f.Dispose();
                }
                sb.AppendLine("qr_max_capacity=" + QrCore.MaxCapacity());
                sb.AppendLine("ffmpeg=" + (VideoProCore.Ready(BASE_DIR) ? "ok" : "missing"));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            DumpTest("_v71dump.txt", sb.ToString());
            Application.Exit();
        }

        // ================= v7.2 五项新工具入口 =================
        void ShowV72Center()
        {
            Form f = new Form();
            f.Text = T("v7.2 新工具 · 小工具箱", "v7.2 New Tools · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(1140, 400);
            f.MinimumSize = new Size(1140, 400);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("v7.2 新工具 · 五项", "v7.2 New Tools · Five");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("端口与进程管家、高清硬编录屏、音频工具箱、代理节点管理、ADB 手机工具箱",
                "Ports & processes, hardware screen recorder, audio tools, proxy nodes, ADB phone kit");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 50);
            sub.Size = new Size(1100, 24);
            f.Controls.Add(sub);

            int cw = 350, chh = 88;
            int cx1 = 20, cx2 = 390, cx3 = 760;
            int cy1 = 92, cy2 = 188;

            f.Controls.Add(PlusCard(cx1, cy1, cw, chh, T("① 端口与进程管家", "① Ports & processes"),
                T("谁占了端口一眼看清：TCP/UDP、进程、可执行文件，可结束进程或整棵树", "See who owns a port; kill the process or the whole tree"),
                UiKit.A(96, 165, 250), delegate { ShowPortTool(); }));

            f.Controls.Add(PlusCard(cx2, cy1, cw, chh, T("② 高清录屏 (MP4)", "② Screen recorder"),
                T("NVENC / QSV / AMF 硬编优先，全屏或框选区域，随时停止成片", "Hardware-encoded capture, full screen or region, stop any time"),
                UiKit.A(34, 197, 94), delegate { ShowScreenRec(); }));

            f.Controls.Add(PlusCard(cx3, cy1, cw, chh, T("③ 音频工具箱", "③ Audio tools"),
                T("批量转码、音量归一、简易降噪、多段合并，原文件不动", "Batch convert, normalize, denoise, merge without touching originals"),
                UiKit.A(14, 165, 233), delegate { ShowAudioTool(); }));

            f.Controls.Add(PlusCard(cx1, cy2, cw, chh, T("④ 代理节点管理", "④ Proxy nodes"),
                T("扫 v2rayN / Clash 节点、批量测速排序、一键切换系统代理", "Scan v2rayN / Clash nodes, latency-test, switch system proxy"),
                UiKit.A(249, 115, 22), delegate { ShowProxyTool(); }));

            f.Controls.Add(PlusCard(cx2, cy2, cw, chh, T("⑤ ADB 手机工具箱", "⑤ ADB phone kit"),
                T("装 APK、截屏录屏、抓日志、推拉文件、重启，没 adb 能一键装", "Install APKs, screenshot, record, logcat, push/pull, reboot"),
                UiKit.A(139, 92, 246), delegate { ShowAdbTool(); }));

            Label hint = new Label();
            hint.Text = T("提示：这五项都在「增强工具中心」第 ⑫ 张卡进入；快捷指令里输「端口」「录屏」「音频」「代理」「手机」也能直接跳。",
                "Tip: all five open from card ⑲ of Power Tools; palette keywords: port, record, audio, proxy, adb.");
            hint.ForeColor = UiKit.A(120, 132, 148);
            hint.Font = new Font("Microsoft YaHei UI", 8.5F);
            hint.Location = new Point(20, 292);
            hint.Size = new Size(1000, 40);
            f.Controls.Add(hint);

            Button close = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 1000, 342, 110, 32);
            close.Click += delegate { f.Close(); };
            f.Controls.Add(close);

            ShowOrTrack(f);
        }

        void ShowPortTool()
        {
            PortForm f = new PortForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowScreenRec()
        {
            ScreenRecForm f = new ScreenRecForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowAudioTool()
        {
            AudioToolForm f = new AudioToolForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowProxyTool()
        {
            ProxyForm f = new ProxyForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowAdbTool()
        {
            AdbForm f = new AdbForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowUpnpTool()
        {
            UpnpForm f = new UpnpForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowLatencyTool()
        {
            LatencyForm f = new LatencyForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        void ShowWifiTool()
        {
            WifiForm f = new WifiForm(langEn, BASE_DIR);
            ShowOrTrack(f);
        }

        // ---------- v7.2 自测 ----------
        void PortSelfTest()
        {
            DumpTest("_port_test.txt", PortCore.SelfTestText());
            Application.Exit();
        }

        void ScreenRecSelfTest()
        {
            DumpTest("_rec_test.txt", ScreenRecCore.SelfTestText(BASE_DIR));
            Application.Exit();
        }

        void AudioSelfTest()
        {
            DumpTest("_audio_test.txt", AudioCore.SelfTestText(BASE_DIR));
            Application.Exit();
        }

        void ProxySelfTest()
        {
            DumpTest("_proxy_test.txt", ProxyCore.SelfTestText(BASE_DIR));
            Application.Exit();
        }

        // v7.8 自测：两个新窗体渲染成图（看排版有没有压盖/裁切）
        void WishShot()
        {
            string err = "";
            try
            {
                WishForm wf = new WishForm(langEn, BASE_DIR);
                wf.StartPosition = FormStartPosition.Manual;
                wf.Location = new Point(0, 0);
                wf.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(wf.Width, wf.Height))
                {
                    wf.DrawToBitmap(bmp, new Rectangle(0, 0, wf.Width, wf.Height));
                    bmp.Save(Path.Combine(BASE_DIR, "_wishshot.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                wf.Close(); wf.Dispose();
                LibHealthForm lf = new LibHealthForm(langEn, BASE_DIR);
                lf.StartPosition = FormStartPosition.Manual;
                lf.Location = new Point(0, 0);
                lf.Show();
                Application.DoEvents();
                lf.ScanForTest();
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(lf.Width, lf.Height))
                {
                    lf.DrawToBitmap(bmp, new Rectangle(0, 0, lf.Width, lf.Height));
                    bmp.Save(Path.Combine(BASE_DIR, "_libshot.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                lf.Close(); lf.Dispose();
            }
            catch (Exception e) { err = e.ToString(); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_wishshot_err.txt"), err.Length == 0 ? "ok" : err, Encoding.UTF8); } catch (Exception dg21558_1) { Diag.Swallow("MainForm.cs:10769", dg21558_1); }
            try { Application.Exit(); } catch (Exception dg21559_1) { Diag.Swallow("MainForm.cs:10770", dg21559_1); }
        }

        // v7.8 自测：愿望单 + 库体检两个新窗体的几何（不联网、不改任何东西）
        void WishUiDump()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                WishForm wf = new WishForm(langEn, BASE_DIR);
                sb.Append(wf.DumpGeometry());
                wf.Dispose();
                sb.Append(WishForm.CheckOverlaps(wf));
                LibHealthForm lf = new LibHealthForm(langEn, BASE_DIR);
                sb.Append(lf.DumpGeometry());
                sb.Append(WishForm.CheckOverlaps(lf));
                lf.Dispose();
                // Steam 进阶页：老按钮 + 两个新入口的压盖检查
                SteamProForm sf = new SteamProForm(langEn, BASE_DIR);
                sb.Append("SteamProForm " + sf.ClientSize.Width + "x" + sf.ClientSize.Height + "\r\n");
                sb.Append(WishForm.CheckOverlaps(sf));
                sf.Dispose();
                sb.Append("ok\r\n");
            }
            catch (Exception e) { sb.Append("EX " + e.Message + "\r\n"); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_wishui.txt"), sb.ToString(), Encoding.UTF8); } catch (Exception dg21584_1) { Diag.Swallow("MainForm.cs:10795", dg21584_1); }
            try { Application.Exit(); } catch (Exception dg21585_1) { Diag.Swallow("MainForm.cs:10796", dg21585_1); }
        }

        // v7.8 自测：库体检真实跑一遍（只读），结果落盘
        void LibCheckTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                LibHealthForm lf = new LibHealthForm(langEn, BASE_DIR);
                lf.ScanForTest();
                sb.Append(lf.ReportForTest());
                sb.Append("geometry:\r\n" + lf.DumpGeometry());
                lf.Dispose();
            }
            catch (Exception e) { sb.Append("EX " + e.Message + "\r\n"); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_libcheck_test.txt"), sb.ToString(), Encoding.UTF8); } catch (Exception dg21601_1) { Diag.Swallow("MainForm.cs:10812", dg21601_1); }
            try { Application.Exit(); } catch (Exception dg21602_1) { Diag.Swallow("MainForm.cs:10813", dg21602_1); }
        }

        // v7.8 自测：愿望单（联网，只读），取前 5 款查价，结果落盘
        void WishTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string sid = SteamWishCore.SteamId64();
                sb.Append("SteamID=" + sid + "\r\n");
                string err = "";
                List<SteamWishCore.Item> items = SteamWishCore.Load(sid, out err);
                sb.Append("wishlist=" + items.Count + " err=" + err + "\r\n");
                int take = items.Count > 5 ? 5 : items.Count;
                List<SteamWishCore.Item> head = new List<SteamWishCore.Item>();
                for (int i = 0; i < take; i++) head.Add(items[i]);
                string perr = "";
                int priced = SteamWishCore.Prices(head, 5, null, null, out perr);
                sb.Append("priced=" + priced + "/" + take + " err=" + perr + "\r\n");
                foreach (SteamWishCore.Item it in head)
                    sb.Append("  " + it.AppId + " | " + it.Name + " | now=" + it.Now + " | first=" + it.First + " | off=" + it.Discount + "%\r\n");
                // 缓存写入 + 读回基准（跨次比价的关键链路）
                SteamWishCore.SaveCache(head, BASE_DIR);
                string cf = SteamWishCore.CacheFile(BASE_DIR);
                sb.Append("cache=" + (File.Exists(cf) ? "yes " + new FileInfo(cf).Length + "B" : "no") + "\r\n");
                if (File.Exists(cf))
                    foreach (string ln in File.ReadAllLines(cf, Encoding.UTF8)) sb.Append("  raw " + ln + "\r\n");
                SteamWishCore.ApplyCache(items, BASE_DIR);
                foreach (SteamWishCore.Item it in items)
                    sb.Append("  applied " + it.AppId + " first=" + it.FirstV.ToString("0.00") + " seen=" + it.Seen + "\r\n");
                sb.Append("csv=" + SteamWishCore.ExportCsv(items, BASE_DIR, false) + "\r\n");
            }
            catch (Exception e) { sb.Append("EX " + e.Message + "\r\n"); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_wish_test.txt"), sb.ToString(), Encoding.UTF8); } catch (Exception dg21636_1) { Diag.Swallow("MainForm.cs:10847", dg21636_1); }
            try { Application.Exit(); } catch (Exception dg21637_1) { Diag.Swallow("MainForm.cs:10848", dg21637_1); }
        }

        // ================= v7.9 投递舱：侧栏入口 + 全窗拖放 + 按类型分流 =================
        void BuildDropPort()
        {
            dropPort = new Panel();
            dropPort.BackColor = C_SIDE;
            dropPort.Location = new Point(8, 500);
            dropPort.Size = new Size(96, 86);
            dropPort.Cursor = Cursors.Hand;
            dropPort.Paint += delegate(object s, PaintEventArgs e) { PaintDropPort(e.Graphics); };
            dropPort.Click += delegate { PickDroppedFile(); };
            dropPort.MouseEnter += delegate { dropHot = true; if (dropPort != null) dropPort.Invalidate(); };
            dropPort.MouseLeave += delegate { dropHot = false; if (dropPort != null) dropPort.Invalidate(); };
            dropPort.Visible = false;   // v17.9：改由侧栏第 11 项承担入口
        }

        void PaintDropPort(Graphics g)
        {
            if (dropPort == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, dropPort.Width - 1, dropPort.Height - 1);
            using (GraphicsPath path = UiKit.RoundedPath(rc, Ui2.R_CTRL))
            {
                Color fill = dropHot ? UiKit.A(36, 46, 64) : UiKit.A(25, 30, 38);
                using (LinearGradientBrush br = new LinearGradientBrush(rc, UiKit.Lighten(fill, 8), fill, 90F))
                    g.FillPath(br, path);
                using (Pen pen = new Pen(dropHot ? Color.FromArgb(215, Ui2.ACCENT) : UiKit.AA(110, 92, 108, 134), dropHot ? 1.6f : 1f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 4f, 4f };
                    g.DrawPath(pen, path);
                }
            }
            Rectangle plus = new Rectangle(11, 9, 24, 24);
            using (GraphicsPath pp = UiKit.RoundedPath(plus, 8))
            using (LinearGradientBrush pb = new LinearGradientBrush(plus, UiKit.A(104, 174, 255), UiKit.A(48, 100, 208), 90F))
            {
                g.FillPath(pb, pp);
                using (Pen lp = new Pen(UiKit.A(242, 250, 255), 2f))
                {
                    g.DrawLine(lp, plus.Left + 7, plus.Top + 12, plus.Right - 7, plus.Top + 12);
                    g.DrawLine(lp, plus.Left + 12, plus.Top + 7, plus.Left + 12, plus.Bottom - 7);
                }
            }
            // v16.0：侧栏收成 112 宽后，投递卡改竖排三段，图标在上、标题与一行说明在下
            using (Font f1 = new Font(Ui2.FamilyName, 8.5F, FontStyle.Bold))
                TextRenderer.DrawText(g, T("投递文件", "Drop files"), f1, new Rectangle(8, 36, 80, 16), C_TEXT,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            using (Font f2 = new Font(Ui2.FamilyName, 7F))
            {
                TextRenderer.DrawText(g, T("视频 · 图片 · 音频", "video · image · audio"), f2,
                    new Rectangle(8, 52, 80, 13), C_SUB, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, T("拖到窗口任意处", "or drop anywhere"), f2,
                    new Rectangle(8, 66, 80, 13), UiKit.A(120, 134, 156), TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter);
            }
        }

        void BuildDropMask()
        {
            dropMask = new Panel();
            dropMask.Location = new Point(sidebar.Width, 0);
            dropMask.Size = new Size(ClientSize.Width - sidebar.Width, ClientSize.Height);
            dropMask.BackColor = UiKit.A(16, 20, 26);
            dropMask.Visible = false;
            dropMask.Paint += delegate(object s, PaintEventArgs e) { PaintDropMask(e.Graphics); };
            Controls.Add(dropMask);
            dropMask.BringToFront();
            dropPulseTimer = new System.Windows.Forms.Timer();
            dropPulseTimer.Interval = 90;
            dropPulseTimer.Tick += delegate
            {
                dropPhase++;
                if (dropMask != null && dropMask.Visible) dropMask.Invalidate();
            };
            dropPulseTimer.Start();
        }

        void PaintDropMask(Graphics g)
        {
            if (dropMask == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle full = new Rectangle(0, 0, dropMask.Width, dropMask.Height);
            using (LinearGradientBrush br = new LinearGradientBrush(full, UiKit.A(21, 27, 37), UiKit.A(11, 14, 19), 90F))
                g.FillRectangle(br, full);
            Ui2.DrawDashFrame(g, new Rectangle(30, 30, full.Width - 60, full.Height - 60), Ui2.ACCENT, dropPhase);

            Rectangle ic = new Rectangle(full.Width / 2 - 27, 150, 54, 54);
            using (GraphicsPath ip = UiKit.RoundedPath(ic, 15))
            using (LinearGradientBrush ib = new LinearGradientBrush(ic, UiKit.A(116, 184, 255), UiKit.A(44, 98, 208), 90F))
            {
                g.FillPath(ib, ip);
                using (Pen dp = new Pen(UiKit.A(240, 250, 255), 2.2f))
                {
                    g.DrawLine(dp, ic.Left + 27, ic.Top + 15, ic.Left + 27, ic.Bottom - 15);
                    g.DrawLine(dp, ic.Left + 15, ic.Top + 27, ic.Right - 15, ic.Top + 27);
                }
                using (Pen bp = new Pen(UiKit.AA(90, 255, 255, 255), 1f))
                    g.DrawPath(bp, ip);
            }

            string title = T("松手投递", "Drop to import");
            using (Font ft = new Font(Ui2.FamilyName, 20F, FontStyle.Bold))
            {
                Size ts = TextRenderer.MeasureText(title, ft, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, title, ft, new Point((full.Width - ts.Width) / 2, 216), Ui2.TEXT, TextFormatFlags.NoPadding);
            }
            string sub = T("图片 / 视频 → 去水印页并自动载入 · 音频 → 音频工具 · 其他 → 路径进剪贴板",
                "image / video -> Watermark page, audio -> Audio Tool, other -> path to clipboard");
            using (Font fs = new Font(Ui2.FamilyName, 9.5F))
            {
                Size ss = TextRenderer.MeasureText(sub, fs, new Size(full.Width - 120, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, sub, fs, new Rectangle((full.Width - Math.Min(full.Width - 120, ss.Width)) / 2, 252, Math.Min(full.Width - 120, ss.Width), ss.Height + 4),
                    Ui2.SUB, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
            }
            if (dropPreview != null && dropPreview.Length > 0)
            {
                Rectangle chip = new Rectangle(full.Width / 2 - 190, 316, 380, 40);
                using (GraphicsPath cp = UiKit.RoundedPath(chip, 10))
                {
                    using (SolidBrush cb = new SolidBrush(UiKit.A(30, 40, 54)))
                        g.FillPath(cb, cp);
                    using (Pen cpen = new Pen(Color.FromArgb(120, Ui2.ACCENT), 1f))
                        g.DrawPath(cpen, cp);
                }
                using (Font fc = new Font(Ui2.FamilyName, 9F, FontStyle.Bold))
                    TextRenderer.DrawText(g, T("即将投递：", "incoming: ") + Path.GetFileName(dropPreview), fc,
                        new Rectangle(chip.Left + 14, chip.Top, chip.Width - 28, chip.Height), Ui2.TEXT,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            using (Font fh = new Font(Ui2.FamilyName, 8F))
                TextRenderer.DrawText(g, T("支持多选 · 拖到窗口任何位置都算", "multi-select ok · drop anywhere in the window"), fh,
                    new Rectangle(0, full.Height - 66, full.Width, 16), UiKit.A(110, 122, 142),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        void HookDropTree(Control c, int depth)
        {
            if (c == null || depth > 9) return;
            try
            {
                c.AllowDrop = true;
                if (c != (Control)this)
                {
                    c.DragEnter += OnDragEnterAny;
                    c.DragLeave += OnDragLeaveAny;
                    c.DragDrop += OnDropAny;
                }
            }
            catch (Exception dg21787_1) { Diag.Swallow("MainForm.cs:10998", dg21787_1); }
            for (int i = 0; i < c.Controls.Count; i++) HookDropTree(c.Controls[i], depth + 1);
        }

        void OnDragEnterAny(object s, DragEventArgs e)
        {
            try
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                    string[] f = null;
                    try { f = e.Data.GetData(DataFormats.FileDrop) as string[]; } catch (Exception dg21799_1) { Diag.Swallow("MainForm.cs:11010", dg21799_1); }
                    dropPreview = (f != null && f.Length > 0) ? f[0] : "";
                    dropDragOn = true;
                    DropUi(true);
                }
                else e.Effect = DragDropEffects.None;
            }
            catch (Exception dg21806_1) { Diag.Swallow("MainForm.cs:11017", dg21806_1); }
        }

        void OnDragLeaveAny(object s, EventArgs e)
        {
            dropDragOn = false;
            DropUi(false);
        }

        void OnDropAny(object s, DragEventArgs e)
        {
            dropDragOn = false;
            if (dropMask != null) dropMask.Visible = false;
            string[] files = null;
            try { files = e.Data.GetData(DataFormats.FileDrop) as string[]; } catch (Exception dg21820_1) { Diag.Swallow("MainForm.cs:11031", dg21820_1); }
            RouteDropped(files);
        }

        void DropUi(bool on)
        {
            try
            {
                if (dropMask == null) return;
                if (dropHideTimer != null) dropHideTimer.Stop();
                if (on)
                {
                    if (!dropMask.Visible)
                    {
                        dropMask.Visible = true;
                        dropMask.BringToFront();
                        dropTapPlayed = false;
                    }
                    if (!dropTapPlayed)
                    {
                        dropTapPlayed = true;
                        Sfx.Click();          // 拖入瞬间一声轻“咔”，告诉你“接住了”
                    }
                }
                else
                {
                    if (dropHideTimer == null)
                    {
                        dropHideTimer = new System.Windows.Forms.Timer();
                        dropHideTimer.Interval = 150;
                        dropHideTimer.Tick += delegate
                        {
                            try
                            {
                                dropHideTimer.Stop();
                                if (!dropDragOn && dropMask != null) dropMask.Visible = false;
                            }
                            catch (Exception dg21857_1) { Diag.Swallow("MainForm.cs:11068", dg21857_1); }
                        };
                    }
                    dropHideTimer.Start();
                }
            }
            catch (Exception dg21863_1) { Diag.Swallow("MainForm.cs:11074", dg21863_1); }
        }

        void PickDroppedFile()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Title = T("选择要投递的文件", "Pick a file to import");
            d.Filter = T("视频 / 图片|*.mp4;*.mkv;*.mov;*.avi;*.flv;*.wmv;*.webm;*.m4v;*.ts;*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|音频|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|全部文件|*.*",
                "video / image|*.mp4;*.mkv;*.mov;*.avi;*.flv;*.wmv;*.webm;*.m4v;*.ts;*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|audio|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|all files|*.*");
            if (d.ShowDialog(this) != DialogResult.OK) return;
            RouteDropped(new string[] { d.FileName });
        }

        void RouteDropped(string[] files)
        {
            if (files == null || files.Length == 0)
            {
                Sfx.Error();
                Ui2.Toast(this, T("没接到文件 —— 请从资源管理器把文件拖进来", "Nothing received - drag a file from Explorer"), Ui2.BAD);
                return;
            }
            string first = files[0];
            if (first == null || first.Length == 0 || !File.Exists(first))
            {
                Sfx.Error();
                Ui2.Toast(this, T("这个拖拽项不是本地文件，无法投递", "That drop item is not a local file"), Ui2.BAD);
                return;
            }
            string ext = Path.GetExtension(first).ToLowerInvariant();
            string name = Path.GetFileName(first);
            string more = files.Length > 1 ? T("（共 " + files.Length + " 个）", " (+" + (files.Length - 1) + " more)") : "";

            if (IsImageExt(ext) || IsVideoExt(ext))
            {
                SelectAction(Array.IndexOf(SLOT, "wm"));
                Sfx.Drop();
                Ui2.Toast(this, T("已投递到「去水印」：", "Dropped to Watermark: ") + name + more, Ui2.ACCENT);
                string f2 = first;
                this.BeginInvoke((Action)delegate { WmLoadFile(f2); });
                return;
            }
            if (IsAudioExt(ext))
            {
                try { Clipboard.SetText(first); } catch (Exception dg21906_1) { Diag.Swallow("MainForm.cs:11117", dg21906_1); }
                Sfx.Drop();
                Ui2.Toast(this, T("音频已投递：", "Audio dropped: ") + name + T("（路径已在剪贴板，音频工具已打开）", " (path copied, audio tool opened)"), Ui2.ACCENT);
                try { AudioToolForm af = new AudioToolForm(langEn, BASE_DIR); af.Show(this); } catch (Exception dg21909_1) { Diag.Swallow("MainForm.cs:11120", dg21909_1); }
                return;
            }
            try { Clipboard.SetText(first); } catch (Exception dg21912_1) { Diag.Swallow("MainForm.cs:11123", dg21912_1); }
            Sfx.Drop();
            Ui2.Toast(this, T("已识别并复制路径：", "Recognized, path copied: ") + name + more, Ui2.WARN);
        }

        static bool IsImageExt(string e)
        {
            return e == ".jpg" || e == ".jpeg" || e == ".png" || e == ".bmp" || e == ".webp" || e == ".gif";
        }
        static bool IsVideoExt(string e)
        {
            return e == ".mp4" || e == ".mkv" || e == ".mov" || e == ".avi" || e == ".flv" || e == ".wmv" || e == ".webm" || e == ".m4v" || e == ".ts";
        }
        static bool IsAudioExt(string e)
        {
            return e == ".mp3" || e == ".wav" || e == ".flac" || e == ".m4a" || e == ".aac" || e == ".ogg" || e == ".wma";
        }

        // ================= v7.9 自测：界面审计 =================
        // ================= v12.3 排版体检：/uitype =================
        // 量三件事：① 还有多少按钮装不下自己的字；② 还有多少字号不在档位上；③ 字族是否混进别的字体。
        // 判定"装不下"时按真实可用空间算（父容器右边界 / 右侧同级控件），不是简单拿宽度比文字宽——
        // 贴右边界的按钮本来就只能那么宽，如果旁边真的没空位，那属于版面约束，不算缺陷。
        void UiTypeSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                int[] acc = new int[6];
                UiType.Scan(this, acc, 0);
                sb.AppendLine("family=" + UiType.Family());
                sb.AppendLine("buttons=" + acc[0]);
                sb.AppendLine("need_room=" + acc[1]);        // 文字比按钮宽（含可撑宽的情形）
                sb.AppendLine("font_off_step=" + acc[2]);
                sb.AppendLine("font_mixed=" + acc[3]);
                sb.AppendLine("no_room=" + acc[4]);          // 真实无空间可撑的（应为 0）
                // 重新走一遍排版，确认幂等：已排好的界面再跑一次不应再改任何东西
                UiType.Polish(this);
                int[] acc2 = new int[6];
                UiType.Scan(this, acc2, 0);
                sb.AppendLine("after_polish buttons=" + acc2[0] + " need_room=" + acc2[1] + " font_off=" + acc2[2] + " mixed=" + acc2[3] + " no_room=" + acc2[4]);
                sb.AppendLine("idempotent=" + (acc2[1] == acc[1] && acc2[2] == acc[2]));
                sb.AppendLine("--- 无空间明细 ---");
                UiType.Offenders(this, sb, 0);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
            File.WriteAllText(Path.Combine(BASE_DIR, "_uitype.txt"), sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        void UiAudit()
        {
            StringBuilder r = new StringBuilder();
            try
            {
                r.AppendLine("== v8.0 界面审计（字体 / 裁切 / 相交 / 越界 / 指令舱）==");
                r.AppendLine("family=" + Ui2.FamilyName + "  client=" + ClientSize.Width + "x" + ClientSize.Height + "  sidebar=" + sidebar.Width);
                r.AppendLine("bounds hdr=" + header.Bounds + " st=" + statusbar.Bounds + " main=" + mainArea.Bounds + " page=" + pageTimer.Bounds);
                r.AppendLine("shell mainClient=" + mainArea.ClientSize + " content=" + content.Bounds + " cDock=" + content.Dock + " pDock=" + pageTimer.Dock + " cParent=" + (content.Parent == mainArea));
                string[] keys = new string[] { "home", "timer", "clean", "wm", "val", "sys", "video", "music", "sam" };
                Control[] pgs = new Control[] { pageHome, pageTimer, pageClean, pageWatermark, pageValue, pageSys, pageVideo, pageMusic, pageSam };
                Dictionary<string, int> fonts = new Dictionary<string, int>();
                int totCtrl = 0, totOvf = 0, totClp = 0, totOvl = 0;
                for (int i = 0; i < keys.Length; i++)
                {
                    // 逐页真正显示一次再量：隐藏页 Dock 不参与布局，ClientSize 会停在 200x100 默认值，量出来全是假越界
                    for (int q = 0; q < pgs.Length; q++) pgs[q].Visible = (q == i);
                    pgs[i].BringToFront();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(60);
                    Application.DoEvents();
                    List<string> ovf = new List<string>();
                    List<string> clp = new List<string>();
                    List<string> ovl = new List<string>();
                    int ctrl = 0;
                    AuditNode(pgs[i], pgs[i], ref ctrl, ovf, clp, ovl, fonts, 0);
                    r.AppendLine("[" + keys[i] + "] ctrl=" + ctrl + " overflow=" + ovf.Count + " clipped=" + clp.Count + " overlap=" + ovl.Count);
                    int lim = 0;
                    for (int k = 0; k < ovf.Count && lim < 8; k++, lim++) r.AppendLine("    OVF " + ovf[k]);
                    for (int k = 0; k < clp.Count && k < 8; k++) r.AppendLine("    CLP " + clp[k]);
                    for (int k = 0; k < ovl.Count && k < 8; k++) r.AppendLine("    OVL " + ovl[k]);
                    totCtrl += ctrl; totOvf += ovf.Count; totClp += clp.Count; totOvl += ovl.Count;
                }
                r.AppendLine("TOTAL ctrl=" + totCtrl + " overflow=" + totOvf + " clipped=" + totClp + " overlap=" + totOvl);
                StringBuilder fsb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in fonts) fsb.Append(kv.Key + "×" + kv.Value + "   ");
                r.AppendLine("FONTS: " + fsb.ToString());
                // v8.1：排版小结——最小字号、低于 8F 的字号数、非目标字族数
                {
                    double minSz = 99.0; int under8 = 0; int offFam = 0; int totalF = 0;
                    foreach (KeyValuePair<string, int> kv in fonts)
                    {
                        string k = kv.Key; int cnt = kv.Value; totalF += cnt;
                        string[] tok = k.Split(' ');
                        string famOnly = k; double sz = 0.0;
                        int end = tok.Length;
                        if (end > 0 && tok[end - 1] == "b") end--;
                        if (end > 1) { try { sz = double.Parse(tok[end - 1], System.Globalization.CultureInfo.InvariantCulture); } catch (Exception dg22009_1) { Diag.Swallow("MainForm.cs:11220", dg22009_1); } famOnly = string.Join(" ", tok, 0, end - 1); }
                        if (famOnly.IndexOf("MDL2", StringComparison.OrdinalIgnoreCase) >= 0) { continue; }
                        if (sz > 0 && sz < minSz) minSz = sz;
                        if (sz > 0 && sz < 8.0) under8 += cnt;
                        if (!Ui2.IsFamilyOk(famOnly) && famOnly.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) < 0) offFam += cnt;
                    }
                    r.AppendLine("TYPO: ctrl=" + totalF + " min_size=" + minSz.ToString("0.#") + " under8=" + under8 + " off_family=" + offFam + " family=" + Ui2.FamilyName);
                }
                // v8.1：对比度最低的几处文字（WCAG 口径），低于 3:1 才算真问题
                {
                    List<KeyValuePair<double, string>> worst = new List<KeyValuePair<double, string>>();
                    for (int i = 0; i < keys.Length; i++)
                    {
                        for (int q = 0; q < pgs.Length; q++) pgs[q].Visible = (q == i);
                        pgs[i].BringToFront();
                        Application.DoEvents();
                        ContrastNode(pgs[i], worst, 0);
                    }
                    worst.Sort(delegate (KeyValuePair<double, string> a, KeyValuePair<double, string> b) { return a.Key.CompareTo(b.Key); });
                    StringBuilder csb = new StringBuilder();
                    for (int k = 0; k < worst.Count && k < 3; k++) csb.Append(worst[k].Value + "(" + worst[k].Key.ToString("0.0") + ")   ");
                    r.AppendLine("CONTRAST worst3: " + csb.ToString());
                }
                // v8.0：把「快捷指令舱」也纳入同一套度量——输入框是这个版本的主角，不能只看 9 个页签
                PaletteForm pf2 = null;
                try
                {
                    pf2 = new PaletteForm(BuildPaletteItems(), langEn);
                    pf2.BuildIntent = PaletteIntent;
                    pf2.RecentProvider = PaletteRecent;
                    pf2.AfterRun = PaletteRemember;
                    Ui2.Apply(pf2);
                    pf2.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                    Application.DoEvents();
                    List<string> ovf2 = new List<string>();
                    List<string> clp2 = new List<string>();
                    List<string> ovl2 = new List<string>();
                    int ctrl2 = 0;
                    AuditNode(pf2, pf2, ref ctrl2, ovf2, clp2, ovl2, fonts, 0);
                    r.AppendLine("[palette] ctrl=" + ctrl2 + " overflow=" + ovf2.Count + " clipped=" + clp2.Count + " overlap=" + ovl2.Count);
                    for (int k = 0; k < ovf2.Count && k < 8; k++) r.AppendLine("    OVF " + ovf2[k]);
                    for (int k = 0; k < clp2.Count && k < 8; k++) r.AppendLine("    CLP " + clp2[k]);
                    for (int k = 0; k < ovl2.Count && k < 8; k++) r.AppendLine("    OVL " + ovl2[k]);
                    r.AppendLine("    GEOM " + pf2.DumpGeometry());
                    pf2.SetQueryForTest("关机 30");
                    r.AppendLine("    query[关机 30] items=" + pf2.ItemCount + " cnt=" + pf2.CountText);
                    pf2.SetQueryForTest("zzz");
                    r.AppendLine("    query[zzz] items=" + pf2.ItemCount + " cnt=" + pf2.CountText);
                    pf2.SetQueryForTest("");
                    totCtrl += ctrl2; totOvf += ovf2.Count; totClp += clp2.Count; totOvl += ovl2.Count;
                    pf2.Close();
                    pf2.Dispose();
                }
                catch (Exception pex) { r.AppendLine("palette EX " + pex.Message); }
                if (dropPort != null)
                    r.AppendLine("drop_port=" + dropPort.Left + "," + dropPort.Top + " " + dropPort.Width + "x" + dropPort.Height
                        + "  bottom=" + dropPort.Bottom + "  autorun_top=" + chkAutorun.Top
                        + "  clash=" + (dropPort.Bottom > chkAutorun.Top ? "YES" : "no"));
                if (dropMask != null)
                    r.AppendLine("drop_mask=" + dropMask.Left + "," + dropMask.Top + " " + dropMask.Width + "x" + dropMask.Height
                        + "  covers_main=" + (dropMask.Width == ClientSize.Width - sidebar.Width ? "yes" : "no"));
                r.AppendLine("sfx=" + (Sfx.On ? "on" : "off") + "  toast=ready  focus_font=" + Ui2.FamilyName);
            }
            catch (Exception ex) { r.AppendLine("EX " + ex); }
            DumpTest("_uiaudit.txt", r.ToString());
            Application.Exit();
        }

        static double Lum(Color c) { return UiKit.Lum(c); }

        static Color BackBehind(Control c) { return UiKit.BackBehind(c); }

        void ContrastNode(Control parent, List<KeyValuePair<double, string>> worst, int depth)
        {
            if (parent == null || depth > 6) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                Label la = c as Label;
                if (la != null && !string.IsNullOrEmpty(la.Text) && la.Visible && !Ui2.IsIconText(la.Text))
                {
                    Color fg = la.ForeColor, bg = BackBehind(la);
                    double l1 = Lum(fg), l2 = Lum(bg);
                    double hi = Math.Max(l1, l2), lo = Math.Min(l1, l2);
                    double ratio = (hi + 0.05) / (lo + 0.05);
                    string t = la.Text.Replace("\n", " ");
                    if (t.Length > 14) t = t.Substring(0, 14);
                    worst.Add(new KeyValuePair<double, string>(ratio, t));
                }
                if (c.Controls.Count > 0) ContrastNode(c, worst, depth + 1);
            }
        }

        void AuditNode(Control parent, Control root, ref int ctrl, List<string> ovf, List<string> clp, List<string> ovl, Dictionary<string, int> fonts, int depth)
        {
            if (parent == null || depth > 6) return;
            List<Control> texts = new List<Control>();
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                ctrl++;
                string txt = c.Text == null ? "" : c.Text;
                if (c.Font != null)
                {
                    string kk = c.Font.Name + " " + c.Font.Size.ToString("0.#") + (c.Font.Bold ? " b" : "");
                    if (fonts.ContainsKey(kk)) fonts[kk] = fonts[kk] + 1; else fonts[kk] = 1;
                }
                Label la = c as Label;
                if (la != null && txt.Length > 0 && !Ui2.IsIconText(txt) && !Ui2.IsIconFont(la.Font))
                {
                    if (!la.AutoSize)
                    {
                        int avail = la.Width - la.Padding.Horizontal;
                        if (avail > 6)
                        {
                            Size need = new Size(0, 0);
                            string[] ln = txt.Split('\n');
                            for (int z = 0; z < ln.Length; z++)
                            {
                                Size nz = TextRenderer.MeasureText(ln[z], la.Font, new Size(int.MaxValue, int.MaxValue),
                                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                                if (nz.Width > need.Width) need = nz;
                            }
                            // 一行放不下时，看“允许换行后”还装不装得下：装得下就不算被裁，真装不下才报
                            bool bad = false;
                            if (need.Width > avail)
                            {
                                Size wr = TextRenderer.MeasureText(txt, la.Font, new Size(avail, int.MaxValue),
                                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                                bad = wr.Height > la.ClientSize.Height + 2;
                            }
                            if (bad)
                                ovf.Add(la.Parent.Name + "/" + Short(txt) + " need=" + need.Width + " avail=" + avail);
                        }
                    }
                    else
                    {
                        Control pc = la.Parent;
                        if (pc != null && (la.Right > pc.ClientSize.Width + 1 || la.Bottom > pc.ClientSize.Height + 1))
                            clp.Add(la.Parent.Name + "/" + Short(txt) + " at=" + la.Left + "," + la.Top + " " + la.Width + "x" + la.Height
                                + " parent=" + pc.ClientSize.Width + "x" + pc.ClientSize.Height);
                    }
                    texts.Add(la);
                }
                if (c is Button)
                {
                    Button bt = (Button)c;
                    if (txt.Length > 0) texts.Add(bt);
                }
                if (c is CheckBox || c is RadioButton) { if (txt.Length > 0) texts.Add(c); }
                if (c.Controls.Count > 0) AuditNode(c, root, ref ctrl, ovf, clp, ovl, fonts, depth + 1);
            }
            // 同一父容器里的文字控件两两求交（只算文字类，避免把卡片/背景当作压盖）
            for (int i = 0; i < texts.Count; i++)
            {
                for (int k = i + 1; k < texts.Count; k++)
                {
                    Control a = texts[i], b2 = texts[k];
                    if (a.Width <= 0 || b2.Width <= 0) continue;
                    Rectangle ra = a.Bounds, rb = b2.Bounds;
                    Rectangle ri = Rectangle.Intersect(ra, rb);
                    if (ri.Width > 0 && ri.Height > 0 && ri.Width * ri.Height > 60)
                        ovl.Add(a.Parent.Name + "/" + Short(a.Text) + " x " + Short(b2.Text) + " area=" + (ri.Width * ri.Height));
                }
            }
        }

        string Short(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 22 ? s.Substring(0, 22) : s;
        }

        // ================= v7.9 自测：音效 / 投递渲染 =================
        void SfxSelftest()
        {
            DumpTest("_sfx_test.txt", Sfx.SelfTest(BASE_DIR));
            Application.Exit();
        }

        // ================= v7.9 自测：把一次「拖放投递」直接喂给处理函数 =================
        // OLE 的拖拽没法在命令行里模拟，但分流主链路（OnDragEnterAny → OnDropAny → RouteDropped）可以直接调用，
        // 这样自测能覆盖「遮罩浮起 → 切页 → WmLoadFile 载入 → 轻提示 → 无效项报错」全过程。
        void DropSim()
        {
            StringBuilder r = new StringBuilder();
            try
            {
                r.AppendLine("== v7.9 投递分流自测 ==");
                string img = Path.Combine(BASE_DIR, "bg.jpg");
                if (!File.Exists(img)) img = Path.Combine(BASE_DIR, "_drop.png");
                r.AppendLine("input=" + Path.GetFileName(img) + " exists=" + File.Exists(img));
                DataObject dob = new DataObject(DataFormats.FileDrop, new string[] { img });
                DragEventArgs de = new DragEventArgs(dob, 1, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
                OnDragEnterAny(this, de);
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
                Application.DoEvents();
                r.AppendLine("drag_enter effect=" + de.Effect + " mask=" + (dropMask.Visible ? "shown" : "hidden")
                    + " preview=" + Path.GetFileName(dropPreview));
                OnDropAny(this, de);
                Application.DoEvents();
                System.Threading.Thread.Sleep(700);
                Application.DoEvents();
                r.AppendLine("after_drop mask=" + (dropMask.Visible ? "shown" : "hidden")
                    + " wm_page=" + (pageWatermark.Visible ? "shown" : "hidden"));
                r.AppendLine("wm_image=" + (wmPreview.Image == null ? "none" : wmPreview.Image.Width + "x" + wmPreview.Image.Height)
                    + " wm_file_label=" + lblWmFile.Text);
                Panel tc = null;
                for (int i = 0; i < this.Controls.Count; i++)
                {
                    Control c = this.Controls[i];
                    if (c is Panel && c.Tag is object[]) { tc = (Panel)c; break; }
                }
                r.AppendLine("toast=" + (tc == null ? "missing" : ("shown " + tc.Width + "x" + tc.Height + " at " + tc.Left + "," + tc.Top)));
                // 无效项（本地不存在）→ 必须走 Error 分支且不抛
                DataObject bad = new DataObject(DataFormats.FileDrop, new string[] { Path.Combine(BASE_DIR, "不存在的文件_xyz.mp4") });
                DragEventArgs de2 = new DragEventArgs(bad, 1, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
                OnDropAny(this, de2);
                Application.DoEvents();
                r.AppendLine("bad_drop=handled");
                r.AppendLine("sfx=" + (Sfx.On ? "on" : "off"));
            }
            catch (Exception ex) { r.AppendLine("EX " + ex); }
            DumpTest("_drop_sim.txt", r.ToString());
            Application.Exit();
        }

        // v8.3：开发用——把 9 个页面逐一切出来各截一张真图，便于逐页体检排版与字距
        void AtDump()
        {
            StringBuilder sb = new StringBuilder();
            Panel pg = pageValue;
            HideAllPages(pg);
            pg.Visible = true;
            pg.Dock = DockStyle.Fill;
            pg.Bounds = content.ClientRectangle;
            Application.DoEvents();
            DumpKids(pg, 0, sb);
            System.IO.File.WriteAllText("D:\\myq\\A1\\_at.txt", sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        // 开发通道：在视频页上做"整树 dump + 逐点深挖 + 列表状态"，用来抓自绘/系统层留下的浅色条
        void HitScan()
        {
            StringBuilder sb = new StringBuilder();
            Panel pg = pageWatermark;
            HideAllPages(pg);
            pg.Visible = true;
            pg.Dock = DockStyle.Fill;
            pg.Bounds = content.ClientRectangle;
            Application.DoEvents();
            sb.AppendLine("== tree ==");
            DumpKids(pg, 0, sb);
            sb.AppendLine("== intersect scan (all controls, incl. hidden/disabled) ==");
            Rectangle probe = new Rectangle(460, 225, 320, 60);
            ProbeRect(pg, pg, probe, sb);
            sb.AppendLine("== dark lists ==");
            DumpLists(pg, sb);
            sb.AppendLine("== deep hit (x sweep) ==");
            int[] ys = new int[] { 240, 245, 250, 255, 262, 275, 282 };
            foreach (int y0 in ys)
            {
                sb.AppendLine("-- y=" + y0 + " --");
                for (int x = 470; x <= 740; x += 2)
                {
                    Control c = DeepHit(pg, new Point(x, y0));
                    String info = (c == null) ? "(none)" : (c.GetType().Name + " b=" + c.Bounds + " w=" + c.Width
                        + " bg=" + c.BackColor.R + "," + c.BackColor.G + "," + c.BackColor.B
                        + " vis=" + c.Visible + " txt=" + (c.Text == null ? "" : (c.Text.Length > 10 ? c.Text.Substring(0, 10) : c.Text)));
                    sb.AppendLine("x=" + x + " " + info);
                }
            }
            System.IO.File.WriteAllText("D:\\myq\\A1\\_hit.txt", sb.ToString(), Encoding.UTF8);
            Application.Exit();
        }

        void DumpLists(Control parent, StringBuilder sb)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c is ListView)
                {
                    ListView lv = (ListView)c;
                    sb.AppendLine("ListView b=" + lv.Bounds + " view=" + lv.View + " items=" + lv.Items.Count
                        + " client=" + lv.ClientSize + " owner=" + lv.OwnerDraw + " vis=" + lv.Visible);
                }
                if (c.Controls.Count > 0) DumpLists(c, sb);
            }
        }

        void ProbeRect(Control root, Control parent, Rectangle probe, StringBuilder sb)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                Point p0 = root.PointToClient(c.PointToScreen(Point.Empty));
                Rectangle rr = new Rectangle(p0, c.Size);
                if (rr.IntersectsWith(probe))
                    sb.AppendLine("HIT " + c.GetType().Name + " b=" + rr + " vis=" + c.Visible
                        + " en=" + c.Enabled + " bg=" + c.BackColor.R + "," + c.BackColor.G + "," + c.BackColor.B
                        + " txt=" + (c.Text == null ? "" : (c.Text.Length > 10 ? c.Text.Substring(0, 10) : c.Text)));
                if (c.Controls.Count > 0) ProbeRect(root, c, probe, sb);
            }
        }

        Control DeepHit(Control host, Point pt)
        {
            try
            {
                Control hit = host.GetChildAtPoint(pt);
                if (hit == null) return host;
                Point local = new Point(pt.X - hit.Left, pt.Y - hit.Top);
                Control deeper = DeepHit(hit, local);
                return deeper == null ? hit : deeper;
            }
            catch { return host; }
        }

        void DumpKids(Control parent, int depth, StringBuilder sb)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                Color bg = c.BackColor;
                sb.AppendLine(new string(' ', depth * 2) + c.GetType().Name + " b=" + c.Bounds + " bg=" + bg.R + "," + bg.G + "," + bg.B + " A=" + bg.A
                    + " vis=" + c.Visible + " txt=" + (c.Text == null ? "" : (c.Text.Length > 12 ? c.Text.Substring(0, 12) : c.Text)));
                if (depth < 3 && c.Controls.Count > 0) DumpKids(c, depth + 1, sb);
            }
        }

        // v9.2：把第三颗快捷预设打上"已选"光环，拍一张整窗图，用来核对光环的实际观感
        void PmarkShot()
        {
            StringBuilder r = new StringBuilder();
            try
            {
                this.TopMost = true;
                this.Activate();
                HideAllPages(pageTimer);
                pageTimer.Visible = true;
                pageTimer.BringToFront();
                try { int si = Array.IndexOf(SLOT, "timer"); if (si >= 0) SidebarSelect(si); } catch (Exception dg22356_1) { Diag.Swallow("MainForm.cs:11567", dg22356_1); }
                Application.DoEvents();
                System.Threading.Thread.Sleep(320);
                Application.DoEvents();
                if (pstBtns.Count > 1) MarkPreset(pstBtns[1]);
                Application.DoEvents();
                System.Threading.Thread.Sleep(360);
                Application.DoEvents();
                r.AppendLine("marks=" + pstBtns.Count + " shot=" + Path.Combine(BASE_DIR, "_pmark.png"));
                GrabWindowPrint(Path.Combine(BASE_DIR, "_pmark.png"), r);
                this.TopMost = false;
            }
            catch (Exception ex) { r.AppendLine("EX " + ex); }
            DumpTest("_pmark.txt", r.ToString());
            Application.Exit();
        }

        void ShotAllPages()
        {
            StringBuilder r = new StringBuilder();
            try
            {
                this.TopMost = true;
                this.Activate();
                Application.DoEvents();
                Panel[] ps = new Panel[] { pageHome, pageTimer, pageClean, pageSam, pageVideo, pageMusic, pageWatermark, pageValue, pageSys };
                string[] keys = new string[] { "home", "timer", "clean", "sam", "video", "music", "watermark", "value", "sys" };
                int from = 0, to = ps.Length;   // /shotfrom:3 只拍第 3 页往后，便于分批截图
                foreach (string a0 in Environment.GetCommandLineArgs())
                {
                    if (a0 == null) continue;
                    if (a0.StartsWith("/shotfrom:"))
                    {
                        string v = a0.Substring(10);
                        int p2 = v.IndexOf(':');
                        int f2 = 0, t2 = 99;
                        int dot = v.IndexOf('-');
                        if (dot < 0)
                        {
                            if (int.TryParse(v, out f2)) { from = f2; to = f2 + 1; }
                        }
                        else
                        {
                            if (int.TryParse(v.Substring(0, dot), out f2)) from = f2;
                            if (int.TryParse(v.Substring(dot + 1), out t2)) to = t2 + 1;
                        }
                    }
                    if (a0.StartsWith("/shotdelay:"))
                    {
                        int ws = 0;
                        if (int.TryParse(a0.Substring(11), out ws) && ws > 0)
                        {
                            // v14.5：截前空转一会儿，让首页的 2 秒采样攒够点，迷你走势才有形
                            for (int t = 0; t < ws * 10; t++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
                        }
                    }
                }
                if (from < 0) from = 0;
                if (to > ps.Length) to = ps.Length;
                for (int i = from; i < to; i++)
                {
                    if (ps[i] == null) continue;
                    try
                    {
                        HideAllPages(ps[i]);
                        ps[i].Visible = true;
                        ps[i].BringToFront();
                        // v9.1：截图前把侧栏选中态同步到当前页，实拍图里"哪一页被选中"才对得上
                        try
                        {
                            string kk = keys[i];
                            if (kk == "watermark") kk = "wm";
                            if (kk == "timer") kk = "power";    // v14.3：定时页在 SLOT 里的 key 是 power
                            if (kk == "value") kk = "val";
                            int si = Array.IndexOf(SLOT, kk);
                            if (si >= 0) SidebarSelect(si);
                        }
                        catch (Exception dg22433_1) { Diag.Swallow("MainForm.cs:11644", dg22433_1); }
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(400);
                        Application.DoEvents();
                        // 截图窗口偶尔会被系统/其他窗口挤到最小化，每页都先拉回正常态
                        try
                        {
                            this.WindowState = FormWindowState.Normal;
                            this.ShowInTaskbar = true;
                            this.TopMost = true;
                            this.Activate();
                            this.BringToFront();
                        }
                        catch (Exception dg22446_1) { Diag.Swallow("MainForm.cs:11657", dg22446_1); }
                        Application.DoEvents();
                        for (int w = 0; w < 40; w++)
                        {
                            if (ps[i].Dock == DockStyle.Fill && ps[i].Width > 100) break;
                            System.Threading.Thread.Sleep(60);
                            Application.DoEvents();
                        }
                        Application.DoEvents();
                        string shotPath = Path.Combine(BASE_DIR, "_page_" + keys[i] + ".png");
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            // 窗口被挤到最小化时，先拉回正常态再拍；拍出来是黑的就重试
                            try { this.WindowState = FormWindowState.Normal; this.Activate(); this.BringToFront(); } catch (Exception dg22459_1) { Diag.Swallow("MainForm.cs:11670", dg22459_1); }
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(280);
                            Application.DoEvents();
                            for (int fz = 0; fz < 20 && mainArea.ClientSize.Width < 400; fz++)
                            {
                                try { this.WindowState = FormWindowState.Normal; this.Activate(); } catch (Exception dg22465_1) { Diag.Swallow("MainForm.cs:11676", dg22465_1); }
                                Application.DoEvents();
                                System.Threading.Thread.Sleep(100);
                                Application.DoEvents();
                            }
                            ps[i].Dock = DockStyle.Fill;
                            try { ps[i].Bounds = content.ClientRectangle; } catch (Exception dg22471_1) { Diag.Swallow("MainForm.cs:11682", dg22471_1); }
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(120);
                            Application.DoEvents();
                            ps[i].Refresh();
                            Application.DoEvents();
                            GrabWindowPrint(shotPath, r);
                            long sz = 0;
                            try { FileInfo fi = new FileInfo(shotPath); if (fi.Exists) sz = fi.Length; } catch (Exception dg22479_1) { Diag.Swallow("MainForm.cs:11690", dg22479_1); }
                            r.AppendLine("shot " + keys[i] + " try" + attempt + " " + ps[i].Width + "x" + ps[i].Height
                                + " form=" + this.ClientSize.Width + "x" + this.ClientSize.Height + " bytes=" + sz);
                            if (sz > 120000 && ps[i].Width > 400 && ps[i].Height > 400) break;
                        }
                    }
                    catch (Exception ex1) { r.AppendLine("EX " + keys[i] + " " + ex1.Message); }
                }
                HideAllPages(pageTimer);
                pageTimer.Visible = true;
                this.TopMost = false;
            }
            catch (Exception ex) { r.AppendLine("EX " + ex); }
            DumpTest("_page_shot.txt", r.ToString());
            Application.Exit();
        }

        void DropShot()
        {
            StringBuilder r = new StringBuilder();
            try
            {
                r.AppendLine("== v7.9 投递舱渲染（真实屏幕抠图，不是 DrawToBitmap）==");
                this.TopMost = true;
                this.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                dropPreview = Path.Combine(BASE_DIR, "示例视频_投递.mp4");
                dropMask.Visible = true;
                dropMask.BringToFront();
                dropMask.Refresh();
                dropPhase = 3;
                Application.DoEvents();
                System.Threading.Thread.Sleep(500);
                Application.DoEvents();
                GrabWindow(Path.Combine(BASE_DIR, "_drop.png"), r);
                dropMask.Visible = false;
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                Ui2.Toast(this, T("已投递到「去水印」：示例视频_投递.mp4", "Dropped to Watermark: sample.mp4"), Ui2.ACCENT);
                Application.DoEvents();
                System.Threading.Thread.Sleep(600);
                Application.DoEvents();
                GrabWindow(Path.Combine(BASE_DIR, "_toast.png"), r);
                this.TopMost = false;
                r.AppendLine("drop_port=" + dropPort.Left + "," + dropPort.Top + " " + dropPort.Width + "x" + dropPort.Height);
                r.AppendLine("sfx=" + (Sfx.On ? "on" : "off"));
            }
            catch (Exception ex) { r.AppendLine("EX " + ex); }
            DumpTest("_drop_shot.txt", r.ToString());
            Application.Exit();
        }

        // 真实屏幕抠图：把窗口在自己屏幕位置上的像素抓下来（DrawToBitmap 抓不到运行时新增的浮层）
        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint nFlags);

        // v8.3：开发用——不抓屏幕，直接让窗口把自己画进内存位图。
        // 抓屏幕会被别的全屏/置顶窗口盖住（上一版就拍到过别的程序），PrintWindow 不受遮挡影响。
        void GrabWindowPrint(string path, StringBuilder log)
        {
            try
            {
                int w = this.Width, h = this.Height;
                if (w < 100 || h < 100) { log.AppendLine("print skip " + w + "x" + h); return; }
                using (Bitmap bmp = new Bitmap(w, h))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        // v17.6：PrintWindow 对 owner-draw 的 Button 会把系统主题外观再画一遍，
                        // 截图里就成了一颗白按钮（真实屏幕上其实是深色胶囊）。改为先抓真实屏幕，
                        // 抓不到再退回 PrintWindow。
                        bool ok = false;
                        try
                        {
                            Rectangle sc = this.Bounds;
                            g.CopyFromScreen(sc.Left, sc.Top, 0, 0, new Size(w, h));
                            ok = true;
                        }
                        catch { ok = false; }
                        if (!ok)
                        {
                            IntPtr hdc = g.GetHdc();
                            try { ok = PrintWindow(this.Handle, hdc, 2); }
                            finally { g.ReleaseHdc(hdc); }
                            if (!ok)
                            {
                                IntPtr hdc2 = g.GetHdc();
                                try { ok = PrintWindow(this.Handle, hdc2, 0); }
                                finally { g.ReleaseHdc(hdc2); }
                            }
                        }
                    }
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                log.AppendLine(Path.GetFileName(path) + "=" + new FileInfo(path).Length + "B print");
            }
            catch (Exception ex) { log.AppendLine("PRINT EX " + ex.Message); }
        }

        void GrabWindow(string path, StringBuilder log)
        {
            Rectangle rc = this.Bounds;
            using (Bitmap bmp = new Bitmap(rc.Width, rc.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            log.AppendLine(Path.GetFileName(path) + "=" + new FileInfo(path).Length + "B  rect=" + rc.Left + "," + rc.Top + " " + rc.Width + "x" + rc.Height);
        }

        // v8.0 自测：指令舱真实截屏（空态 / 意图命中 / 空结果三张）
        void PalShot()
        {
            StringBuilder r = new StringBuilder();
            r.AppendLine("== v8.0 指令舱渲染（真实屏幕抠图，不是 DrawToBitmap）==");
            PaletteForm pf = null;
            try
            {
                pf = new PaletteForm(BuildPaletteItems(), langEn);
                pf.BuildIntent = PaletteIntent;
                pf.RecentProvider = PaletteRecent;
                pf.AfterRun = PaletteRemember;
                pf.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(260);
                Application.DoEvents();
                pf.SetQueryForTest("");
                pf.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(140);
                GrabRegion(Path.Combine(BASE_DIR, "_palette.png"), pf.Bounds, r);
                pf.SetQueryForTest("关机 30");
                pf.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(140);
                GrabRegion(Path.Combine(BASE_DIR, "_palette_intent.png"), pf.Bounds, r);
                pf.SetQueryForTest("手机遥控");
                pf.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(140);
                GrabRegion(Path.Combine(BASE_DIR, "_palette_phone.png"), pf.Bounds, r);
                r.AppendLine("items_phone=" + pf.ItemCount + " cnt=" + pf.CountText);
                pf.SetQueryForTest("没有这个功能");
                pf.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(140);
                GrabRegion(Path.Combine(BASE_DIR, "_palette_empty.png"), pf.Bounds, r);
                r.AppendLine("geom " + pf.DumpGeometry());
                r.AppendLine("items_empty=" + pf.ItemCount);
                pf.SetQueryForTest("关机 30");
                r.AppendLine("items_intent=" + pf.ItemCount + " cnt=" + pf.CountText);
                pf.Close();
            }
            catch (Exception ex) { r.AppendLine("EX " + ex); }
            try { if (pf != null) pf.Dispose(); } catch (Exception dg22637_1) { Diag.Swallow("MainForm.cs:11848", dg22637_1); }
            DumpTest("_pal_shot.txt", r.ToString());
            Application.Exit();
        }

        void GrabRegion(string path, Rectangle rc, StringBuilder log)
        {
            using (Bitmap bmp = new Bitmap(rc.Width, rc.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            log.AppendLine(Path.GetFileName(path) + "=" + new FileInfo(path).Length + "B  rect=" + rc.Left + "," + rc.Top + " " + rc.Width + "x" + rc.Height);
        }

        // v7.7 自测：手机工具箱窗体几何（不碰设备）
        void AdbUiDump()
        {
            StringBuilder sb = new StringBuilder();
            AdbForm f = null;
            try
            {
                f = new AdbForm(langEn, BASE_DIR);
                sb.Append("AdbForm ClientSize=" + f.ClientSize.Width + "x" + f.ClientSize.Height + "\r\n");
                List<string> lines = new List<string>();
                foreach (Control c in f.Controls)
                {
                    string nm = c.Name != null && c.Name.Length > 0 ? c.Name : (c.Text != null && c.Text.Length > 0 ? c.Text : c.GetType().Name);
                    if (c is Button || c is TextBox || c is Label || c is ComboBox)
                        lines.Add("[" + nm + "] " + c.Left + "," + c.Top + " " + c.Width + "x" + c.Height + " [" + c.GetType().Name + "]");
                }
                for (int i = 0; i < lines.Count; i++) sb.Append(lines[i] + "\r\n");
                // 重叠检查：同层可见控件两两相交
                int bad = 0;
                foreach (Control c in f.Controls)
                {
                    foreach (Control d in f.Controls)
                    {
                        if (c == d || !c.Visible || !d.Visible) continue;
                        Rectangle rc = c.Bounds, rd = d.Bounds;
                        if (!rc.IntersectsWith(rd)) continue;
                        // 允许 Label 与别的控件轻微相贴（TextBox 边框），只报明显相交
                        int area = Rectangle.Intersect(rc, rd).Width * Rectangle.Intersect(rc, rd).Height;
                        if (area > 40) { bad++; sb.Append("OVERLAP " + c.GetType().Name + "(" + c.Text + ") vs " + d.GetType().Name + "(" + d.Text + ") area=" + area + "\r\n"); }
                    }
                }
                sb.Append("overlap=" + (bad / 2) + " (成对计数，除以 2)\r\n");
            }
            catch (Exception e) { sb.Append("EX " + e.Message + "\r\n"); }
            try { if (f != null) f.Dispose(); } catch (Exception dg22687_1) { Diag.Swallow("MainForm.cs:11898", dg22687_1); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_adbui.txt"), sb.ToString(), Encoding.UTF8); } catch (Exception dg22688_1) { Diag.Swallow("MainForm.cs:11899", dg22688_1); }
            try { Application.Exit(); } catch (Exception dg22689_1) { Diag.Swallow("MainForm.cs:11900", dg22689_1); }
        }

        // v7.7 自测：每日一图只取图不换壁纸（只读验证网络与解析）
        void WpSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.Append("当前壁纸=" + WpCore.Current() + "\r\n");
                string dir = Path.Combine(BASE_DIR, "壁纸");
                WpCore.BackupOnce(dir);
                string path, title, err;
                bool ok = WpCore.DownloadDaily(dir, out path, out title, out err);
                sb.Append("取图=" + (ok ? "OK" : "FAIL") + " path=" + path + " err=" + err + "\r\n");
                sb.Append("标题=" + title + "\r\n");
                if (ok) { try { sb.Append("字节=" + new FileInfo(path).Length + "\r\n"); } catch (Exception dg22705_1) { Diag.Swallow("MainForm.cs:11916", dg22705_1); } }
                string bk = Path.Combine(dir, "原壁纸.txt");
                sb.Append("备份文件=" + (File.Exists(bk) ? File.ReadAllText(bk, Encoding.UTF8).Trim() : "（无）") + "\r\n");
                sb.Append("（本测试只取图，不改壁纸）\r\n");
            }
            catch (Exception e) { sb.Append("EX " + e.Message + "\r\n"); }
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_wp_test.txt"), sb.ToString(), Encoding.UTF8); } catch (Exception dg22711_1) { Diag.Swallow("MainForm.cs:11922", dg22711_1); }
            try { Application.Exit(); } catch (Exception dg22712_1) { Diag.Swallow("MainForm.cs:11923", dg22712_1); }
        }

        void AdbSelfTest()
        {
            DumpTest("_adb_test.txt", AdbCore.SelfTestText(BASE_DIR));
            Application.Exit();
        }

        // ---------- v7.3 自测 ----------
        void UpnpSelfTest()
        {
            DumpTest("_upnp_test.txt", UpnpCore.SelfTestText());
            Application.Exit();
        }

        void LatencySelfTest()
        {
            DumpTest("_latency_test.txt", LatencyCore.SelfTestText());
            Application.Exit();
        }

        void WifiSelfTest()
        {
            DumpTest("_wifi_test.txt", WifiCore.SelfTestText());
            Application.Exit();
        }

        void V73UiDump()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("VER=" + UiKit.VER);
            try
            {
                UpnpForm a = new UpnpForm(langEn, BASE_DIR);
                sb.AppendLine("UpnpForm client=" + a.ClientSize.Width + "x" + a.ClientSize.Height + " controls=" + a.Controls.Count + " text=" + a.Text);
                a.Dispose();
                LatencyForm b = new LatencyForm(langEn, BASE_DIR);
                sb.AppendLine("LatencyForm client=" + b.ClientSize.Width + "x" + b.ClientSize.Height + " controls=" + b.Controls.Count + " text=" + b.Text);
                b.Dispose();
                WifiForm c = new WifiForm(langEn, BASE_DIR);
                sb.AppendLine("WifiForm client=" + c.ClientSize.Width + "x" + c.ClientSize.Height + " controls=" + c.Controls.Count + " text=" + c.Text);
                c.Dispose();
                sb.AppendLine("local_ip=" + UpnpCore.LocalIp());
                sb.AppendLine("latency_targets=" + LatencyCore.Presets().Count);
                sb.AppendLine("wifi_aps=" + WifiCore.Scan().Count);
                sb.AppendLine("palette_items=" + BuildPaletteItems().Count);
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
            DumpTest("_v73dump.txt", sb.ToString());
            Application.Exit();
        }

        void V72UiDump()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("VER=" + UiKit.VER);
            try
            {
                PortForm a = new PortForm(langEn, BASE_DIR);
                sb.AppendLine("PortForm client=" + a.ClientSize.Width + "x" + a.ClientSize.Height + " controls=" + a.Controls.Count + " text=" + a.Text);
                a.Dispose();
                ScreenRecForm b = new ScreenRecForm(langEn, BASE_DIR);
                sb.AppendLine("ScreenRecForm client=" + b.ClientSize.Width + "x" + b.ClientSize.Height + " controls=" + b.Controls.Count + " text=" + b.Text);
                b.Dispose();
                AudioToolForm c = new AudioToolForm(langEn, BASE_DIR);
                sb.AppendLine("AudioToolForm client=" + c.ClientSize.Width + "x" + c.ClientSize.Height + " controls=" + c.Controls.Count + " text=" + c.Text);
                c.Dispose();
                ProxyForm d = new ProxyForm(langEn, BASE_DIR);
                sb.AppendLine("ProxyForm client=" + d.ClientSize.Width + "x" + d.ClientSize.Height + " controls=" + d.Controls.Count + " text=" + d.Text);
                d.Dispose();
                AdbForm e = new AdbForm(langEn, BASE_DIR);
                sb.AppendLine("AdbForm client=" + e.ClientSize.Width + "x" + e.ClientSize.Height + " controls=" + e.Controls.Count + " text=" + e.Text);
                e.Dispose();
                sb.AppendLine("rec_encoder=" + ScreenRecCore.PickEncoder(BASE_DIR));
                sb.AppendLine("adb_path=" + (AdbCore.FindAdb(BASE_DIR).Length > 0 ? "ok" : "missing"));
                sb.AppendLine("ffmpeg=" + (VideoProCore.Ffmpeg(BASE_DIR).Length > 0 ? "ok" : "missing"));
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex.Message); }
            DumpTest("_v72dump.txt", sb.ToString());
            Application.Exit();
        }

        void ShowCheckup()
        {
            Form f = new Form();
            f.Text = T("一键体检 · 小工具箱", "Checkup · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(800, 600);
            f.MinimumSize = new Size(720, 520);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("一键体检", "One-Click Checkup");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 16);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("检查组件完整性 / Steam 状态 / 网络 / 磁盘 / WinRE / error.log，红灯项可一键修复",
                "Verifies components, Steam, network, disk, WinRE and error.log; failures can be repaired in one click");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.AutoSize = true;
            f.Controls.Add(sub);

            ListView lv = MakeDarkList(new string[] { T("检查项", "Item"), T("状态", "State"), T("详情", "Detail") },
                new int[] { 170, 70, 500 }, 20, 74, 760, 400);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = T("准备就绪，点「开始体检」", "Ready. Click Start.");
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Font = new Font("Microsoft YaHei UI", 9F);
            st.Location = new Point(20, 486);
            st.Size = new Size(500, 20);
            f.Controls.Add(st);

            System.Collections.Generic.List<string> fixable = new System.Collections.Generic.List<string>();

            Action scan = null;
            scan = delegate
            {
                try { f.Invoke(new MethodInvoker(delegate { lv.Items.Clear(); st.Text = T("正在体检…", "Checking…"); })); } catch { return; }
                System.Collections.Generic.List<object[]> rows = new System.Collections.Generic.List<object[]>();

                // 1 版本 / 主程序
                rows.Add(new object[] { T("主程序版本", "App version"), 0, "v" + UiKit.VER + " · " + FmtSize(new FileInfo(Application.ExecutablePath).Length), "" });

                // 2 内嵌资源
                string[] embNames = { "SamZip", "CleanZip", "MainExe" };
                Assembly asm = Assembly.GetExecutingAssembly();
                bool hasSam = false, hasClean = false;
                foreach (string rn in embNames)
                {
                    try { using (Stream rs = asm.GetManifestResourceStream(rn)) { if (rs != null) { if (rn == "SamZip") hasSam = true; if (rn == "CleanZip") hasClean = true; } } }
                    catch (Exception dg22853_1) { Diag.Swallow("MainForm.cs:12064", dg22853_1); }
                }
                rows.Add(new object[] { T("内嵌组件资源", "Embedded resources"), hasSam && hasClean ? 0 : 1,
                    (hasSam ? "SamZip ✓ " : "SamZip ✗ ") + (hasClean ? "CleanZip ✓" : "CleanZip ✗") + T("（精简版走在线下载通道）", " (slim build uses online channel)"), "" });

                // 3~6 四个组件
                string[] ck = { "sam", "clean", "video", "ffmpeg" };
                string[] cz = { "Steam 解锁组件", "C盘清理组件", "视频下载引擎", "FFmpeg 组件" };
                string[] ce = { "Steam unlocker", "C: cleaner", "Video engine", "FFmpeg" };
                for (int i = 0; i < ck.Length; i++)
                {
                    if (ck[i] == "ffmpeg")
                    {
                        string ff = Path.Combine(VideoToolDir(), "ffmpeg.exe");
                        bool hasFf = File.Exists(ff);
                        rows.Add(new object[] { T(cz[i], ce[i]), hasFf ? 0 : 2,
                            hasFf ? "ffmpeg.exe " + FmtSize(new FileInfo(ff).Length) : T("ffmpeg.exe 缺失（随视频组件一起部署）", "ffmpeg.exe missing (ships with video component)"),
                            hasFf ? "" : "video" });
                        continue;
                    }
                    string dir = DirOf(ck[i]);
                    int n = Directory.Exists(dir) ? CountFiles(dir) : -1;
                    int state = 0; string det;
                    if (n < 0) { state = 2; det = T("目录不存在", "folder missing"); }
                    else if (n == 0) { state = 2; det = T("目录为空", "folder empty"); }
                    else { det = n + T(" 个文件 · ", " files · ") + FmtSize(DirectorySizeQuick(dir)); }
                    rows.Add(new object[] { T(cz[i], ce[i]), state, det, state == 0 ? "" : ck[i] });
                }

                // 9 Steam
                int steamState = 0; string steamDet;
                string steamPath = Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null));
                bool steamRun = Process.GetProcessesByName("steam").Length > 0;
                int schema = 0;
                string statsDir = Path.Combine(GetSteamInstallPath(), "appcache\\stats");
                if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(statsDir))
                    try { schema = Directory.GetFiles(statsDir, "UserGameStatsSchema_*.bin").Length; } catch (Exception dg22889_1) { Diag.Swallow("MainForm.cs:12100", dg22889_1); }
                if (string.IsNullOrEmpty(steamPath)) { steamState = 1; steamDet = T("未检测到 Steam 安装", "Steam not found"); }
                else if (schema == 0) { steamState = 1; steamDet = T("已安装但未找到成就 schema（进一次游戏即可生成）", "installed, no schema yet (run a game once)"); }
                else { steamDet = T("游戏 ", "games ") + schema + T(" 款 · 客户端", " · client ") + (steamRun ? T("运行中", "running") : T("未运行", "not running")); }
                rows.Add(new object[] { "Steam", steamState, steamDet, "" });

                // 10 网络 / 更新源
                string upUrl = "";
                try { string p = Path.Combine(BASE_DIR, "update_url.txt"); if (File.Exists(p)) upUrl = File.ReadAllText(p).Trim(); } catch (Exception dg22897_1) { Diag.Swallow("MainForm.cs:12108", dg22897_1); }
                string e1 = "", e2 = "";
                bool net1 = HttpOk("https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/update.json", 8000, out e1);
                bool net2 = upUrl.Length > 4 && HttpOk(upUrl, 8000, out e2);
                string netErr = net1 ? "" : ("  [" + (e1.Length > 60 ? e1.Substring(0, 60) : e1) + "]");
                rows.Add(new object[] { T("网络 / 更新源", "Network / update"), net1 || net2 ? 0 : 2,
                    T("GitHub 清单 ", "manifest ") + (net1 ? "✓" : "✗") + T(" · 自建源 ", " · custom ") + (upUrl.Length > 4 ? (net2 ? "✓" : "✗") : "—") + netErr, "" });

                // 11 C 盘余量
                long free = 0, total = 0;
                try { DriveInfo d = new DriveInfo("C"); free = d.AvailableFreeSpace; total = d.TotalSize; } catch (Exception dg22907_1) { Diag.Swallow("MainForm.cs:12118", dg22907_1); }
                int freeGB = (int)(free / 1073741824L);
                rows.Add(new object[] { T("C 盘剩余空间", "C: free space"), freeGB >= 10 ? 0 : (freeGB >= 3 ? 1 : 2),
                    freeGB + " GB / " + (int)(total / 1073741824L) + " GB", "" });

                // 12 WinRE
                bool winre = false; string wrDet = T("无法判断（需要管理员权限）", "unknown (needs admin)");
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("reagentc.exe", "/info");
                    psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                    using (Process p = Process.Start(psi))
                    {
                        System.IO.StreamReader so = p.StandardOutput;
                        System.IO.StreamReader se = p.StandardError;
                        string o = so.ReadToEnd();
                        string ee = se.ReadToEnd();
                        p.WaitForExit(8000);
                        string all = (o + " " + ee);
                        if (all.IndexOf("拒绝访问", StringComparison.OrdinalIgnoreCase) >= 0 || all.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0
                            || all.IndexOf("需要提升", StringComparison.OrdinalIgnoreCase) >= 0)
                            wrDet = T("无法判断（需要管理员权限）", "unknown (needs admin)");
                        else if (all.IndexOf("已启用", StringComparison.OrdinalIgnoreCase) >= 0 || all.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0) { winre = true; wrDet = T("恢复环境已启用", "recovery enabled"); }
                        else wrDet = T("恢复环境未启用（重装前建议先开）", "recovery not enabled (enable before reinstall)");
                    }
                }
                catch (Exception dg22933_1) { Diag.Swallow("MainForm.cs:12144", dg22933_1); }
                rows.Add(new object[] { "WinRE", winre ? 0 : 1, wrDet, "" });

                // 13 error.log
                string errLog = Path.Combine(BASE_DIR, "error.log");
                bool hasErr = File.Exists(errLog);
                string errDet = T("无异常日志", "no error log");
                if (hasErr)
                {
                    try { errDet = T("存在 error.log（", "error.log present (") + FmtSize(new FileInfo(errLog).Length) + "）"; } catch { errDet = "error.log"; }
                }
                rows.Add(new object[] { "error.log", hasErr ? 1 : 0, errDet, hasErr ? "errorlog" : "" });

                // 14 启动项数量（只看用户侧 Run / 启动文件夹，系统自启服务不算“拖慢开机”的主因）
                int bootUser = 0, bootSvc = 0;
                try
                {
                    foreach (StartupEntry en in CollectStartupItems())
                    {
                        if (en.Kind == "SVC") bootSvc++; else bootUser++;
                    }
                }
                catch (Exception dg22955_1) { Diag.Swallow("MainForm.cs:12166", dg22955_1); }
                rows.Add(new object[] { T("开机启动项", "Startup entries"), bootUser > 15 ? 1 : 0,
                    bootUser + T(" 项用户自启", " user items") + T(" + ", " + ") + bootSvc + T(" 项自启服务", " auto services")
                    + (bootUser > 15 ? T("（超过 15 项会明显拖慢开机）", " (>15 slows boot)") : ""), "" });

                fixable.Clear();
                int fails = 0, warns = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    object[] r = rows[i];
                    int state = (int)r[1];
                    string label = state == 0 ? T("通过", "OK") : (state == 1 ? T("注意", "WARN") : T("失败", "FAIL"));
                    if (state == 2) fails++; else if (state == 1) warns++;
                    string fix = (string)r[3];
                    if (fix.Length > 0) fixable.Add(fix);
                    string[] item = new string[] { (string)r[0], label, (string)r[2] };
                    int si = state;
                    string fi = fix;
                    try
                    {
                        f.Invoke(new MethodInvoker(delegate
                        {
                            ListViewItem it = new ListViewItem(item);
                            it.ForeColor = si == 0 ? UiKit.A(120, 220, 160) : (si == 1 ? UiKit.A(250, 204, 120) : UiKit.A(255, 120, 120));
                            if (fi.Length > 0) it.Tag = fi;
                            lv.Items.Add(it);
                            lv.Invalidate();
                        }));
                    }
                    catch { return; }
                }
                try
                {
                    f.Invoke(new MethodInvoker(delegate
                    {
                        st.Text = (fails == 0 ? T("体检完成：", "Done: ") : T("体检完成：", "Done: "))
                            + fails + T(" 项失败 · ", " fail · ") + warns + T(" 项注意", " warn")
                            + (fixable.Count > 0 ? T("，点「一键修复」处理", ", click Repair")
                            : T("，一切正常", ", all good"));
                        st.ForeColor = fails > 0 ? UiKit.A(255, 120, 120) : UiKit.A(120, 220, 160);
                    }));
                }
                catch (Exception dg22997_1) { Diag.Swallow("MainForm.cs:12208", dg22997_1); }
                if (checkupAutoDump)
                {
                    StringBuilder sx = new StringBuilder();
                    try
                    {
                        foreach (object[] r in rows) sx.AppendLine((string)r[0] + " | " + (int)r[1] + " | " + (string)r[2]);
                        File.WriteAllText(Path.Combine(BASE_DIR, "_checkup_test.txt"), sx.ToString(), Encoding.UTF8);
                    }
                    catch (Exception exd) { try { File.WriteAllText(Path.Combine(BASE_DIR, "_checkup_test.txt"), "EX " + exd.Message, Encoding.UTF8); } catch (Exception dg23006_1) { Diag.Swallow("MainForm.cs:12217", dg23006_1); } }
                    try { f.Invoke(new MethodInvoker(delegate { f.Close(); })); } catch (Exception dg23007_1) { Diag.Swallow("MainForm.cs:12218", dg23007_1); }
                }
            };

            Button btnScan = MakeBtn(T("开始体检", "Start Checkup"), UiKit.A(249, 115, 22), Color.White, 20, 516, 120, 36);
            btnScan.Click += (s, e) =>
            {
                btnScan.Enabled = false;
                System.Threading.Thread th = new System.Threading.Thread(delegate ()
                {
                    scan();
                    try { f.Invoke(new MethodInvoker(delegate { btnScan.Enabled = true; })); } catch (Exception dg23018_1) { Diag.Swallow("MainForm.cs:12229", dg23018_1); }
                });
                th.IsBackground = true;
                th.Start();
            };
            f.Controls.Add(btnScan);

            Button btnFix = MakeBtn(T("一键修复", "Repair"), UiKit.A(34, 197, 94), Color.White, 150, 516, 120, 36);
            btnFix.Click += (s, e) =>
            {
                if (fixable.Count == 0)
                {
                    MessageBox.Show(f, T("没有需要修复的项目。", "Nothing to repair."), "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                System.Collections.Generic.List<string> todo = new System.Collections.Generic.List<string>();
                foreach (string k in fixable) if (!todo.Contains(k)) todo.Add(k);
                string names = "";
                foreach (string k in todo)
                {
                    if (k == "errorlog") names += T("error.log 归档；", "archive error.log; ");
                    else names += DirOf(k) + "；";
                }
                if (MessageBox.Show(f, T("将修复以下项目：\n\n", "Will repair:\n\n") + names + T("\n\n缺失的组件会重新部署（内嵌→同目录 zip→在线下载）。继续？",
                        "\n\nMissing components get redeployed (embedded → local zip → online). Continue?"),
                        "小工具箱", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                btnFix.Enabled = false;
                System.Threading.Thread th = new System.Threading.Thread(delegate ()
                {
                    foreach (string k in todo)
                    {
                        if (k == "errorlog")
                        {
                            try
                            {
                                string src = Path.Combine(BASE_DIR, "error.log");
                                if (File.Exists(src)) File.Move(src, Path.Combine(BASE_DIR, "error_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"));
                            }
                            catch (Exception dg23056_1) { Diag.Swallow("MainForm.cs:12267", dg23056_1); }
                            continue;
                        }
                        string res = ResNameOf(k);
                        if (res.Length == 0) continue;
                        bool ok = UnpackResource(res, DirOf(k), k);
                        ClearSchemaCache();
                    }
                    scan();
                    try { f.Invoke(new MethodInvoker(delegate { btnFix.Enabled = true; })); } catch (Exception dg23065_1) { Diag.Swallow("MainForm.cs:12276", dg23065_1); }
                });
                th.IsBackground = true;
                th.Start();
            };
            f.Controls.Add(btnFix);

            Button btnExp = MakeBtn(T("导出报告", "Export"), C_BUBBLE_ALT(), Color.White, 280, 516, 120, 36);
            btnExp.Click += (s, e) =>
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine(T("小工具箱 v", "Mini Toolbox v") + UiKit.VER + T(" 体检报告  ", " checkup  ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine(new string('-', 60));
                foreach (ListViewItem it in lv.Items)
                {
                    sb.AppendLine(it.SubItems[0].Text + "  |  " + it.SubItems[1].Text + "  |  " + it.SubItems[2].Text);
                }
                try
                {
                    string p = Path.Combine(BASE_DIR, "体检报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(f, T("已导出：\n", "Saved:\n") + p, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(f, ex.Message); }
            };
            f.Controls.Add(btnExp);

            // v10.0：HTML 版报告——同样的体检结果，直接双击能在浏览器里看，也能发群里
            Button btnExpHtml = MakeBtn(T("导出网页", "Web report"), UiKit.A(56, 132, 255), Color.White, 410, 516, 120, 36);
            btnExpHtml.Click += (s, e) =>
            {
                try
                {
                    StringBuilder hb = new StringBuilder();
                    hb.Append("<!DOCTYPE html><html lang=\"zh\"><head><meta charset=\"utf-8\"><title>小工具箱 体检报告</title>");
                    hb.Append("<style>body{margin:0;background:#12161c;color:#dbe3ee;font-family:'Microsoft YaHei UI',system-ui,sans-serif}");
                    hb.Append(".w{max-width:820px;margin:0 auto;padding:34px 28px}h1{font-size:20px;margin:0 0 6px}");
                    hb.Append(".s{color:#8b98ab;font-size:12.5px;margin-bottom:22px}");
                    hb.Append(".c{background:#1a1f27;border:1px solid rgba(255,255,255,.07);border-radius:12px;overflow:hidden}");
                    hb.Append("table{width:100%;border-collapse:collapse;font-size:13px}");
                    hb.Append("th{text-align:left;padding:11px 14px;color:#9fb0c6;font-weight:600;background:#20262f;font-size:12px}");
                    hb.Append("td{padding:10px 14px;border-top:1px solid rgba(255,255,255,.05)}");
                    hb.Append(".g{color:#22c55e}.b{color:#ef4444}.y{color:#f59e0b}");
                    hb.Append("footer{margin-top:18px;color:#6b7789;font-size:11.5px}</style></head><body><div class=\"w\">");
                    hb.Append("<h1>小工具箱 体检报告</h1><div class=\"s\">v" + UiKit.VER + " &nbsp;·&nbsp; " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
                    hb.Append("<div class=\"c\"><table><tr><th>检查项</th><th>结果</th><th>说明</th></tr>");
                    for (int i = 0; i < lv.Items.Count; i++)
                    {
                        ListViewItem it2 = lv.Items[i];
                        string a0 = it2.SubItems.Count > 0 ? it2.SubItems[0].Text : "";
                        string b0 = it2.SubItems.Count > 1 ? it2.SubItems[1].Text : "";
                        string c0 = it2.SubItems.Count > 2 ? it2.SubItems[2].Text : "";
                        string cls = b0.IndexOf("正常") >= 0 || b0.IndexOf("OK") >= 0 || b0.IndexOf("通过") >= 0 ? "g"
                                   : (b0.IndexOf("异常") >= 0 || b0.IndexOf("失败") >= 0 || b0.IndexOf("缺失") >= 0 ? "b" : "y");
                        hb.Append("<tr><td>" + a0.Replace("<", "&lt;") + "</td><td class=\"" + cls + "\">" + b0.Replace("<", "&lt;") + "</td><td>" + c0.Replace("<", "&lt;") + "</td></tr>");
                    }
                    hb.Append("</table></div><footer>本报告由小工具箱生成，只读检查项不写入任何系统设置。</footer></div></body></html>");
                    string hp = Path.Combine(BASE_DIR, "体检报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
                    File.WriteAllText(hp, hb.ToString(), new UTF8Encoding(false));
                    try { Process.Start(new ProcessStartInfo(hp) { UseShellExecute = true }); } catch (Exception dg23124_1) { Diag.Swallow("MainForm.cs:12335", dg23124_1); }
                    MessageBox.Show(f, T("已导出网页报告：\n", "Saved web report:\n") + hp, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(f, ex.Message); }
            };
            f.Controls.Add(btnExpHtml);

            Button btnClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 660, 516, 100, 36);
            btnClose.Click += (s, e) => f.Close();
            f.Controls.Add(btnClose);

            f.Shown += delegate { btnScan.PerformClick(); };
            f.ShowDialog(this);
        }

        long DirectorySizeQuick(string dir)
        {
            try
            {
                long s = 0; int cnt = 0;
                foreach (FileInfo fi in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    s += fi.Length; if (++cnt > 8000) break;
                }
                return s;
            }
            catch { return 0; }
        }

        void ClearSchemaCache() { try { schemaCacheTime = DateTime.MinValue; } catch (Exception dg23153_1) { Diag.Swallow("MainForm.cs:12364", dg23153_1); } }

        // ================= v6.7 开机启动管理 =================
        void ShowStartupManager()
        {
            Form f = new Form();
            f.Text = T("开机启动管理 · 小工具箱", "Startup Manager · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(900, 580);
            f.MinimumSize = new Size(780, 480);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("开机启动管理", "Startup Manager");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 16);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("注册表 Run / 启动文件夹 / 自启服务。禁用时先备份，随时可以恢复",
                "Registry Run / Startup folder / auto services. Disabled items are backed up and restorable");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.AutoSize = true;
            f.Controls.Add(sub);

            ListView lv = MakeDarkList(new string[] { T("名称", "Name"), T("来源", "Source"), T("状态", "State"), T("命令 / 路径", "Command") },
                new int[] { 190, 120, 60, 460 }, 20, 74, 860, 420);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = T("正在读取…", "Loading…");
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 506);
            st.Size = new Size(500, 20);
            f.Controls.Add(st);

            System.Collections.Generic.List<StartupEntry> data = new System.Collections.Generic.List<StartupEntry>();

            Action reload = null;
            reload = delegate
            {
                try { f.Invoke(new MethodInvoker(delegate { lv.Items.Clear(); st.Text = T("正在读取…", "Loading…"); })); } catch { return; }
                data = CollectStartupItems();
                System.Collections.Generic.List<string> bak = LoadBootBackup();
                try
                {
                    f.Invoke(new MethodInvoker(delegate
                    {
                        foreach (StartupEntry e in data)
                        {
                            ListViewItem it = new ListViewItem(new string[] { e.Name, e.Source, e.State, e.Command });
                            it.Tag = e;
                            it.ForeColor = e.State.IndexOf("启用", StringComparison.Ordinal) >= 0 || e.State == "Auto"
                                ? UiKit.A(210, 220, 235) : UiKit.A(130, 140, 155);
                            lv.Items.Add(it);
                        }
                        lv.Invalidate();
                        st.Text = T("共 ", "Total ") + data.Count + T(" 项 · 已禁用的可在「恢复禁用」里找回 · 自启服务需要管理员权限",
                            " items · disabled items restorable · services need admin");
                        st.ForeColor = UiKit.A(150, 160, 176);
                    }));
                }
                catch (Exception dg23221_1) { Diag.Swallow("MainForm.cs:12432", dg23221_1); }
                if (bootAutoDump)
                {
                    try
                    {
                        StringBuilder sx = new StringBuilder();
                        sx.AppendLine("items=" + data.Count);
                        foreach (StartupEntry en in data) sx.AppendLine(en.Kind + " | " + en.Source + " | " + en.Name);
                        File.WriteAllText(Path.Combine(BASE_DIR, "_boot_ui_test.txt"), sx.ToString(), Encoding.UTF8);
                    }
                    catch (Exception dg23231_1) { Diag.Swallow("MainForm.cs:12442", dg23231_1); }
                    try { f.Invoke(new MethodInvoker(delegate { f.Close(); })); } catch (Exception dg23232_1) { Diag.Swallow("MainForm.cs:12443", dg23232_1); }
                }
            };

            Button btnReload = MakeBtn(T("刷新", "Refresh"), UiKit.A(59, 130, 246), Color.White, 20, 534, 100, 34);
            btnReload.Click += (s, e) => reload();
            f.Controls.Add(btnReload);

            Button btnDisable = MakeBtn(T("禁用选中", "Disable"), UiKit.A(232, 82, 61), Color.White, 130, 534, 110, 34);
            btnDisable.Click += (s, e) =>
            {
                if (lv.SelectedItems.Count == 0) { MessageBox.Show(f, T("请先在列表里选中要禁用的项。", "Select an item first.")); return; }
                StartupEntry en = (StartupEntry)lv.SelectedItems[0].Tag;
                if (MessageBox.Show(f, T("禁用「", "Disable \"") + en.Name + T("」？\n\n", "\"?\n\n") + T("会先备份，之后可用「恢复禁用」还原。", "A backup is kept; you can restore it later."),
                        "小工具箱", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                string err = DisableStartup(en);
                if (err != null) MessageBox.Show(f, T("禁用失败：\n", "Failed:\n") + err, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                reload();
            };
            f.Controls.Add(btnDisable);

            Button btnRestore = MakeBtn(T("恢复禁用", "Restore"), UiKit.A(34, 197, 94), Color.White, 250, 534, 110, 34);
            btnRestore.Click += (s, e) =>
            {
                int n = RestoreStartupAll();
                MessageBox.Show(f, n > 0 ? T("已恢复 ", "Restored ") + n + T(" 项。", " item(s).") : T("没有需要恢复的项目。", "Nothing to restore."),
                    "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                reload();
            };
            f.Controls.Add(btnRestore);

            // v14.0：一键体检（失效路径 / 重复项 / 临时目录 / 伪装系统名）
            Button btnScan = MakeBtn(T("体检扫描", "Health scan"), UiKit.A(139, 92, 246), Color.White, 370, 534, 110, 34);
            btnScan.Click += (s, e) => ShowStartupCheckup(data);
            f.Controls.Add(btnScan);

            Button btnClose2 = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 780, 534, 100, 34);
            btnClose2.Click += (s, e) => f.Close();
            f.Controls.Add(btnClose2);

            f.Shown += delegate
            {
                System.Threading.Thread th = new System.Threading.Thread(delegate () { reload(); });
                th.IsBackground = true; th.Start();
            };
            f.ShowDialog(this);
        }

        // ============ v14.0 启动项体检 ============
        static string StartupTargetOf(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return "";
            string c = cmd.Trim();
            try
            {
                if (c.StartsWith("\""))
                {
                    int q = c.IndexOf('"', 1);
                    if (q > 1) return c.Substring(1, q - 1);
                }
                string[] toks = c.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < toks.Length; i++)
                {
                    string t = toks[i].Trim('"');
                    string lo = t.ToLowerInvariant();
                    if (lo.EndsWith(".exe") || lo.EndsWith(".bat") || lo.EndsWith(".cmd") || lo.EndsWith(".dll") || lo.EndsWith(".vbs"))
                        return t;
                }
            }
            catch (Exception dg23301_1) { Diag.Swallow("MainForm.cs:12512", dg23301_1); }
            return "";
        }

        void ShowStartupCheckup(System.Collections.Generic.List<StartupEntry> data)
        {
            StringBuilder rep = new StringBuilder();
            int nDead = 0, nDup = 0, nRisk = 0, nFake = 0;
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> byCmd =
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);
            System.Collections.Generic.List<string> dead = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<string> risk = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<string> fake = new System.Collections.Generic.List<string>();

            for (int i = 0; i < data.Count; i++)
            {
                StartupEntry e = data[i];
                if (e.State.IndexOf(T("禁用", "Disabled"), StringComparison.Ordinal) >= 0 || e.State == "Manual") continue;
                // 服务项共用 svchost，逐条比对会造出一堆假重复；只体检用户级启动项
                string kind0 = e.Kind == null ? "" : e.Kind;
                if (kind0 == "SVC" || kind0.IndexOf("服务", StringComparison.Ordinal) >= 0 || kind0.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string cmd = e.Command == null ? "" : e.Command.Trim();
                if (cmd.Length == 0) continue;

                // 重复：同一命令行挂了多个名字
                string key = cmd;
                if (!byCmd.ContainsKey(key)) byCmd[key] = new System.Collections.Generic.List<string>();
                byCmd[key].Add(e.Name);

                string tgt = StartupTargetOf(cmd);
                if (tgt.Length > 0 && tgt.IndexOf(':') > 0 && !File.Exists(tgt) && !Directory.Exists(tgt))
                {
                    dead.Add("· " + e.Name + "  →  " + T("路径已不存在：", "missing path: ") + tgt);
                    nDead++;
                }

                string lo = (tgt.Length > 0 ? tgt : cmd).ToLowerInvariant();
                if (lo.Contains("\\temp\\") || lo.EndsWith("\\temp") || lo.Contains("\\downloads\\")
                    || lo.Contains("\\users\\public\\") || lo.StartsWith("\\\\"))
                {
                    risk.Add("· " + e.Name + "  →  " + T("位于临时/下载/公共/网络目录", "in temp / downloads / public / network path"));
                    nRisk++;
                }

                string nm = e.Name == null ? "" : e.Name;
                string nmLo = nm.ToLowerInvariant();
                if ((nmLo.Contains("windows") || nmLo.Contains("microsoft") || nmLo.Contains("微软"))
                    && !lo.Contains("\\windows\\") && !lo.Contains("\\program files"))
                {
                    fake.Add("· " + nm + "  →  " + T("名字像系统组件，但可执行文件不在系统目录：", "system-like name but binary is outside system dirs: ") + tgt);
                    nFake++;
                }
            }

            System.Collections.Generic.List<string> dupLines = new System.Collections.Generic.List<string>();
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.List<string>> kv in byCmd)
            {
                if (kv.Value.Count < 2) continue;
                dupLines.Add("· " + kv.Value.Count + T(" 项重复：", " duplicates: ") + string.Join(" / ", kv.Value.ToArray()));
                nDup++;
            }

            rep.AppendLine(T("启动项体检报告  ", "Startup health report  ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            rep.AppendLine(T("共扫描 ", "Scanned ") + data.Count + T(" 项（已禁用项、自启服务不计入）", " entries (disabled + services skipped)"));
            rep.AppendLine("");
            rep.AppendLine(T("【失效路径】", "[Missing paths]") + nDead + T(" 项", ""));
            for (int i = 0; i < dead.Count; i++) rep.AppendLine(dead[i]);
            rep.AppendLine("");
            rep.AppendLine(T("【重复项】", "[Duplicates]") + nDup + T(" 组", ""));
            for (int i = 0; i < dupLines.Count; i++) rep.AppendLine(dupLines[i]);
            rep.AppendLine("");
            rep.AppendLine(T("【高风险位置】", "[Risky location]") + nRisk + T(" 项", ""));
            for (int i = 0; i < risk.Count; i++) rep.AppendLine(risk[i]);
            rep.AppendLine("");
            rep.AppendLine(T("【伪装系统名】", "[Fake system names]") + nFake + T(" 项", ""));
            for (int i = 0; i < fake.Count; i++) rep.AppendLine(fake[i]);
            if (nDead + nDup + nRisk + nFake == 0)
                rep.AppendLine(T("结论：未发现异常，启动项整体健康。", "Conclusion: nothing suspicious found."));

            Form cf = new Form();
            cf.Text = T("启动项体检 · 小工具箱", "Startup Health · Mini Toolbox");
            cf.BackColor = UiKit.A(20, 24, 30);
            cf.StartPosition = FormStartPosition.CenterParent;
            cf.ClientSize = new Size(700, 480);
            cf.ShowIcon = false;
            cf.Font = new Font("Microsoft YaHei UI", 9F);

            Label ct = new Label();
            ct.Text = T("启动项体检", "Startup health");
            ct.ForeColor = UiKit.Txt(Color.White);
            ct.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            ct.Location = new Point(20, 16);
            ct.AutoSize = true;
            cf.Controls.Add(ct);

            Label cs = new Label();
            cs.Text = T("失效 ", "dead ") + nDead + T(" · 重复 ", "· dup ") + nDup + T(" · 风险 ", "· risk ") + nRisk + T(" · 伪装 ", "· fake ") + nFake;
            cs.ForeColor = (nDead + nRisk + nFake) > 0 ? UiKit.A(246, 186, 72) : UiKit.A(84, 224, 146);
            cs.Font = new Font("Microsoft YaHei UI", 8.5F);
            cs.Location = new Point(20, 48);
            cs.AutoSize = true;
            cf.Controls.Add(cs);

            Panel cbox = new Panel();
            cbox.Location = new Point(20, 72);
            cbox.Size = new Size(660, 340);
            cbox.BackColor = UiKit.A(26, 31, 39);
            cbox.Paint += delegate (object s5, PaintEventArgs e5)
            {
                using (Pen p5 = new Pen(UiKit.A(58, 70, 88)))
                    e5.Graphics.DrawRectangle(p5, 0, 0, cbox.Width - 1, cbox.Height - 1);
            };
            cf.Controls.Add(cbox);

            TextBox ctb = new TextBox();
            ctb.Location = new Point(9, 9);
            ctb.Size = new Size(642, 322);
            ctb.Multiline = true;
            ctb.ReadOnly = true;
            ctb.ScrollBars = ScrollBars.Vertical;
            ctb.BorderStyle = BorderStyle.None;
            ctb.BackColor = Ui2.CtlInk(UiKit.A(31, 37, 47));
            ctb.ForeColor = UiKit.A(226, 234, 244);
            ctb.Font = new Font("Microsoft YaHei UI", 9F);
            ctb.Text = rep.ToString().Replace("\n", "\r\n");
            ctb.TabStop = false;
            StylePrevBox(ctb);
            cbox.Controls.Add(ctb);

            Button cCopy = MakeBtn(T("复制报告", "Copy report"), UiKit.A(14, 165, 233), Color.White, 20, 424, 120, 34);
            cCopy.Click += delegate
            {
                try { Clipboard.SetText(rep.ToString()); Sfx.Copy(); } catch (Exception dg23433_1) { Diag.Swallow("MainForm.cs:12644", dg23433_1); }
            };
            cf.Controls.Add(cCopy);

            Button cSave = MakeBtn(T("保存到文件", "Save to file"), C_BUBBLE_ALT(), Color.White, 150, 424, 120, 34);
            cSave.Click += delegate
            {
                try
                {
                    string pf = Path.Combine(BASE_DIR, "启动项体检_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    File.WriteAllText(pf, rep.ToString(), new UTF8Encoding(false));
                    MessageBox.Show(cf, T("已保存：", "Saved: ") + pf, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(cf, ex.Message, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            cf.Controls.Add(cSave);

            Button cClose = MakeBtn(T("关闭", "Close"), C_BUBBLE_ALT(), Color.White, 580, 424, 100, 34);
            cClose.Click += delegate { cf.Close(); };
            cf.Controls.Add(cClose);

            Ui2.Polish(cf);
            cf.ShowDialog(this);
        }

        string BootBackupPath { get { return Path.Combine(BASE_DIR, "启动项禁用备份.tsv"); } }

        System.Collections.Generic.List<string> LoadBootBackup()
        {
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            try
            {
                if (File.Exists(BootBackupPath))
                    foreach (string l in File.ReadAllLines(BootBackupPath, Encoding.UTF8))
                        if (l.Trim().Length > 0) lines.Add(l);
            }
            catch (Exception dg23469_1) { Diag.Swallow("MainForm.cs:12680", dg23469_1); }
            return lines;
        }

        void AppendBootBackup(string line)
        {
            try { File.AppendAllText(BootBackupPath, line + "\r\n", Encoding.UTF8); } catch (Exception dg23475_1) { Diag.Swallow("MainForm.cs:12686", dg23475_1); }
        }

        bool InBootBackup(string key)
        {
            foreach (string l in LoadBootBackup()) if (l.IndexOf(key, StringComparison.OrdinalIgnoreCase) == 0) return true;
            return false;
        }

        System.Collections.Generic.List<StartupEntry> CollectStartupItems()
        {
            System.Collections.Generic.List<StartupEntry> list = new System.Collections.Generic.List<StartupEntry>();
            string[] runPaths = { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                                  @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce" };
            RegistryKey[] hives = { Registry.CurrentUser, Registry.LocalMachine };
            string[] hiveNames = { "HKCU", "HKLM" };
            for (int h = 0; h < hives.Length; h++)
            {
                for (int i = 0; i < runPaths.Length; i++)
                {
                    try
                    {
                        using (RegistryKey k = hives[h].OpenSubKey(runPaths[i]))
                        {
                            if (k == null) continue;
                            foreach (string name in k.GetValueNames())
                            {
                                string cmd = Convert.ToString(k.GetValue(name));
                                string source = hiveNames[h] + (runPaths[i].IndexOf("WOW6432Node") >= 0 ? " (32位)" : "") + (runPaths[i].IndexOf("RunOnce") >= 0 ? " RunOnce" : " Run");
                                string keyId = "REG|" + hiveNames[h] + "|" + runPaths[i] + "|" + name;
                                list.Add(new StartupEntry { Name = name, Source = source, Command = cmd, Kind = "REG", KeyId = keyId, State = T("启用", "Enabled") });
                            }
                        }
                    }
                    catch (Exception dg23509_1) { Diag.Swallow("MainForm.cs:12720", dg23509_1); }
                }
            }
            // 启动文件夹
            string[] folders = { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) };
            foreach (string folder in folders)
            {
                try
                {
                    foreach (string fn in Directory.GetFiles(folder))
                    {
                        string ext = Path.GetExtension(fn).ToLowerInvariant();
                        if (ext == ".ini") continue;
                        list.Add(new StartupEntry { Name = Path.GetFileNameWithoutExtension(fn), Source = T("启动文件夹", "Startup folder"), Command = fn, Kind = "FILE", KeyId = "FILE|" + fn, State = T("启用", "Enabled") });
                    }
                }
                catch (Exception dg23525_1) { Diag.Swallow("MainForm.cs:12736", dg23525_1); }
            }
            // 自启服务
            try
            {
                using (System.Management.ManagementObjectSearcher se = new System.Management.ManagementObjectSearcher("SELECT Name, DisplayName, PathName, StartMode FROM Win32_Service WHERE StartMode='Auto'"))
                {
                    foreach (System.Management.ManagementObject mo in se.Get())
                    {
                        string nm = Convert.ToString(mo["Name"]);
                        string dn = Convert.ToString(mo["DisplayName"]);
                        string pn = Convert.ToString(mo["PathName"]);
                        list.Add(new StartupEntry { Name = dn.Length > 0 ? dn : nm, Source = T("自启服务", "Auto service"), Command = pn, Kind = "SVC", KeyId = "SVC|" + nm, State = "Auto" });
                    }
                }
            }
            catch (Exception dg23541_1) { Diag.Swallow("MainForm.cs:12752", dg23541_1); }
            return list;
        }

        string DisableStartup(StartupEntry en)
        {
            try
            {
                if (InBootBackup(en.KeyId)) { /* 已备份过 */ }
                if (en.Kind == "REG")
                {
                    string[] parts = en.KeyId.Split('|');
                    RegistryKey hive = parts[1] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    using (RegistryKey k = hive.OpenSubKey(parts[2], true))
                    {
                        if (k == null) return T("注册表项不存在", "registry key missing");
                        k.DeleteValue(parts[3], false);
                    }
                    AppendBootBackup(en.KeyId + "\t" + en.Name + "\t" + en.Command);
                    Log("[启动项] 已禁用 " + en.Name + " (" + parts[1] + ")" + DateTime.Now.ToString(" HH:mm:ss"));
                    return null;
                }
                if (en.Kind == "FILE")
                {
                    string bakDir = Path.Combine(BASE_DIR, "启动项禁用文件");
                    if (!Directory.Exists(bakDir)) Directory.CreateDirectory(bakDir);
                    string dst = Path.Combine(bakDir, Path.GetFileName(en.Command));
                    if (File.Exists(dst)) dst = Path.Combine(bakDir, DateTime.Now.ToString("HHmmss") + "_" + Path.GetFileName(en.Command));
                    File.Move(en.Command, dst);
                    AppendBootBackup(en.KeyId + "\t" + en.Name + "\t" + dst);
                    Log("[启动项] 已禁用启动文件夹项 " + en.Name);
                    return null;
                }
                if (en.Kind == "SVC")
                {
                    string svc = en.KeyId.Substring(4);
                    int code = RunSilent("sc.exe", "config \"" + svc + "\" start= demand");
                    if (code != 0) return T("需要管理员权限（请右键以管理员身份运行本工具）", "needs admin rights");
                    AppendBootBackup(en.KeyId + "\t" + en.Name + "\tauto");
                    Log("[启动项] 已把服务 " + svc + " 改为手动");
                    return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
            return null;
        }

        int RestoreStartupAll()
        {
            int ok = 0;
            System.Collections.Generic.List<string> lines = LoadBootBackup();
            System.Collections.Generic.List<string> keep = new System.Collections.Generic.List<string>();
            foreach (string l in lines)
            {
                string[] parts = l.Split('\t');
                if (parts.Length < 3) continue;
                string id = parts[0], name = parts[1], val = parts[2];
                try
                {
                    if (id.StartsWith("REG|"))
                    {
                        string[] p = id.Split('|');
                        RegistryKey hive = p[1] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                        using (RegistryKey k = hive.CreateSubKey(p[2]))
                            k.SetValue(p[3], val);
                        ok++;
                    }
                    else if (id.StartsWith("FILE|"))
                    {
                        string origin = id.Substring(5);
                        if (File.Exists(val)) { File.Move(val, origin); ok++; }
                    }
                    else if (id.StartsWith("SVC|"))
                    {
                        string svc = id.Substring(4);
                        if (RunSilent("sc.exe", "config \"" + svc + "\" start= auto") == 0) ok++;
                        else keep.Add(l);
                    }
                }
                catch { keep.Add(l); }
            }
            try
            {
                if (keep.Count == 0) { if (File.Exists(BootBackupPath)) File.Delete(BootBackupPath); }
                else File.WriteAllLines(BootBackupPath, keep.ToArray(), Encoding.UTF8);
            }
            catch (Exception dg23627_1) { Diag.Swallow("MainForm.cs:12838", dg23627_1); }
            if (ok > 0) Log("[启动项] 恢复 " + ok + " 项");
            return ok;
        }

        static int RunSilent(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(8000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        // ================= v6.7 手机互传 =================
        string XferRoot() { return Path.Combine(BASE_DIR, "互传"); }
        string XferInbox() { return Path.Combine(XferRoot(), "收件-手机传到电脑"); }
        string XferOutbox() { return Path.Combine(XferRoot(), "发件-电脑传给手机"); }

        void ShowTransfer()
        {
            LanXferServer srv = new LanXferServer(XferInbox(), XferOutbox());
            // v6.8 局域网剪贴板：HTTP 线程访问剪贴板必须回到 UI 线程
            srv.ClipGet = delegate ()
            {
                string r = "";
                try { this.Invoke((Action)delegate { try { r = Clipboard.GetText(); } catch { r = ""; } }); } catch (Exception dg23660_1) { Diag.Swallow("MainForm.cs:12871", dg23660_1); }
                return r == null ? "" : r;
            };
            srv.ClipSet = delegate (string t)
            {
                try { this.Invoke((Action)delegate { try { Clipboard.SetText(t); } catch (Exception dg23665_1) { Diag.Swallow("MainForm.cs:12876", dg23665_1); } }); } catch (Exception dg23665_2) { Diag.Swallow("MainForm.cs:12876", dg23665_2); }
            };

            Form f = new Form();
            f.Text = T("手机互传 · 小工具箱", "Phone Transfer · Mini Toolbox");
            f.BackColor = UiKit.A(20, 24, 30);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(800, 560);
            f.MinimumSize = new Size(720, 480);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);

            Label ttl = new Label();
            ttl.Text = T("手机 ⇄ 电脑 互传", "Phone ⇄ PC Transfer");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            ttl.Location = new Point(20, 16);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("手机连同一个 WiFi，浏览器打开下面地址：手机上选文件传上来，列表里的文件可直接下载到手机。不装 App、不走数据线。",
                "Same Wi-Fi, open the address below in your phone browser: upload files, or download listed files. No app, no cable.");
            sub.ForeColor = UiKit.A(150, 160, 176);
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.Location = new Point(20, 46);
            sub.Size = new Size(760, 34);
            f.Controls.Add(sub);

            Label url = new Label();
            url.Text = T("服务未启动", "Server stopped");
            url.ForeColor = UiKit.A(120, 220, 160);
            url.Font = new Font("Consolas", 14F, FontStyle.Bold);
            url.Location = new Point(20, 84);
            url.Size = new Size(760, 30);
            f.Controls.Add(url);

            Button btnStart = MakeBtn(T("启动服务", "Start"), UiKit.A(14, 165, 233), Color.White, 20, 120, 110, 34);
            f.Controls.Add(btnStart);
            Button btnStop = MakeBtn(T("停止服务", "Stop"), UiKit.A(232, 82, 61), Color.White, 140, 120, 110, 34);
            btnStop.Enabled = false;
            f.Controls.Add(btnStop);
            Button btnIn = MakeBtn(T("收件目录", "Open Inbox"), C_BUBBLE_ALT(), Color.White, 260, 120, 120, 34);
            f.Controls.Add(btnIn);
            Button btnOut = MakeBtn(T("发件目录", "Open Outbox"), C_BUBBLE_ALT(), Color.White, 390, 120, 120, 34);
            f.Controls.Add(btnOut);
            Button btnRef = MakeBtn(T("刷新列表", "Refresh"), C_BUBBLE_ALT(), Color.White, 520, 120, 110, 34);
            f.Controls.Add(btnRef);

            ListView lv = MakeDarkList(new string[] { T("方向", "Dir"), T("文件名", "File"), T("大小", "Size"), T("时间", "Time") },
                new int[] { 80, 420, 100, 140 }, 20, 168, 760, 330);
            f.Controls.Add(lv);

            Label st = new Label();
            st.Text = T("提示：首次启动如被防火墙拦下，请允许「专用网络」访问；文件保存在 ", "Tip: allow private-network access if the firewall asks; files live in ")
                + XferRoot();
            st.ForeColor = UiKit.A(150, 160, 176);
            st.Location = new Point(20, 508);
            st.Size = new Size(760, 40);
            f.Controls.Add(st);

            Action refreshList = delegate
            {
                lv.Items.Clear();
                string[] dirs = { XferInbox(), XferOutbox() };
                string[] tags = { T("手机→电脑", "Phone→PC"), T("电脑→手机", "PC→Phone") };
                for (int d = 0; d < dirs.Length; d++)
                {
                    try
                    {
                        if (!Directory.Exists(dirs[d])) continue;
                        DirectoryInfo di = new DirectoryInfo(dirs[d]);
                        FileInfo[] fs = di.GetFiles("*", SearchOption.TopDirectoryOnly);
                        Array.Sort(fs, delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                        foreach (FileInfo fi in fs)
                        {
                            ListViewItem it = new ListViewItem(new string[] { tags[d], fi.Name, FmtSize(fi.Length), fi.LastWriteTime.ToString("MM-dd HH:mm") });
                            it.Tag = fi.FullName;
                            lv.Items.Add(it);
                        }
                    }
                    catch (Exception dg23746_1) { Diag.Swallow("MainForm.cs:12957", dg23746_1); }
                }
                if (lv.Items.Count == 0)
                {
                    ListViewItem it = new ListViewItem(new string[] { "—", T("还没有文件", "no files yet"), "", "" });
                    it.ForeColor = UiKit.A(120, 130, 145);
                    lv.Items.Add(it);
                }
                lv.Invalidate();
            };

            btnStart.Click += (s, e) =>
            {
                string err = srv.Start();
                if (err != null) { MessageBox.Show(f, T("启动失败：\n", "Start failed:\n") + err, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                url.Text = string.Join("   ", srv.Urls());
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                refreshList();
                Log("[互传] 服务启动，端口 " + srv.Port);
            };
            btnStop.Click += (s, e) =>
            {
                srv.Stop();
                url.Text = T("服务已停止", "Server stopped");
                url.ForeColor = UiKit.A(150, 160, 176);
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                Log("[互传] 服务停止");
            };
            btnIn.Click += (s, e) => { try { if (!Directory.Exists(XferInbox())) Directory.CreateDirectory(XferInbox()); Process.Start("explorer.exe", XferInbox()); } catch (Exception dg23776_1) { Diag.Swallow("MainForm.cs:12987", dg23776_1); } };
            btnOut.Click += (s, e) => { try { if (!Directory.Exists(XferOutbox())) Directory.CreateDirectory(XferOutbox()); Process.Start("explorer.exe", XferOutbox()); } catch (Exception dg23777_1) { Diag.Swallow("MainForm.cs:12988", dg23777_1); } };
            btnRef.Click += (s, e) => refreshList();

            lv.DoubleClick += (s, e) =>
            {
                if (lv.SelectedItems.Count == 0) return;
                string p = Convert.ToString(lv.SelectedItems[0].Tag);
                if (p != null && p.Length > 0 && File.Exists(p)) { try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch (Exception dg23784_1) { Diag.Swallow("MainForm.cs:12995", dg23784_1); } }
            };

            f.FormClosing += delegate { srv.Stop(); };
            f.Shown += delegate { btnStart.PerformClick(); refreshList(); };
            f.ShowDialog(this);
        }

        void ShowAbout()
        {
            UpdateForm uf = new UpdateForm(langEn);
            uf.ShowDialog(this);
            ApplyPendingJump();
        }

        // 关于页六格胶囊：点了先关窗，这里接着把主界面切到那一页
        public void ApplyPendingJump()
        {
            int k = UpdateForm.PendingJump;
            UpdateForm.PendingJump = -1;
            if (k < 0) return;
            try { SelectAction(k); } catch (Exception dg23805_1) { Diag.Swallow("MainForm.cs:13016", dg23805_1); }
        }

        // ================= 系统操作 =================
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMsgCond(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // 动作链用的两个原子操作
        static void MuteNow()
        {
            try
            {
                keybd_event((byte)0xAD, 0, 0, 0);
                keybd_event((byte)0xAD, 0, 2, 0);
            }
            catch (Exception dg23822_1) { Diag.Swallow("MainForm.cs:13033", dg23822_1); }
        }

        static void ScreenOff()
        {
            try { SendMsgCond((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2); } catch (Exception dg23827_1) { Diag.Swallow("MainForm.cs:13038", dg23827_1); }
        }

        static bool RunAnyAction(string action)
        {
            try
            {
                // 动作链：先做原子操作，再执行系统动作（中英名都认）
                if (action == "静音后睡眠" || action == "Mute + Sleep")
                {
                    MuteNow();
                    System.Threading.Thread.Sleep(200);
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true });
                    return true;
                }
                if (action == "关屏后锁定" || action == "Screen off + Lock")
                {
                    ScreenOff();
                    System.Threading.Thread.Sleep(200);
                    Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                    return true;
                }
                switch (action)
                {
                    case "关机":
                        Process.Start(new ProcessStartInfo("shutdown", "/s /t 0 /f") { CreateNoWindow = true });
                        return true;
                    case "重启":
                        Process.Start(new ProcessStartInfo("shutdown", "/r /t 0 /f") { CreateNoWindow = true });
                        return true;
                    case "睡眠":
                        Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true });
                        return true;
                    case "锁定":
                        Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                        return true;
                    case "注销":
                        Process.Start(new ProcessStartInfo("shutdown", "/l") { CreateNoWindow = true });
                        return true;
                    case "休眠":
                        Process.Start(new ProcessStartInfo("shutdown", "/h") { CreateNoWindow = true });
                        return true;
                }
            }
            catch { return false; }
            return false;
        }

        void OpenWingetDialog()
        {
            using (WingetForm f = new WingetForm(langEn))
            {
                f.ShowDialog(this);
            }
        }

        void OpenIsoDialog()
        {
            string dir = Path.Combine(vOutDir, "镜像");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch (Exception dg23886_1) { Diag.Swallow("MainForm.cs:13097", dg23886_1); }
            using (IsoForm f = new IsoForm(langEn, dir))
            {
                f.ShowDialog(this);
            }
        }

        bool RunSystemAction(string action)
        {
            try
            {
                // 动作链：先做原子操作，再执行系统动作（中英名都认）
                if (action == "静音后睡眠" || action == "Mute + Sleep")
                {
                    MuteNow();
                    System.Threading.Thread.Sleep(200);
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true });
                    return true;
                }
                if (action == "关屏后锁定" || action == "Screen off + Lock")
                {
                    ScreenOff();
                    System.Threading.Thread.Sleep(200);
                    Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                    return true;
                }
                switch (action)
                {
                    case "关机":
                        Process.Start(new ProcessStartInfo("shutdown", "/s /t 0 /f") { CreateNoWindow = true });
                        return true;
                    case "锁定":
                        Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { CreateNoWindow = true });
                        return true;
                }
            }
            catch { return false; }
            return false;
        }
    }
}