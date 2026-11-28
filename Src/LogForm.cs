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
    class LogForm : Form
    {
        TextBox txt;
        string path;
        bool langEn;
        string T(string zh, string en) { return Lang.T(zh, en); }

        public LogForm(string logPath, bool en)
        {
            path = logPath;
            langEn = en;
            Text = T("任务日志", "Task Log");
            ClientSize = new Size(560, 420);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = UiKit.A(31, 35, 41);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception dg40_1) { Diag.Swallow("LogForm.cs:40", dg40_1); }

            Panel head = new Panel();
            head.BackColor = UiKit.A(38, 43, 51);
            head.Dock = DockStyle.Top;
            head.Height = 48;
            Controls.Add(head);

            Label t = new Label();
            t.Text = T("任务日志（shutdown_history.log）", "Task Log (shutdown_history.log)");
            t.ForeColor = UiKit.A(230, 233, 238);
            t.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            t.Location = new Point(16, 13);
            t.AutoSize = true;
            head.Controls.Add(t);

            txt = new TextBox();
            txt.Multiline = true;
            txt.ReadOnly = true;
            txt.ScrollBars = ScrollBars.Both;
            txt.BackColor = UiKit.A(25, 29, 36);
            txt.ForeColor = UiKit.A(200, 208, 220);
            txt.Font = new Font("Consolas", 9.5F);
            txt.BorderStyle = BorderStyle.None;
            txt.Dock = DockStyle.Fill;
            Controls.Add(txt);

            Panel foot = new Panel();
            foot.BackColor = UiKit.A(30, 34, 41);
            foot.Dock = DockStyle.Bottom;
            foot.Height = 52;
            Controls.Add(foot);

            Button bRefresh = MakeBtn(T("刷新", "Refresh"), 16, 8, 90, 34);
            bRefresh.Click += (s, e) => Reload();
            foot.Controls.Add(bRefresh);

            Button bClear = MakeBtn(T("清空日志", "Clear Log"), 116, 8, 100, 34);
            bClear.Click += (s, e) => ClearLog();
            foot.Controls.Add(bClear);

            Button bClose = MakeBtn(T("关闭", "Close"), 450, 8, 90, 34);
            bClose.Click += (s, e) => Close();
            foot.Controls.Add(bClose);

            Reload();
        }

        Button MakeBtn(string text, int x, int y, int w, int h)
        {
            RoundBtn b = new RoundBtn(UiKit.A(43, 58, 80), UiKit.A(230, 233, 238));
            b.Text = text;
            b.ForeColor = UiKit.A(230, 233, 238);
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        void Reload()
        {
            try
            {
                txt.Text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "（暂无日志）";
            }
            catch { txt.Text = "（读取日志失败）"; }
            txt.SelectionStart = txt.TextLength;
            txt.ScrollToCaret();
        }

        void ClearLog()
        {
            if (MessageBox.Show("确定清空全部日志吗？", "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try { File.WriteAllText(path, "", Encoding.UTF8); } catch (Exception dg116_1) { Diag.Swallow("LogForm.cs:116", dg116_1); }
            Reload();
        }
    }
}