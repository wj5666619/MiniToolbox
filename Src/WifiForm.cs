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
    class WifiForm : Form
    {
        bool en;
        List<WifiCore.Ap> aps = new List<WifiCore.Ap>();
        ListView lv;
        Label cur;
        Label usage;
        Label st;
        public StringBuilder DumpBuf = null;
        public bool AutoClose = false;

        public WifiForm(bool eng, string baseDir)
        {
            en = eng;
            Text = V71Ui.TT(en, "Wi-Fi 信号分析 · 小工具箱", "Wi-Fi analyzer · Mini Toolbox");
            BackColor = V71Ui.BG;
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "Wi-Fi 信号 / 信道分析", "Wi-Fi signal and channels"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "列出周围所有 AP 的信号与信道，算出哪个信道最空，再回路由器里把信道固定过去——比「自动」稳，延迟也低。",
                "Lists nearby APs with signal and channel, computes the emptiest channel, then pin it in the router."),
                20, 52, 960, 20));

            cur = V71Ui.Text(V71Ui.TT(en, "读取当前连接…", "Reading current link…"), 20, 80, 960, 44, 9.5F, V71Ui.TEXT);
            Controls.Add(cur);

            Button bScan = V71Ui.Btn(V71Ui.TT(en, "扫描", "Scan"), UiKit.A(34, 197, 94), 20, 130, 110, 30);
            bScan.Click += delegate { DoScan(); };
            Controls.Add(bScan);

            Button bCopy = V71Ui.Btn(V71Ui.TT(en, "复制结果", "Copy"), UiKit.A(52, 58, 70), 140, 130, 110, 30);
            bCopy.Click += delegate
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(cur.Text);
                    foreach (ListViewItem li in lv.Items)
                        sb.AppendLine(li.SubItems[0].Text + " " + li.SubItems[1].Text + " ch" + li.SubItems[2].Text +
                            " " + li.SubItems[3].Text + " " + li.SubItems[4].Text + " " + li.SubItems[5].Text);
                    sb.AppendLine(usage.Text);
                    Clipboard.SetText(sb.ToString());
                    st.ForeColor = V71Ui.GOOD;
                    st.Text = V71Ui.TT(en, "已复制到剪贴板", "Copied to clipboard");
                }
                catch (Exception dg73_1) { Diag.Swallow("WifiForm.cs:73", dg73_1); }
            };
            Controls.Add(bCopy);

            st = V71Ui.Note("", 264, 136, 696, 22);
            Controls.Add(st);

            lv = V71Ui.List(new string[] {
                V71Ui.TT(en, "SSID", "SSID"), V71Ui.TT(en, "信号", "Signal"), V71Ui.TT(en, "信道", "Channel"),
                V71Ui.TT(en, "频段", "Band"), V71Ui.TT(en, "身份验证", "Auth"), V71Ui.TT(en, "BSSID", "BSSID") },
                new int[] { 250, 70, 70, 90, 180, 190 }, 20, 170, 960, 276);
            Controls.Add(lv);

            usage = V71Ui.Text(V71Ui.TT(en, "点「扫描」开始", "Click Scan to start"), 20, 460, 960, 120, 9F, UiKit.A(150, 160, 176));
            Controls.Add(usage);

            Button close = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 860, 592, 100, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            DoScan();
        }

        void DoScan()
        {
            st.ForeColor = V71Ui.WARN;
            st.Text = V71Ui.TT(en, "扫描中…", "Scanning…");
            Application.DoEvents();
            string c = WifiCore.Current();
            cur.Text = c.Length == 0
                ? V71Ui.TT(en, "未连接无线网络 / 本机没有无线网卡", "No Wi-Fi link / no wireless adapter")
                : V71Ui.TT(en, "当前：", "Now: ") + c;
            aps = WifiCore.Scan();
            lv.Items.Clear();
            List<WifiCore.Ap> sorted = new List<WifiCore.Ap>(aps);
            sorted.Sort(delegate(WifiCore.Ap a, WifiCore.Ap b) { return b.Signal.CompareTo(a.Signal); });
            foreach (WifiCore.Ap a in sorted)
            {
                ListViewItem li = new ListViewItem(new string[] {
                    a.Ssid.Length == 0 ? V71Ui.TT(en, "（隐藏）", "(hidden)") : a.Ssid,
                    a.Signal + "%", a.Channel.ToString(), a.Band, a.Auth, a.Bssid });
                if (a.Signal >= 80) li.ForeColor = UiKit.A(120, 220, 160);
                else if (a.Signal >= 55) li.ForeColor = V71Ui.TEXT;
                else li.ForeColor = UiKit.A(140, 150, 165);
                lv.Items.Add(li);
            }
            if (aps.Count == 0)
            {
                st.ForeColor = V71Ui.BAD;
                st.Text = V71Ui.TT(en, "没扫到 AP：无线网卡关闭、或系统定位权限被关（netsh 需要它）", "No APs: adapter off, or location permission blocked");
                usage.Text = "";
                return;
            }
            st.ForeColor = V71Ui.GOOD;
            st.Text = V71Ui.TT(en, "扫描完成 ", "Done: ") + aps.Count + V71Ui.TT(en, " 个 AP", " APs");
            Dictionary<int, int> load = WifiCore.ChannelLoad(aps);
            List<int> keys = new List<int>();
            foreach (KeyValuePair<int, int> kv in load) keys.Add(kv.Key);
            keys.Sort();
            StringBuilder sb = new StringBuilder();
            sb.Append(V71Ui.TT(en, "信道占用：", "Channel use: "));
            foreach (int k in keys)
            {
                if (k > 14) continue;
                sb.Append("ch" + k + "×" + load[k] + "  ");
            }
            sb.AppendLine();
            List<int> keys5 = new List<int>();
            foreach (KeyValuePair<int, int> kv in load) if (kv.Key > 14) keys5.Add(kv.Key);
            keys5.Sort();
            if (keys5.Count > 0)
            {
                sb.Append(V71Ui.TT(en, "5G 占用：", "5G use: "));
                foreach (int k in keys5) sb.Append("ch" + k + "×" + load[k] + "  ");
                sb.AppendLine();
            }
            sb.Append(WifiCore.Advise(aps));
            usage.Text = sb.ToString();
        }
    }
}