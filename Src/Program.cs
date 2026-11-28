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
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool EnumWindows(WakeEnumProc cb, IntPtr l);
        delegate bool WakeEnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Program.CopyData lParam);
        [StructLayout(LayoutKind.Sequential)]
        internal struct CopyData { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        // v19.6：把 /go:xxx 的跳页请求送给已运行的实例（WM_COPYDATA）
        static void SendJumpToExisting(string slot)
        {
            try
            {
                string me = Process.GetCurrentProcess().ProcessName;
                List<uint> pids = new List<uint>();
                foreach (Process pp in Process.GetProcessesByName(me))
                    if (pp.Id != Process.GetCurrentProcess().Id) pids.Add((uint)pp.Id);
                if (pids.Count == 0) return;
                CopyData cd = new CopyData();
                cd.dwData = (IntPtr)1;
                cd.lpData = Marshal.StringToHGlobalUni(slot);
                cd.cbData = (slot.Length + 1) * 2;
                try
                {
                    // 同进程里可能不止一个顶层窗口（托盘、悬浮条），逐个发，主窗自己会认领
                    EnumWindows(delegate (IntPtr h, IntPtr l)
                    {
                        uint pid = 0;
                        GetWindowThreadProcessId(h, out pid);
                        if (pids.Contains(pid) && GetWindow(h, 4) == IntPtr.Zero && IsWindowVisible(h))
                        {
                            try { SendMessage(h, 0x004A, IntPtr.Zero, ref cd); } catch (Exception dg66_1) { Diag.Swallow("Program.cs:66", dg66_1); }
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                finally { try { Marshal.FreeHGlobal(cd.lpData); } catch (Exception dg71_1) { Diag.Swallow("Program.cs:71", dg71_1); } }
            }
            catch (Exception dg73_1) { Diag.Swallow("Program.cs:73", dg73_1); }
        }

        // 检测到已在运行时，把已有实例的主窗口唤出并置前（而不是只弹提示）
        static void WakeExisting()
        {
            try
            {
                string me = Process.GetCurrentProcess().ProcessName;
                List<uint> pids = new List<uint>();
                foreach (Process pp in Process.GetProcessesByName(me))
                {
                    if (pp.Id != Process.GetCurrentProcess().Id) pids.Add((uint)pp.Id);
                }
                if (pids.Count == 0) return;
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    uint pid = 0;
                    GetWindowThreadProcessId(h, out pid);
                    if (pids.Contains(pid))
                    {
                        if (GetWindow(h, 4) == IntPtr.Zero)
                        {
                            ShowWindow(h, 5);
                            SetForegroundWindow(h);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception dg103_1) { Diag.Swallow("Program.cs:103", dg103_1); }
        }

        // 没有 update_url.txt 时补一份默认更新源，保证全新安装也能在线更新
        static void EnsureUpdateUrlFile()
        {
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "update_url.txt");
                if (!File.Exists(p))
                    File.WriteAllText(p, "https://raw.githubusercontent.com/wj5666619/MiniToolbox/main/update.json", new UTF8Encoding(false));
            }
            catch (Exception dg115_1) { Diag.Swallow("Program.cs:115", dg115_1); }
        }

        // 回退：备份存在就交给一个后台 PowerShell 完成"等进程退出 → 覆盖 → 重启"
        static void DoRollback()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string target = Application.ExecutablePath;
                string prev = Path.Combine(dir, "上一版_小工具箱.exe");
                if (!File.Exists(prev))
                {
                    MessageBox.Show("没有找到上一版备份（上一版_小工具箱.exe）。", "无法回退", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string ps1 = Path.Combine(Path.GetTempPath(), "mtb_rollback.ps1");
                string script =
                    "$ErrorActionPreference='SilentlyContinue'\r\n" +
                    "Start-Sleep -Seconds 2\r\n" +
                    "$t='" + target + "'\r\n" +
                    "$p='" + prev + "'\r\n" +
                    "for($i=0;$i -lt 30;$i++){ Get-Process -Name '小工具箱' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400; try { Copy-Item -LiteralPath $p -Destination $t -Force -ErrorAction Stop; if((Get-Item $t).Length -eq (Get-Item $p).Length){ Start-Process -FilePath $t; break } } catch {} }\r\n";
                File.WriteAllText(ps1, script, new UTF8Encoding(false));
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception dg141_1) { Diag.Swallow("Program.cs:141", dg141_1); }
        }

        public static Mutex mutex;
        static readonly Stopwatch runClock = new Stopwatch();
        static bool exitingLogged = false;
        [STAThread]
        static void Main()
        {
            runClock.Start();
            string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
            try { Diag.Init(baseDir); } catch (Exception dg152_1) { Diag.Swallow("Program.cs:152", dg152_1); }
            // 无窗口子进程模式：--steam-unlock <appid> [dry|selftest]
            // 必须放在单实例 mutex 之前，否则子进程会被当成「已在运行」直接退出
            string[] allArgs = Environment.GetCommandLineArgs();
            for (int i = 1; i < allArgs.Length; i++)
            {
                if (string.Equals(allArgs[i], "--steam-unlock", StringComparison.OrdinalIgnoreCase))
                {
                    string[] sub = new string[allArgs.Length - i - 1];
                    Array.Copy(allArgs, i + 1, sub, 0, sub.Length);
                    Environment.ExitCode = SteamDirect.ChildMain(sub);
                    return;
                }
            }
            // v19.1：/rollback 用「上一版_小工具箱.exe」覆盖当前 exe（更新时自动留的备份）
            for (int i = 1; i < allArgs.Length; i++)
            {
                if (string.Equals(allArgs[i], "/rollback", StringComparison.OrdinalIgnoreCase)) { DoRollback(); return; }
            }
            // v19.6：/go:home 这类参数在二次启动时送给已运行实例，不重复开窗口
            string goSlot = "";
            for (int i = 1; i < allArgs.Length; i++)
            {
                string ga = (allArgs[i] ?? "").Trim();
                if (ga.StartsWith("/go:", StringComparison.OrdinalIgnoreCase)) { goSlot = ga.Substring(4).Trim(); break; }
            }
            bool createdNew = true;
            try
            {
                mutex = new Mutex(true, "DoubaoShutdownTool_SingleInstance", out createdNew);
            }
            catch (Exception ex)
            {
                // 互斥体创建失败（权限/句柄耗尽）不该让程序起不来，退化成允许多开并记日志
                Diag.Warn("单实例互斥体创建失败，本次按独立实例启动：" + ex.Message);
                createdNew = true;
            }
            if (!createdNew)
            {
                Diag.Info("检测到已在运行的实例，转交唤醒" + (goSlot.Length > 0 ? " 与跳页 /go:" + goSlot : ""));
                WakeExisting();
                if (goSlot.Length > 0) SendJumpToExisting(goSlot);
                return;
            }
            // 全局异常捕获：写日志，不弹崩溃框
            Application.ThreadException += (s, e) => CrashLog(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => CrashLog(e.ExceptionObject as Exception);
            // v19.7：正常退出也留一行，日志首尾完整才好判断「是崩了还是关了」
            Application.ApplicationExit += delegate (object s, EventArgs e) { Diag.Shutdown("正常退出", runClock.ElapsedMilliseconds); exitingLogged = true; try { Audit.Write("退出", "运行 " + (int)(runClock.ElapsedMilliseconds / 1000) + " 秒"); } catch (Exception dg200a_1) { Diag.Swallow("Program.cs:exit", dg200a_1); } };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // v19.13：内置工具站用的 WebView2 托管程序集随 exe 一起打包，启动时先挂程序集解析
            try { WebView2Boot.Init(); } catch (Exception dgWebInit) { Diag.Swallow("Program.cs:WebView2Init", dgWebInit); }
            EnsureUpdateUrlFile();
            // v7.9：音效引擎（音量/开关落盘 音效.txt）+ 全局皮肤巡检（新窗体出现即套用）
            try { Cfg.Load(Path.GetDirectoryName(Application.ExecutablePath)); } catch (Exception dg205_1) { Diag.Swallow("Program.cs:205", dg205_1); }   // v19.1 配置总管（迁移旧 txt）
            try { Diag.Relocate(Cfg.Dir); } catch (Exception dg206_1) { Diag.Swallow("Program.cs:206", dg206_1); }
            try { Diag.SetVerbose(Cfg.GetBool("diagVerbose", false)); } catch (Exception dg207_1) { Diag.Swallow("Program.cs:207", dg207_1); }
            // v19.9：企业策略 + 审计日志（策略可强制详细日志，只收紧不放宽）
            try { Policy.Load(); } catch (Exception dg207_2) { Diag.Swallow("Program.cs:Policy", dg207_2); }
            try { if (Policy.Verbose) Diag.SetVerbose(true); } catch (Exception dg207_3) { Diag.Swallow("Program.cs:PolicyVerbose", dg207_3); }
            try { Audit.Init(Cfg.Dir); } catch (Exception dg207_4) { Diag.Swallow("Program.cs:Audit", dg207_4); }
            try { Audit.Write("启动", "v" + UiKit.VER + " | 策略 " + Policy.Describe()); } catch (Exception dg207_5) { Diag.Swallow("Program.cs:AuditStart", dg207_5); }
            try { Diag.Header(Environment.GetCommandLineArgs(), runClock.ElapsedMilliseconds); } catch (Exception dg208_1) { Diag.Swallow("Program.cs:208", dg208_1); }
            RunSelfCheck();
            try { Sfx.Init(Path.GetDirectoryName(Application.ExecutablePath)); } catch (Exception dg210_1) { Diag.Swallow("Program.cs:210", dg210_1); }
            try { Ui2.LoadUiScale(); } catch (Exception dg211_1) { Diag.Swallow("Program.cs:211", dg211_1); }
            Ui2.StartWatch();

            // 启动动画开关：默认播放；带任何 /测试参数 时自动跳过；/splash 强制播放，/nosplash 强制跳过
            bool forceSplash = false, skipSplash = false;
            string[] av = Environment.GetCommandLineArgs();
            if (av != null)
            {
                for (int i = 1; i < av.Length; i++)
                {
                    if (string.Equals((av[i] ?? "").Trim(), "/toolstest", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string d = ToolsSitePack.EnsureSite(null);
                            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "_toolstest.txt"), "OK " + d);
                        }
                        catch (Exception ex)
                        {
                            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "_toolstest.txt"), "FAIL " + ex.Message);
                        }
                        return;
                    }
                }
            }
            if (av != null)
            {
                for (int i = 1; i < av.Length; i++)
                {
                    string al = (av[i] ?? "").Trim().ToLowerInvariant();
                    if (al == "/splash") forceSplash = true;
                    else if (al == "/nosplash") skipSplash = true;
                    else if (al.Length > 1 && al[0] == '/') skipSplash = true;
                }
            }
            if (forceSplash) skipSplash = false;

            Lang.Load();   // v17.7：先把上次选的语言读出来，MainForm 构造时文案才是对的
            // 调试用：/skin4 之类可以直接以指定皮肤启动一次（不落盘）
            for (int i = 1; i < av.Length; i++)
            {
                string sa = (av[i] ?? "").Trim().ToLowerInvariant();
                if (sa.StartsWith("/skin"))
                {
                    int si = 0;
                    if (int.TryParse(sa.Substring(5), out si)) { try { UiKit.ApplySkin(si, false); } catch (Exception dg237_1) { Diag.Swallow("Program.cs:237", dg237_1); } }
                }
            }
            MainForm mf = new MainForm();
            // v19.6：带 /go:xxx 首次启动时，上屏后直接落到那一页
            if (goSlot.Length > 0)
            {
                string gs = goSlot;
                mf.Shown += delegate { try { mf.JumpToSlot(gs); } catch (Exception dg245_1) { Diag.Swallow("Program.cs:245", dg245_1); } };
            }
            // v18.6：启动 25 秒后后台静默查一次新版本，有就用轻提示告知，绝不打断（测试参数下不启用）
            if (!skipSplash)
            {
                try
                {
                    System.Windows.Forms.Timer upT = new System.Windows.Forms.Timer();
                    upT.Interval = 25000;
                    upT.Tick += delegate
                    {
                        try { upT.Stop(); upT.Dispose(); } catch (Exception dg256_1) { Diag.Swallow("Program.cs:256", dg256_1); }
                        try
                        {
                            System.Threading.ThreadPool.QueueUserWorkItem(delegate
                            {
                                string nv = "";
                                try { nv = UpdateForm.QuietCheck(); } catch (Exception dg262_1) { Diag.Swallow("Program.cs:262", dg262_1); }
                                if (nv.Length == 0) return;
                                try
                                {
                                    if (!mf.IsDisposed)
                                        mf.BeginInvoke((Action)delegate
                                        {
                                            Ui2.Toast(mf, "发现新版本 v" + nv + "，可在「检查更新」里升级", Color.FromArgb(255, 176, 74), 4200);
                                        });
                                }
                                catch (Exception dg272_1) { Diag.Swallow("Program.cs:272", dg272_1); }
                            });
                        }
                        catch (Exception dg275_1) { Diag.Swallow("Program.cs:275", dg275_1); }
                    };
                    upT.Start();
                }
                catch (Exception dg279_1) { Diag.Swallow("Program.cs:279", dg279_1); }
            }
            // v18.7：首次启动三步引导（写 首次引导.txt 作标记；/guide 参数可强制预览）
            try
            {
                bool forceGuide = false;
                for (int i = 1; i < av.Length; i++)
                {
                    string ga = (av[i] ?? "").Trim().ToLowerInvariant();
                    if (ga == "/guide") forceGuide = true;
                    else if (ga == "/guide2") { forceGuide = true; FirstRunForm.StartStep = 1; }
                    else if (ga == "/guide3") { forceGuide = true; FirstRunForm.StartStep = 2; }
                }
                if (forceGuide || (FirstRunForm.Needed() && !skipSplash))
                {
                    using (FirstRunForm g = new FirstRunForm()) g.ShowDialog();
                }
            }
            catch (Exception dg297_1) { Diag.Swallow("Program.cs:297", dg297_1); }
            // 自检截图通道：/shot 把窗口自己渲染成 PNG（不依赖屏幕抓取，取证更可靠）
            bool wantShot = false;
            for (int i = 1; i < av.Length; i++)
            {
                string al = (av[i] ?? "").Trim().ToLowerInvariant();
                if (al == "/shot") wantShot = true;
            }
            if (wantShot)
            {
                System.Windows.Forms.Timer st = new System.Windows.Forms.Timer();
                st.Interval = 6000;
                st.Tick += delegate
                  {
                      try
                      {
                          st.Stop(); st.Dispose();
                          Bitmap bmp = new Bitmap(mf.Width, mf.Height);
                          mf.DrawToBitmap(bmp, new Rectangle(0, 0, mf.Width, mf.Height));
                          bmp.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "_shot.png"),
                              System.Drawing.Imaging.ImageFormat.Png);
                          bmp.Dispose();
                      }
                      catch (Exception ex) { CrashLog(ex); }
                  };
                st.Start();
            }
            // v18.7 调试：/about 自动打开「关于 · 软件更新」窗口，方便验收排版
            bool wantAbout = false;
            for (int i = 1; i < av.Length; i++)
            {
                string al0 = (av[i] ?? "").Trim().ToLowerInvariant();
                if (al0 == "/about" || al0 == "/changelog") wantAbout = true;
            }            if (wantAbout)
            {
                System.Windows.Forms.Timer abt = new System.Windows.Forms.Timer();
                abt.Interval = 3000;
                abt.Tick += delegate
                {
                    try
                    {
                        abt.Stop(); abt.Dispose();
                        using (UpdateForm uf = new UpdateForm(false)) uf.ShowDialog(mf);
                        mf.ApplyPendingJump();   // 关于页点了胶囊就直接跳那一页
                    }
                    catch (Exception ex) { CrashLog(ex); }
                };
                abt.Start();
            }
            // v19.14 调试：/ccshot 把控制中心自己渲染成 PNG，验收排版用
            bool wantCcShot = false;
            for (int i = 1; i < av.Length; i++)
            {
                string al = (av[i] ?? "").Trim().ToLowerInvariant();
                if (al == "/ccshot") wantCcShot = true;
                else if (al.StartsWith("/ccshot") && al.Length > 7)
                {
                    int pg = 0;
                    if (int.TryParse(al.Substring(7), out pg)) { wantCcShot = true; ControlCenterForm.StartPage = pg; }
                }
            }
            if (wantCcShot)
            {
                System.Windows.Forms.Timer cct = new System.Windows.Forms.Timer();
                cct.Interval = 3000;
                cct.Tick += delegate
                {
                    try
                    {
                        cct.Stop(); cct.Dispose();
                        using (ControlCenterForm cf = new ControlCenterForm(mf, Path.GetDirectoryName(Application.ExecutablePath)))
                        {
                            cf.Show();
                            Application.DoEvents();
                            Bitmap bmp = new Bitmap(cf.Width, cf.Height);
                            cf.DrawToBitmap(bmp, new Rectangle(0, 0, cf.Width, cf.Height));
                            bmp.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "_cc_shot.png"),
                                System.Drawing.Imaging.ImageFormat.Png);
                            bmp.Dispose();
                            cf.Close();
                        }
                    }
                    catch (Exception ex) { CrashLog(ex); }
                };
                cct.Start();
            }
            if (skipSplash)
            {
                Application.Run(mf);
                GC.KeepAlive(mutex);
                return;
            }

            // 启动动画 → 淡入主界面
            mf.Opacity = 0;
            SplashForm sp = new SplashForm();
            sp.OnDone = delegate { FadeInMain(mf); };
            sp.Show();
            System.Windows.Forms.Timer guard = new System.Windows.Forms.Timer();
            guard.Interval = 2600;
            guard.Tick += delegate
            {
                try { guard.Stop(); guard.Dispose(); } catch (Exception dg363_1) { Diag.Swallow("Program.cs:363", dg363_1); }
                if (!mf.IsDisposed && mf.Opacity < 1) FadeInMain(mf);
            };
            guard.Start();
            Application.Run(mf);
            GC.KeepAlive(mutex);
        }

        // 主界面淡入（启动动画结束后）
        static void FadeInMain(Form f)
        {
            if (f == null || f.IsDisposed) return;
            try { if (!f.Visible) f.Show(); } catch (Exception dg375_1) { Diag.Swallow("Program.cs:375", dg375_1); }
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 16;
            t.Tick += delegate
            {
                try
                {
                    if (f.IsDisposed) { t.Stop(); t.Dispose(); return; }
                    double op = f.Opacity + 0.17;
                    if (op >= 1) { f.Opacity = 1; t.Stop(); t.Dispose(); return; }
                    f.Opacity = op;
                }
                catch { try { t.Stop(); t.Dispose(); } catch (Exception dg387_1) { Diag.Swallow("Program.cs:387", dg387_1); } }
            };
            t.Start();
        }

        // v19.7 启动自检：只做「能提前发现、且用户能自己处理」的检查，结果全部进日志。
        // 不弹窗、不打断启动——企业环境里最烦的就是一开就弹一堆提示。
        static void RunSelfCheck()
        {
            try
            {
                string dir = Cfg.Dir.Length > 0 ? Cfg.Dir : Path.GetDirectoryName(Application.ExecutablePath);
                string root = Path.GetPathRoot(dir);
                try
                {
                    DriveInfo di = new DriveInfo(root);
                    long free = di.AvailableFreeSpace;
                    if (free < 500L * 1024 * 1024) Diag.Warn("磁盘剩余空间偏低：" + (free / 1024 / 1024) + " MB（" + root + "）");
                    else Diag.Info("磁盘剩余 " + (free / 1024 / 1024 / 1024) + " GB（" + root + "）");
                }
                catch (Exception dg407_1) { Diag.Swallow("Program.cs:407", dg407_1); }
                if (Application.ExecutablePath.IndexOf("\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    Application.ExecutablePath.IndexOf("\\Windows\\Temp\\", StringComparison.OrdinalIgnoreCase) >= 0)
                    Diag.Warn("程序从临时目录启动，配置与日志可能随清理丢失：" + Application.ExecutablePath);
                int year = DateTime.Now.Year;
                if (year < 2020 || year > 2100) Diag.Warn("系统时间异常：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "，定时任务与更新校验可能不准");
                if (Cfg.Dir.Length == 0) Diag.Warn("配置目录未初始化，本次设置不会保存");
                else Diag.Info("配置目录：" + Cfg.Dir);
                if (Cfg.LastLoadNote.Length > 0) Diag.Warn(Cfg.LastLoadNote);
                Diag.Info("启动自检完成");
            }
            catch (Exception ex) { try { Diag.Warn("启动自检失败：" + ex.Message); } catch (Exception dg418_1) { Diag.Swallow("Program.cs:418", dg418_1); } }
        }

        static bool crashShown = false;
        static void CrashLog(Exception ex)
        {
            if (ex == null) return;
            try { Diag.Error("未捕获异常", ex); } catch (Exception dg425_1) { Diag.Swallow("Program.cs:425", dg425_1); }
            if (crashShown) return;
            crashShown = true;
            // 有活动窗体就在 UI 线程弹诊断面板；启动早期 / 后台线程没有窗体时退回系统消息框，绝不二次崩溃
            try
            {
                Form af = Form.ActiveForm;
                if (af != null && af.IsHandleCreated)
                {
                    af.BeginInvoke((Action)delegate { try { CrashForm.ShowCrash(ex); } catch (Exception dg434_1) { Diag.Swallow("Program.cs:434", dg434_1); } });
                    return;
                }
            }
            catch (Exception dg438_1) { Diag.Swallow("Program.cs:438", dg438_1); }
            try
            {
                string msg = "小工具箱遇到一个内部错误，已经记录到诊断日志。\n\n" +
                             "日志位置：" + (Diag.FilePath.Length > 0 ? Diag.FilePath : "程序目录\\日志\\小工具箱.log") + "\n\n" +
                             "要现在重启小工具箱吗？（选“否”可以继续使用，个别功能可能不正常）";
                if (MessageBox.Show(msg, "出了点小问题", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                    Application.Restart();
            }
            catch (Exception dg447_1) { Diag.Swallow("Program.cs:447", dg447_1); }
        }
    }
}