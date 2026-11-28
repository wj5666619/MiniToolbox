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
    class IsoForm : Form
    {
        TextBox txt;
        Button btnDl;
        ProgressBar pb;
        Label lbl;
        bool busy = false;
        string outDir;
        bool en;

        public IsoForm(bool langEn, string dir)
        {
            en = langEn;
            outDir = dir;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 258);
            BackColor = UiKit.A(30, 34, 41);
            ShowInTaskbar = false;

            Panel bar = new Panel();
            bar.BackColor = UiKit.A(26, 30, 37);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(560, 42);
            Controls.Add(bar);

            Label ti = new Label();
            ti.Text = en ? "Windows ISO Download" : "系统镜像下载";
            ti.ForeColor = UiKit.A(230, 233, 238);
            ti.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            ti.Location = new Point(16, 10);
            ti.AutoSize = true;
            bar.Controls.Add(ti);

            Button x = new Button();
            x.Text = "×";
            x.FlatStyle = FlatStyle.Standard;
            x.FlatAppearance.BorderSize = 0;
            x.ForeColor = UiKit.A(150, 160, 175);
            x.Font = new Font("Microsoft YaHei UI", 12F);
            x.Location = new Point(520, 6);
            x.Size = new Size(30, 30);
            x.Click += (s, e) => Close();
            bar.Controls.Add(x);

            Label note = new Label();
            note.Text = en ? "Paste the official Microsoft ISO direct link. File is saved to the folder shown below." : "粘贴微软官方的 ISO 直链（从 microsoft.com 官网获取），文件保存到下方目录。";
            note.ForeColor = UiKit.A(150, 160, 175);
            note.Font = new Font("Microsoft YaHei UI", 8.5F);
            note.Location = new Point(20, 52);
            note.Size = new Size(520, 40);
            Controls.Add(note);

            txt = new TextBox();
            txt.BorderStyle = BorderStyle.FixedSingle;
            txt.BackColor = UiKit.A(27, 31, 38);
            txt.ForeColor = UiKit.A(230, 233, 238);
            txt.Font = new Font("Microsoft YaHei UI", 9F);
            txt.Location = new Point(20, 96);
            txt.Size = new Size(520, 26);
            Controls.Add(txt);

            btnDl = new Button();
            btnDl.Text = en ? "Download" : "开始下载";
            btnDl.FlatStyle = FlatStyle.Standard;
            btnDl.FlatAppearance.BorderSize = 0;
            btnDl.BackColor = UiKit.A(34, 197, 94);
            btnDl.ForeColor = Color.Black;
            btnDl.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnDl.Location = new Point(20, 134);
            btnDl.Size = new Size(150, 34);
            btnDl.Click += (s, e) => StartDl();
            Controls.Add(btnDl);

            Button btnClose = new Button();
            btnClose.Text = en ? "Close" : "关闭";
            btnClose.FlatStyle = FlatStyle.Standard;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.BackColor = UiKit.A(45, 52, 64);
            btnClose.ForeColor = UiKit.Txt(Color.White);
            btnClose.Font = new Font("Microsoft YaHei UI", 9F);
            btnClose.Location = new Point(390, 134);
            btnClose.Size = new Size(150, 34);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            pb = new ProgressBar();
            pb.Minimum = 0; pb.Maximum = 100;
            pb.Style = ProgressBarStyle.Continuous;
            pb.Location = new Point(20, 184);
            pb.Size = new Size(520, 14);
            pb.Visible = false;
            Controls.Add(pb);

            lbl = new Label();
            lbl.Text = "";
            lbl.ForeColor = UiKit.A(126, 136, 154);
            lbl.Font = new Font("Microsoft YaHei UI", 8.5F);
            lbl.Location = new Point(20, 206);
            lbl.Size = new Size(520, 36);
            Controls.Add(lbl);
        }

        void StartDl()
        {
            if (busy) return;
            string url = txt.Text.Trim();
            if (!url.StartsWith("http"))
            {
                lbl.Text = en ? "Not a valid http link." : "请输入 http 开头的直链。";
                return;
            }
            busy = true;
            btnDl.Enabled = false;
            pb.Visible = true;
            pb.Value = 0;
            lbl.Text = en ? "Connecting..." : "正在连接…";
            string fn = "windows.iso";
            try { fn = Path.GetFileName(new Uri(url).LocalPath); } catch (Exception dg141_1) { Diag.Swallow("IsoForm.cs:141", dg141_1); }
            if (fn == null || fn.Length < 5) fn = "windows.iso";
            string dst = Path.Combine(outDir, fn);
            Thread t = new Thread(delegate()
            {
                try
                {
                    System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Timeout = 30000;
                    req.UserAgent = "Mozilla/5.0";
                    using (System.Net.WebResponse resp = req.GetResponse())
                    using (Stream src = resp.GetResponseStream())
                    using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                    {
                        long total = resp.ContentLength;
                        long got = 0;
                        byte[] buf = new byte[131072];
                        int n = 0;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            got += n;
                            if (total > 0)
                            {
                                int pct = (int)(got * 100 / total);
                                long gg = got; long tt = total;
                                this.BeginInvoke((Action)delegate
                                {
                                    pb.Value = Math.Min(100, pct);
                                    lbl.Text = (gg / 1048576) + " / " + (tt / 1048576) + " MB   " + pct + "%";
                                });
                            }
                        }
                    }
                    this.BeginInvoke((Action)delegate
                    {
                        pb.Value = 100;
                        lbl.Text = (en ? "Done: " : "完成：") + dst;
                        busy = false;
                        btnDl.Enabled = true;
                    });
                }
                catch (Exception ex)
                {
                    string m = ex.Message;
                    this.BeginInvoke((Action)delegate
                    {
                        lbl.Text = (en ? "Failed: " : "失败：") + m;
                        busy = false;
                        btnDl.Enabled = true;
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}