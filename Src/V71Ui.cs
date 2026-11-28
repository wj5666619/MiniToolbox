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
    static class V71Ui
    {
        public static readonly Color BG = UiKit.A(20, 24, 30);
        public static readonly Color INPUT = UiKit.A(30, 35, 44);
        public static readonly Color TEXT = UiKit.A(226, 232, 240);
        public static readonly Color SUB = UiKit.A(164, 174, 192);
        public static readonly Color GOOD = UiKit.A(120, 220, 160);
        public static readonly Color BAD = UiKit.A(248, 113, 113);
        public static readonly Color WARN = UiKit.A(250, 190, 90);

        public static string TT(bool en, string zh, string eng) { return en ? eng : zh; }

        public static Form NewForm(bool en, string zh, string eng, int w, int h)
        {
            Form f = new Form();
            f.Text = en ? eng : zh;
            f.BackColor = BG;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(w, h);
            f.MinimumSize = new Size(w, h);
            f.ShowIcon = false;
            f.Font = new Font("Microsoft YaHei UI", 9F);
            return f;
        }

        public static Button Btn(string text, Color bg, int x, int y, int w, int h)
        {
            RoundBtn b = new RoundBtn(bg, Color.White);
            b.Text = text;
            b.ForeColor = UiKit.Txt(Color.White);
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        public static ListView List(string[] cols, int[] widths, int x, int y, int w, int h)
        {
            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.BorderStyle = BorderStyle.None;
            lv.BackColor = INPUT;
            lv.ForeColor = TEXT;
            lv.Font = new Font("Microsoft YaHei UI", 9F);
            lv.HideSelection = false;
            for (int i = 0; i < cols.Length; i++) lv.Columns.Add(cols[i], widths[i]);
            lv.Location = new Point(x, y);
            lv.Size = new Size(w, h);
            return lv;
        }

        public static Label Title(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = UiKit.Txt(Color.White);
            l.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        public static Label Note(string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = SUB;
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            return l;
        }

        public static Label Text(string text, int x, int y, int w, int h, float size, Color c)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = c;
            l.Font = new Font("Microsoft YaHei UI", size);
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            return l;
        }

        public static TextBox Box(string text, int x, int y, int w, int h)
        {
            TextBox tb = new TextBox();
            tb.Text = text;
            tb.BackColor = INPUT;
            tb.ForeColor = TEXT;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = new Font("Microsoft YaHei UI", 9F);
            tb.Location = new Point(x, y);
            tb.Size = new Size(w, h);
            return tb;
        }

        public static ComboBox Combo(string[] items, int x, int y, int w)
        {
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.BackColor = INPUT;
            cb.ForeColor = TEXT;
            cb.FlatStyle = FlatStyle.Standard;
            cb.Font = new Font("Microsoft YaHei UI", 9F);
            cb.Location = new Point(x, y);
            cb.Size = new Size(w, 26);
            cb.Items.AddRange(items);
            if (items.Length > 0) cb.SelectedIndex = 0;
            SkinCombo(cb);
            return cb;
        }

        // v12.0：原生下拉框在深色界面里永远带着一圈浅灰描边和系统箭头（像两块白斑）。
        // 这里整块盖一个自绘的深色选择器，点开走深色右键菜单，选完写回原生控件——视觉统一，行为不变。
        public static void SkinCombo(ComboBox cb)
        {
            cb.ParentChanged += delegate
            {
                try
                {
                    if (cb.Parent == null) return;
                    RoundBtn cover = new RoundBtn(INPUT, TEXT);
                    cover.Tag = "cbcover";          // v12.3：排版系统不参与，保持与原下拉框同宽
                    cover.Bounds = new Rectangle(cb.Left - 1, cb.Top - 1, cb.Width + 2, cb.Height + 2);
                    cover.TextAlign = ContentAlignment.MiddleLeft;
                    cover.Font = new Font("Microsoft YaHei UI", 9F);
                    cover.Cursor = Cursors.Hand;
                    cover.Text = "   " + cb.Text + "\u2003\u25BE";
                    ContextMenuStrip cms = new ContextMenuStrip();
                    cms.BackColor = UiKit.A(32, 36, 45);
                    cms.ForeColor = UiKit.A(232, 236, 242);
                    cms.Font = new Font("Microsoft YaHei UI", 9.5F);
                    cms.Renderer = new DarkMenuRenderer();
                    cms.ShowImageMargin = false;
                    cms.Padding = new Padding(0, 4, 0, 4);
                    for (int i = 0; i < cb.Items.Count; i++)
                    {
                        string txt = cb.Items[i] == null ? "" : cb.Items[i].ToString();
                        int idx = i;
                        cms.Items.Add(txt, null, (s2, e2) =>
                        {
                            cb.SelectedIndex = idx;
                            cover.Text = "   " + cb.Text + "\u2003\u25BE";
                        });
                    }
                    cover.Click += (s2, e2) => cms.Show(cover, new Point(0, cover.Height + 2));
                    cb.SelectedIndexChanged += (s2, e2) => { cover.Text = "   " + cb.Text + "\u2003\u25BE"; };
                    cb.Parent.Controls.Add(cover);
                    cover.BringToFront();
                    cb.Visible = false;
                }
                catch (Exception dg180_1) { Diag.Swallow("V71Ui.cs:180", dg180_1); }
            };
        }

        public static void Say(Form owner, string text)
        {
            MessageBox.Show(owner, text, "小工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
    }
}