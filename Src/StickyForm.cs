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
    class StickyForm : Form
    {
        bool en;
        string dir;
        List<StickyCore.Note> notes = new List<StickyCore.Note>();
        ListView lv;
        Dictionary<int, StickyNoteForm> opened = new Dictionary<int, StickyNoteForm>();
        int nextId = 1;

        public StickyForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "桌面便签 · 小工具箱", "Desktop sticky notes · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 540);
            MinimumSize = new Size(820, 540);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "桌面便签", "Sticky notes"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "每条便签是一个独立小窗口，可置顶、可拖动、自动保存到 便签.dat；关掉主程序也不丢。",
                "Each note is its own small window: pin, drag, auto-saved to 便签.dat."), 20, 48, 780, 40));

            lv = V71Ui.List(new string[] { V71Ui.TT(en, "标题", "Title"), V71Ui.TT(en, "内容预览", "Preview"), V71Ui.TT(en, "置顶", "Pin") },
                new int[] { 200, 500, 80 }, 20, 96, 780, 330);
            Controls.Add(lv);

            Button bNew = V71Ui.Btn(V71Ui.TT(en, "新建便签", "New note"), UiKit.A(96, 165, 250), 20, 442, 120, 34);
            Button bOpen = V71Ui.Btn(V71Ui.TT(en, "打开", "Open"), UiKit.A(34, 197, 94), 150, 442, 96, 34);
            Button bDel = V71Ui.Btn(V71Ui.TT(en, "删除", "Delete"), UiKit.A(232, 82, 61), 256, 442, 96, 34);
            Button bClock = V71Ui.Btn(V71Ui.TT(en, "番茄钟", "Pomodoro"), UiKit.A(249, 115, 22), 366, 442, 110, 34);
            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 700, 442, 100, 34);
            Controls.Add(bNew); Controls.Add(bOpen); Controls.Add(bDel); Controls.Add(bClock); Controls.Add(bClose);
            bClose.Click += delegate { Close(); };

            notes = StickyCore.Load(dir);
            foreach (StickyCore.Note n in notes) if (n.Id >= nextId) nextId = n.Id + 1;
            Refresh2();

            bNew.Click += delegate
            {
                StickyCore.Note n = new StickyCore.Note();
                n.Id = nextId++;
                n.Title = V71Ui.TT(en, "便签", "Note") + " " + n.Id;
                n.Body = "";
                n.X = 120 + (n.Id % 6) * 24;
                n.Y = 120 + (n.Id % 6) * 24;
                notes.Add(n);
                StickyCore.Save(dir, notes);
                Refresh2();
                OpenNote(n);
            };

            bOpen.Click += delegate
            {
                StickyCore.Note n = Sel();
                if (n == null) return;
                OpenNote(n);
            };

            bDel.Click += delegate
            {
                StickyCore.Note n = Sel();
                if (n == null) return;
                if (MessageBox.Show(this, V71Ui.TT(en, "删除这条便签？", "Delete this note?"), "小工具箱",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                notes.Remove(n);
                if (opened.ContainsKey(n.Id))
                {
                    try { opened[n.Id].Close(); } catch (Exception dg95_1) { Diag.Swallow("StickyForm.cs:95", dg95_1); }
                    opened.Remove(n.Id);
                }
                StickyCore.Save(dir, notes);
                Refresh2();
            };

            bClock.Click += delegate
            {
                PomodoroForm pf = new PomodoroForm(en, dir);
                pf.Show(this);
            };

            FormClosing += delegate { StickyCore.Save(dir, notes); };
        }

        void Refresh2()
        {
            lv.Items.Clear();
            foreach (StickyCore.Note n in notes)
            {
                string prev = n.Body.Replace("\r\n", " ");
                if (prev.Length > 60) prev = prev.Substring(0, 60) + "…";
                ListViewItem li = new ListViewItem(new string[] { n.Title, prev, n.Top ? "*" : "" });
                li.Tag = n;
                lv.Items.Add(li);
            }
        }

        StickyCore.Note Sel()
        {
            if (lv.SelectedItems.Count == 0) return null;
            return (StickyCore.Note)lv.SelectedItems[0].Tag;
        }

        void OpenNote(StickyCore.Note n)
        {
            if (opened.ContainsKey(n.Id))
            {
                try { opened[n.Id].Activate(); } catch (Exception dg134_1) { Diag.Swallow("StickyForm.cs:134", dg134_1); }
                return;
            }
            StickyNoteForm sf = new StickyNoteForm(n, en);
            sf.FormClosed += delegate
            {
                opened.Remove(n.Id);
                StickyCore.Save(dir, notes);
                Refresh2();
            };
            opened[n.Id] = sf;
            sf.Show();
        }
    }
}