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
    static class Mci
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr hwnd);
        public static string Send(string cmd)
        {
            StringBuilder sb = new StringBuilder(260);
            try { mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero); } catch (Exception dg30_1) { Diag.Swallow("Mci.cs:30", dg30_1); }
            return sb.ToString() ?? "";
        }
    }
}