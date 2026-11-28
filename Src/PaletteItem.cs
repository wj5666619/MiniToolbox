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
    class PaletteItem
    {
        public string Zh;
        public string En;
        public string Keys;
        public string Hay;
        public Action Run;
        public bool Intent;      // v8.0：由当前输入即时解析出来的“意图条目”，置顶显示
        public bool Recent;      // v8.0：最近用过的，置顶显示
        public string Kind;      // v13.0：page = 跳到某个页面 / open = 打开某个目录 / act = 执行动作
    }
}
