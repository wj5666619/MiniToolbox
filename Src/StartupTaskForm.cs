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
    class StartupTaskForm : Form
    {
        bool en;
        string dir;
        ListView lv;
         RoundCheck chkSys;
        Label st;
        List<StartupTaskCore.TaskItem> data = new List<StartupTaskCore.TaskItem>();

        public StartupTaskForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "启动项管理 · 计划任务 · 小工具箱", "Startup & scheduled tasks · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(920, 620);
            MinimumSize = new Size(920, 620);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "启动项与计划任务", "Startup & scheduled tasks"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "这里管的是 Windows 计划任务（schtasks）：禁用不会删除，随时能恢复；新建支持「登录时启动」和「每天定时」。",
                "Manages Windows scheduled tasks: disable keeps it restorable; create on-logon or daily."), 20, 48, 880, 40));

            chkSys = new RoundCheck();
            chkSys.Text = V71Ui.TT(en, "显示系统自带任务", "Show system tasks");
            chkSys.ForeColor = V71Ui.SUB;
            chkSys.Location = new Point(20, 88);
            chkSys.Size = new Size(200, 22);
            chkSys.BackColor = Color.Transparent;
            Controls.Add(chkSys);

            lv = V71Ui.List(new string[] {
                V71Ui.TT(en,"任务名","Task"), V71Ui.TT(en,"状态","State"), V71Ui.TT(en,"触发","Trigger"), V71Ui.TT(en,"执行","Action") },
                new int[] { 260, 80, 160, 380 }, 20, 116, 880, 380);
            Controls.Add(lv);

            st = V71Ui.Note("", 20, 506, 880, 22);
            Controls.Add(st);

            Button bRef = V71Ui.Btn(V71Ui.TT(en, "刷新", "Refresh"), UiKit.A(96, 165, 250), 20, 534, 96, 32);
            Button bOn = V71Ui.Btn(V71Ui.TT(en, "启用", "Enable"), UiKit.A(34, 197, 94), 124, 534, 96, 32);
            Button bOff = V71Ui.Btn(V71Ui.TT(en, "禁用", "Disable"), UiKit.A(249, 115, 22), 228, 534, 96, 32);
            Button bRun = V71Ui.Btn(V71Ui.TT(en, "立即运行", "Run now"), UiKit.A(14, 165, 233), 332, 534, 110, 32);
            Button bDel = V71Ui.Btn(V71Ui.TT(en, "删除", "Delete"), UiKit.A(232, 82, 61), 450, 534, 96, 32);
            Button bNew = V71Ui.Btn(V71Ui.TT(en, "新建任务", "Create"), UiKit.A(139, 92, 246), 554, 534, 110, 32);
            Button bTm = V71Ui.Btn(V71Ui.TT(en, "任务管理器启动项", "TaskMgr startup"), UiKit.A(251, 146, 60), 672, 534, 150, 32);
            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 806, 534, 94, 32);
            Controls.Add(bRef); Controls.Add(bOn); Controls.Add(bOff); Controls.Add(bRun);
            Controls.Add(bDel); Controls.Add(bNew); Controls.Add(bTm); Controls.Add(bClose);

            Action refresh = delegate
            {
                Cursor.Current = Cursors.WaitCursor;
                string err = "";
                data = StartupTaskCore.List(chkSys.Checked, out err);
                lv.Items.Clear();
                foreach (StartupTaskCore.TaskItem it in data)
                {
                    ListViewItem li = new ListViewItem(new string[] { it.Name, it.State, it.When, it.Run });
                    li.Tag = it;
                    if (it.State.IndexOf("Disabled") >= 0 || it.State.IndexOf("禁用") >= 0)
                        li.ForeColor = UiKit.A(140, 150, 165);
                    lv.Items.Add(li);
                }
                Cursor.Current = Cursors.Default;
                st.Text = V71Ui.TT(en, "共 ", "total ") + data.Count + V71Ui.TT(en, " 项计划任务", " scheduled tasks")
                    + (err.Length > 0 ? "  ·  " + err : "");
            };

            bRef.Click += delegate { refresh(); };
            chkSys.CheckedChanged += delegate { refresh(); };
            bOn.Click += delegate { Toggle(true); };
            bOff.Click += delegate { Toggle(false); };
            bRun.Click += delegate { RunOne(); };
            bDel.Click += delegate { DeleteOne(); };
            bNew.Click += delegate { CreateNew(); };
            bTm.Click += delegate { StartupTaskCore.OpenStartupSettings(); };
            bClose.Click += delegate { Close(); };

            refresh();
        }

        StartupTaskCore.TaskItem Sel()
        {
            if (lv.SelectedItems.Count == 0) return null;
            return (StartupTaskCore.TaskItem)lv.SelectedItems[0].Tag;
        }

        void Toggle(bool enable)
        {
            StartupTaskCore.TaskItem it = Sel();
            if (it == null) { V71Ui.Say(this, V71Ui.TT(en, "先选一行任务", "Pick a task first")); return; }
            string err = "";
            if (!StartupTaskCore.Change(it.Name, enable, out err))
                V71Ui.Say(this, V71Ui.TT(en, "改不起来（可能需要管理员）：", "Failed (admin may be required): ") + err);
            else
                st.Text = V71Ui.TT(en, "已切换：", "switched: ") + it.Name;
            Refresh2();
        }

        void Refresh2()
        {
            string err = "";
            data = StartupTaskCore.List(chkSys.Checked, out err);
            lv.Items.Clear();
            foreach (StartupTaskCore.TaskItem it in data)
            {
                ListViewItem li = new ListViewItem(new string[] { it.Name, it.State, it.When, it.Run });
                li.Tag = it;
                if (it.State.IndexOf("Disabled") >= 0 || it.State.IndexOf("禁用") >= 0)
                    li.ForeColor = UiKit.A(140, 150, 165);
                lv.Items.Add(li);
            }
        }

        void RunOne()
        {
            StartupTaskCore.TaskItem it = Sel();
            if (it == null) { V71Ui.Say(this, V71Ui.TT(en, "先选一行任务", "Pick a task first")); return; }
            string err = "";
            if (!StartupTaskCore.RunNow(it.Name, out err))
                V71Ui.Say(this, V71Ui.TT(en, "运行失败：", "Run failed: ") + err);
            else
                st.Text = V71Ui.TT(en, "已触发运行：", "triggered: ") + it.Name;
        }

        void DeleteOne()
        {
            StartupTaskCore.TaskItem it = Sel();
            if (it == null) { V71Ui.Say(this, V71Ui.TT(en, "先选一行任务", "Pick a task first")); return; }
            DialogResult r = MessageBox.Show(this, V71Ui.TT(en, "确定删除这条计划任务？", "Delete this scheduled task?"),
                "小工具箱", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            string err = "";
            if (!StartupTaskCore.Delete(it.Name, out err))
                V71Ui.Say(this, V71Ui.TT(en, "删除失败：", "Delete failed: ") + err);
            Refresh2();
        }

        void CreateNew()
        {
            Form f = V71Ui.NewForm(en, "新建计划任务 · 小工具箱", "Create task · Mini Toolbox", 520, 300);
            f.Controls.Add(V71Ui.Title(V71Ui.TT(en, "新建计划任务", "Create task"), 20, 14));
            f.Controls.Add(V71Ui.Note(V71Ui.TT(en, "任务名不要带反斜杠；执行路径要写完整路径。", "No backslash in name; use a full path in the action."), 20, 46, 470, 22));

            f.Controls.Add(V71Ui.Text(V71Ui.TT(en, "任务名", "Name"), 20, 84, 100, 22, 9F, V71Ui.SUB));
            TextBox tName = V71Ui.Box("小工具箱启动项", 130, 82, 360, 24);
            f.Controls.Add(V71Ui.Text(V71Ui.TT(en, "执行", "Action"), 20, 122, 100, 22, 9F, V71Ui.SUB));
            TextBox tCmd = V71Ui.Box("\"" + Path.Combine(dir, "小工具箱.exe") + "\"", 130, 120, 360, 24);
            f.Controls.Add(V71Ui.Text(V71Ui.TT(en, "方式", "When"), 20, 160, 100, 22, 9F, V71Ui.SUB));
            ComboBox cbMode = V71Ui.Combo(new string[] {
                V71Ui.TT(en, "登录时自动启动", "At logon"),
                V71Ui.TT(en, "每天定时", "Daily at") }, 130, 158, 200);
            TextBox tTime = V71Ui.Box("09:30", 340, 158, 100, 24);
            f.Controls.Add(tName); f.Controls.Add(tCmd); f.Controls.Add(cbMode); f.Controls.Add(tTime);

            Label tip = V71Ui.Note("", 20, 196, 470, 22);
            f.Controls.Add(tip);

            Button ok = V71Ui.Btn(V71Ui.TT(en, "创建", "Create"), UiKit.A(34, 197, 94), 130, 240, 110, 34);
            Button cc = V71Ui.Btn(V71Ui.TT(en, "取消", "Cancel"), UiKit.A(52, 58, 70), 254, 240, 110, 34);
            f.Controls.Add(ok); f.Controls.Add(cc);
            cc.Click += delegate { f.Close(); };
            ok.Click += delegate
            {
                string name = tName.Text.Trim();
                if (name.Length == 0) { tip.Text = V71Ui.TT(en, "任务名不能为空", "Name required"); return; }
                string err = "";
                bool okk = StartupTaskCore.Create(name, tCmd.Text.Trim(), cbMode.SelectedIndex == 0, tTime.Text.Trim(), out err);
                if (!okk) { tip.Text = err; return; }
                f.Close();
                V71Ui.Say(this, V71Ui.TT(en, "已创建：", "created: ") + name);
                Refresh2();
            };
            f.ShowDialog(this);
        }
    }
}
