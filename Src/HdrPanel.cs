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
    class HdrPanel : Panel
{
    // v17.6：Header 是自绘渐变底 + 一堆绝对定位的子控件。
    // 不开双缓冲时，父级重绘会连带把子控件擦掉，右侧那颗青绿按钮就只剩一条斜边。
    public HdrPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
    }
}
}
