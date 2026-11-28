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
    static class ImgQrCore
    {
        public class Job
        {
            public string Path = "";
            public long Before = 0;
            public long After = 0;
            public bool Ok = false;
            public string Msg = "";
        }

        public static string OutDir(string srcDir)
        {
            string d = Path.Combine(srcDir, "压缩输出_" + DateTime.Now.ToString("HHmmss"));
            return d;
        }

        static System.Drawing.Imaging.ImageCodecInfo JpegCodec()
        {
            System.Drawing.Imaging.ImageCodecInfo[] all = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
            foreach (System.Drawing.Imaging.ImageCodecInfo c in all)
                if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) return c;
            return null;
        }

        public static byte[] EncodeJpeg(Image img, long quality)
        {
            System.Drawing.Imaging.ImageCodecInfo codec = JpegCodec();
            if (codec == null) return null;
            System.Drawing.Imaging.EncoderParameters ps = new System.Drawing.Imaging.EncoderParameters(1);
            ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            using (MemoryStream ms = new MemoryStream())
            {
                img.Save(ms, codec, ps);
                return ms.ToArray();
            }
        }

        public static Image LoadUnlocked(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buf = new byte[fs.Length];
                fs.Read(buf, 0, buf.Length);
                using (MemoryStream ms = new MemoryStream(buf))
                    return Image.FromStream(ms);
            }
        }

        // 压到「不超过 targetKB」，可选限制最长边
        public static Job CompressOne(string path, string outDir, int targetKB, int maxSide, string fmt, bool keepSmall)
        {
            Job j = new Job();
            j.Path = path;
            FileInfo fi = new FileInfo(path);
            j.Before = fi.Length;
            try
            {
                using (Image img = LoadUnlocked(path))
                {
                    int w = img.Width, h = img.Height;
                    if (maxSide > 0 && (w > maxSide || h > maxSide))
                    {
                        double sc = (double)maxSide / Math.Max(w, h);
                        w = (int)(w * sc);
                        h = (int)(h * sc);
                    }
                    using (Bitmap nb = new Bitmap(w, h))
                    {
                        using (Graphics g = Graphics.FromImage(nb))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(img, 0, 0, w, h);
                        }
                        j.After = fi.Length;
                        j.Ok = true;

                        if (fmt.Equals("png", StringComparison.OrdinalIgnoreCase))
                        {
                            string dst = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png");
                            nb.Save(dst, System.Drawing.Imaging.ImageFormat.Png);
                            j.After = new FileInfo(dst).Length;
                            if (keepSmall && fi.Length < j.After && Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(dst); } catch (Exception dg109_1) { Diag.Swallow("ImgQrCore.cs:109", dg109_1); }
                                j.After = fi.Length;
                                j.Msg = Path.GetFileName(path) + " 原图更小，保留原图";
                                return j;
                            }
                            j.Msg = dst;
                        }
                        else if (fmt.Equals("bmp", StringComparison.OrdinalIgnoreCase))
                        {
                            string dst = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".bmp");
                            nb.Save(dst, System.Drawing.Imaging.ImageFormat.Bmp);
                            j.After = new FileInfo(dst).Length;
                            j.Msg = dst;
                        }
                        else
                        {
                            byte[] best = null;
                            long bestQ = 85;
                            long[] qs = new long[] { 85, 75, 65, 55, 45, 35, 28, 22, 18, 14, 10 };
                            foreach (long q in qs)
                            {
                                byte[] b = EncodeJpeg(nb, q);
                                if (b == null) continue;
                                if (best == null || b.Length < best.Length) { best = b; bestQ = q; }
                                if (targetKB > 0 && b.Length <= (long)targetKB * 1024) { best = b; bestQ = q; break; }
                            }
                            if (best == null) { j.Ok = false; j.Msg = "编码失败"; return j; }
                            if (keepSmall && fi.Length < best.Length)
                            {
                                j.Ok = true;
                                j.After = fi.Length;
                                j.Msg = Path.GetFileName(path) + " 原图更小，保留原图";
                                return j;
                            }
                            string dst2 = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".jpg");
                            File.WriteAllBytes(dst2, best);
                            j.After = best.Length;
                            j.Msg = dst2 + "  q=" + bestQ;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                j.Ok = false;
                j.Msg = ex.Message;
            }
            return j;
        }

        public static string[] PickImages(string dir)
        {
            List<string> res = new List<string>();
            string[] pats = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tif", "*.tiff" };
            foreach (string p in pats)
            {
                string[] fs = Directory.GetFiles(dir, p);
                foreach (string f in fs) res.Add(f);
            }
            return res.ToArray();
        }

        public static string WifiPayload(string ssid, string pass, string type)
        {
            return "WIFI:T:" + type + ";S:" + ssid + ";P:" + pass + ";;";
        }

        public static string VCardPayload(string name, string tel, string mail)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
            sb.Append("FN:").Append(name).Append("\r\n");
            if (tel.Length > 0) sb.Append("TEL:").Append(tel).Append("\r\n");
            if (mail.Length > 0) sb.Append("EMAIL:").Append(mail).Append("\r\n");
            sb.Append("END:VCARD\r\n");
            return sb.ToString();
        }
    }
}