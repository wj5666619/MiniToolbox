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
    class ShotAnn
    {
        public int Tool;      // 0 箭头 1 矩形 2 马赛克 3 文字 4 序号
        public Rectangle R;
        public Point A;
        public Point B;
        public string Text;
        public int Num;
    }
}
