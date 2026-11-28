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
    class PortForm : Form
    {
        bool en;
        string dir;
        ListView lv;
        Label st;
        TextBox flt;
        bool onlyListen;
        Button bCsv;                     // v12.0：导出 CSV
        Button bAuto;                    // v12.0：2 秒自动刷新开关
        System.Windows.Forms.Timer tmr;   // v12.0：自动刷新节拍
        bool autoOn = false;
        List<PortCore.Row> data = new List<PortCore.Row>();
        public StringBuilder DumpBuf = null;
        public bool AutoClose = false;

        public PortForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "端口与进程管家 · 小工具箱", "Ports & Processes · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1060, 700);
            MinimumSize = new Size(960, 640);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "端口与进程管家", "Ports & Processes"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "看清楚谁在占端口：TCP / UDP 监听与连接、对应进程与可执行文件，支持结束进程或整棵进程树。",
                "See who owns a port: TCP/UDP sockets, owning process and exe. Kill the process or the whole tree."), 20, 48, 1020, 24));

            flt = V71Ui.Box("", 20, 80, 250, 28);
            Controls.Add(flt);
            Label lf = V71Ui.Text(V71Ui.TT(en, "输入端口 / 进程名筛选", "Filter by port or process"), 278, 84, 220, 20, 8.5F, V71Ui.SUB);
            Controls.Add(lf);

            Button bRe = V71Ui.Btn(V71Ui.TT(en, "刷新", "Refresh"), UiKit.A(96, 165, 250), 500, 78, 80, 30);
            Button bKill = V71Ui.Btn(V71Ui.TT(en, "结束进程", "Kill"), UiKit.A(232, 82, 61), 588, 78, 104, 30);
            Button bTree = V71Ui.Btn(V71Ui.TT(en, "结束进程树", "Kill tree"), UiKit.A(190, 60, 45), 700, 78, 112, 30);
            Button bCopy = V71Ui.Btn(V71Ui.TT(en, "复制该行", "Copy row"), UiKit.A(52, 58, 70), 820, 78, 100, 30);
            Button bOnly = V71Ui.Btn(V71Ui.TT(en, "只看监听", "Listening only"), UiKit.A(34, 197, 94), 928, 78, 112, 30);
            Controls.Add(bRe); Controls.Add(bKill); Controls.Add(bTree); Controls.Add(bCopy); Controls.Add(bOnly);

            lv = V71Ui.List(new string[] {
                V71Ui.TT(en, "端口", "Port"), V71Ui.TT(en, "协议", "Proto"), V71Ui.TT(en, "本地地址", "Local"),
                V71Ui.TT(en, "远程地址", "Remote"), V71Ui.TT(en, "状态", "State"), V71Ui.TT(en, "进程", "Process"),
                "PID", V71Ui.TT(en, "可执行文件", "Executable") },
                new int[] { 70, 60, 190, 180, 95, 130, 60, 260 }, 20, 120, 1020, 500);
            Controls.Add(lv);

            st = V71Ui.Note("", 20, 628, 840, 24);
            Controls.Add(st);

            // v12.0：市面级连接查看器标配——导出清单、定时自动刷新（对比 TCPView / Process Explorer）
            bCsv = V71Ui.Btn(V71Ui.TT(en, "导出 CSV", "Export CSV"), UiKit.A(52, 58, 70), 640, 658, 110, 32);
            bAuto = V71Ui.Btn(V71Ui.TT(en, "自动刷新：关", "Auto: off"), UiKit.A(52, 58, 70), 760, 658, 168, 32);
            Controls.Add(bCsv); Controls.Add(bAuto);
            bCsv.Click += delegate { ExportCsv(); };
            bAuto.Click += delegate
            {
                autoOn = !autoOn;
                bAuto.Text = autoOn ? V71Ui.TT(en, "自动刷新：2 秒", "Auto: every 2s") : V71Ui.TT(en, "自动刷新：关", "Auto: off");
                bAuto.BackColor = autoOn ? UiKit.A(22, 138, 66) : UiKit.A(52, 58, 70);
                if (autoOn) { tmr.Start(); Reload(); } else tmr.Stop();
            };
            tmr = new System.Windows.Forms.Timer();
            tmr.Interval = 2000;
            tmr.Tick += delegate { if (autoOn) Reload(); };

            Button close = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 940, 660, 100, 32);
            Controls.Add(close);
            close.Click += delegate { Close(); };

            bRe.Click += delegate { Reload(); };
            bKill.Click += delegate { DoKill(false); };
            bTree.Click += delegate { DoKill(true); };
            bCopy.Click += delegate { CopyRow(); };
            bOnly.Click += delegate
            {
                onlyListen = !onlyListen;
                bOnly.Text = V71Ui.TT(en, onlyListen ? "显示全部" : "只看监听", onlyListen ? "Show all" : "Listening only");
                bOnly.BackColor = onlyListen ? UiKit.A(22, 138, 66) : UiKit.A(34, 197, 94);
                Fill();
            };
            flt.TextChanged += delegate { Fill(); };
            lv.DoubleClick += delegate { DoKill(false); };

            Reload();
        }

        void Reload()
        {
            PortCore.ClearCache();
            data = PortCore.Scan();
            Fill();
            st.Text = V71Ui.TT(en,
                "共 " + data.Count + " 条连接 / 监听 " + PortCore.ListenCount(data) + " 个，占用端口 " + PortCore.DistinctPortCount(data) + " 个",
                data.Count + " sockets, " + PortCore.ListenCount(data) + " listening, " + PortCore.DistinctPortCount(data) + " ports");
        }

        static bool IsListening(PortCore.Row r)
        {
            if (string.Equals(r.State, "LISTENING", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(r.Proto, "UDP", StringComparison.OrdinalIgnoreCase) && r.State == "-") return true;
            return false;
        }

        void Fill()
        {
            string q = (flt.Text == null ? "" : flt.Text.Trim().ToLowerInvariant());
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (PortCore.Row r in data)
            {
                if (onlyListen && !IsListening(r)) continue;
                if (q.Length > 0)
                {
                    string hay = (r.LPort + " " + r.Remote + " " + r.Proc + " " + r.Pid + " " + r.Exe).ToLowerInvariant();
                    if (hay.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                }
                ListViewItem it = new ListViewItem(r.LPort == 0 ? "-" : r.LPort.ToString());
                it.SubItems.Add(r.Proto);
                it.SubItems.Add(r.Local);
                it.SubItems.Add(r.Remote);
                it.SubItems.Add(r.State);
                it.SubItems.Add(r.Proc);
                it.SubItems.Add(r.Pid.ToString());
                it.SubItems.Add(r.Exe);
                it.Tag = r;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }

        // v12.0：把当前列表（即已按筛选条件过滤后的结果）导出成 CSV，方便贴给别人看或归档
        void ExportCsv()
        {
            if (lv.Items.Count == 0) { st.Text = V71Ui.TT(en, "列表为空，没有可导出的内容。", "Nothing to export."); return; }
            SaveFileDialog sd = new SaveFileDialog();
            sd.Filter = "CSV|*.csv";
            sd.FileName = "端口清单_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            if (sd.ShowDialog(this) != DialogResult.OK) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Port,Proto,Local,Remote,State,Process,PID,Executable");
            for (int i = 0; i < lv.Items.Count; i++)
            {
                ListViewItem it = lv.Items[i];
                StringBuilder row = new StringBuilder();
                for (int c = 0; c < it.SubItems.Count; c++)
                {
                    if (c > 0) row.Append(',');
                    row.Append(CsvCell(it.SubItems[c].Text));
                }
                sb.AppendLine(row.ToString());
            }
            try
            {
                File.WriteAllText(sd.FileName, sb.ToString(), new UTF8Encoding(true));
                st.Text = V71Ui.TT(en, "已导出 " + lv.Items.Count + " 行 → ", "Exported " + lv.Items.Count + " rows → ") + sd.FileName;
            }
            catch (Exception ex) { st.Text = V71Ui.TT(en, "导出失败：", "Export failed: ") + ex.Message; }
        }

        static string CsvCell(string v)
        {
            if (v == null) return "";
            if (v.IndexOf('"') >= 0 || v.IndexOf(',') >= 0 || v.IndexOf('\n') >= 0)
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        PortCore.Row Sel()
        {
            if (lv.SelectedItems.Count == 0) return null;
            return (PortCore.Row)lv.SelectedItems[0].Tag;
        }

        void DoKill(bool tree)
        {
            PortCore.Row r = Sel();
            if (r == null) { V71Ui.Say(this, V71Ui.TT(en, "先选一行。", "Select a row first.")); return; }
            string ask = V71Ui.TT(en, "结束进程 ", "Kill ") + r.Proc + " (PID " + r.Pid + ")" + V71Ui.TT(en, " ？", " ?");
            if (MessageBox.Show(this, ask, "小工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            string err = "";
            bool ok = tree ? PortCore.KillTree(r.Pid, out err) : PortCore.KillPid(r.Pid, out err);
            if (!ok) V71Ui.Say(this, V71Ui.TT(en, "失败：", "Failed: ") + err);
            Reload();
        }

        void CopyRow()
        {
            PortCore.Row r = Sel();
            if (r == null) return;
            string s = r.LPort + "\t" + r.Proto + "\t" + r.Local + "\t" + r.Remote + "\t" + r.State + "\t" + r.Proc + "\t" + r.Pid + "\t" + r.Exe;
            try { Clipboard.SetText(s); st.Text = V71Ui.TT(en, "已复制到剪贴板", "Copied"); } catch (Exception dg219_1) { Diag.Swallow("PortForm.cs:219", dg219_1); }
        }
    }
}