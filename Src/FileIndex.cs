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
    static class FileIndex
    {
        public static List<string> Paths = new List<string>();
        public static List<long> Sizes = new List<long>();
        public static DateTime Built = DateTime.MinValue;
        public static volatile bool Building;
        static string cachePath = "";

        public static int Count { get { return Paths.Count; } }

        public static void Init(string dir)
        {
            cachePath = Path.Combine(dir, "文件索引.dat");
            LoadCache();
        }

        public static void LoadCache()
        {
            try
            {
                if (cachePath.Length == 0 || !File.Exists(cachePath)) return;
                List<string> p2 = new List<string>();
                List<long> s2 = new List<long>();
                using (StreamReader sr = new StreamReader(cachePath, Encoding.UTF8))
                {
                    string ln;
                    while ((ln = sr.ReadLine()) != null)
                    {
                        int t = ln.IndexOf('\t');
                        if (t <= 0) continue;
                        long sz = 0;
                        try { sz = long.Parse(ln.Substring(0, t)); } catch (Exception dg54_1) { Diag.Swallow("FileIndex.cs:54", dg54_1); }
                        p2.Add(ln.Substring(t + 1));
                        s2.Add(sz);
                    }
                }
                Paths = p2; Sizes = s2;
                Built = File.GetLastWriteTime(cachePath);
            }
            catch (Exception dg62_1) { Diag.Swallow("FileIndex.cs:62", dg62_1); }
        }

        public static void SaveCache()
        {
            try
            {
                if (cachePath.Length == 0) return;
                if (Paths.Count > 2500000) return;
                using (StreamWriter sw = new StreamWriter(cachePath, false, new UTF8Encoding(false)))
                {
                    for (int i = 0; i < Paths.Count; i++)
                    {
                        sw.Write(Sizes[i].ToString());
                        sw.Write('\t');
                        sw.Write(Paths[i]);
                        sw.Write("\r\n");
                    }
                }
            }
            catch (Exception dg82_1) { Diag.Swallow("FileIndex.cs:82", dg82_1); }
        }

        public static string[] Roots()
        {
            List<string> r = new List<string>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        r.Add(d.RootDirectory.FullName);
                    }
                    catch (Exception dg97_1) { Diag.Swallow("FileIndex.cs:97", dg97_1); }
                }
            }
            catch (Exception dg100_1) { Diag.Swallow("FileIndex.cs:100", dg100_1); }
            if (r.Count == 0)
            {
                try { r.Add(Path.GetPathRoot(Environment.SystemDirectory)); } catch (Exception dg103_1) { Diag.Swallow("FileIndex.cs:103", dg103_1); }
            }
            return r.ToArray();
        }

        public static void Build(Action<int> progress, Action<bool> done, string[] only)
        {
            if (Building) return;
            Building = true;
            Thread th = new Thread(delegate ()
            {
                List<string> p2 = new List<string>(4096);
                List<long> s2 = new List<long>(4096);
                try
                {
                    string[] roots = (only != null && only.Length > 0) ? only : Roots();
                    for (int i = 0; i < roots.Length; i++) Walk(roots[i], p2, s2, progress);
                }
                catch (Exception dg121_1) { Diag.Swallow("FileIndex.cs:121", dg121_1); }
                Paths = p2; Sizes = s2; Built = DateTime.Now; Building = false;
                SaveCache();
                try { if (done != null) done(true); } catch (Exception dg124_1) { Diag.Swallow("FileIndex.cs:124", dg124_1); }
            });
            th.IsBackground = true;
            th.Start();
        }

        static void Walk(string dir, List<string> p2, List<long> s2, Action<int> prog)
        {
            try
            {
                string nm = Path.GetFileName(dir).ToLowerInvariant();
                if (nm == "$recycle.bin" || nm == "system volume information" || nm == "winsxs") return;
                DirectoryInfo di = new DirectoryInfo(dir);
                FileInfo[] fs = null;
                try { fs = di.GetFiles(); } catch (Exception dg138_1) { Diag.Swallow("FileIndex.cs:138", dg138_1); }
                if (fs != null)
                {
                    for (int i = 0; i < fs.Length; i++)
                    {
                        try { p2.Add(fs[i].FullName); s2.Add(fs[i].Length); } catch (Exception dg143_1) { Diag.Swallow("FileIndex.cs:143", dg143_1); }
                    }
                }
                if (prog != null && (p2.Count % 2048) < 32) { try { prog(p2.Count); } catch (Exception dg146_1) { Diag.Swallow("FileIndex.cs:146", dg146_1); } }
                DirectoryInfo[] ds = null;
                try { ds = di.GetDirectories(); } catch (Exception dg148_1) { Diag.Swallow("FileIndex.cs:148", dg148_1); }
                if (ds != null)
                {
                    for (int i = 0; i < ds.Length; i++)
                    {
                        try { if ((ds[i].Attributes & FileAttributes.ReparsePoint) != 0) continue; } catch (Exception dg153_1) { Diag.Swallow("FileIndex.cs:153", dg153_1); }
                        Walk(ds[i].FullName, p2, s2, prog);
                    }
                }
            }
            catch (Exception dg158_1) { Diag.Swallow("FileIndex.cs:158", dg158_1); }
        }

        static long SizeValue(string t)
        {
            if (t == null) return -1;
            string x = t.Trim().ToLowerInvariant();
            if (x.Length == 0) return -1;
            long mul = 1;
            if (x.EndsWith("kb") || x.EndsWith("k")) { mul = 1024L; x = x.TrimEnd('b'); x = x.TrimEnd('k'); }
            else if (x.EndsWith("mb") || x.EndsWith("m")) { mul = 1024L * 1024; x = x.TrimEnd('b'); x = x.TrimEnd('m'); }
            else if (x.EndsWith("gb") || x.EndsWith("g")) { mul = 1024L * 1024 * 1024; x = x.TrimEnd('b'); x = x.TrimEnd('g'); }
            else if (x.EndsWith("b")) x = x.TrimEnd('b');
            double v = 0;
            try { v = double.Parse(x); } catch { return -1; }
            return (long)(v * mul);
        }

        static bool WildMatch(string name, string pat)
        {
            // 只支持 * 与 ?，逐段贪心匹配，够用且不写正则
            int i = 0, j = 0, star = -1, mark = 0;
            while (i < name.Length)
            {
                if (j < pat.Length && (pat[j] == '?' || pat[j] == name[i])) { i++; j++; continue; }
                if (j < pat.Length && pat[j] == '*') { star = j++; mark = i; continue; }
                if (star >= 0) { j = star + 1; i = ++mark; continue; }
                return false;
            }
            while (j < pat.Length && pat[j] == '*') j++;
            return j == pat.Length;
        }

        // 返回总命中数；hits 收集前 max 条的索引
        public static int Search(string query, int max, List<int> hits)
        {
            hits.Clear();
            if (Paths.Count == 0) return 0;
            List<string> words = new List<string>();
            List<string> wild = new List<string>();
            long minSize = -1, maxSize = -1;
            string[] tk = (query == null ? "" : query.ToLowerInvariant()).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tk.Length; i++)
            {
                string t = tk[i].Trim();
                if (t.Length == 0) continue;
                if (t[0] == '>') { long v = SizeValue(t.Substring(1)); if (v > 0) minSize = v; }
                else if (t[0] == '<') { long v = SizeValue(t.Substring(1)); if (v > 0) maxSize = v; }
                else if (t.IndexOf('*') >= 0 || t.IndexOf('?') >= 0) wild.Add(t);
                else words.Add(t);
            }
            int total = 0;
            for (int i = 0; i < Paths.Count; i++)
            {
                long sz = Sizes[i];
                if (minSize > 0 && sz < minSize) continue;
                if (maxSize > 0 && sz > maxSize) continue;
                string pth = Paths[i].ToLowerInvariant();
                bool ok = true;
                for (int w = 0; w < words.Count; w++)
                {
                    if (pth.IndexOf(words[w], StringComparison.Ordinal) < 0) { ok = false; break; }
                }
                if (!ok) continue;
                if (wild.Count > 0)
                {
                    string fn = Path.GetFileName(pth);
                    for (int w = 0; w < wild.Count; w++)
                    {
                        if (!WildMatch(fn, wild[w])) { ok = false; break; }
                    }
                }
                if (!ok) continue;
                total++;
                if (hits.Count < max) hits.Add(i);
                if (total > 400000) break;
            }
            return total;
        }
    }
}