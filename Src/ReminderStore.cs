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
    static class ReminderStore
    {
        public static List<ReminderItem> Items = new List<ReminderItem>();
        static string file = "";

        public static void Load(string dir)
        {
            file = Path.Combine(dir, "reminders.json");
            Items = new List<ReminderItem>();
            try
            {
                if (!File.Exists(file)) return;
                string txt = File.ReadAllText(file);
                System.Web.Script.Serialization.JavaScriptSerializer js =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                List<ReminderItem> list = js.Deserialize<List<ReminderItem>>(txt);
                if (list != null) Items = list;
            }
            catch (Exception dg41_1) { Diag.Swallow("ReminderStore.cs:41", dg41_1); }
        }

        public static void Save(string dir)
        {
            try
            {
                if (file.Length == 0) file = Path.Combine(dir, "reminders.json");
                System.Web.Script.Serialization.JavaScriptSerializer js =
                    new System.Web.Script.Serialization.JavaScriptSerializer();
                File.WriteAllText(file, js.Serialize(Items));
            }
            catch (Exception dg53_1) { Diag.Swallow("ReminderStore.cs:53", dg53_1); }
        }

        public static int NextId()
        {
            int m = 0;
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id > m) m = Items[i].Id;
            return m + 1;
        }

        // 取出所有到点的提醒，并把重复规则推进到下一次
        public static List<ReminderItem> TakeDue()
        {
            List<ReminderItem> due = new List<ReminderItem>();
            DateTime now = DateTime.Now;
            for (int i = 0; i < Items.Count; i++)
            {
                ReminderItem it = Items[i];
                if (!it.On) continue;
                if (it.FireAt > now) continue;
                due.Add(it);
                DateTime next = it.FireAt;
                if (it.Repeat == 1) next = next.AddDays(1);
                else if (it.Repeat == 2) next = next.AddDays(7);
                else if (it.Repeat == 3) next = now.AddMinutes(Math.Max(1, it.IntervalMin));
                else it.On = false;
                while (it.Repeat != 0 && next <= now) next = (it.Repeat == 1) ? next.AddDays(1) : next.AddDays(7);
                if (it.Repeat == 3) next = now.AddMinutes(Math.Max(1, it.IntervalMin));
                it.FireAt = next;
            }
            return due;
        }
    }
}