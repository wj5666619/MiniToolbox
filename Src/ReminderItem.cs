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
    class ReminderItem
    {
        public int Id;
        public string Title = "";
        public string Body = "";
        public string TimeText = "2026-01-01 00:00:00";   // 下次触发时间
        public int Repeat;                                  // 0 一次性 1 每天 2 每周 3 每隔 N 分钟
        public int IntervalMin = 10;
        public bool On = true;
        public bool Sound = true;

        public DateTime FireAt
        {
            get
            {
                DateTime d;
                if (DateTime.TryParse(TimeText, out d)) return d;
                return DateTime.Now;
            }
            set { TimeText = value.ToString("yyyy-MM-dd HH:mm:ss"); }
        }

        public string RepeatText()
        {
            switch (Repeat)
            {
                case 1: return "每天";
                case 2: return "每周";
                case 3: return "每隔 " + IntervalMin + " 分钟";
                default: return "一次性";
            }
        }
    }
}
