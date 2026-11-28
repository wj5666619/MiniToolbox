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
    class PwdEditForm : Form
    {
        bool en;
        public PwdEntry Result;
        TextBox tbSite, tbUser, tbPwd, tbNote, tbSecret;
         RoundCheck cbShow;

        public PwdEditForm(bool langEn, PwdEntry e)
        {
            en = langEn;
            Text = en ? "Entry" : "条目";
            BackColor = UiKit.A(20, 24, 30);
            ClientSize = new Size(460, 340);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(Lbl(en ? "Site / App" : "站点 / 应用", 20, 16));
            tbSite = Tb(20, 40, 420);
            Controls.Add(tbSite);

            Controls.Add(Lbl(en ? "Account" : "账号", 20, 76));
            tbUser = Tb(20, 100, 420);
            Controls.Add(tbUser);

            Controls.Add(Lbl(en ? "Password" : "密码", 20, 136));
            tbPwd = Tb(20, 160, 320);
            tbPwd.UseSystemPasswordChar = true;
            Controls.Add(tbPwd);
            cbShow = new RoundCheck();
            cbShow.Text = en ? "show" : "显示";
            cbShow.ForeColor = UiKit.A(150, 160, 176);
            cbShow.Location = new Point(348, 160);
            cbShow.Size = new Size(90, 24);
            cbShow.CheckedChanged += delegate { tbPwd.UseSystemPasswordChar = !cbShow.Checked; };
            Controls.Add(cbShow);

            Controls.Add(Lbl(en ? "2FA secret (Base32, optional)" : "两步验证密钥（Base32，可空）", 20, 194));
            tbSecret = Tb(20, 218, 420);
            Controls.Add(tbSecret);

            Controls.Add(Lbl(en ? "Note" : "备注", 20, 250));
            tbNote = Tb(20, 274, 240);
            Controls.Add(tbNote);

            Button ok = new Button();
            ok.Text = en ? "OK" : "确定";
            ok.FlatStyle = FlatStyle.Standard;
            ok.BackColor = UiKit.A(40, 48, 60);
            ok.ForeColor = UiKit.A(228, 236, 246);
            ok.FlatAppearance.BorderColor = UiKit.A(70, 84, 104);
            ok.Location = new Point(272, 258);
            ok.Size = new Size(76, 28);
            ok.Click += delegate
            {
                Result = new PwdEntry();
                Result.Site = tbSite.Text.Trim();
                Result.User = tbUser.Text.Trim();
                Result.Pwd = tbPwd.Text;
                Result.Note = tbNote.Text.Trim();
                Result.Secret = tbSecret.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = en ? "Cancel" : "取消";
            cancel.FlatStyle = FlatStyle.Standard;
            cancel.BackColor = UiKit.A(32, 38, 48);
            cancel.ForeColor = UiKit.A(200, 210, 226);
            cancel.FlatAppearance.BorderColor = UiKit.A(70, 84, 104);
            cancel.Location = new Point(356, 258);
            cancel.Size = new Size(84, 28);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            if (e != null)
            {
                tbSite.Text = e.Site;
                tbUser.Text = e.User;
                tbPwd.Text = e.Pwd;
                tbNote.Text = e.Note;
                tbSecret.Text = e.Secret;
            }
        }

        Label Lbl(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = UiKit.A(150, 162, 178);
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        TextBox Tb(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.BackColor = UiKit.A(34, 40, 50);
            t.ForeColor = UiKit.A(232, 240, 250);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = new Font("Microsoft YaHei UI", 9F);
            t.Location = new Point(x, y);
            t.Size = new Size(w, 24);
            return t;
        }
    }
}
