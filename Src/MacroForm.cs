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
    class MacroForm : Form
    {
        bool en;
        ListBox lbList;
        TextBox tbLog;
        TextBox tbName;
        NumSpin nudLoops;
        ComboBox cbSpeed;
        Label lblState;
        Button btnRec;
        System.Windows.Forms.Timer tmState = new System.Windows.Forms.Timer();
        List<MacroEvent> recBuf;

        string Dir()
        {
            string d = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "宏");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg39_1) { Diag.Swallow("MacroForm.cs:39", dg39_1); }
            return d;
        }

        public MacroForm(bool langEn)
        {
            en = langEn;
            Text = en ? "Macro recorder · Mini Toolbox" : "键鼠宏录制 · 小工具箱";
            BackColor = UiKit.A(20, 24, 30);
            ClientSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            Label t = Lbl(en ? "Macro recorder" : "键鼠宏录制", 16F, Color.White, 20, 12);
            Controls.Add(t);
            Label sub = Lbl(en ? "F8 record / stop · F9 replay · Esc abort. Recorded keys go to the 宏 folder." : "F8 录制 / 停止 · F9 回放 · Esc 中止。录好的宏存在「宏」文件夹。", 8.5F, UiKit.A(150, 160, 176), 20, 42);
            sub.Size = new Size(720, 20);
            Controls.Add(sub);

            lbList = new ListBox();
            lbList.BackColor = UiKit.A(18, 22, 28);
            lbList.ForeColor = UiKit.A(206, 218, 232);
            lbList.BorderStyle = BorderStyle.None;
            lbList.Font = new Font("Microsoft YaHei UI", 9.5F);
            lbList.Location = new Point(20, 74);
            lbList.Size = new Size(300, 300);
            lbList.DoubleClick += delegate { Play(); };
            Controls.Add(lbList);

            btnRec = MkBtn(en ? "Record (F8)" : "开始录制（F8）", 20, 386, 140, 32);
            btnRec.Click += delegate { ToggleRec(); };
            Controls.Add(btnRec);

            Button btnStop = MkBtn(en ? "Stop (F8)" : "停止录制（F8）", 168, 386, 140, 32);
            btnStop.Click += delegate { if (MacroCore.Recording) ToggleRec(); };
            Controls.Add(btnStop);

            Button btnPlay = MkBtn(en ? "Replay (F9)" : "回放（F9）", 20, 426, 140, 32);
            btnPlay.Click += delegate { Play(); };
            Controls.Add(btnPlay);

            Button btnStopPlay = MkBtn(en ? "Stop replay" : "停止回放", 168, 426, 140, 32);
            btnStopPlay.Click += delegate { MacroCore.StopPlay(); };
            Controls.Add(btnStopPlay);

            tbName = new TextBox();
            tbName.BackColor = UiKit.A(34, 40, 50);
            tbName.ForeColor = UiKit.A(232, 240, 250);
            tbName.BorderStyle = BorderStyle.FixedSingle;
            tbName.Font = new Font("Microsoft YaHei UI", 9F);
            tbName.Location = new Point(20, 470);
            tbName.Size = new Size(180, 24);
            tbName.Text = "macro_" + DateTime.Now.ToString("HHmmss");
            Controls.Add(tbName);

            Button btnSave = MkBtn(en ? "Save" : "保存", 208, 468, 92, 28);
            btnSave.Click += delegate { SaveMacro(); };
            Controls.Add(btnSave);

            Button btnDel = MkBtn(en ? "Delete" : "删除", 308, 468, 80, 28);
            btnDel.Click += delegate { DeleteMacro(); };
            Controls.Add(btnDel);

            Button btnOpen = MkBtn(en ? "Open folder" : "打开目录", 20, 508, 120, 28);
            btnOpen.Click += delegate
            {
                try { Process.Start("explorer.exe", Dir()); } catch (Exception dg107_1) { Diag.Swallow("MacroForm.cs:107", dg107_1); }
            };
            Controls.Add(btnOpen);

            Button btnRefresh = MkBtn(en ? "Refresh" : "刷新列表", 148, 508, 110, 28);
            btnRefresh.Click += delegate { LoadList(); };
            Controls.Add(btnRefresh);

            Controls.Add(Lbl(en ? "Loops" : "循环次数", 8.5F, UiKit.A(150, 160, 176), 300, 470));
            nudLoops = new NumSpin();
            nudLoops.Minimum = 1; nudLoops.Maximum = 100000; nudLoops.Value = 1;
            nudLoops.BackColor = UiKit.A(34, 40, 50);
            nudLoops.ForeColor = UiKit.A(232, 240, 250);
            nudLoops.BorderStyle = BorderStyle.FixedSingle;
            nudLoops.Location = new Point(360, 469);
            nudLoops.Size = new Size(70, 24);
            Controls.Add(nudLoops);

            Controls.Add(Lbl(en ? "Speed" : "速度", 8.5F, UiKit.A(150, 160, 176), 440, 470));
            cbSpeed = new ComboBox();
            cbSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSpeed.BackColor = UiKit.A(34, 40, 50);
            cbSpeed.ForeColor = UiKit.A(232, 240, 250);
            cbSpeed.Items.Add("0.5x"); cbSpeed.Items.Add("1x"); cbSpeed.Items.Add("2x"); cbSpeed.Items.Add("4x");
            cbSpeed.SelectedIndex = 1;
            cbSpeed.Location = new Point(488, 469);
            cbSpeed.Size = new Size(80, 24);
            Controls.Add(cbSpeed);

            lblState = Lbl(en ? "idle" : "空闲", 9F, UiKit.A(120, 170, 220), 20, 546);
            lblState.Size = new Size(720, 18);
            Controls.Add(lblState);

            tbLog = new TextBox();
            tbLog.Multiline = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.BackColor = UiKit.A(18, 22, 28);
            tbLog.ForeColor = UiKit.A(180, 194, 210);
            tbLog.BorderStyle = BorderStyle.None;
            tbLog.Font = new Font("Consolas", 8.5F);
            tbLog.Location = new Point(336, 74);
            tbLog.Size = new Size(404, 382);
            tbLog.ReadOnly = true;
            Controls.Add(tbLog);

            MacroCore.OnLog = delegate (string s) { Log(s); };
            LoadList();

            tmState.Interval = 400;
            tmState.Tick += delegate
            {
                if (MacroCore.Recording) lblState.Text = en ? "RECORDING — press F8 to stop" : "录制中… 按 F8 停止";
                else if (MacroCore.Playing) lblState.Text = en ? "REPLAYING — Esc aborts" : "回放中… 按 Esc 中止";
                else lblState.Text = en ? "idle" : "空闲";
            };
            tmState.Start();

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F8)
                {
                    if (MacroCore.Playing) MacroCore.StopPlay();
                    else ToggleRec();
                }
                else if (e.KeyCode == Keys.F9) Play();
                else if (e.KeyCode == Keys.Escape) MacroCore.StopPlay();
            };
        }

        Label Lbl(string text, float size, Color color, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = new Font("Microsoft YaHei UI", size, size >= 12F ? FontStyle.Bold : FontStyle.Regular);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        Button MkBtn(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderColor = UiKit.A(70, 84, 104);
            b.BackColor = UiKit.A(40, 48, 60);
            b.ForeColor = UiKit.A(228, 236, 246);
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Cursor = Cursors.Hand;
            return b;
        }

        void Log(string s)
        {
            try { tbLog.AppendText(s + "\r\n"); } catch (Exception dg204_1) { Diag.Swallow("MacroForm.cs:204", dg204_1); }
        }

        void LoadList()
        {
            lbList.Items.Clear();
            try
            {
                string[] fs = Directory.GetFiles(Dir(), "*.mac");
                Array.Sort(fs);
                for (int i = 0; i < fs.Length; i++) lbList.Items.Add(new MacFile(fs[i]));
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        class MacFile
        {
            public string Path;
            public MacFile(string p) { Path = p; }
            public override string ToString()
            {
                return System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }

        void ToggleRec()
        {
            if (MacroCore.Recording)
            {
                recBuf = MacroCore.StopRecord();
                btnRec.Text = en ? "Record (F8)" : "开始录制（F8）";
                return;
            }
            if (MacroCore.Playing) { MacroCore.StopPlay(); }
            string err = "";
            if (MacroCore.StartRecord(out err))
            {
                recBuf = null;
                btnRec.Text = en ? "Stop (F8)" : "停止录制（F8）";
                Log(en ? "recording…" : "开始录制…");
            }
            else Log(err);
        }

        void SaveMacro()
        {
            if (recBuf == null || recBuf.Count == 0)
            {
                Log(en ? "nothing recorded yet" : "还没有录到内容");
                return;
            }
            string name = tbName.Text.Trim();
            if (name.Length == 0) name = "macro_" + DateTime.Now.ToString("HHmmss");
            char[] bad = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            for (int i = 0; i < bad.Length; i++) name = name.Replace(bad[i], '_');
            string file = Path.Combine(Dir(), name + ".mac");
            try
            {
                File.WriteAllText(file, MacroCore.Serialize(recBuf), Encoding.UTF8);
                Log((en ? "saved: " : "已保存：") + Path.GetFileName(file) + "  " + recBuf.Count.ToString() + (en ? " events" : " 条"));
                LoadList();
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        void DeleteMacro()
        {
            if (lbList.SelectedIndex < 0) return;
            MacFile mf = (MacFile)lbList.SelectedItem;
            try
            {
                File.Delete(mf.Path);
                Log((en ? "deleted " : "已删除 ") + mf.ToString());
                LoadList();
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        void Play()
        {
            if (lbList.SelectedIndex < 0)
            {
                Log(en ? "select a macro first" : "请先选一个宏");
                return;
            }
            MacFile mf = (MacFile)lbList.SelectedItem;
            try
            {
                string txt = File.ReadAllText(mf.Path, Encoding.UTF8);
                List<MacroEvent> list = MacroCore.Deserialize(txt);
                double speed = 1.0;
                if (cbSpeed.SelectedIndex == 0) speed = 0.5;
                else if (cbSpeed.SelectedIndex == 2) speed = 2.0;
                else if (cbSpeed.SelectedIndex == 3) speed = 4.0;
                int loops = (int)nudLoops.Value;
                Log((en ? "replay " : "回放 ") + mf.ToString() + " · " + list.Count.ToString() + (en ? " events · loops " : " 条 · 循环 ") + loops.ToString());
                MacroCore.Play(list, speed, loops);
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (MacroCore.Recording) MacroCore.StopRecord(); } catch (Exception dg307_1) { Diag.Swallow("MacroForm.cs:307", dg307_1); }
            try { MacroCore.StopPlay(); } catch (Exception dg308_1) { Diag.Swallow("MacroForm.cs:308", dg308_1); }
            try { MacroCore.OnLog = null; } catch (Exception dg309_1) { Diag.Swallow("MacroForm.cs:309", dg309_1); }
            try { tmState.Stop(); tmState.Dispose(); } catch (Exception dg310_1) { Diag.Swallow("MacroForm.cs:310", dg310_1); }
            base.OnFormClosing(e);
        }
    }
}