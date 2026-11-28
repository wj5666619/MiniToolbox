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
    static class PrivacyCore
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public static string Env(string key)
        {
            try { return Environment.GetEnvironmentVariable(key); } catch { return ""; }
        }

        public static List<PrivItem> Scan()
        {
            List<PrivItem> items = new List<PrivItem>();
            string local = Env("LOCALAPPDATA");
            string appdata = Env("APPDATA");
            string temp = Env("TEMP");

            AddDir(items, "Chrome 缓存", "Chrome cache", Path.Combine(Path.Combine(Path.Combine(Path.Combine(local, "Google"), "Chrome"), Path.Combine("User Data", "Default")), "Cache"));
            AddFile(items, "Chrome Cookie", "Chrome cookies", Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(local, "Google"), "Chrome"), Path.Combine("User Data", "Default")), "Network"), "Cookies"));
            AddDir(items, "Edge 缓存", "Edge cache", Path.Combine(Path.Combine(Path.Combine(Path.Combine(local, "Microsoft"), "Edge"), Path.Combine("User Data", "Default")), "Cache"));
            AddFile(items, "Edge Cookie", "Edge cookies", Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(local, "Microsoft"), "Edge"), Path.Combine("User Data", "Default")), "Network"), "Cookies"));
            AddFirefox(items, appdata);
            AddDir(items, "最近文档列表", "Recent documents", Path.Combine(Path.Combine(appdata, "Microsoft"), Path.Combine("Windows", "Recent")));
            AddDir(items, "临时文件", "Temp files", temp);
            AddThumbCache(items, local);
            AddReg(items, "运行框历史", "Run history", @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
            AddReg(items, "资源管理器搜索历史", "Explorer search history", @"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery");
            AddReg(items, "输入过的网址历史", "Typed URLs", @"Software\Microsoft\Internet Explorer\TypedURLs");
            AddClip(items);
            AddDns(items);
            AddRecycle(items);
            return items;
        }

        static void AddDir(List<PrivItem> items, string zh, string en, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Directory.Exists(path)) return;
            PrivItem it = new PrivItem();
            it.Zh = zh; it.En = en; it.Kind = "dir"; it.Target = path; it.IsDir = true;
            it.Size = DirSize(path, 40000);
            items.Add(it);
        }

        static void AddFile(List<PrivItem> items, string zh, string en, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) return;
            PrivItem it = new PrivItem();
            it.Zh = zh; it.En = en; it.Kind = "file"; it.Target = path;
            try { it.Size = new FileInfo(path).Length; } catch (Exception dg73_1) { Diag.Swallow("PrivacyCore.cs:73", dg73_1); }
            items.Add(it);
        }

        static void AddFirefox(List<PrivItem> items, string appdata)
        {
            try
            {
                string profiles = Path.Combine(Path.Combine(appdata, "Mozilla"), Path.Combine("Firefox", "Profiles"));
                if (!Directory.Exists(profiles)) return;
                string[] dirs = Directory.GetDirectories(profiles);
                long total = 0;
                for (int i = 0; i < dirs.Length; i++)
                {
                    string c2 = Path.Combine(dirs[i], "cache2");
                    if (Directory.Exists(c2)) total += DirSize(c2, 30000);
                }
                if (total <= 0) return;
                PrivItem it = new PrivItem();
                it.Zh = "Firefox 缓存"; it.En = "Firefox cache"; it.Kind = "ffcache"; it.Target = profiles;
                it.Size = total;
                items.Add(it);
            }
            catch (Exception dg96_1) { Diag.Swallow("PrivacyCore.cs:96", dg96_1); }
        }

        static void AddThumbCache(List<PrivItem> items, string local)
        {
            try
            {
                string dir = Path.Combine(Path.Combine(local, "Microsoft"), Path.Combine("Windows", "Explorer"));
                if (!Directory.Exists(dir)) return;
                string[] fs = Directory.GetFiles(dir);
                long total = 0;
                for (int i = 0; i < fs.Length; i++)
                {
                    string n = Path.GetFileName(fs[i]).ToLower();
                    if (n.StartsWith("thumbcache_") || n.StartsWith("iconcache_"))
                    {
                        try { total += new FileInfo(fs[i]).Length; } catch (Exception dg112_1) { Diag.Swallow("PrivacyCore.cs:112", dg112_1); }
                    }
                }
                if (total <= 0) return;
                PrivItem it = new PrivItem();
                it.Zh = "缩略图 / 图标缓存"; it.En = "Thumb & icon cache"; it.Kind = "thumb"; it.Target = dir;
                it.Size = total;
                items.Add(it);
            }
            catch (Exception dg121_1) { Diag.Swallow("PrivacyCore.cs:121", dg121_1); }
        }

        static void AddReg(List<PrivItem> items, string zh, string en, string path)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(path))
                {
                    if (k == null) return;
                    PrivItem it = new PrivItem();
                    it.Zh = zh; it.En = en; it.Kind = "reg"; it.Target = path;
                    it.Size = k.ValueCount;
                    items.Add(it);
                }
            }
            catch (Exception dg137_1) { Diag.Swallow("PrivacyCore.cs:137", dg137_1); }
        }

        static void AddClip(List<PrivItem> items)
        {
            PrivItem it = new PrivItem();
            it.Zh = "剪贴板内容"; it.En = "Clipboard"; it.Kind = "clip"; it.Target = "clipboard";
            long sz = 0;
            try { sz = Clipboard.GetText().Length; } catch (Exception dg145_1) { Diag.Swallow("PrivacyCore.cs:145", dg145_1); }
            it.Size = sz;
            items.Add(it);
        }

        static void AddDns(List<PrivItem> items)
        {
            PrivItem it = new PrivItem();
            it.Zh = "DNS 解析缓存"; it.En = "DNS cache"; it.Kind = "dns"; it.Target = "ipconfig /flushdns";
            items.Add(it);
        }

        static void AddRecycle(List<PrivItem> items)
        {
            PrivItem it = new PrivItem();
            it.Zh = "回收站"; it.En = "Recycle Bin"; it.Kind = "recycle"; it.Target = "recycle";
            try
            {
                string drive = Path.GetPathRoot(Environment.SystemDirectory);
                long sz = 0;
                string rb = Path.Combine(drive, "$Recycle.Bin");
                if (Directory.Exists(rb)) sz = DirSize(rb, 30000);
                it.Size = sz;
            }
            catch (Exception dg169_1) { Diag.Swallow("PrivacyCore.cs:169", dg169_1); }
            items.Add(it);
        }

        public static long DirSize(string path, int maxFiles)
        {
            long total = 0;
            int n = 0;
            try
            {
                Stack<string> stack = new Stack<string>();
                stack.Push(path);
                while (stack.Count > 0)
                {
                    string cur = stack.Pop();
                    try
                    {
                        string[] fs = Directory.GetFiles(cur);
                        for (int i = 0; i < fs.Length; i++)
                        {
                            try { total += new FileInfo(fs[i]).Length; } catch (Exception dg189_1) { Diag.Swallow("PrivacyCore.cs:189", dg189_1); }
                            n++;
                            if (n > maxFiles) return total;
                        }
                        string[] ds = Directory.GetDirectories(cur);
                        for (int i = 0; i < ds.Length; i++) stack.Push(ds[i]);
                    }
                    catch (Exception dg196_1) { Diag.Swallow("PrivacyCore.cs:196", dg196_1); }
                }
            }
            catch (Exception dg199_1) { Diag.Swallow("PrivacyCore.cs:199", dg199_1); }
            return total;
        }

        public static bool Clean(PrivItem it, out string msg)
        {
            msg = "";
            try
            {
                if (it.Kind == "dir")
                {
                    int ok = 0, fail = 0;
                    try
                    {
                        string[] fs = Directory.GetFiles(it.Target);
                        for (int i = 0; i < fs.Length; i++)
                        {
                            try { File.Delete(fs[i]); ok++; } catch { fail++; }
                        }
                        string[] ds = Directory.GetDirectories(it.Target);
                        for (int i = 0; i < ds.Length; i++)
                        {
                            try { DelTree(ds[i]); ok++; } catch { fail++; }
                        }
                    }
                    catch (Exception ex) { msg = ex.Message; return false; }
                    msg = "删除 " + ok.ToString() + " 项，跳过 " + fail.ToString() + " 项";
                    return true;
                }
                if (it.Kind == "file")
                {
                    File.Delete(it.Target);
                    msg = "已删除";
                    return true;
                }
                if (it.Kind == "reg")
                {
                    Registry.CurrentUser.DeleteSubKeyTree(it.Target);
                    msg = "注册表项已清除";
                    return true;
                }
                if (it.Kind == "clip")
                {
                    Clipboard.Clear();
                    msg = "剪贴板已清空";
                    return true;
                }
                if (it.Kind == "dns")
                {
                    ProcessStartInfo psi = new ProcessStartInfo("ipconfig", "/flushdns");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    Process p = Process.Start(psi);
                    try { p.WaitForExit(8000); } catch (Exception dg253_1) { Diag.Swallow("PrivacyCore.cs:253", dg253_1); }
                    msg = "DNS 缓存已刷新";
                    return true;
                }
                if (it.Kind == "recycle")
                {
                    SHEmptyRecycleBin(IntPtr.Zero, null, 7);
                    msg = "回收站已清空";
                    return true;
                }
                if (it.Kind == "thumb")
                {
                    int ok = 0;
                    string[] fs = Directory.GetFiles(it.Target);
                    for (int i = 0; i < fs.Length; i++)
                    {
                        string n = Path.GetFileName(fs[i]).ToLower();
                        if (n.StartsWith("thumbcache_") || n.StartsWith("iconcache_"))
                        {
                            try { File.Delete(fs[i]); ok++; } catch (Exception dg272_1) { Diag.Swallow("PrivacyCore.cs:272", dg272_1); }
                        }
                    }
                    msg = "删除 " + ok.ToString() + " 个缓存文件";
                    return true;
                }
                if (it.Kind == "ffcache")
                {
                    int ok = 0;
                    try
                    {
                        string[] dirs = Directory.GetDirectories(it.Target);
                        for (int i = 0; i < dirs.Length; i++)
                        {
                            string c2 = Path.Combine(dirs[i], "cache2");
                            if (Directory.Exists(c2))
                            {
                                try { DelTree(c2); ok++; } catch (Exception dg289_1) { Diag.Swallow("PrivacyCore.cs:289", dg289_1); }
                            }
                        }
                    }
                    catch (Exception ex) { msg = ex.Message; return false; }
                    msg = "清理 " + ok.ToString() + " 个 profile 缓存";
                    return true;
                }
                msg = "未知类型";
                return false;
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                return false;
            }
        }

        static void DelTree(string path)
        {
            if (!Directory.Exists(path)) return;
            string[] fs = Directory.GetFiles(path);
            for (int i = 0; i < fs.Length; i++)
            {
                try { File.Delete(fs[i]); } catch (Exception dg313_1) { Diag.Swallow("PrivacyCore.cs:313", dg313_1); }
            }
            string[] ds = Directory.GetDirectories(path);
            for (int i = 0; i < ds.Length; i++) DelTree(ds[i]);
            try { Directory.Delete(path); } catch (Exception dg317_1) { Diag.Swallow("PrivacyCore.cs:317", dg317_1); }
        }
    }
}