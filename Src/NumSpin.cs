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
    class NumSpin : Control
    {
        TextBox edit;
        int min_ = 0, max_ = 999, val_ = 0, step_ = 1;
        bool hotUp, hotDn;
        public event EventHandler ValueChanged;

        public NumSpin()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(74, 28);
            base.BackColor = UiKit.Paper ? Color.FromArgb(243, 236, 220) : UiKit.A(26, 30, 38);
            base.ForeColor = UiKit.Paper ? UiKit.Ink : UiKit.A(232, 236, 242);
            edit = new TextBox();
            edit.BorderStyle = BorderStyle.None;
            edit.BackColor = UiKit.Paper ? Color.FromArgb(243, 236, 220) : Ui2.CtlInk(base.BackColor);   // v8.9：内部 TextBox 单独反补
            edit.ForeColor = base.ForeColor;
            edit.Font = new Font("Consolas", 12F, FontStyle.Bold);
            edit.Text = "0";
            edit.TabStop = true;
            edit.KeyDown += delegate (object s1, KeyEventArgs e1)
            {
                if (e1.KeyCode == Keys.Up) { Value = Value + step_; e1.Handled = true; }
                else if (e1.KeyCode == Keys.Down) { Value = Value - step_; e1.Handled = true; }
                else if (e1.KeyCode == Keys.Enter) { Commit(); e1.SuppressKeyPress = true; }
                else if (e1.KeyCode == Keys.Escape) { Sync(); e1.SuppressKeyPress = true; }
            };
            edit.TextChanged += delegate { Invalidate(); };
            edit.GotFocus += delegate { Invalidate(); };
            edit.LostFocus += delegate { if (UiKit.Paper) { edit.Visible = false; } Commit(); Invalidate(); };
            edit.MouseWheel += delegate (object s2, MouseEventArgs e2)
            {
                if (!Enabled) return;
                if (e2.Delta > 0) Value = Value + step_; else if (e2.Delta < 0) Value = Value - step_;
            };
            Controls.Add(edit);
            PlaceEdit();
        }

        // v8.9：NumSpin 是自绘的，本体忠实；但里面嵌着一只真 TextBox，它的底色会被提亮 +34。
        // 原来把同一个值同时喂给两者，结果就是"微调框里套着一小块更亮的矩形"。这里分开：本体用设计色，
        // 内部 TextBox 用反补色。
        public new Color BackColor
        {
            get { return base.BackColor; }
            set { base.BackColor = value; if (edit != null) edit.BackColor = UiKit.Paper ? value : Ui2.CtlInk(value); Invalidate(); }
        }
        public new Color ForeColor { get { return base.ForeColor; } set { base.ForeColor = value; if (edit != null) edit.ForeColor = value; Invalidate(); } }
        public new Font Font { get { return base.Font; } set { base.Font = value; if (edit != null) edit.Font = value; PlaceEdit(); } }
        // 兼容原来 NumericUpDown 的调用写法（本来就是无边框，这个属性现在只接收不生效）
        public BorderStyle BorderStyle { get { return BorderStyle.None; } set { } }
        public int StepSize { get { return step_; } set { step_ = value < 1 ? 1 : value; } }

        public int Minimum
        {
            get { return min_; }
            set { min_ = value; if (max_ < min_) max_ = min_; if (val_ < min_) Value = min_; else Sync(); Invalidate(); }
        }
        public int Maximum
        {
            get { return max_; }
            set { max_ = value; if (min_ > max_) min_ = max_; if (val_ > max_) Value = max_; else Invalidate(); }
        }
        public int Value
        {
            get { return val_; }
            set
            {
                int v = value;
                if (v < min_) v = min_;
                if (v > max_) v = max_;
                bool changed = (v != val_);
                val_ = v;
                Sync();
                Invalidate();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void ApplyPaper()
        {
            try
            {
                base.BackColor = Color.FromArgb(243, 236, 220);
                base.ForeColor = UiKit.Ink;
                if (edit != null)
                {
                    edit.BackColor = Color.FromArgb(243, 236, 220);
                    edit.ForeColor = Color.FromArgb(18, 16, 14);
                    edit.Font = new Font("Consolas", 13F, FontStyle.Bold);
                    edit.Enabled = true;
                    edit.ReadOnly = false;
                    edit.Visible = false;
                    PlaceEdit();
                }
                Invalidate();
            }
            catch (Exception dgap_1) { Diag.Swallow("NumSpin.cs:ApplyPaper", dgap_1); }
        }

        void Sync() { try { if (edit != null && edit.Text != val_.ToString()) edit.Text = val_.ToString(); } catch (Exception dg103_1) { Diag.Swallow("NumSpin.cs:103", dg103_1); } }
        void Commit()
        {
            int v;
            if (int.TryParse((edit.Text ?? "").Replace(" ", "").Trim(), out v)) Value = v;
            else Sync();
        }

        void PlaceEdit()
        {
            try
            {
                if (edit == null) return;
                int eh = edit.PreferredHeight;
                edit.SetBounds(10, Math.Max(1, (Height - eh) / 2 - 1), Math.Max(12, Width - 10 - 18), eh);
            }
            catch (Exception dg119_1) { Diag.Swallow("NumSpin.cs:119", dg119_1); }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); PlaceEdit(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            try { if (edit != null) edit.Enabled = Enabled; } catch (Exception dg126_1) { Diag.Swallow("NumSpin.cs:126", dg126_1); }
            Invalidate();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (edit != null) edit.Focus(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool u = Enabled && e.X >= Width - 19 && e.Y < Height / 2;
            bool d = Enabled && e.X >= Width - 19 && e.Y >= Height / 2;
            if (u != hotUp || d != hotDn) { hotUp = u; hotDn = d; Invalidate(); }
            Cursor = (u || d) ? Cursors.Hand : Cursors.IBeam;
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hotUp || hotDn) { hotUp = false; hotDn = false; Invalidate(); }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            if (e.X >= Width - 19)
            {
                if (e.Y < Height / 2) Value = Value + step_; else Value = Value - step_;
            }
            else if (UiKit.Paper && edit != null)
            {
                edit.Visible = true;
                Invalidate();
            }
            if (edit != null) edit.Focus();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled) return;
            if (e.Delta > 0) Value = Value + step_; else if (e.Delta < 0) Value = Value - step_;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rc.Width < 8 || rc.Height < 8) return;
            Color c = UiKit.Paper ? Color.FromArgb(243, 236, 220) : (Enabled ? base.BackColor : UiKit.Darken(base.BackColor, 8));
            // 内部编辑框是一块直角矩形，外框必须有圆角——两边颜色不一致会露出生硬的接缝。
            // 这里让外框整块直接填编辑框的底色（连 PrepInput 的焦点混色也跟着走），
            // 右侧 18px 再单独压一条更深的箭头列，接缝就变成了有意的分区。
            Color inner = c;   // v8.9：本体自绘，用设计色；edit.BackColor 已经是反补过的值
            using (GraphicsPath path = UiKit.RoundedPath(rc, 8))
            {
                using (SolidBrush bgr = new SolidBrush(inner))
                    g.FillPath(bgr, path);
                // 右侧箭头列：裁在圆角路径里画深色条，右边两角自动是圆的
                Region oldClip = g.Clip;
                g.SetClip(path);
                Color barCol = Enabled ? (UiKit.Paper ? Color.FromArgb(238, 230, 213) : UiKit.Darken(inner, 10)) : UiKit.Darken(inner, 4);
                using (SolidBrush bb = new SolidBrush(barCol))
                    g.FillRectangle(bb, new Rectangle(Width - 19, 0, 20, Height));
                using (Pen sp = new Pen(UiKit.Paper ? Color.FromArgb(186, 172, 146) : UiKit.A(42, 52, 66), 1f))
                    g.DrawLine(sp, Width - 19, 3, Width - 19, Height - 4);
                g.Clip = oldClip;
                if (oldClip != null) oldClip.Dispose();
                bool focus = (edit != null && edit.Focused);
                Color bc = UiKit.Paper
                    ? (focus ? Color.FromArgb(158, 120, 44) : Color.FromArgb(186, 172, 146))
                    : (!Enabled ? UiKit.A(58, 66, 78) : (focus ? Ui2.ACCENT : UiKit.A(94, 106, 124)));
                using (Pen bp = new Pen(bc, focus ? 1.6f : 1f))
                {
                    // 圆角路径只有 Rectangle 版本，聚焦时用内缩 1px 的矩形假装“更粗的边”
                    Rectangle ir = focus ? new Rectangle(1, 1, rc.Width - 2, rc.Height - 2) : rc;
                    using (GraphicsPath ip = UiKit.RoundedPath(ir, 8))
                        g.DrawPath(bp, ip);
                }
            }
            // 右侧两枚箭头：上三角 / 下三角，hover 提亮
            int bx = Width - 15, cw = 8;
            int upY = Height / 2 - 8, dnY = Height / 2 + 2;
            Color ac = UiKit.Paper
                ? (Enabled ? (hotUp ? UiKit.Ink : Color.FromArgb(112, 98, 74)) : Color.FromArgb(170, 158, 136))
                : (!Enabled ? UiKit.A(84, 94, 110) : Color.FromArgb(hotUp ? 235 : 150, hotUp ? 245 : 170, hotUp ? 255 : 196));
            using (SolidBrush b = new SolidBrush(ac))
            {
                g.FillPolygon(b, new PointF[] { new PointF(bx, upY + 5), new PointF(bx + cw, upY + 5), new PointF(bx + cw / 2f, upY) });
            }
            Color dc = UiKit.Paper
                ? (Enabled ? (hotDn ? UiKit.Ink : Color.FromArgb(112, 98, 74)) : Color.FromArgb(170, 158, 136))
                : (!Enabled ? UiKit.A(84, 94, 110) : Color.FromArgb(hotDn ? 235 : 150, hotDn ? 245 : 170, hotDn ? 255 : 196));
            using (SolidBrush b = new SolidBrush(dc))
            {
                g.FillPolygon(b, new PointF[] { new PointF(bx, dnY), new PointF(bx + cw, dnY), new PointF(bx + cw / 2f, dnY + 5) });
            }
            if (UiKit.Paper && (edit == null || !edit.Visible))
            {
                string vt = (edit != null && edit.Text != null && edit.Text.Length > 0) ? edit.Text : val_.ToString();
                StringFormat vsf = new StringFormat(StringFormatFlags.NoWrap);
                vsf.LineAlignment = StringAlignment.Center;
                vsf.Alignment = StringAlignment.Near;
                vsf.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush vb = new SolidBrush(Color.FromArgb(18, 16, 14)))
                    g.DrawString(vt, edit != null ? edit.Font : Font, vb, new RectangleF(7, 0, Width - 24, Height), vsf);
            }
        }
    }
}