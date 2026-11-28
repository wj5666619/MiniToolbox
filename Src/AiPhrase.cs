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
    static class AiPhrase
    {
        public class P
        {
            public string Text = "";
            public string Id = "";
            public int Sec = -1;
            public int Hits = 0;
            public long Last = 0;
        }

        public static string PhraseFile = "";

        public static void Init(string baseDir)
        {
            try
            {
                string d = Path.Combine(baseDir, "AI");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                PhraseFile = Path.Combine(d, "短语.txt");
            }
            catch (Exception dg44_1) { Diag.Swallow("AiPhrase.cs:44", dg44_1); }
        }

        public static List<P> Load()
        {
            List<P> l = new List<P>();
            try
            {
                if (PhraseFile.Length == 0 || !System.IO.File.Exists(PhraseFile)) return l;
                string[] ln = System.IO.File.ReadAllLines(PhraseFile, Encoding.UTF8);
                for (int i = 0; i < ln.Length; i++)
                {
                    string x = ln[i];
                    if (x == null || x.Trim().Length == 0 || x.StartsWith("#")) continue;
                    string[] p = x.Split('\t');
                    if (p.Length < 2) continue;
                    P e = new P();
                    e.Text = p[0].Trim();
                    e.Id = p[1].Trim();
                    if (p.Length > 2) { try { e.Sec = int.Parse(p[2].Trim()); } catch { e.Sec = -1; } }
                    if (p.Length > 3) { try { e.Hits = int.Parse(p[3].Trim()); } catch { e.Hits = 0; } }
                    if (p.Length > 4) { try { e.Last = long.Parse(p[4].Trim()); } catch { e.Last = 0; } }
                    if (e.Text.Length > 0) l.Add(e);
                }
            }
            catch (Exception dg69_1) { Diag.Swallow("AiPhrase.cs:69", dg69_1); }
            return l;
        }

        public static void Save(List<P> l)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 小工具箱 AI 短语记忆（说法 / 动作 / 秒 / 次数 / 最后时间，TAB 分隔，可直接编辑）");
                for (int i = 0; i < l.Count; i++)
                    sb.AppendLine(l[i].Text + "\t" + l[i].Id + "\t" + l[i].Sec + "\t" + l[i].Hits + "\t" + l[i].Last);
                System.IO.File.WriteAllText(PhraseFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg83_1) { Diag.Swallow("AiPhrase.cs:83", dg83_1); }
        }

        public static int Count() { try { return Load().Count; } catch { return 0; } }

        public static string Add(string raw, string id, int sec)
        {
            if (raw == null || raw.Trim().Length == 0) return "空说法";
            if (id == null || id.Length == 0) return "没有动作";
            string txt = raw.Trim();
            List<P> l = Load();
            for (int i = 0; i < l.Count; i++)
            {
                if (string.Equals(l[i].Text, txt, StringComparison.OrdinalIgnoreCase))
                {
                    l[i].Id = id; l[i].Sec = sec;
                    l[i].Hits = l[i].Hits + 1;
                    l[i].Last = DateTime.Now.Ticks;
                    Save(l);
                    return "已更新短语（第 " + l[i].Hits + " 次）：" + txt;
                }
            }
            P e2 = new P();
            e2.Text = txt; e2.Id = id; e2.Sec = sec; e2.Hits = 1; e2.Last = DateTime.Now.Ticks;
            l.Insert(0, e2);
            while (l.Count > 200) l.RemoveAt(l.Count - 1);
            Save(l);
            return "已记住：" + txt;
        }

        // 从一句话里解析出第一个动作再记
        public static string Remember(string raw)
        {
            List<AiIntent.Hit> hs = AiIntent.Parse(raw);
            if (hs == null || hs.Count == 0) return "";
            return Add(raw, hs[0].Id, hs[0].Sec);
        }

        public static List<P> Match(string prefix)
        {
            List<P> l = Load();
            List<P> r = new List<P>();
            string k = prefix == null ? "" : prefix.Trim().ToLowerInvariant();
            for (int i = 0; i < l.Count; i++)
            {
                if (k.Length == 0) { r.Add(l[i]); continue; }
                string t = l[i].Text.ToLowerInvariant();
                if (t.IndexOf(k, StringComparison.Ordinal) >= 0 && t != k) r.Add(l[i]);
            }
            r.Sort(delegate (P a, P b) { return b.Hits.CompareTo(a.Hits); });
            return r;
        }

        public static void Bump(string text)
        {
            List<P> l = Load();
            for (int i = 0; i < l.Count; i++)
                if (string.Equals(l[i].Text, text, StringComparison.OrdinalIgnoreCase))
                {
                    l[i].Hits = l[i].Hits + 1;
                    l[i].Last = DateTime.Now.Ticks;
                    Save(l);
                    return;
                }
        }

        public static bool Remove(string text)
        {
            List<P> l = Load();
            for (int i = 0; i < l.Count; i++)
                if (string.Equals(l[i].Text, text, StringComparison.OrdinalIgnoreCase))
                {
                    l.RemoveAt(i);
                    Save(l);
                    return true;
                }
            return false;
        }

        public static string Ago(long ticks)
        {
            if (ticks <= 0) return "-";
            try
            {
                TimeSpan ts = new TimeSpan(DateTime.Now.Ticks - ticks);
                if (ts.TotalMinutes < 1) return "刚刚";
                if (ts.TotalHours < 1) return ((int)ts.TotalMinutes) + " 分钟前";
                if (ts.TotalDays < 1) return ((int)ts.TotalHours) + " 小时前";
                return ((int)ts.TotalDays) + " 天前";
            }
            catch { return "-"; }
        }

        // ---------- v12.6 宏口令：一句话拆成多步动作 ----------
        public const string MACRO = "m|";

        static readonly string[] SPLIT_WORDS = new string[] { "然后", "接着", "之后", "再", "、", "，", "," };

        public static bool IsMacro(string id)
        {
            return id != null && id.StartsWith(MACRO, StringComparison.Ordinal);
        }

        // 把「关机然后锁屏再静音」这类句子拆成 m|shutdown|1800|lock|-1|mute|-1
        public static string BuildMacro(string raw)
        {
            if (raw == null) return "";
            string q = raw.Trim();
            if (q.Length == 0) return "";
            string[] parts = q.Split(SPLIT_WORDS, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            StringBuilder sb = new StringBuilder(MACRO);
            int steps = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string seg = parts[i].Trim();
                if (seg.Length == 0) continue;
                List<AiIntent.Hit> hs = AiIntent.Parse(seg);
                if (hs == null || hs.Count == 0) continue;
                if (steps > 0) sb.Append("|");
                sb.Append(hs[0].Id).Append("|").Append(hs[0].Sec);
                steps++;
            }
            if (steps < 2) return "";
            return sb.ToString();
        }

        public static int MacroSteps(string id)
        {
            if (!IsMacro(id)) return 0;
            string[] a = id.Substring(MACRO.Length).Split('|');
            return a.Length / 2;
        }

        // 返回 [id, sec] 交错的分段：{"shutdown","1800","lock","-1"}
        public static string[] MacroParts(string id)
        {
            if (!IsMacro(id)) return new string[0];
            return id.Substring(MACRO.Length).Split('|');
        }

        public static string MacroLabel(string id)
        {
            int n = MacroSteps(id);
            if (n == 0) return id;
            StringBuilder sb = new StringBuilder();
            sb.Append("宏 ").Append(n).Append(" 步：");
            string[] a = MacroParts(id);
            for (int i = 0; i + 1 < a.Length; i += 2)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(AiIntent.ZhOf(a[i]));
                int sec = -1;
                try { sec = int.Parse(a[i + 1]); } catch (Exception dg236_1) { Diag.Swallow("AiPhrase.cs:236", dg236_1); }
                if (sec > 0) sb.Append("(").Append(AiIntent.Human(sec)).Append(")");
            }
            return sb.ToString();
        }

    }
}