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
    static class VideoCmp
    {
        public static string Ffmpeg(string baseDir)
        {
            string p = Path.Combine(baseDir, "video_tool", "ffmpeg.exe");
            return File.Exists(p) ? p : "";
        }

        public static string OutDir(string src)
        {
            string d = Path.GetDirectoryName(src);
            if (d == null || d.Length == 0) d = ".";
            string sub = Path.Combine(d, "_压缩");
            try
            {
                if (!Directory.Exists(sub)) Directory.CreateDirectory(sub);
                return sub;
            }
            catch (Exception) { return d; }
        }

        // targetMB > 0：按目标体积两遍编码；targetMB <= 0：质量优先（CRF 23）
        public static bool Compress(string baseDir, string src, int targetMB, int maxHeight,
            out string outFile, out string err, Action<int, string> prog)
        {
            outFile = "";
            err = "";
            string ff = Ffmpeg(baseDir);
            if (ff.Length == 0)
            {
                err = "缺少 ffmpeg：先在「视频下载」页用带 FFmpeg 的高清格式下一次，程序会自动部署。";
                return false;
            }
            if (!File.Exists(src)) { err = "文件不存在：" + src; return false; }
            double dur = VideoProCore.Duration(baseDir, src);
            if (dur <= 0) { err = "读不出视频时长（文件可能损坏或不是视频）"; return false; }

            string dir = OutDir(src);
            string name = Path.GetFileNameWithoutExtension(src);
            string tag = targetMB > 0 ? "_压缩" + targetMB + "M" : "_压缩";
            string dst = Path.Combine(dir, name + tag + ".mp4");
            if (File.Exists(dst))
                dst = Path.Combine(dir, name + tag + "_" + DateTime.Now.ToString("HHmmss") + ".mp4");
            string scale = maxHeight > 0 ? "-vf \"scale=-2:" + maxHeight + "\" " : "";
            int audioKbps = 96;
            string head = "-hide_banner -nostdin -nostats -progress pipe:1 -y -i \"" + src + "\" " + scale;
            string passLog = Path.Combine(Path.GetTempPath(), "mtb_cmp_" + DateTime.Now.Ticks);
            int exit = -1;

            if (targetMB > 0)
            {
                double totalKbps = (double)targetMB * 8192.0 / dur;
                int vk = (int)(totalKbps - audioKbps);
                if (vk < 240) vk = 240;
                if (prog != null) prog(-2, "第一遍分析（目标 " + vk + "k）");
                exit = RunProg(ff, head + "-an -c:v libx264 -preset medium -b:v " + vk + "k -pass 1 -passlogfile \"" + passLog + "\" -f null NUL", dur, prog, true);
                if (prog != null) prog(0, "第二遍编码（目标 " + vk + "k）");
                exit = RunProg(ff, head + "-c:v libx264 -preset medium -b:v " + vk + "k -pass 2 -passlogfile \"" + passLog
                    + "\" -c:a aac -b:a " + audioKbps + "k -movflags +faststart \"" + dst + "\"", dur, prog, false);
            }
            else
            {
                if (prog != null) prog(0, "质量优先（CRF 23）");
                exit = RunProg(ff, head + "-c:v libx264 -preset medium -crf 23 -c:a aac -b:a " + audioKbps
                    + "k -movflags +faststart \"" + dst + "\"", dur, prog, false);
            }
            try
            {
                foreach (string f in Directory.GetFiles(Path.GetDirectoryName(passLog), Path.GetFileName(passLog) + "*"))
                    File.Delete(f);
            }
            catch (Exception dg94_1) { Diag.Swallow("VideoCmp.cs:94", dg94_1); }

            if (!File.Exists(dst) || new FileInfo(dst).Length < 1024)
            {
                err = exit == 0 ? "编码失败（没有生成有效文件）" : "编码失败（ffmpeg 退出码 " + exit + "）";
                return false;
            }
            outFile = dst;
            return true;
        }

        // pct >= 0：更新百分比；pct < 0：只更新文字；quiet：第一遍分析不推进度
        static int RunProg(string exe, string args, double dur, Action<int, string> prog, bool quiet)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process proc = Process.Start(psi);
            StringBuilder er = new StringBuilder();
            proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (er) { er.AppendLine(e.Data); if (er.Length > 3000) er.Remove(0, 1500); }
            };
            try { proc.BeginErrorReadLine(); } catch (Exception dg122_1) { Diag.Swallow("VideoCmp.cs:122", dg122_1); }
            string line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                if (prog == null) continue;
                if (line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms="))
                {
                    int ix = line.IndexOf('=');
                    long us = 0;
                    long.TryParse(line.Substring(ix + 1).Trim(), out us);
                    if (quiet || us <= 0 || dur <= 0) continue;
                    int pct = (int)(us / 1000000.0 / dur * 100.0);
                    if (pct > 99) pct = 99;
                    if (pct < 0) pct = 0;
                    prog(pct, "");
                }
                else if (line.StartsWith("speed="))
                {
                    string sp = line.Substring(6).Trim();
                    if (sp.Length > 0 && !sp.StartsWith("N/A")) prog(-1, "速度 " + sp);
                }
            }
            try { proc.WaitForExit(60000); } catch (Exception dg144_1) { Diag.Swallow("VideoCmp.cs:144", dg144_1); }
            return proc.HasExited ? proc.ExitCode : -1;
        }
    }
}