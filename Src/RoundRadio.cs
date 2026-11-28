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
    class RoundRadio : RadioButton
    {
        bool hot;
        public RoundRadio()
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
            catch (Exception dg40_1) { Diag.Swallow("RoundRadio.cs:40", dg40_1); }
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
            int ds = 15;
            Rectangle dot = new Rectangle(0, (Height - ds) / 2, ds, ds);
            Color bd = UiKit.Paper
                ? (Checked ? Color.FromArgb(150, 116, 40) : Color.FromArgb(168, 154, 130))
                : (!Enabled ? UiKit.A(66, 74, 88) : (hot ? UiKit.A(152, 170, 192) : UiKit.A(108, 122, 142)));
            using (SolidBrush bg2 = new SolidBrush(UiKit.Paper ? Color.FromArgb(255, 252, 243) : UiKit.AA(160, 24, 30, 40)))
                g.FillEllipse(bg2, dot);
            if (Checked)
            {
                if (UiKit.Paper)
                {
                    using (SolidBrush pb = new SolidBrush(Color.FromArgb(205, 168, 82)))
                        g.FillEllipse(pb, dot);
                    using (SolidBrush ib = new SolidBrush(UiKit.Ink))
                        g.FillEllipse(ib, new Rectangle(dot.X + 5, dot.Y + 5, ds - 10, ds - 10));
                }
                else
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(dot, UiKit.A(86, 154, 255), UiKit.A(46, 102, 216), 90f))
                        g.FillEllipse(br, dot);
                    using (SolidBrush wb = new SolidBrush(UiKit.A(238, 245, 255)))
                        g.FillEllipse(wb, new Rectangle(dot.X + 4, dot.Y + 4, ds - 8, ds - 8));
                }
            }
            using (Pen bp = new Pen(bd, 1f)) g.DrawEllipse(bp, dot);
            Control pp = Parent;
            int pguard = 0;
            while (pp != null && !(pp is PagePanel) && pguard++ < 10) pp = pp.Parent;
            bool onPage = pp is PagePanel;
            Color tc = UiKit.Paper
                ? (onPage ? (Enabled ? Color.FromArgb(62, 56, 45) : Color.FromArgb(128, 116, 98))
                    : Color.FromArgb(210, 200, 180))
                : (Enabled ? ForeColor : UiKit.A(108, 118, 134));
            Rectangle tr = new Rectangle(ds + 6, 0, Math.Max(4, Width - ds - 6), Height);
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