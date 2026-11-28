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
    class ReminderEditForm : Form
    {
        public ReminderItem Result;
        ReminderItem src;
        TextBox txtTitle, txtBody;
        DateTimePicker dt;
        NumSpin numMin;
        ComboBox cmbRepeat;
         RoundCheck chkSound;

        public ReminderEditForm(ReminderItem edit)
        {
            src = edit;
            Text = edit == null ? "新建提醒" : "编辑提醒";
            ClientSize = new Size(520, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = UiKit.A(31, 35, 41);
            ForeColor = UiKit.Txt(Color.White);
            Font = new Font("Microsoft YaHei UI", 9F);

            int y = 16;
            AddLabel("标题", 18, y);
            txtTitle = AddInput(18, y + 22, 480);
            y += 66;
            AddLabel("提醒内容（可多行）", 18, y);
            txtBody = AddInput(18, y + 22, 480);
            txtBody.Multiline = true;
            txtBody.Height = 120;
            txtBody.ScrollBars = ScrollBars.Vertical;
            y += 148;
            AddLabel("提醒时间", 18, y);
            dt = new DateTimePicker();
            dt.Format = DateTimePickerFormat.Custom;
            dt.CustomFormat = "yyyy-MM-dd  HH:mm:ss";
            dt.Location = new Point(18, y + 22);
            dt.Size = new Size(230, 26);
            dt.Value = DateTime.Now.AddMinutes(5);
            Controls.Add(dt);

            AddLabel("重复", 264, y);
            cmbRepeat = new ComboBox();
            cmbRepeat.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRepeat.Location = new Point(264, y + 22);
            cmbRepeat.Size = new Size(140, 26);
            cmbRepeat.Items.Add("一次性");
            cmbRepeat.Items.Add("每天");
            cmbRepeat.Items.Add("每周");
            cmbRepeat.Items.Add("每隔 N 分钟");
            cmbRepeat.SelectedIndex = 0;
            cmbRepeat.SelectedIndexChanged += (a, b) =>
            {
                numMin.Enabled = (cmbRepeat.SelectedIndex == 3);
            };
            Controls.Add(cmbRepeat);

            numMin = new NumSpin();
            numMin.Minimum = 1;
            numMin.Maximum = 1440;
            numMin.Value = 30;
            numMin.Location = new Point(412, y + 22);
            numMin.Size = new Size(86, 26);
            numMin.Enabled = false;
            Controls.Add(numMin);

            y += 66;
            chkSound = new RoundCheck();
            chkSound.Text = "弹窗时播放提示音";
            chkSound.ForeColor = UiKit.Txt(Color.White);
            chkSound.Location = new Point(18, y);
            chkSound.AutoSize = true;
            chkSound.Checked = true;
            Controls.Add(chkSound);

            Button ok = new Button();
            ok.Text = "保存";
            ok.Location = new Point(320, 388);
            ok.Size = new Size(90, 32);
            ok.FlatStyle = FlatStyle.Standard;
            ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = UiKit.A(208, 58, 68);
            ok.ForeColor = UiKit.Txt(Color.White);
            ok.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            ok.Click += (a, b) => SaveAndClose();
            Controls.Add(ok);

            Button no = new Button();
            no.Text = "取消";
            no.Location = new Point(418, 388);
            no.Size = new Size(80, 32);
            no.FlatStyle = FlatStyle.Standard;
            no.FlatAppearance.BorderSize = 0;
            no.BackColor = UiKit.A(60, 66, 78);
            no.ForeColor = UiKit.Txt(Color.White);
            no.Click += (a, b) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(no);

            AcceptButton = ok;
            CancelButton = no;

            if (edit != null)
            {
                txtTitle.Text = edit.Title;
                txtBody.Text = edit.Body;
                try { dt.Value = edit.FireAt; } catch (Exception dg129_1) { Diag.Swallow("ReminderEditForm.cs:129", dg129_1); }
                cmbRepeat.SelectedIndex = Math.Min(3, Math.Max(0, edit.Repeat));
                numMin.Value = Math.Min(1440, Math.Max(1, edit.IntervalMin));
                numMin.Enabled = (edit.Repeat == 3);
                chkSound.Checked = edit.Sound;
            }
        }

        void AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = UiKit.A(150, 162, 180);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            Controls.Add(l);
        }

        TextBox AddInput(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, 26);
            t.BackColor = UiKit.A(38, 45, 58);
            t.ForeColor = UiKit.Txt(Color.White);
            t.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(t);
            return t;
        }

        void SaveAndClose()
        {
            if (txtTitle.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "请填写提醒标题。", "定时提醒", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ReminderItem it = src ?? new ReminderItem();
            it.Title = txtTitle.Text.Trim();
            it.Body = txtBody.Text;
            it.FireAt = dt.Value;
            it.Repeat = cmbRepeat.SelectedIndex;
            it.IntervalMin = (int)numMin.Value;
            it.Sound = chkSound.Checked;
            it.On = true;
            if (it.Repeat == 3)
            {
                // 间隔提醒：从当前时间起算，避免设成过去的时间后狂弹
                if (dt.Value <= DateTime.Now) it.FireAt = DateTime.Now.AddMinutes(it.IntervalMin);
            }
            Result = it;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}