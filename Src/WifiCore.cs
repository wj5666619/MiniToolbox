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
    static class WifiCore
    {
        public class Ap
        {
            public string Ssid = "";
            public string Bssid = "";
            public string Auth = "";
            public string Band = "";
            public int Signal = 0;
            public int Channel = 0;
        }

        public static string Run(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                try { psi.StandardOutputEncoding = Encoding.GetEncoding(936); }
                catch (Exception dg44_1) { Diag.Swallow("WifiCore.cs:44", dg44_1); }
                Process p = Process.Start(psi);
                string s = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return s;
            }
            catch (Exception) { return ""; }
        }

        static bool IsKey(string kl, params string[] keys)
        {
            if (kl.Length == 0) return false;
            foreach (string k in keys) if (kl.StartsWith(k)) return true;
            return false;
        }

        public static List<Ap> Scan()
        {
            return ParseText(Run("wlan show networks mode=bssid"));
        }

        public static List<Ap> ParseText(string txt)
        {
            List<Ap> list = new List<Ap>();
            if (txt == null || txt.Length == 0) return list;
            string ssid = "";
            string auth = "";
            Ap cur = null;
            string[] lines = txt.Split('\n');
            foreach (string raw in lines)
            {
                string ln = raw.Trim();
                if (ln.Length == 0) continue;
                int c = ln.IndexOf(':');
                if (c <= 0) continue;
                string k = ln.Substring(0, c).Trim();
                string v = ln.Substring(c + 1).Trim();
                string kl = k.ToLowerInvariant();
                if (kl.StartsWith("ssid") && !kl.StartsWith("bssid"))
                {
                    ssid = v;
                    cur = null;
                    continue;
                }
                if (IsKey(kl, "身份验证", "认证", "authentication")) { auth = v; continue; }
                if (kl.StartsWith("bssid"))
                {
                    cur = new Ap();
                    cur.Ssid = ssid;
                    cur.Auth = auth;
                    cur.Bssid = v;
                    list.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                if (IsKey(kl, "信号", "signal"))
                {
                    string num = v.Replace("%", "").Trim();
                    int s = 0;
                    if (int.TryParse(num, out s)) cur.Signal = s;
                    continue;
                }
                if (IsKey(kl, "频带", "频段", "band")) { cur.Band = v; continue; }
                if (IsKey(kl, "通道", "信道", "channel"))
                {
                    int s = 0;
                    if (int.TryParse(v, out s)) cur.Channel = s;
                    continue;
                }
            }
            foreach (Ap a in list)
                if (a.Band.Length == 0) a.Band = a.Channel > 14 ? "5 GHz" : "2.4 GHz";
            return list;
        }

        public static string Current()
        {
            string txt = Run("wlan show interfaces");
            if (txt.Length == 0) return "";
            string state = "";
            string ssid = "";
            string signal = "";
            string ch = "";
            string rate = "";
            string[] lines = txt.Split('\n');
            foreach (string raw in lines)
            {
                string ln = raw.Trim();
                if (ln.Length == 0) continue;
                int c = ln.IndexOf(':');
                if (c <= 0) continue;
                string k = ln.Substring(0, c).Trim();
                string v = ln.Substring(c + 1).Trim();
                string kl = k.ToLowerInvariant();
                if (IsKey(kl, "状态", "state")) state = v;
                else if (kl.StartsWith("ssid") && !kl.StartsWith("bssid")) ssid = v;
                else if (IsKey(kl, "信号", "signal")) signal = v;
                else if (IsKey(kl, "通道", "信道", "channel")) ch = v;
                else if (IsKey(kl, "接收速率", "收发速率", "receive rate")) rate = v;
            }
            if (state.Length == 0) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append(state);
            if (ssid.Length > 0) sb.Append(" · " + ssid);
            if (signal.Length > 0) sb.Append(" · 信号 " + signal);
            if (ch.Length > 0) sb.Append(" · 信道 " + ch);
            if (rate.Length > 0) sb.Append(" · " + rate);
            return sb.ToString();
        }

        public static Dictionary<int, int> ChannelLoad(List<Ap> list)
        {
            Dictionary<int, int> d = new Dictionary<int, int>();
            foreach (Ap a in list)
            {
                if (a.Channel <= 0) continue;
                if (d.ContainsKey(a.Channel)) d[a.Channel] = d[a.Channel] + 1;
                else d[a.Channel] = 1;
            }
            return d;
        }

        public static string Advise(List<Ap> list)
        {
            StringBuilder sb = new StringBuilder();
            int n24 = 0;
            int n5 = 0;
            foreach (Ap a in list)
            {
                if (a.Channel > 0 && a.Channel <= 14) n24++;
                else if (a.Channel > 14) n5++;
            }
            sb.AppendLine("周围 AP：" + list.Count + " 个（2.4G " + n24 + " / 5G " + n5 + "）");
            int[] c24 = new int[] { 1, 6, 11 };
            int best24 = 1;
            int least24 = -1;
            foreach (int ch in c24)
            {
                int load = 0;
                foreach (Ap a in list)
                {
                    if (a.Channel <= 0 || a.Channel > 14) continue;
                    int diff = a.Channel - ch;
                    if (diff < 0) diff = -diff;
                    if (diff <= 4) load++;
                }
                if (least24 < 0 || load < least24) { least24 = load; best24 = ch; }
            }
            sb.AppendLine("2.4G 建议信道 " + best24 + "（±4 内重叠 " + least24 + " 个）");
            int[] c5 = new int[] { 36, 40, 44, 48, 149, 153, 157, 161, 165 };
            int best5 = 36;
            int least5 = -1;
            foreach (int ch in c5)
            {
                int load = 0;
                foreach (Ap a in list) if (a.Channel == ch) load++;
                if (least5 < 0 || load < least5) { least5 = load; best5 = ch; }
            }
            sb.AppendLine("5G 建议信道 " + best5 + "（同信道占用 " + least5 + " 个）");
            if (n5 == 0) sb.AppendLine("提示：一条 5G AP 都没有，说明周围 5G 很空，能用 5G 就用 5G。");
            return sb.ToString();
        }

        public static string SelfTestText()
        {
            StringBuilder sb = new StringBuilder();
            string cur = Current();
            sb.AppendLine("current=" + (cur.Length == 0 ? "未连接 / 无无线网卡" : cur));
            List<Ap> list = Scan();
            sb.AppendLine("aps=" + list.Count);
            List<Ap> top = new List<Ap>(list);
            top.Sort(delegate(Ap a, Ap b) { return b.Signal.CompareTo(a.Signal); });
            int lim = top.Count < 6 ? top.Count : 6;
            for (int i = 0; i < lim; i++)
            {
                Ap a = top[i];
                sb.AppendLine("  " + a.Signal + "% ch" + a.Channel + " " + a.Band + " " + a.Ssid + " " + a.Auth);
            }
            Dictionary<int, int> load = ChannelLoad(list);
            StringBuilder cl = new StringBuilder();
            List<int> keys = new List<int>();
            foreach (KeyValuePair<int, int> kv in load) keys.Add(kv.Key);
            keys.Sort();
            foreach (int k in keys) cl.Append("ch" + k + "=" + load[k] + " ");
            sb.AppendLine("channel_load=" + cl.ToString().Trim());
            sb.Append("advise=" + Advise(list).Replace("\r\n", " / ").Replace("\n", " / "));
            sb.AppendLine();
            string[] zh = new string[] {
                "接口名称: 无线网络连接", "当前有 2 个网络可见。", "",
                "SSID 1 : Home-2G", "    网络类型            : 结构", "    身份验证            : WPA2 - 个人",
                "    加密                : CCMP", "    BSSID 1             : aa:bb:cc:dd:ee:01",
                "         信号            : 82%", "         无线电类型      : 802.11n",
                "         频带            : 2.4 GHz", "         通道            : 6", "",
                "SSID 2 : Home-5G", "    身份验证            : WPA2 - 个人",
                "    BSSID 1             : aa:bb:cc:dd:ee:02", "         信号            : 61%",
                "         频带            : 5 GHz", "         通道            : 149" };
            List<Ap> z = ParseText(string.Join("\n", zh));
            StringBuilder zs = new StringBuilder();
            foreach (Ap a in z) zs.Append(a.Ssid + "/ch" + a.Channel + "/" + a.Signal + "%/" + a.Band + " ");
            sb.AppendLine("parse_zh=" + z.Count + " " + zs.ToString().Trim());
            string[] en = new string[] {
                "SSID 1 : Office", "    Network type            : Infrastructure",
                "    Authentication          : WPA2-Personal", "    BSSID 1                 : aa:bb:cc:dd:ee:03",
                "         Signal             : 70%", "         Band               : 5 GHz",
                "         Channel            : 44" };
            List<Ap> e = ParseText(string.Join("\n", en));
            StringBuilder es = new StringBuilder();
            foreach (Ap a in e) es.Append(a.Ssid + "/ch" + a.Channel + "/" + a.Signal + "%/" + a.Band + " ");
            sb.AppendLine("parse_en=" + e.Count + " " + es.ToString().Trim());
            if (z.Count > 0) sb.AppendLine("parse_zh_ch6=" + (z[0].Channel == 6 && z[0].Signal == 82) + " ch149=" + (z.Count > 1 && z[1].Channel == 149));
            if (e.Count > 0) sb.AppendLine("parse_en_ch44=" + (e[0].Channel == 44 && e[0].Signal == 70));
            return sb.ToString();
        }
    }
}