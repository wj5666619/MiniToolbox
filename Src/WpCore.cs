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
    static class WpCore
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

        public static string Current()
        {
            try
            {
                StringBuilder sb = new StringBuilder(600);
                SystemParametersInfo(0x0073, (uint)sb.Capacity, sb, 0);
                return sb.ToString();
            }
            catch { return ""; }
        }

        public static bool Apply(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (k != null)
                    {
                        k.SetValue("WallpaperStyle", "10", Microsoft.Win32.RegistryValueKind.String);
                        k.SetValue("TileWallpaper", "0", Microsoft.Win32.RegistryValueKind.String);
                    }
                }
                return SystemParametersInfo(0x0014, 0, path, 0x01 | 0x02);
            }
            catch { return false; }
        }

        public static void BackupOnce(string dir)
        {
            try
            {
                string f = Path.Combine(dir, "原壁纸.txt");
                if (File.Exists(f)) return;
                string cur = Current();
                if (cur == null || cur.Length == 0) return;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(f, cur, new UTF8Encoding(false));
            }
            catch (Exception dg70_1) { Diag.Swallow("WpCore.cs:70", dg70_1); }
        }

        public static bool RestoreOriginal(string dir, out string err)
        {
            err = "";
            try
            {
                string f = Path.Combine(dir, "原壁纸.txt");
                if (!File.Exists(f)) { err = "没有记录原壁纸"; return false; }
                string p = File.ReadAllText(f, Encoding.UTF8).Trim();
                if (p.Length == 0 || !File.Exists(p)) { err = "原壁纸文件已不存在：" + p; return false; }
                if (!Apply(p)) { err = "系统拒绝恢复"; return false; }
                return true;
            }
            catch (Exception e) { err = e.Message; return false; }
        }

        // 取 Bing 今日一图（免密钥），返回本地 jpg 路径
        public static bool DownloadDaily(string dir, out string path, out string title, out string err)
        {
            path = ""; title = ""; err = "";
            try
            {
                try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)768 | (System.Net.SecurityProtocolType)3072; } catch (Exception dg94_1) { Diag.Swallow("WpCore.cs:94", dg94_1); }
                string today = DateTime.Now.ToString("yyyyMMdd");
                string jpg = Path.Combine(dir, "每日一图_" + today + ".jpg");
                if (File.Exists(jpg) && new FileInfo(jpg).Length > 20000) { path = jpg; title = "（今日已缓存）"; return true; }
                string json;
                using (System.Net.WebClient wc = new System.Net.WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MiniToolbox/" + "7.7");
                    wc.Encoding = Encoding.UTF8;
                    json = wc.DownloadString("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=zh-CN");
                }
                string baseUrl = "";
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json, "\"urlbase\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) baseUrl = "https://www.bing.com" + m.Groups[1].Value + "_UHD.jpg";
                if (baseUrl.Length == 0)
                {
                    m = System.Text.RegularExpressions.Regex.Match(json, "\"url\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) baseUrl = "https://www.bing.com" + m.Groups[1].Value.Replace("\\/", "/");
                }
                if (baseUrl.Length == 0) { err = "接口返回里没有图片地址"; return false; }
                System.Text.RegularExpressions.Match tm = System.Text.RegularExpressions.Regex.Match(json, "\"copyright\"\\s*:\\s*\"([^\"]+)\"");
                if (tm.Success) title = tm.Groups[1].Value.Replace("\\/", "/");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = jpg + ".part";
                using (System.Net.WebClient wc = new System.Net.WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MiniToolbox/" + "7.7");
                    wc.DownloadFile(baseUrl, tmp);
                }
                if (new FileInfo(tmp).Length < 20000) { try { File.Delete(tmp); } catch (Exception dg123_1) { Diag.Swallow("WpCore.cs:123", dg123_1); } err = "下载到的文件太小"; return false; }
                if (File.Exists(jpg)) { try { File.Delete(jpg); } catch (Exception dg124_1) { Diag.Swallow("WpCore.cs:124", dg124_1); } }
                File.Move(tmp, jpg);
                path = jpg;
                return true;
            }
            catch (Exception e) { err = e.Message; return false; }
        }
    }
}