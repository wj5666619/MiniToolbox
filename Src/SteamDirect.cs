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
    static class SteamDirect
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr h, string name);

        delegate bool DInit();
        delegate void DVoid();
        delegate IntPtr DStats();
        delegate bool DBoolSelf(IntPtr self);
        delegate uint DUIntSelf(IntPtr self);
        delegate IntPtr DNameAt(IntPtr self, uint index);
        delegate bool DStr(IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string name);

        static IntPtr api = IntPtr.Zero;
        static string apiPath = null;
        static bool looked = false;

        // ---- 找 steam_api64.dll：先看自己目录/组件目录，再从 Steam 库里任意游戏扒一个 ----
        public static string FindApiDll()
        {
            if (looked) return apiPath;
            looked = true;
            string appDir = Path.GetDirectoryName(Application.ExecutablePath);
            string[] cand = new string[]
            {
                Path.Combine(appDir, "steam_api64.dll"),
                Path.Combine(appDir, "sam_tool\\steam_api64.dll"),
                Path.Combine(appDir, "components\\steam_api64.dll")
            };
            for (int i = 0; i < cand.Length; i++)
            {
                if (File.Exists(cand[i])) { apiPath = cand[i]; return apiPath; }
            }
            string steam = SteamInstall();
            if (steam == null) return null;
            string common = Path.Combine(steam, "steamapps\\common");
            string hit = ScanForDll(common, 4, 0);
            if (hit != null) apiPath = hit;
            return apiPath;
        }

        static string ScanForDll(string dir, int maxDepth, int depth)
        {
            if (depth > maxDepth || dir == null || !Directory.Exists(dir)) return null;
            try
            {
                string own = Path.Combine(dir, "steam_api64.dll");
                if (File.Exists(own)) return own;
                DirectoryInfo di = new DirectoryInfo(dir);
                DirectoryInfo[] subs = di.GetDirectories();
                for (int i = 0; i < subs.Length; i++)
                {
                    string r = ScanForDll(subs[i].FullName, maxDepth, depth + 1);
                    if (r != null) return r;
                }
            }
            catch (Exception dg81_1) { Diag.Swallow("SteamDirect.cs:81", dg81_1); }
            return null;
        }

        public static string SteamInstall()
        {
            string p = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k != null)
                    {
                        string sp = Convert.ToString(k.GetValue("SteamPath"));
                        if (sp != null && sp.Length > 0) p = sp.Replace('/', '\\');
                    }
                }
            }
            catch (Exception dg99_1) { Diag.Swallow("SteamDirect.cs:99", dg99_1); }
            if (p == null)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                    {
                        if (k != null)
                        {
                            string sp = Convert.ToString(k.GetValue("InstallPath"));
                            if (sp != null && sp.Length > 0) p = sp.Replace('/', '\\');
                        }
                    }
                }
                catch (Exception dg113_1) { Diag.Swallow("SteamDirect.cs:113", dg113_1); }
            }
            return p;
        }

        static bool EnsureApi()
        {
            if (api != IntPtr.Zero) return true;
            string p = FindApiDll();
            if (p == null) return false;
            api = LoadLibraryW(p);
            return api != IntPtr.Zero;
        }

        static T Fn<T>(string name) where T : class
        {
            IntPtr p = GetProcAddress(api, name);
            if (p == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        // ---- 子进程入口 ----
        public static int ChildMain(string[] args)
        {
            int appid = 0;
            bool dry = false, selftest = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] == null ? "" : args[i].Trim();
                if (a == "dry") dry = true;
                else if (a == "selftest") selftest = true;
                else if (IsInt(a))
                {
                    try { appid = int.Parse(a); } catch (Exception dg146_1) { Diag.Swallow("SteamDirect.cs:146", dg146_1); }
                }
            }
            if (appid <= 0)
            {
                Console.WriteLine("RESULT appid=0 total=0 before=0 set=0 fail=0 after=0 err=缺少 appid");
                return 1;
            }
            string line = RunOne(appid, dry, selftest);
            Console.WriteLine(line);
            return 0;
        }

        static bool IsInt(string s)
        {
            if (s == null || s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }

        // ---- 单款直接解锁（在子进程里跑） ----
        public static string RunOne(int appid, bool dry, bool selftest)
        {
            int total = 0, before = 0, set = 0, fail = 0, after = 0;
            string store = "0";
            string probe = "-";
            string err = "";
            string work = Path.Combine(Path.GetTempPath(), "mtb_steam_" + appid);
            string oldCwd = null;
            try { oldCwd = Directory.GetCurrentDirectory(); } catch (Exception dg178_1) { Diag.Swallow("SteamDirect.cs:178", dg178_1); }
            try
            {
                if (!EnsureApi())
                {
                    return "RESULT appid=" + appid + " total=0 before=0 set=0 fail=0 after=0 store=0 err=" + OneLine("找不到 steam_api64.dll");
                }
                try { Directory.CreateDirectory(work); } catch (Exception dg185_1) { Diag.Swallow("SteamDirect.cs:185", dg185_1); }
                File.WriteAllText(Path.Combine(work, "steam_appid.txt"), appid.ToString(), new UTF8Encoding(false));
                Directory.SetCurrentDirectory(work);

                DInit init = Fn<DInit>("SteamAPI_Init");
                if (init == null)
                {
                    return "RESULT appid=" + appid + " total=0 before=0 set=0 fail=0 after=0 store=0 err=" + OneLine("steam_api64.dll 里没有 SteamAPI_Init");
                }
                bool inited = false;
                try { inited = init(); }
                catch (Exception e) { err = "InitException " + e.Message; }
                if (!inited)
                {
                    if (err.Length == 0) err = "SteamAPI_Init 失败（Steam 没登录 / 没这个游戏 / 有别的游戏正在跑）";
                    return "RESULT appid=" + appid + " total=0 before=0 set=0 fail=0 after=0 store=0 err=" + OneLine(err);
                }

                DStats gs = Fn<DStats>("SteamAPI_SteamUserStats_v012");
                IntPtr stats = gs == null ? IntPtr.Zero : gs();
                if (stats == IntPtr.Zero)
                {
                    err = "拿不到 UserStats 接口";
                }
                else
                {
                    DBoolSelf req = Fn<DBoolSelf>("SteamAPI_ISteamUserStats_RequestCurrentStats");
                    if (req != null) { try { req(stats); } catch (Exception dg212_1) { Diag.Swallow("SteamDirect.cs:212", dg212_1); } }
                    Thread.Sleep(350);

                    DUIntSelf num = Fn<DUIntSelf>("SteamAPI_ISteamUserStats_GetNumAchievements");
                    DNameAt nameAt = Fn<DNameAt>("SteamAPI_ISteamUserStats_GetAchievementName");
                    DStr getAch = Fn<DStr>("SteamAPI_ISteamUserStats_GetAchievement");
                    DStr setAch = Fn<DStr>("SteamAPI_ISteamUserStats_SetAchievement");
                    DBoolSelf storeStats = Fn<DBoolSelf>("SteamAPI_ISteamUserStats_StoreStats");
                    if (num == null || nameAt == null || getAch == null || setAch == null || storeStats == null)
                    {
                        err = "成就相关导出缺失（steam_api64.dll 版本不对）";
                    }
                    else
                    {
                        total = (int)num(stats);
                        // Steam 刚起来时统计数据可能还没到位，total=0 时重试几次，免得误判成「没成就」
                        for (int attempt = 0; attempt < 10 && total == 0 && !dry; attempt++)
                        {
                            try { if (req != null) req(stats); } catch (Exception dg230_1) { Diag.Swallow("SteamDirect.cs:230", dg230_1); }
                            Thread.Sleep(400);
                            total = (int)num(stats);
                        }
                        for (int attempt = 0; attempt < 4 && total == 0 && dry; attempt++)
                        {
                            try { if (req != null) req(stats); } catch (Exception dg236_1) { Diag.Swallow("SteamDirect.cs:236", dg236_1); }
                            Thread.Sleep(350);
                            total = (int)num(stats);
                        }
                        List<string> names = new List<string>();
                        for (uint i = 0; i < (uint)total; i++)
                        {
                            IntPtr p = nameAt(stats, i);
                            if (p == IntPtr.Zero) continue;
                            string nm = Marshal.PtrToStringAnsi(p);
                            if (nm != null && nm.Length > 0) names.Add(nm);
                        }
                        string firstUnlocked = null;
                        for (int i = 0; i < names.Count; i++)
                        {
                            bool got = false;
                            try { got = getAch(stats, names[i]); } catch (Exception dg252_1) { Diag.Swallow("SteamDirect.cs:252", dg252_1); }
                            if (got)
                            {
                                before++;
                                if (firstUnlocked == null) firstUnlocked = names[i];
                            }
                        }
                        if (!dry)
                        {
                            for (int i = 0; i < names.Count; i++)
                            {
                                bool got = false;
                                try { got = getAch(stats, names[i]); } catch (Exception dg264_1) { Diag.Swallow("SteamDirect.cs:264", dg264_1); }
                                if (got) continue;
                                bool r = false;
                                try { r = setAch(stats, names[i]); } catch (Exception e) { err = e.Message; }
                                if (r) set++; else fail++;
                            }
                        }
                        bool needStore = (set > 0) || selftest;
                        if (selftest && firstUnlocked != null)
                        {
                            // 用一个本来就已解锁的成就走一遍写入，验证写入通道通不通（不会改状态）
                            try { probe = setAch(stats, firstUnlocked) ? "1" : "0"; } catch { probe = "x"; }
                        }
                        if (needStore)
                        {
                            bool ok2 = false;
                            try { ok2 = storeStats(stats); } catch (Exception e) { err = e.Message; }
                            store = ok2 ? "1" : "0";
                            if (!ok2 && err.Length == 0) err = "StoreStats 失败（写入没提交）";
                            if (!dry) { Thread.Sleep(400); }
                        }
                        for (int i = 0; i < names.Count; i++)
                        {
                            bool got = false;
                            try { got = getAch(stats, names[i]); } catch (Exception dg288_1) { Diag.Swallow("SteamDirect.cs:288", dg288_1); }
                            if (got) after++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                err = e.GetType().Name + " " + e.Message;
            }
            finally
            {
                try { DVoid shut = Fn<DVoid>("SteamAPI_Shutdown"); if (shut != null) shut(); } catch (Exception dg300_1) { Diag.Swallow("SteamDirect.cs:300", dg300_1); }
                try { if (oldCwd != null) Directory.SetCurrentDirectory(oldCwd); } catch (Exception dg301_1) { Diag.Swallow("SteamDirect.cs:301", dg301_1); }
                try { Directory.Delete(work, true); } catch (Exception dg302_1) { Diag.Swallow("SteamDirect.cs:302", dg302_1); }
            }
            return "RESULT appid=" + appid + " total=" + total + " before=" + before + " set=" + set
                + " fail=" + fail + " after=" + after + " store=" + store + " probe=" + probe
                + (err.Length > 0 ? " err=" + OneLine(err) : "");
        }

        static string OneLine(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}