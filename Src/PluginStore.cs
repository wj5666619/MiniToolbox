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
    static class PluginStore
    {
        public class Entry
        {
            public string Name = "";
            public string File = "";
            public string Ver = "";
            public string Author = "";
            public string Desc = "";
            public string Kind = "builtin";   // builtin / url / zip
            public string Payload = "";       // 内置 id，或下载地址
            public string Source = "本地";
            public long Size = 0;
        }

        public static string Dir = "", IndexFile = "", SourceFile = "", CacheDir = "";
        public static int LastNetHits = 0, LastCacheHits = 0;

        public static void Init(string baseDir)
        {
            Dir = Path.Combine(baseDir, "插件");
            try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); } catch (Exception dg44_1) { Diag.Swallow("PluginStore.cs:44", dg44_1); }
            IndexFile = Path.Combine(Dir, "仓库.json");
            SourceFile = Path.Combine(Dir, "仓库源.txt");
            CacheDir = Path.Combine(Dir, "缓存");
            try { if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir); } catch (Exception dg48_1) { Diag.Swallow("PluginStore.cs:48", dg48_1); }
            EnsureIndex();
        }

        static Entry E(string name, string file, string ver, string author, string desc, string kind, string payload)
        {
            Entry e = new Entry();
            e.Name = name; e.File = file; e.Ver = ver; e.Author = author; e.Desc = desc; e.Kind = kind; e.Payload = payload;
            return e;
        }

        public static List<Entry> Builtins()
        {
            List<Entry> l = new List<Entry>();
            l.Add(E("磁盘空间看板", "磁盘空间看板.ps1", "1.0", "官方", "列出所有盘符的总量、剩余与占用条，一眼看出谁快满了", "builtin", "disk"));
            l.Add(E("网络延迟速测", "网络延迟速测.ps1", "1.0", "官方", "对常用站点 ping 一轮，按延迟排序，掉线一眼可见", "builtin", "latency"));
            l.Add(E("进程 Top10", "进程Top10.ps1", "1.0", "官方", "按 CPU 与内存排出最吃资源的十个进程，附 PID", "builtin", "proc"));
            l.Add(E("批量改名向导", "批量改名向导.ps1", "1.0", "官方", "给一个目录、一个前缀，先预览再改名，改名可撤销", "builtin", "rename"));
            return l;
        }

        static string Q(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static void EnsureIndex()
        {
            try
            {
                if (File.Exists(IndexFile)) return;
                List<Entry> bl = Builtins();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"name\": \"小工具箱官方仓库\",");
                sb.AppendLine("  \"plugins\": [");
                for (int i = 0; i < bl.Count; i++)
                {
                    sb.Append("    { \"name\": " + Q(bl[i].Name) + ", \"file\": " + Q(bl[i].File) + ", \"ver\": " + Q(bl[i].Ver)
                        + ", \"author\": " + Q(bl[i].Author) + ", \"desc\": " + Q(bl[i].Desc)
                        + ", \"kind\": \"builtin\", \"payload\": " + Q(bl[i].Payload) + " }");
                    if (i < bl.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(IndexFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg97_1) { Diag.Swallow("PluginStore.cs:97", dg97_1); }
        }

        public static string[] Sources()
        {
            try
            {
                if (!File.Exists(SourceFile)) return new string[0];
                List<string> l = new List<string>();
                string[] ln = File.ReadAllLines(SourceFile, Encoding.UTF8);
                for (int i = 0; i < ln.Length; i++)
                {
                    string x = ln[i] == null ? "" : ln[i].Trim();
                    if (x.Length == 0 || x.StartsWith("#")) continue;
                    if (!x.StartsWith("http")) continue;
                    l.Add(x);
                }
                return l.ToArray();
            }
            catch { return new string[0]; }
        }

        public static void AddSource(string url)
        {
            try
            {
                if (url == null || !url.StartsWith("http")) return;
                List<string> l = new List<string>();
                if (File.Exists(SourceFile))
                {
                    string[] ln = File.ReadAllLines(SourceFile, Encoding.UTF8);
                    for (int i = 0; i < ln.Length; i++) if (ln[i] != null && ln[i].Trim().Length > 0) l.Add(ln[i].Trim());
                }
                if (!l.Contains(url)) l.Add(url);
                File.WriteAllLines(SourceFile, l.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception dg133_1) { Diag.Swallow("PluginStore.cs:133", dg133_1); }
        }

        // ---------- v12.5 本地缓存：远程源拉取成功就落盘，失败时回退到上次的副本 ----------
        static string CacheFile(int i)
        {
            return Path.Combine(CacheDir, "源" + (i + 1) + ".json");
        }

        public static int CacheCount()
        {
            try { return Directory.Exists(CacheDir) ? Directory.GetFiles(CacheDir, "*.json").Length : 0; }
            catch { return 0; }
        }

        public static string ClearCache()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return "缓存本来就是空的";
                string[] fs = Directory.GetFiles(CacheDir, "*.json");
                int n = 0;
                for (int i = 0; i < fs.Length; i++)
                {
                    try { System.IO.File.Delete(fs[i]); n++; } catch (Exception dg157_1) { Diag.Swallow("PluginStore.cs:157", dg157_1); }
                }
                return "已清除 " + n + " 个缓存源";
            }
            catch (Exception ex) { return "清理失败：" + ex.Message; }
        }

        static string FetchOrCache(string url, int i, out bool fromCache)
        {
            fromCache = false;
            try
            {
                string js = Fetch(url, 6000);
                if (js != null && js.Trim().Length > 0)
                {
                    try { System.IO.File.WriteAllText(CacheFile(i), js, new UTF8Encoding(false)); } catch (Exception dg172_1) { Diag.Swallow("PluginStore.cs:172", dg172_1); }
                    LastNetHits++;
                    return js;
                }
            }
            catch (Exception dg177_1) { Diag.Swallow("PluginStore.cs:177", dg177_1); }
            try
            {
                string cf = CacheFile(i);
                if (System.IO.File.Exists(cf))
                {
                    fromCache = true;
                    LastCacheHits++;
                    return System.IO.File.ReadAllText(cf, Encoding.UTF8);
                }
            }
            catch (Exception dg188_1) { Diag.Swallow("PluginStore.cs:188", dg188_1); }
            return "";
        }

        static string Fetch(string url, int ms)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = ms;
            req.ReadWriteTimeout = ms;
            req.UserAgent = "MiniToolbox/12.4";
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream st = res.GetResponseStream())
            using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string S(Dictionary<string, object> d, string k)
        {
            if (d == null || k == null || !d.ContainsKey(k) || d[k] == null) return "";
            object o = d[k];
            return o.ToString().Trim();
        }

        public static List<Entry> Parse(string js, string src)
        {
            List<Entry> list = new List<Entry>();
            if (js == null || js.Trim().Length == 0) return list;
            try
            {
                object o = new JavaScriptSerializer().DeserializeObject(js);
                object arr = o;
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d != null)
                {
                    if (d.ContainsKey("plugins")) arr = d["plugins"];
                    else if (d.ContainsKey("items")) arr = d["items"];
                }
                object[] items = arr as object[];
                if (items == null)
                {
                    System.Collections.ArrayList al = arr as System.Collections.ArrayList;
                    if (al != null) items = al.ToArray();
                }
                if (items == null) return list;
                for (int i = 0; i < items.Length; i++)
                {
                    Dictionary<string, object> it = items[i] as Dictionary<string, object>;
                    if (it == null) continue;
                    Entry e = new Entry();
                    e.Name = S(it, "name");
                    if (e.Name.Length == 0) continue;
                    e.File = S(it, "file");
                    if (e.File.Length == 0) e.File = e.Name + ".ps1";
                    e.Ver = S(it, "ver");
                    if (e.Ver.Length == 0) e.Ver = S(it, "version");
                    e.Author = S(it, "author");
                    e.Desc = S(it, "desc");
                    if (e.Desc.Length == 0) e.Desc = S(it, "description");
                    e.Kind = S(it, "kind");
                    if (e.Kind.Length == 0) e.Kind = "url";
                    e.Payload = S(it, "payload");
                    if (e.Payload.Length == 0) e.Payload = S(it, "url");
                    e.Source = src;
                    if (e.Kind == "builtin" && e.Payload.Length == 0) continue;
                    if (e.Kind != "builtin" && e.Payload.Length == 0) continue;
                    list.Add(e);
                }
            }
            catch (Exception dg256_1) { Diag.Swallow("PluginStore.cs:256", dg256_1); }
            return list;
        }

        public static List<Entry> Load()
        {
            List<Entry> all = new List<Entry>();
            try { if (File.Exists(IndexFile)) all.AddRange(Parse(File.ReadAllText(IndexFile, Encoding.UTF8), "本地")); } catch (Exception dg263_1) { Diag.Swallow("PluginStore.cs:263", dg263_1); }
            LastNetHits = 0;
            LastCacheHits = 0;
            string[] src = Sources();
            for (int i = 0; i < src.Length && i < 4; i++)
            {
                bool fromCache = false;
                string js = FetchOrCache(src[i], i, out fromCache);
                if (js == null || js.Trim().Length == 0) continue;
                all.AddRange(Parse(js, fromCache ? ("缓存 " + (i + 1)) : ("远程 " + (i + 1))));
            }
            return all;
        }

        public static string Target(Entry e)
        {
            return Path.Combine(Dir, SafeName(e.File));
        }

        static string SafeName(string f)
        {
            if (f == null || f.Length == 0) return "plugin.ps1";
            string nm = Path.GetFileName(f);
            if (nm.Length == 0) return "plugin.ps1";
            return nm;
        }

        public static bool Installed(Entry e)
        {
            try { return File.Exists(Target(e)); } catch { return false; }
        }

        public static string BuiltinBody(string id)
        {
            if (id == "disk")
                return "# 描述: 列出所有盘符的总量、剩余与占用条\r\n" +
                       "$ErrorActionPreference='SilentlyContinue'\r\n" +
                       "Get-CimInstance Win32_LogicalDisk -Filter \"DriveType=3\" | ForEach-Object {\r\n" +
                       "  $pct = [int](($_.FreeSpace / $_.Size) * 100)\r\n" +
                       "  $fill = [int]($pct / 5)\r\n" +
                       "  $bar = ('#' * $fill) + ('-' * (20 - $fill))\r\n" +
                       "  '{0}  总 {1:N0} GB  剩 {2:N0} GB  可用 {3}%  |{4}|' -f $_.DeviceID, ($_.Size/1GB), ($_.FreeSpace/1GB), $pct, $bar\r\n" +
                       "}\r\n" +
                       "Write-Host ''\r\n" +
                       "Read-Host '按回车关闭'\r\n";
            if (id == "latency")
                return "# 描述: 对常用站点 ping 一轮，按延迟排序\r\n" +
                       "$ErrorActionPreference='SilentlyContinue'\r\n" +
                       "$hosts = @('www.baidu.com','www.qq.com','github.com','www.aliyun.com','www.bing.com','store.steampowered.com','www.163.com','www.taobao.com')\r\n" +
                       "$rows = foreach ($h in $hosts) {\r\n" +
                       "  $r = Test-Connection -ComputerName $h -Count 4 -ErrorAction SilentlyContinue\r\n" +
                       "  if ($r) { [pscustomobject]@{ 站点=$h; 平均延迟=([int](($r | Measure-Object -Property ResponseTime -Average).Average)).ToString()+' ms' } }\r\n" +
                       "  else { [pscustomobject]@{ 站点=$h; 平均延迟='不通' } }\r\n" +
                       "}\r\n" +
                       "$rows | Sort-Object { if ($_.'平均延迟' -eq '不通') { 99999 } else { [int]($_.'平均延迟' -replace ' ms','') } } | Format-Table -AutoSize\r\n" +
                       "Read-Host '按回车关闭'\r\n";
            if (id == "proc")
                return "# 描述: 按 CPU 与内存排出最吃资源的十个进程\r\n" +
                       "$ErrorActionPreference='SilentlyContinue'\r\n" +
                       "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 `\r\n" +
                       "  @{n='进程';e={$_.ProcessName}}, @{n='PID';e={$_.Id}}, @{n='CPU秒';e={[int]$_.CPU}}, @{n='内存MB';e={[int]($_.WorkingSet64/1MB)}} |\r\n" +
                       "  Format-Table -AutoSize\r\n" +
                       "Read-Host '按回车关闭'\r\n";
            if (id == "rename")
                return "# 描述: 给一个目录、一个前缀，先预览再改名\r\n" +
                       "$ErrorActionPreference='Stop'\r\n" +
                       "$dir = Read-Host '目录路径'\r\n" +
                       "$pfx = Read-Host '新文件名前缀（可留空）'\r\n" +
                       "if (-not (Test-Path $dir)) { Write-Host '目录不存在'; Read-Host '回车关闭'; exit }\r\n" +
                       "$files = Get-ChildItem -LiteralPath $dir -File | Sort-Object Name\r\n" +
                       "$i = 1\r\n" +
                       "$files | ForEach-Object { '{0}  ->  {1}{2:D3}{3}' -f $_.Name, $pfx, $i, $_.Extension; $i++ }\r\n" +
                       "$ok = Read-Host '确认改名？输入 y 回车执行'\r\n" +
                       "if ($ok -eq 'y') {\r\n" +
                       "  $i = 1\r\n" +
                       "  foreach ($f in $files) { Rename-Item -LiteralPath $f.FullName -NewName ('{0}{1:D3}{2}' -f $pfx, $i, $f.Extension); $i++ }\r\n" +
                       "  Write-Host '完成'\r\n" +
                       "}\r\n" +
                       "Read-Host '按回车关闭'\r\n";
            return "";
        }

        public static string Install(Entry e)
        {
            if (e == null) return "空条目";
            try
            {
                string target = Target(e);
                if (e.Kind == "builtin")
                {
                    string body = BuiltinBody(e.Payload);
                    if (body.Length == 0) return "内置插件缺失：" + e.Payload;
                    File.WriteAllText(target, body, new UTF8Encoding(false));
                    MarkVer(e);
                    return "已安装 " + e.Name;
                }
                string ext = Path.GetExtension(e.Payload.Split('?')[0]);
                string tmp = Path.Combine(Path.GetTempPath(), "mtb_pl_" + DateTime.Now.Ticks + ext);
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "MiniToolbox");
                    wc.DownloadFile(e.Payload, tmp);
                }
                if (ext == ".zip" || e.Kind == "zip")
                {
                    int n = 0;
                    using (ZipArchive za = ZipFile.OpenRead(tmp))
                    {
                        foreach (ZipArchiveEntry ze in za.Entries)
                        {
                            string nm = Path.GetFileName(ze.FullName);
                            if (nm.Length == 0) continue;
                            ze.ExtractToFile(Path.Combine(Dir, nm), true);
                            n++;
                        }
                    }
                    try { File.Delete(tmp); } catch (Exception dg379_1) { Diag.Swallow("PluginStore.cs:379", dg379_1); }
                    MarkVer(e);
                    return "已解压安装 " + n + " 个文件";
                }
                File.Copy(tmp, target, true);
                try { File.Delete(tmp); } catch (Exception dg384_1) { Diag.Swallow("PluginStore.cs:384", dg384_1); }
                MarkVer(e);
                return "已下载安装 " + e.Name;
            }
            catch (Exception ex) { return "安装失败：" + ex.Message; }
        }


        // ---------- v12.6 ④ 已装版本标记 + 一键更新 ----------
        public static string VerFile(Entry e)
        {
            try { return Path.Combine(Dir, e.File + ".ver"); } catch { return ""; }
        }

        public static void MarkVer(Entry e)
        {
            try { File.WriteAllText(VerFile(e), e.Ver == null ? "" : e.Ver, new UTF8Encoding(false)); } catch (Exception dg400_1) { Diag.Swallow("PluginStore.cs:400", dg400_1); }
        }

        public static string InstalledVer(Entry e)
        {
            try
            {
                string f = VerFile(e);
                if (f.Length > 0 && File.Exists(f)) return File.ReadAllText(f, Encoding.UTF8).Trim();
            }
            catch (Exception dg410_1) { Diag.Swallow("PluginStore.cs:410", dg410_1); }
            return "";
        }

        public static bool CanUpdate(Entry e)
        {
            try
            {
                if (e == null || !Installed(e)) return false;
                string have = InstalledVer(e);
                if (have.Length == 0 || e.Ver == null || e.Ver.Length == 0) return false;
                return CmpVer(have, e.Ver) < 0;
            }
            catch { return false; }
        }

        public static int CmpVer(string a, string b)
        {
            try
            {
                string[] x = a.Split('.'), y = b.Split('.');
                int n = Math.Max(x.Length, y.Length);
                for (int i = 0; i < n; i++)
                {
                    int xi = 0, yi = 0;
                    if (i < x.Length) { int.TryParse(x[i].Trim(), out xi); }
                    if (i < y.Length) { int.TryParse(y[i].Trim(), out yi); }
                    if (xi != yi) return xi < yi ? -1 : 1;
                }
                return 0;
            }
            catch { return 0; }
        }

        public static string StateText(Entry e)
        {
            if (e == null) return "";
            if (!Installed(e)) return "未安装";
            string have = InstalledVer(e);
            if (CanUpdate(e)) return "可更新 " + have + "→" + e.Ver;
            return have.Length > 0 ? "已安装 v" + have : "已安装";
        }

        public static int UpdateAll(out int tried)
        {
            tried = 0;
            int ok = 0;
            try
            {
                List<Entry> all = Load();
                for (int i = 0; i < all.Count; i++)
                {
                    if (!CanUpdate(all[i])) continue;
                    tried++;
                    string r = Install(all[i]);
                    if (r != null && r.IndexOf("失败", StringComparison.Ordinal) < 0) ok++;
                }
            }
            catch (Exception dg468_1) { Diag.Swallow("PluginStore.cs:468", dg468_1); }
            return ok;
        }

        public static string Uninstall(Entry e)
        {
            try
            {
                string target = Target(e);
                if (!File.Exists(target)) return "未安装";
                File.Delete(target);
                try { string vf = VerFile(e); if (vf.Length > 0 && File.Exists(vf)) File.Delete(vf); } catch (Exception dg479_1) { Diag.Swallow("PluginStore.cs:479", dg479_1); }
                return "已卸载 " + e.Name;
            }
            catch (Exception ex) { return "卸载失败：" + ex.Message; }
        }
    }
}