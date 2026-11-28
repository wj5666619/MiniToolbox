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
    class DeskSortCore
    {
        static readonly string[] E_IMG = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".svg", ".psd", ".tif", ".tiff", ".ico", ".raw" };
        static readonly string[] E_VID = { ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v", ".ts" };
        static readonly string[] E_AUD = { ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma" };
        static readonly string[] E_DOC = { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".md", ".csv", ".rtf", ".odt", ".epub" };
        static readonly string[] E_ARC = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso" };
        static readonly string[] E_INS = { ".exe", ".msi", ".apk", ".appx" };

        public static string TypeOf(string name)
        {
            string e = Path.GetExtension(name);
            if (e == null) return "其他";
            e = e.ToLowerInvariant();
            if (Array.IndexOf(E_IMG, e) >= 0) return "图片";
            if (Array.IndexOf(E_VID, e) >= 0) return "视频";
            if (Array.IndexOf(E_AUD, e) >= 0) return "音频";
            if (Array.IndexOf(E_DOC, e) >= 0) return "文档";
            if (Array.IndexOf(E_ARC, e) >= 0) return "压缩包";
            if (Array.IndexOf(E_INS, e) >= 0) return "安装包";
            return "其他";
        }

        // 返回 [原路径, 文件名, 类型, 目标路径]
        public static List<string[]> Plan(string dir)
        {
            List<string[]> plan = new List<string[]>();
            if (!Directory.Exists(dir)) return plan;
            string[] fs;
            try { fs = Directory.GetFiles(dir); }
            catch { return plan; }
            foreach (string f in fs)
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("~$")) continue;
                string ext = Path.GetExtension(name);
                if (ext != null && ext.ToLowerInvariant() == ".lnk") continue;   // 桌面快捷方式不动
                string type = TypeOf(name);
                string tdir = Path.Combine(dir, "整理-" + type);
                plan.Add(new string[] { f, name, type, Path.Combine(tdir, name) });
            }
            return plan;
        }

        static string Unique(string dst)
        {
            if (!File.Exists(dst)) return dst;
            string dir = Path.GetDirectoryName(dst);
            string name = Path.GetFileNameWithoutExtension(dst);
            string ext = Path.GetExtension(dst);
            for (int i = 1; i < 500; i++)
            {
                string c = Path.Combine(dir, name + "_" + i + ext);
                if (!File.Exists(c)) return c;
            }
            return dst;
        }

        public static string Execute(List<string[]> plan, string recordDir, bool dryRun)
        {
            StringBuilder rep = new StringBuilder();
            StringBuilder rec = new StringBuilder();
            rec.Append("{\"time\":\"").Append(VaultStore.Esc(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append("\",\"items\":[");
            int moved = 0, skip = 0;
            bool firstRec = true;
            foreach (string[] p in plan)
            {
                string src = p[0];
                string type = p[2];
                try
                {
                    if (!File.Exists(src)) { skip++; continue; }
                    string target = p[3];
                    string tdir = Path.GetDirectoryName(target);
                    if (dryRun)
                    {
                        rep.AppendLine("· " + p[1] + " → 整理-" + type + "\\");
                        moved++;
                        continue;
                    }
                    if (!Directory.Exists(tdir)) Directory.CreateDirectory(tdir);
                    target = Unique(target);
                    File.Move(src, target);
                    if (!firstRec) rec.Append(",");
                    firstRec = false;
                    rec.Append("{\"f\":\"").Append(VaultStore.Esc(src)).Append("\",\"t\":\"").Append(VaultStore.Esc(target)).Append("\"}");
                    moved++;
                }
                catch (Exception ex)
                {
                    rep.AppendLine("· " + p[1] + " 移动失败：" + ex.Message);
                    skip++;
                }
            }
            rec.Append("]}");
            if (!dryRun)
            {
                try
                {
                    if (!Directory.Exists(recordDir)) Directory.CreateDirectory(recordDir);
                    string rf = Path.Combine(recordDir, "记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                    File.WriteAllText(rf, rec.ToString(), Encoding.UTF8);
                    rep.AppendLine("记录已保存：" + rf);
                }
                catch (Exception ex) { rep.AppendLine("记录写入失败：" + ex.Message); }
            }
            rep.AppendLine("合计移动 " + moved + " 个文件，跳过 " + skip + " 个。");
            return rep.ToString();
        }

        public static string LatestRecord(string recordDir)
        {
            try
            {
                if (!Directory.Exists(recordDir)) return null;
                string[] fs = Directory.GetFiles(recordDir, "记录_*.json");
                if (fs.Length == 0) return null;
                Array.Sort(fs);
                return fs[fs.Length - 1];
            }
            catch { return null; }
        }

        public static string Undo(string recordFile, bool dryRun)
        {
            StringBuilder rep = new StringBuilder();
            if (recordFile == null || !File.Exists(recordFile)) return "没有可撤销的记录。";
            string json = File.ReadAllText(recordFile, Encoding.UTF8);
            JavaScriptSerializer js = new JavaScriptSerializer();
            object root = js.DeserializeObject(json);
            System.Collections.Generic.IDictionary<string, object> rd = root as System.Collections.Generic.IDictionary<string, object>;
            if (rd == null) return "记录格式不对。";
            System.Collections.IEnumerable arr = rd["items"] as System.Collections.IEnumerable;
            if (arr == null) return "记录里没有条目。";
            int back = 0, skip = 0;
            foreach (object o in arr)
            {
                System.Collections.Generic.IDictionary<string, object> d = o as System.Collections.Generic.IDictionary<string, object>;
                if (d == null) continue;
                string from = Convert.ToString(d["f"]);
                string to = Convert.ToString(d["t"]);
                try
                {
                    if (!File.Exists(to)) { skip++; continue; }
                    string dir = Path.GetDirectoryName(from);
                    if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(from)) from = Unique(from);
                    if (dryRun) { rep.AppendLine("· " + Path.GetFileName(to) + " → 桌面"); back++; continue; }
                    File.Move(to, from);
                    back++;
                }
                catch (Exception ex) { rep.AppendLine("· " + Path.GetFileName(to) + " 还原失败：" + ex.Message); skip++; }
            }
            // 清掉空的整理目录
            if (!dryRun)
            {
                try
                {
                    foreach (object o in arr)
                    {
                        System.Collections.Generic.IDictionary<string, object> d = o as System.Collections.Generic.IDictionary<string, object>;
                        if (d == null) continue;
                        string tdir = Path.GetDirectoryName(Convert.ToString(d["t"]));
                        if (tdir != null && Directory.Exists(tdir) && Directory.GetFiles(tdir).Length == 0 && Directory.GetDirectories(tdir).Length == 0)
                            Directory.Delete(tdir);
                    }
                }
                catch (Exception dg190_1) { Diag.Swallow("DeskSortCore.cs:190", dg190_1); }
            }
            rep.AppendLine("已还原 " + back + " 个文件，跳过 " + skip + " 个。");
            if (!dryRun)
            {
                try { File.Delete(recordFile); } catch (Exception dg195_1) { Diag.Swallow("DeskSortCore.cs:195", dg195_1); }
            }
            return rep.ToString();
        }
    }
}