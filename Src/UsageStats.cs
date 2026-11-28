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
    static class UsageStats
    {
        public static string FilePath = "";

        public static void Init(string dir) { try { FilePath = Path.Combine(dir, "用时统计.txt"); } catch (Exception dg27_1) { Diag.Swallow("UsageStats.cs:27", dg27_1); } }

        public static void Add(int minutes, string action)
        {
            try
            {
                if (FilePath == null || FilePath.Length == 0) return;
                string day = DateTime.Now.ToString("yyyy-MM-dd");
                Dictionary<string, int[]> map = Read();
                int[] v;
                if (!map.TryGetValue(day, out v)) { v = new int[2]; map[day] = v; }
                v[0] = v[0] + 1;
                v[1] = v[1] + Math.Max(1, minutes);
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, int[]> kv in map)
                    sb.AppendLine(kv.Key + "|" + kv.Value[0] + "|" + kv.Value[1]);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg45_1) { Diag.Swallow("UsageStats.cs:45", dg45_1); }
        }

        static Dictionary<string, int[]> Read()
        {
            Dictionary<string, int[]> map = new Dictionary<string, int[]>();
            try
            {
                if (FilePath == null || !File.Exists(FilePath)) return map;
                foreach (string ln in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] a = ln.Split('|');
                    if (a.Length < 3) continue;
                    int c = 0, m = 0;
                    int.TryParse(a[1], out c);
                    int.TryParse(a[2], out m);
                    map[a[0].Trim()] = new int[] { c, m };
                }
            }
            catch (Exception dg64_1) { Diag.Swallow("UsageStats.cs:64", dg64_1); }
            return map;
        }

        /// <summary>今天次数、今天分钟、连续天数、近 7 天分钟（最早→今天）</summary>
        public static int[] Snapshot(out int count, out int minutes, out int streak)
        {
            Dictionary<string, int[]> map = Read();
            DateTime today = DateTime.Now.Date;
            string k0 = today.ToString("yyyy-MM-dd");
            int[] v0 = map.ContainsKey(k0) ? map[k0] : new int[] { 0, 0 };
            count = v0[0];
            minutes = v0[1];
            streak = 0;
            for (int i = 0; i < 400; i++)
            {
                string kk = today.AddDays(-i).ToString("yyyy-MM-dd");
                int[] vv = map.ContainsKey(kk) ? map[kk] : null;
                if (vv != null && vv[0] > 0) streak++;
                else if (i > 0) break;
            }
            int[] last7 = new int[7];
            for (int i = 0; i < 7; i++)
            {
                string kk = today.AddDays(-(6 - i)).ToString("yyyy-MM-dd");
                int[] vv = map.ContainsKey(kk) ? map[kk] : null;
                last7[i] = vv == null ? 0 : vv[1];
            }
            return last7;
        }
    }
}