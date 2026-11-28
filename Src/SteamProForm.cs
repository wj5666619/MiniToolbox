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
    class SteamProForm : Form
    {
        bool en;
        string dir;
        ListView lv;
        ListView lvNode;
        ComboBox cbTarget;
        Label st;
        List<SteamProCore.Lib> libs = new List<SteamProCore.Lib>();
        List<string> libPaths = new List<string>();

        public SteamProForm(bool eng, string baseDir)
        {
            en = eng; dir = baseDir;
            Text = en ? "Steam Tools · Mini Toolbox" : "Steam 进阶 · 小工具箱";
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1020, 680);
            MinimumSize = new Size(900, 620);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "Steam 进阶", "Steam Tools"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "库清单导出 CSV / 游戏库搬家（不断链）/ 下载节点延迟排序。搬家前请先退出 Steam",
                "Export library CSV, move games between drives safely, rank download nodes. Quit Steam before moving"), 20, 48, 970, 20));

            lv = V71Ui.List(new string[] { V71Ui.TT(en, "游戏", "Game"), "AppID", V71Ui.TT(en, "大小", "Size"), V71Ui.TT(en, "所在库", "Library") },
                new int[] { 420, 90, 100, 370 }, 20, 76, 980, 380);
            Controls.Add(lv);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "搬到哪个库", "Move to"), 20, 468, 80, 20, 9F, V71Ui.SUB));
            cbTarget = V71Ui.Combo(new string[] { "-" }, 100, 464, 360);
            Controls.Add(cbTarget);

            Button bMove = V71Ui.Btn(V71Ui.TT(en, "搬家选中游戏", "Move selected"), UiKit.A(232, 82, 61), 470, 462, 140, 30);
            bMove.Click += delegate { MoveSelected(); };
            Controls.Add(bMove);

            Button bReload = V71Ui.Btn(V71Ui.TT(en, "刷新库", "Refresh"), UiKit.A(59, 130, 246), 20, 506, 100, 32);
            bReload.Click += delegate { Reload(); };
            Controls.Add(bReload);

            Button bCsv = V71Ui.Btn(V71Ui.TT(en, "导出 CSV", "Export CSV"), UiKit.A(34, 197, 94), 130, 506, 110, 32);
            bCsv.Click += delegate
            {
                if (libs.Count == 0) { V71Ui.Say(this, V71Ui.TT(en, "先点刷新读取游戏库", "Refresh first")); return; }
                string p = SteamProCore.ExportCsv(libs, dir);
                st.Text = p.Length > 0 ? V71Ui.TT(en, "已导出：" + p, "exported: " + p) : V71Ui.TT(en, "导出失败", "export failed");
                st.ForeColor = p.Length > 0 ? V71Ui.GOOD : V71Ui.BAD;
            };
            Controls.Add(bCsv);

            Button bNode = V71Ui.Btn(V71Ui.TT(en, "节点测速", "Node latency"), UiKit.A(56, 189, 248), 250, 506, 110, 32);
            bNode.Click += delegate { TestNodes(); };
            Controls.Add(bNode);

            Button bWish = V71Ui.Btn(V71Ui.TT(en, "愿望单降价", "Wishlist drops"), UiKit.A(250, 190, 90), 370, 506, 120, 32);
            bWish.Click += delegate { try { WishForm wf = new WishForm(en, dir); wf.ShowDialog(this); } catch (Exception dg80_1) { Diag.Swallow("SteamProForm.cs:80", dg80_1); } };
            Controls.Add(bWish);

            Button bHealth = V71Ui.Btn(V71Ui.TT(en, "库体检 + 一键修", "Library health"), UiKit.A(232, 82, 61), 496, 506, 140, 32);
            bHealth.Click += delegate { try { LibHealthForm lf = new LibHealthForm(en, dir); lf.ShowDialog(this); } catch (Exception dg84_1) { Diag.Swallow("SteamProForm.cs:84", dg84_1); } };
            Controls.Add(bHealth);

            lvNode = V71Ui.List(new string[] { V71Ui.TT(en, "节点", "Node"), V71Ui.TT(en, "延迟", "Latency") },
                new int[] { 250, 110 }, 640, 470, 360, 130);
            Controls.Add(lvNode);

            st = V71Ui.Text(V71Ui.TT(en, "读取中…", "Loading…"), 20, 552, 600, 20, 9F, V71Ui.SUB);
            Controls.Add(st);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(43, 58, 80), 900, 630, 100, 32);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);

            Reload();
        }

        void Reload()
        {
            libs = SteamProCore.Libraries();
            lv.Items.Clear();
            libPaths.Clear();
            cbTarget.Items.Clear();
            int total = 0;
            foreach (SteamProCore.Lib l in libs) libPaths.Add(l.Path);
            foreach (SteamProCore.Lib l in libs)
            {
                cbTarget.Items.Add(l.Path + "  (" + SteamProCore.GbText(SteamProCore.GamesBytes(l)) + ")");
                foreach (SteamProCore.Game g in l.Games)
                {
                    ListViewItem li = new ListViewItem(new string[] { g.Name, g.AppId.ToString(), SteamProCore.GbText(g.Size), g.LibPath });
                    li.Tag = g;
                    lv.Items.Add(li);
                    total++;
                }
            }
            if (cbTarget.Items.Count > 0) cbTarget.SelectedIndex = 0;
            lv.Invalidate();
            st.Text = libs.Count == 0 ? V71Ui.TT(en, "没找到 Steam 游戏库（检查是否装过 Steam）", "no Steam library found")
                : V71Ui.TT(en, "共 " + libs.Count + " 个库 / " + total + " 款游戏", libs.Count + " libraries / " + total + " games");
            st.ForeColor = libs.Count == 0 ? V71Ui.WARN : V71Ui.SUB;
        }

        void MoveSelected()
        {
            if (lv.SelectedItems.Count == 0) { V71Ui.Say(this, V71Ui.TT(en, "先在列表里选一款游戏", "Select a game first")); return; }
            if (cbTarget.SelectedIndex < 0 || cbTarget.SelectedIndex >= libPaths.Count) { V71Ui.Say(this, V71Ui.TT(en, "先选目标库", "Pick a target library")); return; }
            SteamProCore.Game g = (SteamProCore.Game)lv.SelectedItems[0].Tag;
            string target = libPaths[cbTarget.SelectedIndex];
            if (target == g.LibPath) { V71Ui.Say(this, V71Ui.TT(en, "已经在目标库里了", "Already in that library")); return; }
            if (Process.GetProcessesByName("steam").Length > 0)
            {
                V71Ui.Say(this, V71Ui.TT(en, "请先退出 Steam 再搬家，否则文件会被占用。", "Quit Steam first, files are locked otherwise."));
                return;
            }
            if (MessageBox.Show(this, V71Ui.TT(en, "把「" + g.Name + "」从\n" + g.LibPath + "\n搬到\n" + target + "\n\n期间搬完会自动搬 appmanifest，Steam 里不用重新下载。继续？",
                "Move \"" + g.Name + "\" from\n" + g.LibPath + "\nto\n" + target + "\n\nThe appmanifest moves too, so Steam won't re-download. Continue?"),
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            st.Text = V71Ui.TT(en, "正在搬家…", "moving…");
            st.ForeColor = V71Ui.WARN;
            string err = "";
            bool ok = SteamProCore.MoveGame(g, target, out err);
            st.Text = ok ? V71Ui.TT(en, "搬家完成：" + g.Name, "done: " + g.Name) : V71Ui.TT(en, "搬家失败：" + err, "move failed: " + err);
            st.ForeColor = ok ? V71Ui.GOOD : V71Ui.BAD;
            if (ok) Reload();
        }

        void TestNodes()
        {
            lvNode.Items.Clear();
            st.Text = V71Ui.TT(en, "正在测节点…", "measuring nodes…");
            st.ForeColor = V71Ui.WARN;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                List<KeyValuePair<string, int>> l = SteamProCore.NodeLatency();
                try
                {
                    Invoke(new MethodInvoker(delegate
                    {
                        foreach (KeyValuePair<string, int> kv in l)
                        {
                            ListViewItem li = new ListViewItem(new string[] { kv.Key, kv.Value < 0 ? "-" : kv.Value + " ms" });
                            if (kv.Value >= 0 && kv.Value < 80) li.ForeColor = V71Ui.GOOD;
                            else if (kv.Value > 300) li.ForeColor = V71Ui.BAD;
                            lvNode.Items.Add(li);
                        }
                        lvNode.Invalidate();
                        st.Text = V71Ui.TT(en, "延迟越低越顺；- 表示连不上（被墙或域名变更）", "lower is better; - means unreachable");
                        st.ForeColor = V71Ui.SUB;
                    }));
                }
                catch (Exception dg175_1) { Diag.Swallow("SteamProForm.cs:175", dg175_1); }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}