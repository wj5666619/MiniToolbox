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
    class BoostCore
    {
        public static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string RunCmd(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e2 = p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);
                    return (o + " " + e2).Trim();
                }
            }
            catch (Exception ex) { return "EX " + ex.Message; }
        }

        public static string CurrentPowerGuid()
        {
            string o = RunCmd("powercfg", "/getactivescheme");
            int i = o.IndexOf("GUID:");
            if (i < 0) i = o.IndexOf("guid:");
            if (i < 0) return "";
            string s = o.Substring(i + 5).Trim();
            int sp = s.IndexOfAny(new char[] { ' ', '\r', '\n', '（', '(' });
            if (sp > 0) s = s.Substring(0, sp);
            return s.Trim();
        }

        public static bool HasProcess(string name)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(name);
                bool ok = ps.Length > 0;
                foreach (Process p in ps) { try { p.Dispose(); } catch (Exception dg75_1) { Diag.Swallow("BoostCore.cs:75", dg75_1); } }
                return ok;
            }
            catch { return false; }
        }

        // 返回执行报告；dryRun 只写“将要做什么”，不改系统
        public static string Run(string backupPath, bool dryRun, List<string> procNames,
            bool boostProc, bool powerHigh, bool flushDns, bool lowerOthers, bool stopSearch)
        {
            StringBuilder rep = new StringBuilder();
            StringBuilder bk = new StringBuilder();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            rep.AppendLine("开始：" + stamp + (dryRun ? "（演练模式，不改系统）" : ""));

            // 1) 电源计划
            string prev = CurrentPowerGuid();
            if (prev.Length > 0) bk.AppendLine("POWER\t" + prev);
            if (powerHigh)
            {
                string high = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
                if (dryRun) rep.AppendLine("· 电源计划：高性能（原 " + (prev.Length > 0 ? prev : "未知") + "）");
                else
                {
                    string r = RunCmd("powercfg", "/setactive " + high);
                    bool ok = !r.StartsWith("EX");
                    if (!ok)
                    {
                        r = RunCmd("powercfg", "/setactive SCHEME_MIN");
                        ok = !r.StartsWith("EX");
                    }
                    rep.AppendLine("· 电源计划：切换到高性能" + (ok ? " ✓" : " ✗ " + r));
                }
            }

            // 2) 目标进程提优先级
            List<string> used = new List<string>();
            if (boostProc && procNames != null && procNames.Count > 0)
            {
                foreach (string nm in procNames)
                {
                    string name = nm.Trim();
                    if (name.Length == 0) continue;
                    if (!HasProcess(name)) { rep.AppendLine("· 进程 " + name + "：未运行，跳过"); continue; }
                    if (dryRun) { rep.AppendLine("· 进程 " + name + "：优先级设为「高」"); used.Add(name); continue; }
                    bool any = false;
                    Process[] ps = Process.GetProcessesByName(name);
                    foreach (Process p in ps)
                    {
                        try
                        {
                            p.PriorityClass = ProcessPriorityClass.High;
                            bk.AppendLine("LOW\t" + p.Id + "\t" + name);
                            any = true;
                        }
                        catch (Exception dg130_1) { Diag.Swallow("BoostCore.cs:130", dg130_1); }
                        try { p.Dispose(); } catch (Exception dg131_1) { Diag.Swallow("BoostCore.cs:131", dg131_1); }
                    }
                    rep.AppendLine("· 进程 " + name + "：优先级设为「高」" + (any ? " ✓" : " ✗（权限不足）"));
                    if (any) used.Add(name);
                }
            }

            // 3) DNS 缓存
            if (flushDns)
            {
                if (dryRun) rep.AppendLine("· 刷新 DNS 缓存");
                else
                {
                    string r = RunCmd("ipconfig", "/flushdns");
                    rep.AppendLine("· 刷新 DNS 缓存" + (!r.StartsWith("EX") ? " ✓" : " ✗ " + r));
                }
            }

            // 4) 把其他前台程序降到底层优先级（不关进程，零数据风险）
            if (lowerOthers)
            {
                int n = 0;
                try
                {
                    foreach (Process p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.Id == Process.GetCurrentProcess().Id) continue;
                            if (p.MainWindowHandle == IntPtr.Zero) continue;
                            string nm = p.ProcessName.ToLowerInvariant();
                            if (used.Contains(nm)) continue;
                            if (nm == "explorer" || nm == "dwm" || nm == "taskmgr" || nm.StartsWith("steam")) continue;
                            if (p.PriorityClass == ProcessPriorityClass.Idle) continue;
                            if (dryRun) { n++; continue; }
                            p.PriorityClass = ProcessPriorityClass.BelowNormal;
                            bk.AppendLine("LOW\t" + p.Id + "\t" + nm);
                            n++;
                        }
                        catch (Exception dg170_1) { Diag.Swallow("BoostCore.cs:170", dg170_1); }
                        finally { }
                    }
                }
                catch (Exception dg174_1) { Diag.Swallow("BoostCore.cs:174", dg174_1); }
                rep.AppendLine("· 其他前台程序降到「低于正常」优先级：" + n + " 个（不关进程、不动数据）");
            }

            // 5) 暂停 Windows Search 索引（需管理员）
            if (stopSearch)
            {
                if (!IsAdmin())
                {
                    rep.AppendLine("· 暂停 Windows Search：跳过（需要管理员权限）");
                }
                else
                {
                    string mode = "auto";
                    string q = RunCmd("sc", "qc WSearch");
                    if (q.IndexOf("DEMAND", StringComparison.OrdinalIgnoreCase) >= 0) mode = "demand";
                    bk.AppendLine("SVC\tWSearch\t" + mode);
                    if (dryRun) rep.AppendLine("· 暂停 Windows Search 索引（原启动类型 " + mode + "）");
                    else
                    {
                        string r1 = RunCmd("sc", "config WSearch start= demand");
                        string r2 = RunCmd("sc", "stop WSearch");
                        rep.AppendLine("· 暂停 Windows Search 索引" + (!r2.StartsWith("EX") ? " ✓" : " ✗ " + r2));
                    }
                }
            }

            if (bk.Length > 0)
            {
                try
                {
                    if (!dryRun)
                    {
                        StringBuilder all = new StringBuilder();
                        all.AppendLine("# 小工具箱 加速备份 " + stamp + "（「一键还原」用它回滚）");
                        all.Append(bk.ToString());
                        File.WriteAllText(backupPath, all.ToString(), Encoding.UTF8);
                        rep.AppendLine("备份已写入：" + backupPath);
                    }
                }
                catch (Exception ex) { rep.AppendLine("备份写入失败：" + ex.Message); }
            }
            rep.AppendLine("完成：" + DateTime.Now.ToString("HH:mm:ss"));
            return rep.ToString();
        }

        public static string Restore(string backupPath, bool dryRun)
        {
            StringBuilder rep = new StringBuilder();
            if (!File.Exists(backupPath)) return "没有找到备份文件：" + backupPath;
            string[] lines = File.ReadAllLines(backupPath, Encoding.UTF8);
            int done = 0;
            foreach (string ln in lines)
            {
                if (ln == null || ln.StartsWith("#") || ln.Trim().Length == 0) continue;
                string[] p = ln.Split('\t');
                try
                {
                    if (p[0] == "POWER" && p.Length >= 2)
                    {
                        if (dryRun) { rep.AppendLine("· 电源计划 → " + p[1]); done++; }
                        else
                        {
                            string r = RunCmd("powercfg", "/setactive " + p[1]);
                            rep.AppendLine("· 电源计划 → " + p[1] + (!r.StartsWith("EX") ? " ✓" : " ✗ " + r));
                            done++;
                        }
                    }
                    else if (p[0] == "SVC" && p.Length >= 3)
                    {
                        if (dryRun) { rep.AppendLine("· 服务 " + p[1] + " → 启动类型 " + p[2]); done++; }
                        else
                        {
                            RunCmd("sc", "config " + p[1] + " start= " + p[2]);
                            RunCmd("sc", "start " + p[1]);
                            rep.AppendLine("· 服务 " + p[1] + " 恢复为 " + p[2] + " ✓");
                            done++;
                        }
                    }
                    else if (p[0] == "LOW" && p.Length >= 3)
                    {
                        int pid = 0;
                        if (!int.TryParse(p[1], out pid)) continue;
                        if (dryRun) { rep.AppendLine("· 进程 " + p[2] + "(" + pid + ") → 正常优先级"); done++; continue; }
                        try
                        {
                            Process pr = Process.GetProcessById(pid);
                            pr.PriorityClass = ProcessPriorityClass.Normal;
                            rep.AppendLine("· 进程 " + p[2] + "(" + pid + ") → 正常优先级 ✓");
                            done++;
                            pr.Dispose();
                        }
                        catch { rep.AppendLine("· 进程 " + p[2] + "(" + pid + ") 已退出，跳过"); }
                    }
                }
                catch (Exception dg269_1) { Diag.Swallow("BoostCore.cs:269", dg269_1); }
            }
            rep.AppendLine("还原完成，共 " + done + " 项。");
            return rep.ToString();
        }

        public static List<string> TopProcesses(int n)
        {
            List<string> names = new List<string>();
            List<long> mems = new List<long>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        string nm = p.ProcessName;
                        if (nm == "小工具箱") continue;
                        long m = 0;
                        try { m = p.WorkingSet64; } catch (Exception dg289_1) { Diag.Swallow("BoostCore.cs:289", dg289_1); }
                        names.Add(nm);
                        mems.Add(m);
                    }
                    catch (Exception dg293_1) { Diag.Swallow("BoostCore.cs:293", dg293_1); }
                }
            }
            catch (Exception dg296_1) { Diag.Swallow("BoostCore.cs:296", dg296_1); }
            // 去重并按内存排序
            List<string> uniq = new List<string>();
            for (int i = 0; i < names.Count; i++)
            {
                bool dup = false;
                for (int j = 0; j < uniq.Count; j++)
                {
                    if (uniq[j] == names[i]) { dup = true; break; }
                }
                if (!dup) uniq.Add(names[i]);
            }
            List<long> score = new List<long>();
            for (int i = 0; i < uniq.Count; i++) score.Add(0);
            for (int i = 0; i < names.Count; i++)
            {
                int idx = uniq.IndexOf(names[i]);
                if (idx >= 0) score[idx] += mems[i];
            }
            for (int i = 0; i < uniq.Count; i++)
            {
                for (int j = i + 1; j < uniq.Count; j++)
                {
                    if (score[j] > score[i])
                    {
                        long tv = score[i]; score[i] = score[j]; score[j] = tv;
                        string tn = uniq[i]; uniq[i] = uniq[j]; uniq[j] = tn;
                    }
                }
            }
            List<string> res = new List<string>();
            for (int i = 0; i < uniq.Count && i < n; i++) res.Add(uniq[i]);
            return res;
        }
    }
}