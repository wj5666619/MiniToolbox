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
    static class ScreenRecCore
    {
        static string encCache = "";

        public static string Ffmpeg(string baseDir)
        {
            string p = Path.Combine(baseDir, "video_tool", "ffmpeg.exe");
            return File.Exists(p) ? p : "";
        }

        public static string Encoders(string baseDir)
        {
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0) return "";
            return PortCore.ExecText(ff, "-hide_banner -encoders", 25000);
        }

        public static string PickEncoder(string baseDir)
        {
            if (encCache.Length > 0) return encCache;
            string o = Encoders(baseDir);
            if (o.IndexOf("h264_nvenc", StringComparison.Ordinal) >= 0) encCache = "h264_nvenc";
            else if (o.IndexOf("h264_qsv", StringComparison.Ordinal) >= 0) encCache = "h264_qsv";
            else if (o.IndexOf("h264_amf", StringComparison.Ordinal) >= 0) encCache = "h264_amf";
            else encCache = "libx264";
            return encCache;
        }

        public static string EncoderLabel(string enc)
        {
            if (enc == "h264_nvenc") return "NVIDIA NVENC";
            if (enc == "h264_qsv") return "Intel QuickSync";
            if (enc == "h264_amf") return "AMD AMF";
            return "CPU (libx264)";
        }

        public static int[] PrimarySize()
        {
            Rectangle r = Screen.PrimaryScreen.Bounds;
            return new int[] { r.Width, r.Height };
        }

        public static string BuildArgs(bool region, int x, int y, int w, int h, int fps, string enc, string outFile)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("-hide_banner -y -f gdigrab -framerate ").Append(fps);
            if (region && w > 0 && h > 0)
            {
                sb.Append(" -offset_x ").Append(x).Append(" -offset_y ").Append(y);
                sb.Append(" -video_size ").Append(w).Append('x').Append(h);
            }
            sb.Append(" -i desktop -c:v ").Append(enc);
            if (enc == "h264_nvenc") sb.Append(" -preset p5 -tune hq -rc vbr -cq 23 -b:v 0");
            else if (enc == "h264_qsv") sb.Append(" -preset medium -global_quality 23");
            else if (enc == "h264_amf") sb.Append(" -quality balanced -qp_i 22 -qp_p 24");
            else sb.Append(" -preset veryfast -crf 23");
            sb.Append(" -pix_fmt yuv420p -movflags +faststart \"");
            sb.Append(outFile).Append('"');
            return sb.ToString();
        }

        // 自测：真录 2 秒，验证文件生成与体积
        public static string SelfTestText(string baseDir)
        {
            StringBuilder sb = new StringBuilder();
            string ff = Ffmpeg(baseDir);
            sb.AppendLine("ffmpeg=" + (ff.Length > 0 ? ff : "missing"));
            if (ff.Length == 0) return sb.ToString();
            string enc = PickEncoder(baseDir);
            sb.AppendLine("encoder=" + enc + " (" + EncoderLabel(enc) + ")");
            int[] sz = PrimarySize();
            sb.AppendLine("screen=" + sz[0] + "x" + sz[1]);
            string tmpDir = Path.Combine(Path.GetTempPath(), "mtb_rec");
            try { if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir); } catch (Exception dg96_1) { Diag.Swallow("ScreenRecCore.cs:96", dg96_1); }
            string outp = Path.Combine(tmpDir, "rectest.mp4");
            try { if (File.Exists(outp)) File.Delete(outp); } catch (Exception dg98_1) { Diag.Swallow("ScreenRecCore.cs:98", dg98_1); }
            string args = BuildArgs(false, 0, 0, 0, 0, 15, enc, outp) + " -t 2";
            string log = VideoProCore.Run(ff, args, 90000, null);
            long len = 0;
            try { if (File.Exists(outp)) len = new FileInfo(outp).Length; } catch (Exception dg102_1) { Diag.Swallow("ScreenRecCore.cs:102", dg102_1); }
            sb.AppendLine("record_len=" + len);
            sb.AppendLine("record_ok=" + (len > 1000));
            sb.AppendLine("args_sample=" + BuildArgs(true, 100, 100, 800, 600, 30, enc, "C:\\t.mp4"));
            if (len <= 1000)
            {
                string tail = log == null ? "" : log.Trim();
                if (tail.Length > 300) tail = tail.Substring(tail.Length - 300);
                sb.AppendLine("log=" + tail.Replace('\n', ' '));
            }
            return sb.ToString();
        }
    }
}