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
    class LatencyForm : Form
    {
        bool en;
        List<LatencyCore.Target> targets = new List<LatencyCore.Target>();
        ListView lv;
        TextBox tbCust;
        Label st;
        Label adv;
        Button bRun;
        Thread runner = null;
        public StringBuilder DumpBuf = null;
        public bool AutoClose = false;

        public LatencyForm(bool eng, string baseDir)
        {
            en = eng;
            Text = V71Ui.TT(en, "延迟优选 · 小工具箱", "Latency picker · Mini Toolbox");
            BackColor = V71Ui.BG;
            ClientSize = new Size(1000, 620);
            MinimumSize = new Size(1000, 620);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "延迟优选", "Latency picker"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "对 Steam / Epic / 平台登录节点做 TCP 握手测速，横向比较哪条线路最快，用来挑下载区、代理节点或加速器线路。",
                "TCP handshake timing against Steam / Epic / launcher endpoints: pick the fastest route or proxy line."),
                20, 52, 960, 20));

            targets = LatencyCore.Presets();
            lv = V71Ui.List(new string[] {
                V71Ui.TT(en, "目标", "Target"), V71Ui.TT(en, "主机", "Host"), V71Ui.TT(en, "端口", "Port"),
                V71Ui.TT(en, "最低", "Min"), V71Ui.TT(en, "平均", "Avg"), V71Ui.TT(en, "结果", "Result") },
                new int[] { 200, 250, 70, 90, 90, 240 }, 20, 104, 960, 302);
            Controls.Add(lv);

            bRun = V71Ui.Btn(V71Ui.TT(en, "开始测速", "Run"), UiKit.A(34, 197, 94), 20, 418, 120, 32);
            bRun.Click += delegate { RunTest(); };
            Controls.Add(bRun);

            Button bCopy = V71Ui.Btn(V71Ui.TT(en, "复制结果", "Copy"), UiKit.A(52, 58, 70), 150, 418, 110, 32);
            bCopy.Click += delegate
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (ListViewItem li in lv.Items)
                        sb.AppendLine(li.SubItems[0].Text + " " + li.SubItems[1].Text + ":" + li.SubItems[2].Text +
                            " min=" + li.SubItems[3].Text + " avg=" + li.SubItems[4].Text + " " + li.SubItems[5].Text);
                    Clipboard.SetText(sb.ToString());
                    st.ForeColor = V71Ui.GOOD;
                    st.Text = V71Ui.TT(en, "已复制到剪贴板", "Copied to clipboard");
                }
                catch (Exception dg77_1) { Diag.Swallow("LatencyForm.cs:77", dg77_1); }
            };
            Controls.Add(bCopy);

            Button bDel = V71Ui.Btn(V71Ui.TT(en, "删除选中", "Remove"), UiKit.A(232, 82, 61), 270, 418, 110, 32);
            bDel.Click += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                int i = lv.SelectedItems[0].Index;
                lv.Items.RemoveAt(i);
                if (i >= 0 && i < targets.Count) targets.RemoveAt(i);
                st.Text = V71Ui.TT(en, "已删除 1 项", "Removed one target");
            };
            Controls.Add(bDel);

            Controls.Add(V71Ui.Note(V71Ui.TT(en, "自定义目标（主机 或 主机:端口）", "Custom target (host or host:port)"), 20, 462, 260, 18));
            tbCust = V71Ui.Box("example.com:80", 288, 458, 300, 26);
            Controls.Add(tbCust);
            Button bAdd = V71Ui.Btn(V71Ui.TT(en, "加入列表", "Add"), UiKit.A(96, 165, 250), 598, 456, 100, 30);
            bAdd.Click += delegate
            {
                string h;
                int p;
                if (!LatencyCore.ParseTarget(tbCust.Text, out h, out p))
                {
                    st.ForeColor = V71Ui.BAD;
                    st.Text = V71Ui.TT(en, "格式不对：写 host 或 host:port", "Bad format: use host or host:port");
                    return;
                }
                LatencyCore.Target t = LatencyCore.Make(h, h, p);
                targets.Add(t);
                AddRow(t);
                st.ForeColor = V71Ui.GOOD;
                st.Text = V71Ui.TT(en, "已加入 ", "Added ") + h + ":" + p;
            };
            Controls.Add(bAdd);

            st = V71Ui.Note(V71Ui.TT(en, "还没开始测速", "Not measured yet"), 20, 498, 960, 20);
            Controls.Add(st);

            adv = V71Ui.Text("", 20, 522, 960, 48, 9F, UiKit.A(150, 160, 176));
            Controls.Add(adv);

            Button close = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 880, 570, 100, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Fill();
        }

        void AddRow(LatencyCore.Target t)
        {
            ListViewItem li = new ListViewItem(new string[] {
                t.Name, t.Host, t.Port.ToString(), "-", "-", V71Ui.TT(en, "未测", "idle") });
            li.Tag = t;
            lv.Items.Add(li);
        }

        void Fill()
        {
            lv.Items.Clear();
            foreach (LatencyCore.Target t in targets) AddRow(t);
        }

        void RowOf(string host, int port)
        {
            foreach (ListViewItem li in lv.Items)
            {
                LatencyCore.Target t = (LatencyCore.Target)li.Tag;
                if (t.Host == host && t.Port == port)
                {
                    li.SubItems[3].Text = t.Min < 0 ? "-" : t.Min.ToString("0");
                    li.SubItems[4].Text = t.Avg < 0 ? "-" : t.Avg.ToString("0");
                    li.SubItems[5].Text = t.Detail;
                    if (t.Avg < 0) li.ForeColor = UiKit.A(248, 113, 113);
                    else if (t.Avg < 60) li.ForeColor = UiKit.A(120, 220, 160);
                    else if (t.Avg < 120) li.ForeColor = V71Ui.TEXT;
                    else li.ForeColor = UiKit.A(250, 190, 90);
                }
            }
        }

        void RunTest()
        {
            if (runner != null && runner.IsAlive) return;
            bRun.Enabled = false;
            st.ForeColor = V71Ui.WARN;
            st.Text = V71Ui.TT(en, "测速中（每项 3 次）…", "Measuring, 3 tries each…");
            adv.Text = "";
            foreach (LatencyCore.Target t in targets) { t.Min = -1; t.Avg = -1; t.Detail = "wait"; }
            Fill();
            runner = new Thread(new ThreadStart(delegate()
            {
                LatencyCore.ProbeAll(targets, 3, delegate(LatencyCore.Target t)
                {
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                            Invoke((Action)delegate { RowOf(t.Host, t.Port); });
                    }
                    catch (Exception dg177_1) { Diag.Swallow("LatencyForm.cs:177", dg177_1); }
                });
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        Invoke((Action)delegate
                        {
                            bRun.Enabled = true;
                            string fast = LatencyCore.Fastest(targets);
                            if (fast.Length == 0)
                            {
                                st.ForeColor = V71Ui.BAD;
                                st.Text = V71Ui.TT(en, "全部不可达：检查网络或先关掉代理再测", "All unreachable: check the network, or turn the proxy off first");
                                adv.Text = "";
                            }
                            else
                            {
                                st.ForeColor = V71Ui.GOOD;
                                st.Text = V71Ui.TT(en, "测速完成，最快：", "Done, fastest: ") + fast;
                                adv.Text = V71Ui.TT(en,
                                    "怎么用：下载慢就挑「Steam 下载 CDN」最低的那条；代理节点同理，先在这里比出基线，再回代理节点管理里按延迟选节点。绿色 <60ms、黄色 <120ms、红色不可达。",
                                    "How to use: pick the fastest CDN row for downloads; use this baseline before choosing a proxy node. Green <60ms, yellow <120ms, red unreachable.");
                            }
                        });
                    }
                }
                catch (Exception dg204_1) { Diag.Swallow("LatencyForm.cs:204", dg204_1); }
            }));
            runner.IsBackground = true;
            runner.Start();
        }
    }
}