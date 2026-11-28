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
    class PrivItem
    {
        public string Zh = "";
        public string En = "";
        public string Kind = "";      // dir / file / reg / clip / dns / recycle
        public string Target = "";
        public bool IsDir = false;
        public long Size = 0;
    }
}
