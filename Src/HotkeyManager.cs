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
    class HotkeyManager
    {
        public class Item
        {
            public string Key;
            public string NameZh;
            public string NameEn;
            public int Mods;   // 0 = 未设置
            public int Vk;
        }

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr h, int id);

        const int NOREPEAT = 0x4000;
        public List<Item> Items = new List<Item>();
        public Action<string> OnFire;
        public string LastError = "";
        public int BindCount = 0;
        IntPtr hwnd;
        string cfg;
        Dictionary<int, string> map = new Dictionary<int, string>();

        public HotkeyManager(IntPtr h, string cfgPath)
        {
            hwnd = h; cfg = cfgPath;
            Items.Add(new Item { Key = "shot", NameZh = "全屏截图", NameEn = "Screenshot", Mods = 3, Vk = 83 });
            Items.Add(new Item { Key = "main", NameZh = "唤出主窗口", NameEn = "Show main window", Mods = 3, Vk = 72 });
            Items.Add(new Item { Key = "xfer", NameZh = "打开手机互传", NameEn = "Phone transfer", Mods = 3, Vk = 84 });
            Items.Add(new Item { Key = "plus", NameZh = "打开增强中心", NameEn = "Power tools", Mods = 3, Vk = 71 });
            Items.Add(new Item { Key = "checkup", NameZh = "一键体检", NameEn = "Checkup", Mods = 0, Vk = 0 });
            Items.Add(new Item { Key = "boost", NameZh = "一键游戏加速", NameEn = "Game boost", Mods = 0, Vk = 0 });
            Items.Add(new Item { Key = "desk", NameZh = "桌面整理", NameEn = "Desktop sort", Mods = 0, Vk = 0 });
            Items.Add(new Item { Key = "dupe", NameZh = "重复文件清理", NameEn = "Duplicate finder", Mods = 0, Vk = 0 });
            Items.Add(new Item { Key = "shutdown60", NameZh = "60 秒后关机", NameEn = "Shutdown in 60s", Mods = 0, Vk = 0 });
            Items.Add(new Item { Key = "cliphist", NameZh = "剪贴板历史", NameEn = "Clipboard history", Mods = 3, Vk = 86 });
            Items.Add(new Item { Key = "record", NameZh = "开始/停止录屏", NameEn = "Record toggle", Mods = 3, Vk = 82 });
            Items.Add(new Item { Key = "palette", NameZh = "快捷指令面板", NameEn = "Command palette", Mods = 3, Vk = 32 });
            Items.Add(new Item { Key = "gsearch", NameZh = "全局搜索", NameEn = "Global search", Mods = 2, Vk = 75 });
        }

        public Item Get(string key)
        {
            foreach (Item it in Items) if (it.Key == key) return it;
            return null;
        }

        public string ConflictOf(Item target)
        {
            foreach (Item it in Items)
            {
                if (it == target) continue;
                if (it.Mods == 0 || it.Vk == 0) continue;
                if (it.Mods == target.Mods && it.Vk == target.Vk) return it.NameZh;
            }
            return null;
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(cfg)) return;
                JavaScriptSerializer js = new JavaScriptSerializer();
                object root = js.DeserializeObject(File.ReadAllText(cfg, Encoding.UTF8));
                System.Collections.Generic.IDictionary<string, object> rd = root as System.Collections.Generic.IDictionary<string, object>;
                if (rd == null || !rd.ContainsKey("items")) return;
                System.Collections.IEnumerable arr = rd["items"] as System.Collections.IEnumerable;
                if (arr == null) return;
                foreach (object o in arr)
                {
                    System.Collections.Generic.IDictionary<string, object> d = o as System.Collections.Generic.IDictionary<string, object>;
                    if (d == null) continue;
                    string k = Convert.ToString(d["k"]);
                    Item it = Get(k);
                    if (it == null) continue;
                    it.Mods = Convert.ToInt32(d["m"]);
                    it.Vk = Convert.ToInt32(d["v"]);
                }
            }
            catch (Exception dg105_1) { Diag.Swallow("HotkeyManager.cs:105", dg105_1); }
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"items\":[");
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"k\":\"").Append(Items[i].Key).Append("\",\"m\":").Append(Items[i].Mods)
                  .Append(",\"v\":").Append(Items[i].Vk).Append("}");
            }
            sb.Append("]}");
            try { File.WriteAllText(cfg, sb.ToString(), Encoding.UTF8); } catch (Exception dg119_1) { Diag.Swallow("HotkeyManager.cs:119", dg119_1); }
        }

        public void UnregisterAll()
        {
            foreach (int id in map.Keys) { try { UnregisterHotKey(hwnd, id); } catch (Exception dg124_1) { Diag.Swallow("HotkeyManager.cs:124", dg124_1); } }
            map.Clear();
            BindCount = 0;
        }

        public void RegisterAll()
        {
            UnregisterAll();
            LastError = "";
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                if (it.Mods == 0 || it.Vk == 0) continue;
                int id = i + 1;
                bool ok = false;
                try { ok = RegisterHotKey(hwnd, id, it.Mods | NOREPEAT, it.Vk); } catch (Exception dg139_1) { Diag.Swallow("HotkeyManager.cs:139", dg139_1); }
                if (ok) { map[id] = it.Key; BindCount++; }
                else LastError += it.NameZh + " 注册失败（可能被其他程序占用）；";
            }
        }

        public void Fire(int id)
        {
            string key = null;
            if (!map.TryGetValue(id, out key)) return;
            if (OnFire != null) OnFire(key);
        }

        public static string VkName(int vk)
        {
            if (vk >= 65 && vk <= 90) return ((char)vk).ToString();
            if (vk >= 48 && vk <= 57) return ((char)vk).ToString();
            if (vk >= 112 && vk <= 123) return "F" + (vk - 111);
            switch (vk)
            {
                case 32: return "Space";
                case 13: return "Enter";
                case 9: return "Tab";
                case 27: return "Esc";
                case 187: return "=";
                case 189: return "-";
                case 188: return ",";
                case 190: return ".";
                case 191: return "/";
                case 192: return "`";
                case 219: return "[";
                case 220: return "\\";
                case 221: return "]";
                case 186: return ";";
                case 222: return "'";
                case 37: return "Left";
                case 38: return "Up";
                case 39: return "Right";
                case 40: return "Down";
                case 45: return "Insert";
                case 46: return "Delete";
                case 36: return "Home";
                case 35: return "End";
                case 33: return "PgUp";
                case 34: return "PgDn";
                case 44: return "PrtSc";
            }
            return "VK" + vk;
        }

        public static string Display(int mods, int vk)
        {
            if (mods == 0 || vk == 0) return "—";
            StringBuilder sb = new StringBuilder();
            if ((mods & 2) != 0) sb.Append("Ctrl+");
            if ((mods & 1) != 0) sb.Append("Alt+");
            if ((mods & 4) != 0) sb.Append("Shift+");
            if ((mods & 8) != 0) sb.Append("Win+");
            sb.Append(VkName(vk));
            return sb.ToString();
        }
    }
}