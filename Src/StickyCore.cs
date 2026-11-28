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
    static class StickyCore
    {
        public class Note
        {
            public int Id = 0;
            public string Title = "";
            public string Body = "";
            public int X = 120, Y = 120, W = 260, H = 200;
            public bool Top = false;
        }

        public static string PathOf(string baseDir)
        {
            return Path.Combine(baseDir, "便签.dat");
        }

        public static List<Note> Load(string baseDir)
        {
            List<Note> res = new List<Note>();
            string p = PathOf(baseDir);
            if (!File.Exists(p)) return res;
            try
            {
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                foreach (string ln in lines)
                {
                    if (ln.Length == 0) continue;
                    string[] c = ln.Split('\u0001');
                    if (c.Length < 7) continue;
                    Note n = new Note();
                    int.TryParse(c[0], out n.Id);
                    n.Title = Esc(c[1]);
                    n.Body = Esc(c[2]);
                    int.TryParse(c[3], out n.X);
                    int.TryParse(c[4], out n.Y);
                    int.TryParse(c[5], out n.W);
                    int.TryParse(c[6], out n.H);
                    n.Top = c.Length > 7 && c[7] == "1";
                    res.Add(n);
                }
            }
            catch (Exception dg64_1) { Diag.Swallow("StickyCore.cs:64", dg64_1); }
            return res;
        }

        public static void Save(string baseDir, List<Note> notes)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Note n in notes)
            {
                sb.Append(n.Id).Append('\u0001');
                sb.Append(Raw(n.Title)).Append('\u0001');
                sb.Append(Raw(n.Body)).Append('\u0001');
                sb.Append(n.X).Append('\u0001');
                sb.Append(n.Y).Append('\u0001');
                sb.Append(n.W).Append('\u0001');
                sb.Append(n.H).Append('\u0001');
                sb.Append(n.Top ? "1" : "0");
                sb.Append('\n');
            }
            File.WriteAllText(PathOf(baseDir), sb.ToString(), Encoding.UTF8);
        }

        static string Raw(string s)
        {
            return s.Replace("\u0001", " ").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
        }

        static string Esc(string s)
        {
            return s.Replace("\\n", "\r\n");
        }
    }
}