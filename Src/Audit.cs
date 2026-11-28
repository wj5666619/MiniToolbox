using System;
using System.IO;
using System.Text;

namespace ShutdownTool
{
    /// <summary>
    /// v19.9：审计日志。与诊断日志分开落盘，只记「谁在什么时候做了什么」，
    /// 供企业管理员核对敏感动作（清理、重装、解锁、更新、导出诊断、策略加载）。
    /// </summary>
    internal static class Audit
    {
        const long MAX_BYTES = 1024 * 1024;
        static readonly object gate = new object();
        public static string FilePath = "";

        public static void Init(string cfgDir)
        {
            if (cfgDir == null || cfgDir.Length == 0) return;
            try { FilePath = Path.Combine(Path.Combine(cfgDir, "日志"), "审计.log"); }
            catch (Exception ex) { Diag.Swallow("Audit.cs:Init", ex); }
        }

        public static void Write(string action, string detail)
        {
            if (FilePath.Length == 0) return;
            try
            {
                lock (gate)
                {
                    string dir = Path.GetDirectoryName(FilePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Rotate();
                    StringBuilder sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.Append(" | ").Append(Environment.UserName);
                    sb.Append(" | ").Append(action);
                    if (detail != null && detail.Length > 0) sb.Append(" | ").Append(detail.Replace("\r", " ").Replace("\n", " "));
                    sb.Append("\r\n");
                    File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex) { Diag.Swallow("Audit.cs:Write", ex); }
        }

        static void Rotate()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                if (new FileInfo(FilePath).Length < MAX_BYTES) return;
                string b1 = FilePath + ".1";
                string b2 = FilePath + ".2";
                if (File.Exists(b2)) File.Delete(b2);
                if (File.Exists(b1)) File.Move(b1, b2);
                File.Move(FilePath, b1);
            }
            catch (Exception ex) { Diag.Swallow("Audit.cs:Rotate", ex); }
        }
    }
}
