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
    class RecorderCore
    {
        static Process proc;
        static string outFile = "";

        public static string LastFile { get { return outFile; } }

        public static bool Running
        {
            get { try { return proc != null && !proc.HasExited; } catch { return false; } }
        }

        public static bool Start(string ffmpeg, int x, int y, int w, int h, int fps, string file, out string err)
        {
            err = "";
            try
            {
                if (!File.Exists(ffmpeg)) { err = "未找到 ffmpeg.exe"; return false; }
                if (file == null || file.Length == 0) { err = "输出路径为空"; return false; }
                if (Running) { err = "已经在录制中"; return false; }
                string dir = Path.GetDirectoryName(file);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder a = new StringBuilder();
                a.Append("-y -hide_banner -loglevel error ");
                a.Append("-f gdigrab -framerate ").Append(fps).Append(" ");
                if (x > 0) a.Append("-offset_x ").Append(x).Append(" ");
                if (y > 0) a.Append("-offset_y ").Append(y).Append(" ");
                if (w > 0 && h > 0) a.Append("-video_size ").Append(w).Append("x").Append(h).Append(" ");
                a.Append("-draw_mouse 1 -i desktop ");
                a.Append("-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p ");
                a.Append("\"").Append(file).Append("\"");

                ProcessStartInfo si = new ProcessStartInfo(ffmpeg, a.ToString());
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardInput = true;
                si.RedirectStandardError = true;
                proc = Process.Start(si);
                outFile = file;

                Thread th = new Thread(delegate ()
                {
                    try { while (proc != null && proc.StandardError.ReadLine() != null) { } }
                    catch (Exception dg67_1) { Diag.Swallow("RecorderCore.cs:67", dg67_1); }
                });
                th.IsBackground = true;
                th.Start();

                Thread.Sleep(700);
                if (proc == null || proc.HasExited) { err = "ffmpeg 启动失败"; proc = null; return false; }
                return true;
            }
            catch (Exception ex) { err = ex.Message; proc = null; return false; }
        }

        public static bool Stop(out string err)
        {
            err = "";
            try
            {
                if (proc == null) { err = "没有在录制"; return false; }
                try { proc.StandardInput.WriteLine("q"); proc.StandardInput.Flush(); } catch (Exception dg85_1) { Diag.Swallow("RecorderCore.cs:85", dg85_1); }
                DateTime t0 = DateTime.Now;
                while (!proc.HasExited && (DateTime.Now - t0).TotalMilliseconds < 9000) Thread.Sleep(120);
                if (!proc.HasExited) { try { proc.Kill(); } catch (Exception dg88_1) { Diag.Swallow("RecorderCore.cs:88", dg88_1); } err = "超时已强制停止，最后几秒可能不完整"; }
                proc = null;
                return true;
            }
            catch (Exception ex) { err = ex.Message; proc = null; return false; }
        }

        public static bool ToGif(string ffmpeg, string mp4, string gif, int fps, int width, out string err)
        {
            err = "";
            try
            {
                if (!File.Exists(ffmpeg)) { err = "未找到 ffmpeg.exe"; return false; }
                if (!File.Exists(mp4)) { err = "找不到源视频"; return false; }
                string dir = Path.GetDirectoryName(gif);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string pal = Path.Combine(Path.GetTempPath(), "mtb_pal_" + DateTime.Now.ToString("HHmmssfff") + ".png");
                string vf1 = "fps=" + fps + ",scale=" + width + ":-1:flags=lanczos,palettegen";
                if (!RunOne(ffmpeg, "-y -hide_banner -loglevel error -i \"" + mp4 + "\" -vf \"" + vf1 + "\" \"" + pal + "\"", out err))
                {
                    try { File.Delete(pal); } catch (Exception dg108_1) { Diag.Swallow("RecorderCore.cs:108", dg108_1); }
                    return false;
                }
                string vf2 = "fps=" + fps + ",scale=" + width + ":-1:flags=lanczos[x];[x][1:v]paletteuse";
                bool ok = RunOne(ffmpeg, "-y -hide_banner -loglevel error -i \"" + mp4 + "\" -i \"" + pal + "\" -lavfi \"" + vf2 + "\" \"" + gif + "\"", out err);
                try { File.Delete(pal); } catch (Exception dg113_1) { Diag.Swallow("RecorderCore.cs:113", dg113_1); }
                return ok;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        static bool RunOne(string exe, string args, out string err)
        {
            err = "";
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(exe, args);
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardError = true;
                using (Process p = Process.Start(si))
                {
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch (Exception dg131_1) { Diag.Swallow("RecorderCore.cs:131", dg131_1); } err = "ffmpeg 超时"; return false; }
                    if (p.ExitCode != 0) { err = e.Trim(); if (err.Length > 300) err = err.Substring(err.Length - 300); return false; }
                    return true;
                }
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }
    }
}