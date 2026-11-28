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
    class UpnpForm : Form
    {
        bool en;
        List<UpnpCore.Device> devs = new List<UpnpCore.Device>();
        ListView lvDev;
        ListView lvMap;
        TextBox tbPort;
        TextBox tbDesc;
        TextBox tbIp;
        ComboBox cbProto;
        Label st;
        Label st2;
        Button bSearch;
        Button bAdd;
        Button bDel;
        Button bList;
        public StringBuilder DumpBuf = null;
        public bool AutoClose = false;

        public UpnpForm(bool eng, string baseDir)
        {
            en = eng;
            Text = V71Ui.TT(en, "端口映射 / 开服助手 · 小工具箱", "UPnP port mapping · Mini Toolbox");
            BackColor = V71Ui.BG;
            ClientSize = new Size(980, 660);
            MinimumSize = new Size(980, 660);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "端口映射 / 开服助手", "UPnP port mapping"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "用路由器自带的 UPnP 把本机端口映射到外网，开私服不用再登路由器后台；只动映射表，不改路由器其它设置。",
                "Map a local port to the internet through the router's own UPnP. Nothing but the mapping table is touched."),
                20, 52, 940, 20));

            bSearch = V71Ui.Btn(V71Ui.TT(en, "搜索路由器", "Find router"), UiKit.A(96, 165, 250), 20, 84, 120, 30);
            bSearch.Click += delegate { Search(); };
            Controls.Add(bSearch);

            Button bExt = V71Ui.Btn(V71Ui.TT(en, "取外网 IP", "External IP"), UiKit.A(14, 165, 233), 148, 84, 110, 30);
            bExt.Click += delegate { RefreshExt(); };
            Controls.Add(bExt);

            st = V71Ui.Note(V71Ui.TT(en, "本机 IPv4 ", "Local IPv4 ") + UpnpCore.LocalIp() +
                V71Ui.TT(en, " · 点「搜索路由器」开始", " · click Find router to start"), 268, 90, 692, 22);
            Controls.Add(st);

            lvDev = V71Ui.List(new string[] {
                V71Ui.TT(en, "路由器", "Router"), V71Ui.TT(en, "控制地址", "Control URL"),
                V71Ui.TT(en, "外网 IP", "External IP"), V71Ui.TT(en, "设备地址", "Device IP") },
                new int[] { 250, 330, 180, 160 }, 20, 118, 940, 108);
            lvDev.SelectedIndexChanged += delegate { PickDevice(); };
            Controls.Add(lvDev);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "端口映射", "Port mapping"), 20, 240, 200, 22, 9.5F, Color.White));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "外部端口", "Ext port"), 20, 264, 90, 18));
            tbPort = V71Ui.Box("25565", 20, 282, 90, 26);
            Controls.Add(tbPort);
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "协议", "Proto"), 122, 264, 70, 18));
            cbProto = V71Ui.Combo(new string[] { "TCP", "UDP" }, 122, 282, 80);
            Controls.Add(cbProto);
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "描述", "Description"), 214, 264, 120, 18));
            tbDesc = V71Ui.Box(V71Ui.TT(en, "小工具箱开服", "MiniToolbox server"), 214, 282, 180, 26);
            Controls.Add(tbDesc);
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "本机 IPv4", "Local IPv4"), 406, 264, 120, 18));
            tbIp = V71Ui.Box(UpnpCore.LocalIp(), 406, 282, 130, 26);
            Controls.Add(tbIp);

            bAdd = V71Ui.Btn(V71Ui.TT(en, "添加映射", "Add"), UiKit.A(34, 197, 94), 550, 280, 110, 30);
            bAdd.Click += delegate { AddOne(); };
            Controls.Add(bAdd);

            bDel = V71Ui.Btn(V71Ui.TT(en, "删除映射", "Delete"), UiKit.A(232, 82, 61), 670, 280, 110, 30);
            bDel.Click += delegate { DelOne(); };
            Controls.Add(bDel);

            bList = V71Ui.Btn(V71Ui.TT(en, "读取映射表", "Read table"), UiKit.A(52, 58, 70), 790, 280, 130, 30);
            bList.Click += delegate { LoadTable(); };
            Controls.Add(bList);

            lvMap = V71Ui.List(new string[] {
                V71Ui.TT(en, "外部端口", "Ext port"), V71Ui.TT(en, "协议", "Proto"),
                V71Ui.TT(en, "内部地址", "Internal"), V71Ui.TT(en, "描述", "Description"),
                V71Ui.TT(en, "启用", "On") },
                new int[] { 110, 70, 260, 380, 100 }, 20, 322, 940, 190);
            Controls.Add(lvMap);

            st2 = V71Ui.Note(V71Ui.TT(en,
                "说明：映射只在本机和路由器同时可达时生效；外部端口建议 1024 以上，路由器上「UPnP」需为开启状态。",
                "Note: mapping works only when both sides are reachable; use an external port above 1024 and keep UPnP enabled."),
                20, 522, 940, 44);
            Controls.Add(st2);

            Button bCopy = V71Ui.Btn(V71Ui.TT(en, "复制结果", "Copy"), UiKit.A(52, 58, 70), 730, 600, 120, 32);
            bCopy.Click += delegate
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (ListViewItem li in lvDev.Items)
                        sb.AppendLine(li.Text + " | " + string.Join(" | ", li.SubItems[1].Text, li.SubItems[2].Text, li.SubItems[3].Text));
                    foreach (ListViewItem li in lvMap.Items)
                        sb.AppendLine(li.Text + " | " + string.Join(" | ", li.SubItems[1].Text, li.SubItems[2].Text, li.SubItems[3].Text, li.SubItems[4].Text));
                    Clipboard.SetText(sb.ToString());
                    st.Text = V71Ui.TT(en, "已复制到剪贴板", "Copied to clipboard");
                }
                catch (Exception dg130_1) { Diag.Swallow("UpnpForm.cs:130", dg130_1); }
            };
            Controls.Add(bCopy);

            Button close = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 860, 600, 100, 32);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            bAdd.Enabled = false;
            bDel.Enabled = false;
            bList.Enabled = false;
        }

        void Search()
        {
            bSearch.Enabled = false;
            st.ForeColor = V71Ui.WARN;
            st.Text = V71Ui.TT(en, "正在搜索路由器（最多 7 秒）…", "Searching for routers (up to 7s)…");
            Thread t = new Thread(new ThreadStart(delegate()
            {
                List<UpnpCore.Device> found = UpnpCore.Discover(7000);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        Invoke((Action)delegate
                        {
                            devs = found;
                            FillDev();
                            bSearch.Enabled = true;
                            if (found.Count > 0)
                            {
                                st.ForeColor = V71Ui.GOOD;
                                st.Text = V71Ui.TT(en, "找到 ", "Found ") + found.Count +
                                    V71Ui.TT(en, " 台路由器 · 本机 ", " router(s) · local ") + UpnpCore.LocalIp();
                                bAdd.Enabled = true;
                            }
                            else
                            {
                                st.ForeColor = V71Ui.BAD;
                                st.Text = V71Ui.TT(en,
                                    "没找到支持 UPnP 的路由器：确认路由器管理页里 UPnP / NAT-PMP 已开启，或在 Windows 防火墙放行。",
                                    "No UPnP router found: enable UPnP in the router page, or allow it through the firewall.");
                            }
                        });
                    }
                }
                catch (Exception dg177_1) { Diag.Swallow("UpnpForm.cs:177", dg177_1); }
            }));
            t.IsBackground = true;
            t.Start();
        }

        void FillDev()
        {
            lvDev.Items.Clear();
            foreach (UpnpCore.Device d in devs)
            {
                ListViewItem li = new ListViewItem(new string[] { d.Name, d.ControlUrl, d.ExternalIp.Length == 0 ? "-" : d.ExternalIp, d.LocalIp });
                lvDev.Items.Add(li);
            }
            if (devs.Count > 0) lvDev.Items[0].Selected = true;
        }

        UpnpCore.Device Cur()
        {
            if (lvDev.SelectedItems.Count == 0) return null;
            int i = lvDev.SelectedItems[0].Index;
            if (i < 0 || i >= devs.Count) return null;
            return devs[i];
        }

        void PickDevice()
        {
            bool has = Cur() != null;
            bAdd.Enabled = has;
            bDel.Enabled = has;
            bList.Enabled = has;
        }

        void RefreshExt()
        {
            UpnpCore.Device d = Cur();
            if (d == null) { st.Text = V71Ui.TT(en, "先搜索并选中一台路由器", "Pick a router first"); return; }
            int idx = lvDev.SelectedItems[0].Index;
            st.ForeColor = V71Ui.WARN;
            st.Text = V71Ui.TT(en, "读取外网 IP…", "Reading external IP…");
            string ip = UpnpCore.ExternalIp(d);
            if (idx >= 0 && idx < lvDev.Items.Count)
                lvDev.Items[idx].SubItems[2].Text = ip.Length == 0 ? "-" : ip;
            st.ForeColor = ip.Length == 0 ? V71Ui.BAD : V71Ui.GOOD;
            st.Text = ip.Length == 0
                ? V71Ui.TT(en, "路由器没给出外网 IP（可能上层还有一层 NAT）", "Router returned no external IP (maybe double NAT)")
                : V71Ui.TT(en, "外网 IP ", "External IP ") + ip;
        }

        int Port()
        {
            int p = 0;
            int.TryParse(tbPort.Text.Trim(), out p);
            return p;
        }

        void AddOne()
        {
            UpnpCore.Device d = Cur();
            if (d == null) return;
            int port = Port();
            if (port < 1 || port > 65535)
            {
                st.ForeColor = V71Ui.BAD;
                st.Text = V71Ui.TT(en, "端口要写 1-65535", "Port must be 1-65535");
                return;
            }
            string proto = Convert.ToString(cbProto.SelectedItem);
            string lip = tbIp.Text.Trim();
            if (lip.Length == 0) lip = UpnpCore.LocalIp();
            string r = UpnpCore.AddMapping(d, tbDesc.Text.Trim(), port, proto, lip);
            st.ForeColor = r == "ok" ? V71Ui.GOOD : V71Ui.BAD;
            st.Text = r == "ok"
                ? V71Ui.TT(en, "已添加映射 ", "Mapping added ") + proto + " " + port + " -> " + lip + ":" + port
                : V71Ui.TT(en, "添加失败 ", "Add failed ") + r;
            if (r == "ok") LoadTable();
        }

        void DelOne()
        {
            UpnpCore.Device d = Cur();
            if (d == null) return;
            int port = Port();
            string proto = Convert.ToString(cbProto.SelectedItem);
            string r = UpnpCore.DeleteMapping(d, port, proto);
            st.ForeColor = r == "ok" ? V71Ui.GOOD : V71Ui.BAD;
            st.Text = r == "ok"
                ? V71Ui.TT(en, "已删除映射 ", "Mapping removed ") + proto + " " + port
                : V71Ui.TT(en, "删除失败 ", "Delete failed ") + r;
            if (r == "ok") LoadTable();
        }

        void LoadTable()
        {
            UpnpCore.Device d = Cur();
            if (d == null) return;
            st.ForeColor = V71Ui.WARN;
            st.Text = V71Ui.TT(en, "读取路由器映射表…", "Reading router mapping table…");
            lvMap.Items.Clear();
            List<string[]> rows = UpnpCore.ListMappings(d, 40);
            foreach (string[] r in rows)
            {
                ListViewItem li = new ListViewItem(r);
                if (r[4] == "关") li.ForeColor = UiKit.A(140, 150, 165);
                lvMap.Items.Add(li);
            }
            st.ForeColor = V71Ui.GOOD;
            st.Text = V71Ui.TT(en, "映射表 ", "Table rows ") + rows.Count +
                (rows.Count >= 40 ? V71Ui.TT(en, " 条（只读前 40）", " (first 40 only)") : "");
        }
    }
}