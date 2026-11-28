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
    static class AiIntent
    {
        public class Hit
        {
            public string Id;
            public int Sec = -1;
            public int Score;
            public string Zh;
            public string En;
            public string Hay;
            public string Open;
        }

        // v12.6：动作 id 的中文短名（宏口令展示用）
        public static string ZhOf(string id)
        {
            switch (id)
            {
                case "shutdown": return "关机";
                case "restart": return "重启";
                case "hibernate": return "休眠";
                case "sleep": return "睡眠";
                case "lock": return "锁屏";
                case "cancel": return "取消关机";
                case "shot": return "截图";
                case "clean": return "清理";
                case "mute": return "静音";
                case "volup": return "音量+";
                case "voldn": return "音量-";
                case "open": return "打开功能";
            }
            return id;
        }

        const string CNUM = "零一二三四五六七八九十两";
        const string DIG = "一二三四五六七八九";

        // ---------- 归一化 ----------
        public static string Norm(string s)
        {
            if (s == null) return "";
            s = s.Replace("，", " ").Replace("。", " ").Replace("！", " ").Replace("？", " ")
                 .Replace(",", " ").Replace("、", " ").Replace("~", " ");
            s = s.Trim().ToLowerInvariant();
            string[] junk = { "麻烦你", "麻烦", "请你", "请帮我", "帮我", "帮忙", "我想要", "我想", "我要",
                              "可不可以", "能不能", "可以", "一下", "立即", "马上", "立刻", "现在", "然后就", "然后" };
            for (int i = 0; i < junk.Length; i++) s = s.Replace(junk[i], "");
            return s.Trim();
        }

        static bool Has(string t, params string[] ws)
        {
            for (int i = 0; i < ws.Length; i++)
                if (ws[i] != null && ws[i].Length > 0 && t.IndexOf(ws[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ---------- 中文数字 ----------
        static int CnNum(string s)
        {
            int total = 0, cur = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '十') { if (cur <= 0) cur = 1; total += cur * 10; cur = 0; }
                else if (c == '两') cur = cur * 10 + 2;
                else if (c == '零') { }
                else
                {
                    int dg = DIG.IndexOf(c);
                    if (dg < 0) break;
                    cur = cur * 10 + (dg + 1);
                }
            }
            return total + cur;
        }

        // 从 endExcl 往前取一个数字（阿拉伯或中文），可带「半」
        static bool NumBefore(string t, int endExcl, out int val, out bool half)
        {
            val = -1; half = false;
            int e = endExcl;
            while (e > 0 && t[e - 1] == ' ') e--;
            if (e > 0 && t[e - 1] == '半') { half = true; e--; }
            int st = e;
            while (st > 0 && ((t[st - 1] >= '0' && t[st - 1] <= '9') || CNUM.IndexOf(t[st - 1]) >= 0)) st--;
            if (st == e) return false;
            string num = t.Substring(st, e - st);
            if (num.Length == 0) return false;
            if (num[0] >= '0' && num[0] <= '9')
            {
                try { val = int.Parse(num); } catch { val = -1; }
                return val >= 0;
            }
            val = CnNum(num);
            return val > 0;
        }

        public static string Human(int sec)
        {
            if (sec < 0) return "-";
            if (sec < 60) return sec + " 秒";
            int m = sec / 60, rs = sec % 60;
            if (m < 60) return rs == 0 ? m + " 分钟" : m + " 分 " + rs + " 秒";
            int h = m / 60, mm = m % 60;
            return mm == 0 ? h + " 小时" : h + " 小时 " + mm + " 分";
        }

        // ---------- 时间解析 ----------
        // 支持：半小时 / 一个半小时 / 一刻钟 / 25分钟 / 2小时 / 90秒 / 18:30 / 8点 / 八点半 / 晚上8点20
        public static bool When(string t, out int sec, out string human)
        {
            sec = -1; human = "";
            if (Has(t, "一刻钟")) { sec = 900; human = "15 分钟"; return true; }
            if (Has(t, "一个半小时", "一小时半", "1.5小时", "1.5 小时")) { sec = 5400; human = "1 小时 30 分"; return true; }
            if (Has(t, "半小时", "半个小时", "半钟头")) { sec = 1800; human = "30 分钟"; return true; }
            // N 刻钟：三刻钟 = 45 分
            int ke = t.IndexOf("刻钟", StringComparison.Ordinal);
            if (ke > 0)
            {
                int kv; bool kh;
                if (NumBefore(t, ke, out kv, out kh)) { sec = kv * 900 + (kh ? 450 : 0); human = Human(sec); return true; }
            }

            // 绝对时刻
            int colon = t.IndexOf(':');
            if (colon < 0) colon = t.IndexOf('：');
            int dian = t.IndexOf('点');
            int mark = -1; char kind = (char)0;
            if (colon > 0) { mark = colon; kind = 'c'; }
            else if (dian > 0) { mark = dian; kind = 'd'; }
            if (mark > 0)
            {
                int hv; bool hhalf;
                if (NumBefore(t, mark, out hv, out hhalf) && hv >= 0 && hv <= 24)
                {
                    int mv = 0; bool mhalf = false;
                    int mv2;
                    bool ok2 = NumBefore(t.Length == mark + 1 ? t : t, Math.Min(t.Length, mark + 3), out mv2, out mhalf);
                    if (kind == 'c' || ok2)
                    {
                        if (ok2 && mv2 >= 0 && mv2 < 60) mv = mv2;
                        if (mhalf) mv += 30;
                    }
                    bool pm = Has(t, "下午", "晚上", "傍晚", "夜里", "夜");
                    bool noon = Has(t, "中午", "正午");
                    if (noon && hv < 12) hv = 12;
                    else if (pm && hv < 12) hv += 12;
                    if (hv > 23) hv = hv % 24;
                    if (mv > 59) mv = 59;
                    DateTime now = DateTime.Now;
                    DateTime target = new DateTime(now.Year, now.Month, now.Day, hv, mv, 0);
                    if (target <= now.AddSeconds(20)) target = target.AddDays(1);
                    sec = (int)(target - now).TotalSeconds;
                    human = target.ToString("HH:mm");
                    return true;
                }
            }

            // 相对时长
            string[] units = { "小时", "钟头", "分钟", "分", "秒" };
            int[] mult = { 3600, 3600, 60, 60, 1 };
            for (int u = 0; u < units.Length; u++)
            {
                int p = t.IndexOf(units[u], StringComparison.Ordinal);
                if (p <= 0) continue;
                int v; bool half;
                if (!NumBefore(t, p, out v, out half)) continue;
                int total = v * mult[u] + (half ? mult[u] / 2 : 0);
                // 复合时长：X 小时 Y 分 / X 分 Y 秒
                int tailAt = p + units[u].Length;
                if (mult[u] == 3600)
                {
                    int p2 = t.IndexOf("分", tailAt, StringComparison.Ordinal);
                    if (p2 > tailAt)
                    {
                        int v2; bool h2;
                        if (NumBefore(t, p2, out v2, out h2)) total += v2 * 60 + (h2 ? 30 : 0);
                    }
                }
                else if (mult[u] == 60)
                {
                    int p2 = t.IndexOf("秒", tailAt, StringComparison.Ordinal);
                    if (p2 > tailAt)
                    {
                        int v2; bool h2;
                        if (NumBefore(t, p2, out v2, out h2)) total += v2;
                    }
                }
                if (total <= 0) continue;
                sec = total; human = Human(total);
                return true;
            }
            return false;
        }

        static string OpenKeyword(string t)
        {
            string[] verbs = { "打开", "进入", "跳到", "切到", "切换到", "调用", "启动", "去看看", "看看", "在哪里", "在哪" };
            for (int i = 0; i < verbs.Length; i++)
            {
                int p = t.IndexOf(verbs[i], StringComparison.Ordinal);
                if (p < 0) continue;
                string kw = t.Substring(p + verbs[i].Length).Trim();
                kw = kw.Replace("页面", "").Replace("页", "").Replace("功能", "").Replace("这个", "").Replace("那个", "").Replace("吧", "").Trim();
                if (kw.Length >= 2) return kw;
            }
            return "";
        }

        static Hit Mk(string id, int sec, int score, string zh, string en, string hay)
        {
            Hit h = new Hit();
            h.Id = id; h.Sec = sec; h.Score = score; h.Zh = zh; h.En = en; h.Hay = hay;
            return h;
        }

        public static List<Hit> Parse(string raw)
        {
            List<Hit> list = new List<Hit>();
            string t = Norm(raw);
            if (t.Length == 0) return list;

            int sec = -1; string when = "";
            bool hasWhen = When(t, out sec, out when);
            string tail = hasWhen ? (when + "后") : "";

            bool cancel = Has(t, "取消关机", "撤销关机", "别关机", "不要关机", "取消定时关机", "cancel shutdown", "shutdown -a");
            bool restart = Has(t, "重启", "重新启动", "重新开机", "restart", "reboot");
            bool hibernate = Has(t, "休眠", "hibernate");
            bool sleep = !hibernate && Has(t, "睡眠", "待机", "sleep");
            bool lockScr = Has(t, "锁屏", "锁定屏幕", "锁电脑", "lock");
            bool shut = !cancel && !restart && !hibernate && !sleep && Has(t, "关机", "关电脑", "关闭电脑", "关掉电脑", "关机器", "shutdown", "turn off");
            bool shot = Has(t, "截图", "截屏", "抓屏", "screenshot");
            bool clean = Has(t, "清理", "垃圾", "清扫", "清一清", "腾空间");
            bool mute = Has(t, "静音", "mute");
            bool volUp = Has(t, "音量加", "调大音量", "声音大点", "大声点", "volume up");
            bool volDn = Has(t, "调小音量", "声音小点", "小声点", "volume down");
            // 只是搜「定时关机」这个页面名时不要误触发关机
            if (t.IndexOf("定时关机", StringComparison.Ordinal) >= 0 && t.Length <= 6) shut = false;

            if (cancel) list.Add(Mk("cancel", -1, 94, "取消关机", "Cancel shutdown", "quxiao guanji cancel"));
            if (shut)
                list.Add(Mk("shutdown", hasWhen ? sec : 60, hasWhen ? 86 : 76,
                    (hasWhen ? tail : "60 秒后") + "关机", (hasWhen ? "Shutdown after " + when : "Shutdown in 60s"), "guanji guanji shutdown"));
            if (restart)
                list.Add(Mk("restart", hasWhen ? sec : 3, hasWhen ? 84 : 74,
                    (hasWhen ? tail : "马上") + "重启", (hasWhen ? "Restart after " + when : "Restart now"), "chongqi restart reboot"));
            if (hibernate) list.Add(Mk("hibernate", -1, 82, "休眠", "Hibernate", "xiuzheng hibernate"));
            if (sleep) list.Add(Mk("sleep", -1, 80, "进入睡眠", "Sleep", "shuimian sleep"));
            if (lockScr) list.Add(Mk("lock", -1, 78, "锁屏", "Lock screen", "suoping lock"));
            if (shot)
                list.Add(Mk("shot", hasWhen ? sec : 3, hasWhen ? 80 : 70,
                    (hasWhen ? tail : "3 秒后") + "截图", (hasWhen ? "Screenshot after " + when : "Screenshot in 3s"), "jietu screenshot"));
            if (clean) list.Add(Mk("clean", -1, 72, "清理 C 盘（切到清理页）", "Clean C: (open page)", "qingli cpan laji clean"));
            if (mute) list.Add(Mk("mute", -1, 68, "静音", "Mute", "jingyin mute"));
            if (volUp) list.Add(Mk("volup", -1, 66, "音量 +", "Volume up", "yinliang volume"));
            if (volDn) list.Add(Mk("voldn", -1, 66, "音量 −", "Volume down", "yinliang volume"));

            string openKw = OpenKeyword(t);
            if (openKw.Length > 0)
            {
                Hit oh = Mk("open", -1, 52, "", "", openKw);
                oh.Open = openKw;
                list.Add(oh);
            }

            list.Sort(delegate(Hit a, Hit b) { return b.Score.CompareTo(a.Score); });
            return list;
        }

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        public static void Volume(string what)
        {
            byte vk = 0xAD;
            if (what == "up") vk = 0xAF;
            else if (what == "down") vk = 0xAE;
            try
            {
                for (int i = 0; i < (what == "mute" ? 1 : 4); i++)
                {
                    keybd_event(vk, 0, 0, 0);
                    keybd_event(vk, 0, 2, 0);
                    System.Threading.Thread.Sleep(60);
                }
            }
            catch (Exception dg311_1) { Diag.Swallow("AiIntent.cs:311", dg311_1); }
        }
    }
}