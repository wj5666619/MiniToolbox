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
    class WinMgrForm : Form
    {
        bool en;
        string dir;
        ListView lv;
        Label st;
        List<WinCore.Item> data = new List<WinCore.Item>();
        public StringBuilder DumpBuf = null;
        public bool AutoClose = false;

        public WinMgrForm(bool eng, string baseDir)
        {
            en = eng; dir = baseDir;
            Text = en ? "Window Manager · Mini Toolbox" : "窗口管家 · 小工具箱";
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1020, 640);
            MinimumSize = new Size(900, 560);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "窗口管家", "Window Manager"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "列出当前所有可见窗口：一键置顶 / 分屏 / 最大化 / 收起全部，布局可以存下来下次还原",
                "List every visible window: pin on top, snap, maximize, hide all; save and restore layouts"), 20, 50, 980, 20));

            lv = V71Ui.List(new string[] { V71Ui.TT(en, "窗口标题", "Title"), "PID", V71Ui.TT(en, "进程", "Process"), V71Ui.TT(en, "状态", "State") },
                new int[] { 560, 70, 180, 150 }, 20, 82, 980, 430);
            Controls.Add(lv);

            st = V71Ui.Text(V71Ui.TT(en, "点「刷新」读取窗口列表", "Click Refresh to load windows"), 20, 526, 980, 20, 9F, V71Ui.SUB);
            Controls.Add(st);

            Button bR = V71Ui.Btn(V71Ui.TT(en, "刷新", "Refresh"), UiKit.A(59, 130, 246), 20, 560, 90, 34);
            bR.Click += delegate { Reload(); };
            Controls.Add(bR);

            Button bTop = V71Ui.Btn(V71Ui.TT(en, "置顶 / 取消", "Pin / Unpin"), UiKit.A(139, 92, 246), 120, 560, 120, 34);
            bTop.Click += delegate
            {
                WinCore.Item it = Sel();
                if (it == null) return;
                WinCore.Topmost(it.H, !it.Topmost);
                Reload();
            };
            Controls.Add(bTop);

            Button bL = V71Ui.Btn(V71Ui.TT(en, "左半屏", "Snap left"), UiKit.A(45, 212, 191), 250, 560, 90, 34);
            bL.Click += delegate { WinCore.Snap(Sel(), 1); Reload(); };
            Controls.Add(bL);

            Button bR2 = V71Ui.Btn(V71Ui.TT(en, "右半屏", "Snap right"), UiKit.A(45, 212, 191), 350, 560, 90, 34);
            bR2.Click += delegate { WinCore.Snap(Sel(), 2); Reload(); };
            Controls.Add(bR2);

            Button bMax = V71Ui.Btn(V71Ui.TT(en, "最大化", "Maximize"), UiKit.A(56, 189, 248), 450, 560, 90, 34);
            bMax.Click += delegate { WinCore.Snap(Sel(), 3); Reload(); };
            Controls.Add(bMax);

            Button bMin = V71Ui.Btn(V71Ui.TT(en, "最小化", "Minimize"), UiKit.A(148, 163, 184), 550, 560, 90, 34);
            bMin.Click += delegate { WinCore.Snap(Sel(), 4); Reload(); };
            Controls.Add(bMin);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭窗口", "Close window"), UiKit.A(232, 82, 61), 650, 560, 110, 34);
            bClose.Click += delegate
            {
                WinCore.Item it = Sel();
                if (it == null) return;
                if (MessageBox.Show(this, V71Ui.TT(en, "关闭「" + it.Title + "」？未保存的修改会丢失。", "Close \"" + it.Title + "\"? Unsaved work will be lost."),
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                WinCore.CloseWin(it.H);
                System.Threading.Thread.Sleep(300);
                Reload();
            };
            Controls.Add(bClose);

            Button bAll = V71Ui.Btn(V71Ui.TT(en, "一键收起全部", "Hide all"), UiKit.A(249, 115, 22), 770, 560, 120, 34);
            bAll.Click += delegate
            {
                int n = WinCore.MinimizeAllCount();
                st.Text = V71Ui.TT(en, "已收起 " + n + " 个窗口（点任务栏图标即可恢复）", n + " windows minimized (click taskbar to restore)");
                st.ForeColor = V71Ui.GOOD;
            };
            Controls.Add(bAll);

            Button bSave = V71Ui.Btn(V71Ui.TT(en, "保存布局", "Save layout"), UiKit.A(34, 197, 94), 20, 600, 110, 30);
            bSave.Click += delegate
            {
                string p = WinCore.SaveLayout(data, dir);
                st.Text = V71Ui.TT(en, "布局已保存：" + Path.GetFileName(p), "layout saved: " + Path.GetFileName(p));
                st.ForeColor = V71Ui.GOOD;
            };
            Controls.Add(bSave);

            Button bLoad = V71Ui.Btn(V71Ui.TT(en, "还原布局", "Restore layout"), UiKit.A(34, 197, 94), 140, 600, 110, 30);
            bLoad.Click += delegate
            {
                int n = WinCore.RestoreLayout(dir);
                st.Text = n < 0 ? V71Ui.TT(en, "没有找到保存过的布局", "no saved layout yet")
                    : V71Ui.TT(en, "已按保存的位置还原 " + n + " 个窗口", n + " windows restored");
                st.ForeColor = n > 0 ? V71Ui.GOOD : V71Ui.WARN;
                Reload();
            };
            Controls.Add(bLoad);

            Button bNear = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(43, 58, 80), 890, 600, 110, 30);
            bNear.Click += delegate { Close(); };
            Controls.Add(bNear);

            Reload();
            if (AutoClose) Dump();
        }

        WinCore.Item Sel()
        {
            if (lv.SelectedItems.Count == 0)
            {
                st.Text = V71Ui.TT(en, "请先选中一个窗口", "Select a window first");
                st.ForeColor = V71Ui.WARN;
                return null;
            }
            return (WinCore.Item)lv.SelectedItems[0].Tag;
        }

        void Reload()
        {
            int n = 0;
            data = WinCore.Collect(out n);
            lv.Items.Clear();
            foreach (WinCore.Item it in data)
            {
                string state = (it.Topmost ? V71Ui.TT(en, "置顶 ", "top ") : "") + (it.Min ? V71Ui.TT(en, "最小化", "min") : V71Ui.TT(en, "正常", "normal"));
                ListViewItem li = new ListViewItem(new string[] { it.Title, it.Pid.ToString(), it.Proc, state });
                li.Tag = it;
                if (it.Topmost) li.ForeColor = UiKit.A(196, 181, 253);
                lv.Items.Add(li);
            }
            lv.Invalidate();
            st.Text = V71Ui.TT(en, "共 " + data.Count + " 个可见窗口", data.Count + " visible windows");
            st.ForeColor = V71Ui.SUB;
        }

        void Dump()
        {
            if (DumpBuf != null)
            {
                DumpBuf.AppendLine("winmgr_ui ok");
                DumpBuf.AppendLine("hint_fields=" + Controls.Count);
            }
            try { Close(); } catch (Exception dg171_1) { Diag.Swallow("WinMgrForm.cs:171", dg171_1); }
        }
    }
}