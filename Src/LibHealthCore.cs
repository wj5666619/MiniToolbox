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
    static class LibHealthCore
    {
        public class Found
        {
            public int AppId;
            public string Name = "";
            public string Kind = "";        // update / partial / missing / orphan
            public string Detail = "";
            public long Size;
            public string LibPath = "";
        }

        public static List<Found> Scan(List<SteamProCore.Lib> libs, out int total, out long bytes)
        {
            List<Found> res = new List<Found>();
            total = 0;
            bytes = 0;
            foreach (SteamProCore.Lib l in libs)
            {
                foreach (SteamProCore.Game g in l.Games)
                {
                    total++;
                    bytes += g.Size;
                    string baseDir = Path.Combine(l.Path, "steamapps", "common");
                    string dir = g.InstallDir.Length > 0 ? Path.Combine(baseDir, g.InstallDir) : "";
                    Found f = null;
                    if (dir.Length == 0 || !Directory.Exists(dir))
                    {
                        f = new Found();
                        f.Kind = "missing";
                        f.Detail = dir.Length == 0 ? "清单里没有安装目录名" : "安装目录不存在（可能被移动或删掉）";
                    }
                    else if ((g.StateFlags & 4) == 0)
                    {
                        f = new Found();
                        f.Kind = "partial";
                        f.Detail = "状态未标为「完整安装」（多半是下载/更新没跑完）";
                    }
                    else if ((g.StateFlags & 2) != 0)
                    {
                        f = new Found();
                        f.Kind = "update";
                        f.Detail = "Steam 标记为「需要更新」";
                    }
                    else if (g.BytesToDownload > 0 && g.BytesDownloaded > 0 && g.BytesDownloaded < g.BytesToDownload)
                    {
                        f = new Found();
                        f.Kind = "update";
                        f.Detail = "有 " + SteamProCore.GbText(g.BytesToDownload - g.BytesDownloaded) + " 没下完";
                    }
                    if (f != null)
                    {
                        f.AppId = g.AppId;
                        f.Name = g.Name;
                        f.Size = g.Size;
                        f.LibPath = l.Path;
                        res.Add(f);
                    }
                }
                // 孤儿目录：steamapps\common 下有文件夹，但没有任何 appmanifest 指向它
                try
                {
                    List<string> known = new List<string>();
                    foreach (SteamProCore.Game g in l.Games) if (g.InstallDir.Length > 0) known.Add(g.InstallDir.ToLower());
                    string common = Path.Combine(l.Path, "steamapps", "common");
                    if (Directory.Exists(common))
                    {
                        foreach (string d in Directory.GetDirectories(common))
                        {
                            string nm = Path.GetFileName(d);
                            string low = nm.ToLower();
                            // Steam 自带的系统目录不是游戏，别当成孤儿
                            if (low == "steamworks shared" || low == "steam controller configs" || low == "steamvr"
                                || low.StartsWith("steam linux runtime") || low.StartsWith("steamworks common")) continue;
                            if (known.Contains(low)) continue;
                            Found f = new Found();
                            f.AppId = 0;
                            f.Name = nm;
                            f.Kind = "orphan";
                            f.Detail = "目录在，但没有安装清单（多为卸载残留）";
                            f.LibPath = l.Path;
                            res.Add(f);
                        }
                    }
                }
                catch (Exception dg108_1) { Diag.Swallow("LibHealthCore.cs:108", dg108_1); }
            }
            return res;
        }

        public static bool Fix(Found f, out string msg)
        {
            msg = "";
            if (f == null) { msg = "没选中"; return false; }
            if (f.Kind == "orphan") { msg = "孤儿目录不自动删；确认后可在资源管理器里删除"; return false; }
            string url = f.Kind == "partial" ? ("steam://install/" + f.AppId) : ("steam://validate/" + f.AppId);
            try
            {
                Process.Start(url);
                msg = "已交给 Steam：" + url;
                return true;
            }
            catch (Exception e) { msg = "调起 Steam 失败：" + e.Message; return false; }
        }

        public static string Export(List<Found> list, int total, long bytes, string dir)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("appid,游戏,问题,体积字节,说明,所在库");
                foreach (Found f in list)
                    sb.AppendLine(f.AppId + ",\"" + f.Name.Replace("\"", "\"\"") + "\",\"" + KindZh(f.Kind) + "\"," + f.Size
                        + ",\"" + f.Detail.Replace("\"", "\"\"") + "\",\"" + f.LibPath.Replace("\"", "\"\"") + "\"");
                string p = Path.Combine(dir, "游戏库体检_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(true));
                return p + "（" + list.Count + " 条 / 共 " + total + " 款）";
            }
            catch (Exception e) { return "导出失败：" + e.Message; }
        }

        public static string KindZh(string k)
        {
            switch (k)
            {
                case "update": return "需要更新";
                case "partial": return "没装完";
                case "missing": return "文件缺失";
                case "orphan": return "孤儿目录";
            }
            return k;
        }

        public static Color KindColor(string k)
        {
            switch (k)
            {
                case "missing": return V71Ui.BAD;
                case "partial": return V71Ui.WARN;
                case "update": return UiKit.A(250, 190, 90);
                case "orphan": return UiKit.A(148, 163, 184);
            }
            return V71Ui.TEXT;
        }
    }
}