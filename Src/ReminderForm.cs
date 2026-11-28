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
    class ReminderForm : Form
    {
        ListView lv;
        public ReminderForm()
        {
            Text = "定时提醒";
            ClientSize = new Size(700, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = UiKit.A(31, 35, 41);
            ForeColor = UiKit.Txt(Color.White);
            Font = new Font("Microsoft YaHei UI", 9F);

            Label t = new Label();
            t.Text = "定时提醒";
            t.ForeColor = UiKit.Txt(Color.White);
            t.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            t.Location = new Point(18, 14);
            t.AutoSize = true;
            Controls.Add(t);

            Label s = new Label();
            s.Text = "到点自动弹窗提醒，内容自定义；支持一次性 / 每天 / 每周 / 每隔 N 分钟。";
            s.ForeColor = UiKit.A(150, 162, 180);
            s.Font = new Font("Microsoft YaHei UI", 8.5F);
            s.Location = new Point(20, 48);
            s.AutoSize = true;
            Controls.Add(s);

            lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.MultiSelect = false;
            lv.BorderStyle = BorderStyle.None;
            lv.BackColor = UiKit.A(38, 45, 58);
            lv.ForeColor = UiKit.Txt(Color.White);
            lv.Location = new Point(20, 76);
            lv.Size = new Size(660, 340);
            lv.Columns.Add("状态", 60);
            lv.Columns.Add("下次提醒", 150);
            lv.Columns.Add("标题", 150);
            lv.Columns.Add("内容", 200);
            lv.Columns.Add("重复", 100);
            lv.DoubleClick += (a, b) => EditSel();
            Controls.Add(lv);

            Button add = MkBtn("新建", 20, 424, 90, 30, UiKit.A(208, 58, 68));
            add.Click += (a, b) => AddNew();
            Controls.Add(add);

            Button ed = MkBtn("编辑", 118, 424, 90, 30, UiKit.A(52, 74, 108));
            ed.Click += (a, b) => EditSel();
            Controls.Add(ed);

            Button del = MkBtn("删除", 216, 424, 90, 30, UiKit.A(60, 66, 78));
            del.Click += (a, b) => DelSel();
            Controls.Add(del);

            Button tg = MkBtn("启用/停用", 314, 424, 100, 30, UiKit.A(60, 66, 78));
            tg.Click += (a, b) => ToggleSel();
            Controls.Add(tg);

            Button ts = MkBtn("立即测试", 422, 424, 100, 30, UiKit.A(60, 66, 78));
            ts.Click += (a, b) => TestSel();
            Controls.Add(ts);

            Button cl = MkBtn("关闭", 590, 424, 90, 30, UiKit.A(60, 66, 78));
            cl.Click += (a, b) => Close();
            Controls.Add(cl);

            Reload();
        }

        Button MkBtn(string text, int x, int y, int w, int h, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = UiKit.Txt(Color.White);
            b.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            ((Control)b).Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            return b;
        }

        void Reload()
        {
            lv.Items.Clear();
            for (int i = 0; i < ReminderStore.Items.Count; i++)
            {
                ReminderItem it = ReminderStore.Items[i];
                ListViewItem li = new ListViewItem(it.On ? "已启用" : "已停用");
                li.SubItems.Add(it.TimeText);
                li.SubItems.Add(it.Title);
                li.SubItems.Add((it.Body ?? "").Replace("\r", " ").Replace("\n", " "));
                li.SubItems.Add(it.RepeatText());
                li.Tag = it;
                if (!it.On) li.ForeColor = UiKit.A(130, 138, 150);
                lv.Items.Add(li);
            }
        }

        ReminderItem Sel()
        {
            if (lv.SelectedItems.Count == 0) return null;
            return lv.SelectedItems[0].Tag as ReminderItem;
        }

        void AddNew()
        {
            ReminderEditForm f = new ReminderEditForm(null);
            if (f.ShowDialog(this) == DialogResult.OK && f.Result != null)
            {
                f.Result.Id = ReminderStore.NextId();
                ReminderStore.Items.Add(f.Result);
                ReminderStore.Save(BASE_DIR_OF());
                Reload();
            }
        }

        void EditSel()
        {
            ReminderItem it = Sel();
            if (it == null) { MessageBox.Show(this, "请先选中一条提醒。", "定时提醒", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            ReminderEditForm f = new ReminderEditForm(it);
            if (f.ShowDialog(this) == DialogResult.OK && f.Result != null) ReminderStore.Save(BASE_DIR_OF());
            Reload();
        }

        void DelSel()
        {
            ReminderItem it = Sel();
            if (it == null) return;
            if (MessageBox.Show(this, "确定删除这条提醒？", "定时提醒", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ReminderStore.Items.Remove(it);
            ReminderStore.Save(BASE_DIR_OF());
            Reload();
        }

        void ToggleSel()
        {
            ReminderItem it = Sel();
            if (it == null) return;
            it.On = !it.On;
            ReminderStore.Save(BASE_DIR_OF());
            Reload();
        }

        void TestSel()
        {
            ReminderItem it = Sel();
            if (it == null) return;
            ReminderPopup p = new ReminderPopup(it);
            p.Show();
        }

        static string BASE_DIR_OF()
        {
            return Path.GetDirectoryName(Application.ExecutablePath);
        }
    }
}
