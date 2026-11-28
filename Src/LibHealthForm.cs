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
    class LibHealthForm : Form
    {
        bool en;
        string dir;
        ListView lv;
        Label st;
        List<SteamProCore.Lib> libs = new List<SteamProCore.Lib>();
        List<LibHealthCore.Found> found = new List<LibHealthCore.Found>();
        int totalGames = 0;
        long totalBytes = 0;

        public LibHealthForm(bool eng, string baseDir)
        {
            en = eng; dir = baseDir;
            Text = en ? "Library health · Mini Toolbox" : "游戏库体检 · 小工具箱";
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(920, 620);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "游戏库体检", "Library health"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "只看不动：查「需要更新 / 没装完 / 文件缺失 / 孤儿目录」，一键修交给 Steam 校验或续传，不替你删东西",
                "Read-only check for updates, partial installs, missing folders and orphans; Fix hands it to Steam"), 20, 50, 960, 22));

            lv = V71Ui.List(new string[] { V71Ui.TT(en, "游戏", "Game"), "AppID", V71Ui.TT(en, "问题", "Problem"), V71Ui.TT(en, "体积", "Size"), V71Ui.TT(en, "说明", "Detail") },
                new int[] { 330, 80, 100, 100, 350 }, 20, 90, 960, 430);
            Controls.Add(lv);

            st = V71Ui.Text(V71Ui.TT(en, "点「体检」开始", "Press Check to start"), 20, 528, 960, 20, 9F, V71Ui.SUB);
            Controls.Add(st);

            Button bScan = V71Ui.Btn(V71Ui.TT(en, "体检", "Check"), UiKit.A(59, 130, 246), 20, 562, 100, 34);
            bScan.Click += delegate { DoScan(); };
            Controls.Add(bScan);

            Button bFixAll = V71Ui.Btn(V71Ui.TT(en, "一键修全部异常", "Fix all"), UiKit.A(232, 82, 61), 130, 562, 150, 34);
            bFixAll.Click += delegate { FixAll(); };
            Controls.Add(bFixAll);

            Button bFixOne = V71Ui.Btn(V71Ui.TT(en, "修选中", "Fix selected"), UiKit.A(250, 190, 90), 290, 562, 100, 34);
            bFixOne.Click += delegate { FixOne(); };
            Controls.Add(bFixOne);

            Button bOpen = V71Ui.Btn(V71Ui.TT(en, "打开所在库", "Open library"), UiKit.A(45, 212, 191), 400, 562, 120, 34);
            bOpen.Click += delegate
            {
                LibHealthCore.Found f = Sel();
                if (f == null) return;
                try { Process.Start("explorer.exe", Path.Combine(f.LibPath, "steamapps", "common")); } catch (Exception dg73_1) { Diag.Swallow("LibHealthForm.cs:73", dg73_1); }
            };
            Controls.Add(bOpen);

            Button bCsv = V71Ui.Btn(V71Ui.TT(en, "导出报告", "Export report"), UiKit.A(139, 92, 246), 530, 562, 110, 34);
            bCsv.Click += delegate
            {
                if (found.Count == 0 && totalGames == 0) { V71Ui.Say(this, V71Ui.TT(en, "先体检", "Check first")); return; }
                st.Text = LibHealthCore.Export(found, totalGames, totalBytes, dir); st.ForeColor = V71Ui.GOOD;
            };
            Controls.Add(bCsv);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(43, 58, 80), 880, 562, 100, 34);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);
        }

        void DoScan()
        {
            st.Text = V71Ui.TT(en, "扫描中…", "Scanning…");
            Application.DoEvents();
            libs = SteamProCore.Libraries();
            found = LibHealthCore.Scan(libs, out totalGames, out totalBytes);
            lv.Items.Clear();
            foreach (LibHealthCore.Found f in found)
            {
                ListViewItem row = new ListViewItem(f.Name);
                row.SubItems.Add(f.AppId > 0 ? f.AppId.ToString() : "-");
                row.SubItems.Add(LibHealthCore.KindZh(f.Kind));
                row.SubItems.Add(f.Size > 0 ? SteamProCore.GbText(f.Size) : "-");
                row.SubItems.Add(f.Detail);
                row.ForeColor = LibHealthCore.KindColor(f.Kind);
                row.Tag = f;
                lv.Items.Add(row);
            }
            int upd = 0, par = 0, mis = 0, orph = 0;
            foreach (LibHealthCore.Found f in found)
            {
                if (f.Kind == "update") upd++;
                else if (f.Kind == "partial") par++;
                else if (f.Kind == "missing") mis++;
                else orph++;
            }
            st.Text = V71Ui.TT(en,
                "共 " + totalGames + " 款 / " + SteamProCore.GbText(totalBytes) + "；异常 " + found.Count
                + " 条 —— 需要更新 " + upd + " / 没装完 " + par + " / 文件缺失 " + mis + " / 孤儿目录 " + orph,
                totalGames + " titles / " + SteamProCore.GbText(totalBytes) + "; " + found.Count + " issues (update " + upd
                + " / partial " + par + " / missing " + mis + " / orphan " + orph + ")");
            st.ForeColor = found.Count > 0 ? V71Ui.WARN : V71Ui.GOOD;
        }

        LibHealthCore.Found Sel()
        {
            if (lv.SelectedItems.Count == 0) { V71Ui.Say(this, V71Ui.TT(en, "先选一行", "Select a row first")); return null; }
            return (LibHealthCore.Found)lv.SelectedItems[0].Tag;
        }

        void FixOne()
        {
            LibHealthCore.Found f = Sel();
            if (f == null) return;
            string msg = "";
            bool ok = LibHealthCore.Fix(f, out msg);
            st.Text = msg; st.ForeColor = ok ? V71Ui.GOOD : V71Ui.WARN;
        }

        void FixAll()
        {
            if (found.Count == 0) { V71Ui.Say(this, V71Ui.TT(en, "先体检", "Check first")); return; }
            int cnt = 0, skip = 0;
            foreach (LibHealthCore.Found f in found)
            {
                if (f.Kind == "orphan") { skip++; continue; }
                string msg = "";
                if (LibHealthCore.Fix(f, out msg)) cnt++;
                Application.DoEvents();
                System.Threading.Thread.Sleep(1200);
            }
            st.Text = V71Ui.TT(en, "已交给 Steam " + cnt + " 款（它会在后台校验/续传）；孤儿目录 " + skip + " 个未动",
                "Handed " + cnt + " titles to Steam; " + skip + " orphan folders left alone");
            st.ForeColor = V71Ui.GOOD;
        }

        public string DumpGeometry()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("LibHealthForm " + ClientSize.Width + "x" + ClientSize.Height + "\r\n");
            foreach (Control c in Controls)
            {
                string nm = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
                if (nm.Length > 26) nm = nm.Substring(0, 26);
                sb.Append("  " + c.GetType().Name + " " + c.Left + "," + c.Top + " " + c.Width + "x" + c.Height + " \"" + nm + "\"\r\n");
            }
            return sb.ToString();
        }

        public void ScanForTest() { DoScan(); }
        public string ReportForTest()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("total=" + totalGames + " bytes=" + totalBytes + " issues=" + found.Count + "\r\n");
            foreach (LibHealthCore.Found f in found)
                sb.Append("  " + LibHealthCore.KindZh(f.Kind) + " | " + f.Name + " (" + f.AppId + ") | " + f.Detail + " | " + f.LibPath + "\r\n");
            return sb.ToString();
        }
    }
}