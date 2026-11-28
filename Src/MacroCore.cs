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
    static class MacroCore
    {
        public static bool Recording = false;
        public static bool Playing = false;
        static List<MacroEvent> buf = new List<MacroEvent>();
        static DateTime recT0 = DateTime.MinValue;
        static DateTime lastEvt = DateTime.MinValue;
        static IntPtr hKb = IntPtr.Zero, hMs = IntPtr.Zero;
        static HookProc kbProc, msProc;
        public static Action<string> OnLog;

        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;
        const uint MOVEF = 0x0001, ABSOLUTE = 0x8000;
        const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010, WHEELF = 0x0800;
        const uint KEYUP = 0x0002;

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLL
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINTST
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLL
        {
            public POINTST pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUTST
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUTST[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        static void Log(string s)
        {
            try { if (OnLog != null) OnLog(s); } catch (Exception dg121_1) { Diag.Swallow("MacroCore.cs:121", dg121_1); }
        }

        static IntPtr KbHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && Recording)
                {
                    KBDLL d = (KBDLL)Marshal.PtrToStructure(lParam, typeof(KBDLL));
                    int msg = (int)wParam;
                    int type = (msg == 0x100 || msg == 0x104) ? 1 : 2;  // WM_KEYDOWN / WM_SYSKEYDOWN
                    if (d.vkCode != 27)
                    {
                        MacroEvent ev = new MacroEvent();
                        ev.T = type;
                        ev.Vk = d.vkCode;
                        ev.Delay = SinceLast();
                        lock (buf) buf.Add(ev);
                    }
                }
            }
            catch (Exception dg143_1) { Diag.Swallow("MacroCore.cs:143", dg143_1); }
            return CallNextHookEx(hKb, nCode, wParam, lParam);
        }

        static IntPtr MsHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && Recording)
                {
                    MSLL d = (MSLL)Marshal.PtrToStructure(lParam, typeof(MSLL));
                    int msg = (int)wParam;
                    MacroEvent ev = new MacroEvent();
                    bool take = true;
                    if (msg == 0x200) { ev.T = 3; ev.X = d.pt.x; ev.Y = d.pt.y; }
                    else if (msg == 0x201) ev.T = 4;
                    else if (msg == 0x202) ev.T = 5;
                    else if (msg == 0x204) ev.T = 6;
                    else if (msg == 0x205) ev.T = 7;
                    else if (msg == 0x20A) { ev.T = 8; ev.Data = d.mouseData; }
                    else take = false;
                    if (take)
                    {
                        ev.Delay = SinceLast();
                        lock (buf) buf.Add(ev);
                    }
                }
            }
            catch (Exception dg171_1) { Diag.Swallow("MacroCore.cs:171", dg171_1); }
            return CallNextHookEx(hMs, nCode, wParam, lParam);
        }

        static int SinceLast()
        {
            DateTime now = DateTime.Now;
            int ms = lastEvt == DateTime.MinValue ? 0 : (int)(now - lastEvt).TotalMilliseconds;
            lastEvt = now;
            if (ms < 0) ms = 0;
            if (ms > 60000) ms = 60000;
            return ms;
        }

        public static bool StartRecord(out string err)
        {
            err = "";
            if (Recording) return true;
            lock (buf) buf.Clear();
            recT0 = DateTime.Now;
            lastEvt = DateTime.MinValue;
            kbProc = new HookProc(KbHook);
            msProc = new HookProc(MsHook);
            IntPtr mod = GetModuleHandle(null);
            hKb = SetWindowsHookEx(WH_KEYBOARD_LL, kbProc, mod, 0);
            hMs = SetWindowsHookEx(WH_MOUSE_LL, msProc, mod, 0);
            if (hKb == IntPtr.Zero)
            {
                err = "键盘钩子安装失败";
                return false;
            }
            Recording = true;
            Log("开始录制");
            return true;
        }

        public static List<MacroEvent> StopRecord()
        {
            Recording = false;
            try { if (hKb != IntPtr.Zero) UnhookWindowsHookEx(hKb); } catch (Exception dg210_1) { Diag.Swallow("MacroCore.cs:210", dg210_1); }
            try { if (hMs != IntPtr.Zero) UnhookWindowsHookEx(hMs); } catch (Exception dg211_1) { Diag.Swallow("MacroCore.cs:211", dg211_1); }
            hKb = IntPtr.Zero; hMs = IntPtr.Zero;
            List<MacroEvent> copy = new List<MacroEvent>();
            lock (buf) for (int i = 0; i < buf.Count; i++) copy.Add(buf[i]);
            Log("录制结束，事件 " + copy.Count.ToString() + " 条");
            return copy;
        }

        public static string Serialize(List<MacroEvent> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MTBMAC 1");
            for (int i = 0; i < list.Count; i++)
            {
                MacroEvent e = list[i];
                sb.AppendLine(e.T.ToString() + "\t" + e.Delay.ToString() + "\t" + e.Vk.ToString() + "\t" + e.X.ToString() + "\t" + e.Y.ToString() + "\t" + e.Data.ToString());
            }
            return sb.ToString();
        }

        public static List<MacroEvent> Deserialize(string text)
        {
            List<MacroEvent> list = new List<MacroEvent>();
            if (text == null) return list;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length == 0 || ln.StartsWith("MTBMAC")) continue;
                string[] p = ln.Split('\t');
                if (p.Length < 6) continue;
                try
                {
                    MacroEvent e = new MacroEvent();
                    e.T = int.Parse(p[0]);
                    e.Delay = int.Parse(p[1]);
                    e.Vk = int.Parse(p[2]);
                    e.X = int.Parse(p[3]);
                    e.Y = int.Parse(p[4]);
                    e.Data = int.Parse(p[5]);
                    list.Add(e);
                }
                catch (Exception dg253_1) { Diag.Swallow("MacroCore.cs:253", dg253_1); }
            }
            return list;
        }

        public static void Play(List<MacroEvent> list, double speed, int loops)
        {
            if (list == null || list.Count == 0) { Log("宏是空的"); return; }
            if (Playing) { Log("正在回放中"); return; }
            Playing = true;
            Thread th = new Thread(delegate ()
            {
                try
                {
                    for (int lp = 0; lp < loops; lp++)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (!Playing) break;
                            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { Log("检测到 Esc，已中止回放"); Playing = false; break; }
                            MacroEvent e = list[i];
                            int d = e.Delay;
                            if (speed > 0 && speed != 1.0) d = (int)(d / speed);
                            if (d > 0) Thread.Sleep(d);
                            SendOne(e);
                        }
                        if (!Playing) break;
                    }
                }
                catch (Exception ex) { Log("回放出错：" + ex.Message); }
                Playing = false;
                Log("回放结束");
            });
            th.IsBackground = true;
            th.Start();
        }

        public static void StopPlay()
        {
            Playing = false;
        }

        static void SendOne(MacroEvent e)
        {
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            if (e.T == 1 || e.T == 2)
            {
                INPUTST ip = new INPUTST();
                ip.type = 1;
                ip.u.ki = new KEYBDINPUT();
                ip.u.ki.wVk = (ushort)e.Vk;
                ip.u.ki.wScan = 0;
                ip.u.ki.dwFlags = (e.T == 2) ? KEYUP : 0;
                ip.u.ki.time = 0;
                ip.u.ki.dwExtraInfo = IntPtr.Zero;
                INPUTST[] arr = new INPUTST[1];
                arr[0] = ip;
                SendInput(1, arr, Marshal.SizeOf(typeof(INPUTST)));
            }
            else if (e.T == 3)
            {
                int dx = 0, dy = 0;
                if (scr.Width > 1) dx = (int)((double)e.X / (double)(scr.Width - 1) * 65535.0);
                if (scr.Height > 1) dy = (int)((double)e.Y / (double)(scr.Height - 1) * 65535.0);
                MouseSend(dx, dy, MOVEF | ABSOLUTE, 0);
            }
            else if (e.T == 4) MouseSend(0, 0, LEFTDOWN, 0);
            else if (e.T == 5) MouseSend(0, 0, LEFTUP, 0);
            else if (e.T == 6) MouseSend(0, 0, RIGHTDOWN, 0);
            else if (e.T == 7) MouseSend(0, 0, RIGHTUP, 0);
            else if (e.T == 8) MouseSend(0, 0, WHEELF, (uint)e.Data);
        }

        static void MouseSend(int dx, int dy, uint flags, uint data)
        {
            INPUTST ip = new INPUTST();
            ip.type = 0;
            ip.u.mi = new MOUSEINPUT();
            ip.u.mi.dx = dx;
            ip.u.mi.dy = dy;
            ip.u.mi.mouseData = data;
            ip.u.mi.dwFlags = flags;
            ip.u.mi.time = 0;
            ip.u.mi.dwExtraInfo = IntPtr.Zero;
            INPUTST[] arr = new INPUTST[1];
            arr[0] = ip;
            SendInput(1, arr, Marshal.SizeOf(typeof(INPUTST)));
        }
    }
}