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
    class PalList : Panel
    {
        public List<PaletteItem> items = new List<PaletteItem>();
        public int sel = -1;
        public int top = 0;
        public bool en = false;
        public string query = "";
        public Action RunRequested;
        const int RowH = 36;
        const int Gutter = 9;

        public PalList()
        {
            this.BackColor = UiKit.A(26, 31, 39);
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public int VisibleCount { get { int n = (Height - 2) / RowH; return n < 1 ? 1 : n; } }
        public int SelectedIndex { get { return sel; } }

        public void SetItems(List<PaletteItem> v, string q)
        {
            items = v;
            query = q == null ? "" : q;
            sel = v.Count > 0 ? 0 : -1;
            top = 0;
            Invalidate();
        }

        public void Step(int d)
        {
            if (items.Count == 0) { sel = -1; Invalidate(); return; }
            int n = sel + d;
            if (n < 0) n = 0;
            if (n > items.Count - 1) n = items.Count - 1;
            sel = n;
            EnsureVisible();
            Invalidate();
        }

        void EnsureVisible()
        {
            int vc = VisibleCount;
            if (sel < top) top = sel;
            if (sel >= top + vc) top = sel - vc + 1;
            int maxTop = Math.Max(0, items.Count - vc);
            if (top < 0) top = 0;
            if (top > maxTop) top = maxTop;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            top += (e.Delta > 0 ? -3 : 3);
            int maxTop = Math.Max(0, items.Count - VisibleCount);
            if (top < 0) top = 0;
            if (top > maxTop) top = maxTop;
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int idx = top + e.Y / RowH;
            if (idx >= 0 && idx < items.Count) { sel = idx; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int idx = top + e.Y / RowH;
            if (idx >= 0 && idx < items.Count)
            {
                sel = idx;
                if (RunRequested != null) RunRequested();
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            if (items.Count == 0)
            {
                // 空态：与其只在小字里说“没匹配”，不如把整块空区域变成一句能用的提示
                string t1 = en ? "Nothing matches" : "没有匹配的功能";
                string t2 = en ? "try:  screenshot  ·  clean C  ·  shutdown 30" : "试试「截图」「清理 C 盘」「关机 30」";
                using (Font f1 = new Font("Microsoft YaHei UI", 13F))
                using (Font f2 = new Font("Microsoft YaHei UI", 9F))
                using (SolidBrush b1 = new SolidBrush(UiKit.A(150, 168, 190)))
                using (SolidBrush b2 = new SolidBrush(UiKit.A(115, 130, 150)))
                {
                    SizeF s1 = g.MeasureString(t1, f1);
                    SizeF s2 = g.MeasureString(t2, f2);
                    float cx = (Width - s1.Width) / 2f;
                    if (cx < 8) cx = 8;
                    g.DrawString(t1, f1, b1, cx, (Height / 2f) - s1.Height - 6);
                    float cx2 = (Width - s2.Width) / 2f;
                    if (cx2 < 8) cx2 = 8;
                    g.DrawString(t2, f2, b2, cx2, (Height / 2f) + 6);
                }
                return;
            }

            int last = Math.Min(items.Count, top + VisibleCount);
            for (int i = top; i < last; i++)
            {
                PaletteItem it = items[i];
                int y = (i - top) * RowH;
                bool s2 = (i == sel);
                Color bg2 = UiKit.A(26, 31, 39);
                if (s2) bg2 = UiKit.A(52, 66, 86);
                else if (it.Intent) bg2 = UiKit.A(32, 44, 60);
                else if (it.Recent) bg2 = UiKit.A(29, 35, 44);
                using (SolidBrush bb = new SolidBrush(bg2)) g.FillRectangle(bb, 0, y, Width, RowH);

                int tx = 10;
                if (it.Intent)
                {
                    using (SolidBrush ab = new SolidBrush(UiKit.A(64, 132, 255)))
                        g.FillRectangle(ab, 4, y + 8, 3, RowH - 16);
                    tx = 14;
                }
                string title = en ? it.En : it.Zh;
                Color fg = s2 ? Color.White : (it.Intent ? UiKit.A(150, 200, 255) : UiKit.A(210, 220, 234));
                int hi = -1;
                if (query.Length > 0 && !it.Intent) hi = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                int drawY = y + 9;
                if (hi >= 0)
                {
                    int hl = Math.Min(query.Length, title.Length - hi);
                    string pre = title.Substring(0, hi);
                    string mid = title.Substring(hi, hl);
                    string suf = title.Substring(hi + hl);
                    TextFormatFlags mf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                    int wPre = pre.Length == 0 ? 0 : TextRenderer.MeasureText(pre, Font, new Size(int.MaxValue, int.MaxValue), mf).Width;
                    int wMid = TextRenderer.MeasureText(mid, Font, new Size(int.MaxValue, int.MaxValue), mf).Width;
                    if (pre.Length > 0)
                        using (SolidBrush b0 = new SolidBrush(fg)) g.DrawString(pre, Font, b0, tx, drawY);
                    using (SolidBrush b2 = new SolidBrush(UiKit.A(96, 172, 255)))
                        g.DrawString(mid, Font, b2, tx + wPre, drawY);
                    if (suf.Length > 0)
                        using (SolidBrush b3 = new SolidBrush(fg)) g.DrawString(suf, Font, b3, tx + wPre + wMid, drawY);
                }
                else
                {
                    using (SolidBrush fb = new SolidBrush(fg)) g.DrawString(title, Font, fb, tx, drawY);
                }

                if (it.Intent)
                {
                    using (Font ft = new Font("Microsoft YaHei UI", 7.5F))
                    using (SolidBrush tb = new SolidBrush(UiKit.A(96, 172, 255)))
                    {
                        string tag = en ? "INTENT" : "意图";
                        SizeF tsz = g.MeasureString(tag, ft);
                        Rectangle tr = new Rectangle(Width - Gutter - (int)tsz.Width - 16, y + 9, (int)tsz.Width + 8, 17);
                        using (SolidBrush tbg = new SolidBrush(UiKit.A(38, 60, 92))) g.FillRectangle(tbg, tr);
                        g.DrawString(tag, ft, tb, tr.X + 4, tr.Y + 2);
                    }
                }
                int rx = Width - Gutter - 8;
                if (it.Keys != null && it.Keys.Length > 0)
                {
                    using (Font fk = new Font("Microsoft YaHei UI", 8.5F))
                    using (SolidBrush kb = new SolidBrush(UiKit.A(130, 145, 165)))
                    {
                        SizeF sz = g.MeasureString(it.Keys, fk);
                        rx -= (int)sz.Width + 8;
                        g.DrawString(it.Keys, fk, kb, rx, y + 10);
                    }
                }
                // v13.0：类型小标（页 / 开），一眼分清「跳过去」和「就地执行」
                if (it.Kind == "page" || it.Kind == "open")
                {
                    using (Font ft = new Font("Microsoft YaHei UI", 8F))
                    using (SolidBrush tb = new SolidBrush(it.Kind == "page" ? UiKit.A(150, 200, 255) : UiKit.A(140, 200, 170)))
                    {
                        string tag = it.Kind == "page" ? (en ? "PAGE" : "页") : (en ? "OPEN" : "开");
                        SizeF tsz = g.MeasureString(tag, ft);
                        int tw = (int)tsz.Width + 10;
                        Rectangle tr = new Rectangle(rx - tw - 4, y + 8, tw, 18);
                        using (SolidBrush tbg = new SolidBrush(UiKit.A(34, 46, 62))) g.FillRectangle(tbg, tr);
                        g.DrawString(tag, ft, tb, tr.X + 5, tr.Y + 3);
                    }
                }
                using (Pen lp = new Pen(UiKit.A(22, 26, 33)))
                    g.DrawLine(lp, 12, y + RowH - 1, Width - Gutter - 4, y + RowH - 1);
            }

            // 自己画的滚动拇指：没有内容溢出就不画；有溢出时先铺一条更深的槽，再把 4px 强调色拇指放进去，
            // 这样它读起来像“轨道里的滑块”，而不是一条贴边的亮线
            if (items.Count > VisibleCount)
            {
                int gutW = 12;
                using (SolidBrush gb = new SolidBrush(UiKit.A(22, 26, 33)))
                    g.FillRectangle(gb, Width - gutW, 0, gutW, Height);
                int trackH = Height - 8;
                int thumbH = (int)(trackH * (double)VisibleCount / items.Count);
                if (thumbH < 30) thumbH = 30;
                int span = Math.Max(1, items.Count - VisibleCount);
                int y0 = 4 + (int)((trackH - thumbH) * (top / (double)span));
                using (GraphicsPath rp = UiKit.RoundedPath(new Rectangle(Width - 8, y0, 4, thumbH), 2))
                using (SolidBrush tbb = new SolidBrush(UiKit.A(96, 172, 255)))
                    g.FillPath(tbb, rp);
            }
        }
    }
}
