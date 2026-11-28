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
    class NetToolForm : Form
    {
        bool en;
        string dir;
        ListView lvNic;
        ListView lvHost;
        Label st;
        ComboBox cbDns;
        TextBox tbHosts;
        Label lbSpeed;
        List<string> nicNames = new List<string>();

        static readonly string[] DnsNames = {
            "阿里 DNS 223.5.5.5", "腾讯 DNS 119.29.29.29", "114 DNS 114.114.114.114",
            "Cloudflare 1.1.1.1", "Google DNS 8.8.8.8", "自动获取（DHCP）"
        };
        static readonly string[][] DnsAddrs = {
            new string[] { "223.5.5.5", "223.6.6.6" },
            new string[] { "119.29.29.29", "182.254.116.116" },
            new string[] { "114.114.114.114", "114.114.115.115" },
            new string[] { "1.1.1.1", "1.0.0.1" },
            new string[] { "8.8.8.8", "8.8.4.4" },
            new string[] { "", "" }
        };

        public NetToolForm(bool eng, string baseDir)
        {
            en = eng; dir = baseDir;
            Text = en ? "Network Toolbox · Mini Toolbox" : "网络工具箱 · 小工具箱";
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 700);
            MinimumSize = new Size(900, 640);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "网络工具箱", "Network Toolbox"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "DNS 一键切换 / hosts 编辑 / 局域网扫描 / 实测下载速度（改 DNS 和 hosts 需要管理员权限）",
                "DNS switching, hosts editing, LAN scan, real download speed (DNS/hosts need admin)"), 20, 48, 960, 20));

            lvNic = V71Ui.List(new string[] { V71Ui.TT(en, "网卡", "Adapter"), "IPv4", V71Ui.TT(en, "网关", "Gateway"), "DNS", V71Ui.TT(en, "状态", "State") },
                new int[] { 210, 150, 140, 300, 90 }, 20, 76, 960, 130);
            Controls.Add(lvNic);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "DNS 方案", "DNS preset"), 20, 218, 80, 20, 9F, V71Ui.SUB));
            cbDns = V71Ui.Combo(DnsNames, 100, 214, 240);
            Controls.Add(cbDns);

            Button bDns = V71Ui.Btn(V71Ui.TT(en, "应用 DNS", "Apply DNS"), UiKit.A(59, 130, 246), 350, 212, 110, 30);
            bDns.Click += delegate { ApplyDns(); };
            Controls.Add(bDns);

            Button bFlush = V71Ui.Btn(V71Ui.TT(en, "刷新 DNS 缓存", "Flush DNS"), UiKit.A(45, 212, 191), 470, 212, 130, 30);
            bFlush.Click += delegate
            {
                NetCore.Run("ipconfig.exe", "/flushdns", 6000);
                st.Text = V71Ui.TT(en, "DNS 缓存已刷新", "DNS cache flushed");
                st.ForeColor = V71Ui.GOOD;
            };
            Controls.Add(bFlush);

            Button bHosts = V71Ui.Btn(V71Ui.TT(en, "用记事本打开 hosts", "Open hosts"), UiKit.A(139, 92, 246), 610, 212, 150, 30);
            bHosts.Click += delegate
            {
                try { Process.Start("notepad.exe", NetCore.HostsPath()); } catch (Exception ex) { V71Ui.Say(this, ex.Message); }
            };
            Controls.Add(bHosts);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "hosts 内容（点保存即写入系统）", "hosts content (Save writes it)"), 20, 252, 400, 20, 9F, V71Ui.SUB));
            tbHosts = new TextBox();
            tbHosts.Multiline = true;
            tbHosts.ScrollBars = ScrollBars.Vertical;
            tbHosts.BackColor = V71Ui.INPUT;
            tbHosts.ForeColor = V71Ui.TEXT;
            tbHosts.BorderStyle = BorderStyle.FixedSingle;
            tbHosts.Font = new Font("Consolas", 9F);
            tbHosts.Location = new Point(20, 274);
            tbHosts.Size = new Size(480, 190);
            tbHosts.Text = NetCore.ReadHosts();
            Controls.Add(tbHosts);

            Button bHostsSave = V71Ui.Btn(V71Ui.TT(en, "保存 hosts", "Save hosts"), UiKit.A(34, 197, 94), 20, 472, 110, 30);
            bHostsSave.Click += delegate
            {
                string err = "";
                if (NetCore.SaveHosts(tbHosts.Text, out err))
                {
                    st.Text = V71Ui.TT(en, "hosts 已保存", "hosts saved");
                    st.ForeColor = V71Ui.GOOD;
                }
                else
                {
                    st.Text = V71Ui.TT(en, "保存失败：" + err, "save failed: " + err);
                    st.ForeColor = V71Ui.BAD;
                }
            };
            Controls.Add(bHostsSave);

            Button bHostsReload = V71Ui.Btn(V71Ui.TT(en, "重新读取", "Reload"), UiKit.A(43, 58, 80), 140, 472, 100, 30);
            bHostsReload.Click += delegate { tbHosts.Text = NetCore.ReadHosts(); };
            Controls.Add(bHostsReload);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "局域网设备（当前网段 ping 扫描 + ARP 取 MAC）", "LAN devices (ping sweep + ARP)"), 520, 252, 460, 20, 9F, V71Ui.SUB));
            lvHost = V71Ui.List(new string[] { "IP", "MAC", V71Ui.TT(en, "延迟", "Ping") }, new int[] { 170, 190, 90 }, 520, 274, 460, 190);
            Controls.Add(lvHost);

            Button bScan = V71Ui.Btn(V71Ui.TT(en, "扫描局域网", "Scan LAN"), UiKit.A(56, 189, 248), 520, 472, 130, 30);
            bScan.Click += delegate { ScanLan(); };
            Controls.Add(bScan);

            lbSpeed = V71Ui.Text(V71Ui.TT(en, "下载测速：点右边按钮", "Speed: click the button"), 520, 508, 460, 20, 9F, V71Ui.SUB);
            Controls.Add(lbSpeed);

            Button bSpeed = V71Ui.Btn(V71Ui.TT(en, "测下载速度", "Test speed"), UiKit.A(249, 115, 22), 520, 528, 120, 30);
            bSpeed.Click += delegate { TestSpeed(); };
            Controls.Add(bSpeed);

            st = V71Ui.Text(V71Ui.TT(en, "就绪", "Ready"), 20, 512, 480, 40, 9F, V71Ui.SUB);
            Controls.Add(st);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(43, 58, 80), 880, 650, 100, 32);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);

            ReloadNics();
        }

        void ReloadNics()
        {
            lvNic.Items.Clear();
            nicNames.Clear();
            List<NetCore.Nic> list = NetCore.Nics();
            foreach (NetCore.Nic n in list)
            {
                nicNames.Add(n.Name);
                ListViewItem li = new ListViewItem(new string[] { n.Name, n.Ip, n.Gateway, n.Dns, n.Up ? V71Ui.TT(en, "已连接", "up") : V71Ui.TT(en, "未连接", "down") });
                lvNic.Items.Add(li);
            }
            lvNic.Invalidate();
            st.Text = V71Ui.TT(en, "共 " + list.Count + " 个网卡；改 DNS / hosts 需要管理员权限", list.Count + " adapters; admin needed for DNS/hosts");
            st.ForeColor = V71Ui.SUB;
        }

        void ApplyDns()
        {
            if (lvNic.SelectedItems.Count == 0)
            {
                st.Text = V71Ui.TT(en, "请先在网卡列表里选一行", "Select an adapter row first");
                st.ForeColor = V71Ui.WARN;
                return;
            }
            string nic = lvNic.SelectedItems[0].SubItems[0].Text;
            int idx = cbDns.SelectedIndex;
            if (idx < 0) return;
            string[] addrs = DnsAddrs[idx];
            string err = "";
            bool ok = addrs[0].Length == 0 ? NetCore.SetDnsDhcp(nic, out err) : NetCore.SetDnsStatic(nic, addrs, out err);
            st.Text = ok ? V71Ui.TT(en, "DNS 已切换：" + DnsNames[idx], "DNS switched to " + DnsNames[idx])
                : V71Ui.TT(en, "切换失败：" + err + "（多半要右键以管理员身份运行）", "failed: " + err + " (run as administrator)");
            st.ForeColor = ok ? V71Ui.GOOD : V71Ui.BAD;
            if (ok) ReloadNics();
        }

        void ScanLan()
        {
            string prefix = NetCore.SubnetPrefix();
            if (prefix.Length == 0)
            {
                st.Text = V71Ui.TT(en, "没找到可扫描的网段", "no local subnet found");
                st.ForeColor = V71Ui.BAD;
                return;
            }
            lvHost.Items.Clear();
            st.Text = V71Ui.TT(en, "正在扫描 " + prefix + "1-254 …", "scanning " + prefix + "1-254 …");
            st.ForeColor = V71Ui.WARN;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                List<NetCore.LanHost> hs = NetCore.ScanLan(prefix);
                Dictionary<string, string> arp = NetCore.ArpTable();
                try
                {
                    Invoke(new MethodInvoker(delegate
                    {
                        foreach (NetCore.LanHost h in hs)
                        {
                            string mac = arp.ContainsKey(h.Ip) ? arp[h.Ip] : "-";
                            lvHost.Items.Add(new ListViewItem(new string[] { h.Ip, mac, h.Ms + " ms" }));
                        }
                        lvHost.Invalidate();
                        st.Text = V71Ui.TT(en, "扫描完成：发现 " + hs.Count + " 台在线设备", "done: " + hs.Count + " devices online");
                        st.ForeColor = V71Ui.GOOD;
                    }));
                }
                catch (Exception dg216_1) { Diag.Swallow("NetToolForm.cs:216", dg216_1); }
            });
            th.IsBackground = true;
            th.Start();
        }

        void TestSpeed()
        {
            lbSpeed.Text = V71Ui.TT(en, "正在测速…", "testing…");
            lbSpeed.ForeColor = V71Ui.WARN;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                string info = "";
                double mbps = NetCore.TestDownload(out info);
                try
                {
                    Invoke(new MethodInvoker(delegate
                    {
                        if (mbps > 0)
                        {
                            lbSpeed.Text = V71Ui.TT(en, "下行约 " + mbps.ToString("0.0") + " Mbps · " + info, "down ~" + mbps.ToString("0.0") + " Mbps · " + info);
                            lbSpeed.ForeColor = mbps >= 20 ? V71Ui.GOOD : V71Ui.WARN;
                        }
                        else
                        {
                            lbSpeed.Text = V71Ui.TT(en, "测速失败：" + info, "speed test failed: " + info);
                            lbSpeed.ForeColor = V71Ui.BAD;
                        }
                    }));
                }
                catch (Exception dg246_1) { Diag.Swallow("NetToolForm.cs:246", dg246_1); }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}