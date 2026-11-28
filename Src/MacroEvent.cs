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
    class MacroEvent
    {
        public int T;       // 1 键盘按下 2 键盘抬起 3 鼠标移动 4 左键按下 5 左键抬起 6 右键按下 7 右键抬起 8 滚轮
        public int Vk;
        public int X;
        public int Y;
        public int Data;
        public int Delay;   // 距上一条的毫秒
    }
}
