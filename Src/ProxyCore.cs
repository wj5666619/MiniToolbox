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
    static class ProxyCore
    {
        public class Node
        {
            public string Src;
            public string Name;
            public string Host;
            public int Port;
            public string Type;
            public int Latency = -1;
        }

        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        static void RefreshInet()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch (Exception dg45_1) { Diag.Swallow("ProxyCore.cs:45", dg45_1); }
        }

        public static string Feature(string dir)
        {
            if (Directory.Exists(Path.Combine(dir, "guiConfigs"))) return "v2rayN";
            if (Directory.Exists(Path.Combine(dir, "binConfigs"))) return "v2rayN";
            if (File.Exists(Path.Combine(dir, "config.yaml"))) return "clash";
            if (File.Exists(Path.Combine(dir, "config.yml"))) return "clash";
            return "";
        }

        // 扫各固定盘根下两层，认「guiConfigs / binConfigs / config.yaml」这类特征目录
        public static List<string> AutoDiscover()
        {
            List<string> hits = new List<string>();
            try
            {
                DriveInfo[] ds = DriveInfo.GetDrives();
                foreach (DriveInfo d in ds)
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed) continue;
                        string root = d.RootDirectory.FullName;
                        string[] one = Directory.GetDirectories(root);
                        foreach (string lv1 in one)
                        {
                            try
                            {
                                if (Feature(lv1).Length > 0) { hits.Add(lv1); continue; }
                                string[] two = Directory.GetDirectories(lv1);
                                foreach (string lv2 in two)
                                {
                                    if (Feature(lv2).Length > 0) hits.Add(lv2);
                                }
                            }
                            catch (Exception dg83_1) { Diag.Swallow("ProxyCore.cs:83", dg83_1); }
                        }
                    }
                    catch (Exception dg86_1) { Diag.Swallow("ProxyCore.cs:86", dg86_1); }
                }
            }
            catch (Exception dg89_1) { Diag.Swallow("ProxyCore.cs:89", dg89_1); }
            return hits;
        }

        public static string CacheFile(string baseDir) { return Path.Combine(baseDir, "代理节点目录.txt"); }

        public static List<string> Dirs(string baseDir)
        {
            List<string> list = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            list.Add(Path.Combine(appdata, "v2rayN"));
            list.Add(Path.Combine(local, "v2rayN"));
            list.Add(Path.Combine(home, "v2rayN"));
            list.Add(Path.Combine(baseDir, "v2rayN"));
            list.Add(Path.Combine(appdata, "clash"));
            list.Add(Path.Combine(home, ".config", "clash"));
            list.Add(Path.Combine(home, ".config", "clash-verge"));
            list.Add(Path.Combine(local, "clash-verge"));
            list.Add(Path.Combine(baseDir, "clash"));
            list.Add(Path.Combine(baseDir, "mihomo"));

            string cache = CacheFile(baseDir);
            bool scanned = false;
            try
            {
                if (File.Exists(cache))
                {
                    string[] got = File.ReadAllLines(cache, Encoding.UTF8);
                    foreach (string ln in got)
                    {
                        string t = ln.Trim();
                        if (t.Length > 0 && Directory.Exists(t) && !list.Contains(t)) list.Add(t);
                    }
                    scanned = got.Length > 0;
                }
            }
            catch (Exception dg127_1) { Diag.Swallow("ProxyCore.cs:127", dg127_1); }

            if (!scanned)
            {
                foreach (string d in AutoDiscover())
                {
                    if (!list.Contains(d)) list.Add(d);
                    try { File.AppendAllText(cache, d + "\r\n", Encoding.UTF8); } catch (Exception dg134_1) { Diag.Swallow("ProxyCore.cs:134", dg134_1); }
                }
            }
            return list;
        }

        public static bool AddDir(string baseDir, string dir)
        {
            try
            {
                if (dir == null || dir.Trim().Length == 0) return false;
                if (!Directory.Exists(dir)) return false;
                File.AppendAllText(CacheFile(baseDir), dir.Trim() + "\r\n", Encoding.UTF8);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static List<Node> Scan(string baseDir, out string log)
        {
            StringBuilder sb = new StringBuilder();
            List<Node> nodes = new List<Node>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            foreach (string d in Dirs(baseDir))
            {
                if (!Directory.Exists(d)) continue;
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(d, "*.json", SearchOption.AllDirectories);
                }
                catch (Exception) { files = new string[0]; }
                foreach (string f in files)
                {
                    try
                    {
                        if (new FileInfo(f).Length > 12L * 1024 * 1024) continue;
                        string txt = File.ReadAllText(f, Encoding.UTF8);
                        object o = new JavaScriptSerializer().DeserializeObject(txt);
                        string tag = Feature(d);
                        if (tag.Length == 0) tag = Path.GetFileName(d);
                        Walk(o, tag, d, nodes, seen);
                    }
                    catch (Exception dg177_1) { Diag.Swallow("ProxyCore.cs:177", dg177_1); }
                }
                string[] yml = null;
                try { yml = Directory.GetFiles(d, "*.yaml", SearchOption.AllDirectories); }
                catch (Exception) { yml = new string[0]; }
                foreach (string f in yml) ParseClash(File.ReadAllText(f, Encoding.UTF8), Path.GetFileName(d), nodes, seen);
                string[] yml2 = null;
                try { yml2 = Directory.GetFiles(d, "*.yml", SearchOption.AllDirectories); }
                catch (Exception) { yml2 = new string[0]; }
                foreach (string f in yml2) ParseClash(File.ReadAllText(f, Encoding.UTF8), Path.GetFileName(d), nodes, seen);
            }
            sb.AppendLine("nodes=" + nodes.Count);
            log = sb.ToString();
            return nodes;
        }

        static void Walk(object o, string src, string dir, List<Node> nodes, Dictionary<string, bool> seen)
        {
            Walk(o, src, dir, nodes, seen, "", "");
        }

        static void Walk(object o, string src, string dir, List<Node> nodes, Dictionary<string, bool> seen, string ctx, string ctxType)
        {
            if (o == null) return;
            if (o is Dictionary<string, object>)
            {
                Dictionary<string, object> d = (Dictionary<string, object>)o;
                string nm = Str(d, "remark");
                if (nm.Length == 0) nm = Str(d, "ps");
                if (nm.Length == 0) nm = Str(d, "name");
                if (nm.Length == 0) nm = Str(d, "tag");
                if (nm.Length == 0) nm = ctx;
                string ty = Str(d, "protocol");
                if (ty.Length == 0) ty = Str(d, "type");
                string host = Str(d, "address");
                if (host.Length == 0) host = Str(d, "add");
                if (host.Length == 0) host = Str(d, "server");
                int port = Num(d, "port");
                if (host.Length > 0 && port > 0)
                {
                    Node n = new Node();
                    n.Src = src;
                    n.Host = host;
                    n.Port = port;
                    n.Name = nm;
                    n.Type = ty.Length > 0 ? ty : ctxType;
                    string key = host + ":" + port;
                    if (!seen.ContainsKey(key))
                    {
                        seen[key] = true;
                        nodes.Add(n);
                    }
                }
                foreach (KeyValuePair<string, object> kv in d) Walk(kv.Value, src, dir, nodes, seen, nm, ty.Length > 0 ? ty : ctxType);
                return;
            }
            if (o is object[])
            {
                foreach (object it in (object[])o) Walk(it, src, dir, nodes, seen, ctx, ctxType);
                return;
            }
            if (o is System.Collections.ArrayList)
            {
                foreach (object it in (System.Collections.ArrayList)o) Walk(it, src, dir, nodes, seen, ctx, ctxType);
            }
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return "";
            object o = d[key];
            if (o == null) return "";
            string s = o as string;
            return s == null ? "" : s.Trim();
        }

        static int Num(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return 0;
            object o = d[key];
            if (o == null) return 0;
            int v = 0;
            if (o is int) return (int)o;
            int.TryParse(o.ToString().Trim(), out v);
            return v;
        }

        public static void ParseClash(string text, string src, List<Node> nodes, Dictionary<string, bool> seen)
        {
            if (text == null) return;
            string[] lines = text.Split('\n');
            string name = "", host = "", type = "";
            int port = 0;
            bool inProxies = false;
            foreach (string raw in lines)
            {
                string ln = raw.Replace("\r", "");
                string t = ln.Trim();
                if (t.StartsWith("proxies:", StringComparison.Ordinal)) { inProxies = true; continue; }
                if (inProxies && ln.Length > 0 && ln[0] != ' ' && ln[0] != '-' && !t.StartsWith("- ")) { inProxies = false; }
                if (!inProxies) continue;
                if (t.StartsWith("- ", StringComparison.Ordinal))
                {
                    Flush(src, name, host, port, type, nodes, seen);
                    name = ""; host = ""; port = 0; type = "";
                    string rest = t.Substring(2).Trim();
                    if (rest.StartsWith("name:", StringComparison.Ordinal)) name = Cut(rest);
                    continue;
                }
                if (t.StartsWith("name:", StringComparison.Ordinal)) name = Cut(t);
                else if (t.StartsWith("server:", StringComparison.Ordinal)) host = Cut(t);
                else if (t.StartsWith("port:", StringComparison.Ordinal))
                {
                    int p = 0;
                    int.TryParse(Cut(t), out p);
                    port = p;
                }
                else if (t.StartsWith("type:", StringComparison.Ordinal)) type = Cut(t);
            }
            Flush(src, name, host, port, type, nodes, seen);
        }

        static string Cut(string s)
        {
            int i = s.IndexOf(':');
            if (i < 0) return "";
            string v = s.Substring(i + 1).Trim();
            if (v.Length > 1 && (v[0] == '"' || v[0] == '\''))
            {
                char q = v[0];
                int j = v.IndexOf(q, 1);
                if (j > 0) v = v.Substring(1, j - 1);
            }
            int hash = v.IndexOf(" #", StringComparison.Ordinal);
            if (hash > 0) v = v.Substring(0, hash).Trim();
            return v;
        }

        static void Flush(string src, string name, string host, int port, string type, List<Node> nodes, Dictionary<string, bool> seen)
        {
            if (host.Length == 0 || port <= 0) return;
            string key = host + ":" + port;
            if (seen.ContainsKey(key)) return;
            seen[key] = true;
            Node n = new Node();
            n.Src = src;
            n.Name = name;
            n.Host = host;
            n.Port = port;
            n.Type = type;
            nodes.Add(n);
        }

        public static int Ping(string host, int port, int timeoutMs)
        {
            int best = -1;
            for (int i = 0; i < 3; i++)
            {
                DateTime t = DateTime.Now;
                try
                {
                    TcpClient c = new TcpClient();
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) { try { c.Close(); } catch (Exception dg340_1) { Diag.Swallow("ProxyCore.cs:340", dg340_1); } continue; }
                    c.EndConnect(ar);
                    int ms = (int)(DateTime.Now - t).TotalMilliseconds;
                    if (best < 0 || ms < best) best = ms;
                    c.Close();
                }
                catch (Exception dg346_1) { Diag.Swallow("ProxyCore.cs:346", dg346_1); }
            }
            return best;
        }

        public static string RegGet(string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    if (k == null) return "";
                    object o = k.GetValue(name);
                    return o == null ? "" : o.ToString();
                }
            }
            catch (Exception) { return ""; }
        }

        public static bool BackupProxy(string baseDir, out string err)
        {
            err = "";
            try
            {
                string txt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\tProxyEnable=" + RegGet("ProxyEnable")
                    + "\tProxyServer=" + RegGet("ProxyServer") + "\tProxyOverride=" + RegGet("ProxyOverride") + "\r\n";
                File.AppendAllText(Path.Combine(baseDir, "系统代理备份.txt"), txt, Encoding.UTF8);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static bool SetSystemProxy(string baseDir, string hostPort, out string err)
        {
            err = "";
            BackupProxy(baseDir, out err);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (k == null) { err = "打不开 Internet Settings"; return false; }
                    k.SetValue("ProxyServer", hostPort, RegistryValueKind.String);
                    k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    string ov = null;
                    object cur = k.GetValue("ProxyOverride");
                    if (cur != null) ov = cur.ToString();
                    if (ov == null || ov.Length == 0) k.SetValue("ProxyOverride", "localhost;127.*;10.*;192.168.*;<local>", RegistryValueKind.String);
                }
                RefreshInet();
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static bool ClearSystemProxy(string baseDir, out string err)
        {
            err = "";
            BackupProxy(baseDir, out err);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (k == null) { err = "打不开 Internet Settings"; return false; }
                    k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }
                RefreshInet();
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static string FindClient(string baseDir, string tag)
        {
            List<string> dirs = Dirs(baseDir);
            string[] names = tag == "clash"
                ? new string[] { "Clash for Windows.exe", "clash-verge.exe", "Clash Verge.exe", "mihomo.exe", "clash.exe" }
                : new string[] { "v2rayN.exe", "v2rayN_core.exe" };
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                string parent = Path.GetDirectoryName(d);
                foreach (string n in names)
                {
                    string p = Path.Combine(d, n);
                    if (File.Exists(p)) return p;
                    if (parent != null)
                    {
                        string p2 = Path.Combine(parent, n);
                        if (File.Exists(p2)) return p2;
                    }
                }
                try
                {
                    string[] found = Directory.GetFiles(d, "*.exe", SearchOption.TopDirectoryOnly);
                    foreach (string f in found)
                    {
                        string fn = Path.GetFileName(f).ToLowerInvariant();
                        if (tag == "clash" && fn.IndexOf("clash") >= 0) return f;
                        if (tag != "clash" && fn.IndexOf("v2ray") >= 0) return f;
                    }
                }
                catch (Exception dg447_1) { Diag.Swallow("ProxyCore.cs:447", dg447_1); }
            }
            return "";
        }

        public static string SelfTestText(string baseDir)
        {
            StringBuilder sb = new StringBuilder();
            string sample = "{\"servers\":[{\"remarks\":\"香港01\",\"address\":\"hk1.example.com\",\"port\":443,\"protocol\":\"vmess\"},"
                + "{\"ps\":\"日本02\",\"add\":\"jp2.example.com\",\"port\":8443,\"protocol\":\"trojan\"}]}";
            List<Node> nodes = new List<Node>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            object o = new JavaScriptSerializer().DeserializeObject(sample);
            Walk(o, "sample", "", nodes, seen);
            sb.AppendLine("json_nodes=" + nodes.Count);
            string yml = "proxies:\n  - name: \"新加坡03\"\n    type: ss\n    server: sg3.example.com\n    port: 8388\n    cipher: aes-128-gcm\n"
                + "  - name: \"美国04\"\n    type: vmess\n    server: us4.example.com\n    port: 443\n";
            List<Node> n2 = new List<Node>();
            Dictionary<string, bool> seen2 = new Dictionary<string, bool>();
            ParseClash(yml, "sample", n2, seen2);
            sb.AppendLine("yaml_nodes=" + n2.Count);
            foreach (Node n in n2) sb.AppendLine("  " + n.Name + " " + n.Host + ":" + n.Port + " " + n.Type);
            sb.AppendLine("dedup_ok=" + (Dis(new string[] { "a:1", "a:1" }) == 1));
            string host = "127.0.0.1";
            int port = 0;
            List<PortCore.Row> rows = PortCore.Scan();
            foreach (PortCore.Row r in rows)
            {
                if (string.Equals(r.State, "LISTENING", StringComparison.OrdinalIgnoreCase) && r.LPort > 0) { port = r.LPort; break; }
            }
            int ms = port > 0 ? Ping(host, port, 800) : -1;
            sb.AppendLine("ping_local=" + host + ":" + port + " -> " + ms + "ms (ok=" + (ms >= 0) + ")");
            sb.AppendLine("dirs_exist=" + ExistingDirs(baseDir));
            string slog = "";
            List<Node> real = Scan(baseDir, out slog);
            sb.AppendLine("real_nodes=" + real.Count);
            int showN = 0;
            foreach (Node n in real)
            {
                if (showN++ >= 6) break;
                sb.AppendLine("  " + n.Src + " | " + n.Name + " | " + n.Host + ":" + n.Port + " | " + n.Type);
            }
            sb.AppendLine("reg_proxy=" + RegGet("ProxyEnable") + " / " + RegGet("ProxyServer"));
            return sb.ToString();
        }

        static int Dis(string[] a)
        {
            Dictionary<string, bool> d = new Dictionary<string, bool>();
            foreach (string s in a) d[s] = true;
            return d.Count;
        }

        static string ExistingDirs(string baseDir)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string d in Dirs(baseDir))
            {
                if (Directory.Exists(d))
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(d);
                }
            }
            return sb.Length == 0 ? "(未发现)" : sb.ToString();
        }
    }
}