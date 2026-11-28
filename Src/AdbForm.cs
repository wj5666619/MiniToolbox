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
    class AdbForm : Form
    {
        bool en;
        string dir;
        string adb = "";
        Label st;
        ComboBox cbDev;
        TextBox tbCmd;
        TextBox tbLog;
        Button bDeploy;

        public AdbForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "ADB 手机工具箱 · 小工具箱", "ADB Phone Kit · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(940, 706);
            MinimumSize = new Size(940, 706);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            adb = AdbCore.FindAdb(dir);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "ADB 手机工具箱", "ADB phone kit"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "插上手机（开 USB 调试）后可以装 APK、截屏、录屏、抓日志、推拉文件、重启、跑任意 shell 命令。没有 adb 时这里能一键装 platform-tools。",
                "With USB debugging on: install APKs, screenshot, record, pull logs, push/pull files, reboot or run shell commands."), 20, 48, 900, 40));

            Label lp = V71Ui.Note("", 20, 90, 700, 22);
            lp.Text = adb.Length > 0 ? V71Ui.TT(en, "adb：", "adb: ") + adb : V71Ui.TT(en, "未找到 adb（可点右边一键下载 platform-tools）", "adb not found");
            lp.ForeColor = adb.Length > 0 ? V71Ui.SUB : V71Ui.WARN;
            Controls.Add(lp);

            bDeploy = V71Ui.Btn(V71Ui.TT(en, "下载 platform-tools", "Get platform-tools"), UiKit.A(96, 165, 250), 740, 86, 180, 32);
            Controls.Add(bDeploy);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "设备", "Device"), 20, 126, 80, 22, 9F, V71Ui.SUB));
            cbDev = V71Ui.Combo(new string[] { V71Ui.TT(en, "（还没扫）", "(not scanned)") }, 20, 150, 320);
            Controls.Add(cbDev);
            Button bRef = V71Ui.Btn(V71Ui.TT(en, "刷新设备", "Refresh"), UiKit.A(34, 197, 94), 350, 148, 110, 30);
            Controls.Add(bRef);

            Button bApk = V71Ui.Btn(V71Ui.TT(en, "安装 APK", "Install APK"), UiKit.A(249, 115, 22), 20, 194, 130, 32);
            Button bShot = V71Ui.Btn(V71Ui.TT(en, "截屏", "Screenshot"), UiKit.A(14, 165, 233), 158, 194, 100, 32);
            Button bRec = V71Ui.Btn(V71Ui.TT(en, "录屏 10s", "Record 10s"), UiKit.A(139, 92, 246), 266, 194, 110, 32);
            Button bLog = V71Ui.Btn(V71Ui.TT(en, "抓日志", "Logcat"), UiKit.A(250, 190, 90), 384, 194, 100, 32);
            Button bPush = V71Ui.Btn(V71Ui.TT(en, "推送文件", "Push file"), UiKit.A(72, 208, 130), 492, 194, 110, 32);
            Button bPull = V71Ui.Btn(V71Ui.TT(en, "拉取文件", "Pull file"), UiKit.A(72, 208, 130), 610, 194, 110, 32);
            Button bReb = V71Ui.Btn(V71Ui.TT(en, "重启手机", "Reboot"), UiKit.A(232, 82, 61), 728, 194, 110, 32);
            Button bFold = V71Ui.Btn(V71Ui.TT(en, "打开输出目录", "Open output"), UiKit.A(52, 58, 70), 846, 194, 74, 32);
            Controls.Add(bApk); Controls.Add(bShot); Controls.Add(bRec); Controls.Add(bLog);
            Controls.Add(bPush); Controls.Add(bPull); Controls.Add(bReb); Controls.Add(bFold);

            // 速传行：截屏直接存盘并塞进 Windows 剪贴板；电脑↔手机剪贴板互传
            Button bShotD = V71Ui.Btn(V71Ui.TT(en, "截屏直传（存盘 + 复制到剪贴板）", "Screenshot to clipboard"), UiKit.A(37, 99, 235), 20, 236, 246, 32);
            Button bClipG = V71Ui.Btn(V71Ui.TT(en, "读手机剪贴板", "Read phone clipboard"), UiKit.A(52, 58, 70), 274, 236, 140, 32);
            Button bClipP = V71Ui.Btn(V71Ui.TT(en, "发送电脑剪贴板到手机", "Send clipboard to phone"), UiKit.A(52, 58, 70), 422, 236, 182, 32);
            Button bShotO = V71Ui.Btn(V71Ui.TT(en, "打开截图目录", "Open shots"), UiKit.A(52, 58, 70), 612, 236, 118, 32);
            Controls.Add(bShotD); Controls.Add(bClipG); Controls.Add(bClipP); Controls.Add(bShotO);

            tbCmd = V71Ui.Box("", 20, 282, 700, 28);
            Controls.Add(tbCmd);
            Button bRun = V71Ui.Btn(V71Ui.TT(en, "执行", "Run"), UiKit.A(52, 58, 70), 730, 280, 90, 32);
            Controls.Add(bRun);

            tbLog = new TextBox();
            tbLog.Multiline = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.BackColor = V71Ui.INPUT;
            tbLog.ForeColor = V71Ui.TEXT;
            tbLog.BorderStyle = BorderStyle.FixedSingle;
            tbLog.Font = new Font("Consolas", 9F);
            tbLog.Location = new Point(20, 322);
            tbLog.Size = new Size(900, 300);
            tbLog.ReadOnly = true;
            Controls.Add(tbLog);

            st = V71Ui.Note("", 20, 626, 900, 30);
            Controls.Add(st);

            Button close = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 820, 662, 100, 32);
            Controls.Add(close);
            close.Click += delegate { Close(); };

            bRef.Click += delegate { RefreshDevices(); };
            bDeploy.Click += delegate
            {
                bDeploy.Enabled = false;
                st.Text = V71Ui.TT(en, "正在下载 platform-tools（约 20 MB）…", "Downloading platform-tools (~20 MB)…");
                Application.DoEvents();
                string err = "";
                bool ok = AdbCore.Deploy(dir, delegate (string s) { st.Text = s; Application.DoEvents(); }, out err);
                bDeploy.Enabled = true;
                if (ok)
                {
                    adb = AdbCore.FindAdb(dir);
                    lp.Text = V71Ui.TT(en, "adb：", "adb: ") + adb;
                    st.Text = V71Ui.TT(en, "已装好：", "Installed: ") + adb;
                }
                else st.Text = V71Ui.TT(en, "下载失败：", "Download failed: ") + err;
            };

            bApk.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                OpenFileDialog od = new OpenFileDialog();
                od.Filter = "APK|*.apk|所有文件|*.*";
                if (od.ShowDialog(this) != DialogResult.OK) return;
                st.Text = V71Ui.TT(en, "安装中…", "Installing…");
                Application.DoEvents();
                string o = AdbCore.Run(adb, "-s " + serial + " install -r \"" + od.FileName + "\"", 180000);
                Log(o);
                st.Text = o.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0 ? V71Ui.TT(en, "安装成功", "Success") : V71Ui.TT(en, "安装返回：", "Result: ") + Tail(o);
            };

            bShot.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                string outDir = OutDir();
                string remote = "/sdcard/mtb_shot.png";
                AdbCore.Run(adb, "-s " + serial + " shell screencap -p " + remote, 30000);
                string local = Path.Combine(outDir, "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                string o = AdbCore.Run(adb, "-s " + serial + " pull " + remote + " \"" + local + "\"", 60000);
                Log(o);
                if (File.Exists(local)) st.Text = V71Ui.TT(en, "已保存：", "Saved: ") + local;
                else st.Text = V71Ui.TT(en, "截屏失败：", "Failed: ") + Tail(o);
            };

            bRec.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                string outDir = OutDir();
                string remote = "/sdcard/mtb_rec.mp4";
                st.Text = V71Ui.TT(en, "录屏 10 秒…", "Recording 10s…");
                Application.DoEvents();
                AdbCore.Run(adb, "-s " + serial + " shell screenrecord --time-limit 10 " + remote, 30000);
                string local = Path.Combine(outDir, "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
                string o = AdbCore.Run(adb, "-s " + serial + " pull " + remote + " \"" + local + "\"", 60000);
                Log(o);
                if (File.Exists(local)) st.Text = V71Ui.TT(en, "已保存：", "Saved: ") + local;
                else st.Text = V71Ui.TT(en, "录屏失败：", "Failed: ") + Tail(o);
            };

            bLog.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                string outDir = OutDir();
                string o = AdbCore.Run(adb, "-s " + serial + " logcat -d -t 800", 30000);
                string local = Path.Combine(outDir, "日志_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                try { File.WriteAllText(local, o, Encoding.UTF8); } catch (Exception dg178_1) { Diag.Swallow("AdbForm.cs:178", dg178_1); }
                Log(o);
                st.Text = V71Ui.TT(en, "日志已保存：", "Log saved: ") + local;
            };

            bPush.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                OpenFileDialog od = new OpenFileDialog();
                if (od.ShowDialog(this) != DialogResult.OK) return;
                string o = AdbCore.Run(adb, "-s " + serial + " push \"" + od.FileName + "\" /sdcard/", 120000);
                Log(o);
                st.Text = V71Ui.TT(en, "推送完成（到 /sdcard/）", "Pushed to /sdcard/");
            };

            bPull.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                string remote = tbCmd.Text != null && tbCmd.Text.Trim().Length > 0 ? tbCmd.Text.Trim() : "/sdcard/";
                string outDir = OutDir();
                string o = AdbCore.Run(adb, "-s " + serial + " pull \"" + remote + "\" \"" + outDir + "\"", 180000);
                Log(o);
                st.Text = V71Ui.TT(en, "拉取完成：", "Pulled: ") + outDir;
            };

            bReb.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                if (MessageBox.Show(this, V71Ui.TT(en, "确定重启手机？", "Reboot the phone?"), "小工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                AdbCore.Run(adb, "-s " + serial + " reboot", 15000);
                st.Text = V71Ui.TT(en, "已发送重启命令。", "Reboot sent.");
            };

            bFold.Click += delegate
            {
                try { Process.Start("explorer.exe", OutDir()); } catch (Exception dg216_1) { Diag.Swallow("AdbForm.cs:216", dg216_1); }
            };

            bShotD.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                string outDir = OutDir();
                string local = Path.Combine(outDir, "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                st.Text = V71Ui.TT(en, "正在截屏…", "Taking screenshot…");
                Application.DoEvents();
                string err = "";
                bool ok = AdbCore.RunToFile(adb, "-s " + serial + " exec-out screencap -p", local, 40000, out err);
                if (!ok || !File.Exists(local))
                {
                    if (File.Exists(local)) { try { File.Delete(local); } catch (Exception dg231_1) { Diag.Swallow("AdbForm.cs:231", dg231_1); } }
                    st.Text = V71Ui.TT(en, "截屏失败：", "Screenshot failed: ") + err;
                    return;
                }
                bool clip = false;
                try
                {
                    using (Image im = Image.FromFile(local))
                    {
                        Clipboard.SetImage((Image)im.Clone());
                        clip = true;
                    }
                }
                catch (Exception ce) { err = ce.Message; }
                st.Text = V71Ui.TT(en, "已保存：", "Saved: ") + local
                    + (clip ? V71Ui.TT(en, " · 已复制到剪贴板，Ctrl+V 直接粘贴", " · copied to clipboard") : V71Ui.TT(en, " · 复制剪贴板失败：" + err, " · clipboard failed"));
            };

            bClipG.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                st.Text = V71Ui.TT(en, "正在读手机剪贴板…", "Reading phone clipboard…");
                Application.DoEvents();
                string t = AdbCore.Run(adb, "-s " + serial + " shell cmd clipboard get-text", 20000).Trim();
                if (t.Length == 0 || t.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t = AdbCore.Run(adb, "-s " + serial + " shell dumpsys clipboard", 20000);
                    int at = t.IndexOf("text=", StringComparison.OrdinalIgnoreCase);
                    if (at >= 0)
                    {
                        int end = t.IndexOf('\n', at);
                        t = end > at ? t.Substring(at + 5, end - at - 5) : t.Substring(at + 5);
                    }
                }
                t = t.Trim();
                Log(t);
                if (t.Length == 0)
                {
                    st.Text = V71Ui.TT(en, "手机侧没给出剪贴板内容（Android 10+ 需要系统权限，读不到就改用「截屏直传」）。",
                        "Phone did not expose clipboard (Android 10+ restricts it); use screenshot-to-clipboard instead.");
                    return;
                }
                try { Clipboard.SetText(t); st.Text = V71Ui.TT(en, "已复制到电脑剪贴板（", "Copied to PC clipboard (") + t.Length + V71Ui.TT(en, " 字）", " chars)"); }
                catch (Exception ce) { st.Text = V71Ui.TT(en, "写入剪贴板失败：", "Clipboard failed: ") + ce.Message; }
            };

            bClipP.Click += delegate
            {
                string serial = SelSerial();
                if (serial.Length == 0) return;
                string txt = "";
                try { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); } catch (Exception dg283_1) { Diag.Swallow("AdbForm.cs:283", dg283_1); }
                if (txt == null) txt = "";
                txt = txt.Trim();
                if (txt.Length == 0) { st.Text = V71Ui.TT(en, "电脑剪贴板里没有文字。", "PC clipboard has no text."); return; }
                bool ascii = true;
                for (int ci = 0; ci < txt.Length; ci++) { if (txt[ci] > 127) { ascii = false; break; } }
                if (ascii)
                {
                    string esc = txt.Replace("%", "\\%").Replace(" ", "%s")
                        .Replace("(", "\\(").Replace(")", "\\)").Replace("<", "\\<").Replace(">", "\\>")
                        .Replace("&", "\\&").Replace("|", "\\|").Replace(";", "\\;").Replace("*", "\\*")
                        .Replace("~", "\\~").Replace("\"", "\\\"").Replace("'", "\\'").Replace("\r", "").Replace("\n", " ");
                    string o = AdbCore.Run(adb, "-s " + serial + " shell input text \"" + esc + "\"", 30000);
                    Log(o);
                    st.Text = V71Ui.TT(en, "已输入到手机当前光标处（", "Typed into phone (") + txt.Length + V71Ui.TT(en, " 字）", " chars)");
                }
                else
                {
                    string outDir = OutDir();
                    string local = Path.Combine(outDir, "剪贴板_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    try { File.WriteAllText(local, txt, Encoding.UTF8); } catch (Exception dg303_1) { Diag.Swallow("AdbForm.cs:303", dg303_1); }
                    string o = AdbCore.Run(adb, "-s " + serial + " push \"" + local + "\" /sdcard/", 60000);
                    Log(o);
                    st.Text = V71Ui.TT(en, "中文没法直接 input text：已推到手机 /sdcard/（同名 txt），手机上打开即可复制。",
                        "CJK cannot be typed via input text: pushed to /sdcard/ as a txt file.");
                }
            };

            bShotO.Click += delegate
            {
                try { Process.Start("explorer.exe", OutDir()); } catch (Exception dg313_1) { Diag.Swallow("AdbForm.cs:313", dg313_1); }
            };

            bRun.Click += delegate
            {
                if (adb.Length == 0) { V71Ui.Say(this, V71Ui.TT(en, "先装 adb。", "Install adb first.")); return; }
                string cmd = tbCmd.Text == null ? "" : tbCmd.Text.Trim();
                if (cmd.Length == 0) return;
                string serial = SelSerial();
                string o = AdbCore.Run(adb, (serial.Length > 0 ? "-s " + serial + " " : "") + "shell " + cmd, 60000);
                Log(o);
                st.Text = V71Ui.TT(en, "已执行。", "Done.");
            };

            RefreshDevices();
        }

        string OutDir()
        {
            string d = Path.Combine(dir, "手机工具");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg333_1) { Diag.Swallow("AdbForm.cs:333", dg333_1); }
            return d;
        }

        string SelSerial()
        {
            if (adb.Length == 0) { V71Ui.Say(this, V71Ui.TT(en, "没有 adb：先点「下载 platform-tools」。", "No adb: click Get platform-tools first.")); return ""; }
            if (cbDev.SelectedItem == null) { V71Ui.Say(this, V71Ui.TT(en, "没有设备：插上手机并打开 USB 调试后点「刷新设备」。", "No device: connect with USB debugging on, then Refresh.")); return ""; }
            return AdbCore.SerialOf(cbDev.SelectedItem.ToString());
        }

        void RefreshDevices()
        {
            if (adb.Length == 0) { st.Text = V71Ui.TT(en, "还没装 adb。", "adb missing."); return; }
            AdbCore.Run(adb, "start-server", 15000);
            List<string> devs = AdbCore.Devices(adb);
            cbDev.Items.Clear();
            if (devs.Count == 0)
            {
                cbDev.Items.Add(V71Ui.TT(en, "（无设备）", "(no device)"));
                cbDev.SelectedIndex = 0;
                st.Text = V71Ui.TT(en, "没检测到手机：检查数据线、USB 调试授权。", "No phone detected: check cable and USB debugging." );
            }
            else
            {
                foreach (string d in devs) cbDev.Items.Add(d);
                cbDev.SelectedIndex = 0;
                st.Text = V71Ui.TT(en, "找到 ", "Found ") + devs.Count + V71Ui.TT(en, " 个设备", " device(s)");
            }
        }

        void Log(string text)
        {
            string s = text == null ? "" : text.Replace("\r", "");
            tbLog.Text = s.Length > 20000 ? s.Substring(0, 20000) : s;
            tbLog.SelectionStart = tbLog.Text.Length;
            tbLog.ScrollToCaret();
        }

        static string Tail(string s)
        {
            if (s == null) return "";
            string t = s.Trim().Replace('\n', ' ');
            return t.Length <= 200 ? t : t.Substring(t.Length - 200);
        }
    }
}