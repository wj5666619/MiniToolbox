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
    static class WinCore
    {
        public class Item
        {
            public IntPtr H;
            public string Title;
            public int Pid;
            public string Proc;
            public bool Min;
            public bool Topmost;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc cb, IntPtr p);
        delegate bool EnumProc(IntPtr h, IntPtr p);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowTextLength(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int h2, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr h, int idx);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        static readonly IntPtr TOPMOST = new IntPtr(-1);
        static readonly IntPtr NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        const int SW_MINIMIZE = 6, SW_RESTORE = 9, SW_MAXIMIZE = 3;
        const uint WM_CLOSE = 0x10;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOPMOST = 0x8;

        public static List<Item> Collect(out int dummy)
        {
            dummy = 0;
            List<Item> list = new List<Item>();
            int selfPid = Process.GetCurrentProcess().Id;
            try
            {
                EnumProc cb = delegate (IntPtr h, IntPtr p)
                {
                    try
                    {
                        if (!IsWindowVisible(h)) return true;
                        int len = GetWindowTextLength(h);
                        if (len <= 0) return true;
                        StringBuilder sb = new StringBuilder(len + 2);
                        GetWindowText(h, sb, len + 2);
                        string title = sb.ToString().Trim();
                        if (title.Length == 0) return true;
                        uint pid = 0;
                        GetWindowThreadProcessId(h, out pid);
                        if ((int)pid == selfPid) return true;
                        Item it = new Item();
                        it.H = h;
                        it.Title = title;
                        it.Pid = (int)pid;
                        it.Proc = ProcName((int)pid);
                        it.Min = IsIconic(h);
                        it.Topmost = (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
                        list.Add(it);
                    }
                    catch (Exception dg102_1) { Diag.Swallow("WinCore.cs:102", dg102_1); }
                    return true;
                };
                EnumWindows(cb, IntPtr.Zero);
            }
            catch (Exception dg107_1) { Diag.Swallow("WinCore.cs:107", dg107_1); }
            return list;
        }

        public static List<Item> Collect()
        {
            int d = 0;
            return Collect(out d);
        }

        static string ProcName(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName; }
            catch { return "pid " + pid; }
        }

        public static void Topmost(IntPtr h, bool on)
        {
            try { SetWindowPos(h, on ? TOPMOST : NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE); }
            catch (Exception dg126_1) { Diag.Swallow("WinCore.cs:126", dg126_1); }
        }

        public static void Snap(Item it, int mode)
        {
            if (it == null) return;
            IntPtr h = it.H;
            if (!IsWindow(h)) return;
            try
            {
                if (mode == 3) { ShowWindow(h, SW_RESTORE); ShowWindow(h, SW_MAXIMIZE); return; }
                if (mode == 4) { ShowWindow(h, SW_MINIMIZE); return; }
                ShowWindow(h, SW_RESTORE);
                Rectangle wa = Screen.FromHandle(h).WorkingArea;
                RECT r;
                GetWindowRect(h, out r);
                if (mode == 1) SetWindowPos(h, IntPtr.Zero, wa.Left, wa.Top, wa.Width / 2, wa.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                else if (mode == 2) SetWindowPos(h, IntPtr.Zero, wa.Left + wa.Width / 2, wa.Top, wa.Width / 2, wa.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch (Exception dg145_1) { Diag.Swallow("WinCore.cs:145", dg145_1); }
        }

        public static void CloseWin(IntPtr h)
        {
            try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception dg151_1) { Diag.Swallow("WinCore.cs:151", dg151_1); }
        }

        public static int MinimizeAllCount()
        {
            int n = 0;
            List<Item> ws = Collect();
            foreach (Item it in ws)
            {
                if (it.Min) continue;
                try { ShowWindow(it.H, SW_MINIMIZE); n++; }
                catch (Exception dg162_1) { Diag.Swallow("WinCore.cs:162", dg162_1); }
            }
            return n;
        }

        // ---- 布局保存 / 还原（标题匹配，够用且不依赖进程重启） ----
        public static string LayoutPath(string dir)
        {
            return Path.Combine(dir, "窗口布局.tbl");
        }

        public static string SaveLayout(List<Item> ws, string dir)
        {
            string p = LayoutPath(dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# MTB layout v1");
            foreach (Item it in ws)
            {
                RECT r;
                if (!GetWindowRect(it.H, out r)) continue;
                if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) continue;
                sb.AppendLine(Clean(it.Title) + "\t" + r.Left + "\t" + r.Top + "\t" + (r.Right - r.Left) + "\t" + (r.Bottom - r.Top));
            }
            try { File.WriteAllText(p, sb.ToString(), Encoding.UTF8); } catch (Exception dg185_1) { Diag.Swallow("WinCore.cs:185", dg185_1); }
            return p;
        }

        static string Clean(string s)
        {
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        public static int RestoreLayout(string dir)
        {
            string p = LayoutPath(dir);
            if (!File.Exists(p)) return -1;
            string[] lines;
            try { lines = File.ReadAllLines(p, Encoding.UTF8); }
            catch { return 0; }
            int done = 0;
            foreach (string line in lines)
            {
                if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                string[] seg = line.Split('\t');
                if (seg.Length < 5) continue;
                string title = seg[0];
                int x = 0, y = 0, w = 0, h2 = 0;
                if (!int.TryParse(seg[1], out x)) continue;
                if (!int.TryParse(seg[2], out y)) continue;
                if (!int.TryParse(seg[3], out w)) continue;
                if (!int.TryParse(seg[4], out h2)) continue;
                foreach (Item it in Collect())
                {
                    if (it.Title != title) continue;
                    try { ShowWindow(it.H, SW_RESTORE); SetWindowPos(it.H, IntPtr.Zero, x, y, w, h2, SWP_NOZORDER); done++; }
                    catch (Exception dg217_1) { Diag.Swallow("WinCore.cs:217", dg217_1); }
                    break;
                }
            }
            return done;
        }

        public static bool LayoutTest(string dir)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# probe");
                sb.AppendLine("探针窗体\t100\t200\t400\t300");
                File.WriteAllText(LayoutPath(dir), sb.ToString(), Encoding.UTF8);
                return File.Exists(LayoutPath(dir));
            }
            catch { return false; }
        }
    }
}