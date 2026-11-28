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
    class RecycleHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public uint fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        public static bool Delete(string path)
        {
            try
            {
                SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
                op.wFunc = 3;                 // FO_DELETE
                op.pFrom = path + "\0";
                op.pTo = null;
                op.fFlags = 0x40 | 0x10 | 0x400 | 0x200 | 0x4;  // ALLOWUNDO | NOCONFIRM | NOERRORUI | SILENT | SIMPLEPROGRESS
                int r = SHFileOperation(ref op);
                return r == 0;
            }
            catch { return false; }
        }
    }
}
