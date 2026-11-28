using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ShutdownTool
{
    /// <summary>
    /// v19.14：主程序里的轻量任务总线。清理扫描、系统清理、视频批量队列、更新下载
    /// 都把进度写到这里，控制中心只读展示；记录落到 任务记录.txt，重启后仍能看到上一轮。
    /// 不是线程池，不负责执行；执行仍由各功能自己完成，避免把现有流程推倒重写。
    /// </summary>
    internal static class TaskHub
    {
        internal class Item
        {
            public string Id;
            public string Kind;
            public string Title;
            public string Detail;
            public string State;       // 进行中 / 完成 / 失败 / 已取消 / 中断
            public int Percent;        // -1 表示不确定进度
            public DateTime Started;
            public DateTime Updated;
        }

        static readonly object gate = new object();
        static readonly List<Item> items = new List<Item>();
        static string filePath = "";
        static int seq = 0;

        public static void Init(string baseDir)
        {
            try
            {
                filePath = Path.Combine(baseDir, "任务记录.txt");
                Load();
            }
            catch (Exception ex) { Diag.Swallow("TaskHub.cs:Init", ex); }
        }

        public static string Start(string kind, string title, string detail)
        {
            string id = "t" + DateTime.Now.ToString("HHmmss") + "-" + (++seq).ToString();
            Item it = new Item();
            it.Id = id;
            it.Kind = kind ?? "";
            it.Title = title ?? "";
            it.Detail = detail ?? "";
            it.State = "进行中";
            it.Percent = -1;
            it.Started = DateTime.Now;
            it.Updated = it.Started;
            lock (gate)
            {
                items.Add(it);
                while (items.Count > 60) items.RemoveAt(0);
            }
            Save();
            return id;
        }

        public static void Progress(string id, int percent, string detail)
        {
            if (id == null) return;
            lock (gate)
            {
                Item it = Find(id);
                if (it == null) return;
                if (percent >= 0)
                {
                    if (percent > 100) percent = 100;
                    it.Percent = percent;
                }
                if (detail != null) it.Detail = detail;
                it.Updated = DateTime.Now;
            }
            Save();
        }

        public static void Done(string id, string detail)
        {
            Finish(id, "完成", 100, detail);
        }

        public static void Fail(string id, string detail)
        {
            Finish(id, "失败", -1, detail);
        }

        public static void Cancel(string id, string detail)
        {
            Finish(id, "已取消", -1, detail);
        }

        static void Finish(string id, string state, int percent, string detail)
        {
            if (id == null) return;
            lock (gate)
            {
                Item it = Find(id);
                if (it == null) return;
                it.State = state;
                it.Percent = percent;
                if (detail != null) it.Detail = detail;
                it.Updated = DateTime.Now;
            }
            Save();
        }

        static Item Find(string id)
        {
            for (int i = items.Count - 1; i >= 0; i--)
                if (items[i].Id == id) return items[i];
            return null;
        }

        public static List<Item> Snapshot()
        {
            List<Item> list = new List<Item>();
            lock (gate)
            {
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    Item src = items[i];
                    Item c = new Item();
                    c.Id = src.Id; c.Kind = src.Kind; c.Title = src.Title; c.Detail = src.Detail;
                    c.State = src.State; c.Percent = src.Percent; c.Started = src.Started; c.Updated = src.Updated;
                    list.Add(c);
                }
            }
            return list;
        }

        public static int RunningCount()
        {
            int n = 0;
            lock (gate) { for (int i = 0; i < items.Count; i++) if (items[i].State == "进行中") n++; }
            return n;
        }

        public static void ClearFinished()
        {
            lock (gate)
            {
                for (int i = items.Count - 1; i >= 0; i--)
                    if (items[i].State != "进行中") items.RemoveAt(i);
            }
            Save();
        }

        static void Save()
        {
            try
            {
                if (filePath == null || filePath.Length == 0) return;
                List<Item> copy;
                lock (gate) { copy = new List<Item>(items); }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < copy.Count; i++)
                {
                    Item it = copy[i];
                    sb.Append(Esc(it.Id)).Append('|')
                      .Append(Esc(it.Kind)).Append('|')
                      .Append(Esc(it.Title)).Append('|')
                      .Append(Esc(it.Detail)).Append('|')
                      .Append(Esc(it.State)).Append('|')
                      .Append(it.Percent).Append('|')
                      .Append(it.Started.ToString("yyyy-MM-dd HH:mm:ss")).Append('|')
                      .Append(it.Updated.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
                }
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Diag.Swallow("TaskHub.cs:Save", ex); }
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\r", " ").Replace("\n", " ");
        }

        static string Unesc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'p') sb.Append('|');
                    else if (n == '\\') sb.Append('\\');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static void Load()
        {
            lock (gate) { items.Clear(); }
            if (filePath == null || !File.Exists(filePath)) return;
            try
            {
                foreach (string ln in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    string[] a = ln.Split('|');
                    if (a.Length < 8) continue;
                    Item it = new Item();
                    it.Id = Unesc(a[0]);
                    it.Kind = Unesc(a[1]);
                    it.Title = Unesc(a[2]);
                    it.Detail = Unesc(a[3]);
                    it.State = Unesc(a[4]);
                    int.TryParse(a[5], out it.Percent);
                    try { it.Started = DateTime.Parse(a[6]); } catch { it.Started = DateTime.Now; }
                    try { it.Updated = DateTime.Parse(a[7]); } catch { it.Updated = DateTime.Now; }
                    // 上次退出时还在跑的任务，本轮不会有执行者了，标成中断而不是假装还在跑
                    if (it.State == "进行中") { it.State = "中断"; it.Detail = "上次退出时任务未结束"; }
                    lock (gate) { items.Add(it); }
                }
            }
            catch (Exception ex) { Diag.Swallow("TaskHub.cs:Load", ex); }
        }
    }
}
