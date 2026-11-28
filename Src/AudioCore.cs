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
    static class AudioCore
    {
        public static string Codec(string ext, int kbps)
        {
            if (ext == "mp3") return "-c:a libmp3lame -b:a " + kbps + "k";
            if (ext == "m4a" || ext == "aac") return "-c:a aac -b:a " + kbps + "k";
            if (ext == "wav") return "-c:a pcm_s16le";
            if (ext == "flac") return "-c:a flac";
            return "-c:a aac -b:a " + kbps + "k";
        }

        static string Suf(string src, string tag, string ext)
        {
            return Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_" + tag + "." + ext);
        }

        static string TailLog(string log)
        {
            if (log == null) return "";
            string s = log.Trim();
            if (s.Length > 260) s = s.Substring(s.Length - 260);
            return s.Replace('\n', ' ');
        }

        public static bool Convert(string baseDir, string src, string ext, int kbps, out string dst, out string err)
        {
            dst = ""; err = "";
            string ff = VideoProCore.Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            dst = Suf(src, "转码", ext);
            string args = "-hide_banner -y -i \"" + src + "\" -vn " + Codec(ext, kbps) + " \"" + dst + "\"";
            string log = VideoProCore.Run(ff, args, 900000, null);
            if (!File.Exists(dst) || new FileInfo(dst).Length <= 0) { err = TailLog(log); return false; }
            return true;
        }

        public static bool Normalize(string baseDir, string src, out string dst, out string err)
        {
            return Normalize(baseDir, src, -16, out dst, out err);
        }

        // v12.0：目标响度可选（市面级归一都带 LUFS 档位：-14 音乐、-16 通用、-20 播客）
        public static bool Normalize(string baseDir, string src, int lufs, out string dst, out string err)
        {
            dst = ""; err = "";
            string ff = VideoProCore.Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            dst = Suf(src, "归一" + Math.Abs(lufs), "mp3");
            string args = "-hide_banner -y -i \"" + src + "\" -vn -af loudnorm=I=" + lufs + ":TP=-1.5:LRA=11 -c:a libmp3lame -b:a 192k \"" + dst + "\"";
            string log = VideoProCore.Run(ff, args, 900000, null);
            if (!File.Exists(dst) || new FileInfo(dst).Length <= 0) { err = TailLog(log); return false; }
            return true;
        }

        // v12.0：去静音——掐掉开头结尾的无音区，并压掉中段长于 1 秒的空白（录屏 / 会议录音最常用）
        public static bool TrimSilence(string baseDir, string src, out string dst, out string err)
        {
            dst = ""; err = "";
            string ff = VideoProCore.Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            dst = Suf(src, "去静音", "mp3");
            string af = "silenceremove=start_periods=1:start_duration=0.1:start_threshold=-50dB:detection=peak,"
                      + "areverse,silenceremove=start_periods=1:start_duration=0.1:start_threshold=-50dB:detection=peak,areverse,"
                      + "silenceremove=stop_periods=-1:stop_duration=1:stop_threshold=-45dB:detection=peak";
            string args = "-hide_banner -y -i \"" + src + "\" -vn -af \"" + af + "\" -c:a libmp3lame -b:a 192k \"" + dst + "\"";
            string log = VideoProCore.Run(ff, args, 900000, null);
            if (!File.Exists(dst) || new FileInfo(dst).Length <= 0) { err = TailLog(log); return false; }
            return true;
        }

        public static bool Denoise(string baseDir, string src, out string dst, out string err)
        {
            dst = ""; err = "";
            string ff = VideoProCore.Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            dst = Suf(src, "降噪", "mp3");
            string args = "-hide_banner -y -i \"" + src + "\" -vn -af afftdn=nf=-25 -c:a libmp3lame -b:a 192k \"" + dst + "\"";
            string log = VideoProCore.Run(ff, args, 900000, null);
            if (!File.Exists(dst) || new FileInfo(dst).Length <= 0) { err = TailLog(log); return false; }
            return true;
        }

        public static bool Merge(string baseDir, List<string> srcs, out string dst, out string err)
        {
            dst = ""; err = "";
            string ff = VideoProCore.Ffmpeg(baseDir);
            if (ff.Length == 0) { err = "缺少 ffmpeg"; return false; }
            if (srcs == null || srcs.Count == 0) { err = "没有选中文件"; return false; }
            StringBuilder sb = new StringBuilder();
            sb.Append("-hide_banner -y ");
            foreach (string s in srcs) sb.Append("-i \"").Append(s).Append("\" ");
            sb.Append("-filter_complex \"");
            for (int i = 0; i < srcs.Count; i++) sb.Append('[').Append(i).Append(":a]");
            sb.Append("concat=n=").Append(srcs.Count).Append(":v=0:a=1[a]\" -map \"[a]\" -c:a aac -b:a 192k \"");
            dst = Path.Combine(Path.GetDirectoryName(srcs[0]), "合并_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".m4a");
            sb.Append(dst).Append('"');
            string log = VideoProCore.Run(ff, sb.ToString(), 900000, null);
            if (!File.Exists(dst) || new FileInfo(dst).Length <= 0) { err = TailLog(log); dst = ""; return false; }
            return true;
        }

        public static string SelfTestText(string baseDir)
        {
            StringBuilder sb = new StringBuilder();
            string ff = VideoProCore.Ffmpeg(baseDir);
            sb.AppendLine("ffmpeg=" + (ff.Length > 0 ? ff : "missing"));
            if (ff.Length == 0) return sb.ToString();
            string tdir = Path.Combine(Path.GetTempPath(), "mtb_audio");
            try { if (!Directory.Exists(tdir)) Directory.CreateDirectory(tdir); } catch (Exception dg131_1) { Diag.Swallow("AudioCore.cs:131", dg131_1); }
            string a = Path.Combine(tdir, "tone_a.wav");
            string b = Path.Combine(tdir, "tone_b.wav");
            VideoProCore.Run(ff, "-hide_banner -y -f lavfi -i sine=frequency=440:duration=2 -ar 44100 \"" + a + "\"", 60000, null);
            VideoProCore.Run(ff, "-hide_banner -y -f lavfi -i sine=frequency=880:duration=2 -ar 44100 \"" + b + "\"", 60000, null);
            sb.AppendLine("tone_a=" + (File.Exists(a) ? new FileInfo(a).Length.ToString() : "0"));
            string outDst = "", err = "";
            bool c1 = Convert(baseDir, a, "mp3", 192, out outDst, out err);
            sb.AppendLine("convert_ok=" + c1 + " " + (c1 ? new FileInfo(outDst).Length.ToString() : err));
            bool c2 = Normalize(baseDir, a, out outDst, out err);
            sb.AppendLine("normalize_ok=" + c2 + " " + (c2 ? new FileInfo(outDst).Length.ToString() : err));
            bool c3 = Denoise(baseDir, a, out outDst, out err);
            sb.AppendLine("denoise_ok=" + c3 + " " + (c3 ? new FileInfo(outDst).Length.ToString() : err));
            List<string> two = new List<string>();
            two.Add(a); two.Add(b);
            bool c4 = Merge(baseDir, two, out outDst, out err);
            double dur = c4 ? VideoProCore.Duration(baseDir, outDst) : 0;
            sb.AppendLine("merge_ok=" + c4 + " dur=" + dur.ToString("0.00") + (c4 ? "" : " " + err));
            return sb.ToString();
        }
    }
}