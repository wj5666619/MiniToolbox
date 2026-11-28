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
    static class TriggerCfg
    {
        public class Trig
        {
            public string Name = "";
            public string Kind = "time";     // time / interval / boot / folder / window
            public string Spec = "";
            public string Action = "chain";
            public bool On = true;
            public long LastTicks = 0;
        }

        public static List<Trig> Items = new List<Trig>();
        public static string FilePath = "";

        public static void Init(string dir)
        {
            FilePath = Path.Combine(dir, "触发器.txt");
            Load();
        }

        public static void Load()
        {
            Items = new List<Trig>();
            try
            {
                if (FilePath == null || FilePath.Length == 0 || !File.Exists(FilePath)) return;
                foreach (string ln in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string l2 = ln.TrimEnd();
                    if (l2.Length == 0 || l2.StartsWith("#")) continue;
                    string[] a = l2.Split('\t');
                    if (a.Length < 4) continue;
                    Trig t = new Trig();
                    t.On = a[0].Trim() == "1";
                    t.Kind = a[1].Trim();
                    t.Spec = a[2].Trim();
                    t.Action = a[3].Trim();
                    t.Name = a.Length > 4 && a[4].Trim().Length > 0 ? a[4].Trim() : (t.Kind + " " + t.Spec);
                    if (a.Length > 5) { try { t.LastTicks = long.Parse(a[5].Trim()); } catch (Exception dg62_1) { Diag.Swallow("TriggerCfg.cs:62", dg62_1); } }
                    Items.Add(t);
                }
            }
            catch (Exception dg66_1) { Diag.Swallow("TriggerCfg.cs:66", dg66_1); }
        }

        public static void Save()
        {
            try
            {
                if (FilePath == null || FilePath.Length == 0) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 每行：启用(1/0) TAB 类型 TAB 参数 TAB 动作 TAB 名称 TAB 上次触发ticks");
                for (int i = 0; i < Items.Count; i++)
                {
                    Trig t = Items[i];
                    sb.Append(t.On ? "1" : "0").Append('\t').Append(t.Kind).Append('\t')
                      .Append(t.Spec).Append('\t').Append(t.Action).Append('\t')
                      .Append(t.Name).Append('\t').Append(t.LastTicks.ToString()).AppendLine();
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg85_1) { Diag.Swallow("TriggerCfg.cs:85", dg85_1); }
        }
    }
}