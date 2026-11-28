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
    class StickyNoteForm : Form
    {
        public StickyCore.Note Note;
        TextBox body;


        public StickyNoteForm(StickyCore.Note n, bool eng)
        {
            Note = n;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = UiKit.A(255, 249, 196);
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(n.X, n.Y, n.W, n.H);
            TopMost = n.Top;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9.5F);

            Panel bar = new Panel();
            bar.Height = 26;
            bar.Dock = DockStyle.Top;
            bar.BackColor = UiKit.A(240, 226, 150);
            Controls.Add(bar);

            Label ttl = new Label();
            ttl.Text = n.Title.Length > 0 ? n.Title : "便签";
            ttl.ForeColor = UiKit.A(60, 52, 20);
            ttl.Location = new Point(8, 4);
            ttl.AutoSize = true;
            ttl.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            bar.Controls.Add(ttl);

            Button pin = new Button();
            pin.Text = n.Top ? "取消置顶" : "置顶";
            pin.FlatStyle = FlatStyle.Standard;
            pin.FlatAppearance.BorderSize = 0;
            pin.BackColor = Color.Transparent;
            pin.ForeColor = UiKit.A(60, 52, 20);
            pin.Size = new Size(64, 22);
            pin.Location = new Point(n.W - 96, 2);
            pin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bar.Controls.Add(pin);

            Button cl = new Button();
            cl.Text = "×";
            cl.FlatStyle = FlatStyle.Standard;
            cl.FlatAppearance.BorderSize = 0;
            cl.BackColor = Color.Transparent;
            cl.ForeColor = UiKit.A(140, 60, 40);
            cl.Size = new Size(26, 22);
            cl.Location = new Point(n.W - 30, 1);
            cl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bar.Controls.Add(cl);

            body = new TextBox();
            body.Multiline = true;
            body.BorderStyle = BorderStyle.None;
            body.BackColor = UiKit.A(255, 249, 196);
            body.ForeColor = UiKit.A(50, 44, 18);
            body.Font = new Font("Microsoft YaHei UI", 10F);
            body.Dock = DockStyle.Fill;
            body.Text = n.Body;
            body.ScrollBars = ScrollBars.Vertical;
            Controls.Add(body);

            pin.Click += delegate
            {
                Note.Top = !Note.Top;
                TopMost = Note.Top;
                pin.Text = Note.Top ? "取消置顶" : "置顶";
            };

            cl.Click += delegate
            {
                Note.Body = body.Text;
                Note.X = Left; Note.Y = Top; Note.W = Width; Note.H = Height;
                Close();
            };



            MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) DragBar(e); };
            bar.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) DragBar(e); };
            ttl.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) DragBar(e); };

            FormClosing += delegate
            {
                Note.Body = body.Text;
                Note.X = Left; Note.Y = Top; Note.W = Width; Note.H = Height;
            };
        }

        void DragBar(MouseEventArgs e)
        {
            NativeDrag();
        }

        void NativeDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
