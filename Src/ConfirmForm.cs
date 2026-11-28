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
    class ConfirmForm : Form
    {
        Action onConfirm, onCancel;
        System.Windows.Forms.Timer t;
        int remain;
        string action;
        bool langEn;
        Label lblMsg, lblCount;
        string T(string zh, string en) { return Lang.T(zh, en); }

        public ConfirmForm(string act, int secs, bool en, Action confirm, Action cancel)
        {
            action = act;
            langEn = en;
            onConfirm = confirm;
            onCancel = cancel;
            remain = secs;
            Text = T("到点确认", "Time Reached");
            ClientSize = new Size(420, 200);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = UiKit.A(31, 35, 41);
            Font = new Font("Microsoft YaHei UI", 9F);
            TopMost = true;

            Label title = new Label();
            title.Text = T("已到预定时间", "Time is up");
            title.ForeColor = UiKit.A(230, 233, 238);
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.Location = new Point(20, 18);
            title.AutoSize = true;
            Controls.Add(title);

            lblMsg = new Label();
            lblMsg.Text = T("是否执行「" + action + "」？", "Execute \"" + action + "\" now?");
            lblMsg.ForeColor = UiKit.A(139, 149, 165);
            lblMsg.Font = new Font("Microsoft YaHei UI", 10F);
            lblMsg.Location = new Point(20, 58);
            lblMsg.AutoSize = true;
            Controls.Add(lblMsg);

            lblCount = new Label();
            lblCount.Text = remain + T(" 秒后自动执行", "s auto-execute");
            lblCount.ForeColor = UiKit.A(61, 126, 255);
            lblCount.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            lblCount.Location = new Point(20, 88);
            lblCount.AutoSize = true;
            Controls.Add(lblCount);

            Button bYes = new Button();
            bYes.Text = T("立即执行", "Run Now");
            bYes.BackColor = UiKit.A(196, 61, 43);
            bYes.ForeColor = UiKit.Txt(Color.White);
            bYes.FlatStyle = FlatStyle.Standard;
            bYes.FlatAppearance.BorderSize = 0;
            bYes.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            bYes.Size = new Size(175, 40);
            bYes.Location = new Point(20, 136);
            bYes.Click += (s, e) => { t.Stop(); DialogResult = DialogResult.OK; onConfirm(); Close(); };
            Controls.Add(bYes);

            Button bNo = new Button();
            bNo.Text = T("取消", "Cancel");
            bNo.BackColor = UiKit.A(51, 58, 70);
            bNo.ForeColor = UiKit.A(154, 164, 178);
            bNo.FlatStyle = FlatStyle.Standard;
            bNo.FlatAppearance.BorderSize = 0;
            bNo.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            bNo.Size = new Size(175, 40);
            bNo.Location = new Point(210, 136);
            bNo.Click += (s, e) => { t.Stop(); DialogResult = DialogResult.Cancel; onCancel(); Close(); };
            Controls.Add(bNo);

            t = new System.Windows.Forms.Timer();
            t.Interval = 1000;
            t.Tick += (s, e) =>
            {
                remain--;
                lblCount.Text = remain + T(" 秒后自动执行", "s auto-execute");
                if (remain <= 0)
                {
                    t.Stop();
                    DialogResult = DialogResult.OK;
                    onConfirm();
                    Close();
                }
            };
            t.Start();
        }

        public void CancelByUser()
        {
            if (t != null) t.Stop();
            if (!IsDisposed) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (t != null) t.Stop();
            base.OnFormClosing(e);
        }
    }
}
