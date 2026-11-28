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
    static class UiType
    {
        static bool en = false;
        public static void SetLanguage(bool english) { en = english; }

        // 字号档位（pt）。所有界面文字吸到这几档，避免 8.2 / 8.8 / 9.3 这种肉眼分不出的碎档位
        public static readonly float[] Steps = new float[] { 8F, 8.5F, 9F, 9.5F, 10F, 10.5F, 11F, 12F, 14F, 15F, 16F, 18F, 20F, 22F };

        public static float Snap(float size)
        {
            if (size <= 0F) return size;
            float best = Steps[0];
            float dmin = float.MaxValue;
            for (int i = 0; i < Steps.Length; i++)
            {
                float d = Math.Abs(Steps[i] - size);
                if (d < dmin) { dmin = d; best = Steps[i]; }
            }
            return dmin <= 0.45F ? best : size;
        }

        // 中文用「微软雅黑 UI」，英文界面用 Segoe UI——英文在 Segoe UI 下的字面更修长，行距也更匀
        public static string Family()
        {
            if (!en) return Ui2.FamilyName;
            try
            {
                using (Font f = new Font("Segoe UI", 9F))
                    if (string.Equals(f.Name, "Segoe UI", StringComparison.OrdinalIgnoreCase)) return "Segoe UI";
            }
            catch (Exception dg53_1) { Diag.Swallow("UiType.cs:53", dg53_1); }
            return Ui2.FamilyName;
        }

        public static void Polish(Form f)
        {
            if (f == null || f.IsDisposed) return;
            try { Fonts(f, 0); } catch (Exception dg60_1) { Diag.Swallow("UiType.cs:60", dg60_1); }
            try { Buttons(f); } catch (Exception dg61_1) { Diag.Swallow("UiType.cs:61", dg61_1); }
            try { Rows(f); } catch (Exception dg62_1) { Diag.Swallow("UiType.cs:62", dg62_1); }
        }

        // ---------- 1. 字号吸附 + 字族统一 ----------
        static void Fonts(Control parent, int depth)
        {
            if (parent == null || depth > 12) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                try
                {
                    Font f = c.Font;
                    if (f != null && f.Size > 4F && !Ui2.IsIconFont(f))
                    {
                        float sz = Snap(f.Size);
                        string fam = Family();
                        bool famOk = f.Name.Equals(fam, StringComparison.OrdinalIgnoreCase);
                        if (Math.Abs(sz - f.Size) > 0.01F || !famOk)
                        {
                            Font nf = new Font(fam, sz, f.Style);
                            if (!string.Equals(nf.Name, f.Name, StringComparison.OrdinalIgnoreCase) || Math.Abs(nf.Size - f.Size) > 0.01F)
                                c.Font = nf;
                        }
                    }
                }
                catch (Exception dg88_1) { Diag.Swallow("UiType.cs:88", dg88_1); }
                if (c.Controls.Count > 0) Fonts(c, depth + 1);
            }
        }

        // ---------- 2. 按钮按文字自适应宽度 ----------
        // 规则：只做"撑宽"，绝不缩窄（缩窄会把原本排好的版挤乱）；
        // 撑宽只撑到"父容器右边界 / 同一行右侧最近控件"为止，因此永远不会压到别人；
        // 实在没空间才把字号降到 8F，再不行才交给省略号（AutoEllipsis 早已常开）。
        static void Buttons(Control parent)
        {
            if (parent == null) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c is Button) FitOne((Button)c);
                if (c.Controls.Count > 0) Buttons(c);
            }
        }

        static void FitOne(Button b)
        {
            try
            {
                if (b == null || b.IsDisposed || b.Width < 16) return;
                if (b.Tag != null && b.Tag.ToString() == "cbcover") return;
                string txt = b.Text;
                if (txt == null || txt.Trim().Length == 0) return;
                if (Ui2.IsIconText(txt)) return;
                Font f = b.Font;
                if (f == null) return;

                Size need = TextRenderer.MeasureText(txt, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int pad = 26;                                  // 撑宽目标：左右各 13px 呼吸
                int minPad = 12;                               // 及格线：左右各 6px——比这更挤才叫裁字
                if (need.Width + minPad <= b.Width + 1) return; // 已经装得下

                Control p = b.Parent;
                if (p == null) return;
                int limit = p.ClientSize.Width - 6;            // 父容器右边界
                for (int i = 0; i < p.Controls.Count; i++)
                {
                    Control sib = p.Controls[i];
                    if (sib == b || !sib.Visible) continue;
                    if (sib.Top < b.Bottom - 2 && sib.Bottom > b.Top + 2 && sib.Left >= b.Left + b.Width - 1)
                        limit = Math.Min(limit, sib.Left - 8);
                }
                int room = limit - b.Left;
                if (room > b.Width)
                {
                    int target = Math.Min(need.Width + pad, room);
                    if (target > b.Width) b.Width = target;
                }
                else
                {
                    // 右边顶到边了：看看左边还有没有余地——向右不动，往左长
                    int llimit = 2;
                    for (int i = 0; i < p.Controls.Count; i++)
                    {
                        Control sib = p.Controls[i];
                        if (sib == b || !sib.Visible) continue;
                        if (sib.Top < b.Bottom - 2 && sib.Bottom > b.Top + 2 && sib.Right <= b.Left + 1)
                            llimit = Math.Max(llimit, sib.Right + 8);
                    }
                    int roomL = b.Left - llimit;
                    if (roomL > 0)
                    {
                        int grow = Math.Min(need.Width + pad - b.Width, roomL);
                        if (grow > 0)
                        {
                            Rectangle nr = new Rectangle(b.Left - grow, b.Top, b.Width + grow, b.Height);
                            bool clash = false;
                            for (int i = 0; i < p.Controls.Count; i++)
                            {
                                Control sib = p.Controls[i];
                                if (sib == b || !sib.Visible) continue;
                                Rectangle ix = Rectangle.Intersect(nr, sib.Bounds);
                                if (ix.Width > 2 && ix.Height > 2) { clash = true; break; }
                            }
                            if (!clash) { b.Left -= grow; b.Width += grow; }
                        }
                    }
                }
                if (need.Width + minPad <= b.Width + 1) return; // 撑开后够了

                // 空间不够：先把字号降到 8F 再量
                if (f.Size > 8.01F)
                {
                    Font small = new Font(f.Name, 8F, f.Style);
                    Size n2 = TextRenderer.MeasureText(txt, small, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    if (n2.Width + minPad <= b.Width + 1)
                    {
                        b.Font = small;
                        return;
                    }
                }
                try { b.AutoEllipsis = true; } catch (Exception dg186_1) { Diag.Swallow("UiType.cs:186", dg186_1); }
            }
            catch (Exception dg188_1) { Diag.Swallow("UiType.cs:188", dg188_1); }
        }

        // ---------- 3. 同一行按钮的顶部对齐 ----------
        // 只把 Top 相差 1~6px 的按钮拉到同一基线上，移动后还要做一次碰撞检查，碰到别的控件就放弃。
        static void Rows(Control parent)
        {
            if (parent == null) return;
            System.Collections.Generic.List<Button> bs = new System.Collections.Generic.List<Button>();
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c is Button && c.Visible && c.Height > 18)
                {
                    if (c.Tag != null && c.Tag.ToString() == "cbcover") continue;
                    bs.Add((Button)c);
                }
            }
            if (bs.Count >= 2)
            {
                bool changed = true;
                int guard = 0;
                while (changed && guard++ < 6)
                {
                    changed = false;
                    for (int i = 0; i < bs.Count; i++)
                    {
                        for (int j = i + 1; j < bs.Count; j++)
                        {
                            int dtop = bs[j].Top - bs[i].Top;
                            if (dtop == 0 || Math.Abs(dtop) > 6) continue;
                            // 同高才动（高度不同说明原本就分层，不去搅）
                            if (bs[i].Height != bs[j].Height) continue;
                            Button mover = dtop > 0 ? bs[j] : bs[i];
                            Button anchor = dtop > 0 ? bs[i] : bs[j];
                            if (!SafeMove(mover, anchor.Top)) continue;
                            changed = true;
                        }
                    }
                }
            }
            for (int i = 0; i < parent.Controls.Count; i++)
                if (parent.Controls[i].Controls.Count > 0) Rows(parent.Controls[i]);
        }

        static bool SafeMove(Button mover, int newTop)
        {
            try
            {
                if (mover.Top == newTop) return false;
                Rectangle nr = new Rectangle(mover.Left, newTop, mover.Width, mover.Height);
                Control p = mover.Parent;
                if (p == null) return false;
                if (nr.Bottom > p.ClientSize.Height - 2) return false;
                for (int i = 0; i < p.Controls.Count; i++)
                {
                    Control o = p.Controls[i];
                    if (o == mover || !o.Visible) continue;
                    Rectangle orc = o.Bounds;
                    Rectangle ix = Rectangle.Intersect(nr, orc);
                    if (ix.Width > 2 && ix.Height > 2) return false;
                }
                mover.Top = newTop;
                return true;
            }
            catch { return false; }
        }

        public static void Offenders(Control parent, StringBuilder sb, int depth)
        {
            if (parent == null || depth > 12) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                try
                {
                    if (c is Button && c.Width >= 16 && c.Text != null && c.Text.Trim().Length > 0 && !Ui2.IsIconText(c.Text))
                    {
                        Size need = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        if (need.Width + 12 > c.Width + 1)
                        {
                            Control p = c.Parent;
                            int limit = p == null ? c.Right : p.ClientSize.Width - 6;
                            if (p != null)
                                for (int k = 0; k < p.Controls.Count; k++)
                                {
                                    Control sib = p.Controls[k];
                                    if (sib == c || !sib.Visible) continue;
                                    if (sib.Top < c.Bottom - 2 && sib.Bottom > c.Top + 2 && sib.Left >= c.Left + c.Width - 1)
                                        limit = Math.Min(limit, sib.Left - 8);
                                }
                            if (need.Width + 12 > limit - c.Left)
                                sb.AppendLine("  [" + c.Text + "] w=" + c.Width + " need=" + (need.Width + 12)
                                    + " roomR=" + (limit - c.Left) + " font=" + c.Font.Size
                                    + " parent=" + (p == null ? "?" : p.GetType().Name) + " rect=" + c.Bounds);
                        }
                    }
                }
                catch (Exception dg287_1) { Diag.Swallow("UiType.cs:287", dg287_1); }
                if (c.Controls.Count > 0) Offenders(c, sb, depth + 1);
            }
        }

        // ---------- 4. 体检：把"还装不下字的按钮"和"档位外的字号"数出来 ----------
        public static void Scan(Control parent, int[] acc, int depth)
        {
            if (parent == null || depth > 12) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                try
                {
                    Font f = c.Font;
                    if (f != null && f.Size > 4F && !Ui2.IsIconFont(f))
                    {
                        if (Math.Abs(Snap(f.Size) - f.Size) > 0.45F) acc[2]++;
                        if (f.Name.IndexOf("Microsoft YaHei", StringComparison.OrdinalIgnoreCase) < 0
                            && f.Name.IndexOf("Segoe UI", StringComparison.OrdinalIgnoreCase) < 0) acc[3]++;
                    }
                    if (c is Button && c.Width >= 16 && c.Text != null && c.Text.Trim().Length > 0 && !Ui2.IsIconText(c.Text))
                    {
                        Size need = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        acc[0]++;
                        if (need.Width + 12 > c.Width + 1)
                        {
                            acc[1]++;
                            // 与"父容器右边界/同级控件"的真实可用空间比：真的没空间才算一处
                            Control p = c.Parent;
                            int limit = p == null ? c.Right : p.ClientSize.Width - 6;
                            if (p != null)
                                for (int k = 0; k < p.Controls.Count; k++)
                                {
                                    Control sib = p.Controls[k];
                                    if (sib == c || !sib.Visible) continue;
                                    if (sib.Top < c.Bottom - 2 && sib.Bottom > c.Top + 2 && sib.Left >= c.Left + c.Width - 1)
                                        limit = Math.Min(limit, sib.Left - 8);
                                }
                            if (need.Width + 12 > limit - c.Left) acc[4]++;
                        }
                    }
                }
                catch (Exception dg331_1) { Diag.Swallow("UiType.cs:331", dg331_1); }
                if (c.Controls.Count > 0) Scan(c, acc, depth + 1);
            }
        }
    }
}