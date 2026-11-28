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
    static class SteamWishCore
    {
        public class Item
        {
            public int AppId;
            public string Name = "";
            public int Priority;
            public string Now = "-";
            public double NowV = -1;
            public string First = "";
            public double FirstV = -1;
            public int Discount;
            public string Seen = "";
        }

        public static string CacheFile(string dir) { return Path.Combine(dir, "steam_wishlist_price.txt"); }

        // 从 Steam 的 loginusers.vdf 里认出这台机器上的 SteamID64（不需要密码，也不需要登录态）
        public static string SteamId64()
        {
            try
            {
                string root = SteamProCore.Root();
                if (root.Length == 0) return "";
                string lu = Path.Combine(root, "config\\loginusers.vdf");
                if (!File.Exists(lu)) return "";
                string txt = File.ReadAllText(lu, Encoding.UTF8);
                System.Text.RegularExpressions.MatchCollection ms =
                    System.Text.RegularExpressions.Regex.Matches(txt, "\"(7656\\d{13})\"\\s*\\{([^}]*)\\}");
                string first = "";
                foreach (System.Text.RegularExpressions.Match m in ms)
                {
                    if (first.Length == 0) first = m.Groups[1].Value;
                    if (System.Text.RegularExpressions.Regex.IsMatch(m.Groups[2].Value, "\"MostRecent\"\\s+\"1\""))
                        return m.Groups[1].Value;
                }
                return first;
            }
            catch { return ""; }
        }

        static string Fetch(string url)
        {
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)768 | (SecurityProtocolType)3072; }
                catch (Exception dg69_1) { Diag.Swallow("SteamWishCore.cs:69", dg69_1); }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                req.Headers.Add("Accept-Encoding", "gzip, deflate");
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        // 愿望单：公开愿望单可直接读；私密愿望单会返回空，由界面如实提示
        public static List<Item> Load(string sid, out string err)
        {
            err = "";
            List<Item> res = new List<Item>();
            if (sid == null || sid.Trim().Length == 0) { err = "没拿到 SteamID"; return res; }
            sid = sid.Trim();
            string kind = sid.StartsWith("7656") ? "profiles" : "id";
            for (int page = 0; page < 30; page++)
            {
                string json = Fetch("https://store.steampowered.com/wishlist/" + kind + "/" + sid + "/wishlistdata/?p=" + page);
                if (json.Length == 0) { if (page == 0) err = "抓取失败：网络或愿望单不可见"; break; }
                if (json.IndexOf("\"success\"") >= 0 && json.IndexOf("\"name\"") < 0) { err = "愿望单不可见（需要在 Steam 隐私设置里把「愿望单」设为公开）"; break; }
                int before = res.Count;
                System.Text.RegularExpressions.MatchCollection ms =
                    System.Text.RegularExpressions.Regex.Matches(json, "\"(\\d{2,8})\"\\s*:\\s*\\{([^{}]*)\\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                foreach (System.Text.RegularExpressions.Match m in ms)
                {
                    Item it = new Item();
                    if (!int.TryParse(m.Groups[1].Value, out it.AppId)) continue;
                    string body = m.Groups[2].Value;
                    System.Text.RegularExpressions.Match mn =
                        System.Text.RegularExpressions.Regex.Match(body, "\"name\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                    it.Name = mn.Success ? mn.Groups[1].Value : ("App " + it.AppId);
                    System.Text.RegularExpressions.Match mp =
                        System.Text.RegularExpressions.Regex.Match(body, "\"priority\"\\s*:\\s*(\\d+)");
                    if (mp.Success) int.TryParse(mp.Groups[1].Value, out it.Priority);
                    bool dup = false;
                    for (int k = 0; k < res.Count; k++) if (res[k].AppId == it.AppId) { dup = true; break; }
                    if (!dup) res.Add(it);
                }
                if (res.Count == before) break;
            }
            if (res.Count == 0 && err.Length == 0) err = "愿望单是空的，或者没读到（先确认 Steam 隐私设置里愿望单是公开）";
            // 按愿望单优先级排序
            res.Sort(delegate (Item a, Item b)
            {
                if (a.Priority == 0 && b.Priority > 0) return 1;
                if (b.Priority == 0 && a.Priority > 0) return -1;
                return a.Priority - b.Priority;
            });
            return res;
        }

        // 价格：appdetails 支持一次问多个 appid（带 filters=price_overview 后只回价格段）
        public static int Prices(List<Item> items, int batch, Action<int, int> progress, Func<bool> cancel, out string err)
        {
            err = "";
            int okCount = 0;
            for (int i = 0; i < items.Count; i += batch)
            {
                if (cancel != null && cancel()) { err = "已停止"; break; }
                if (progress != null) progress(i, items.Count);
                StringBuilder ids = new StringBuilder();
                int n = 0;
                for (int k = i; k < items.Count && k < i + batch; k++)
                {
                    if (ids.Length > 0) ids.Append(",");
                    ids.Append(items[k].AppId);
                    n++;
                }
                string json = Fetch("https://store.steampowered.com/api/appdetails?appids=" + ids + "&cc=cn&l=schinese&filters=price_overview");
                if (json.Length == 0) { err = "价格接口没有返回（可能被限流，稍后再试）"; break; }
                for (int k = i; k < items.Count && k < i + batch; k++)
                {
                    Item it = items[k];
                    int at = json.IndexOf("\"" + it.AppId + "\":");
                    int at2 = -1;
                    if (at >= 0)
                    {
                        for (int j = at + 1; j < json.Length - 2; j++)
                        {
                            if (json[j] == '"' && json[j + 1] >= '1' && json[j + 1] <= '9')
                            {
                                int q = j + 1;
                                int cnt = 0;
                                while (q < json.Length && json[q] >= '0' && json[q] <= '9') { q++; cnt++; }
                                if (cnt >= 2 && q < json.Length && json[q] == '"' && q + 1 < json.Length && json[q + 1] == ':') { at2 = j; break; }
                            }
                        }
                    }
                    if (at < 0) continue;
                    string seg = at2 > at ? json.Substring(at, at2 - at) : json.Substring(at);
                    if (seg.IndexOf("\"success\":true") < 0 && seg.IndexOf("\"success\": true") < 0) continue;
                    System.Text.RegularExpressions.Match mf =
                        System.Text.RegularExpressions.Regex.Match(seg, "\"final\"\\s*:\\s*(\\d+)");
                    System.Text.RegularExpressions.Match mi =
                        System.Text.RegularExpressions.Regex.Match(seg, "\"initial\"\\s*:\\s*(\\d+)");
                    System.Text.RegularExpressions.Match md =
                        System.Text.RegularExpressions.Regex.Match(seg, "\"discount_percent\"\\s*:\\s*(\\d+)");
                    System.Text.RegularExpressions.Match mc =
                        System.Text.RegularExpressions.Regex.Match(seg, "\"currency\"\\s*:\\s*\"([A-Z]+)\"");
                    if (!mf.Success)
                    {
                        if (seg.IndexOf("is_free") >= 0 && seg.IndexOf("\"is_free\":true") >= 0)
                        {
                            it.Now = "免费"; it.NowV = 0; it.Discount = 100; it.First = it.First.Length > 0 ? it.First : "免费";
                            if (it.FirstV < 0) it.FirstV = 0;
                            okCount++;
                        }
                        else { it.Now = "未上架/无价"; }
                        continue;
                    }
                    double fin = 0, ini = 0;
                    try { fin = double.Parse(mf.Groups[1].Value) / 100.0; } catch (Exception dg188_1) { Diag.Swallow("SteamWishCore.cs:188", dg188_1); }
                    try { if (mi.Success) ini = double.Parse(mi.Groups[1].Value) / 100.0; } catch (Exception dg189_1) { Diag.Swallow("SteamWishCore.cs:189", dg189_1); }
                    string cur = mc.Success ? mc.Groups[1].Value : "CNY";
                    string sym = cur == "CNY" ? "¥" : cur + " ";
                    it.NowV = fin;
                    it.Now = (md.Success ? "[" + md.Groups[1].Value + "%] " : "") + sym + fin.ToString("0.00");
                    if (it.First.Length == 0) { it.First = sym + (ini > 0 ? ini : fin).ToString("0.00"); it.FirstV = ini > 0 ? ini : fin; }
                    if (md.Success) int.TryParse(md.Groups[1].Value, out it.Discount);
                    okCount++;
                }
                System.Threading.Thread.Sleep(400);
            }
            if (progress != null) progress(items.Count, items.Count);
            return okCount;
        }

        // 把上次存的「首见价」读回来当基准，这样隔天再开也能算出降了多少
        public static void ApplyCache(List<Item> items, string dir)
        {
            try
            {
                string p = CacheFile(dir);
                if (!File.Exists(p)) return;
                Dictionary<int, string[]> d = new Dictionary<int, string[]>();
                foreach (string ln in File.ReadAllLines(p, Encoding.UTF8))
                {
                    string[] a = ln.Split('|');
                    if (a.Length < 4) continue;
                    int id = 0;
                    if (!int.TryParse(a[0], out id)) continue;
                    d[id] = a;
                }
                foreach (Item it in items)
                {
                    string[] a;
                    if (!d.TryGetValue(it.AppId, out a)) continue;
                    double first = -1;
                    if (!double.TryParse(a[3], out first)) first = -1;
                    if (first >= 0 && it.First.Length == 0)
                    {
                        it.FirstV = first;
                        it.First = "¥" + first.ToString("0.00");
                    }
                    if (a.Length > 4) it.Seen = a[4];
                }
            }
            catch (Exception dg234_1) { Diag.Swallow("SteamWishCore.cs:234", dg234_1); }
        }

        public static void SaveCache(List<Item> items, string dir)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Item it in items)
                    if (it.NowV >= 0)
                        sb.AppendLine(it.AppId + "|" + it.Name.Replace("|", " ") + "|" + it.NowV.ToString("0.00") + "|" + it.FirstV.ToString("0.00") + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                File.WriteAllText(CacheFile(dir), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception dg247_1) { Diag.Swallow("SteamWishCore.cs:247", dg247_1); }
        }

        public static string ExportCsv(List<Item> items, string dir, bool onlyDrop)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("appid,游戏,首见价,现价,便宜了,折扣%");
                int cnt = 0;
                foreach (Item it in items)
                {
                    if (it.NowV < 0) continue;
                    double drop = (it.FirstV >= 0 && it.NowV >= 0) ? it.FirstV - it.NowV : 0;
                    if (onlyDrop && drop <= 0) continue;
                    sb.AppendLine(it.AppId + ",\"" + it.Name.Replace("\"", "\"\"") + "\","
                        + (it.FirstV >= 0 ? it.FirstV.ToString("0.00") : "-") + ","
                        + it.NowV.ToString("0.00") + "," + drop.ToString("0.00") + "," + it.Discount);
                    cnt++;
                }
                string p = Path.Combine(dir, (onlyDrop ? "愿望单降价_" : "愿望单价格_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(true));
                return p + "（" + cnt + " 款）";
            }
            catch (Exception e) { return "导出失败：" + e.Message; }
        }
    }
}