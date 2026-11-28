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
    static class CleanHistory
    {
        public static string FilePath = "";
        static List<long> size = new List<long>();
        static List<string> when = new List<string>();
        static List<long> files = new List<long>();

        public static void Init(string dir)
        {
            FilePath = Path.Combine(dir, "清理历史.txt");
            Load();
        }

        public static void Load()
        {
            size.Clear(); when.Clear(); files.Clear();
            try
            {
                if (FilePath == null || !File.Exists(FilePath)) return;
                foreach (string ln in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] a = ln.Split('|');
                    if (a.Length < 2) continue;
                    long v = 0, f = 0;
                    long.TryParse(a[1], out v);
                    if (a.Length > 2) long.TryParse(a[2], out f);
                    when.Add(a[0]);
                    size.Add(v);
                    files.Add(f);
                }
            }
            catch (Exception dg51_1) { Diag.Swallow("CleanHistory.cs:51", dg51_1); }
        }

        public static void Add(long bytes, long fileCount)
        {
            try
            {
                if (FilePath == null || FilePath.Length == 0) return;
                size.Add(bytes);
                when.Add(DateTime.Now.ToString("MM-dd HH:mm"));
                files.Add(fileCount);
                while (size.Count > 60) { size.RemoveAt(0); when.RemoveAt(0); files.RemoveAt(0); }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < size.Count; i++) sb.AppendLine(when[i] + "|" + size[i] + "|" + (i < files.Count ? files[i] : 0));
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg66_1) { Diag.Swallow("CleanHistory.cs:66", dg66_1); }
        }

        public static int Count { get { return size.Count; } }
        public static long At(int i) { return (i >= 0 && i < size.Count) ? size[i] : 0; }
        public static long FilesAt(int i) { return (i >= 0 && i < files.Count) ? files[i] : 0; }
        public static string WhenAt(int i) { return (i >= 0 && i < when.Count) ? when[i] : ""; }
    }
}