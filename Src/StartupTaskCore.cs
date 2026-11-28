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
    static class StartupTaskCore
    {
        public class TaskItem
        {
            public string Name = "";
            public string Folder = "";
            public string State = "";
            public string Run = "";
            public string When = "";
            public bool IsTask = false;
        }

        public static List<TaskItem> List(bool includeSystem, out string err)
        {
            err = "";
            List<TaskItem> res = new List<TaskItem>();
            try
            {
                string outp = Exec("schtasks.exe", "/query /fo CSV /v /nh", 20000);
                string[] lines = outp.Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length < 5 || line[0] != '"') continue;
                    List<string> cols = SplitCsv(line);
                    if (cols.Count < 9) continue;
                    TaskItem it = new TaskItem();
                    it.IsTask = true;
                    it.Name = cols[1];
                    it.State = cols[2];
                    it.When = cols[5];
                    it.Run = cols[8];
                    int bs = it.Name.LastIndexOf('\\');
                    it.Folder = bs > 0 ? it.Name.Substring(0, bs) : "\\";
                    bool sys = it.Folder.StartsWith("\\Microsoft");
                    if (sys && !includeSystem) continue;
                    res.Add(it);
                }
            }
            catch (Exception ex) { err = ex.Message; }
            return res;
        }

        public static bool SplitCsvTest()
        {
            List<string> c = SplitCsv("\"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\",\"Ready\",\"Running\",\"12:00:00\",\"2026/11/16\",\"N/A\",\"Interactive/Background\",\"Built-in\"");
            return c.Count >= 8 && c[1] == "Ready";
        }

        static List<string> SplitCsv(string line)
        {
            List<string> cols = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (q && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else q = !q;
                }
                else if (c == ',' && !q) { cols.Add(sb.ToString()); sb.Length = 0; }
                else if (c == '\r') { }
                else sb.Append(c);
            }
            cols.Add(sb.ToString());
            return cols;
        }

        public static string Exec(string exe, string args, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process p = Process.Start(psi);
            StringBuilder sb = new StringBuilder();
            long limit = DateTime.Now.Ticks + (long)timeoutMs * 10000L;
            try
            {
                while (DateTime.Now.Ticks < limit)
                {
                    if (p.HasExited) break;
                    System.Threading.Thread.Sleep(20);
                }
                if (!p.HasExited) { try { p.Kill(); } catch (Exception dg112_1) { Diag.Swallow("StartupTaskCore.cs:112", dg112_1); } }
                string so = p.StandardOutput.ReadToEnd();
                string se = p.StandardError.ReadToEnd();
                sb.Append(so);
                if (se.Length > 0) sb.Append(se);
            }
            catch (Exception ex) { sb.Append(ex.Message); }
            return sb.ToString();
        }

        public static bool Change(string taskName, bool enable, out string err)
        {
            err = "";
            string args = "/change /tn \"" + taskName + "\" " + (enable ? "/enable" : "/disable");
            string o = Exec("schtasks.exe", args, 15000);
            if (o.IndexOf("成功") >= 0 || o.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (o.IndexOf("错误", StringComparison.Ordinal) >= 0 || o.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err = o.Trim();
                if (err.Length > 300) err = err.Substring(0, 300);
                return false;
            }
            return true;
        }

        public static bool RunNow(string taskName, out string err)
        {
            err = "";
            string o = Exec("schtasks.exe", "/run /tn \"" + taskName + "\"", 15000);
            if (o.IndexOf("错误", StringComparison.Ordinal) >= 0 || o.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err = o.Trim();
                return false;
            }
            return true;
        }

        public static bool Delete(string taskName, out string err)
        {
            err = "";
            string o = Exec("schtasks.exe", "/delete /f /tn \"" + taskName + "\"", 15000);
            if (o.IndexOf("错误", StringComparison.Ordinal) >= 0 || o.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err = o.Trim();
                return false;
            }
            return true;
        }

        // 新建：开机自启 或 每天定时
        public static bool Create(string name, string cmd, bool onLogon, string everyDayAt, out string err)
        {
            err = "";
            string args;
            if (onLogon)
                args = "/create /tn \"" + name + "\" /tr \"" + cmd + "\" /sc onlogon /rl highest /f";
            else
                args = "/create /tn \"" + name + "\" /tr \"" + cmd + "\" /sc daily /st " + everyDayAt + " /rl highest /f";
            string o = Exec("schtasks.exe", args, 20000);
            if (o.IndexOf("错误", StringComparison.Ordinal) >= 0 || o.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err = o.Trim();
                if (err.Length > 400) err = err.Substring(0, 400);
                return false;
            }
            return true;
        }

        public static void OpenStartupSettings()
        {
            try { Process.Start("taskmgr.exe", "/0 /startup"); } catch (Exception dg182_1) { Diag.Swallow("StartupTaskCore.cs:182", dg182_1); }
        }
    }
}