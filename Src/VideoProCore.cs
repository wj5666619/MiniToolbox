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
    static class VideoProCore
    {
        public static string Ffmpeg(string baseDir)
        {
            string p = Path.Combine(baseDir, "video_tool", "ffmpeg.exe");
            return File.Exists(p) ? p : "";
        }

        public static bool Ready(string baseDir)
        {
            return Ffmpeg(baseDir).Length > 0;
        }

        public static double Duration(string baseDir, string file)
        {
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0) return 0;
            ProcessStartInfo psi = new ProcessStartInfo(ff, "-hide_banner -i \"" + file + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process p = Process.Start(psi);
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            return ParseDuration(se);
        }

        public static double ParseDuration(string text)
        {
            int i = text.IndexOf("Duration:", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            string s = text.Substring(i + 9).Trim();
            int comma = s.IndexOf(',');
            if (comma > 0) s = s.Substring(0, comma);
            string[] parts = s.Split(':');
            if (parts.Length != 3) return 0;
            double h = 0, m = 0, sec = 0;
            double.TryParse(parts[0], out h);
            double.TryParse(parts[1], out m);
            double.TryParse(parts[2], out sec);
            return h * 3600 + m * 60 + sec;
        }

        // 压缩到目标体积（MB）：按目标码率单遍编码
        public static bool Compress(string baseDir, string src, int targetMB, out string outFile, out string err, Action<string> log)
        {
            outFile = "";
            err = "";
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg（先到「视频下载」页用高清格式下载一次即可自动部署）"; return false; }
            double dur = Duration(baseDir, src);
            if (dur <= 0) { err = "读不出视频时长"; return false; }
            double totalKbps = (double)targetMB * 8192.0 / dur;      // MB → kbit
            int audioKbps = 96;
            int videoKbps = (int)(totalKbps - audioKbps);
            if (videoKbps < 200) { videoKbps = 200; }
            string dst = Path.Combine(Path.GetDirectoryName(src),
                Path.GetFileNameWithoutExtension(src) + "_压缩" + targetMB + "M.mp4");
            string args = "-hide_banner -y -i \"" + src + "\" -c:v libx264 -preset medium -b:v " + videoKbps
                + "k -maxrate " + videoKbps + "k -bufsize " + (videoKbps * 2) + "k -c:a aac -b:a " + audioKbps
                + "k -movflags +faststart \"" + dst + "\"";
            if (log != null) log("目标码率 " + videoKbps + "k / 时长 " + ((int)dur) + "s");
            string o = Run(ff, args, 0, log);
            if (!File.Exists(dst)) { err = "编码失败：" + Tail(o); return false; }
            outFile = dst;
            return true;
        }

        // 多段拼接（同编码时直接 stream copy，不同则重编码）
        public static bool Concat(string baseDir, List<string> files, bool reencode, out string outFile, out string err, Action<string> log)
        {
            outFile = "";
            err = "";
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            if (files.Count < 2) { err = "至少选两个视频"; return false; }
            string listFile = Path.Combine(Path.GetTempPath(), "mtb_concat_" + DateTime.Now.Ticks + ".txt");
            StringBuilder sb = new StringBuilder();
            foreach (string s in files)
            {
                string p = s.Replace("\\", "/");
                sb.AppendLine("file '" + p.Replace("'", "'\\''") + "'");
            }
            File.WriteAllText(listFile, sb.ToString(), Encoding.UTF8);
            string dst = Path.Combine(Path.GetDirectoryName(files[0]), "拼接_" + DateTime.Now.ToString("HHmmss") + ".mp4");
            string args;
            if (reencode)
                args = "-hide_banner -y -f concat -safe 0 -i \"" + listFile + "\" -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 128k \"" + dst + "\"";
            else
                args = "-hide_banner -y -f concat -safe 0 -i \"" + listFile + "\" -c copy \"" + dst + "\"";
            string o = Run(ff, args, 0, log);
            try { File.Delete(listFile); } catch (Exception dg115_1) { Diag.Swallow("VideoProCore.cs:115", dg115_1); }
            if (!File.Exists(dst)) { err = "拼接失败：" + Tail(o); return false; }
            outFile = dst;
            return true;
        }

        // 抽字幕（软字幕轨 → .srt）；没有字幕轨时可抽音轨
        public static bool ExtractSub(string baseDir, string src, out string outFile, out string err)
        {
            outFile = "";
            err = "";
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            string dst = Path.ChangeExtension(src, null) + ".srt";
            string args = "-hide_banner -y -i \"" + src + "\" -map 0:s:0 -c:s srt \"" + dst + "\"";
            string o = Run(ff, args, 120000, null);
            if (!File.Exists(dst) || new FileInfo(dst).Length == 0)
            {
                err = "这条视频没有内嵌字幕轨（只有画面里烧进的字幕才需要 OCR，这里抽不出来）";
                return false;
            }
            outFile = dst;
            return true;
        }

        public static bool ExtractAudio(string baseDir, string src, out string outFile, out string err)
        {
            outFile = "";
            err = "";
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            string dst = Path.ChangeExtension(src, null) + ".m4a";
            string args = "-hide_banner -y -i \"" + src + "\" -vn -c:a copy \"" + dst + "\"";
            string o = Run(ff, args, 120000, null);
            if (!File.Exists(dst))
            {
                args = "-hide_banner -y -i \"" + src + "\" -vn -c:a aac -b:a 128k \"" + dst + "\"";
                o = Run(ff, args, 180000, null);
            }
            if (!File.Exists(dst)) { err = "抽音轨失败：" + Tail(o); return false; }
            outFile = dst;
            return true;
        }

        public static string Run(string exe, string args, int timeoutMs, Action<string> log)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process p = Process.Start(psi);
            StringBuilder sb = new StringBuilder();
            DateTime start = DateTime.Now;
            while (!p.HasExited)
            {
                string ln = p.StandardError.ReadLine();
                if (ln != null && ln.Length > 0)
                {
                    sb.Append(ln);
                    sb.Append('\n');
                    if (sb.Length > 40000) sb.Remove(0, 20000);
                    if (log != null && ln.StartsWith("frame=", StringComparison.Ordinal)) log(ln.Trim());
                }
                if (timeoutMs > 0 && (DateTime.Now - start).TotalMilliseconds > timeoutMs)
                {
                    try { p.Kill(); } catch (Exception dg182_1) { Diag.Swallow("VideoProCore.cs:182", dg182_1); }
                    break;
                }
            }
            try { sb.Append(p.StandardError.ReadToEnd()); } catch (Exception dg186_1) { Diag.Swallow("VideoProCore.cs:186", dg186_1); }
            return sb.ToString();
        }

        static string Tail(string s)
        {
            if (s.Length <= 300) return s.Trim();
            return s.Substring(s.Length - 300).Trim();
        }
    }
}