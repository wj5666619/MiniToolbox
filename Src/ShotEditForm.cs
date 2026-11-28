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
    class ShotEditForm : Form
    {
        Bitmap src;
        bool en;
        Panel view;
        ShotCanvas cv;
        int tool = 0;
        List<ShotAnn> anns = new List<ShotAnn>();
        List<Button> toolBtns = new List<Button>();
        bool dragging = false;
        Point p0, p1;
        Label st;
        int numSeq = 1;
        public Bitmap Result;
        public bool Saved;

        string L(string zh, string et) { return en ? et : zh; }

        Button MkBtn(string t, Color bg, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = t;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = bg;
            b.ForeColor = UiKit.Txt(Color.White);
            b.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
        }

        public ShotEditForm(Bitmap img, bool enUs)
        {
            src = img;
            en = enUs;
            Text = L("截图标注 · 小工具箱", "Screenshot Editor · Mini Toolbox");
            BackColor = UiKit.A(20, 24, 30);
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            TopMost = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            int vw = Math.Min(src.Width, 1280);
            int vh = Math.Min(src.Height, 780);
            ClientSize = new Size(vw + 24, vh + 96);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 48;
            bar.BackColor = UiKit.A(26, 31, 39);
            Controls.Add(bar);

            view = new Panel();
            view.Dock = DockStyle.Fill;
            view.AutoScroll = true;
            view.BackColor = UiKit.A(16, 19, 24);
            Controls.Add(view);
            view.BringToFront();

            cv = new ShotCanvas();
            cv.Location = new Point(0, 0);
            cv.Size = new Size(src.Width, src.Height);
            cv.BackColor = Color.Black;
            cv.Cursor = Cursors.Cross;
            view.Controls.Add(cv);

            cv.Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.DrawImageUnscaled(src, 0, 0);
                ShotEditForm.DrawAnns(e.Graphics, src, anns);
                if (dragging)
                {
                    Rectangle r = ShotEditForm.Norm(p0, p1);
                    using (Pen p = new Pen(UiKit.A(250, 190, 90), 2))
                        e.Graphics.DrawRectangle(p, r);
                }
            };

            cv.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                p0 = e.Location;
                p1 = e.Location;
                dragging = true;
            };
            cv.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (!dragging) return;
                p1 = e.Location;
                cv.Invalidate();
            };
            cv.MouseUp += delegate (object s, MouseEventArgs e)
            {
                if (!dragging) return;
                dragging = false;
                p1 = e.Location;
                Rectangle r = ShotEditForm.Norm(p0, p1);
                if (tool == 3)
                {
                    string txt = Microsoft.VisualBasic.Interaction.InputBox(L("输入要标注的文字：", "Text to add:"), L("文字标注", "Text"), "");
                    if (txt != null && txt.Length > 0)
                    {
                        ShotAnn a = new ShotAnn();
                        a.Tool = 3; a.Text = txt;
                        a.R = new Rectangle(p0.X, p0.Y, 10, 10);
                        anns.Add(a);
                    }
                }
                else if (tool == 4)
                {
                    ShotAnn a = new ShotAnn();
                    a.Tool = 4; a.Num = numSeq++;
                    a.R = new Rectangle(p0.X, p0.Y, 4, 4);
                    anns.Add(a);
                }
                else if (tool == 0)
                {
                    if (Math.Abs(p1.X - p0.X) + Math.Abs(p1.Y - p0.Y) > 8)
                    {
                        ShotAnn a = new ShotAnn();
                        a.Tool = 0; a.A = p0; a.B = p1;
                        anns.Add(a);
                    }
                }
                else if (r.Width > 6 && r.Height > 6)
                {
                    ShotAnn a = new ShotAnn();
                    a.Tool = tool; a.R = r;
                    anns.Add(a);
                }
                cv.Invalidate();
            };

            int bx = 8;
            string[] names = new string[] { L("箭头", "Arrow"), L("矩形", "Box"), L("马赛克", "Pixel"), L("文字", "Text"), L("序号", "No.") };
            Color[] colors = new Color[] { UiKit.A(235, 82, 61), UiKit.A(59, 130, 246), UiKit.A(120, 130, 150), UiKit.A(16, 185, 129), UiKit.A(250, 190, 90) };
            for (int i = 0; i < 5; i++)
            {
                Button b = MkBtn(names[i], UiKit.A(44, 50, 60), bx, 9, 72, 30);
                int idx = i;
                b.Click += delegate
                {
                    tool = idx;
                    for (int j = 0; j < toolBtns.Count; j++)
                        toolBtns[j].BackColor = j == idx ? colors[j] : UiKit.A(44, 50, 60);
                };
                toolBtns.Add(b);
                bar.Controls.Add(b);
                bx += 78;
            }
            toolBtns[0].BackColor = colors[0];

            Button bUndo = MkBtn(L("撤销", "Undo"), UiKit.A(44, 50, 60), bx, 9, 72, 30);
            bx += 78;
            bUndo.Click += delegate
            {
                if (anns.Count > 0) { anns.RemoveAt(anns.Count - 1); cv.Invalidate(); }
            };
            bar.Controls.Add(bUndo);

            Button bCopy = MkBtn(L("复制", "Copy"), UiKit.A(14, 165, 233), bx, 9, 80, 30);
            bx += 86;
            bCopy.Click += delegate
            {
                Bitmap outb = ShotEditForm.Compose(src, anns);
                Result = outb;
                try { Clipboard.SetImage(outb); st.Text = L("已复制到剪贴板", "Copied to clipboard"); }
                catch (Exception ex) { st.Text = ex.Message; }
            };
            bar.Controls.Add(bCopy);

            Button bSave = MkBtn(L("保存", "Save"), UiKit.A(16, 185, 129), bx, 9, 80, 30);
            bx += 86;
            bSave.Click += delegate
            {
                Bitmap outb = ShotEditForm.Compose(src, anns);
                Result = outb;
                SaveFileDialog sd = new SaveFileDialog();
                sd.Filter = "PNG 图片|*.png";
                sd.FileName = "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                try { sd.InitialDirectory = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "截图"); } catch (Exception dg204_1) { Diag.Swallow("ShotEditForm.cs:204", dg204_1); }
                if (sd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        outb.Save(sd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                        Saved = true;
                        st.Text = L("已保存：", "Saved: ") + sd.FileName;
                Sfx.Save();
                    }
                    catch (Exception ex) { st.Text = ex.Message; }
                }
            };
            bar.Controls.Add(bSave);

            Button bClose = MkBtn(L("关闭", "Close"), UiKit.A(51, 58, 70), bx, 9, 80, 30);
            bClose.Click += delegate { Close(); };
            bar.Controls.Add(bClose);

            st = new Label();
            st.ForeColor = UiKit.A(150, 160, 176);
            st.AutoSize = true;
            st.Location = new Point(bx + 92, 17);
            bar.Controls.Add(st);
        }

        public static Rectangle Norm(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            return new Rectangle(x, y, w, h);
        }

        public static Bitmap Compose(Bitmap baseImg, List<ShotAnn> list)
        {
            Bitmap outb = new Bitmap(baseImg.Width, baseImg.Height);
            using (Graphics g = Graphics.FromImage(outb))
            {
                g.DrawImageUnscaled(baseImg, 0, 0);
                DrawAnns(g, baseImg, list);
            }
            return outb;
        }

        public static void DrawAnns(Graphics g, Bitmap baseImg, List<ShotAnn> list)
        {
            if (list == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (ShotAnn a in list)
            {
                switch (a.Tool)
                {
                    case 0:
                        {
                            using (Pen p = new Pen(UiKit.A(235, 82, 61), 3)) g.DrawLine(p, a.A, a.B);
                            double ang = Math.Atan2(a.B.Y - a.A.Y, a.B.X - a.A.X);
                            const double sp = 0.45;
                            const int len = 16;
                            Point q1 = new Point(a.B.X - (int)(len * Math.Cos(ang - sp)), a.B.Y - (int)(len * Math.Sin(ang - sp)));
                            Point q2 = new Point(a.B.X - (int)(len * Math.Cos(ang + sp)), a.B.Y - (int)(len * Math.Sin(ang + sp)));
                            using (SolidBrush br = new SolidBrush(UiKit.A(235, 82, 61)))
                                g.FillPolygon(br, new Point[] { a.B, q1, q2 });
                            break;
                        }
                    case 1:
                        {
                            using (Pen p = new Pen(UiKit.A(96, 165, 250), 3)) g.DrawRectangle(p, a.R);
                            break;
                        }
                    case 2:
                        {
                            DrawMosaic(g, baseImg, a.R);
                            break;
                        }
                    case 3:
                        {
                            using (Font ft = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold))
                            {
                                using (SolidBrush sh = new SolidBrush(UiKit.AA(170, 0, 0, 0)))
                                    g.DrawString(a.Text, ft, sh, new PointF(a.R.X + 2, a.R.Y + 2));
                                using (SolidBrush fb = new SolidBrush(UiKit.AA(255, 244, 246, 250)))
                                    g.DrawString(a.Text, ft, fb, new PointF(a.R.X, a.R.Y));
                            }
                            break;
                        }
                    case 4:
                        {
                            int rad = 14;
                            using (SolidBrush br = new SolidBrush(UiKit.A(250, 190, 90)))
                                g.FillEllipse(br, a.R.X - rad, a.R.Y - rad, rad * 2, rad * 2);
                            using (Font ft = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold))
                            using (SolidBrush tb = new SolidBrush(UiKit.A(20, 24, 30)))
                            {
                                string num = Convert.ToString(a.Num);
                                SizeF sz = g.MeasureString(num, ft);
                                g.DrawString(num, ft, tb, new PointF(a.R.X - sz.Width / 2, a.R.Y - sz.Height / 2));
                            }
                            break;
                        }
                }
            }
        }

        static void DrawMosaic(Graphics g, Bitmap baseImg, Rectangle r)
        {
            if (r.Width < 4 || r.Height < 4) return;
            Rectangle clipR = Rectangle.Intersect(r, new Rectangle(0, 0, baseImg.Width, baseImg.Height));
            if (clipR.Width < 2 || clipR.Height < 2) return;
            int bw = Math.Max(2, clipR.Width / 12);
            int bh = Math.Max(2, clipR.Height / 12);
            using (Bitmap small = new Bitmap(bw, bh))
            {
                using (Graphics gs = Graphics.FromImage(small))
                {
                    gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    gs.DrawImage(baseImg, new Rectangle(0, 0, bw, bh), clipR, GraphicsUnit.Pixel);
                }
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, clipR);
            }
        }
    }
}