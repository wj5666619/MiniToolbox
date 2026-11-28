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
    class ReminderPopup : Form
    {
        ReminderItem item;
        public ReminderPopup(ReminderItem it)
        {
            item = it;
            Text = "定时提醒";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            ClientSize = new Size(420, 200);
            BackColor = UiKit.A(31, 35, 41);
            ForeColor = UiKit.Txt(Color.White);
            Font = new Font("Microsoft YaHei UI", 9F);

            Panel card = new Panel();
            card.BackColor = UiKit.A(40, 46, 58);
            card.Location = new Point(10, 10);
            card.Size = new Size(400, 180);
            Controls.Add(card);

            Label bar = new Label();
            bar.BackColor = UiKit.A(208, 58, 68);
            bar.Location = new Point(0, 0);
            bar.Size = new Size(4, 180);
            card.Controls.Add(bar);

            Label tt = new Label();
            tt.Text = string.IsNullOrEmpty(it.Title) ? "提醒" : it.Title;
            tt.ForeColor = UiKit.Txt(Color.White);
            tt.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            tt.Location = new Point(16, 12);
            tt.Size = new Size(360, 26);
            tt.AutoEllipsis = true;
            card.Controls.Add(tt);

            Label body = new Label();
            body.Text = string.IsNullOrEmpty(it.Body) ? it.TimeText : it.Body;
            body.ForeColor = UiKit.A(198, 208, 222);
            body.Font = new Font("Microsoft YaHei UI", 9.5F);
            body.Location = new Point(16, 44);
            body.Size = new Size(364, 76);
            card.Controls.Add(body);

            Label tm = new Label();
            tm.Text = "设定时间：" + it.TimeText + "   " + it.RepeatText();
            tm.ForeColor = UiKit.A(140, 152, 170);
            tm.Font = new Font("Microsoft YaHei UI", 8F);
            tm.Location = new Point(16, 126);
            tm.Size = new Size(280, 18);
            card.Controls.Add(tm);

            Button ok = PopBtn("知道了", 200, 138, 96, 28, UiKit.A(208, 58, 68));
            ok.Click += (a, b) => Close();
            card.Controls.Add(ok);

            Button later = PopBtn("稍后 10 分钟", 302, 138, 94, 28, UiKit.A(58, 66, 80));
            later.Click += (a, b) =>
            {
                try
                {
                    item.FireAt = DateTime.Now.AddMinutes(10);
                    item.On = true;
                    ReminderStore.Save(Path.GetDirectoryName(Application.ExecutablePath));
                }
                catch (Exception dg89_1) { Diag.Swallow("ReminderPopup.cs:89", dg89_1); }
                Close();
            };
            card.Controls.Add(later);

            if (it.Sound)
            {
                Sfx.Alarm();
            }
        }

        Button PopBtn(string text, int x, int y, int w, int h, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = UiKit.Txt(Color.White);
            b.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
        }
    }
}