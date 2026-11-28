using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ShutdownTool
{
    // v19.7 诊断底座：全程序统一日志。
    // 设计原则：写日志本身绝不能失败成灾——所有入口 try/catch 吞掉异常；
    // 单文件上限 1MB，超了滚动成 .1/.2/.3，最多占 4MB；
    // 落盘前做脱敏（程序目录、用户名、GitHub 令牌样式），导给开发者时不泄露本机信息。
    static class Diag
    {
        const long MAX_BYTES = 1024 * 1024;
        static readonly object gate = new object();
        public static string Dir = "";
        public static string FilePath = "";
        static string appDir = "";
        static bool ready = false;
        static int errBurst = 0;
        static DateTime errBurstAt = DateTime.MinValue;

        public static void Init(string baseDir)
        {
            try
            {
                appDir = baseDir ?? "";
                string dir = Path.Combine(baseDir, "日志");
                if (CanWrite(dir)) Dir = dir;
                else
                {
                    string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniToolbox", "日志");
                    Directory.CreateDirectory(app);
                    Dir = app;
                }
                FilePath = Path.Combine(Dir, "小工具箱.log");
                ready = true;
            }
            catch { ready = false; }
        }

        // 配置目录定下来之后（可能在 APPDATA），把日志跟着挪过去
        public static void Relocate(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return;
            try
            {
                string dir = Path.Combine(baseDir, "日志");
                if (!CanWrite(dir)) return;
                if (string.Equals(dir, Dir, StringComparison.OrdinalIgnoreCase)) return;
                string old = FilePath;
                lock (gate) { Dir = dir; FilePath = Path.Combine(dir, "小工具箱.log"); }
                Info("日志目录切换到 " + FilePath);
                if (File.Exists(old)) { try { File.AppendAllText(FilePath, File.ReadAllText(old, Encoding.UTF8), new UTF8Encoding(false)); File.Delete(old); } catch { } }
            }
            catch { }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write_test");
                File.WriteAllText(probe, "1", new UTF8Encoding(false));
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static void Info(string msg) { Write("INFO", msg, null); }
        public static void Warn(string msg) { Write("WARN", msg, null); }
        public static void Error(string msg, Exception ex) { Write("ERROR", msg, ex); }

        // v19.8：全仓空 catch 收敛到这一处。
        // 默认关闭时是一次静态 bool 判断，没有文件 IO，行为与原来的 catch{} 一致；
        // 需要排查时把 配置.json 的 diagVerbose 设为 1（或调用 SetVerbose），
        // 每个被忽略的异常连同「文件:行」写进日志，既不改变正常路径，也不再让错误凭空消失。
        static bool verbose = false;

        public static bool Verbose { get { return verbose; } }

        public static void SetVerbose(bool on)
        {
            verbose = on;
            try { if (on) Info("详细日志已开启（诊断模式）"); } catch { }
        }

        public static void Swallow(string tag, Exception ex)
        {
            if (!verbose || ex == null) return;
            try
            {
                Write("TRACE", "已忽略 " + tag + " | " + ex.GetType().Name + ": " + ex.Message, null);
            }
            catch { }
        }

        static void Write(string level, string msg, Exception ex)
        {
            if (!ready || FilePath.Length == 0) return;
            try
            {
                lock (gate)
                {
                    // 崩溃风暴保护：同一分钟内同类错误最多记 60 条，避免日志被刷爆
                    if (level == "ERROR")
                    {
                        if ((DateTime.Now - errBurstAt).TotalMinutes >= 1) { errBurstAt = DateTime.Now; errBurst = 0; }
                        errBurst++;
                        if (errBurst > 60) return;
                    }
                    Rotate();
                    StringBuilder sb = new StringBuilder(256);
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ');
                    sb.Append(Clean(msg));
                    if (ex != null)
                    {
                        sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(Clean(ex.Message));
                        if (!string.IsNullOrEmpty(ex.StackTrace))
                            sb.Append("\r\n    ").Append(Clean(ex.StackTrace).Replace("\r\n", "\r\n    ").Replace("\n", "\r\n    "));
                        Exception inner = ex.InnerException;
                        int depth = 0;
                        while (inner != null && depth < 3)
                        {
                            sb.Append("\r\n    内部异常: ").Append(inner.GetType().Name).Append(": ").Append(Clean(inner.Message));
                            inner = inner.InnerException; depth++;
                        }
                    }
                    sb.Append("\r\n");
                    File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch { }
        }

        static void Rotate()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                FileInfo fi = new FileInfo(FilePath);
                if (fi.Length < MAX_BYTES) return;
                string b1 = FilePath + ".1", b2 = FilePath + ".2", b3 = FilePath + ".3";
                try { if (File.Exists(b3)) File.Delete(b3); } catch { }
                try { if (File.Exists(b2)) File.Move(b2, b3); } catch { }
                try { if (File.Exists(b1)) File.Move(b1, b2); } catch { }
                File.Move(FilePath, b1);
            }
            catch { }
        }

        // 脱敏：本机路径、用户名、令牌样式都不落盘
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                if (appDir.Length > 1) s = s.Replace(appDir, "<APP>");
                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user) && user.Length > 1)
                    s = s.Replace("\\Users\\" + user, "\\Users\\<USER>").Replace("/Users/" + user, "/Users/<USER>");
                s = System.Text.RegularExpressions.Regex.Replace(s, @"github_pat_[A-Za-z0-9_]{10,}", "<TOKEN>");
                s = System.Text.RegularExpressions.Regex.Replace(s, @"ghp_[A-Za-z0-9]{10,}", "<TOKEN>");
            }
            catch { }
            return s;
        }

        // 启动头：一次把排查需要的环境信息写齐，之后看日志不用再问用户
        public static void Header(string[] args, long startupMs)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("===== 启动 v").Append(UiKit.VER).Append(" =====");
                sb.Append("\r\n程序: ").Append(Application.ExecutablePath);
                sb.Append("\r\n系统: ").Append(Environment.OSVersion.VersionString).Append(" / ").Append(Environment.Is64BitProcess ? "x64" : "x86");
                sb.Append("\r\n.NET: ").Append(Environment.Version).Append("  处理器: ").Append(Environment.ProcessorCount);
                try
                {
                    Screen sc = Screen.PrimaryScreen;
                    sb.Append("\r\n屏幕: ").Append(sc.Bounds.Width).Append("x").Append(sc.Bounds.Height).Append(" 工作区 ").Append(sc.WorkingArea.Width).Append("x").Append(sc.WorkingArea.Height);
                }
                catch { }
                sb.Append("\r\n参数: ").Append(args == null || args.Length < 2 ? "(无)" : string.Join(" ", args));
                if (startupMs > 0) sb.Append("\r\n启动耗时: ").Append(startupMs).Append(" ms");
                Info(sb.ToString());
            }
            catch { }
        }

        public static void Shutdown(string reason, long runMs)
        {
            try { Info("退出（" + reason + "），本次运行 " + (runMs / 1000) + " 秒"); } catch { }
        }

        // 取日志尾部，供诊断包 / 崩溃窗口复制用
        public static string Tail(int maxLines)
        {
            try
            {
                if (FilePath.Length == 0 || !File.Exists(FilePath)) return "(暂无日志)";
                string[] all = File.ReadAllLines(FilePath, Encoding.UTF8);
                int from = Math.Max(0, all.Length - Math.Max(10, maxLines));
                StringBuilder sb = new StringBuilder();
                for (int i = from; i < all.Length; i++) sb.AppendLine(all[i]);
                return sb.ToString();
            }
            catch (Exception ex) { return "(日志读取失败: " + ex.Message + ")"; }
        }

        // 诊断摘要：版本 + 环境 + 最近错误，供「复制诊断」与反馈使用
        public static string Summary(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.AppendLine("小工具箱 v" + UiKit.VER);
                sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("程序: " + Application.ExecutablePath);
                sb.AppendLine("系统: " + Environment.OSVersion.VersionString + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine("日志: " + (FilePath.Length > 0 ? FilePath : "(未初始化)"));
                if (ex != null) sb.AppendLine("异常: " + ex.GetType().FullName + ": " + ex.Message);
                sb.AppendLine();
                sb.AppendLine("--- 日志尾部 ---");
                sb.Append(Tail(60));
            }
            catch { }
            return sb.ToString();
        }
    }
}
