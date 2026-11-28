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
    class DupCore
    {
        public static string Md5Of(string path, int firstBytes)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192))
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    if (firstBytes > 0)
                    {
                        int len = (int)Math.Min((long)firstBytes, fs.Length);
                        byte[] b = new byte[len];
                        int read = 0;
                        while (read < len)
                        {
                            int n = fs.Read(b, read, len - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        return BitConverter.ToString(md5.ComputeHash(b)).Replace("-", "");
                    }
                    return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "");
                }
            }
            catch { return null; }
        }

        public static List<string> Collect(string root, long minSize, Action<string> progress, Func<bool> cancelled)
        {
            List<string> files = new List<string>();
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            int dirs = 0;
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                dirs++;
                if (dirs % 30 == 0)
                {
                    if (cancelled != null && cancelled()) return null;
                    if (progress != null) progress("扫描目录 " + dirs + " 个，文件 " + files.Count + " 个");
                }
                string[] subs = null;
                try { subs = Directory.GetDirectories(d); } catch (Exception dg67_1) { Diag.Swallow("DupCore.cs:67", dg67_1); }
                if (subs != null) foreach (string s in subs) stack.Push(s);
                string[] fs = null;
                try { fs = Directory.GetFiles(d); } catch (Exception dg70_1) { Diag.Swallow("DupCore.cs:70", dg70_1); }
                if (fs != null)
                {
                    foreach (string f in fs)
                    {
                        try { if (new FileInfo(f).Length >= minSize) files.Add(f); } catch (Exception dg75_1) { Diag.Swallow("DupCore.cs:75", dg75_1); }
                    }
                }
            }
            if (progress != null) progress("共扫到 " + files.Count + " 个候选文件，开始分组比对");
            return files;
        }

        // 返回重复分组（每组 >= 2 个文件，同组内容完全相同）
        public static List<List<string>> Group(List<string> files, Action<string> progress, Func<bool> cancelled)
        {
            Dictionary<long, List<string>> bySize = new Dictionary<long, List<string>>();
            foreach (string f in files)
            {
                long len = -1;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len <= 0) continue;
                if (!bySize.ContainsKey(len)) bySize[len] = new List<string>();
                bySize[len].Add(f);
            }
            List<List<string>> groups = new List<List<string>>();
            int checked0 = 0;
            foreach (List<string> same in bySize.Values)
            {
                if (same.Count < 2) continue;
                checked0++;
                if (checked0 % 20 == 0)
                {
                    if (cancelled != null && cancelled()) return groups;
                    if (progress != null) progress("已比对 " + checked0 + " 组同大小文件");
                }
                // 先比首 8KB，再比全文
                Dictionary<string, List<string>> quick = new Dictionary<string, List<string>>();
                foreach (string f in same)
                {
                    string h = Md5Of(f, 8192);
                    if (h == null) continue;
                    if (!quick.ContainsKey(h)) quick[h] = new List<string>();
                    quick[h].Add(f);
                }
                foreach (List<string> q in quick.Values)
                {
                    if (q.Count < 2) continue;
                    Dictionary<string, List<string>> full = new Dictionary<string, List<string>>();
                    foreach (string f in q)
                    {
                        string h = Md5Of(f, 0);
                        if (h == null) continue;
                        if (!full.ContainsKey(h)) full[h] = new List<string>();
                        full[h].Add(f);
                    }
                    foreach (List<string> g in full.Values)
                    {
                        if (g.Count >= 2) groups.Add(g);
                    }
                }
            }
            return groups;
        }
    }
}