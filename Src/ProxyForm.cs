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
    class ProxyForm : Form
    {
        bool en;
        string dir;
        ListView lv;
        Label st;
        List<ProxyCore.Node> data = new List<ProxyCore.Node>();

        public ProxyForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "代理节点管理 · 小工具箱", "Proxy nodes · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(1000, 680);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "代理节点管理", "Proxy node manager"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "扫出 v2rayN / Clash / mihomo 里配好的节点，批量 TCP 测速并按延迟排序；可把最快节点直接挂到 Windows 系统代理，也能一键关掉。",
                "Lists nodes from v2rayN / Clash / mihomo, TCP-tests them and sorts by latency; can point the Windows system proxy at the fastest node and turn it off again."), 20, 48, 960, 40));

            Button bScan = V71Ui.Btn(V71Ui.TT(en, "扫描节点", "Scan"), UiKit.A(96, 165, 250), 20, 92, 110, 32);
            Button bPing = V71Ui.Btn(V71Ui.TT(en, "批量测速", "Test all"), UiKit.A(34, 197, 94), 140, 92, 110, 32);
            Button bOn = V71Ui.Btn(V71Ui.TT(en, "设为系统代理", "Set system proxy"), UiKit.A(249, 115, 22), 260, 92, 140, 32);
            Button bOff = V71Ui.Btn(V71Ui.TT(en, "关闭系统代理", "Clear proxy"), UiKit.A(190, 60, 45), 410, 92, 140, 32);
            Button bCopy = V71Ui.Btn(V71Ui.TT(en, "复制地址", "Copy host:port"), UiKit.A(52, 58, 70), 560, 92, 120, 32);
            Button bOpen = V71Ui.Btn(V71Ui.TT(en, "打开客户端", "Open client"), UiKit.A(14, 165, 233), 690, 92, 120, 32);
            Button bRep = V71Ui.Btn(V71Ui.TT(en, "导出测速报告", "Export report"), UiKit.A(139, 92, 246), 820, 92, 140, 32);
            Button bDir = V71Ui.Btn(V71Ui.TT(en, "指定配置目录", "Pick config folder"), UiKit.A(72, 208, 130), 20, 132, 150, 30);
            Controls.Add(bScan); Controls.Add(bPing); Controls.Add(bOn); Controls.Add(bOff);
            Controls.Add(bCopy); Controls.Add(bOpen); Controls.Add(bRep); Controls.Add(bDir);

            lv = V71Ui.List(new string[] {
                V71Ui.TT(en, "来源", "Source"), V71Ui.TT(en, "备注", "Name"), V71Ui.TT(en, "地址", "Host"),
                V71Ui.TT(en, "端口", "Port"), V71Ui.TT(en, "类型", "Type"), V71Ui.TT(en, "延迟 ms", "Latency") },
                new int[] { 120, 260, 300, 80, 100, 100 }, 20, 168, 960, 400);
            Controls.Add(lv);

            st = V71Ui.Note("", 20, 578, 900, 40);
            Controls.Add(st);

            Button close = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 880, 640, 100, 32);
            Controls.Add(close);
            close.Click += delegate { Close(); };

            bScan.Click += delegate { ScanNodes(); };
            bDir.Click += delegate
            {
                FolderBrowserDialog fd = new FolderBrowserDialog();
                fd.Description = V71Ui.TT(en, "选 v2rayN / Clash 所在目录（含 guiConfigs 或 config.yaml 的那层）", "Pick the v2rayN / Clash folder (the one holding guiConfigs or config.yaml)");
                if (fd.ShowDialog(this) != DialogResult.OK) return;
                if (ProxyCore.AddDir(dir, fd.SelectedPath)) { ScanNodes(); }
                else st.Text = V71Ui.TT(en, "目录无效。", "Invalid folder.");
            };
            bPing.Click += delegate { PingAll(); };
            bCopy.Click += delegate
            {
                ProxyCore.Node n = Sel();
                if (n == null) return;
                try { Clipboard.SetText(n.Host + ":" + n.Port); st.Text = V71Ui.TT(en, "已复制：", "Copied: ") + n.Host + ":" + n.Port; } catch (Exception dg86_1) { Diag.Swallow("ProxyForm.cs:86", dg86_1); }
            };
            bOn.Click += delegate
            {
                ProxyCore.Node n = Sel();
                if (n == null) { V71Ui.Say(this, V71Ui.TT(en, "先选一个节点。", "Select a node first.")); return; }
                string err = "";
                if (ProxyCore.SetSystemProxy(dir, n.Host + ":" + n.Port, out err))
                    st.Text = V71Ui.TT(en, "已把系统代理指向 ", "System proxy -> ") + n.Host + ":" + n.Port + V71Ui.TT(en, "（原设置备份在 系统代理备份.txt）", " (backup in 系统代理备份.txt)");
                else st.Text = V71Ui.TT(en, "设置失败：", "Failed: ") + err;
            };
            bOff.Click += delegate
            {
                string err = "";
                if (ProxyCore.ClearSystemProxy(dir, out err)) st.Text = V71Ui.TT(en, "系统代理已关闭。", "System proxy cleared.");
                else st.Text = V71Ui.TT(en, "取消失败：", "Failed: ") + err;
            };
            bOpen.Click += delegate
            {
                string c1 = ProxyCore.FindClient(dir, "clash");
                string c2 = ProxyCore.FindClient(dir, "v2ray");
                if (c1.Length > 0) { try { Process.Start(c1); return; } catch (Exception dg107_1) { Diag.Swallow("ProxyForm.cs:107", dg107_1); } }
                if (c2.Length > 0) { try { Process.Start(c2); return; } catch (Exception dg108_1) { Diag.Swallow("ProxyForm.cs:108", dg108_1); } }
                V71Ui.Say(this, V71Ui.TT(en, "没找到 v2rayN.exe / Clash 主程序。", "No v2rayN / Clash client found."));
            };
            bRep.Click += delegate
            {
                string p = Path.Combine(dir, "节点测速_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("代理节点测速报告\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("来源\t备注\t地址\t端口\t类型\t延迟ms");
                foreach (ProxyCore.Node n in data)
                    sb.AppendLine(n.Src + "\t" + n.Name + "\t" + n.Host + "\t" + n.Port + "\t" + n.Type + "\t" + (n.Latency < 0 ? "未测" : n.Latency.ToString()));
                try { File.WriteAllText(p, sb.ToString(), Encoding.UTF8); st.Text = V71Ui.TT(en, "报告已保存：", "Report saved: ") + p; }
                catch (Exception ex) { st.Text = ex.Message; }
            };

            ScanNodes();
        }

        void ScanNodes()
        {
            string log = "";
            data = ProxyCore.Scan(dir, out log);
            Fill();
            st.Text = V71Ui.TT(en, "扫到 " + data.Count + " 个节点（v2rayN / Clash / mihomo 目录）。测速后再排序。",
                data.Count + " nodes found. Run the latency test to sort them.") + "  " + log.Replace("\r", "").Replace("\n", " ");
        }

        void Fill()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (ProxyCore.Node n in data)
            {
                ListViewItem it = new ListViewItem(n.Src);
                it.SubItems.Add(n.Name);
                it.SubItems.Add(n.Host);
                it.SubItems.Add(n.Port.ToString());
                it.SubItems.Add(n.Type);
                it.SubItems.Add(n.Latency < 0 ? "-" : n.Latency.ToString());
                if (n.Latency >= 0)
                {
                    if (n.Latency <= 60) it.ForeColor = V71Ui.GOOD;
                    else if (n.Latency <= 150) it.ForeColor = V71Ui.WARN;
                    else it.ForeColor = V71Ui.BAD;
                }
                it.Tag = n;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }

        ProxyCore.Node Sel()
        {
            if (lv.SelectedItems.Count == 0) return null;
            return (ProxyCore.Node)lv.SelectedItems[0].Tag;
        }

        void PingAll()
        {
            if (data.Count == 0) { st.Text = V71Ui.TT(en, "先扫节点。", "Scan first."); return; }
            int total = data.Count;
            int idx = 0;
            int done = 0;
            object locker = new object();
            System.Threading.Thread[] ths = new System.Threading.Thread[8];
            for (int t = 0; t < 8; t++)
            {
                ths[t] = new System.Threading.Thread(delegate ()
                {
                    while (true)
                    {
                        int i;
                        lock (locker)
                        {
                            i = idx;
                            idx++;
                        }
                        if (i >= total) return;
                        data[i].Latency = ProxyCore.Ping(data[i].Host, data[i].Port, 1200);
                        lock (locker) { done++; }
                    }
                });
                ths[t].IsBackground = true;
                ths[t].Start();
            }
            while (true)
            {
                int d0;
                lock (locker) { d0 = done; }
                st.Text = V71Ui.TT(en, "测速中 ", "Testing ") + d0 + "/" + total;
                Application.DoEvents();
                if (d0 >= total) break;
                System.Threading.Thread.Sleep(100);
            }
            data.Sort(delegate (ProxyCore.Node a, ProxyCore.Node b)
            {
                int la = a.Latency < 0 ? 99999 : a.Latency;
                int lb = b.Latency < 0 ? 99999 : b.Latency;
                return la.CompareTo(lb);
            });
            Fill();
            string best = data.Count > 0 ? data[0].Name + " (" + data[0].Latency + "ms)" : "-";
            st.Text = V71Ui.TT(en, "已完成，最快：", "Done, fastest: ") + best;
        }
    }
}