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
    static class ChainCfg
    {
        public static string FilePath = "";
        public static bool Mute, Lock, ScreenOff, Notify, Clip, Temp, Kill;
        public static string KillName = "";

        public static void Init(string dir)
        {
            FilePath = Path.Combine(dir, "动作链.txt");
            Load();
        }

        public static void Load()
        {
            Mute = false; Lock = false; ScreenOff = false; Notify = true; Clip = false; Temp = false; Kill = false; KillName = "";
            try
            {
                if (FilePath == null || !File.Exists(FilePath)) return;
                foreach (string ln in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = ln.IndexOf('=');
                    if (i <= 0) continue;
                    string k = ln.Substring(0, i).Trim().ToLowerInvariant();
                    string v = ln.Substring(i + 1).Trim();
                    bool b = (v == "1" || v == "true" || v == "on" || v == "开");
                    if (k == "mute") Mute = b;
                    else if (k == "lock") Lock = b;
                    else if (k == "screenoff") ScreenOff = b;
                    else if (k == "notify") Notify = b;
                    else if (k == "clip") Clip = b;
                    else if (k == "temp") Temp = b;
                    else if (k == "kill") Kill = b;
                    else if (k == "killname") KillName = v;
                }
            }
            catch (Exception dg58_1) { Diag.Swallow("ChainCfg.cs:58", dg58_1); }
        }

        public static void Save()
        {
            try
            {
                if (FilePath == null || FilePath.Length == 0) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("mute=" + (Mute ? "1" : "0"));
                sb.AppendLine("lock=" + (Lock ? "1" : "0"));
                sb.AppendLine("screenoff=" + (ScreenOff ? "1" : "0"));
                sb.AppendLine("notify=" + (Notify ? "1" : "0"));
                sb.AppendLine("clip=" + (Clip ? "1" : "0"));
                sb.AppendLine("temp=" + (Temp ? "1" : "0"));
                sb.AppendLine("kill=" + (Kill ? "1" : "0"));
                sb.AppendLine("killname=" + KillName);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg77_1) { Diag.Swallow("ChainCfg.cs:77", dg77_1); }
        }
    }
}