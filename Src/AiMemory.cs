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
    static class AiMemory
    {
        public static string Export(string baseDir)
        {
            try
            {
                List<string> ph = new List<string>();
                List<AiPhrase.P> l = AiPhrase.Load();
                for (int i = 0; i < l.Count; i++)
                {
                    StringBuilder one = new StringBuilder();
                    one.Append("{\"text\":").Append(J(l[i].Text))
                       .Append(",\"id\":").Append(J(l[i].Id))
                       .Append(",\"sec\":").Append(l[i].Sec)
                       .Append(",\"hits\":").Append(l[i].Hits)
                       .Append(",\"last\":").Append(l[i].Last).Append("}");
                    ph.Add(one.ToString());
                }
                string repo = "";
                try { if (File.Exists(PluginStore.IndexFile)) repo = File.ReadAllText(PluginStore.IndexFile, Encoding.UTF8); } catch (Exception dg42_1) { Diag.Swallow("AiMemory.cs:42", dg42_1); }
                List<string> srcs = new List<string>();
                string[] ss = PluginStore.Sources();
                for (int i = 0; i < ss.Length; i++) srcs.Add(J(ss[i]));
                StringBuilder sb = new StringBuilder();
                sb.Append("{\r\n");
                sb.Append("  \"tool\": \"小工具箱\",\r\n");
                sb.Append("  \"ver\": \"").Append(UiKit.VER).Append("\",\r\n");
                sb.Append("  \"time\": ").Append(DateTime.Now.Ticks).Append(",\r\n");
                sb.Append("  \"phrases\": [").Append(string.Join(",", ph.ToArray())).Append("],\r\n");
                sb.Append("  \"sources\": [").Append(string.Join(",", srcs.ToArray())).Append("],\r\n");
                sb.Append("  \"repo\": ").Append(repo.Trim().Length == 0 ? "\"\"" : J(repo)).Append("\r\n");
                sb.Append("}\r\n");
                string dir = Path.Combine(baseDir, "AI");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "记忆_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json");
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                return file;
            }
            catch (Exception ex) { return "导出失败：" + ex.Message; }
        }

        static string J(string x)
        {
            if (x == null) x = "";
            StringBuilder sb = new StringBuilder("\"");
            for (int i = 0; i < x.Length; i++)
            {
                char c = x[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            sb.Append("\"");
            return sb.ToString();
        }

        // 只做合并：同说法覆盖、新说法追加；仓库源也并进去
        public static string Import(string file)
        {
            try
            {
                if (file == null || !File.Exists(file)) return "找不到这个文件";
                string all = File.ReadAllText(file, Encoding.UTF8);
                List<AiPhrase.P> cur = AiPhrase.Load();
                int addN = 0, updN = 0;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(all, "\\{\"text\":\"(.*?)\",\"id\":\"(.*?)\",\"sec\":(-?\\d+),\"hits\":(\\d+),\"last\":(\\d+)\\}"))
                {
                    string txt = Un( m.Groups[1].Value );
                    string id = Un( m.Groups[2].Value );
                    if (txt.Length == 0 || id.Length == 0) continue;
                    bool hit = false;
                    for (int i = 0; i < cur.Count; i++)
                        if (string.Equals(cur[i].Text, txt, StringComparison.OrdinalIgnoreCase))
                        {
                            cur[i].Id = id;
                            cur[i].Sec = int.Parse(m.Groups[3].Value);
                            hit = true; updN++;
                            break;
                        }
                    if (!hit)
                    {
                        AiPhrase.P p = new AiPhrase.P();
                        p.Text = txt; p.Id = id;
                        p.Sec = int.Parse(m.Groups[3].Value);
                        p.Hits = int.Parse(m.Groups[4].Value);
                        p.Last = long.Parse(m.Groups[5].Value);
                        cur.Insert(0, p); addN++;
                    }
                }
                while (cur.Count > 200) cur.RemoveAt(cur.Count - 1);
                AiPhrase.Save(cur);
                int srcN = 0;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(all, "\"https?.*?\""))
                {
                    string u = Un(m.Value.Trim('"'));
                    if (u.Length > 0) { PluginStore.AddSource(u); srcN++; }
                }
                return "导入完成：新增 " + addN + " 条、覆盖 " + updN + " 条、仓库源 " + srcN + " 个";
            }
            catch (Exception ex) { return "导入失败：" + ex.Message; }
        }

        static string Un(string x)
        {
            if (x == null) return "";
            return x.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
        }
    }
}