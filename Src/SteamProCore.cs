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
    static class SteamProCore
    {
        public class Game
        {
            public int AppId;
            public string Name;
            public string InstallDir;
            public long Size;
            public string LibPath;
            public int StateFlags;          // acf StateFlags：4=完整安装，2=需要更新
            public long LastPlayed;
            public long BytesToDownload;
            public long BytesDownloaded;
        }
        public class Lib
        {
            public string Path;
            public List<Game> Games = new List<Game>();
        }

        static readonly int[] Skip = { 228980, 228990, 250820, 1070560, 1491000 };

        public static string Root()
        {
            try
            {
                string p = Convert.ToString(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null));
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
            }
            catch (Exception dg52_1) { Diag.Swallow("SteamProCore.cs:52", dg52_1); }
            string[] cand = { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"D:\Steam", @"E:\Steam", @"D:\Games\Steam" };
            foreach (string c in cand) if (Directory.Exists(c)) return c;
            return "";
        }

        static void AddPath(List<string> paths, string p)
        {
            if (p == null || p.Length == 0) return;
            string np = p.Replace('/', '\\').TrimEnd('\\');
            for (int i = 0; i < paths.Count; i++)
                if (string.Equals(paths[i], np, StringComparison.OrdinalIgnoreCase)) return;
            paths.Add(np);
        }

        public static List<Lib> Libraries()
        {
            List<Lib> res = new List<Lib>();
            string root = Root();
            if (root.Length == 0) return res;
            List<string> paths = new List<string>();
            AddPath(paths, root);
            string lf = Path.Combine(root, "steamapps\\libraryfolders.vdf");
            if (File.Exists(lf))
            {
                try
                {
                    string txt = File.ReadAllText(lf, Encoding.UTF8);
                    System.Text.RegularExpressions.MatchCollection ms =
                        System.Text.RegularExpressions.Regex.Matches(txt, "\"path\"\\s+\"([^\"]+)\"");
                    foreach (System.Text.RegularExpressions.Match m in ms)
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p)) AddPath(paths, p);
                    }
                }
                catch (Exception dg88_1) { Diag.Swallow("SteamProCore.cs:88", dg88_1); }
            }
            foreach (string p in paths)
            {
                Lib l = new Lib();
                l.Path = p;
                string apps = Path.Combine(p, "steamapps");
                if (Directory.Exists(apps))
                {
                    string[] acfs = null;
                    try { acfs = Directory.GetFiles(apps, "appmanifest_*.acf"); }
                    catch { acfs = null; }
                    if (acfs != null)
                    {
                        foreach (string acf in acfs)
                        {
                            Dictionary<string, string> d = ParseAcf(acf);
                            Game g = new Game();
                            int id = 0;
                            if (d.ContainsKey("appid")) int.TryParse(d["appid"], out id);
                            if (id <= 0) continue;
                            bool skip = false;
                            for (int i = 0; i < Skip.Length; i++) if (Skip[i] == id) skip = true;
                            if (skip) continue;
                            g.AppId = id;
                            g.Name = d.ContainsKey("name") && d["name"].Trim().Length > 0 ? d["name"].Trim() : ("App " + id);
                            g.InstallDir = d.ContainsKey("installdir") ? d["installdir"] : "";
                            long sz = 0;
                            if (d.ContainsKey("sizeondisk")) long.TryParse(d["sizeondisk"], out sz);
                            g.Size = sz;
                            g.LibPath = p;
                            int sf = 0;
                            if (d.ContainsKey("stateflags")) int.TryParse(d["stateflags"], out sf);
                            g.StateFlags = sf;
                            long lp = 0;
                            if (d.ContainsKey("lastplayed")) long.TryParse(d["lastplayed"], out lp);
                            g.LastPlayed = lp;
                            long btd = 0;
                            if (d.ContainsKey("bytestodownload")) long.TryParse(d["bytestodownload"], out btd);
                            g.BytesToDownload = btd;
                            long bd = 0;
                            if (d.ContainsKey("bytesdownloaded")) long.TryParse(d["bytesdownloaded"], out bd);
                            g.BytesDownloaded = bd;
                            l.Games.Add(g);
                        }
                    }
                }
                res.Add(l);
            }
            return res;
        }

        static Dictionary<string, string> ParseAcf(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            try
            {
                string txt = File.ReadAllText(path, Encoding.UTF8);
                System.Text.RegularExpressions.MatchCollection ms =
                    System.Text.RegularExpressions.Regex.Matches(txt, "\"([^\"]+)\"\\s+\"([^\"]*)\"");
                foreach (System.Text.RegularExpressions.Match m in ms)
                {
                    string k = m.Groups[1].Value.ToLower();
                    if (!d.ContainsKey(k)) d[k] = m.Groups[2].Value;
                }
            }
            catch (Exception dg154_1) { Diag.Swallow("SteamProCore.cs:154", dg154_1); }
            return d;
        }

        public static long GamesBytes(Lib l)
        {
            long s = 0;
            foreach (Game g in l.Games) s += g.Size;
            return s;
        }

        public static string GbText(long b)
        {
            if (b <= 0) return "-";
            double g = b / 1073741824.0;
            if (g >= 1) return g.ToString("0.00") + " GB";
            return (b / 1048576.0).ToString("0") + " MB";
        }

        public static string ExportCsv(List<Lib> libs, string dir)
        {
            try
            {
                string p = Path.Combine(dir, "Steam库清单_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("AppID,名称,大小(字节),大小,安装目录,所在库");
                foreach (Lib l in libs)
                {
                    foreach (Game g in l.Games)
                    {
                        sb.AppendLine(g.AppId + "," + Csv(g.Name) + "," + g.Size + "," + GbText(g.Size) + "," + Csv(g.InstallDir) + "," + Csv(l.Path));
                    }
                }
                File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
                return p;
            }
            catch { return ""; }
        }

        static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // 搬家：优先直接 Move（同盘瞬时），跨盘时退化为复制 + 删除
        public static bool MoveGame(Game g, string targetLib, out string err)
        {
            err = "";
            try
            {
                string srcCommon = Path.Combine(g.LibPath, "steamapps", "common");
                string dstCommon = Path.Combine(targetLib, "steamapps", "common");
                if (g.InstallDir.Length == 0) { err = "appmanifest 里没有 installdir"; return false; }
                string srcDir = Path.Combine(srcCommon, g.InstallDir);
                string dstDir = Path.Combine(dstCommon, g.InstallDir);
                if (!Directory.Exists(srcDir)) { err = "游戏目录不存在：" + srcDir; return false; }
                if (Directory.Exists(dstDir)) { err = "目标目录已存在：" + dstDir; return false; }
                if (!Directory.Exists(dstCommon)) Directory.CreateDirectory(dstCommon);
                try { Directory.Move(srcDir, dstDir); }
                catch
                {
                    CopyDir(srcDir, dstDir);
                    Directory.Delete(srcDir, true);
                }
                string acfName = "appmanifest_" + g.AppId + ".acf";
                string srcAcf = Path.Combine(g.LibPath, "steamapps", acfName);
                string dstAcf = Path.Combine(targetLib, "steamapps", acfName);
                if (File.Exists(srcAcf))
                {
                    if (File.Exists(dstAcf)) File.Delete(dstAcf);
                    File.Move(srcAcf, dstAcf);
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        public static List<KeyValuePair<string, int>> NodeLatency()
        {
            string[] hosts = { "store.steampowered.com", "api.steampowered.com", "cdn.steamstatic.com", "media.steampowered.com", "steamcdn-a.akamaihd.net" };
            List<KeyValuePair<string, int>> res = new List<KeyValuePair<string, int>>();
            foreach (string h in hosts)
            {
                int ms = TcpLatency(h, 443, 3000);
                res.Add(new KeyValuePair<string, int>(h, ms));
            }
            return res;
        }

        public static int TcpLatency(string host, int port, int timeoutMs)
        {
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                System.Net.Sockets.TcpClient c = new System.Net.Sockets.TcpClient();
                sw.Start();
                System.Net.Sockets.TcpClient cc = c;
                IAsyncResult ar = cc.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    c.Close();
                    return -1;
                }
                cc.EndConnect(ar);
                sw.Stop();
                int ms = (int)sw.ElapsedMilliseconds;
                c.Close();
                return ms;
            }
            catch { return -1; }
        }
    }
}