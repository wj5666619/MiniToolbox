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
    class BgPanel : Panel
    {
        public Image Bg;
        public bool ShowBg;
        public Color BaseColor = UiKit.A(14, 18, 24);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush gb = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle,
                    UiKit.A(25, 32, 45), UiKit.A(9, 12, 17), 42f))
                    e.Graphics.FillRectangle(gb, ClientRectangle);
            }
            if (ShowBg && Bg != null)
            {
                e.Graphics.Clear(BaseColor);
                int cw = ClientSize.Width, ch = ClientSize.Height;
                if (cw > 0 && ch > 0)
                {
                    float s = Math.Max((float)cw / Bg.Width, (float)ch / Bg.Height);
                    int w = (int)(Bg.Width * s), h = (int)(Bg.Height * s);
                    e.Graphics.DrawImage(Bg, (cw - w) / 2, (ch - h) / 2, w, h);
                    using (Brush b = new SolidBrush(UiKit.AA(205, 12, 16, 22)))
                        e.Graphics.FillRectangle(b, 0, 0, cw, ch);
                }
            }
            else { }
        }
    }
}
