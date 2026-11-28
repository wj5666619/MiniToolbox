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
    static class PortCore
    {
        public class Row
        {
            public string Proto;
            public string Local;
            public int LPort;
            public string Remote;
            public string State;
            public int Pid;
            public string Proc;
            public string Exe;
        }

        static Dictionary<int, string[]> cache = new Dictionary<int, string[]>();

        public static string ExecText(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                Process p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd();
                try { p.StandardError.ReadToEnd(); } catch (Exception dg52_1) { Diag.Swallow("PortCore.cs:52", dg52_1); }
                if (timeoutMs > 0) p.WaitForExit(timeoutMs);
                return o;
            }
            catch (Exception ex) { return "ERR " + ex.Message; }
        }

        static int ParseInt(string s)
        {
            int v = 0;
            int.TryParse(s.Trim(), out v);
            return v;
        }

        public static int PortOf(string addr)
        {
            if (addr == null || addr.Length == 0) return 0;
            int i = addr.LastIndexOf(':');
            if (i < 0 || i + 1 >= addr.Length) return 0;
            return ParseInt(addr.Substring(i + 1));
        }

        static string[] ProcInfo(int pid)
        {
            if (pid <= 0 || pid == 4) return new string[] { pid == 4 ? "System" : "-", "" };
            if (cache.ContainsKey(pid)) return cache[pid];
            string[] r = new string[] { "?", "" };
            try
            {
                Process p = Process.GetProcessById(pid);
                r[0] = p.ProcessName;
                try { r[1] = p.MainModule.FileName; } catch (Exception) { r[1] = ""; }
            }
            catch (Exception) { r[0] = "已退出"; }
            cache[pid] = r;
            return r;
        }

        public static void ClearCache() { cache.Clear(); }

        public static List<Row> Scan()
        {
            List<Row> rows = new List<Row>();
            string text = ExecText("netstat.exe", "-ano", 15000);
            string[] lines = text.Split('\n');
            foreach (string raw in lines)
            {
                string ln = raw.Trim();
                if (ln.Length == 0) continue;
                if (!(ln.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) || ln.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))) continue;
                string[] f = ln.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 4) continue;
                Row r = new Row();
                r.Proto = f[0].ToUpperInvariant();
                r.Local = f[1];
                r.Remote = f[2];
                if (f.Length >= 5) { r.State = f[3]; r.Pid = ParseInt(f[4]); }
                else { r.State = "-"; r.Pid = ParseInt(f[3]); }
                r.LPort = PortOf(r.Local);
                string[] pi = ProcInfo(r.Pid);
                r.Proc = pi[0];
                r.Exe = pi[1];
                rows.Add(r);
            }
            return rows;
        }

        public static int ListenCount(List<Row> rows)
        {
            int n = 0;
            foreach (Row r in rows) if (string.Equals(r.State, "LISTENING", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        public static int DistinctPortCount(List<Row> rows)
        {
            Dictionary<int, bool> seen = new Dictionary<int, bool>();
            foreach (Row r in rows)
            {
                if (r.LPort > 0) seen[r.LPort] = true;
            }
            return seen.Count;
        }

        public static bool KillPid(int pid, out string err)
        {
            err = "";
            if (pid <= 0 || pid == 4) { err = "系统进程不能结束"; return false; }
            try
            {
                string o = ExecText("taskkill.exe", "/PID " + pid + " /F", 10000);
                if (o.IndexOf("ERR", StringComparison.Ordinal) >= 0 && o.IndexOf("ERR ", StringComparison.Ordinal) == 0) { err = o; return false; }
                cache.Remove(pid);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static bool KillTree(int pid, out string err)
        {
            err = "";
            if (pid <= 0 || pid == 4) { err = "系统进程不能结束"; return false; }
            try
            {
                string o = ExecText("taskkill.exe", "/PID " + pid + " /T /F", 12000);
                cache.Remove(pid);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        // 自测用：不依赖窗体的纯逻辑
        public static string SelfTestText()
        {
            StringBuilder sb = new StringBuilder();
            List<Row> rows = Scan();
            sb.AppendLine("rows=" + rows.Count);
            sb.AppendLine("listen=" + ListenCount(rows));
            sb.AppendLine("ports=" + DistinctPortCount(rows));
            int n = 0;
            foreach (Row r in rows)
            {
                if (string.Equals(r.State, "LISTENING", StringComparison.OrdinalIgnoreCase) && r.LPort > 0)
                {
                    if (n++ >= 6) break;
                    sb.AppendLine(r.LPort + " | " + r.Proto + " | pid=" + r.Pid + " | " + r.Proc + " | " + r.Exe);
                }
            }
            sb.AppendLine("portof_ok=" + (PortOf("0.0.0.0:135") == 135 && PortOf("[::]:445") == 445 && PortOf("bad") == 0));
            return sb.ToString();
        }
    }
}