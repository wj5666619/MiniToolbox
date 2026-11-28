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
    static class AdbCore
    {
        public const string PtUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

        public static string FindAdb(string baseDir)
        {
            string[] direct = new string[] {
                Path.Combine(baseDir, "adb_tool", "platform-tools", "adb.exe"),
                Path.Combine(baseDir, "adb_tool", "adb.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\platform-tools\adb.exe"
            };
            foreach (string p in direct) if (File.Exists(p)) return p;
            try
            {
                string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
                foreach (string d in paths)
                {
                    if (d == null || d.Trim().Length == 0) continue;
                    string p = Path.Combine(d.Trim(), "adb.exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch (Exception dg46_1) { Diag.Swallow("AdbCore.cs:46", dg46_1); }
            try
            {
                string[] found = Directory.GetFiles(baseDir, "adb.exe", SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
            catch (Exception dg52_1) { Diag.Swallow("AdbCore.cs:52", dg52_1); }
            return "";
        }

        public static bool Download(string url, string dst, Action<string> log, out string err)
        {
            err = "";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "MiniToolbox/7.2";
                req.Timeout = 60000;
                req.ReadWriteTimeout = 120000;
                using (WebResponse resp = req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[8192];
                    long total = 0;
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n);
                        total += n;
                        if (log != null && total % (1024 * 1024) < 9000) log("下载中 " + (total / 1024) + " KB");
                    }
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static bool Deploy(string baseDir, Action<string> log, out string err)
        {
            err = "";
            string zip = Path.Combine(baseDir, "adb_tool.zip");
            if (!Download(PtUrl, zip, log, out err)) return false;
            try
            {
                string dir = Path.Combine(baseDir, "adb_tool");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                ZipFile.ExtractToDirectory(zip, dir);
                try { File.Delete(zip); } catch (Exception dg94_1) { Diag.Swallow("AdbCore.cs:94", dg94_1); }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static string Run(string adb, string args, int timeoutMs)
        {
            return PortCore.ExecText(adb, args, timeoutMs);
        }

        // 二进制导出（截图这类 stdout 直接是 PNG 的命令，走文本捕获会被搞坏）
        public static bool RunToFile(string adb, string args, string outFile, int timeoutMs, out string err)
        {
            err = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(adb, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = false;
                psi.WorkingDirectory = Path.GetDirectoryName(outFile);
                Process p = Process.Start(psi);
                long got = 0;
                using (FileStream fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
                {
                    Stream src = p.StandardOutput.BaseStream;
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n);
                        got += n;
                    }
                }
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception dg132_1) { Diag.Swallow("AdbCore.cs:132", dg132_1); }
                    err = "超时";
                    return false;
                }
                if (p.ExitCode != 0) { err = "adb 退出码 " + p.ExitCode; return false; }
                if (got <= 0) { err = "没有输出"; return false; }
                return true;
            }
            catch (Exception e) { err = e.Message; return false; }
        }

        public static List<string> Devices(string adb)
        {
            List<string> list = new List<string>();
            if (adb.Length == 0) return list;
            string o = Run(adb, "devices", 15000);
            string[] lines = o.Replace("\r", "").Split('\n');
            foreach (string ln in lines)
            {
                string t = ln.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
                string[] f = t.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 2) list.Add(f[0] + " (" + f[1] + ")");
            }
            return list;
        }

        public static string SerialOf(string item)
        {
            if (item == null) return "";
            int i = item.IndexOf(' ');
            return i > 0 ? item.Substring(0, i) : item;
        }

        public static string SelfTestText(string baseDir)
        {
            StringBuilder sb = new StringBuilder();
            string adb = FindAdb(baseDir);
            sb.AppendLine("adb=" + (adb.Length > 0 ? adb : "missing"));
            if (adb.Length > 0)
            {
                string ver = Run(adb, "version", 15000);
                string first = ver.Replace("\r", "").Split('\n')[0].Trim();
                sb.AppendLine("version=" + first);
                List<string> dev = Devices(adb);
                sb.AppendLine("devices=" + dev.Count);
                foreach (string d in dev) sb.AppendLine("  " + d);
            }
            bool reach = false;
            string rerr = "";
            try
            {
                using (System.Net.Sockets.TcpClient tc = new System.Net.Sockets.TcpClient())
                {
                    IAsyncResult ar = tc.BeginConnect("dl.google.com", 443, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(6000) && tc.Connected) reach = true;
                    else rerr = "连接超时（代理/网络不通）";
                }
            }
            catch (Exception ex) { rerr = ex.Message; }
            sb.AppendLine("platform_tools_reachable=" + reach + (reach ? "" : " " + rerr));
            sb.AppendLine("serial_parse_ok=" + (SerialOf("emulator-5554 (device)") == "emulator-5554"));
            return sb.ToString();
        }
    }
}