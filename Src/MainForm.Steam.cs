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
    // v19.8：从 MainForm.cs 原样迁出（Steam 解锁与工作台），仅为拆分文件，逻辑未改
    partial class MainForm
    {
        // ===== 一体式工作台：控制台 / 进度 / 宿主切换 =====
        // 跑批的全量输出（_batch_report.txt 的内容）实时镜像到页内控制台，不再弹汇总框、不再跳记事本
        void SamConsoleSet(string text)
        {
            try
            {
                if (samConsole == null) return;
                Action act = delegate()
                {
                    try
                    {
                        samConsole.Text = text == null ? "" : text;
                        samConsole.SelectionStart = samConsole.TextLength;
                        samConsole.ScrollToCaret();
                    }
                    catch (Exception dg7564_1) { Diag.Swallow("MainForm.Steam.cs:41", dg7564_1); }
                };
                if (samConsole.InvokeRequired) samConsole.BeginInvoke(act);
                else act();
            }
            catch (Exception dg7569_1) { Diag.Swallow("MainForm.Steam.cs:46", dg7569_1); }
        }

        // 进度条：0~100 按已处理款数推进，跑完自动满格
        void SamProgress(int done, int total)
        {
            try
            {
                if (samBarFill == null) return;
                int pct = (total <= 0) ? 0 : (int)Math.Round(done * 100.0 / total);
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                Action act = delegate()
                {
                    try
                    {
                        int w = samBarTrack == null ? 0 : samBarTrack.ClientSize.Width;
                        samBarFill.Width = Math.Max(0, Math.Min(w, (int)(w * pct / 100.0)));
                    }
                    catch (Exception dg7588_1) { Diag.Swallow("MainForm.Steam.cs:65", dg7588_1); }
                };
                if (samBarFill.InvokeRequired) samBarFill.BeginInvoke(act);
                else act();
            }
            catch (Exception dg7593_1) { Diag.Swallow("MainForm.Steam.cs:70", dg7593_1); }
        }

        // 默认形态：控制台在上，SAM 宿主收起（一体式不需要成就管理器窗口）
        void SamShowConsoleMode()
        {
            if (samConsole != null) { samConsole.Visible = true; samConsole.BringToFront(); }
            if (samHost != null) samHost.Visible = false;
        }

        // 旧路径形态：把 SAM 窗口嵌进来，控制台暂时让位
        void SamShowHostMode()
        {
            if (samConsole != null) samConsole.Visible = false;
            if (samHost != null) { samHost.Visible = true; samHost.BringToFront(); }
        }

        bool samBatchRunning = false;
        bool samBatchStop = false;
        int samOnlyAppId = 0;      // >0 时只处理这一款（调试/单游戏验证用）

        void SamTrace(string s)
        {
            // 正常使用不落 trace：以前每跑一次解锁就在目录里堆一份 _batch_trace.txt，属于无用数据
            if (!samTestMode) return;
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "_batch_trace.txt"),
                    System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n");
            }
            catch (Exception dg7623_1) { Diag.Swallow("MainForm.Steam.cs:100", dg7623_1); }
        }

        // ============ 直接解锁（默认模式）============
        // 不打开任何游戏成就页，逐款拉起本程序的无窗口子进程，直连 Steamworks 把成就写进账号。
        void DirectUnlockAll()
        {
            if (samBatchRunning)
            {
                // 一体式：同一个按钮兼作停止键，不再弹「是/否」确认框
                samBatchStop = true;
                SamSetState("正在停止…（当前这一款写完就退出，再点一次从这一款接着跑）", "Stopping after current game…");
                return;
            }
            if (samScanRunning)
            {
                SamSetState("正在统计完成度，等它读完再解锁", "Completion scan running");
                return;
            }
            try
            {
                if (Process.GetProcessesByName("steam").Length == 0)
                {
                    if (samTestMode) { SamSetState("【自测】Steam 未运行", "TEST: steam not running"); return; }
                    SamSetState("Steam 未运行：先启动 Steam 并登录账号", "Steam not running: start it and sign in");
                    SamConsoleSet("【未开始】Steam 未运行。\r\n先启动 Steam 并登录账号，再点一次「一键全成就」——不需要打开成就管理器窗口。");
                    return;
                }
            }
            catch (Exception dg7652_1) { Diag.Swallow("MainForm.Steam.cs:129", dg7652_1); }
            string dll = SteamDirect.FindApiDll();
            if (dll == null)
            {
                if (samTestMode) { SamSetState("【自测】找不到 steam_api64.dll", "TEST: steam_api64.dll missing"); return; }
                SamSetState("找不到 steam_api64.dll：直连通道不可用", "steam_api64.dll missing: direct path unavailable");
                SamConsoleSet("【未开始】没找到 steam_api64.dll，直连写入需要它。\r\n通常是 Steam 没装全或装在非常规目录；"
                    + "确实想试试旧办法就点下面「进游戏页解锁（旧）」。");
                return;
            }
            List<SchemaGame> games = ScanSchemaGames();
            if (games.Count == 0)
            {
                SamSetState("Steam 本地没有成就定义文件，请先启动 Steam 登录一次再试。",
                    "No achievement schema found; start Steam and sign in once, then retry.");
                SamConsoleSet("【未开始】本地没有读到任何成就定义。\r\n先启动 Steam 登录一次（随便进一款有成就的游戏更好），再点一次「一键全成就」。");
                SamProgress(0, 0);
                return;
            }
            if (samOnlyAppId > 0)
            {
                List<SchemaGame> one = new List<SchemaGame>();
                for (int k = 0; k < games.Count; k++) if (games[k].AppId == samOnlyAppId) one.Add(games[k]);
                games = one;
                if (games.Count == 0)
                {
                    SamSetState("没有找到 appid=" + samOnlyAppId + " 的成就定义", "No schema for this appid");
                    return;
                }
            }
            SamTrace("mode=direct dll=" + dll + " games=" + games.Count);
            directDone = 0;
            directTotal = games.Count;
            SetDirectBtnText("停止解锁 0/" + games.Count);
            SamShowConsoleMode();
            SamProgress(0, games.Count);
            samBatchRunning = true;
            samBatchStop = false;
            Thread th = new Thread(delegate() { DirectWorker(games, dll); });
            th.IsBackground = true;
            th.Start();
        }

        int directDone = 0;     // 已处理款数（含跳过）
        int directTotal = 0;
        bool samScanRunning = false;   // 成就完成度统计是否在跑
        int samScanLimit = 0;          // >0 时只统计前 N 款（仅自测用）
        bool samPctTest = false;       // 自测模式：跑完把控制台正文写进 _pct_test.txt 再退出

        // 按钮上显示进度，顺便当「正在跑」的视觉提示
        void SetDirectBtnText(string txt)
        {
            try
            {
                if (btnUnlockDirect == null) return;
                if (btnUnlockDirect.InvokeRequired)
                    btnUnlockDirect.BeginInvoke(new Action(delegate() { btnUnlockDirect.Text = txt; }));
                else
                    btnUnlockDirect.Text = txt;
            }
            catch (Exception dg7712_1) { Diag.Swallow("MainForm.Steam.cs:189", dg7712_1); }
        }

        void DirectWorker(List<SchemaGame> games, string dll)
        {
            StringBuilder report = new StringBuilder();
            int okG = 0, skipG = 0, failG = 0, protG = 0, gain = 0;
            bool stopped = false;
            int n = games.Count;
            string cursorPath = Path.Combine(BASE_DIR, "_direct_cursor.txt");
            int startI = 0;
            // 上次被停掉的话，从停下的那一款接着跑（列表没变才续跑）
            try
            {
                if (File.Exists(cursorPath))
                {
                    string[] parts = File.ReadAllText(cursorPath).Trim().Split('|');
                    if (parts.Length == 2 && IsAllDigits(parts[0]) && IsAllDigits(parts[1]))
                    {
                        int done = int.Parse(parts[0]);
                        int tot = int.Parse(parts[1]);
                        if (tot == n && done >= 0 && done < n) startI = done;
                    }
                }
            }
            catch (Exception dg7737_1) { Diag.Swallow("MainForm.Steam.cs:214", dg7737_1); }
            List<string> failList = new List<string>();
            report.Append("【直接解锁】共 " + n + " 个有成就定义的游戏，逐款直连 Steamworks 写入，不打开游戏页\r\n");
            report.Append("使用的组件：" + dll + "\r\n");
            if (startI > 0) report.Append("续跑：上次已处理 " + startI + " 款，从第 " + (startI + 1) + " 款接着跑\r\n");
            report.Append("\r\n");
            FlushBatchReport(report);
            try
            {
                for (int i = startI; i < n; i++)
                {
                    if (samBatchStop) { stopped = true; break; }
                    SchemaGame g = games[i];
                    directDone = i;
                    SetDirectBtnText("停止解锁 " + (i + 1) + "/" + n);
                    SamProgress(i + 1, n);
                    SamSetState("【直接解锁 " + (i + 1) + "/" + n + "】" + g.Name + "（" + g.AppId + "）…",
                        "Direct unlock " + (i + 1) + "/" + n + ": " + g.Name);
                    string line = RunDirectChild(g.AppId, "");
                    int total = KeyInt(line, "total");
                    int before = KeyInt(line, "before");
                    int set = KeyInt(line, "set");
                    int failc = KeyInt(line, "fail");
                    int after = KeyInt(line, "after");
                    string err = KeyStr(line, "err");
                    if (total <= 0)
                    {
                        // 失败款：先原地重试一次（Steam 刚启动时的偶发失败多半能过），再不行进失败清单，收尾统一补跑
                        Thread.Sleep(700);
                        line = RunDirectChild(g.AppId, "");
                        total = KeyInt(line, "total");
                        before = KeyInt(line, "before");
                        set = KeyInt(line, "set");
                        failc = KeyInt(line, "fail");
                        after = KeyInt(line, "after");
                        err = KeyStr(line, "err");
                    }
                    if (total <= 0)
                    {
                        failG++;
                        failList.Add(g.AppId + "\t" + g.Name + "\t" + (err.Length > 0 ? err : "没有成就 / 未拥有该游戏"));
                        report.Append("✗ " + g.Name + " (" + g.AppId + ")  失败：" + (err.Length > 0 ? err : "没有成就 / 未拥有该游戏") + "\r\n");
                    }
                    else if (set == 0 && failc == 0)
                    {
                        skipG++;
                        report.Append("· " + g.Name + " (" + g.AppId + ")  本来就全解锁 " + before + "/" + total + "，跳过\r\n");
                    }
                    else if (set == 0)
                    {
                        protG++;
                        report.Append("🔒 " + g.Name + " (" + g.AppId + ")  受保护成就 " + failc + " 个被游戏侧拒绝写入（现在 " + after + "/" + total + "）\r\n");
                    }
                    else
                    {
                        okG++;
                        gain += set;
                        report.Append("✓ " + g.Name + " (" + g.AppId + ")  解锁 " + set + " 个，失败 " + failc
                            + " 个，现在 " + after + "/" + total + (err.Length > 0 ? "（" + err + "）" : "") + "\r\n");
                    }
                    try { File.WriteAllText(cursorPath, (i + 1) + "|" + n, new UTF8Encoding(false)); } catch (Exception dg7797_1) { Diag.Swallow("MainForm.Steam.cs:274", dg7797_1); }
                    FlushBatchReport(report);
                }
            }
            catch (Exception e)
            {
                report.Append("\r\n异常中断：" + e.Message + "\r\n");
            }
            // 收尾补跑：失败款统一再跑一轮，成功的扣掉失败数，真不行的写进失败清单
            if (!stopped && failList.Count > 0)
            {
                report.Append("\r\n【补跑】" + failList.Count + " 款失败游戏再试一轮\r\n");
                FlushBatchReport(report);
                List<string> still = new List<string>();
                for (int k = 0; k < failList.Count; k++)
                {
                    if (samBatchStop) { still.Add(failList[k]); continue; }
                    string[] parts2 = failList[k].Split('\t');
                    int appid2 = 0;
                    try { appid2 = int.Parse(parts2[0]); } catch { still.Add(failList[k]); continue; }
                    string nm2 = parts2.Length > 1 ? parts2[1] : Convert.ToString(appid2);
                    SamSetState("【补跑 " + (k + 1) + "/" + failList.Count + "】" + nm2 + "（" + appid2 + "）",
                        "Retry " + (k + 1) + "/" + failList.Count);
                    try { Thread.Sleep(500); } catch (Exception dg7820_1) { Diag.Swallow("MainForm.Steam.cs:297", dg7820_1); }
                    string line2 = RunDirectChild(appid2, "");
                    int total2 = KeyInt(line2, "total");
                    int before2 = KeyInt(line2, "before");
                    int set2 = KeyInt(line2, "set");
                    int failc2 = KeyInt(line2, "fail");
                    int after2 = KeyInt(line2, "after");
                    string err2 = KeyStr(line2, "err");
                    if (total2 > 0 && set2 > 0)
                    {
                        if (failG > 0) failG--;
                        okG++;
                        gain += set2;
                        report.Append("✓ 补跑成功 " + nm2 + " (" + appid2 + ") 解锁 " + set2 + "，现在 " + after2 + "/" + total2 + "\r\n");
                    }
                    else if (total2 > 0 && failc2 == 0 && before2 >= total2)
                    {
                        if (failG > 0) failG--;
                        skipG++;
                        report.Append("· 补跑发现 " + nm2 + " (" + appid2 + ") 本来就全解锁，改记跳过\r\n");
                    }
                    else
                    {
                        still.Add(appid2 + "\t" + nm2 + "\t" + (err2.Length > 0 ? err2 : "仍失败"));
                        report.Append("✗ 补跑仍失败 " + nm2 + " (" + appid2 + ") " + err2 + "\r\n");
                    }
                    FlushBatchReport(report);
                }
                failList = still;
            }
            if (failList.Count > 0)
            {
                try
                {
                    StringBuilder fb = new StringBuilder();
                    fb.AppendLine("appid\tname\treason");
                    for (int k = 0; k < failList.Count; k++) fb.AppendLine(failList[k]);
                    File.WriteAllText(Path.Combine(BASE_DIR, "_unlock_failed.txt"), fb.ToString(), new UTF8Encoding(false));
                }
                catch (Exception dg7859_1) { Diag.Swallow("MainForm.Steam.cs:336", dg7859_1); }
            }
            if (!stopped)
            {
                try { File.Delete(cursorPath); } catch (Exception dg7863_1) { Diag.Swallow("MainForm.Steam.cs:340", dg7863_1); }
                directDone = n;
            }
            int doneNow = stopped ? directDone : n;
            report.Append("\r\n【完成】写入成功 " + okG + " 款、受保护 " + protG + " 款、跳过 " + skipG + " 款、失败 " + failG + " 款，共解锁 " + gain + " 个成就");
            report.Append(stopped ? "（手动停止，已处理 " + doneNow + "/" + n + " 款，剩余 " + (n - doneNow) + " 款下次接着跑）" : "（全库 " + n + " 款已全部处理）");
            report.Append("\r\n");
            FlushBatchReport(report);
            string summary = "直接解锁" + (stopped ? "已停止" : "完成") + "：成功 " + okG + " 款 / 受保护 " + protG + " 款 / 跳过 " + skipG + " 款 / 失败 " + failG + " 款，共写入 " + gain + " 个成就"
                + "；已处理 " + doneNow + " / " + n + " 款" + (stopped ? "（剩余 " + (n - doneNow) + " 款，再点一次接着跑）" : "")
                + (failList.Count > 0 ? "；仍失败的 " + failList.Count + " 款记在 _unlock_failed.txt（多为没成就定义或库里没有这款）" : "")
                + "。";
            // 一体式：汇总直接追加进报告（控制台同步显示），不再弹窗
            report.Append("\r\n【汇总】" + summary + "\r\n");
            if (stopped) report.Append("进度 " + doneNow + "/" + n + "，再点一次「一键全成就」会从这一款接着跑。\r\n");
            FlushBatchReport(report);
            SamProgress(stopped ? doneNow : n, n);
            SamSetState("直接解锁结束：成功 " + okG + " 款，受保护 " + protG + "，跳过 " + skipG + "，失败 " + failG + "，写入 " + gain + " 个成就"
                + (stopped ? "（已停止 " + doneNow + "/" + n + "）" : "（全库完成）"),
                "Done: " + okG + " ok, " + skipG + " skipped, " + failG + " failed, " + gain + " unlocked");
            SetDirectBtnText(T("一键全成就\n（跑完全库）", "Unlock all\n(full library)"));
            samBatchRunning = false;
            samBatchStop = false;
        }

        // ============ 成就完成度面板（只读：只读已解锁数与总数，绝不写账号）============
        void SamCompletionScan()
        {
            if (samScanRunning)
            {
                samBatchStop = true;
                SamSetState("正在停止统计…（当前这一款读完就退出）", "Stopping the scan…");
                return;
            }
            if (samBatchRunning)
            {
                SamSetState("正在跑「一键全成就」，等它跑完再统计", "Unlock batch running");
                return;
            }
            try
            {
                if (Process.GetProcessesByName("steam").Length == 0)
                {
                    if (samTestMode) { SamSetState("【自测】Steam 未运行", "TEST: steam not running"); return; }
                    SamSetState("Steam 未运行：先启动 Steam 并登录账号", "Steam not running");
                    SamConsoleSet("【未开始】Steam 未运行。\r\n统计完成度只是读取本地成就数据，但也需要 Steam 在跑并登录。");
                    return;
                }
            }
            catch (Exception dg7912_1) { Diag.Swallow("MainForm.Steam.cs:389", dg7912_1); }
            string dll = SteamDirect.FindApiDll();
            if (dll == null)
            {
                SamSetState("找不到 steam_api64.dll：读不到完成度", "steam_api64.dll missing");
                SamConsoleSet("【未开始】没找到 steam_api64.dll，只读统计同样需要它。");
                return;
            }
            List<SchemaGame> games = ScanSchemaGames();
            if (games.Count == 0)
            {
                SamSetState("本地没有成就定义：先启动 Steam 登录一次", "No schema found");
                SamConsoleSet("【未开始】本地没有读到任何成就定义。先启动 Steam 登录一次再点。");
                return;
            }
            SamTrace("mode=completion dll=" + dll + " games=" + games.Count);
            SamShowConsoleMode();
            SamProgress(0, games.Count);
            samScanRunning = true;
            samBatchStop = false;
            Thread th = new Thread(delegate() { CompletionWorker(games, dll); });
            th.IsBackground = true;
            th.Start();
        }

        void CompletionWorker(List<SchemaGame> games, string dll)
        {
            int n = games.Count;
            int full = 0, part = 0, zero = 0, failN = 0;
            long sumGot = 0, sumAll = 0;
            bool stopped = false;
            List<string[]> rows = new List<string[]>();
            StringBuilder typed = new StringBuilder();
            typed.Append("【成就完成度】只读统计：逐款读取「已解锁 / 总数」，全程不写入任何成就，不会改动账号。\r\n");
            typed.Append("共 " + n + " 个有成就定义的游戏，组件：" + dll + "\r\n\r\n");
            SamConsoleSet(typed.ToString());
            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (samBatchStop) { stopped = true; break; }
                    if (samScanLimit > 0 && i >= samScanLimit) { stopped = true; break; }
                    SchemaGame g = games[i];
                    SamSetState("【读完成度 " + (i + 1) + "/" + n + "】" + g.Name + "（" + g.AppId + "）…",
                        "Reading completion " + (i + 1) + "/" + n);
                    string line = RunDirectChild(g.AppId, "dry");
                    int total = KeyInt(line, "total");
                    int got = KeyInt(line, "before");
                    string err = KeyStr(line, "err");
                    if (total <= 0)
                    {
                        failN++;
                        typed.Append("✗ " + g.Name + " (" + g.AppId + ")  读不到：" + (err.Length > 0 ? err : "没有成就 / 库里没有这款") + "\r\n");
                    }
                    else
                    {
                        sumGot += got;
                        sumAll += total;
                        if (got >= total) full++;
                        else if (got > 0) part++;
                        else zero++;
                        int pct = (int)Math.Round(got * 100.0 / total);
                        typed.Append("· " + g.Name + " (" + g.AppId + ")  " + got + "/" + total + "  " + pct + "%\r\n");
                        rows.Add(new string[] { Convert.ToString(g.AppId), g.Name, Convert.ToString(got), Convert.ToString(total) });
                    }
                    SamProgress(i + 1, n);
                    SamConsoleSet(typed.ToString());
                }
            }
            catch (Exception e)
            {
                typed.Append("\r\n异常中断：" + e.Message + "\r\n");
            }
            // 按完成度从高到低写 CSV（Excel 可直接打开）
            try
            {
                rows.Sort(delegate(string[] a, string[] b)
                {
                    double pa = 0, pb = 0;
                    try { pa = double.Parse(a[2]) * 100.0 / double.Parse(a[3]); } catch (Exception dg7991_1) { Diag.Swallow("MainForm.Steam.cs:468", dg7991_1); }
                    try { pb = double.Parse(b[2]) * 100.0 / double.Parse(b[3]); } catch (Exception dg7992_1) { Diag.Swallow("MainForm.Steam.cs:469", dg7992_1); }
                    if (pb > pa) return 1;
                    if (pb < pa) return -1;
                    return string.Compare(a[1], b[1], StringComparison.Ordinal);
                });
                StringBuilder csv = new StringBuilder();
                csv.Append("appid,游戏,已解锁,总成就,完成度%\r\n");
                for (int k = 0; k < rows.Count; k++)
                {
                    string[] r = rows[k];
                    double pc = 0;
                    try { pc = double.Parse(r[2]) * 100.0 / double.Parse(r[3]); } catch (Exception dg8003_1) { Diag.Swallow("MainForm.Steam.cs:480", dg8003_1); }
                    csv.Append(r[0] + "," + "\"" + r[1].Replace("\"", "\"\"") + "\"" + "," + r[2] + "," + r[3] + "," + pc.ToString("0.0") + "\r\n");
                }
                File.WriteAllText(Path.Combine(BASE_DIR, "_achievement_progress.csv"), csv.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e) { typed.Append("\r\nCSV 写出失败：" + e.Message + "\r\n"); }
            int totalPct = sumAll > 0 ? (int)Math.Round(sumGot * 100.0 / sumAll) : 0;
            typed.Append("\r\n【汇总】" + (stopped ? "已停止（以下只统计已读的部分）：" : "")
                + "总完成度 " + sumGot + "/" + sumAll + "（" + totalPct + "%）；"
                + "全成就 " + full + " 款 / 有进度 " + part + " 款 / 零进度 " + zero + " 款 / 读不到 " + failN + " 款"
                + "；已读 " + (stopped ? rows.Count + failN : n) + "/" + n + " 款\r\n");
            typed.Append("明细已写出：" + Path.Combine(BASE_DIR, "_achievement_progress.csv") + "（按完成度从高到低，Excel 可直接打开）\r\n");
            if (rows.Count > 0)
            {
                typed.Append("\r\n完成度最高的 10 款：\r\n");
                for (int k = 0; k < rows.Count && k < 10; k++)
                {
                    string[] r = rows[k];
                    typed.Append("  " + (k + 1) + ". " + r[1] + "  " + r[2] + "/" + r[3] + "\r\n");
                }
            }
            SamConsoleSet(typed.ToString());
            SamSetState(stopped ? "统计已停止（再点一次「成就完成度」重新统计）" : "统计完成：总完成度 " + totalPct + "%",
                stopped ? "Scan stopped" : "Scan done: " + totalPct + "%");
            SamProgress(n, n);
            samScanRunning = false;
            samBatchStop = false;
            if (samPctTest)
            {
                try { File.WriteAllText(Path.Combine(BASE_DIR, "_pct_test.txt"), typed.ToString(), Encoding.UTF8); } catch (Exception dg8032_1) { Diag.Swallow("MainForm.Steam.cs:509", dg8032_1); }
                try { Application.Exit(); } catch (Exception dg8033_1) { Diag.Swallow("MainForm.Steam.cs:510", dg8033_1); }
            }
        }

        // 跑一款：拉起自己的子进程（--steam-unlock <appid>），把它的 RESULT 行取回来
        string RunDirectChild(int appid, string mode)
        {
            StringBuilder buf = new StringBuilder();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath,
                    "--steam-unlock " + appid + (mode != null && mode.Length > 0 ? " " + mode : ""));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = BASE_DIR;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (buf) buf.AppendLine(e.Data); }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (buf) buf.AppendLine(e.Data); }
                };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                int waited = 0;
                while (!p.HasExited && waited < 90000 && !samBatchStop)
                {
                    Thread.Sleep(150);
                    waited += 150;
                }
                if (!p.HasExited)
                {
                    try { p.Kill(); } catch (Exception dg8070_1) { Diag.Swallow("MainForm.Steam.cs:547", dg8070_1); }
                    lock (buf) buf.AppendLine("RESULT err=超时未返回");
                }
                try { p.WaitForExit(5000); } catch (Exception dg8073_1) { Diag.Swallow("MainForm.Steam.cs:550", dg8073_1); }
            }
            catch (Exception e)
            {
                lock (buf) buf.AppendLine("RESULT err=" + e.Message);
            }
            string all;
            lock (buf) all = buf.ToString();
            string found = "";
            string[] lines = all.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("RESULT ") >= 0) found = lines[i].Trim();
            }
            if (found.Length == 0)
            {
                string tail = all.Length > 300 ? all.Substring(all.Length - 300) : all;
                found = "RESULT total=0 before=0 set=0 fail=0 after=0 err=" + tail.Replace("\r", " ").Replace("\n", " ");
            }
            SamTrace("direct appid=" + appid + " → " + found);
            return found;
        }

        int KeyInt(string line, string key)
        {
            try
            {
                int i = line.IndexOf(key + "=", StringComparison.Ordinal);
                if (i < 0) return 0;
                int s = i + key.Length + 1;
                int e = s;
                while (e < line.Length && ((line[e] >= '0' && line[e] <= '9') || line[e] == '-')) e++;
                if (e <= s) return 0;
                return int.Parse(line.Substring(s, e - s));
            }
            catch { return 0; }
        }

        string KeyStr(string line, string key)
        {
            try
            {
                int i = line.IndexOf(key + "=", StringComparison.Ordinal);
                if (i < 0) return "";
                return line.Substring(i + key.Length + 1).Trim();
            }
            catch { return ""; }
        }

        // 一键全解锁（全库）：遍历 Steam 库里所有游戏，先判断哪些成就不能解锁，再只解锁能解锁的
        void UnlockAllSam()
        {
            if (samBatchRunning)
            {
                samBatchStop = true;
                SamSetState("正在停止…（当前这一款游戏完成后退出）", "Stopping after current game…");
                return;
            }
            if (samHost == null) return;
            SamShowHostMode();
            // 主路径：按 appid 直启（本地成就缓存里有全部有成就的游戏，不依赖 Picker 列表）
            List<SchemaGame> sgames = ScanSchemaGames();
            if (sgames.Count > 0)
            {
                SamTrace("mode=appid games=" + sgames.Count);
                samBatchRunning = true;
                samBatchStop = false;
                Thread th2 = new Thread(delegate() { SamBatchByAppIdWorker(); });
                th2.IsBackground = true;
                th2.Start();
                return;
            }
            // 兜底：老办法，逐个双击 Picker 列表
            IntPtr pick = FindPickerList();
            if (pick == IntPtr.Zero)
            {
                if (samTestMode) { SamSetState("【自测】未找到游戏列表", "TEST: picker list not found"); return; }
                EnsureSamEmbedded();
                SamSetState("已启动成就管理器列表，等它加载完再点一次「进游戏页解锁（旧）」", "SAM picker launching; press the legacy button again once loaded");
                SamConsoleSet("【旧路径】没有读到 appid 清单，已改为启动成就管理器列表。\r\n等列表加载出来后，再点一次「进游戏页解锁（旧）」。");
                return;
            }
            samBatchRunning = true;
            samBatchStop = false;
            Thread th = new Thread(delegate() { SamBatchWorker(pick); });
            th.IsBackground = true;
            th.Start();
        }

        // 找 SAM.Picker 的游戏列表（标题里没有「|」的那个宿主窗口下的 SysListView32）
        IntPtr FindPickerList()
        {
            IntPtr hostWnd = IntPtr.Zero;
            try
            {
                EnumChildWindows(samHost.Handle, delegate(IntPtr h, IntPtr l)
                {
                    StringBuilder t = new StringBuilder(512);
                    GetWindowText(h, t, 512);
                    string s = t.ToString();
                    if (s.IndexOf("Steam 成就管理器") >= 0 && s.IndexOf("|") < 0)
                    {
                        hostWnd = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception dg8181_1) { Diag.Swallow("MainForm.Steam.cs:658", dg8181_1); }
            if (hostWnd == IntPtr.Zero) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            try
            {
                EnumChildWindows(hostWnd, delegate(IntPtr h, IntPtr l)
                {
                    StringBuilder c = new StringBuilder(256);
                    GetClassName(h, c, 256);
                    if (c.ToString().IndexOf("SysListView32", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception dg8198_1) { Diag.Swallow("MainForm.Steam.cs:675", dg8198_1); }
            return found;
        }

        // 等 SAM.Game 把成就页加载出来并嵌进主界面
        bool WaitSamGameReady(int seconds)
        {
            int ticks = seconds * 4;
            int deadTick = 0;
            for (int i = 0; i < ticks; i++)
            {
                if (samBatchStop) return false;
                // 6 秒后才开始查进程（SAM.Game 冷启动本身要几秒，查太早会误判成"打不开"）
                if (i >= 24 && i % 4 == 0)
                {
                    int alive = 0;
                    try { alive = Process.GetProcessesByName("SAM.Game").Length; } catch (Exception dg8214_1) { Diag.Swallow("MainForm.Steam.cs:691", dg8214_1); }
                    if (alive == 0)
                    {
                        deadTick++;
                        if (deadTick >= 2) return false;   // 连续两次没有进程，才算真打不开
                    }
                    else deadTick = 0;
                }
                try
                {
                    if (samGameHwnd != IntPtr.Zero)
                    {
                        StringBuilder t = new StringBuilder(512);
                        GetWindowText(samGameHwnd, t, 512);
                        if (t.ToString().IndexOf("|") >= 0)
                        {
                            IntPtr lvNow = FindSamListView();
                            if (lvNow != IntPtr.Zero && ReadSamAchRows(lvNow).Count > 0) return true;
                        }
                    }
                }
                catch (Exception dg8235_1) { Diag.Swallow("MainForm.Steam.cs:712", dg8235_1); }
                Thread.Sleep(250);
            }
            try
            {
                StringBuilder t2 = new StringBuilder(512);
                GetWindowText(samGameHwnd, t2, 512);
                SamTrace("waitfail title='" + t2.ToString() + "' parent=" + GetParent(samGameHwnd) +
                         " host=" + (samHost == null ? "null" : samHost.Handle.ToString()) +
                         " lv=" + FindSamListView());
            }
            catch (Exception dg8246_1) { Diag.Swallow("MainForm.Steam.cs:723", dg8246_1); }
            return false;
        }

        // Picker 的列表是虚拟列表：滚动区外的行没有实体对象，双击（accDoDefaultAction）会静默失效。
        // 所以必须先让它滚进可视区，再选中、再双击；失败再重来一轮。
        bool TryPickerDefault(IAccessible acc, int idx)
        {
            try { acc.accDoDefaultAction(idx); return true; } catch (Exception dg8254_1) { Diag.Swallow("MainForm.Steam.cs:731", dg8254_1); }
            try
            {
                object ch = acc.get_accChild(idx);
                if (ch != null) { ((IAccessible)ch).accDoDefaultAction(0); return true; }
            }
            catch (Exception dg8260_1) { Diag.Swallow("MainForm.Steam.cs:737", dg8260_1); }
            return false;
        }

        bool ClickPickerItem(IAccessible acc, IntPtr pickList, int idx)
        {
            // 双击要生效，项必须同时拿到 SELECTED 和 FOCUSED；FOCUSED 只有列表真有输入焦点时才有。
            // 所以先把 Picker 窗口提到前台、把焦点交给列表本身，再滚、再选、再双击。
            try
            {
                IntPtr top = pickList;
                for (int d = 0; d < 6; d++)
                {
                    IntPtr p = GetParent(top);
                    if (p == IntPtr.Zero) break;
                    top = p;
                }
                SetForegroundWindow(top);
                SetFocus(pickList);
                Thread.Sleep(300);
            }
            catch (Exception dg8281_1) { Diag.Swallow("MainForm.Steam.cs:758", dg8281_1); }
            try { SendMessage(pickList, LVM_ENSUREVISIBLE, (IntPtr)(idx - 1), IntPtr.Zero); } catch (Exception dg8282_1) { Diag.Swallow("MainForm.Steam.cs:759", dg8282_1); }
            Thread.Sleep(300);
            try { acc.accSelect(2, idx); } catch (Exception dg8284_1) { Diag.Swallow("MainForm.Steam.cs:761", dg8284_1); }
            Thread.Sleep(150);
            try { acc.accSelect(3, idx); } catch (Exception dg8286_1) { Diag.Swallow("MainForm.Steam.cs:763", dg8286_1); }
            Thread.Sleep(300);
            if (TryPickerDefault(acc, idx)) return true;
            // 第二轮：再滚一次、再选一次、再双击
            SamTrace("click-retry " + idx);
            try { SendMessage(pickList, LVM_ENSUREVISIBLE, (IntPtr)(idx - 1), IntPtr.Zero); } catch (Exception dg8291_1) { Diag.Swallow("MainForm.Steam.cs:768", dg8291_1); }
            Thread.Sleep(600);
            try { acc.accSelect(2, idx); } catch (Exception dg8293_1) { Diag.Swallow("MainForm.Steam.cs:770", dg8293_1); }
            Thread.Sleep(200);
            try { acc.accSelect(3, idx); } catch (Exception dg8295_1) { Diag.Swallow("MainForm.Steam.cs:772", dg8295_1); }
            Thread.Sleep(400);
            return TryPickerDefault(acc, idx);
        }

        // ============ 按 appid 直启批量：绕开 Picker 虚拟列表 ============
        // 本地 appcache\stats\UserGameStatsSchema_<appid>.bin 里有成就定义和 gamename，
        // 用它拿到「所有真正有成就的游戏」，再直接 SAM.Game.exe <appid>，不依赖 Picker 双击。
        class SchemaGame
        {
            public int AppId;
            public string Name;
            public int Protected;
        }

        // 扫描阶段的结果：每款游戏到底有多少成就是已经解锁的、多少还锁着
        class ScanItem
        {
            public int AppId;
            public string Name;
            public int Total;
            public int Unlocked;
            public int Pending;
            public string Flag;   // ok / noach / openfail / loadfail
        }

        int IndexOfBytes(byte[] d, string s)
        {
            byte[] pat = Encoding.ASCII.GetBytes(s);
            for (int i = 0; i + pat.Length <= d.Length; i++)
            {
                bool m = true;
                for (int j = 0; j < pat.Length; j++)
                {
                    if (d[i + j] != pat[j]) { m = false; break; }
                }
                if (m) return i;
            }
            return -1;
        }

        string ReadSchemaName(byte[] d)
        {
            int gi = IndexOfBytes(d, "gamename");
            if (gi < 0) return null;
            int p = gi + 8;
            while (p < d.Length && d[p] == 0) p++;
            int q = p;
            while (q < d.Length && d[q] != 0) q++;
            if (q <= p) return null;
            try { return Encoding.UTF8.GetString(d, p, q - p); }
            catch { return null; }
        }

        string GetSteamInstallPath()
        {
            string steamPath = "d:/myq/steam";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k != null)
                    {
                        string sp = Convert.ToString(k.GetValue("SteamPath"));
                        if (sp != null && sp.Length > 0) steamPath = sp;
                    }
                }
            }
            catch (Exception dg8363_1) { Diag.Swallow("MainForm.Steam.cs:840", dg8363_1); }
            return steamPath;
        }

        // ---------- 通用：带超时的 HTTP 取文本（任何一步卡住都不会把线程吊死）----------
        string HttpGetText(string url, string referer, int timeoutMs, int retries)
        {
            Exception last = null;
            for (int t = 0; t <= (retries < 0 ? 0 : retries); t++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = timeoutMs;
                    req.ReadWriteTimeout = timeoutMs;
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    req.AllowAutoRedirect = true;
                    if (referer != null && referer.Length > 0) req.Referer = referer;
                    try { req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
                    catch (Exception dg8382_1) { Diag.Swallow("MainForm.Steam.cs:859", dg8382_1); }
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (t < retries) { try { Thread.Sleep(400); } catch (Exception dg8392_1) { Diag.Swallow("MainForm.Steam.cs:869", dg8392_1); } }
                }
            }
            throw last;
        }

        // ---------- 通用：读图不锁文件（先读字节再解码，用户原图不会被占用）----------
        Image SafeLoadImage(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image tmp = Image.FromStream(ms))
            {
                Bitmap copy = new Bitmap(tmp.Width, tmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImageUnscaled(tmp, 0, 0);
                }
                return copy;
            }
        }

        List<SchemaGame> ScanSchemaGames()
        {
            // 5 分钟内复用上一次的扫描结果：成就页反复点按钮不用每次都重读几百个 schema 文件
            if (schemaCache != null && (DateTime.Now - schemaCacheTime).TotalSeconds < 300) return schemaCache;
            List<SchemaGame> list = new List<SchemaGame>();
            try
            {
                string stats = Path.Combine(GetSteamInstallPath(), "appcache\\stats");
                if (!Directory.Exists(stats)) return list;
                foreach (string f in Directory.GetFiles(stats, "UserGameStatsSchema_*.bin"))
                {
                    string fn = Path.GetFileNameWithoutExtension(f);
                    if (fn == null || fn.Length <= "UserGameStatsSchema_".Length) continue;
                    string idStr = fn.Substring("UserGameStatsSchema_".Length);
                    int appid;
                    if (!int.TryParse(idStr, out appid)) continue;
                    try
                    {
                        byte[] d = File.ReadAllBytes(f);
                        string name = ReadSchemaName(d);
                        if (name == null || name.Length == 0) continue;
                        if (name.StartsWith("ValveTestApp")) continue;
                        SchemaGame g = new SchemaGame();
                        g.AppId = appid;
                        g.Name = name;
                        g.Protected = CountBinaryHits(d, "\u0002protected\u0000\u0001\u0000\u0000\u0000");
                        list.Add(g);
                    }
                    catch (Exception dg8443_1) { Diag.Swallow("MainForm.Steam.cs:920", dg8443_1); }
                }
            }
            catch (Exception dg8446_1) { Diag.Swallow("MainForm.Steam.cs:923", dg8446_1); }
            list.Sort(delegate(SchemaGame a, SchemaGame b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (list.Count > 0) { schemaCache = list; schemaCacheTime = DateTime.Now; }
            return list;
        }

        List<SchemaGame> schemaCache = null;
        DateTime schemaCacheTime = DateTime.MinValue;

        int CountBinaryHits(byte[] d, string s)
        {
            // s 里用 \u0000 表示 NUL，逐段转成字节后做整体匹配
            byte[] pat = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) pat[i] = (byte)s[i];
            int count = 0;
            for (int i = 0; i + pat.Length <= d.Length; i++)
            {
                bool m = true;
                for (int j = 0; j < pat.Length; j++)
                {
                    if (d[i + j] != pat[j]) { m = false; break; }
                }
                if (m) { count++; i += pat.Length - 1; }
            }
            return count;
        }

        // 直启一个 appid 的 SAM.Game，并把它嵌入主界面，返回是否成功
        bool OpenSamGameByAppId(int appid, out Process proc)
        {
            proc = null;
            samGameHwnd = IntPtr.Zero;   // 必须先清掉上一款的句柄，否则会误判成「这一款已就绪」
            // SAM.Game 是单实例的，残留实例会让新进程拿不到窗口，先清干净
            try
            {
                Process[] old = Process.GetProcessesByName("SAM.Game");
                for (int q = 0; q < old.Length; q++)
                {
                    try { if (!old[q].HasExited) old[q].Kill(); } catch (Exception dg8487_1) { Diag.Swallow("MainForm.Steam.cs:964", dg8487_1); }
                }
                if (old.Length > 0) Thread.Sleep(2500);
                else Thread.Sleep(1000);   // 即使没残留也稍等一下，给上一款的单实例锁留释放时间
            }
            catch (Exception dg8492_1) { Diag.Swallow("MainForm.Steam.cs:969", dg8492_1); }
            try
            {
                string samDir = Path.Combine(BASE_DIR, "sam_tool");
                string exe = Path.Combine(samDir, "SAM.Game.exe");
                if (!File.Exists(exe)) return false;
                proc = Process.Start(new ProcessStartInfo(exe)
                {
                    Arguments = appid.ToString(),
                    WorkingDirectory = samDir
                });
                if (proc == null) return false;
                for (int t = 0; t < 40; t++)
                {
                    if (proc.HasExited)
                    {
                        SamTrace("openexited appid=" + appid);
                        return false;
                    }
                    proc.Refresh();
                    IntPtr mh = proc.MainWindowHandle;
                    if (mh != IntPtr.Zero)
                    {
                        StringBuilder cn = new StringBuilder(64);
                        GetClassName(mh, cn, 64);
                        if (cn.ToString() == "#32770")
                        {
                            // 启动时弹的是错误提示框，不是成就窗口：关掉它继续等真正的主窗口
                            PostMessage(mh, 0x0010, IntPtr.Zero, IntPtr.Zero);
                            SamTrace("openerr-dlg appid=" + appid);
                            Thread.Sleep(600);
                            continue;
                        }
                        return EmbedInto(proc, samHost) != IntPtr.Zero;
                    }
                    Thread.Sleep(300);
                }
            }
            catch (Exception dg8530_1) { Diag.Swallow("MainForm.Steam.cs:1007", dg8530_1); }
            return false;
        }

        // 关掉一个 SAM.Game 实例并等它真正退出，避免影响下一次
        void CloseSamGame(Process p)
        {
            try
            {
                if (p != null && !p.HasExited) p.Kill();
            }
            catch (Exception dg8541_1) { Diag.Swallow("MainForm.Steam.cs:1018", dg8541_1); }
            try
            {
                // SAM.Game 是单实例的：上一个没退干净，新进程会拿不到窗口
                for (int w = 0; w < 40; w++)
                {
                    Process[] all = Process.GetProcessesByName("SAM.Game");
                    if (all.Length == 0) break;
                    for (int q = 0; q < all.Length; q++)
                    {
                        try { if (!all[q].HasExited) all[q].Kill(); } catch (Exception dg8551_1) { Diag.Swallow("MainForm.Steam.cs:1028", dg8551_1); }
                    }
                    Thread.Sleep(200);
                }
                samGameHwnd = IntPtr.Zero;
                Thread.Sleep(3500);   // 进程没了还要等它释放单实例锁，否则下一款会卡死拿不到窗口
            }
            catch (Exception dg8558_1) { Diag.Swallow("MainForm.Steam.cs:1035", dg8558_1); }
        }

        // 关掉当前 SAM.Game 并把 Picker 恢复出来，准备下一款游戏
        void ResetPickerAfterGame()
        {
            SamTrace("reset:start");
            try
            {
                Process[] gs = Process.GetProcessesByName("SAM.Game");
                SamTrace("reset:sams=" + gs.Length);
                foreach (Process g in gs)
                {
                    try { g.Kill(); SamTrace("reset:kill " + g.Id); } catch (Exception ex) { SamTrace("reset:killerr " + ex.Message); }
                }
                // 必须等进程真正消失：残留的 SAM.Game 会让 Picker 认为上一款还在跑，下一次双击就被吞掉
                for (int w = 0; w < 20; w++)
                {
                    if (Process.GetProcessesByName("SAM.Game").Length == 0) break;
                    Thread.Sleep(300);
                }
            }
            catch (Exception ex) { SamTrace("reset:enum " + ex.Message); }
            Thread.Sleep(1400);
            samGameHwnd = IntPtr.Zero;
            try
            {
                Process[] ps = Process.GetProcessesByName("SAM.Picker");
                SamTrace("reset:pickers=" + ps.Length);
                if (ps.Length > 0)
                {
                    procSam = ps[0];
                    IntPtr h = ps[0].MainWindowHandle;
                    if (h != IntPtr.Zero) ShowWindow(h, SW_SHOW);
                }
            }
            catch (Exception ex) { SamTrace("reset:pickererr " + ex.Message); }
            SamTrace("reset:done");
        }

        // 报告即时落盘：跑批中途被中断也不会丢已有结果
        void FlushBatchReport(StringBuilder sb)
        {
            try { File.WriteAllText(Path.Combine(BASE_DIR, "_batch_report.txt"), sb.ToString(), new UTF8Encoding(false)); } catch (Exception dg8601_1) { Diag.Swallow("MainForm.Steam.cs:1078", dg8601_1); }
            SamConsoleSet(sb.ToString());   // 一体式：报告落盘的同时镜像到页内控制台
        }

        // ============ 扫描阶段：只读每一款游戏的真实状态，不改任何成就 ============
        ScanItem ScanOneGame(SchemaGame g)
        {
            ScanItem it = new ScanItem();
            it.AppId = g.AppId;
            it.Name = g.Name;
            it.Flag = "ok";
            Process p = null;
            if (!OpenSamGameByAppId(g.AppId, out p))
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    SamTrace("open-retry " + g.AppId + " #" + (attempt + 1));
                    Thread.Sleep(2500);
                    if (OpenSamGameByAppId(g.AppId, out p)) break;
                }
                if (p == null || samGameHwnd == IntPtr.Zero)
                {
                    it.Flag = "openfail";
                    CloseSamGame(p);
                    return it;
                }
            }
            bool readyNow = WaitSamGameReady(samTestMode ? 15 : 10);
            if (!readyNow && !samTestMode)
            {
                SamTrace("ready-wait2 " + g.AppId);
                Thread.Sleep(6000);
                IntPtr lvq = FindSamListView();
                if (lvq != IntPtr.Zero && ReadSamAchRows(lvq).Count > 0) readyNow = true;
            }
            if (!readyNow)
            {
                it.Flag = "loadfail";
                CloseSamGame(p);
                return it;
            }
            IntPtr lv = FindSamListView();
            List<SamAch> rows = ReadSamAchRows(lv);
            SamTrace("scan-rows=" + rows.Count);
            if (rows.Count == 0)
            {
                it.Flag = "noach";
                CloseSamGame(p);
                return it;
            }
            int u = 0;
            for (int k = 0; k < rows.Count; k++) if (rows[k].Unlocked) u++;
            it.Total = rows.Count;
            it.Unlocked = u;
            it.Pending = rows.Count - u;
            CloseSamGame(p);
            return it;
        }

        string ScanLine(ScanItem it)
        {
            return it.AppId + "\t" + (it.Name == null ? "" : it.Name.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ")) +
                   "\t" + it.Total + "\t" + it.Unlocked + "\t" + it.Pending + "\t" + it.Flag;
        }

        List<ScanItem> LoadScanList(string path)
        {
            List<ScanItem> list = new List<ScanItem>();
            try
            {
                if (!File.Exists(path)) return list;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int k = 0; k < lines.Length; k++)
                {
                    string ln = lines[k];
                    if (ln == null || ln.Trim().Length == 0) continue;
                    string[] f = ln.Split('\t');
                    if (f.Length < 6) continue;
                    int appid, total, unlocked, pending;
                    if (!int.TryParse(f[0], out appid)) continue;
                    if (!int.TryParse(f[2], out total)) continue;
                    if (!int.TryParse(f[3], out unlocked)) continue;
                    if (!int.TryParse(f[4], out pending)) continue;
                    ScanItem it = new ScanItem();
                    it.AppId = appid;
                    it.Name = f[1];
                    it.Total = total;
                    it.Unlocked = unlocked;
                    it.Pending = pending;
                    it.Flag = f[5];
                    list.Add(it);
                }
            }
            catch (Exception dg8694_1) { Diag.Swallow("MainForm.Steam.cs:1171", dg8694_1); }
            return list;
        }

        void WriteScanList(string path, List<ScanItem> items)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int k = 0; k < items.Count; k++) sb.Append(ScanLine(items[k])).Append("\r\n");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg8706_1) { Diag.Swallow("MainForm.Steam.cs:1183", dg8706_1); }
        }

        // 关掉 SAM 弹出的「该成就受保护，无法通过本工具修改」提示框
        // 这个框是模态的，不关掉会挡住后面的「提交修改」，同时它本身就是受保护的信号
        int CloseSamPopups()
        {
            int closed = 0;
            for (int round = 0; round < 60; round++)
            {
                IntPtr dlg = IntPtr.Zero;
                try
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        StringBuilder cn = new StringBuilder(64);
                        GetClassName(h, cn, 64);
                        if (cn.ToString() != "#32770") return true;
                        StringBuilder tt = new StringBuilder(256);
                        GetWindowText(h, tt, 256);
                        string t = tt.ToString();
                        if (t.IndexOf("错误") < 0 && t.IndexOf("Error", StringComparison.OrdinalIgnoreCase) < 0) return true;
                        dlg = h;
                        return false;
                    }, IntPtr.Zero);
                }
                catch { break; }
                if (dlg == IntPtr.Zero) break;
                PostMessage(dlg, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE = 点掉提示框
                closed++;
                Thread.Sleep(300);
            }
            return closed;
        }

        // 全库批量（直启版）：按 appid 逐个打开 SAM.Game → 判断受保护 → 解锁能解锁的 → 提交
        // 这是主路径：不依赖 Picker 虚拟列表，因此不存在「滚动区外双击被吞」的问题
        // 全库批量（两阶段版）：
        // 第一步 全库扫描：只读每一款的状态，把「已经全解锁的」剔掉，把所有还有未解锁成就的游戏列出来
        // 第二步 对着清单逐个解锁：能解的解，整款受保护的直接跳过、不提交
        void SamBatchByAppIdWorker()
        {
            StringBuilder report = new StringBuilder();
            int noAch = 0, already = 0, okGames = 0, failGames = 0, totalGain = 0, protSkip = 0, protAch = 0;
            bool completedAll = false;
            int lastI = 0;
            string stage = "scan";
            try
            {
                // 跑批期间用不到 Picker，但它会占住 Steam 上下文，导致后面的 SAM.Game 起不来
                try
                {
                    Process[] pk = Process.GetProcessesByName("SAM.Picker");
                    for (int q = 0; q < pk.Length; q++) { try { if (!pk[q].HasExited) pk[q].Kill(); } catch (Exception dg8759_1) { Diag.Swallow("MainForm.Steam.cs:1236", dg8759_1); } }
                    if (pk.Length > 0) Thread.Sleep(2500);
                }
                catch (Exception dg8762_1) { Diag.Swallow("MainForm.Steam.cs:1239", dg8762_1); }
                List<SchemaGame> games = ScanSchemaGames();
                if (samOnlyAppId > 0)
                {
                    List<SchemaGame> one = new List<SchemaGame>();
                    for (int k = 0; k < games.Count; k++) if (games[k].AppId == samOnlyAppId) one.Add(games[k]);
                    games = one;
                    if (games.Count == 0)
                    {
                        SamSetState("没有找到 appid=" + samOnlyAppId + " 的成就定义", "No schema for this appid");
                        return;
                    }
                }
                int n = games.Count;
                if (n == 0)
                {
                    SamSetState("Steam 本地没有找到成就定义文件，请先启动 Steam 登录一次再试。",
                        "No achievement schema found; start Steam and sign in once, then retry.");
                    return;
                }

                string scanPath = Path.Combine(BASE_DIR, "_scan_list.txt");
                string scanCursorPath = Path.Combine(BASE_DIR, "_scan_cursor.txt");
                List<ScanItem> items = new List<ScanItem>();
                bool scanDone = false;

                // ---------- 第一步：全库扫描 ----------
                if (samOnlyAppId == 0)
                {
                    if (File.Exists(scanPath))
                    {
                        List<ScanItem> cached = LoadScanList(scanPath);
                        if (cached.Count == n) { items = cached; scanDone = true; }
                    }
                    if (!scanDone)
                    {
                        int scanStart = 0;
                        try
                        {
                            if (File.Exists(scanCursorPath))
                            {
                                string[] parts = File.ReadAllText(scanCursorPath).Trim().Split('|');
                                if (parts.Length == 2 && IsAllDigits(parts[0]) && IsAllDigits(parts[1]))
                                {
                                    int done = int.Parse(parts[0]);
                                    int total = int.Parse(parts[1]);
                                    if (total == n && done >= 0 && done <= n) scanStart = done;
                                }
                            }
                        }
                        catch (Exception dg8812_1) { Diag.Swallow("MainForm.Steam.cs:1289", dg8812_1); }
                        if (scanStart > 0) items = LoadScanList(scanPath);

                        report.Append("【第一步】全库扫描：共 " + n + " 个有成就定义的游戏，只读取状态，不改动任何成就\r\n");
                        if (scanStart > 0) report.Append("续扫：上次已扫完 " + scanStart + " 个，从第 " + (scanStart + 1) + " 个继续\r\n");
                        report.Append("说明：扫描阶段每款都要打开一次成就页，扫完才会开始解锁\r\n\r\n");
                        FlushBatchReport(report);

                        for (int i = scanStart; i < n; i++)
                        {
                            if (samBatchStop) break;
                            lastI = i;
                            try { File.WriteAllText(scanCursorPath, i + "|" + n, new UTF8Encoding(false)); } catch (Exception dg8824_1) { Diag.Swallow("MainForm.Steam.cs:1301", dg8824_1); }
                            SchemaGame g = games[i];
                            SamSetState("【扫描 " + (i + 1) + "/" + n + "】" + g.Name + " — 正在读取成就状态…",
                                "Scanning " + (i + 1) + "/" + n + ": " + g.Name);
                            ScanItem it = ScanOneGame(g);
                            items.Add(it);
                            WriteScanList(scanPath, items);
                        }
                        if (!samBatchStop)
                        {
                            scanDone = true;
                            try { File.Delete(scanCursorPath); } catch (Exception dg8835_1) { Diag.Swallow("MainForm.Steam.cs:1312", dg8835_1); }
                        }
                    }
                }
                else
                {
                    ScanItem oneItem = ScanOneGame(games[0]);
                    items.Add(oneItem);
                    scanDone = true;
                }

                // ---------- 汇总扫描结果，列出所有还没解锁的游戏 ----------
                if (scanDone)
                {
                    int alreadyCnt = 0, pendCnt = 0, badCnt = 0, pendAchTotal = 0;
                    List<ScanItem> todo = new List<ScanItem>();
                    for (int k = 0; k < items.Count; k++)
                    {
                        ScanItem it = items[k];
                        if (it.Flag == "openfail" || it.Flag == "loadfail" || it.Flag == "noach") { badCnt++; continue; }
                        if (it.Pending <= 0) { alreadyCnt++; continue; }
                        todo.Add(it);
                        pendCnt++;
                        pendAchTotal += it.Pending;
                    }

                    report.Append("\r\n【扫描完成】共 " + items.Count + " 个游戏\r\n");
                    report.Append("  已全解锁（不处理）：" + alreadyCnt + " 个\r\n");
                    report.Append("  还有未解锁成就：" + pendCnt + " 个，合计 " + pendAchTotal + " 个成就待解锁\r\n");
                    report.Append("  打不开/读不到：" + badCnt + " 个\r\n");
                    if (pendCnt == 0)
                    {
                        report.Append("\r\n没有需要解锁的游戏，全部已经解锁完成。\r\n");
                    }
                    else
                    {
                        report.Append("\r\n【待解锁清单】\r\n");
                        for (int k = 0; k < todo.Count; k++)
                        {
                            ScanItem it = todo[k];
                            report.Append("  " + (k + 1) + ". " + it.Name + " (" + it.AppId + ")  待解锁 " +
                                          it.Pending + " / 共 " + it.Total + "\r\n");
                        }
                        report.Append("\r\n【第二步】开始逐个解锁（整款受保护的会直接跳过，不提交）\r\n");
                    }
                    FlushBatchReport(report);

                    // ---------- 第二步：对着清单逐个解锁 ----------
                    if (pendCnt > 0 && !samTestMode)
                    {
                        stage = "unlock";
                        int startU = 0;
                        try
                        {
                            string cp = Path.Combine(BASE_DIR, "_batch_cursor.txt");
                            if (File.Exists(cp))
                            {
                                string[] parts = File.ReadAllText(cp).Trim().Split('|');
                                if (parts.Length == 2 && IsAllDigits(parts[0]) && IsAllDigits(parts[1]))
                                {
                                    int done = int.Parse(parts[0]);
                                    int total = int.Parse(parts[1]);
                                    if (total == todo.Count && done >= 0 && done <= todo.Count) startU = done;
                                }
                            }
                        }
                        catch (Exception dg8901_1) { Diag.Swallow("MainForm.Steam.cs:1378", dg8901_1); }

                        for (int k = startU; k < todo.Count; k++)
                        {
                            if (samBatchStop) break;
                            lastI = k;
                            try { File.WriteAllText(Path.Combine(BASE_DIR, "_batch_cursor.txt"), k + "|" + todo.Count, new UTF8Encoding(false)); } catch (Exception dg8907_1) { Diag.Swallow("MainForm.Steam.cs:1384", dg8907_1); }
                            ScanItem it = todo[k];
                            SchemaGame g = new SchemaGame();
                            g.AppId = it.AppId;
                            g.Name = it.Name;
                            SamTrace("unlock " + (k + 1) + "/" + todo.Count + " appid=" + g.AppId + " name=" + g.Name + " pending=" + it.Pending);
                            SamSetState("【解锁 " + (k + 1) + "/" + todo.Count + "】" + g.Name + " — 待解锁 " + it.Pending + " 个…",
                                "Unlocking " + (k + 1) + "/" + todo.Count + ": " + g.Name);

                            Process p = null;
                            if (!OpenSamGameByAppId(g.AppId, out p))
                            {
                                for (int attempt = 0; attempt < 4; attempt++)
                                {
                                    SamTrace("open-retry " + g.AppId + " #" + (attempt + 1));
                                    Thread.Sleep(2500);
                                    if (OpenSamGameByAppId(g.AppId, out p)) break;
                                }
                                if (p == null || samGameHwnd == IntPtr.Zero)
                                {
                                    report.Append("SKIP 打不开 " + g.Name + " (" + g.AppId + ")\r\n");
                                    FlushBatchReport(report);
                                    noAch++;
                                    CloseSamGame(p);
                                    continue;
                                }
                            }
                            bool readyNow = WaitSamGameReady(12);
                            if (!readyNow)
                            {
                                SamTrace("ready-wait2 " + g.AppId);
                                Thread.Sleep(8000);
                                IntPtr lvq = FindSamListView();
                                if (lvq != IntPtr.Zero && ReadSamAchRows(lvq).Count > 0) readyNow = true;
                            }
                            if (!readyNow)
                            {
                                report.Append("SKIP 成就页没加载出来 " + g.Name + " (" + g.AppId + ")\r\n");
                                FlushBatchReport(report);
                                noAch++;
                                CloseSamGame(p);
                                continue;
                            }
                            IntPtr lv = FindSamListView();
                            List<SamAch> rows = ReadSamAchRows(lv);
                            if (rows.Count == 0)
                            {
                                report.Append("SKIP 无成就 " + g.Name + "\r\n");
                                FlushBatchReport(report);
                                noAch++;
                                CloseSamGame(p);
                                continue;
                            }
                            int unlocked = 0;
                            for (int q = 0; q < rows.Count; q++) if (rows[q].Unlocked) unlocked++;
                            if (unlocked >= rows.Count)
                            {
                                report.Append("SKIP 已全解锁 " + g.Name + " (" + rows.Count + ")\r\n");
                                FlushBatchReport(report);
                                already++;
                                CloseSamGame(p);
                                continue;
                            }
                            int pending = rows.Count - unlocked;
                            if (!InvokeSamButton(samGameHwnd, "解锁全部", "Unlock All"))
                            {
                                report.Append("FAIL 无法点解锁全部 " + g.Name + "\r\n");
                                FlushBatchReport(report);
                                failGames++;
                                CloseSamGame(p);
                                continue;
                            }
                            int popups = 0;
                            for (int w = 0; w < 28; w++)
                            {
                                popups += CloseSamPopups();
                                Thread.Sleep(500);
                            }
                            if (popups > 0) SamTrace("popups=" + popups);
                            List<SamAch> mid = ReadSamAchRows(lv);
                            int checkedNow = 0;
                            for (int q = 0; q < mid.Count; q++) if (mid[q].Unlocked) checkedNow++;
                            if (checkedNow <= unlocked && popups == 0)
                            {
                                Thread.Sleep(6000);
                                mid = ReadSamAchRows(lv);
                                checkedNow = 0;
                                for (int q = 0; q < mid.Count; q++) if (mid[q].Unlocked) checkedNow++;
                            }
                            if (checkedNow <= unlocked)
                            {
                                protSkip++;
                                protAch += pending;
                                report.Append("SKIP 整款受保护 " + g.Name + "（" + pending + " 个受保护成就已跳过，未提交）\r\n");
                                FlushBatchReport(report);
                                CloseSamGame(p);
                                continue;
                            }
                            CloseSamPopups();
                            ClickSamSubmit(samGameHwnd);
                            Thread.Sleep(4000);
                            CloseSamPopups();
                            InvokeSamButton(samGameHwnd, "刷新", "Refresh");
                            Thread.Sleep(6000);
                            List<SamAch> after = ReadSamAchRows(lv);
                            int finalU = 0;
                            for (int q = 0; q < after.Count; q++) if (after[q].Unlocked) finalU++;
                            int gain = finalU - unlocked;
                            if (gain > 0) totalGain += gain;
                            if (finalU >= after.Count)
                            {
                                okGames++;
                                report.Append("OK " + g.Name + " +" + gain + "\r\n");
                                FlushBatchReport(report);
                            }
                            else
                            {
                                int left = after.Count - finalU;
                                protAch += left;
                                failGames++;
                                report.Append("PART " + g.Name + " +" + gain + "，受保护/校验未通过 " + left + " 个已跳过\r\n");
                                FlushBatchReport(report);
                            }
                            CloseSamGame(p);
                        }
                        stage = "done";
                    }
                    else
                    {
                        stage = "done";
                    }
                }
                SamTrace("loopfinished");
                completedAll = !samBatchStop;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[sam-batch-id] " + ex.Message + "\r\n"); } catch (Exception dg9044_1) { Diag.Swallow("MainForm.Steam.cs:1521", dg9044_1); }
                report.Append("ERROR " + ex.Message + "\r\n");
                SamTrace("exception " + ex.Message);
            }
            finally
            {
                samBatchRunning = false;
                try
                {
                    string head;
                    if (completedAll && stage == "done")
                    {
                        head = "全库处理结束：解锁成功 " + okGames + " 个游戏、部分未解锁 " + failGames +
                               " 个、本来就全解锁 " + already + " 个、无成就/打不开 " + noAch + " 个、整款受保护跳过 " + protSkip +
                               " 个；本次共解锁 " + totalGain + " 个成就，受保护/未写入 " + protAch + " 个。";
                        try { File.Delete(Path.Combine(BASE_DIR, "_batch_cursor.txt")); } catch (Exception dg9059_1) { Diag.Swallow("MainForm.Steam.cs:1536", dg9059_1); }
                    }
                    else if (stage == "scan")
                    {
                        head = "扫描阶段已暂停：扫到第 " + (lastI + 1) + " 项，进度已保存，再点按钮从断点继续。";
                    }
                    else
                    {
                        head = "解锁阶段已暂停：处理到第 " + (lastI + 1) + " 项，进度已保存，再点按钮从断点继续，不用重新扫描。";
                    }
                    SamSetState(head, completedAll && stage == "done"
                        ? "Done: " + okGames + " ok, " + protSkip + " protected-only, +" + totalGain + " unlocked"
                        : "Paused; click again to resume");
                    FlushBatchReport(report);
                }
                catch (Exception dg9074_1) { Diag.Swallow("MainForm.Steam.cs:1551", dg9074_1); }
            }
        }

        // 全库批量：逐个游戏 → 判断能否解锁 → 解锁能解锁的 → 提交 → 记录
        void SamBatchWorker(IntPtr pickList)
        {
            StringBuilder report = new StringBuilder();
            int noAch = 0, already = 0, okGames = 0, failGames = 0, totalGain = 0, protSkip = 0, protAch = 0;
            bool completedAll = false;
            int lastI = 0;
            try
            {
                Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                object accObj = null;
                int hr = AccessibleObjectFromWindow(pickList, 0xFFFFFFFC, ref iid, out accObj);
                if (hr != 0 || accObj == null)
                {
                    SamSetState("无法读取游戏列表", "Cannot read the game list");
                    return;
                }
                IAccessible acc = (IAccessible)accObj;
                int n = acc.accChildCount;
                for (int w = 0; w < 120 && n == 0 && !samBatchStop; w++)
                {
                    Thread.Sleep(250);
                    n = acc.accChildCount;
                }
                if (n == 0)
                {
                    SamSetState("游戏列表还没加载出内容，请稍后再点一次。", "The game list is empty; try again later.");
                    return;
                }
                int limit = samTestMode ? Math.Min(n, 2) : n;
                int onlyIndex = 0;
                if (samTestMode)
                {
                    // 自测：优先挑已知有未解锁成就的样本
                    for (int q = 1; q <= n; q++)
                    {
                        try
                        {
                            string qn = acc.get_accName(q);
                            if (qn != null && qn.IndexOf("100 Ninja Cats", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                onlyIndex = q;
                                break;
                            }
                        }
                        catch (Exception dg9123_1) { Diag.Swallow("MainForm.Steam.cs:1600", dg9123_1); }
                    }
                }
                report.Append("游戏总数 " + n + "，本次处理 " + limit + " 个，样本索引 " + onlyIndex + "\r\n");
                int startI = onlyIndex > 0 ? onlyIndex : 1;
                int endI = onlyIndex > 0 ? onlyIndex : limit;

                // 断点续跑：上次没跑完的，这次接着来，不用从头再来一遍
                if (!samTestMode)
                {
                    int resumeAt = 0;
                    try
                    {
                        if (File.Exists(Path.Combine(BASE_DIR, "_batch_cursor.txt")))
                        {
                            string[] c = File.ReadAllText(Path.Combine(BASE_DIR, "_batch_cursor.txt")).Trim().Split('|');
                            if (c.Length >= 2 && c[1].Trim() == n.ToString())
                            {
                                int.TryParse(c[0].Trim(), out resumeAt);
                            }
                        }
                    }
                    catch (Exception dg9145_1) { Diag.Swallow("MainForm.Steam.cs:1622", dg9145_1); }
                    if (resumeAt > 0 && resumeAt < n)
                    {
                        startI = resumeAt + 1;
                        report.Append("续跑：上次已完成 " + resumeAt + " 项，从第 " + startI + " 项继续\r\n");
                        SamSetState("上次没跑完，从第 " + startI + " 项接着跑…",
                            "Resuming from item #" + startI + " of " + n);
                    }
                    if (startI < 1) startI = 1;
                    if (startI > n) startI = 1;
                }

                for (int i = startI; i <= endI; i++)
                {
                    if (samBatchStop) break;
                    lastI = i;
                    if (!samTestMode)
                    {
                        // 记住进度 + 把已出的结果落盘，中途崩溃也不丢
                        try { File.WriteAllText(Path.Combine(BASE_DIR, "_batch_cursor.txt"), (i - 1) + "|" + n); } catch (Exception dg9164_1) { Diag.Swallow("MainForm.Steam.cs:1641", dg9164_1); }
                        try { FlushBatchReport(report); } catch (Exception dg9165_1) { Diag.Swallow("MainForm.Steam.cs:1642", dg9165_1); }
                        // 清掉上款可能残留的 SAM.Game，避免句柄串到这一款
                        if (i > startI)
                        {
                            try
                            {
                                foreach (Process og in Process.GetProcessesByName("SAM.Game"))
                                {
                                    try { og.Kill(); } catch (Exception dg9173_1) { Diag.Swallow("MainForm.Steam.cs:1650", dg9173_1); }
                                }
                            }
                            catch (Exception dg9176_1) { Diag.Swallow("MainForm.Steam.cs:1653", dg9176_1); }
                        }
                    }
                    if (samBatchStop) break;
                    string nm = "";
                    try { nm = acc.get_accName(i); } catch { nm = ""; }
                    SamSetState("[" + i + "/" + limit + "] " + nm + " — 正在加载成就…",
                        "[" + i + "/" + limit + "] loading achievements…");
                    bool clicked = false;
                    try { clicked = ClickPickerItem(acc, pickList, i); }
                    catch (Exception ex) { SamTrace("clickerr " + ex.Message); }
                    SamTrace("loop " + i + " name=" + nm + " clicked=" + clicked);
                    if (!clicked)
                    {
                        report.Append("SKIP 打不开 " + nm + "\r\n");
                        noAch++;
                        ResetPickerAfterGame();
                        continue;
                    }
                    bool readyNow = WaitSamGameReady(samTestMode ? 15 : 14);
                    SamTrace("ready=" + readyNow + " gameHwnd=" + samGameHwnd);
                    if (!readyNow && !samTestMode)
                    {
                        // 第一次没起来不代表这款打不开：清掉残留、再滚一次、再双击一次
                        SamTrace("ready-retry " + i);
                        try
                        {
                            foreach (Process og in Process.GetProcessesByName("SAM.Game"))
                            {
                                try { og.Kill(); } catch (Exception dg9205_1) { Diag.Swallow("MainForm.Steam.cs:1682", dg9205_1); }
                            }
                        }
                        catch (Exception dg9208_1) { Diag.Swallow("MainForm.Steam.cs:1685", dg9208_1); }
                        Thread.Sleep(900);
                        try { SendMessage(pickList, LVM_ENSUREVISIBLE, (IntPtr)(i - 1), IntPtr.Zero); } catch (Exception dg9210_1) { Diag.Swallow("MainForm.Steam.cs:1687", dg9210_1); }
                        Thread.Sleep(700);
                        try { TryPickerDefault(acc, i); } catch (Exception dg9212_1) { Diag.Swallow("MainForm.Steam.cs:1689", dg9212_1); }
                        readyNow = WaitSamGameReady(10);
                        SamTrace("ready2=" + readyNow + " gameHwnd=" + samGameHwnd);
                    }
                    if (!readyNow)
                    {
                        report.Append("SKIP 无成就/未加载 " + nm + "\r\n");
                        noAch++;
                        ResetPickerAfterGame();
                        continue;
                    }
                    IntPtr lv = FindSamListView();
                    List<SamAch> rows = ReadSamAchRows(lv);
                    SamTrace("lv=" + lv + " rows=" + rows.Count);
                    if (rows.Count == 0)
                    {
                        report.Append("SKIP 无成就 " + nm + "\r\n");
                        noAch++;
                        ResetPickerAfterGame();
                        continue;
                    }
                    int unlocked = 0;
                    for (int k = 0; k < rows.Count; k++) if (rows[k].Unlocked) unlocked++;
                    if (unlocked >= rows.Count)
                    {
                        report.Append("SKIP 已全解锁 " + nm + " (" + rows.Count + ")\r\n");
                        already++;
                        ResetPickerAfterGame();
                        continue;
                    }
                    int pending = rows.Count - unlocked;
                    SamSetState("[" + i + "/" + limit + "] " + nm + " — 共 " + rows.Count + " 个，待解锁 " + pending + " 个，正在解锁…",
                        "[" + i + "/" + limit + "] " + pending + " to unlock…");
                    bool ok = InvokeSamButton(samGameHwnd, "解锁全部", "Unlock All");
                    if (!ok)
                    {
                        report.Append("FAIL 无法点解锁全部 " + nm + "\r\n");
                        failGames++;
                        ResetPickerAfterGame();
                        continue;
                    }
                    Thread.Sleep(samTestMode ? 6000 : 12000);
                    int checkedNow = 0;
                    List<SamAch> mid = ReadSamAchRows(lv);
                    for (int k = 0; k < mid.Count; k++) if (mid[k].Unlocked) checkedNow++;
                    if (samTestMode)
                    {
                        report.Append("TEST " + nm + " 已勾选 " + checkedNow + "/" + mid.Count +
                                      "（受保护 " + (mid.Count - checkedNow) + " 个未勾选），提交已跳过\r\n");
                        ResetPickerAfterGame();
                        break;
                    }
                    // 点了「解锁全部」但没有任何新勾选：先再等一轮排除慢刷新，确认后整款跳过，不提交
                    if (checkedNow <= unlocked)
                    {
                        Thread.Sleep(6000);
                        mid = ReadSamAchRows(lv);
                        checkedNow = 0;
                        for (int k = 0; k < mid.Count; k++) if (mid[k].Unlocked) checkedNow++;
                    }
                    if (checkedNow <= unlocked)
                    {
                        protSkip++;
                        protAch += (rows.Count - unlocked);
                        report.Append("SKIP 整款受保护 " + nm + "（" + (rows.Count - unlocked) + " 个受保护成就已跳过，未提交）\r\n");
                        ResetPickerAfterGame();
                        continue;
                    }
                    ClickSamSubmit(samGameHwnd);
                    Thread.Sleep(4000);
                    InvokeSamButton(samGameHwnd, "刷新", "Refresh");
                    Thread.Sleep(6000);
                    List<SamAch> after = ReadSamAchRows(lv);
                    int finalU = 0;
                    for (int k = 0; k < after.Count; k++) if (after[k].Unlocked) finalU++;
                    int gain = finalU - unlocked;
                    if (gain > 0) totalGain += gain;
                    if (finalU >= after.Count)
                    {
                        okGames++;
                        report.Append("OK " + nm + " +" + gain + "\r\n");
                    }
                    else
                    {
                        int left = after.Count - finalU;
                        protAch += left;
                        failGames++;
                        report.Append("PART " + nm + " +" + gain + "，受保护/校验未通过 " + left + " 个已跳过\r\n");
                    }
                    ResetPickerAfterGame();
                }
                SamTrace("loopfinished");
                completedAll = !samBatchStop && !samTestMode;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(BASE_DIR, "error.log"), "[sam-batch] " + ex.Message + "\r\n"); } catch (Exception dg9308_1) { Diag.Swallow("MainForm.Steam.cs:1785", dg9308_1); }
                report.Append("ERROR " + ex.Message + "\r\n");
                SamTrace("exception " + ex.Message);
            }
            finally
            {
                samBatchRunning = false;
                try
                {
                    string head;
                    if (completedAll)
                    {
                        head = "全库处理结束：解锁成功 " + okGames + " 个游戏、部分未解锁 " + failGames +
                               " 个、本来就全解锁 " + already + " 个、无成就 " + noAch + " 个、整款受保护跳过 " + protSkip +
                               " 个；本次共解锁 " + totalGain + " 个成就，受保护/未写入 " + protAch + " 个。";
                        try { File.Delete(Path.Combine(BASE_DIR, "_batch_cursor.txt")); } catch (Exception dg9323_1) { Diag.Swallow("MainForm.Steam.cs:1800", dg9323_1); }
                    }
                    else
                    {
                        head = "已暂停：处理到第 " + lastI + " 项，进度已保存，再点按钮从断点继续，不用从头再来。";
                    }
                    SamSetState(head, completedAll
                        ? "Batch finished: " + okGames + " ok, " + protSkip + " protected-only, +" + totalGain + " unlocked"
                        : "Paused; click again to resume from the breakpoint");
                    FlushBatchReport(report);
                }
                catch (Exception dg9334_1) { Diag.Swallow("MainForm.Steam.cs:1811", dg9334_1); }
            }
        }

        // 后台流程：检查 → 解锁全部 → 提交修改 → 刷新复查 → 报告结果

        IntPtr FindSamListView()
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                EnumChildWindows(samGameHwnd, delegate(IntPtr h, IntPtr l)
                {
                    StringBuilder c = new StringBuilder(256);
                    GetClassName(h, c, 256);
                    if (c.ToString().IndexOf("SysListView32", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception dg9357_1) { Diag.Swallow("MainForm.Steam.cs:1834", dg9357_1); }
            return found;
        }

        // 用 MSAA 读取每行成就名称与勾选状态（owner-data ListView 用普通 LVM 消息读不到文本）
        List<SamAch> ReadSamAchRows(IntPtr lv)
        {
            List<SamAch> list = new List<SamAch>();
            try
            {
                Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                object accObj = null;
                int hr = AccessibleObjectFromWindow(lv, 0xFFFFFFFC, ref iid, out accObj);
                if (hr != 0 || accObj == null) return list;
                IAccessible acc = (IAccessible)accObj;
                int n = acc.accChildCount;
                for (int i = 1; i <= n; i++)
                {
                    string nm = null;
                    try { nm = acc.get_accName(i); } catch { nm = null; }
                    if (nm == null || nm.Trim().Length == 0) continue;
                    int st = 0;
                    try { object o = acc.get_accState(i); if (o != null) st = Convert.ToInt32(o); } catch { st = 0; }
                    SamAch a = new SamAch();
                    a.Name = nm;
                    a.Unlocked = (st & 0x10) != 0;   // STATE_SYSTEM_CHECKED
                    list.Add(a);
                }
            }
            catch (Exception dg9386_1) { Diag.Swallow("MainForm.Steam.cs:1863", dg9386_1); }
            return list;
        }

        // SAM.Game 工具栏按钮没有独立 HWND，只能通过 UIAutomation 的 Invoke 触发
        bool InvokeSamButton(IntPtr win, string cn, string en)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(win);
                Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection all = root.FindAll(TreeScope.Descendants, cond);
                foreach (AutomationElement b in all)
                {
                    string nm = b.Current.Name;
                    if (nm == null) continue;
                    if (nm.Trim() == cn || (en != null && nm.IndexOf(en, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        try
                        {
                            ((InvokePattern)b.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        }
                        catch
                        {
                            // Invoke 是同步调用，SAM 处理较久会抛超时异常，但指令已送达
                        }
                        return true;
                    }
                }
            }
            catch (Exception dg9416_1) { Diag.Swallow("MainForm.Steam.cs:1893", dg9416_1); }
            return false;
        }

        // 右上角「提交修改」在顶部 ToolStrip 上且 UIA 不暴露名称，改用鼠标消息投递
        bool ClickSamSubmit(IntPtr win)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(win);
                Condition cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar);
                AutomationElementCollection bars = root.FindAll(TreeScope.Descendants, cond);
                AutomationElement top = null;
                double minY = double.MaxValue;
                foreach (AutomationElement b in bars)
                {
                    var r = b.Current.BoundingRectangle;
                    if (r.Y < minY) { minY = r.Y; top = b; }
                }
                if (top == null) return false;
                IntPtr hBar = (IntPtr)top.Current.NativeWindowHandle;
                var topRect = top.Current.BoundingRectangle;
                int w = (int)topRect.Width;
                int px = w - 45;
                int py = 12;
                IntPtr lp = (IntPtr)((py << 16) | (px & 0xFFFF));
                PostMessage(hBar, 0x0201, (IntPtr)1, lp);
                Thread.Sleep(120);
                PostMessage(hBar, 0x0202, IntPtr.Zero, lp);
                return true;
            }
            catch (Exception dg9447_1) { Diag.Swallow("MainForm.Steam.cs:1924", dg9447_1); }
            return false;
        }

        bool IsAllDigits(string s)
        {
            if (s == null || s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }


        void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

    }
}