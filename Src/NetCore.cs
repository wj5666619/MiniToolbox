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
    static class NetCore
    {
        public class Nic
        {
            public string Name;
            public string Ip;
            public string Gateway;
            public string Dns;
            public bool Up;
        }

        public static List<Nic> Nics()
        {
            List<Nic> list = new List<Nic>();
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    System.Net.NetworkInformation.IPInterfaceProperties props = null;
                    try { props = ni.GetIPProperties(); } catch { continue; }
                    string ip = "";
                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        ip = ua.Address.ToString();
                        break;
                    }
                    if (ip.Length == 0) continue;
                    string gw = "-";
                    try
                    {
                        foreach (System.Net.NetworkInformation.GatewayIPAddressInformation g in props.GatewayAddresses)
                        {
                            if (g.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                            gw = g.Address.ToString();
                            break;
                        }
                    }
                    catch (Exception dg62_1) { Diag.Swallow("NetCore.cs:62", dg62_1); }
                    string dns = "";
                    try
                    {
                        foreach (System.Net.IPAddress d in props.DnsAddresses)
                        {
                            if (d.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                            if (dns.Length > 0) dns += ",";
                            dns += d.ToString();
                        }
                    }
                    catch (Exception dg73_1) { Diag.Swallow("NetCore.cs:73", dg73_1); }
                    if (dns.Length == 0) dns = "(DHCP)";
                    Nic n = new Nic();
                    n.Name = ni.Name;
                    n.Ip = ip;
                    n.Gateway = gw;
                    n.Dns = dns;
                    n.Up = ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up;
                    list.Add(n);
                }
            }
            catch (Exception dg84_1) { Diag.Swallow("NetCore.cs:84", dg84_1); }
            return list;
        }

        public static string Run(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append(p.StandardOutput.ReadToEnd());
                    sb.Append(p.StandardError.ReadToEnd());
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception dg104_1) { Diag.Swallow("NetCore.cs:104", dg104_1); } }
                    return sb.ToString();
                }
            }
            catch (Exception ex) { return "EX " + ex.Message; }
        }

        public static bool SetDnsStatic(string nic, string[] addrs, out string err)
        {
            err = "";
            string o1 = Run("netsh.exe", "interface ip set dns name=\"" + nic + "\" source=static address=" + addrs[0] + " register=PRIMARY", 8000);
            if (o1.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0 || o1.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err = T2(o1);
                return false;
            }
            if (addrs.Length > 1 && addrs[1].Length > 0)
                Run("netsh.exe", "interface ip add dns name=\"" + nic + "\" address=" + addrs[1] + " index=2", 8000);
            err = "";
            return true;
        }

        public static bool SetDnsDhcp(string nic, out string err)
        {
            err = "";
            string o = Run("netsh.exe", "interface ip set dns name=\"" + nic + "\" source=dhcp", 8000);
            if (o.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0 || o.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                err = T2(o);
                return false;
            }
            return true;
        }

        static string T2(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 120 ? s.Substring(0, 120) : s;
        }

        public static string HostsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\drivers\\etc\\hosts");
        }

        public static string ReadHosts()
        {
            try
            {
                if (!File.Exists(HostsPath())) return "";
                return File.ReadAllText(HostsPath(), Encoding.UTF8);
            }
            catch (Exception ex) { return "# 读取失败：" + ex.Message; }
        }

        public static bool SaveHosts(string text, out string err)
        {
            err = "";
            try
            {
                string p = HostsPath();
                string bak = p + ".mtb_backup";
                if (File.Exists(p) && !File.Exists(bak)) File.Copy(p, bak, false);
                File.WriteAllText(p, text, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        public static string SubnetPrefix()
        {
            try
            {
                foreach (Nic n in Nics())
                {
                    if (!n.Up) continue;
                    if (n.Ip.StartsWith("127.") || n.Ip.StartsWith("169.254.")) continue;
                    string[] seg = n.Ip.Split('.');
                    if (seg.Length != 4) continue;
                    return seg[0] + "." + seg[1] + "." + seg[2] + ".";
                }
            }
            catch (Exception dg191_1) { Diag.Swallow("NetCore.cs:191", dg191_1); }
            return "";
        }

        public class LanHost
        {
            public string Ip;
            public int Ms;
        }

        public static List<LanHost> ScanLan(string prefix)
        {
            List<LanHost> res = new List<LanHost>();
            if (prefix.Length == 0) return res;
            object lk = new object();
            int pending = 254;
            System.Threading.ManualResetEvent ev = new System.Threading.ManualResetEvent(false);
            for (int i = 1; i <= 254; i++)
            {
                string ip = prefix + i;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate (object o)
                {
                    string sip = (string)o;
                    try
                    {
                        using (System.Net.NetworkInformation.Ping pg = new System.Net.NetworkInformation.Ping())
                        {
                            System.Net.NetworkInformation.PingReply r = pg.Send(sip, 300);
                            if (r != null && r.Status == System.Net.NetworkInformation.IPStatus.Success)
                            {
                                LanHost h = new LanHost();
                                h.Ip = sip;
                                h.Ms = (int)r.RoundtripTime;
                                lock (lk) { res.Add(h); }
                            }
                        }
                    }
                    catch (Exception dg228_1) { Diag.Swallow("NetCore.cs:228", dg228_1); }
                    if (System.Threading.Interlocked.Decrement(ref pending) == 0) ev.Set();
                }, ip);
            }
            ev.WaitOne(40000);
            res.Sort(delegate (LanHost a, LanHost b) { return NetCore.IpNum(a.Ip).CompareTo(NetCore.IpNum(b.Ip)); });
            return res;
        }

        public static long IpNum(string ip)
        {
            string[] seg = ip.Split('.');
            long n = 0;
            if (seg.Length == 4)
                for (int i = 0; i < 4; i++)
                {
                    int v = 0;
                    int.TryParse(seg[i], out v);
                    n = n * 256 + v;
                }
            return n;
        }

        public static Dictionary<string, string> ArpTable()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            string o = Run("arp.exe", "-a", 6000);
            string[] lines = o.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                string[] seg = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (seg.Length < 2) continue;
                string ip = seg[0];
                string mac = seg[1];
                if (mac.IndexOf('-') < 0 && mac.IndexOf(':') < 0) continue;
                if (!d.ContainsKey(ip)) d[ip] = mac;
            }
            return d;
        }

        // 实测下载速度：先试 Cloudflare 测速接口，失败再用 GitHub 上的真实文件
        public static double TestDownload(out string info)
        {
            info = "";
            string[] urls = {
                "https://speed.cloudflare.com/__down?bytes=5000000",
                "https://github.com/wj5666619/MiniToolbox/releases/download/6.5/video_tool.zip"
            };
            foreach (string u in urls)
            {
                try
                {
                    System.Net.WebClient wc = new System.Net.WebClient();
                    wc.Headers.Add("User-Agent", "MiniToolbox/7.1");
                    DateTime t0 = DateTime.Now;
                    byte[] data = wc.DownloadData(u);
                    double sec = (DateTime.Now - t0).TotalSeconds;
                    if (data == null || data.Length < 200000) continue;
                    double mbps = (data.Length * 8.0 / 1000000.0) / Math.Max(0.05, sec);
                    info = Math.Round(data.Length / 1048576.0, 1) + " MB / " + Math.Round(sec, 1) + " s";
                    return mbps;
                }
                catch (Exception ex) { info = ex.Message; }
            }
            return 0;
        }
    }
}