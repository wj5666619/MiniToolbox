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
    class TaskCfg
    {
        public string Action = "关机";
        public string Mode = "countdown";   // countdown / datetime / daily / weekly
        public int Hours = 0, Mins = 30, Secs = 0;
        public DateTime AtTime = DateTime.Now.AddHours(1);
        public bool[] Week = new bool[] { false, false, false, false, false, false, false }; // 周一~周日
        public bool NeedConfirm = true;
        public int ConfirmSecs = 60;
        public bool Sound = true;
        public bool Tray = true;
        public bool Autorun = false;
        // 运行态
        public bool Active = false;
        public bool Periodic = false;
        public DateTime EndTime;
        public int TotalSec = 0;
        public string Desc = "";
    }
}
