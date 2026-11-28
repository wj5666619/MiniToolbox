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
    class PwdBookForm : Form
    {
        bool en;
        List<PwdEntry> list = new List<PwdEntry>();
        string master = "";
        bool unlocked = false;
        ListView lv;
        TextBox tbPwd, tbLog, tbDetail;
        Label lblCode;
        System.Windows.Forms.Timer tmCode = new System.Windows.Forms.Timer();

        string FilePath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "密码本.pwb");
        }

        public PwdBookForm(bool langEn)
        {
            en = langEn;
            Text = en ? "Password book · Mini Toolbox" : "离线密码本 / 两步验证 · 小工具箱";
            BackColor = UiKit.A(20, 24, 30);
            ClientSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(Lbl(en ? "Password book + TOTP" : "离线密码本 + 两步验证", 16F, Color.White, 20, 12));
            Label sub = Lbl(en ? "AES-256 + PBKDF2 on disk. TOTP works offline, no network." : "落盘 AES-256 + PBKDF2，动态验证码纯离线计算。", 8.5F, UiKit.A(150, 160, 176), 20, 44);
            sub.Size = new Size(840, 20);
            Controls.Add(sub);

            Controls.Add(Lbl(en ? "Master password" : "主密码", 8.5F, UiKit.A(150, 162, 178), 20, 78));
            tbPwd = new TextBox();
            tbPwd.BackColor = UiKit.A(34, 40, 50);
            tbPwd.ForeColor = UiKit.A(232, 240, 250);
            tbPwd.BorderStyle = BorderStyle.FixedSingle;
            tbPwd.UseSystemPasswordChar = true;
            tbPwd.Location = new Point(20, 100);
            tbPwd.Size = new Size(240, 24);
            Controls.Add(tbPwd);

            Button btnUnlock = Btn(en ? "Unlock / create" : "解锁 / 新建", 272, 98, 130, 28);
            btnUnlock.Click += delegate { Unlock(); };
            Controls.Add(btnUnlock);

            Button btnSave = Btn(en ? "Save" : "保存", 412, 98, 90, 28);
            btnSave.Click += delegate { Save(); };
            Controls.Add(btnSave);

            lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.BackColor = UiKit.A(18, 22, 28);
            lv.ForeColor = UiKit.A(206, 218, 232);
            lv.BorderStyle = BorderStyle.None;
            lv.Font = new Font("Microsoft YaHei UI", 9F);
            lv.Location = new Point(20, 140);
            lv.Size = new Size(560, 392);
            lv.Columns.Add(en ? "Site" : "站点", 190);
            lv.Columns.Add(en ? "Account" : "账号", 170);
            lv.Columns.Add(en ? "2FA" : "两步验证", 90);
            lv.Columns.Add(en ? "Note" : "备注", 100);
            lv.SelectedIndexChanged += delegate { ShowDetail(); };
            lv.DoubleClick += delegate { EditEntry(); };
            Controls.Add(lv);

            Button bAdd = Btn(en ? "Add" : "新增", 20, 540, 84, 28);
            bAdd.Click += delegate { AddEntry(); };
            Controls.Add(bAdd);
            Button bEdit = Btn(en ? "Edit" : "修改", 112, 540, 84, 28);
            bEdit.Click += delegate { EditEntry(); };
            Controls.Add(bEdit);
            Button bDel = Btn(en ? "Delete" : "删除", 204, 540, 84, 28);
            bDel.Click += delegate { DelEntry(); };
            Controls.Add(bDel);
            Button bCopy = Btn(en ? "Copy pwd" : "复制密码", 296, 540, 100, 28);
            bCopy.Click += delegate { CopyPwd(); };
            Controls.Add(bCopy);
            Button bCopyUser = Btn(en ? "Copy acc" : "复制账号", 404, 540, 100, 28);
            bCopyUser.Click += delegate { CopyUser(); };
            Controls.Add(bCopyUser);

            Controls.Add(Lbl(en ? "TOTP code" : "动态验证码", 8.5F, UiKit.A(150, 162, 178), 600, 140));
            lblCode = new Label();
            lblCode.Text = "------";
            lblCode.ForeColor = UiKit.A(120, 220, 170);
            lblCode.Font = new Font("Consolas", 26F, FontStyle.Bold);
            lblCode.Location = new Point(596, 162);
            lblCode.Size = new Size(288, 52);
            lblCode.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(lblCode);

            Button bCode = Btn(en ? "Copy code" : "复制验证码", 596, 218, 130, 28);
            bCode.Click += delegate { CopyCode(); };
            Controls.Add(bCode);

            tbDetail = new TextBox();
            tbDetail.Multiline = true;
            tbDetail.ScrollBars = ScrollBars.Vertical;
            tbDetail.BackColor = UiKit.A(24, 29, 37);
            tbDetail.ForeColor = UiKit.A(190, 202, 218);
            tbDetail.BorderStyle = BorderStyle.None;
            tbDetail.Font = new Font("Microsoft YaHei UI", 9F);
            tbDetail.Location = new Point(596, 254);
            tbDetail.Size = new Size(288, 190);
            tbDetail.ReadOnly = true;
            Controls.Add(tbDetail);

            tbLog = new TextBox();
            tbLog.Multiline = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.BackColor = UiKit.A(18, 22, 28);
            tbLog.ForeColor = UiKit.A(170, 186, 204);
            tbLog.BorderStyle = BorderStyle.None;
            tbLog.Font = new Font("Consolas", 8.5F);
            tbLog.Location = new Point(596, 452);
            tbLog.Size = new Size(288, 80);
            tbLog.ReadOnly = true;
            Controls.Add(tbLog);

            Button bClose = Btn(en ? "Close" : "关闭", 596, 540, 90, 28);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);

            tmCode.Interval = 1000;
            tmCode.Tick += delegate { RefreshCode(); };
            tmCode.Start();
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

        void Unlock()
        {
            string pwd = tbPwd.Text;
            if (pwd.Length == 0)
            {
                Log(en ? "master password cannot be empty" : "主密码不能为空");
                return;
            }
            string file = FilePath();
            if (!File.Exists(file))
            {
                // 新建空库
                list = new List<PwdEntry>();
                master = pwd;
                unlocked = true;
                FillList();
                Log(en ? "new book created (not saved yet)" : "已新建空密码本（尚未保存）");
                Save();
                return;
            }
            string err = "";
            bool wrong = false;
            List<PwdEntry> got = PwdBookCore.Load(file, pwd, out err, out wrong);
            if (wrong)
            {
                Log(en ? "wrong password" : "密码错误");
                return;
            }
            list = got;
            master = pwd;
            unlocked = true;
            FillList();
            Log((en ? "unlocked, " : "已解锁，") + list.Count.ToString() + (en ? " entries" : " 条记录"));
        }

        void Save()
        {
            if (!unlocked) { Log(en ? "unlock first" : "请先解锁"); return; }
            string err = "";
            if (PwdBookCore.Save(FilePath(), master, list, out err))
                Log(en ? "saved" : "已保存");
            else Log((en ? "save failed: " : "保存失败：") + err);
        }

        void FillList()
        {
            lv.Items.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                PwdEntry e = list[i];
                ListViewItem it = new ListViewItem(e.Site);
                it.SubItems.Add(e.User);
                it.SubItems.Add(e.Secret.Length > 0 ? "TOTP" : "-");
                it.SubItems.Add(e.Note);
                lv.Items.Add(it);
            }
        }

        PwdEntry Sel()
        {
            if (lv.SelectedIndices.Count == 0) return null;
            int i = lv.SelectedIndices[0];
            if (i < 0 || i >= list.Count) return null;
            return list[i];
        }

        void ShowDetail()
        {
            PwdEntry e = Sel();
            if (e == null) { tbDetail.Text = ""; return; }
            tbDetail.Text = (en ? "Site: " : "站点：") + e.Site + "\r\n"
                + (en ? "Account: " : "账号：") + e.User + "\r\n"
                + (en ? "Password: " : "密码：") + new string('*', Math.Min(12, e.Pwd.Length)) + "\r\n"
                + (en ? "Note: " : "备注：") + e.Note + "\r\n"
                + (en ? "Secret: " : "密钥：") + (e.Secret.Length > 0 ? e.Secret : "-");
            RefreshCode();
        }

        void RefreshCode()
        {
            PwdEntry e = Sel();
            if (e == null || e.Secret.Length == 0)
            {
                lblCode.Text = "------";
                return;
            }
            try
            {
                string code = PwdBookCore.Totp(e.Secret, DateTime.Now, 6, 30);
                int left = PwdBookCore.RemainSeconds(30);
                lblCode.Text = code.Length == 0 ? "密钥无效" : (code.Substring(0, 3) + " " + code.Substring(3) + "   " + left.ToString() + "s");
            }
            catch { lblCode.Text = "密钥无效"; }
        }

        void AddEntry()
        {
            if (!unlocked) { Log(en ? "unlock first" : "请先解锁"); return; }
            PwdEditForm ef = new PwdEditForm(en, null);
            if (ef.ShowDialog(this) != DialogResult.OK || ef.Result == null) return;
            if (ef.Result.Site.Length == 0) { Log(en ? "site required" : "站点不能为空"); return; }
            list.Add(ef.Result);
            FillList();
            Save();
        }

        void EditEntry()
        {
            if (!unlocked) { Log(en ? "unlock first" : "请先解锁"); return; }
            PwdEntry e = Sel();
            if (e == null) return;
            PwdEditForm ef = new PwdEditForm(en, e);
            if (ef.ShowDialog(this) != DialogResult.OK || ef.Result == null) return;
            e.Site = ef.Result.Site;
            e.User = ef.Result.User;
            e.Pwd = ef.Result.Pwd;
            e.Note = ef.Result.Note;
            e.Secret = ef.Result.Secret;
            FillList();
            Save();
        }

        void DelEntry()
        {
            PwdEntry e = Sel();
            if (e == null) return;
            if (MessageBox.Show(this, en ? "Delete this entry?" : "删除这条记录？", "小工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            list.Remove(e);
            FillList();
            Save();
        }

        void CopyPwd()
        {
            PwdEntry e = Sel();
            if (e == null) return;
            try { Clipboard.SetText(e.Pwd); Log(en ? "password copied" : "密码已复制"); } catch (Exception ex) { Log(ex.Message); }
        }

        void CopyUser()
        {
            PwdEntry e = Sel();
            if (e == null) return;
            try { Clipboard.SetText(e.User); Log(en ? "account copied" : "账号已复制"); } catch (Exception ex) { Log(ex.Message); }
        }

        void CopyCode()
        {
            PwdEntry e = Sel();
            if (e == null || e.Secret.Length == 0) return;
            try
            {
                string code = PwdBookCore.Totp(e.Secret, DateTime.Now, 6, 30);
                Clipboard.SetText(code);
                Log(en ? "code copied" : "验证码已复制");
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { tmCode.Stop(); tmCode.Dispose(); } catch (Exception dg345_1) { Diag.Swallow("PwdBookForm.cs:345", dg345_1); }
            base.OnFormClosing(e);
        }
    }
}