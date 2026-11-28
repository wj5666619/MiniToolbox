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
    class RoundCheck : CheckBox
    {
        bool hot;
        public RoundCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ForeColor = UiKit.A(226, 232, 240);
            Cursor = Cursors.Hand;
        }
        Color Bg()
        {
            try
            {
                if (BackColor != Color.Transparent) return BackColor;
                if (Parent != null && Parent.BackColor != Color.Transparent) return Parent.BackColor;
            }
            catch (Exception dg40_1) { Diag.Swallow("RoundCheck.cs:40", dg40_1); }
            return UiKit.A(30, 36, 46);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!UiKit.PaintParentBack(this, g))
            {
                using (SolidBrush bgr = new SolidBrush(Bg())) g.FillRectangle(bgr, ClientRectangle);
            }
            int bs = 15;
            Rectangle box = new Rectangle(0, (Height - bs) / 2, bs, bs);
            Color bd = UiKit.Paper
                ? (Checked ? Color.FromArgb(150, 116, 40) : Color.FromArgb(168, 154, 130))
                : (!Enabled ? UiKit.A(66, 74, 88) : (hot ? UiKit.A(152, 170, 192) : UiKit.A(108, 122, 142)));
            using (GraphicsPath path = UiKit.RoundedPath(box, 4))
            {
                if (Checked)
                {
                    if (UiKit.Paper)
                    {
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(205, 168, 82)))
                            g.FillPath(pb, path);
                        using (Pen pen = new Pen(UiKit.Ink, 1.7f))
                            g.DrawLines(pen, new Point[] {
                                new Point(box.X + 3, box.Y + 8), new Point(box.X + 6, box.Y + 11), new Point(box.X + 12, box.Y + 4) });
                    }
                    else
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(box, UiKit.A(78, 148, 255), UiKit.A(44, 100, 214), 90f))
                            g.FillPath(br, path);
                        using (Pen pen = new Pen(UiKit.A(240, 246, 255), 1.7f))
                            g.DrawLines(pen, new Point[] {
                                new Point(box.X + 3, box.Y + 8), new Point(box.X + 6, box.Y + 11), new Point(box.X + 12, box.Y + 4) });
                    }
                }
                else
                {
                    // 未选中：半透明深底，让卡片渐变透出来，边界靠描边交代；纸感用纸白实底
                    using (SolidBrush sb = new SolidBrush(UiKit.Paper ? Color.FromArgb(255, 252, 243) : UiKit.AA(160, 24, 30, 40)))
                        g.FillPath(sb, path);
                }
                using (Pen bp = new Pen(bd, 1f)) g.DrawPath(bp, path);
            }
            Control pp = Parent;
            int pguard = 0;
            while (pp != null && !(pp is PagePanel) && pguard++ < 10) pp = pp.Parent;
            bool onPage = pp is PagePanel;
            Color tc = UiKit.Paper
                ? (onPage ? (Enabled ? Color.FromArgb(62, 56, 45) : Color.FromArgb(128, 116, 98))
                    : Color.FromArgb(210, 200, 180))
                : (Enabled ? ForeColor : UiKit.A(108, 118, 134));
            Rectangle tr = new Rectangle(bs + 7, 0, Math.Max(4, Width - bs - 7), Height);
            if (UiKit.Paper)
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Near;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    using (SolidBrush tb = new SolidBrush(tc))
                        g.DrawString(Text, Font, tb, tr, sf);
                }
            }
            else
            TextRenderer.DrawText(g, Text, Font, tr, tc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}