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
    static class MediaCore
    {
        public static bool Run(string ffmpeg, string args, int timeoutMs, out string err)
        {
            err = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = ffmpeg;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.RedirectStandardOutput = false;
                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                StringBuilder sb = new StringBuilder();
                DateTime t0 = DateTime.Now;
                while (!p.HasExited)
                {
                    if ((DateTime.Now - t0).TotalMilliseconds > timeoutMs)
                    {
                        try { p.Kill(); } catch (Exception dg47_1) { Diag.Swallow("MediaCore.cs:47", dg47_1); }
                        err = "超时";
                        return false;
                    }
                    Thread.Sleep(80);
                }
                string se = "";
                try { se = p.StandardError.ReadToEnd(); } catch (Exception dg54_1) { Diag.Swallow("MediaCore.cs:54", dg54_1); }
                sb.Append(se);
                try { p.WaitForExit(2000); } catch (Exception dg56_1) { Diag.Swallow("MediaCore.cs:56", dg56_1); }
                int code = 0;
                try { code = p.ExitCode; } catch (Exception dg58_1) { Diag.Swallow("MediaCore.cs:58", dg58_1); }
                if (code != 0)
                {
                    err = "ffmpeg 退出码 " + code + "：" + Trailing(sb.ToString());
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        static string Trailing(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length <= 160) return s;
            return s.Substring(s.Length - 160);
        }

        public static bool ConvertImage(string src, string dst, int jpgQuality, out string err)
        {
            err = "";
            try
            {
                string ext = Path.GetExtension(dst).ToLower();
                using (Image img = Image.FromFile(src))
                {
                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                        System.Drawing.Imaging.ImageCodecInfo jpg = null;
                        System.Drawing.Imaging.ImageCodecInfo[] codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
                        for (int i = 0; i < codecs.Length; i++)
                            if (codecs[i].FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) jpg = codecs[i];
                        if (jpg == null)
                        {
                            img.Save(dst, System.Drawing.Imaging.ImageFormat.Jpeg);
                            return true;
                        }
                        System.Drawing.Imaging.EncoderParameters ep = new System.Drawing.Imaging.EncoderParameters(1);
                        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)jpgQuality);
                        img.Save(dst, jpg, ep);
                        return true;
                    }
                    if (ext == ".png") { img.Save(dst, System.Drawing.Imaging.ImageFormat.Png); return true; }
                    if (ext == ".bmp") { img.Save(dst, System.Drawing.Imaging.ImageFormat.Bmp); return true; }
                    if (ext == ".gif") { img.Save(dst, System.Drawing.Imaging.ImageFormat.Gif); return true; }
                    if (ext == ".tif" || ext == ".tiff") { img.Save(dst, System.Drawing.Imaging.ImageFormat.Tiff); return true; }
                    err = "不支持的图片目标格式：" + ext;
                    return false;
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }
    }
}