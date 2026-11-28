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
    class DarkScrollBar : NativeWindow
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct SCROLLINFO { public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos; }
        [StructLayout(LayoutKind.Sequential)]
        struct SCROLLBARINFO
        {
            public int cbSize; public RECT rcBar; public IntPtr hwndBar; public int info;
            public int st0, st1, st2, st3, st4, st5;
        }
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
        [DllImport("user32.dll")] static extern bool GetScrollBarInfo(IntPtr h, int idObject, ref SCROLLBARINFO sbi);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr rc, IntPtr rgn, int flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int idx);
        [DllImport("user32.dll")] static extern int ShowScrollBar(IntPtr h, int bar, bool show);

        // ListView 的横向滚动条纯粹是"列宽溢出"的噪音，直接不显示；
        // 竖向滚动条保留（改用自绘槽），这样列表边缘只剩一条深色条。
        static readonly System.Collections.Generic.HashSet<IntPtr> noH = new System.Collections.Generic.HashSet<IntPtr>();

        public static void HideHorz(Control c)
        {
            if (c == null) return;
            if (c.IsHandleCreated) { noH.Add(c.Handle); try { ShowScrollBar(c.Handle, 0, false); } catch (Exception dg51_1) { Diag.Swallow("DarkScrollBar.cs:51", dg51_1); } }
            c.SizeChanged += delegate { try { ShowScrollBar(c.Handle, 0, false); } catch (Exception dg52_1) { Diag.Swallow("DarkScrollBar.cs:52", dg52_1); } };
            c.HandleCreated += delegate { noH.Add(c.Handle); try { ShowScrollBar(c.Handle, 0, false); } catch (Exception dg53_1) { Diag.Swallow("DarkScrollBar.cs:53", dg53_1); } };
        }

        const int WM_NCPAINT = 0x0085, WM_NCCALCSIZE = 0x0083, WM_VSCROLL = 0x0115, WM_HSCROLL = 0x0114,
            WM_MOUSEWHEEL = 0x020A, WM_SIZE = 0x0005, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2,
            WM_NCMOUSEMOVE = 0x00A0;
        const int SB_VERT = 1, SB_HORZ = 0, SIF_ALL = 0x17, OBJID_VSCROLL = -15, OBJID_HSCROLL = -16;
        const int ST_INVISIBLE = 0x00008000, ST_UNAVAILABLE = 0x00000001;
        const int RDW_FRAME = 0x0400, RDW_INVALIDATE = 0x0001;

        static readonly Dictionary<IntPtr, DarkScrollBar> live = new Dictionary<IntPtr, DarkScrollBar>();

        /// <summary>给任意带系统滚动条的控件套深色皮肤（幂等，重复调用无副作用）。</summary>
        public static void Attach(Control c)
        {
            try
            {
                if (c == null) return;
                if (!c.IsHandleCreated) { c.HandleCreated += delegate { Attach(c); }; return; }
                IntPtr h = c.Handle;
                if (live.ContainsKey(h)) return;
                DarkScrollBar sb = new DarkScrollBar();
                sb.AssignHandle(h);
                live[h] = sb;
                try { File.AppendAllText(Path.Combine(Application.StartupPath, "_scroll_debug.txt"), "attach " + c.GetType().Name + " " + h.ToString("X") + "\r\n"); } catch (Exception dg77_1) { Diag.Swallow("DarkScrollBar.cs:77", dg77_1); }
            }
            catch (Exception dg79_1) { Diag.Swallow("DarkScrollBar.cs:79", dg79_1); }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            try
            {
                int msg = m.Msg;
                if (msg == WM_NCPAINT || msg == WM_NCCALCSIZE) { PaintBars(); return; }
                if (msg == WM_VSCROLL || msg == WM_HSCROLL || msg == WM_MOUSEWHEEL || msg == WM_SIZE
                    || msg == WM_NCLBUTTONDOWN || msg == WM_NCLBUTTONUP || msg == WM_NCMOUSEMOVE)
                {
                    try { RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_FRAME | RDW_INVALIDATE); } catch (Exception dg92_1) { Diag.Swallow("DarkScrollBar.cs:92", dg92_1); }
                    PaintBars();
                    return;
                }
            }
            catch (Exception dg97_1) { Diag.Swallow("DarkScrollBar.cs:97", dg97_1); }
        }

        void PaintBars()
        {
            RECT wr;
            if (!GetWindowRect(Handle, out wr)) return;
            IntPtr dc = GetWindowDC(Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                using (Graphics g = Graphics.FromHdc(dc))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    PaintOne(g, wr, true);
                    if (!noH.Contains(Handle)) PaintOne(g, wr, false);
                }
            }
            catch (Exception dg115_1) { Diag.Swallow("DarkScrollBar.cs:115", dg115_1); }
            finally { try { ReleaseDC(Handle, dc); } catch (Exception dg116_1) { Diag.Swallow("DarkScrollBar.cs:116", dg116_1); } }
        }

        void PaintOne(Graphics g, RECT wr, bool vertical)
        {
            int ww = wr.right - wr.left, wh = wr.bottom - wr.top;
            SCROLLBARINFO sbi = new SCROLLBARINFO();
            sbi.cbSize = Marshal.SizeOf(typeof(SCROLLBARINFO));
            bool got = GetScrollBarInfo(Handle, vertical ? OBJID_VSCROLL : OBJID_HSCROLL, ref sbi);
            Rectangle rb = Rectangle.Empty;
            Rectangle gutter = Rectangle.Empty;
            if (got && (sbi.st0 & ST_INVISIBLE) == 0 && sbi.rcBar.right > sbi.rcBar.left && sbi.rcBar.bottom > sbi.rcBar.top)
                rb = new Rectangle(sbi.rcBar.left - wr.left, sbi.rcBar.top - wr.top,
                    sbi.rcBar.right - sbi.rcBar.left, sbi.rcBar.bottom - sbi.rcBar.top);
            else
            {
                int sz = GetSystemMetrics(2);
                if (sz < 12 || sz > 32) sz = 17;
                if (vertical) { rb = new Rectangle(ww - sz, 0, sz, wh); gutter = new Rectangle(ww - sz - 2, 0, 2, wh); }
                else rb = new Rectangle(0, wh - sz, ww, sz);
            }
            if (rb.Width <= 2 || rb.Height <= 2) return;
            bool dis0 = got && (sbi.st0 & ST_UNAVAILABLE) != 0;

            bool dis = dis0;
            Color track = UiKit.A(22, 27, 34);
            using (SolidBrush b = new SolidBrush(track)) { if (gutter.Width > 0) g.FillRectangle(b, gutter); g.FillRectangle(b, rb); }
            using (Pen p = new Pen(UiKit.A(44, 52, 66), 1f))
            {
                if (vertical) g.DrawLine(p, rb.Left, rb.Top, rb.Left, rb.Bottom - 1);
                else g.DrawLine(p, rb.Left, rb.Top, rb.Right - 1, rb.Top);
            }

            SCROLLINFO si = new SCROLLINFO();
            si.cbSize = Marshal.SizeOf(typeof(SCROLLINFO));
            si.fMask = SIF_ALL;
            try { GetScrollInfo(Handle, vertical ? SB_VERT : SB_HORZ, ref si); } catch (Exception dg152_1) { Diag.Swallow("DarkScrollBar.cs:152", dg152_1); }

            int span = si.nMax - si.nMin + 1;
            if (span < 1) span = 1;
            int page = si.nPage > 0 ? si.nPage : span;
            if (page > span) page = span;
            int arrow = 16;
            int trackLen = vertical ? rb.Height - arrow * 2 : rb.Width - arrow * 2;
            if (trackLen < 10) { arrow = 0; trackLen = vertical ? rb.Height : rb.Width; }
            if (trackLen < 4) return;

            int thumbLen = (int)Math.Round((double)trackLen * page / span);
            if (thumbLen < 22) thumbLen = 22;
            if (thumbLen > trackLen) thumbLen = trackLen;
            int maxPos = span - page;
            if (maxPos < 1) maxPos = 1;
            int pos = si.nPos - si.nMin;
            if (pos < 0) pos = 0;
            if (pos > maxPos) pos = maxPos;
            int travel = trackLen - thumbLen;
            int off = (int)Math.Round((double)travel * pos / maxPos);

            Color thumbTop = dis ? UiKit.A(48, 56, 68) : UiKit.A(80, 94, 114);
            Color thumbBot = dis ? UiKit.A(38, 44, 56) : UiKit.A(58, 68, 84);
            Color edge = dis ? UiKit.A(60, 68, 82) : UiKit.A(104, 120, 142);

            Rectangle th = vertical
                ? new Rectangle(rb.Left + 1, rb.Top + arrow + off, rb.Width - 2, thumbLen)
                : new Rectangle(rb.Left + arrow + off, rb.Top + 1, thumbLen, rb.Height - 2);
            if (th.Width > 3 && th.Height > 3)
            {
                using (GraphicsPath path = UiKit.RoundedPath(th, Math.Min(5, Math.Min(th.Width, th.Height) / 2)))
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(th, thumbTop, thumbBot, vertical ? 0f : 90f))
                        g.FillPath(br, path);
                    using (Pen bp = new Pen(edge, 1f)) g.DrawPath(bp, path);
                }
            }

            // 两枚箭头（只画外观，点击仍落在系统箭头上）
            if (arrow > 0)
            {
                Color ac = dis ? UiKit.A(70, 78, 92) : UiKit.A(150, 164, 184);
                using (SolidBrush b = new SolidBrush(ac))
                {
                    if (vertical)
                    {
                        float cx = rb.Left + rb.Width / 2f;
                        g.FillPolygon(b, new PointF[] {
                            new PointF(cx - 4, rb.Top + 10), new PointF(cx + 4, rb.Top + 10), new PointF(cx, rb.Top + 5) });
                        g.FillPolygon(b, new PointF[] {
                            new PointF(cx - 4, rb.Bottom - 10), new PointF(cx + 4, rb.Bottom - 10), new PointF(cx, rb.Bottom - 5) });
                    }
                    else
                    {
                        float cy = rb.Top + rb.Height / 2f;
                        g.FillPolygon(b, new PointF[] {
                            new PointF(rb.Left + 10, cy - 4), new PointF(rb.Left + 10, cy + 4), new PointF(rb.Left + 5, cy) });
                        g.FillPolygon(b, new PointF[] {
                            new PointF(rb.Right - 10, cy - 4), new PointF(rb.Right - 10, cy + 4), new PointF(rb.Right - 5, cy) });
                    }
                }
            }
        }
    }
}