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
    class PomodoroForm : Form
    {
        bool en;
        string dir;
        int total = 25 * 60;
        int left = 25 * 60;
        int cycles = 0;
        int workMin = 25, restMin = 5;
        bool running = false;
        bool resting = false;
        System.Windows.Forms.Timer tick;
        Label big;
        Label info;
        ProgressBar pb;

        public PomodoroForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "番茄钟 · 小工具箱", "Pomodoro · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 420);
            MinimumSize = new Size(480, 420);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "番茄钟", "Pomodoro"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "专注 25 分钟 → 休息 5 分钟，自动轮转；完成数记到 番茄统计.txt。",
                "25m focus → 5m break, auto cycles; logged to 番茄统计.txt."), 20, 48, 440, 22));

            big = V71Ui.Text("25:00", 20, 84, 440, 90, 42F, V71Ui.TEXT);
            big.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(big);

            pb = new ProgressBar();
            pb.Location = new Point(20, 186);
            pb.Size = new Size(440, 10);
            pb.Minimum = 0;
            pb.Maximum = 100;
            pb.Value = 100;
            Controls.Add(pb);

            info = V71Ui.Note("", 20, 204, 440, 40);
            Controls.Add(info);

            Button bStart = V71Ui.Btn(V71Ui.TT(en, "开始 / 暂停", "Start / Pause"), UiKit.A(34, 197, 94), 20, 258, 140, 36);
            Button bReset = V71Ui.Btn(V71Ui.TT(en, "重置", "Reset"), UiKit.A(52, 58, 70), 170, 258, 100, 36);
            Button bSkip = V71Ui.Btn(V71Ui.TT(en, "跳过当前", "Skip"), UiKit.A(96, 165, 250), 280, 258, 110, 36);
            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 400, 258, 80, 36);
            Controls.Add(bStart); Controls.Add(bReset); Controls.Add(bSkip); Controls.Add(bClose);
            bClose.Click += delegate { Close(); };

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "专注分钟", "Work"), 20, 310, 90, 22, 9F, V71Ui.SUB));
            ComboBox cbW = V71Ui.Combo(new string[] { "15", "20", "25", "30", "40", "45", "50" }, 110, 308, 80);
            cbW.SelectedIndex = 2;
            Controls.Add(cbW);
            Controls.Add(V71Ui.Text(V71Ui.TT(en, "休息分钟", "Break"), 210, 310, 90, 22, 9F, V71Ui.SUB));
            ComboBox cbR = V71Ui.Combo(new string[] { "3", "5", "10", "15" }, 300, 308, 80);
            cbR.SelectedIndex = 1;
            Controls.Add(cbR);

            Label tip = V71Ui.Note(V71Ui.TT(en, "计时靠系统时钟，最小化到托盘也照走。", "Timing uses the system clock; keeps running minimized."), 20, 344, 440, 40);
            Controls.Add(tip);

            cbW.SelectedIndexChanged += delegate
            {
                int.TryParse((string)cbW.SelectedItem, out workMin);
                if (!running && !resting) { total = workMin * 60; left = total; Show2(); }
            };
            cbR.SelectedIndexChanged += delegate
            {
                int.TryParse((string)cbR.SelectedItem, out restMin);
            };

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate
            {
                if (!running) return;
                left--;
                if (left <= 0) PhaseEnd();
                Show2();
            };

            bStart.Click += delegate
            {
                running = !running;
                info.Text = running
                    ? V71Ui.TT(en, "正在计时…", "Running…")
                    : V71Ui.TT(en, "已暂停", "Paused");
            };

            bReset.Click += delegate
            {
                running = false;
                resting = false;
                total = workMin * 60;
                left = total;
                Show2();
                info.Text = V71Ui.TT(en, "已重置", "Reset");
            };

            bSkip.Click += delegate { PhaseEnd(); };

            Show2();
            tick.Start();
        }

        void PhaseEnd()
        {
            bool wasRest = resting;
            resting = !resting;
            running = false;
            total = (resting ? restMin : workMin) * 60;
            left = total;
            Sfx.Bell();
            if (!wasRest)
            {
                cycles++;
                try
                {
                    File.AppendAllText(Path.Combine(dir, "番茄统计.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\t" + V71Ui.TT(en, "完成一个番茄", "pomodoro done") + "\r\n",
                        Encoding.UTF8);
                }
                catch (Exception dg149_1) { Diag.Swallow("PomodoroForm.cs:149", dg149_1); }
            }
            info.Text = resting
                ? V71Ui.TT(en, "休息 5 分钟，起来走两步", "Break time — stand up and stretch")
                : V71Ui.TT(en, "休息结束，开始下一个番茄", "Break over — next pomodoro");
            Show2();
        }

        void Show2()
        {
            int m = left / 60, s = left % 60;
            big.Text = m.ToString("00") + ":" + s.ToString("00");
            if (resting) big.ForeColor = V71Ui.WARN;
            else big.ForeColor = running ? V71Ui.GOOD : V71Ui.TEXT;
            int pct = total <= 0 ? 100 : (int)(left * 100.0 / total);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            pb.Value = pct;
            info.Text = V71Ui.TT(en, "已完成 ", "done ") + cycles + V71Ui.TT(en, " 个番茄 · 当前：", " · phase: ")
                + (resting ? V71Ui.TT(en, "休息", "break") : V71Ui.TT(en, "专注", "focus"));
        }
    }
}