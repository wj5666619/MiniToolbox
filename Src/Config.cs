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
    static class Config
    {
        public static void Save(string dir, TaskCfg c)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("action=" + c.Action);
                sb.AppendLine("mode=" + c.Mode);
                sb.AppendLine("hours=" + c.Hours);
                sb.AppendLine("mins=" + c.Mins);
                sb.AppendLine("secs=" + c.Secs);
                sb.AppendLine("at=" + c.AtTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("week=" + string.Join(",", c.Week));
                sb.AppendLine("needconfirm=" + c.NeedConfirm);
                sb.AppendLine("confirms=" + c.ConfirmSecs);
                sb.AppendLine("sound=" + c.Sound);
                sb.AppendLine("tray=" + c.Tray);
                sb.AppendLine("autorun=" + c.Autorun);
                sb.AppendLine("active=" + c.Active);
                sb.AppendLine("periodic=" + c.Periodic);
                sb.AppendLine("endtime=" + c.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("totalsec=" + c.TotalSec);
                sb.AppendLine("desc=" + c.Desc);
                File.WriteAllText(Path.Combine(dir, "config.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception dg49_1) { Diag.Swallow("Config.cs:49", dg49_1); }
        }
        public static TaskCfg Load(string dir)
        {
            var c = new TaskCfg();
            try
            {
                string p = Path.Combine(dir, "config.txt");
                if (!File.Exists(p)) return c;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "action": c.Action = v; break;
                        case "mode": c.Mode = v; break;
                        case "hours": int.TryParse(v, out c.Hours); break;
                        case "mins": int.TryParse(v, out c.Mins); break;
                        case "secs": int.TryParse(v, out c.Secs); break;
                        case "at": DateTime.TryParse(v, out c.AtTime); break;
                        case "week":
                            {
                                string[] ws = v.Split(',');
                                for (int j = 0; j < 7 && j < ws.Length; j++) { bool b; if (bool.TryParse(ws[j], out b)) c.Week[j] = b; }
                            }
                            break;
                        case "needconfirm": bool.TryParse(v, out c.NeedConfirm); break;
                        case "confirms": int.TryParse(v, out c.ConfirmSecs); break;
                        case "sound": bool.TryParse(v, out c.Sound); break;
                        case "tray": bool.TryParse(v, out c.Tray); break;
                        case "autorun": bool.TryParse(v, out c.Autorun); break;
                        case "active": bool.TryParse(v, out c.Active); break;
                        case "periodic": bool.TryParse(v, out c.Periodic); break;
                        case "endtime": DateTime.TryParse(v, out c.EndTime); break;
                        case "totalsec": int.TryParse(v, out c.TotalSec); break;
                        case "desc": c.Desc = v; break;
                    }
                }
            }
            catch (Exception dg91_1) { Diag.Swallow("Config.cs:91", dg91_1); }
            return c;
        }
    }
}