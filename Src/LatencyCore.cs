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
    static class LatencyCore
    {
        public class Target
        {
            public string Name = "";
            public string Host = "";
            public int Port = 443;
            public double Min = -1;
            public double Avg = -1;
            public string Detail = "";
        }

        public static Target Make(string name, string host, int port)
        {
            Target t = new Target();
            t.Name = name;
            t.Host = host;
            t.Port = port;
            return t;
        }

        public static List<Target> Presets()
        {
            List<Target> l = new List<Target>();
            l.Add(Make("Steam 商店", "store.steampowered.com", 443));
            l.Add(Make("Steam 社区", "steamcommunity.com", 443));
            l.Add(Make("Steam 下载 CDN", "cdn.steamstatic.com", 443));
            l.Add(Make("Steam API", "api.steampowered.com", 443));
            l.Add(Make("Steam 内容 CDN", "steamcdn-a.akamaihd.net", 443));
            l.Add(Make("Epic 商店", "www.epicgames.com", 443));
            l.Add(Make("WeGame 大厅", "wegame.qq.com", 443));
            l.Add(Make("Riot 官网", "www.riotgames.com", 443));
            return l;
        }

        public static bool ParseTarget(string text, out string host, out int port)
        {
            host = "";
            port = 443;
            string s = (text == null ? "" : text.Trim());
            if (s.Length == 0) return false;
            int c = s.IndexOf(':');
            if (c > 0 && c < s.Length - 1)
            {
                string pv = s.Substring(c + 1).Trim();
                int pp = 0;
                if (int.TryParse(pv, out pp) && pp > 0 && pp < 65536)
                {
                    host = s.Substring(0, c).Trim();
                    port = pp;
                    return host.Length > 0;
                }
            }
            if (s.Length > 0 && s.IndexOf(' ') < 0)
            {
                host = s;
                return true;
            }
            return false;
        }

        public static void Probe(Target t, int tries)
        {
            double sum = 0;
            int ok = 0;
            double min = -1;
            string last = "";
            for (int i = 0; i < tries; i++)
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                TcpClient c = new TcpClient();
                try
                {
                    IAsyncResult ar = c.BeginConnect(t.Host, t.Port, null, null);
                    bool done = ar.AsyncWaitHandle.WaitOne(4200);
                    if (!done)
                    {
                        last = "超时";
                        c.Close();
                        continue;
                    }
                    c.EndConnect(ar);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    sum += ms;
                    ok++;
                    if (min < 0 || ms < min) min = ms;
                }
                catch (Exception ex)
                {
                    last = ex.Message.Length > 42 ? ex.Message.Substring(0, 42) : ex.Message;
                    try { c.Close(); } catch (Exception dg114_1) { Diag.Swallow("LatencyCore.cs:114", dg114_1); }
                }
                Thread.Sleep(120);
            }
            if (ok > 0)
            {
                t.Min = min;
                t.Avg = sum / ok;
                t.Detail = "ok " + ok + "/" + tries;
            }
            else
            {
                t.Min = -1;
                t.Avg = -1;
                t.Detail = last.Length == 0 ? "不可达" : last;
            }
        }

        public static void ProbeAll(List<Target> list, int tries, Action<Target> each)
        {
            int n = list.Count;
            if (n == 0) return;
            int workers = n < 6 ? n : 6;
            int idx = -1;
            int done = 0;
            object locker = new object();
            Thread[] th = new Thread[workers];
            for (int i = 0; i < workers; i++)
            {
                th[i] = new Thread(new ThreadStart(delegate()
                {
                    while (true)
                    {
                        int k;
                        lock (locker)
                        {
                            idx++;
                            k = idx;
                            if (k >= n) return;
                        }
                        Probe(list[k], tries);
                        if (each != null) { try { each(list[k]); } catch (Exception dg155_1) { Diag.Swallow("LatencyCore.cs:155", dg155_1); } }
                        lock (locker) { done++; }
                    }
                }));
                th[i].IsBackground = true;
                th[i].Start();
            }
            foreach (Thread t in th) { try { t.Join(15000); } catch (Exception dg162_1) { Diag.Swallow("LatencyCore.cs:162", dg162_1); } }
        }

        public static string Fastest(List<Target> list)
        {
            Target best = null;
            foreach (Target t in list)
            {
                if (t.Avg < 0) continue;
                if (best == null || t.Avg < best.Avg) best = t;
            }
            if (best == null) return "";
            return best.Name + "（" + best.Avg.ToString("0.0") + " ms）";
        }

        public static string SelfTestText()
        {
            StringBuilder sb = new StringBuilder();
            List<Target> list = Presets();
            sb.AppendLine("targets=" + list.Count);
            ProbeAll(list, 2, null);
            foreach (Target t in list)
            {
                sb.AppendLine("  " + t.Name + " " + t.Host + ":" + t.Port +
                    " min=" + (t.Min < 0 ? "fail" : t.Min.ToString("0.0")) +
                    " avg=" + (t.Avg < 0 ? "fail" : t.Avg.ToString("0.0")) + " " + t.Detail);
            }
            string fast = Fastest(list);
            sb.AppendLine("fastest=" + (fast.Length == 0 ? "none" : fast));
            string h;
            int p;
            bool parsed = ParseTarget("example.test:8443", out h, out p);
            sb.AppendLine("parse_ok=" + parsed + " host=" + h + " port=" + p);
            return sb.ToString();
        }
    }
}