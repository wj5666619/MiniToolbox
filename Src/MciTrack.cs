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
    class MciTrack
    {
        const string AL = "tingtk";
        public string Path = "";

        public bool Open(string file)
        {
            Close();
            Mci.Send("open \"" + file + "\" type mpegvideo alias " + AL);
            Mci.Send("set " + AL + " time format milliseconds");
            if (Mci.Send("status " + AL + " length").Length == 0)
                Mci.Send("open \"" + file + "\" alias " + AL);
            Path = file;
            return true;
        }

        public void Close() { if (Path.Length > 0) { Mci.Send("close " + AL); Path = ""; } }
        public void PlayFrom(long ms) { Mci.Send("seek " + AL + " to " + Math.Max(0, ms)); Mci.Send("play " + AL); }
        public void Pause() { Mci.Send("pause " + AL); }
        public void Resume() { Mci.Send("resume " + AL); }
        public long Position() { long v = 0; long.TryParse(Mci.Send("status " + AL + " position"), out v); return v; }
        public long Length() { long v = 0; long.TryParse(Mci.Send("status " + AL + " length"), out v); return v; }
        public string Mode() { return Mci.Send("status " + AL + " mode").Trim(); }
        public void Volume(int v) { Mci.Send("setaudio " + AL + " volume to " + Math.Max(0, Math.Min(1000, v))); }
    }
}
