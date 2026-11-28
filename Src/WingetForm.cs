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
    class WingetForm : Form
    {
        CheckBox[] cbs;
        Button btnGo;
        ProgressBar pb;
        Label lbl;
        bool busy = false;
        bool en;
        string[] NAMES = { "VS Code", "7-Zip", "Git", "VLC", "Chrome", "Everything", "Notepad++", "PowerToys" };
        string[] IDS = { "Microsoft.VisualStudioCode", "7zip.7zip", "Git.Git", "VideoLAN.VLC", "Google.Chrome", "voidtools.Everything", "Notepad++.Notepad++", "Microsoft.PowerToys" };

        public WingetForm(bool langEn)
        {
            en = langEn;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 420);
            BackColor = UiKit.A(30, 34, 41);
            ShowInTaskbar = false;

            Panel bar = new Panel();
            bar.BackColor = UiKit.A(26, 30, 37);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(560, 42);
            Controls.Add(bar);

            Label ti = new Label();
            ti.Text = en ? "Install Common Apps" : "常用软件一键装";
            ti.ForeColor = UiKit.A(230, 233, 238);
            ti.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            ti.Location = new Point(16, 10);
            ti.AutoSize = true;
            bar.Controls.Add(ti);

            Button x = new Button();
            x.Text = "×";
            x.FlatStyle = FlatStyle.Standard;
            x.FlatAppearance.BorderSize = 0;
            x.ForeColor = UiKit.A(150, 160, 175);
            x.Font = new Font("Microsoft YaHei UI", 12F);
            x.Location = new Point(520, 6);
            x.Size = new Size(30, 30);
            x.Click += (s, e) => Close();
            bar.Controls.Add(x);

            Label note = new Label();
            note.Text = en ? "Uses winget. Silent install, accepts agreements automatically. Admin may be required for some apps." : "基于 winget 静默安装，自动接受协议。部分软件可能需要管理员权限。";
            note.ForeColor = UiKit.A(150, 160, 175);
            note.Font = new Font("Microsoft YaHei UI", 8.5F);
            note.Location = new Point(20, 52);
            note.Size = new Size(520, 34);
            Controls.Add(note);

            cbs = new CheckBox[NAMES.Length];
            for (int i = 0; i < NAMES.Length; i++)
            {
                CheckBox c = new RoundCheck();
                c.Text = NAMES[i];
                c.ForeColor = UiKit.A(230, 233, 238);
                c.BackColor = UiKit.A(30, 34, 41);
                c.FlatStyle = FlatStyle.Standard;
                c.Font = new Font("Microsoft YaHei UI", 9F);
                c.Location = new Point(20 + (i % 2) * 265, 96 + (i / 2) * 38);
                c.Size = new Size(250, 26);
                Controls.Add(c);
                cbs[i] = c;
            }

            btnGo = new Button();
            btnGo.Text = en ? "Install Selected" : "安装勾选的软件";
            btnGo.FlatStyle = FlatStyle.Standard;
            btnGo.FlatAppearance.BorderSize = 0;
            btnGo.BackColor = UiKit.A(45, 212, 191);
            btnGo.ForeColor = Color.Black;
            btnGo.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnGo.Location = new Point(20, 264);
            btnGo.Size = new Size(170, 36);
            btnGo.Click += (s, e) => StartInstall();
            Controls.Add(btnGo);

            Button btnClose = new Button();
            btnClose.Text = en ? "Close" : "关闭";
            btnClose.FlatStyle = FlatStyle.Standard;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.BackColor = UiKit.A(45, 52, 64);
            btnClose.ForeColor = UiKit.Txt(Color.White);
            btnClose.Font = new Font("Microsoft YaHei UI", 9F);
            btnClose.Location = new Point(390, 264);
            btnClose.Size = new Size(150, 36);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            pb = new ProgressBar();
            pb.Minimum = 0; pb.Maximum = 100;
            pb.Style = ProgressBarStyle.Continuous;
            pb.Location = new Point(20, 312);
            pb.Size = new Size(520, 14);
            pb.Visible = false;
            Controls.Add(pb);

            lbl = new Label();
            lbl.Text = "";
            lbl.ForeColor = UiKit.A(126, 136, 154);
            lbl.Font = new Font("Microsoft YaHei UI", 8.5F);
            lbl.Location = new Point(20, 334);
            lbl.Size = new Size(520, 70);

            if (!HasWinget())
            {
                lbl.Text = en ? "winget not found on this PC. Click the button below to open the official winget download page." : "本机未检测到 winget。点击下方按钮会打开官方下载页，装好 winget 后即可一键安装全部软件。";
                btnGo.Text = en ? "Get winget" : "安装 winget";
                btnGo.BackColor = UiKit.A(59, 130, 246);
            }
            Controls.Add(lbl);
        }

        void StartInstall()
        {
            if (busy) return;
            if (!HasWinget())
            {
                lbl.Text = en ? "Opening the official winget download page…" : "正在打开 winget 官方下载页…";
                try { Process.Start("https://github.com/microsoft/winget-cli/releases/latest"); }
                catch (Exception ex) { lbl.Text = (en ? "Cannot open browser: " : "无法打开浏览器：") + ex.Message; }
                return;
            }
            List<string> pick = new List<string>();
            for (int i = 0; i < cbs.Length; i++) if (cbs[i].Checked) pick.Add(IDS[i]);
            if (pick.Count == 0) { lbl.Text = en ? "Nothing selected." : "没有勾选任何软件。"; return; }
            busy = true;
            btnGo.Enabled = false;
            pb.Visible = true;
            pb.Value = 0;
            int total = pick.Count;
            Thread t = new Thread(delegate()
            {
                int ok = 0, fail = 0;
                for (int i = 0; i < total; i++)
                {
                    string id = pick[i];
                    int cur = i;
                    this.BeginInvoke((Action)delegate
                    {
                        lbl.Text = (en ? "Installing " : "正在安装 ") + (cur + 1) + " / " + total + "：" + id;
                        pb.Value = (cur * 100) / total;
                    });
                    bool r = RunWinget(id);
                    if (r) ok++; else fail++;
                }
                this.BeginInvoke((Action)delegate
                {
                    pb.Value = 100;
                    lbl.Text = (en ? "Done. ok " : "完成：成功 ") + ok + (en ? ", failed " : " 个，失败 ") + fail + (en ? "" : " 个");
                    busy = false;
                    btnGo.Enabled = true;
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool HasWinget()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("where", "winget");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process pp = Process.Start(psi))
                {
                    // 两路异步读：子进程日志写满缓冲区时不会双双卡死
                    System.Text.StringBuilder sbO = new System.Text.StringBuilder(), sbE = new System.Text.StringBuilder();
                    pp.OutputDataReceived += delegate(object s4, DataReceivedEventArgs e4) { if (e4.Data != null) sbO.AppendLine(e4.Data); };
                    pp.ErrorDataReceived += delegate(object s4, DataReceivedEventArgs e4) { if (e4.Data != null) sbE.AppendLine(e4.Data); };
                    pp.BeginOutputReadLine();
                    pp.BeginErrorReadLine();
                    if (!pp.WaitForExit(5000)) { try { pp.Kill(); } catch (Exception dg201_1) { Diag.Swallow("WingetForm.cs:201", dg201_1); } return false; }
                    return pp.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        static bool RunWinget(string id)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("winget",
                    "install --id " + id + " -e --silent --accept-package-agreements --accept-source-agreements");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process pp = Process.Start(psi))
                {
                    // 两路异步读：子进程日志写满缓冲区时不会双双卡死
                    System.Text.StringBuilder sbO = new System.Text.StringBuilder(), sbE = new System.Text.StringBuilder();
                    pp.OutputDataReceived += delegate(object s4, DataReceivedEventArgs e4) { if (e4.Data != null) sbO.AppendLine(e4.Data); };
                    pp.ErrorDataReceived += delegate(object s4, DataReceivedEventArgs e4) { if (e4.Data != null) sbE.AppendLine(e4.Data); };
                    pp.BeginOutputReadLine();
                    pp.BeginErrorReadLine();
                    if (!pp.WaitForExit(600000)) { try { pp.Kill(); } catch (Exception dg226_1) { Diag.Swallow("WingetForm.cs:226", dg226_1); } return false; }
                    return pp.HasExited && pp.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }
}