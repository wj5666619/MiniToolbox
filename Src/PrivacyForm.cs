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
    class PrivacyForm : Form
    {
        bool en;
        ListView lv;
        TextBox tbLog;
        Label lblSum;
        List<PrivItem> items = new List<PrivItem>();

        public PrivacyForm(bool langEn)
        {
            en = langEn;
            Text = en ? "Privacy cleanup · Mini Toolbox" : "隐私痕迹清理 · 小工具箱";
            BackColor = UiKit.A(20, 24, 30);
            ClientSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(Lbl(en ? "Privacy traces" : "隐私痕迹清理", 16F, Color.White, 20, 12));
            Label sub = Lbl(en ? "Browser caches, cookies, recent lists, thumb cache, clipboard, DNS, recycle bin. Everything is listed before you clean." : "浏览器缓存 / Cookie、最近文档、缩略图缓存、剪贴板、DNS、回收站。先列清单再动手。", 8.5F, UiKit.A(150, 160, 176), 20, 44);
            sub.Size = new Size(850, 20);
            Controls.Add(sub);

            lv = new ListView();
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.BackColor = UiKit.A(18, 22, 28);
            lv.ForeColor = UiKit.A(206, 218, 232);
            lv.BorderStyle = BorderStyle.None;
            lv.Font = new Font("Microsoft YaHei UI", 9F);
            lv.Location = new Point(20, 76);
            lv.Size = new Size(860, 360);
            lv.Columns.Add(en ? "Item" : "项目", 250);
            lv.Columns.Add(en ? "Size" : "占用", 110);
            lv.Columns.Add(en ? "Path" : "路径", 470);
            Controls.Add(lv);

            Button bScan = Btn(en ? "Scan" : "扫描", 20, 448, 110, 32);
            bScan.Click += delegate { Scan(); };
            Controls.Add(bScan);
            Button bAll = Btn(en ? "Check all" : "全选", 140, 448, 90, 32);
            bAll.Click += delegate
            {
                for (int i = 0; i < lv.Items.Count; i++) lv.Items[i].Checked = true;
            };
            Controls.Add(bAll);
            Button bNone = Btn(en ? "Check none" : "全不选", 238, 448, 96, 32);
            bNone.Click += delegate
            {
                for (int i = 0; i < lv.Items.Count; i++) lv.Items[i].Checked = false;
            };
            Controls.Add(bNone);
            Button bClean = Btn(en ? "Clean checked" : "清理选中", 344, 448, 130, 32);
            bClean.Click += delegate { Clean(); };
            Controls.Add(bClean);

            lblSum = Lbl("", 9F, UiKit.A(120, 170, 220), 490, 456);
            lblSum.Size = new Size(390, 18);
            Controls.Add(lblSum);

            tbLog = new TextBox();
            tbLog.Multiline = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.BackColor = UiKit.A(18, 22, 28);
            tbLog.ForeColor = UiKit.A(175, 190, 206);
            tbLog.BorderStyle = BorderStyle.None;
            tbLog.Font = new Font("Consolas", 8.5F);
            tbLog.Location = new Point(20, 490);
            tbLog.Size = new Size(770, 110);
            tbLog.ReadOnly = true;
            Controls.Add(tbLog);

            Button bClose = Btn(en ? "Close" : "关闭", 800, 546, 80, 30);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);
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

        Button Btn(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Standard;
            b.BackColor = UiKit.A(40, 48, 60);
            b.ForeColor = UiKit.A(228, 236, 246);
            b.FlatAppearance.BorderColor = UiKit.A(70, 84, 104);
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Cursor = Cursors.Hand;
            return b;
        }

        void Log(string s)
        {
            tbLog.AppendText(s + "\r\n");
        }

        public void ScanForTest()
        {
            Scan();
        }

        void Scan()
        {
            lv.Items.Clear();
            items.Clear();
            lblSum.Text = en ? "scanning…" : "扫描中…";
            Application.DoEvents();
            try
            {
                items = PrivacyCore.Scan();
                for (int i = 0; i < items.Count; i++)
                {
                    PrivItem it = items[i];
                    ListViewItem row = new ListViewItem(en ? it.En : it.Zh);
                    row.SubItems.Add(PrivCore(it.Size));
                    row.SubItems.Add(it.Target);
                    row.Checked = true;
                    row.Tag = it;
                    lv.Items.Add(row);
                }
                long total = 0;
                for (int i = 0; i < items.Count; i++) total += items[i].Size;
                lblSum.Text = (en ? "found " : "共 ") + items.Count.ToString() + (en ? " items · " : " 项 · ") + PrivCore(total);
                Log((en ? "scan done: " : "扫描完成：") + items.Count.ToString() + (en ? " items" : " 项"));
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                lblSum.Text = "扫描失败";
            }
        }

        static string PrivCore(long b)
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("0.0") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return (b / 1024.0).ToString("0.0") + " KB";
            return b.ToString() + " B";
        }

        void Clean()
        {
            int done = 0, fail = 0;
            for (int i = 0; i < lv.Items.Count; i++)
            {
                ListViewItem row = lv.Items[i];
                if (!row.Checked) continue;
                PrivItem it = (PrivItem)row.Tag;
                string msg = "";
                bool ok = PrivacyCore.Clean(it, out msg);
                if (ok) done++; else fail++;
                Log((ok ? "OK " : "X  ") + (en ? it.En : it.Zh) + " — " + msg);
                row.Text = (en ? it.En : it.Zh) + (ok ? "  ✓" : "  ✗");
                Application.DoEvents();
            }
            Log((en ? "clean finished: " : "清理结束：") + done.ToString() + (en ? " ok, " : " 成功，") + fail.ToString() + (en ? " failed" : " 失败"));
        }
    }
}
