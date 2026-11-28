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
    // v19.8：从 MainForm.cs 原样迁出（工作台首页与 Steam 价值），仅为拆分文件，逻辑未改
    partial class MainForm
    {
        // ================= v14.2 工作台首页 =================
        Panel[] homeCards;
        Label[] homeVals, homeSubs;
        double[] homePct = new double[] { 0, 0, 0 };
        // v14.5：三卡的 60 秒迷你走势（每 2 秒一个点，保留 30 个）
        List<double>[] homeHist = new List<double>[] { new List<double>(), new List<double>(), new List<double>() };
        ulong homePrevIdle, homePrevKernel, homePrevUser;
        bool homePrevInit = false;
        long homeMemTotal, homeMemUsed, homeDiskTotal, homeDiskFree;
        Label homeClock;
        Panel homeRecent;          // v14.3：最近使用区容器
        // v17.9：chips 原来是子控件，动态壁纸每 80ms 重绘一次，它们没开双缓冲就一直闪；
        // 改成容器自绘（一次 blit，不再有子窗口重绘），悬停/点击用命中测试处理。
        System.Collections.Generic.List<string> homeRecentItems = new System.Collections.Generic.List<string>();
        int homeRecentHover = -1;
        Panel homeTaskChip;        // v14.4：标题行任务胶囊
        System.Windows.Forms.Timer homeTimer;

        void BuildHomePage()
        {
            if (pageHome == null) return;

            Label hTitle = new Label();
            hTitle.Text = T("系统概况", "Overview");
            hTitle.ForeColor = UiKit.Paper ? Color.FromArgb(31, 29, 24) : C_TEXT;
            hTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            hTitle.Location = new Point(30, 24);
            hTitle.AutoSize = true;
            pageHome.Controls.Add(hTitle);

            Label hSub = new Label();
            hSub.Text = T("先看状态，再决定做什么：下面是这台机器现在的样子，和常用的几个入口。",
                "Status first: what this machine looks like now, plus the shortcuts you use most.");
            hSub.ForeColor = UiKit.Paper ? Color.FromArgb(96, 87, 72) : C_SUB;
            hSub.Font = new Font("Microsoft YaHei UI", 9F);
            hSub.Location = new Point(30, 68);
            hSub.AutoSize = true;
            pageHome.Controls.Add(hSub);

            homeClock = new Label();
            homeClock.Text = "--";
            homeClock.ForeColor = UiKit.Paper ? Color.FromArgb(92, 84, 70) : UiKit.A(170, 184, 207);
            homeClock.Font = new Font("Microsoft YaHei UI", 9F);
            homeClock.Location = new Point(600, 30);
            homeClock.Size = new Size(140, 20);
            homeClock.TextAlign = ContentAlignment.MiddleRight;
            pageHome.Controls.Add(homeClock);

            // v14.4：标题行右侧的任务胶囊——有任务时显示动作与剩余时间，点一下去定时页
            Panel taskChip = new Panel();
            taskChip.BackColor = Color.Transparent;
            taskChip.Location = new Point(330, 26);
            taskChip.Size = new Size(250, 26);
            taskChip.Cursor = Cursors.Hand;
            taskChip.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool on = taskActive;
                bool over = taskChip.Tag != null;
                Rectangle cr = new Rectangle(0, 0, taskChip.Width - 1, taskChip.Height - 1);
                using (GraphicsPath path = UiKit.RoundedPath(cr, 13))
                {
                    Color ct = UiKit.Paper
                        ? (over ? Color.FromArgb(255, 252, 243) : Color.FromArgb(250, 245, 233))
                        : (over ? UiKit.A(52, 62, 82) : UiKit.A(34, 40, 52));
                    Color cb = UiKit.Paper
                        ? (over ? Color.FromArgb(246, 240, 226) : Color.FromArgb(242, 235, 220))
                        : (over ? UiKit.A(38, 46, 61) : UiKit.A(26, 31, 41));
                    using (LinearGradientBrush br = new LinearGradientBrush(cr, ct, cb, 90F))
                        e.Graphics.FillPath(br, path);
                    Color bd = UiKit.Paper
                        ? (on ? Color.FromArgb(190, 214, 176, 86) : Color.FromArgb(255, 205, 194, 174))
                        : (on ? Color.FromArgb(170, curActionColor) : UiKit.AA(56, 255, 255, 255));
                    using (Pen pp = new Pen(bd, 1f))
                        e.Graphics.DrawPath(pp, path);
                }
                Color dotC = on ? (UiKit.Paper ? Color.FromArgb(198, 150, 45) : curActionColor) : (UiKit.Paper ? Color.FromArgb(174, 163, 143) : UiKit.A(104, 116, 134));
                using (SolidBrush db = new SolidBrush(dotC))
                    e.Graphics.FillEllipse(db, 12, 10, 7, 7);
                string txt;
                if (on)
                {
                    int leftS = (int)Math.Max(0, (endTime - DateTime.Now).TotalSeconds);
                    txt = curAction + T(" · 剩余 ", " · left ") + Fmt(leftS);
                }
                else txt = T("当前没有进行中的任务", "No task running");
                using (Font f = new Font(Ui2.FamilyName, 9F))
                {
                    StringFormat sf = new StringFormat(StringFormatFlags.NoWrap);
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Alignment = StringAlignment.Near;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    using (SolidBrush tb = new SolidBrush(on ? (UiKit.Paper ? Color.FromArgb(45, 40, 32) : Color.White)
                           : (UiKit.Paper ? Color.FromArgb(48, 42, 33) : UiKit.A(158, 170, 188))))
                        e.Graphics.DrawString(txt, f, tb, new RectangleF(26, 0, taskChip.Width - 36, taskChip.Height), sf);
                }
            };
            taskChip.MouseEnter += delegate { taskChip.Tag = "1"; taskChip.Invalidate(); };
            taskChip.MouseLeave += delegate { taskChip.Tag = null; taskChip.Invalidate(); };
            taskChip.Click += delegate
            {
                try { Sfx.Switch(); } catch (Exception dg9731_1) { Diag.Swallow("MainForm.HomeValue.cs:114", dg9731_1); }
                try { SelectAction(Array.IndexOf(SLOT, "power")); } catch (Exception dg9732_1) { Diag.Swallow("MainForm.HomeValue.cs:115", dg9732_1); }
            };
            pageHome.Controls.Add(taskChip);
            homeTaskChip = taskChip;

            // —— 三张实时状态卡（沿用电脑配置页的卡片画法）——
            homeCards = new Panel[3];
            homeVals = new Label[3];
            homeSubs = new Label[3];
            string[] hz = { "CPU 使用率", "内存占用", "C 盘可用" };
            string[] he = { "CPU usage", "Memory", "C: free" };
            Color[] hc = { UiKit.A(96, 165, 250), UiKit.A(16, 185, 129), UiKit.A(249, 115, 22) };
            for (int i = 0; i < 3; i++)
            {
                Panel cd = new Panel();
                cd.BackColor = Color.Transparent;
                cd.Location = new Point(30 + i * 252, 96);
                cd.Size = new Size(238, 96);
                cd.Name = "homeStat" + i;
                int ci = i;
                cd.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle cr = new Rectangle(0, 0, cd.Width - 1, cd.Height - 1);
                    using (GraphicsPath path = UiKit.RoundedPath(cr, 12))
                    {
                        if (UiKit.Paper)
                        {
                            Rectangle sh = new Rectangle(2, 3, cd.Width - 2, cd.Height - 2);
                            using (GraphicsPath sp = UiKit.RoundedPath(sh, 12))
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(20, 96, 78, 54)))
                                e.Graphics.FillPath(sb, sp);
                        }
                        e.Graphics.SetClip(path);
                        if (UiKit.Paper)
                        {
                            using (LinearGradientBrush br = new LinearGradientBrush(cr,
                                Color.FromArgb(255, 252, 243), Color.FromArgb(246, 240, 226), 90F))
                                e.Graphics.FillRectangle(br, cr);
                        }
                        else
                        {
                            using (LinearGradientBrush br = new LinearGradientBrush(cr,
                                UiKit.A(58, 67, 84), UiKit.A(35, 41, 54), 90F))
                                e.Graphics.FillRectangle(br, cr);
                        }
                        Rectangle tr = new Rectangle(0, 0, cd.Width, UiKit.Paper ? 2 : 3);
                        using (LinearGradientBrush tb = new LinearGradientBrush(tr, hc[ci], UiKit.Lighten(hc[ci], 65), 0F))
                            e.Graphics.FillRectangle(tb, tr);
                        int barW = cd.Width - 28;
                        double pv = homePct[ci];
                        if (pv < 0) pv = 0; if (pv > 100) pv = 100;
                        int fill = (int)Math.Round(barW * pv / 100.0);
                        using (SolidBrush bgB = new SolidBrush(UiKit.Paper ? Color.FromArgb(219, 210, 192) : UiKit.A(40, 48, 60)))
                            e.Graphics.FillRectangle(bgB, 14, cd.Height - 10, barW, 5);
                        using (SolidBrush fgB = new SolidBrush(hc[ci]))
                            e.Graphics.FillRectangle(fgB, 14, cd.Height - 10, Math.Max(2, fill), 5);
                        // v14.5：右侧 60 秒迷你走势（最多 30 个点，每点 2 秒）
                        List<double> hs = homeHist[ci];
                        if (hs.Count >= 2)
                        {
                            Rectangle hr = new Rectangle(170, 24, 56, 32);
                            double mx = hs[0], mn = hs[0];
                            for (int k = 0; k < hs.Count; k++) { if (hs[k] > mx) mx = hs[k]; if (hs[k] < mn) mn = hs[k]; }
                            // 自适应刻度：稳定数值也给足高度，但不夸大小抖动（最小跨度取量值的 3%）
                            double floor = Math.Max(mx * 0.03, 0.6);
                            if (mx - mn < floor) { double mid = (mx + mn) / 2; mn = mid - floor / 2; mx = mid + floor / 2; }
                            PointF[] pts = new PointF[hs.Count];
                            for (int k = 0; k < hs.Count; k++)
                            {
                                float px = hr.Left + (float)hr.Width * k / (hs.Count - 1);
                                float py = hr.Bottom - (float)(hr.Height * (hs[k] - mn) / (mx - mn));
                                pts[k] = new PointF(px, py);
                            }
                            using (Pen pl = new Pen(Color.FromArgb(160, hc[ci]), 1.3f))
                                e.Graphics.DrawLines(pl, pts);
                            PointF last = pts[hs.Count - 1];
                            using (SolidBrush d2 = new SolidBrush(hc[ci]))
                                e.Graphics.FillEllipse(d2, last.X - 2, last.Y - 2, 4, 4);
                            using (Pen bl = new Pen(UiKit.AA(40, 255, 255, 255), 1f))
                                e.Graphics.DrawLine(bl, hr.Left, hr.Bottom + 2, hr.Right, hr.Bottom + 2);
                        }
                        e.Graphics.ResetClip();
                        using (Pen pp = new Pen(UiKit.Paper ? Color.FromArgb(255, 178, 165, 141) : UiKit.AA(52, 255, 255, 255), 1f))
                            e.Graphics.DrawPath(pp, path);
                    }
                };
                Label cap = new Label();
                cap.Text = T(hz[i], he[i]);
                cap.BackColor = Color.Transparent;
                cap.ForeColor = UiKit.Paper ? Color.FromArgb(88, 80, 66) : UiKit.A(166, 180, 200);
                cap.Font = new Font("Microsoft YaHei UI", 8.5F);
                cap.Location = new Point(14, 8);
                cap.AutoSize = true;
                cd.Controls.Add(cap);
                Label vv = new Label();
                vv.Text = "--";
                vv.BackColor = Color.Transparent;
                vv.ForeColor = UiKit.Paper ? Color.FromArgb(34, 31, 26) : UiKit.A(238, 244, 255);
                vv.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
                vv.Location = new Point(13, 26);
                vv.Size = new Size(150, 30);
                cd.Controls.Add(vv);
                Label sm = new Label();
                sm.Text = T("采样中", "sampling");
                sm.BackColor = Color.Transparent;
                sm.ForeColor = UiKit.Paper ? Color.FromArgb(108, 98, 82) : UiKit.A(126, 140, 160);
                sm.Font = new Font("Microsoft YaHei UI", 8F);
                sm.Location = new Point(14, 60);
                sm.Size = new Size(200, 16);
                cd.Controls.Add(sm);
                if (UiKit.Paper)
                {
                    foreach (Control c0 in cd.Controls) if (c0 is Label) c0.Visible = false;
                    cd.Paint += delegate (object s3, PaintEventArgs e3)
                    {
                        Graphics g3 = e3.Graphics;
                        g3.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        foreach (Control c0 in cd.Controls)
                        {
                            Label l0 = c0 as Label; if (l0 == null) continue;
                            Color lc = (l0 == vv) ? Color.FromArgb(30, 27, 23)
                                     : (l0 == sm) ? Color.FromArgb(96, 86, 70)
                                     : Color.FromArgb(78, 70, 58);
                            using (StringFormat lsf = new StringFormat(StringFormatFlags.NoWrap))
                            {
                                lsf.LineAlignment = StringAlignment.Center;
                                lsf.Alignment = StringAlignment.Near;
                                lsf.Trimming = StringTrimming.EllipsisCharacter;
                                using (SolidBrush lb = new SolidBrush(lc))
                                    g3.DrawString(l0.Text, l0.Font, lb, new RectangleF(l0.Left, l0.Top, l0.Width, l0.Height), lsf);
                            }
                        }
                    };
                }
                pageHome.Controls.Add(cd);
                homeCards[i] = cd;
                homeVals[i] = vv;
                homeSubs[i] = sm;
            }

            // —— 快捷开始：四枚直接开倒计时的按钮 ——
            Panel quick = new Panel();
            quick.BackColor = Color.Transparent;
            quick.Location = new Point(30, 208);
            quick.Size = new Size(770, 92);
            quick.Name = "homeQuick";
            quick.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle qr = new Rectangle(0, 0, quick.Width - 1, quick.Height - 1);
                using (GraphicsPath path = UiKit.RoundedPath(qr, 12))
                {
                    e.Graphics.SetClip(path);
                    if (UiKit.Paper)
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(qr,
                            Color.FromArgb(255, 252, 243), Color.FromArgb(245, 239, 225), 90F))
                            e.Graphics.FillRectangle(br, qr);
                    }
                    else
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(qr,
                            UiKit.A(52, 60, 78), UiKit.A(33, 39, 51), 90F))
                            e.Graphics.FillRectangle(br, qr);
                    }
                    e.Graphics.ResetClip();
                    using (Pen pp = new Pen(UiKit.Paper ? Color.FromArgb(255, 190, 177, 151) : UiKit.AA(46, 255, 255, 255), 1f))
                        e.Graphics.DrawPath(pp, path);
                }
            };
            pageHome.Controls.Add(quick);

            Label qCap = new Label();
            qCap.Text = T("快捷开始", "Quick start");
            qCap.BackColor = Color.Transparent;
            qCap.ForeColor = UiKit.Paper ? Color.FromArgb(40, 36, 30) : UiKit.A(166, 180, 200);
            qCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            qCap.Location = new Point(14, 8);
            qCap.AutoSize = true;
            quick.Controls.Add(qCap);

            string[] qTxt = { "30 分钟后关机", "1 小时后关机", "今晚 22:00 关机", "取消当前任务" };
            string[] qEn = { "Shut down in 30m", "Shut down in 1h", "Shut down at 22:00", "Cancel task" };
            int[] qMin = { 30, 60, -1, -2 };
            for (int i = 0; i < 4; i++)
            {
                int qi = i;
                Color qbg = i == 0 ? UiKit.A(26, 104, 92) : (i == 3 ? UiKit.A(34, 40, 52) : UiKit.A(40, 49, 66));
                Color qfg = i == 3 ? UiKit.A(150, 162, 180) : Color.White;
                Button qb = MakeBtn(T(qTxt[i], qEn[i]), qbg, qfg, 14 + i * 182, 34, 162, 40);
                qb.Font = new Font("Microsoft YaHei UI", i == 3 ? 8.5F : 9F, FontStyle.Bold);
                qb.Click += (s, e) =>
                {
                    try
                    {
                        if (qMin[qi] == -2) { CancelTask(); return; }
                        if (qMin[qi] == -1) { if (dtpTime != null) dtpTime.Value = DateTime.Today.AddHours(22); rbDatetime.Checked = true; UpdateModeUI(); StartTask(); return; }
                        PresetCountdown(qMin[qi]);
                    }
                    catch (Exception dg9883_1) { Diag.Swallow("MainForm.HomeValue.cs:266", dg9883_1); }
                };
                quick.Controls.Add(qb);
            }

            // —— 全部功能：九宫格入口 ——
            Label fCap = new Label();
            fCap.Text = T("全部功能", "All features");
            fCap.ForeColor = UiKit.Paper ? UiKit.Ink : UiKit.A(166, 180, 200);
            fCap.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            if (UiKit.Paper) fCap.BackColor = UiKit.PaperBg;   // 透明标签在自绘纸上会出白边空心字
            fCap.Location = new Point(30, 314);
            fCap.AutoSize = true;
            if (UiKit.Paper)
            {
                fCap.Visible = false;   // 透明 Label 在自绘纸上会出白边空心字，改由页面自绘
                pageHome.Paint += delegate (object sCap, PaintEventArgs eCap)
                {
                    eCap.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font fc = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold))
                    using (SolidBrush bc2 = new SolidBrush(UiKit.Ink))
                        eCap.Graphics.DrawString(T("全部功能", "All features"), fc, bc2, new PointF(30f, 312f));
                };
            }
            pageHome.Controls.Add(fCap);

            int nTiles = SLOT.Length - 1;
            int tileCols = (nTiles % 3 == 0) ? 3 : 4;   // v17.9：8 项时按四列排，正好两行铺满
            for (int i = 1; i < SLOT.Length; i++)
            {
                int si = i;
                int col = (i - 1) % tileCols;
                int row = (i - 1) / tileCols;
                Panel tile = new Panel();
                tile.BackColor = Color.Transparent;
                tile.Location = new Point(30 + col * 258, 344 + row * 74);
                tile.Size = new Size(244, 64);
                tile.Name = "homeTile" + (i - 1);
                tile.Cursor = Cursors.Hand;
                Color tc = ACTION_COLOR[i];
                tile.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle wr = new Rectangle(0, 0, tile.Width - 1, tile.Height - 1);
                    bool over = tile.Tag != null;
                    using (GraphicsPath path = UiKit.RoundedPath(wr, 11))
                    {
                        if (UiKit.Paper)
                        {
                            Rectangle sh = new Rectangle(2, 3, tile.Width - 2, tile.Height - 2);
                            using (GraphicsPath sp = UiKit.RoundedPath(sh, 11))
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(16, 96, 78, 54)))
                                e.Graphics.FillPath(sb, sp);
                        }
                        e.Graphics.SetClip(path);
                        if (UiKit.Paper)
                        {
                            using (LinearGradientBrush br = new LinearGradientBrush(wr,
                                over ? Color.FromArgb(255, 252, 243) : Color.FromArgb(250, 245, 233),
                                over ? Color.FromArgb(247, 241, 228) : Color.FromArgb(242, 235, 220), 90F))
                                e.Graphics.FillRectangle(br, wr);
                        }
                        else
                        {
                            using (LinearGradientBrush br = new LinearGradientBrush(wr,
                                over ? UiKit.A(62, 72, 92) : UiKit.A(46, 53, 68),
                                over ? UiKit.A(40, 47, 61) : UiKit.A(31, 36, 47), 90F))
                                e.Graphics.FillRectangle(br, wr);
                        }
                        e.Graphics.ResetClip();
                        using (Pen pp = new Pen(UiKit.Paper ? (over ? Color.FromArgb(190, tc) : Color.FromArgb(255, 190, 177, 151)) : (over ? Color.FromArgb(150, tc) : UiKit.AA(44, 255, 255, 255)), 1f))
                            e.Graphics.DrawPath(pp, path);
                    }
                    Rectangle ar = new Rectangle(12, 15, 34, 34);
                    Color ic = UiKit.Paper ? UiKit.Darken(tc, 52) : tc;
                    using (GraphicsPath ap = UiKit.RoundedPath(ar, 9))
                    {
                        using (LinearGradientBrush ab = new LinearGradientBrush(ar, UiKit.Lighten(ic, 10), UiKit.Darken(ic, 26), 90F))
                            e.Graphics.FillPath(ab, ap);
                    }
                    UiKit.Glyph(e.Graphics, new Rectangle(12, 15, 34, 34), si);
                    if (UiKit.Paper)
                    {
                        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        using (Font ft = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
                        using (SolidBrush tb = new SolidBrush(Color.FromArgb(36, 32, 27)))
                            e.Graphics.DrawString(ACTIONS[si], ft, tb, new PointF(56f, 12f));
                        using (Font fs = new Font("Microsoft YaHei UI", 8F))
                        using (SolidBrush sb2 = new SolidBrush(Color.FromArgb(82, 73, 60)))
                            e.Graphics.DrawString(ACTION_SUB[si], fs, sb2, new PointF(56f, 36f));
                    }
                    if (over)
                    {
                        using (Font fu = new Font(Ui2.FamilyName, 9F))
                            TextRenderer.DrawText(e.Graphics, "›", fu, new Rectangle(tile.Width - 28, 16, 18, 28),
                                Color.FromArgb(220, tc), TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    }
                };
                Label tl = new Label();
                tl.Text = ACTIONS[i];
                tl.BackColor = Color.Transparent;
                tl.Visible = !UiKit.Paper;   // 纸感改由卡片自绘文字，绕开透明 Label 的白边空心字
                tl.ForeColor = UiKit.Paper ? Color.FromArgb(36, 32, 27) : C_TEXT;
                tl.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                tl.Location = new Point(54, 15);
                tl.AutoSize = true;
                tl.Cursor = Cursors.Hand;
                tile.Controls.Add(tl);
                Label tsub = new Label();
                tsub.Text = ACTION_SUB[i];
                tsub.BackColor = Color.Transparent;
                tsub.ForeColor = UiKit.Paper ? Color.FromArgb(98, 88, 73) : C_SUB;
                tsub.BackColor = Color.Transparent;
                tsub.Visible = !UiKit.Paper;
                tsub.Font = new Font("Microsoft YaHei UI", 8F);
                tsub.Location = new Point(54, 37);
                tsub.Size = new Size(180, 16);
                tsub.AutoEllipsis = true;
                tsub.Cursor = Cursors.Hand;
                tile.Controls.Add(tsub);
                EventHandler go = delegate
                {
                    try { Sfx.Click(); } catch (Exception dg9962_1) { Diag.Swallow("MainForm.HomeValue.cs:345", dg9962_1); }
                    try { SelectAction(si); } catch (Exception dg9963_1) { Diag.Swallow("MainForm.HomeValue.cs:346", dg9963_1); }
                };
                tile.Click += go;
                tl.Click += go;
                tsub.Click += go;
                tile.MouseEnter += delegate { tile.Tag = "1"; tile.Invalidate(); };
                tile.MouseLeave += delegate { tile.Tag = null; tile.Invalidate(); };
                pageHome.Controls.Add(tile);
            }

            // v19.11：新增「实用工具」磁贴（不进侧栏，点开独立小窗：哈希 / 取色 / 印章 / Base64 / 时间戳 / 密码）
            Panel utilTile = new Panel();
            utilTile.BackColor = Color.Transparent;
            utilTile.Location = new Point(30, 344 + 2 * 74);
            utilTile.Size = new Size(244, 64);
            utilTile.Name = "homeWide0";
            utilTile.Cursor = Cursors.Hand;
            utilTile.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle wr = new Rectangle(0, 0, utilTile.Width - 1, utilTile.Height - 1);
                bool over = utilTile.Tag != null;
                Color tc = UiKit.A(45, 212, 191);
                using (GraphicsPath path = UiKit.RoundedPath(wr, 11))
                {
                    if (UiKit.Paper)
                    {
                        Rectangle sh = new Rectangle(2, 3, utilTile.Width - 2, utilTile.Height - 2);
                        using (GraphicsPath sp = UiKit.RoundedPath(sh, 11))
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(16, 96, 78, 54)))
                            e.Graphics.FillPath(sb, sp);
                    }
                    e.Graphics.SetClip(path);
                    if (UiKit.Paper)
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(wr,
                            over ? Color.FromArgb(255, 252, 243) : Color.FromArgb(250, 245, 233),
                            over ? Color.FromArgb(247, 241, 228) : Color.FromArgb(242, 235, 220), 90F))
                            e.Graphics.FillRectangle(br, wr);
                    }
                    else
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(wr,
                            over ? UiKit.A(62, 72, 92) : UiKit.A(46, 53, 68),
                            over ? UiKit.A(40, 47, 61) : UiKit.A(31, 36, 47), 90F))
                            e.Graphics.FillRectangle(br, wr);
                    }
                    e.Graphics.ResetClip();
                    using (Pen pp = new Pen(UiKit.Paper ? (over ? Color.FromArgb(190, tc) : Color.FromArgb(255, 190, 177, 151)) : (over ? Color.FromArgb(150, tc) : UiKit.AA(44, 255, 255, 255)), 1f))
                        e.Graphics.DrawPath(pp, path);
                }
                Rectangle ar = new Rectangle(12, 16, 32, 32);
                using (GraphicsPath ap = UiKit.RoundedPath(ar, 9))
                using (LinearGradientBrush ab = new LinearGradientBrush(ar, UiKit.Lighten(tc, 18), UiKit.Darken(tc, 46), 90F))
                    e.Graphics.FillPath(ab, ap);
                Color[] gc = { UiKit.A(56, 189, 248), UiKit.A(45, 212, 191), Color.FromArgb(198, 32, 40), UiKit.A(129, 140, 248) };
                for (int q = 0; q < 4; q++)
                {
                    Rectangle qr = new Rectangle(ar.X + 6 + (q % 2) * 10, ar.Y + 6 + (q / 2) * 10, 8, 8);
                    using (GraphicsPath qp = UiKit.RoundedPath(qr, 2))
                    using (SolidBrush qb = new SolidBrush(gc[q]))
                        e.Graphics.FillPath(qb, qp);
                }
                if (over)
                {
                    using (Font fu = new Font(Ui2.FamilyName, 9F))
                        TextRenderer.DrawText(e.Graphics, "›", fu, new Rectangle(utilTile.Width - 28, 16, 18, 28),
                            Color.FromArgb(220, tc), TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
                if (UiKit.Paper)
                {
                    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font ft = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(36, 32, 27)))
                        e.Graphics.DrawString(T("实用工具", "Utilities"), ft, tb, new PointF(56f, 12f));
                    using (Font fs = new Font("Microsoft YaHei UI", 8F))
                    using (SolidBrush sb2 = new SolidBrush(Color.FromArgb(82, 73, 60)))
                        e.Graphics.DrawString(T("哈希 · 取色 · 印章 · 编码", "Hash, color, seal, code"), fs, sb2, new PointF(56f, 36f));
                }
            };
            Label utl = new Label();
            utl.Text = T("实用工具", "Utilities");
            utl.BackColor = Color.Transparent;
            utl.ForeColor = UiKit.Paper ? Color.FromArgb(36, 32, 27) : C_TEXT;
            utl.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            utl.Location = new Point(54, 15);
            utl.AutoSize = true;
            utl.Cursor = Cursors.Hand;
            if (UiKit.Paper) utl.Visible = false;
            utilTile.Controls.Add(utl);
            Label usub = new Label();
            usub.Text = T("哈希 · 取色 · 印章 · 编码", "Hash, color, seal, code");
            usub.BackColor = Color.Transparent;
            usub.ForeColor = UiKit.Paper ? Color.FromArgb(98, 88, 73) : C_SUB;
            usub.Font = new Font("Microsoft YaHei UI", 8F);
            usub.Location = new Point(54, 37);
            usub.Size = new Size(320, 16);
            usub.AutoEllipsis = true;
            usub.Cursor = Cursors.Hand;
            if (UiKit.Paper) usub.Visible = false;
            utilTile.Controls.Add(usub);
            EventHandler utilGo = delegate
            {
                try { Sfx.Click(); } catch (Exception dgUtil1) { Diag.Swallow("MainForm.HomeValue.cs:util1", dgUtil1); }
                try { ShowUtilTools(); } catch (Exception dgUtil2) { Diag.Swallow("MainForm.HomeValue.cs:util2", dgUtil2); }
            };
            utilTile.Click += utilGo;
            utl.Click += utilGo;
            usub.Click += utilGo;
            utilTile.MouseEnter += delegate { utilTile.Tag = "1"; utilTile.Invalidate(); };
            utilTile.MouseLeave += delegate { utilTile.Tag = null; utilTile.Invalidate(); };
            pageHome.Controls.Add(utilTile);

            // v19.13：新增「工具站」磁贴——整站 88 个开源工具，独立窗口打开（v19.15 起改为离线包按需加载）
            Panel siteTile = new Panel();
            siteTile.BackColor = Color.Transparent;
            siteTile.Location = new Point(288, 344 + 2 * 74);
            siteTile.Size = new Size(244, 64);
            siteTile.Name = "homeWide1";
            siteTile.Cursor = Cursors.Hand;
            siteTile.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle wr = new Rectangle(0, 0, siteTile.Width - 1, siteTile.Height - 1);
                bool over = siteTile.Tag != null;
                Color tc = UiKit.A(245, 158, 11);
                using (GraphicsPath path = UiKit.RoundedPath(wr, 11))
                {
                    if (UiKit.Paper)
                    {
                        Rectangle sh = new Rectangle(2, 3, siteTile.Width - 2, siteTile.Height - 2);
                        using (GraphicsPath sp = UiKit.RoundedPath(sh, 11))
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(16, 96, 78, 54)))
                            e.Graphics.FillPath(sb, sp);
                    }
                    e.Graphics.SetClip(path);
                    if (UiKit.Paper)
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(wr,
                            over ? Color.FromArgb(255, 252, 243) : Color.FromArgb(250, 245, 233),
                            over ? Color.FromArgb(247, 241, 228) : Color.FromArgb(242, 235, 220), 90F))
                            e.Graphics.FillRectangle(br, wr);
                    }
                    else
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(wr,
                            over ? UiKit.A(66, 55, 40) : UiKit.A(48, 40, 30),
                            over ? UiKit.A(43, 35, 25) : UiKit.A(33, 28, 21), 90F))
                            e.Graphics.FillRectangle(br, wr);
                    }
                    e.Graphics.ResetClip();
                    using (Pen pp = new Pen(over ? Color.FromArgb(UiKit.Paper ? 190 : 170, tc) : (UiKit.Paper ? Color.FromArgb(255, 190, 177, 151) : UiKit.AA(52, 245, 158, 11)), 1f))
                        e.Graphics.DrawPath(pp, path);
                }
                Rectangle ar = new Rectangle(12, 16, 32, 32);
                using (GraphicsPath ap = UiKit.RoundedPath(ar, 9))
                using (LinearGradientBrush ab = new LinearGradientBrush(ar, UiKit.A(251, 191, 36), UiKit.A(146, 64, 14), 90F))
                    e.Graphics.FillPath(ab, ap);
                TextRenderer.DrawText(e.Graphics, "88", new Font(Ui2.FamilyName, 11F, FontStyle.Bold), ar,
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (over)
                {
                    using (Font fu = new Font(Ui2.FamilyName, 9F))
                        TextRenderer.DrawText(e.Graphics, "›", fu, new Rectangle(siteTile.Width - 28, 16, 18, 28),
                            Color.FromArgb(230, tc), TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
                if (UiKit.Paper)
                {
                    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font ft = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(36, 32, 27)))
                        e.Graphics.DrawString(T("工具站", "Tool station"), ft, tb, new PointF(56f, 12f));
                    using (Font fs = new Font("Microsoft YaHei UI", 8F))
                    using (SolidBrush sb2 = new SolidBrush(Color.FromArgb(82, 73, 60)))
                        e.Graphics.DrawString(T("88 个工具 · 首次按需加载", "88 tools, on demand"), fs, sb2, new PointF(56f, 36f));
                }
            };
            Label stl = new Label();
            stl.Text = T("工具站", "Tool station");
            stl.BackColor = Color.Transparent;
            stl.ForeColor = UiKit.Paper ? Color.FromArgb(36, 32, 27) : C_TEXT;
            stl.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            stl.Location = new Point(54, 15);
            stl.AutoSize = true;
            stl.Cursor = Cursors.Hand;
            if (UiKit.Paper) stl.Visible = false;
            siteTile.Controls.Add(stl);
            Label ssub = new Label();
            ssub.Text = T("88 个工具 · 首次按需加载", "88 tools, on demand");
            ssub.BackColor = Color.Transparent;
            ssub.ForeColor = UiKit.Paper ? Color.FromArgb(98, 88, 73) : C_SUB;
            ssub.Font = new Font("Microsoft YaHei UI", 8F);
            ssub.Location = new Point(54, 37);
            ssub.Size = new Size(320, 16);
            ssub.AutoEllipsis = true;
            ssub.Cursor = Cursors.Hand;
            if (UiKit.Paper) ssub.Visible = false;
            siteTile.Controls.Add(ssub);
            EventHandler siteGo = delegate
            {
                try { Sfx.Click(); } catch (Exception dgSite1) { Diag.Swallow("MainForm.HomeValue.cs:site1", dgSite1); }
                try { ShowToolsSite(); } catch (Exception dgSite2) { Diag.Warn("打开工具站失败：" + dgSite2.Message); }
            };
            siteTile.Click += siteGo;
            stl.Click += siteGo;
            ssub.Click += siteGo;
            siteTile.MouseEnter += delegate { siteTile.Tag = "1"; siteTile.Invalidate(); };
            siteTile.MouseLeave += delegate { siteTile.Tag = null; siteTile.Invalidate(); };
            pageHome.Controls.Add(siteTile);

            // —— v14.3：最近使用（读 指令历史.txt，点一下把原话送回指令舱）——
            Label rCap = new Label();
            rCap.Text = T("最近使用", "Recently used");
            rCap.ForeColor = UiKit.Paper ? UiKit.Ink : UiKit.A(166, 180, 200);
            rCap.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            rCap.Location = new Point(30, 566);
            rCap.AutoSize = true;
            pageHome.Controls.Add(rCap);

            homeRecent = new Panel();
            homeRecent.BackColor = Color.Transparent;
            homeRecent.Location = new Point(30, 592);
            homeRecent.Size = new Size(770, 64);
            MakeDoubleBuffered(homeRecent);
            homeRecent.Paint += (s2, e2) => PaintHomeRecent(e2.Graphics);
            homeRecent.MouseMove += delegate (object s2, MouseEventArgs e2)
            {
                int idx = HomeRecentHit(e2.X, e2.Y);
                if (idx != homeRecentHover)
                {
                    homeRecentHover = idx;
                    homeRecent.Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                    homeRecent.Invalidate();
                }
            };
            homeRecent.MouseLeave += delegate
            {
                if (homeRecentHover != -1)
                {
                    homeRecentHover = -1;
                    homeRecent.Cursor = Cursors.Default;
                    homeRecent.Invalidate();
                }
            };
            homeRecent.Click += delegate
            {
                if (homeRecentHover < 0 || homeRecentHover >= homeRecentItems.Count) return;
                string txt = homeRecentItems[homeRecentHover];
                try { Sfx.Click(); } catch (Exception dg10011_1) { Diag.Swallow("MainForm.HomeValue.cs:394", dg10011_1); }
                try
                {
                    PaletteForm pf = ShowPalette();
                    if (pf != null) { pf.SetQueryForTest(txt); pf.Activate(); }
                }
                catch (Exception dg10017_1) { Diag.Swallow("MainForm.HomeValue.cs:400", dg10017_1); }
            };
            pageHome.Controls.Add(homeRecent);
            RefreshHomeRecent();

            // v14.3：提示改成自绘面板，Right + EndEllipsis，宁可省略也不越出右边界
            Panel tipP = new Panel();
            tipP.BackColor = Color.Transparent;
            tipP.Location = new Point(140, 568);
            tipP.Size = new Size(600, 16);
            tipP.Paint += (s, e) =>
            {
                if (UiKit.Paper)
                {
                    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font f = new Font(Ui2.FamilyName, 9F))
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(68, 60, 48)))
                    using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                    {
                        sf.Trimming = StringTrimming.EllipsisCharacter;
                        sf.LineAlignment = StringAlignment.Center;
                        e.Graphics.DrawString(
                            T("Ctrl+K 全程序搜索 · 状态两秒刷新一次", "Ctrl+K searches all · stats refresh every 2s"),
                            f, br, new RectangleF(0, 0, tipP.Width, 16), sf);
                    }
                }
                else
                {
                    using (Font f = new Font(Ui2.FamilyName, 8F))
                        TextRenderer.DrawText(e.Graphics,
                            T("Ctrl+K 全程序搜索 · 状态两秒刷新一次", "Ctrl+K searches all · stats refresh every 2s"),
                            f, new Rectangle(0, 0, tipP.Width, 16), UiKit.A(146, 162, 186),
                            TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
            };
            pageHome.Controls.Add(tipP);

            homeTimer = new System.Windows.Forms.Timer();
            homeTimer.Interval = 2000;
            homeTimer.Tick += delegate { try { UpdateHomeStats(); } catch (Exception dg10039_1) { Diag.Swallow("MainForm.HomeValue.cs:422", dg10039_1); } };
            homeTimer.Start();
            UpdateHomeStats();
        }

        // v14.3：首页「最近使用」——只读 指令历史.txt，不写任何东西；点击只把原话预填进指令舱，不自动执行
        string[] LoadRecentCmds(int n)
        {
            List<string> ls = new List<string>();
            try
            {
                string hf = Path.Combine(BASE_DIR, "指令历史.txt");
                if (File.Exists(hf))
                {
                    string[] hl = File.ReadAllLines(hf, Encoding.UTF8);
                    for (int i = hl.Length - 1; i >= 0 && ls.Count < n; i--)
                    {
                        string ln = hl[i] == null ? "" : hl[i].Trim();
                        if (ln.Length == 0) continue;
                        if (ls.Contains(ln)) continue;
                        ls.Add(ln);
                    }
                }
            }
            catch (Exception dg10063_1) { Diag.Swallow("MainForm.HomeValue.cs:446", dg10063_1); }
            return ls.ToArray();
        }

        void RefreshHomeRecent()
        {
            if (homeRecent == null) return;
            try
            {
                homeRecentItems.Clear();
                homeRecentHover = -1;
                string[] rc = LoadRecentCmds(6);
                for (int i = 0; i < rc.Length && i < 6; i++) homeRecentItems.Add(rc[i]);
                if (homeRecent.Cursor != Cursors.Default) homeRecent.Cursor = Cursors.Default;
                homeRecent.Invalidate();
            }
            catch (Exception dg10079_1) { Diag.Swallow("MainForm.HomeValue.cs:462", dg10079_1); }
        }

        Rectangle HomeRecentRect(int i)
        {
            int cw4 = Math.Max(200, (homeRecent.Width - 28) / 3);
            int col = i % 3, row = i / 3;
            return new Rectangle(col * (cw4 + 14), row * 34, cw4, 28);
        }

        int HomeRecentHit(int x, int y)
        {
            for (int i = 0; i < homeRecentItems.Count; i++)
                if (HomeRecentRect(i).Contains(x, y)) return i;
            return -1;
        }

        void PaintHomeRecent(Graphics g)
        {
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (homeRecentItems.Count == 0)
                {
                    if (UiKit.Paper)
                    {
                        Rectangle er = new Rectangle(2, 2, homeRecent.Width - 4, 26);
                        using (GraphicsPath ep = UiKit.RoundedPath(er, 9))
                        {
                            using (SolidBrush eb = new SolidBrush(Color.FromArgb(250, 245, 233)))
                                g.FillPath(eb, ep);
                            using (Pen epen = new Pen(Color.FromArgb(222, 210, 188), 1f))
                                g.DrawPath(epen, ep);
                        }
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        using (Font f0 = new Font(Ui2.FamilyName, 9.5F))
                        using (SolidBrush br0 = new SolidBrush(Color.FromArgb(64, 56, 45)))
                        using (StringFormat sf0 = new StringFormat(StringFormatFlags.NoWrap))
                        {
                            sf0.Trimming = StringTrimming.EllipsisCharacter;
                            sf0.LineAlignment = StringAlignment.Center;
                            g.DrawString(T("还没有记录：在指令舱（Ctrl+K）里执行过的指令，会按时间倒序出现在这里。",
                                "Nothing yet: commands you run in the palette (Ctrl+K) show up here, newest first."),
                                f0, br0, new RectangleF(12, 2, homeRecent.Width - 24, 26), sf0);
                        }
                    }
                    else
                    {
                        using (Font f0 = new Font(Ui2.FamilyName, 8.5F))
                        using (SolidBrush br0 = new SolidBrush(UiKit.A(120, 134, 154)))
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.DrawString(T("还没有记录：在指令舱（Ctrl+K）里执行过的指令，会按时间倒序出现在这里。",
                                "Nothing yet: commands you run in the palette (Ctrl+K) show up here, newest first."),
                                f0, br0, new RectangleF(4, 6, homeRecent.Width - 12, 20));
                        }
                    }
                    return;
                }
                Color cc = UiKit.Paper ? Color.FromArgb(190, 154, 76) : UiKit.A(129, 140, 248);
                using (Font f = new Font(Ui2.FamilyName, UiKit.Paper ? 8.5F : 8.5F))
                using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                {
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.LineAlignment = StringAlignment.Center;
                    for (int i = 0; i < homeRecentItems.Count; i++)
                    {
                        Rectangle cr = HomeRecentRect(i);
                        bool over = (i == homeRecentHover);
                        using (GraphicsPath path = UiKit.RoundedPath(cr, 8))
                        {
                            if (UiKit.Paper)
                            {
                                using (LinearGradientBrush br = new LinearGradientBrush(cr,
                                    over ? Color.FromArgb(255, 252, 243) : Color.FromArgb(250, 245, 233),
                                    over ? Color.FromArgb(247, 241, 228) : Color.FromArgb(242, 235, 220), 90F))
                                    g.FillPath(br, path);
                                using (Pen pp = new Pen(over ? Color.FromArgb(176, 146, 82) : Color.FromArgb(222, 210, 188), 1f))
                                    g.DrawPath(pp, path);
                            }
                            else
                            {
                                using (LinearGradientBrush br = new LinearGradientBrush(cr,
                                    over ? UiKit.A(46, 55, 72) : UiKit.A(32, 38, 50),
                                    over ? UiKit.A(34, 41, 55) : UiKit.A(27, 32, 42), 90F))
                                    g.FillPath(br, path);
                                using (Pen pp = new Pen(over ? Color.FromArgb(150, cc) : UiKit.AA(40, 255, 255, 255), 1f))
                                    g.DrawPath(pp, path);
                            }
                        }
                        if (UiKit.Paper)
                        {
                            // 真正的“最近使用”图标：金底小钟表，替代原来那颗看不出含义的圆点
                            Rectangle ir = new Rectangle(cr.X + 9, cr.Y + 7, 14, 14);
                            using (GraphicsPath ip = UiKit.RoundedPath(ir, 7))
                            {
                                using (SolidBrush ib = new SolidBrush(Color.FromArgb(206, 174, 102)))
                                    g.FillPath(ib, ip);
                                using (Pen ipn = new Pen(Color.FromArgb(150, 120, 64), 1f))
                                    g.DrawPath(ipn, ip);
                            }
                            using (Pen hp = new Pen(Color.FromArgb(52, 45, 35), 1.2f))
                            {
                                g.DrawLine(hp, ir.X + 7, ir.Y + 4, ir.X + 7, ir.Y + 7);
                                g.DrawLine(hp, ir.X + 7, ir.Y + 7, ir.X + 10, ir.Y + 9);
                            }
                        }
                        else
                        {
                            using (SolidBrush dot = new SolidBrush(cc))
                                g.FillEllipse(dot, cr.X + 12, cr.Y + 10, 7, 7);
                        }
                        string show = homeRecentItems[i];
                        if (show.StartsWith("跳转：")) show = show.Substring(3);
                        if (UiKit.Paper)
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            using (SolidBrush tb = new SolidBrush(over ? Color.FromArgb(28, 25, 21) : Color.FromArgb(64, 56, 45)))
                            using (SolidBrush ab = new SolidBrush(over ? Color.FromArgb(192, 152, 62) : Color.FromArgb(150, 126, 88)))
                            using (Font af = new Font("Segoe UI", 9.5F))
                            {
                                g.DrawString(show, f, tb,
                                    new RectangleF(cr.X + 30, cr.Y + 4, Math.Max(40, cr.Width - 56), 20), sf);
                                sf.Alignment = StringAlignment.Far;
                                g.DrawString("›", af, ab,
                                    new RectangleF(cr.Right - 20, cr.Y + 4, 12, 20), sf);
                                sf.Alignment = StringAlignment.Near;
                            }
                        }
                        else
                        {
                            TextRenderer.DrawText(g, show, f,
                                new Rectangle(cr.X + 26, cr.Y + 5, Math.Max(40, cr.Width - 64), 18),
                                over ? Color.White : UiKit.A(198, 208, 224),
                                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                            using (Font fr = new Font(Ui2.FamilyName, 8.5F))
                                TextRenderer.DrawText(g, "›", fr, new Rectangle(cr.Right - 22, cr.Y + 5, 14, 18),
                                    over ? Color.White : UiKit.A(140, 190, 180),
                                    TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                        }
                    }
                }
            }
            catch (Exception dg10140_1) { Diag.Swallow("MainForm.HomeValue.cs:523", dg10140_1); }
        }

        void UpdateHomeStats()
        {
            try
            {
                if (homeClock != null)
                    homeClock.Text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm");
                if (homeTaskChip != null) homeTaskChip.Invalidate();   // 剩余时间随采样一起走
                ulong i2, k2, u2;
                if (GetSystemTimes(out i2, out k2, out u2))
                {
                    if (homePrevInit)
                    {
                        ulong dIdle = i2 - homePrevIdle;
                        ulong dTotal = (k2 - homePrevKernel) + (u2 - homePrevUser);
                        if (dTotal > 0)
                        {
                            double busy = 1.0 - (double)dIdle / dTotal;
                            if (busy < 0) busy = 0; if (busy > 1) busy = 1;
                            homePct[0] = busy * 100.0;
                        }
                    }
                    homePrevIdle = i2; homePrevKernel = k2; homePrevUser = u2; homePrevInit = true;
                }
                MEMORYSTATUSEX ms = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(ms))
                {
                    homeMemTotal = (long)ms.ullTotalPhys;
                    homeMemUsed = (long)(ms.ullTotalPhys - ms.ullAvailPhys);
                    homePct[1] = ms.dwMemoryLoad;
                }
                DriveInfo di = new DriveInfo("C");
                homeDiskTotal = di.TotalSize;
                homeDiskFree = di.TotalFreeSpace;
                homePct[2] = homeDiskTotal > 0 ? 100.0 * (homeDiskTotal - homeDiskFree) / homeDiskTotal : 0;

                if (homeVals != null && homeVals[0] != null) homeVals[0].Text = homePct[0].ToString("0.0") + "%";
                if (homeVals != null && homeVals[1] != null) homeVals[1].Text = (homeMemUsed / 1073741824.0).ToString("0.0") + " GB";
                if (homeVals != null && homeVals[2] != null) homeVals[2].Text = (homeDiskFree / 1073741824.0).ToString("0.0") + " GB";
                if (homeSubs != null && homeSubs[0] != null) homeSubs[0].Text = T("每 2 秒实时采样", "sampled every 2s");
                if (homeSubs != null && homeSubs[1] != null) homeSubs[1].Text = T("共 ", "of ") + (homeMemTotal / 1073741824.0).ToString("0.0") + " GB · " + T("占用 ", "used ") + homePct[1].ToString("0") + "%";
                if (homeSubs != null && homeSubs[2] != null) homeSubs[2].Text = T("已用 ", "used ") + homePct[2].ToString("0.0") + "% · " + T("共 ", "of ") + (homeDiskTotal / 1073741824.0).ToString("0.0") + " GB";
                if (homeCards != null) for (int i = 0; i < homeCards.Length; i++) if (homeCards[i] != null) homeCards[i].Invalidate();
                for (int i = 0; i < 3; i++)
                {
                    homeHist[i].Add(homePct[i]);
                    while (homeHist[i].Count > 30) homeHist[i].RemoveAt(0);
                }
            }
            catch (Exception dg10191_1) { Diag.Swallow("MainForm.HomeValue.cs:574", dg10191_1); }
        }

        void ShowHomePage(bool show)
        {
            if (pageHome == null) return;
            if (show)
            {
                HideAllPages((Panel)pageHome);
                pageHome.Visible = true;
                pageHome.BringToFront();
                UpdateHomeStats();
                RefreshHomeRecent();
            }
            else
            {
                // v17.6：这里原来直接切 pageTimer，绕过了 HideAllPages，导致从首页点回「关机」
                // 页面内容变了、Header 却还写着「工作台 00 / DASHBOARD」，右上三主操作也不出现
                HideAllPages((Panel)pageTimer);
                pageTimer.Visible = true;
                pageTimer.BringToFront();
            }
        }

        void HideAllPages(Panel except)
        {
            if (pageHome != null && pageHome != except) pageHome.Visible = false;
            if (pageTimer != null && pageTimer != except) pageTimer.Visible = false;
            if (pageReset != null && pageReset != except) pageReset.Visible = false;
            if (pageClean != null && pageClean != except) pageClean.Visible = false;
            if (pageSam != null && pageSam != except) pageSam.Visible = false;
            if (pageVideo != null && pageVideo != except) pageVideo.Visible = false;
            if (pageMusic != null && pageMusic != except) pageMusic.Visible = false;
            if (pageWatermark != null && pageWatermark != except) pageWatermark.Visible = false;
            if (pageValue != null && pageValue != except) pageValue.Visible = false;
            if (pageSys != null && pageSys != except) pageSys.Visible = false;
            SyncHeader(except);
        }

        void ShowVideoPage(bool show)
        {
            if (pageVideo == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageMusic != null) pageMusic.Visible = false;
            pageVideo.Visible = show;
            if (show) { lblPeer.Text = T("视频下载", "Video Download"); lblPeer.ForeColor = ActCol("video"); }
            else RestoreTimerHeader();
        }

        void ShowMusicPage(bool show)
        {
            if (pageMusic == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageVideo != null) pageVideo.Visible = false;
            pageMusic.Visible = show;
            if (show) { lblPeer.Text = T("音乐下载", "Music Download"); lblPeer.ForeColor = ActCol("music"); }
            else RestoreTimerHeader();
        }

        void ShowValuePage(bool show)
        {
            if (pageValue == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageSam != null) pageSam.Visible = false;
            if (pageVideo != null) pageVideo.Visible = false;
            if (pageMusic != null) pageMusic.Visible = false;
            if (pageWatermark != null) pageWatermark.Visible = false;
            if (pageSys != null) pageSys.Visible = false;
            pageValue.Visible = show;
            if (show) { lblPeer.Text = T("Steam 账号价值", "Steam Account Value"); lblPeer.ForeColor = ActCol("val"); }
            else RestoreTimerHeader();
        }

        void ShowSysPage(bool show)
        {
            if (pageSys == null) return;
            pageTimer.Visible = !show;
            if (pageReset != null) pageReset.Visible = false;
            if (pageClean != null) pageClean.Visible = false;
            if (pageSam != null) pageSam.Visible = false;
            if (pageVideo != null) pageVideo.Visible = false;
            if (pageMusic != null) pageMusic.Visible = false;
            if (pageWatermark != null) pageWatermark.Visible = false;
            if (pageValue != null) pageValue.Visible = false;
            pageSys.Visible = show;
            if (show) { lblPeer.Text = T("电脑配置", "PC Specs"); lblPeer.ForeColor = ActCol("sys"); }
            else RestoreTimerHeader();
        }

        // ================= Steam 账号价值 =================
        class ValRow
        {
            public int AppId;
            public string Name;
            public string PriceText;
            public string SizeText;
            public long TotalBytes;
        }

        void ValUi(MethodInvoker a)
        {
            if (this.InvokeRequired) { try { this.BeginInvoke(a); } catch (Exception dg10298_1) { Diag.Swallow("MainForm.HomeValue.cs:681", dg10298_1); } }
            else a();
        }

        void ValSetStatus(string text, Color c)
        {
            ValUi(delegate ()
            {
                if (lblValStatus == null) return;
                lblValStatus.Text = text;
                lblValStatus.ForeColor = c;
            });
        }

        void ValFinish()
        {
            ValUi(delegate ()
            {
                valBusy = false;
                if (btnValScan != null) btnValScan.Enabled = true;
            });
        }

        string SteamRoot()
        {
            try
            {
                object o = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                if (o != null)
                {
                    string p = o.ToString().Replace('/', '\\');
                    if (Directory.Exists(p)) return p;
                }
            }
            catch (Exception dg10332_1) { Diag.Swallow("MainForm.HomeValue.cs:715", dg10332_1); }
            try
            {
                object o2 = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null);
                if (o2 != null)
                {
                    string p2 = o2.ToString().Replace('/', '\\');
                    if (Directory.Exists(p2)) return p2;
                }
            }
            catch (Exception dg10342_1) { Diag.Swallow("MainForm.HomeValue.cs:725", dg10342_1); }
            string[] cand = { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"D:\Steam", @"D:\Program Files (x86)\Steam", @"E:\Steam", @"D:\Games\Steam" };
            foreach (string c in cand) if (Directory.Exists(c)) return c;
            return null;
        }

        List<string> SteamLibraries(string root)
        {
            List<string> libs = new List<string>();
            string lf = Path.Combine(root, "steamapps\\libraryfolders.vdf");
            if (File.Exists(lf))
            {
                try
                {
                    string txt = File.ReadAllText(lf, Encoding.UTF8);
                    System.Text.RegularExpressions.MatchCollection ms =
                        System.Text.RegularExpressions.Regex.Matches(txt, "\"path\"\\s+\"([^\"]+)\"");
                    foreach (System.Text.RegularExpressions.Match m in ms)
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p) && !libs.Contains(p)) libs.Add(p);
                    }
                }
                catch (Exception dg10365_1) { Diag.Swallow("MainForm.HomeValue.cs:748", dg10365_1); }
            }
            if (!libs.Contains(root)) libs.Insert(0, root);
            return libs;
        }

        Dictionary<string, string> ValParseAcf(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            string txt = "";
            try { txt = File.ReadAllText(path, Encoding.UTF8); }
            catch { return d; }
            System.Text.RegularExpressions.MatchCollection ms =
                System.Text.RegularExpressions.Regex.Matches(txt, "\"([^\"]+)\"\\s+\"([^\"]*)\"");
            foreach (System.Text.RegularExpressions.Match m in ms)
            {
                string k = m.Groups[1].Value.ToLower();
                if (!d.ContainsKey(k)) d[k] = m.Groups[2].Value;
            }
            return d;
        }

        string ValFmtSize(long b)
        {
            if (b <= 0) return "--";
            double g = b / 1073741824.0;
            if (g >= 1) return g.ToString("0.0") + " GB";
            return (b / 1048576.0).ToString("0") + " MB";
        }

        string ValPriceCache()
        {
            return Path.Combine(BASE_DIR, "steam_price_cache.txt");
        }

        // 价格缓存：appid|name|type|price（每秒一次扫描后复用，避免重复请求）
        void ValCacheLoad(Dictionary<int, string[]> cache)
        {
            try
            {
                string p = ValPriceCache();
                if (!File.Exists(p)) return;
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                foreach (string ln in lines)
                {
                    if (ln == null || ln.Length == 0) continue;
                    string[] parts = ln.Split('|');
                    if (parts.Length < 4) continue;
                    int id = 0;
                    if (!int.TryParse(parts[0], out id)) continue;
                    cache[id] = new string[] { parts[1], parts[2], parts[3] };
                }
            }
            catch (Exception dg10418_1) { Diag.Swallow("MainForm.HomeValue.cs:801", dg10418_1); }
        }

        void ValCacheSave(Dictionary<int, string[]> cache)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<int, string[]> kv in cache)
                    sb.AppendLine(kv.Key + "|" + kv.Value[0] + "|" + kv.Value[1] + "|" + kv.Value[2]);
                File.WriteAllText(ValPriceCache(), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception dg10430_1) { Diag.Swallow("MainForm.HomeValue.cs:813", dg10430_1); }
        }

        bool ValQueryApp(int appid, out string name, out string type, out double price)
        {
            name = null;
            type = "";
            price = -1;
            string url = "https://store.steampowered.com/api/appdetails?appids=" + appid + "&cc=cn&l=schinese";
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                catch (Exception dg10442_1) { Diag.Swallow("MainForm.HomeValue.cs:825", dg10442_1); }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Headers.Add("Accept-Encoding", "gzip, deflate");
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    if (string.IsNullOrEmpty(json) || json.IndexOf("\"success\":true") < 0) return false;
                    System.Text.RegularExpressions.Match mn =
                        System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                    if (mn.Success) name = mn.Groups[1].Value;
                    System.Text.RegularExpressions.Match mt =
                        System.Text.RegularExpressions.Regex.Match(json, "\"type\"\\s*:\\s*\"(\\w+)\"");
                    if (mt.Success) type = mt.Groups[1].Value;
                    System.Text.RegularExpressions.Match mp =
                        System.Text.RegularExpressions.Regex.Match(json, "\"initial\"\\s*:\\s*(\\d+)");
                    if (!mp.Success)
                        mp = System.Text.RegularExpressions.Regex.Match(json, "\"final\"\\s*:\\s*(\\d+)");
                    if (mp.Success)
                    {
                        try { price = double.Parse(mp.Groups[1].Value) / 100.0; }
                        catch { price = 0; }
                    }
                    else price = 0;
                    return name != null && name.Length > 0;
                }
            }
            catch { return false; }
        }

        string ValUserConfigPath(string root)
        {
            try
            {
                string ud = Path.Combine(root, "userdata");
                if (!Directory.Exists(ud)) return null;
                string[] dirs = Directory.GetDirectories(ud);
                string best = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (string d in dirs)
                {
                    string lc = Path.Combine(d, "config\\localconfig.vdf");
                    if (!File.Exists(lc)) continue;
                    DateTime t = File.GetLastWriteTime(lc);
                    if (t > bestTime) { bestTime = t; best = lc; }
                }
                return best;
            }
            catch { return null; }
        }

        List<int> ValAppIdsFromLocalConfig(string lcPath)
        {
            List<int> ids = new List<int>();
            if (lcPath == null || !File.Exists(lcPath)) return ids;
            string txt = "";
            try { txt = File.ReadAllText(lcPath, Encoding.UTF8); }
            catch { return ids; }
            // localconfig 里有多处 "apps" 键，取能捞出最多 app 块的那一段
            List<int> bestIds = new List<int>();
            int p = -1;
            while ((p = txt.IndexOf("\"apps\"", p + 1)) >= 0)
            {
                string seg = txt.Substring(p);
                List<int> cur = new List<int>();
                System.Text.RegularExpressions.MatchCollection ms =
                    System.Text.RegularExpressions.Regex.Matches(seg, "\"(\\d{3,7})\"\\s*\\r?\\n\\s*\\{");
                foreach (System.Text.RegularExpressions.Match m in ms)
                {
                    int v = 0;
                    if (int.TryParse(m.Groups[1].Value, out v) && v > 0 && !cur.Contains(v)) cur.Add(v);
                }
                if (cur.Count > bestIds.Count) bestIds = cur;
            }
            return bestIds;
        }

        void ValAddRow(ValRow r)
        {
            ValUi(delegate ()
            {
                if (lvValue == null) return;
                ListViewItem it = new ListViewItem(r.Name);
                it.SubItems.Add(r.PriceText == null ? "--" : r.PriceText);
                it.SubItems.Add(r.SizeText == null ? "--" : r.SizeText);
                it.SubItems.Add(r.AppId.ToString());
                lvValue.Items.Add(it);
                if (lvValue.Items.Count > 0) lvValue.Items[lvValue.Items.Count - 1].EnsureVisible();
            });
        }

        Dictionary<string, string> ValLoadCache()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            try
            {
                string cp = ValPriceCache();
                if (!File.Exists(cp)) return d;
                foreach (string ln in File.ReadAllLines(cp, Encoding.UTF8))
                {
                    string[] p = ln.Split('|');
                    if (p.Length >= 5 && p[0].Trim().Length > 0) d[p[0].Trim()] = ln.Trim();
                }
            }
            catch (Exception dg10550_1) { Diag.Swallow("MainForm.HomeValue.cs:933", dg10550_1); }
            return d;
        }

        // 查一款游戏国区真实价，返回 "appid|final|fmt|disc|free"（free=1 表示免费游戏）
        // 预检：store.steampowered.com 是否可达（该域名在部分网络下不可直连）
        static bool SteamStoreReachable()
        {
            try
            {
                System.Net.Sockets.TcpClient c = new System.Net.Sockets.TcpClient();
                IAsyncResult ar = c.BeginConnect("store.steampowered.com", 443, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(4000, false);
                if (ok) c.EndConnect(ar);
                c.Close();
                return ok;
            }
            catch { return false; }
        }

        string ValQueryOne(string appid)
        {
            try
            {
                string url = "https://store.steampowered.com/api/appdetails?appids=" + appid
                    + "&cc=cn&filters=price_overview&l=schinese";
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.UserAgent = "Mozilla/5.0";
                using (System.Net.WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string js = sr.ReadToEnd();
                    System.Text.RegularExpressions.Match mf = System.Text.RegularExpressions.Regex.Match(js, "\"final\"\\s*:\\s*(\\d+)");
                    System.Text.RegularExpressions.Match mff = System.Text.RegularExpressions.Regex.Match(js, "\"final_formatted\"\\s*:\\s*\"([^\"]+)\"");
                    System.Text.RegularExpressions.Match md = System.Text.RegularExpressions.Regex.Match(js, "\"discount_percent\"\\s*:\\s*(\\d+)");
                    if (!mf.Success) return appid + "|0|-|0|1";
                    return appid + "|" + mf.Groups[1].Value + "|" + (mff.Success ? mff.Groups[1].Value : "-")
                        + "|" + (md.Success ? md.Groups[1].Value : "0") + "|0";
                }
            }
            catch { return appid + "|0|-|0|0"; }
        }

        // 全库真实价：复用列表里已有的 AppID 列，4 路并发 + 本地缓存
        void ValRealPrices(bool forceRefresh)
        {
            if (vRealBusy) return;
            if (!SteamStoreReachable())
            {
                MessageBox.Show(T("连不上 Steam 商店（store.steampowered.com），查价必须能访问该域名。请开启加速器 / 代理后再试。", "Cannot reach store.steampowered.com. A proxy or accelerator is required."), T("网络不通", "Network"));
                return;
            }
            if (lvValue.Items.Count == 0)
            {
                MessageBox.Show(this, T("请先点「扫描账号」拿到游戏列表。", "Scan the library first."),
                    T("Steam 价值", "Steam Value"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Dictionary<string, string> cache = ValLoadCache();
            List<string> todo = new List<string>();
            List<ListViewItem> todoItems = new List<ListViewItem>();
            foreach (ListViewItem it in lvValue.Items)
            {
                string a = it.SubItems[3].Text.Trim();
                if (a.Length == 0 || a == "-") continue;
                string ln = cache.ContainsKey(a) ? cache[a] : null;
                if (ln != null && !forceRefresh)
                {
                    string[] p0 = ln.Split('|');
                    if (p0[4] == "1") it.SubItems[1].Text = T("免费", "Free");
                    else if (p0[1] == "0") it.SubItems[1].Text = T("未知", "Unknown");
                    else it.SubItems[1].Text = p0[2] + (p0[3] != "0" ? "  (-" + p0[3] + "%)" : "");
                    continue;
                }
                todo.Add(a);
                todoItems.Add(it);
            }

            vRealBusy = true;
            btnValReal.Enabled = false;
            if (btnValScan != null) btnValScan.Enabled = false;
            int total = lvValue.Items.Count;
            if (todo.Count == 0)
            {
                lblValPath.Text = T("全部命中本地缓存，共 ", "All cached, ") + total + T(" 款，无需联网", " games");
                lblValPath.ForeColor = C_ON;
                vRealBusy = false;
                btnValReal.Enabled = true;
                if (btnValScan != null) btnValScan.Enabled = true;
                return;
            }
            lblValPath.Text = T("查真实价：待查 ", "Pricing: ") + todo.Count + T(" / 共 ", " / of ") + total
                + T(" 款（4 路并发，约 ", " (4 threads, ~") + ((todo.Count * 3) / 4) + T(" 秒）", "s)");
            lblValPath.ForeColor = UiKit.A(240, 160, 90);

            Thread th = new Thread(delegate()
            {
                int done = 0;
                int nthreads = 4;
                int chunk = (todo.Count + nthreads - 1) / nthreads;
                Dictionary<string, string> fresh = new Dictionary<string, string>();
                List<Thread> ws = new List<Thread>();
                for (int ti = 0; ti < nthreads; ti++)
                {
                    int start = ti * chunk;
                    int end = Math.Min(todo.Count, start + chunk);
                    if (start >= end) continue;
                    int si = start, ei = end;
                    Thread w = new Thread(delegate()
                    {
                        for (int i = si; i < ei; i++)
                        {
                            string line = ValQueryOne(todo[i]);
                            string[] p = line.Split('|');
                            ListViewItem it = todoItems[i];
                            lock (fresh) { fresh[todo[i]] = line; done++; }
                            this.BeginInvoke((Action)delegate
                            {
                                if (p[4] == "1") it.SubItems[1].Text = T("免费", "Free");
                                else if (p[1] == "0") it.SubItems[1].Text = T("未知", "Unknown");
                                else it.SubItems[1].Text = p[2] + (p[3] != "0" ? "  (-" + p[3] + "%)" : "");
                                lblValPath.Text = T("查价中 ", "Pricing ") + done + " / " + todo.Count;
                            });
                        }
                    });
                    w.IsBackground = true;
                    ws.Add(w);
                }
                foreach (Thread w in ws) w.Start();
                foreach (Thread w in ws) w.Join();

                // 合并写缓存
                try
                {
                    foreach (string k in fresh.Keys) cache[k] = fresh[k];
                    StringBuilder sb = new StringBuilder();
                    foreach (string k in cache.Keys) sb.Append(cache[k] + "\r\n");
                    File.WriteAllText(ValPriceCache(), sb.ToString(), Encoding.UTF8);
                }
                catch (Exception dg10691_1) { Diag.Swallow("MainForm.HomeValue.cs:1074", dg10691_1); }

                int ok = 0, free = 0, unk = 0;
                foreach (string k in fresh.Keys)
                {
                    string[] p = fresh[k].Split('|');
                    if (p[4] == "1") free++;
                    else if (p[1] == "0") unk++;
                    else ok++;
                }
                this.BeginInvoke((Action)delegate
                {
                    vRealBusy = false;
                    btnValReal.Enabled = true;
                    if (btnValScan != null) btnValScan.Enabled = true;
                    lblValPath.Text = T("真实价完成：有价 ", "Done: priced ") + ok + T(" 款 / 免费 ", " / free ") + free
                        + T(" 款 / 未知 ", " / unknown ") + unk + T(" 款，缓存已存 " + Path.GetFileName(ValPriceCache()), "  cached");
                    lblValPath.ForeColor = C_ON;
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>把 Steam 价值页当前清单导出为 CSV（Excel 可直接打开）。</summary>
        void ValExportCsv()
        {
            try
            {
                if (lvValue == null || lvValue.Items.Count == 0)
                {
                    MessageBox.Show(T("还没有清单，先点「扫描账号」。", "No list yet — click Scan Library first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("\"小工具箱 · Steam 账号价值清单\"");
                sb.AppendLine("\"导出时间\",\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"");
                sb.AppendLine("\"游戏数\",\"" + (lblValGames == null ? "" : lblValGames.Text) + "\"");
                sb.AppendLine("\"估算总价值\",\"" + (lblValWorth == null ? "" : lblValWorth.Text) + "\"");
                sb.AppendLine("\"采样均价\",\"" + (lblValAvg == null ? "" : lblValAvg.Text) + "\"");
                sb.AppendLine("\"库占容量\",\"" + (lblValSize == null ? "" : lblValSize.Text) + "\"");
                sb.AppendLine("");
                sb.AppendLine("\"游戏\",\"商店原价\",\"占用\",\"AppID\"");
                for (int i = 0; i < lvValue.Items.Count; i++)
                {
                    ListViewItem it = lvValue.Items[i];
                    string l = "";
                    for (int c = 0; c < it.SubItems.Count; c++)
                    {
                        string v = it.SubItems[c].Text == null ? "" : it.SubItems[c].Text;
                        if (v.Length > 200) v = v.Substring(0, 200);
                        l += (c == 0 ? "" : ",") + "\"" + v.Replace("\"", "\"\"") + "\"";
                    }
                    sb.AppendLine(l);
                }
                string path = Path.Combine(BASE_DIR, "Steam价值_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Log("[Steam价值] 清单已导出：" + path);
                MessageBox.Show(T("已导出 ", "Saved ") + lvValue.Items.Count + T(" 款：", " games: ") + path, T("完成", "Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select," + path); } catch (Exception dg10750_1) { Diag.Swallow("MainForm.HomeValue.cs:1133", dg10750_1); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("导出失败：", "Export failed: ") + ex.Message, T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ValStartScan()
        {
            if (valBusy) return;
            string root = SteamRoot();
            if (root == null)
            {
                ValSetStatus(T("没有找到 Steam 安装目录，请确认本机已安装 Steam。", "Steam folder not found on this PC."), UiKit.A(255, 120, 120));
                return;
            }
            valBusy = true;
            btnValScan.Enabled = false;
            lvValue.Items.Clear();
            lblValDetail.Text = "";
            lblValGames.Text = "--";
            lblValWorth.Text = "--";
            lblValAvg.Text = "--";
            lblValSize.Text = "--";
            lblValPath.Text = root;
            ValSetStatus(T("正在定位 Steam 库…", "Locating Steam libraries..."), C_SUB);
            Thread th = new Thread(delegate ()
            {
                try { ValScanWorker(root); }
                catch (Exception ex)
                {
                    ValSetStatus(T("扫描失败：", "Scan failed: ") + ex.Message, UiKit.A(255, 120, 120));
                    ValFinish();
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void ValScanWorker(string root)
        {
            List<string> libs = SteamLibraries(root);
            ValSetStatus(T("正在读取游戏库清单…", "Reading library manifests..."), C_SUB);

            Dictionary<int, ValRow> map = new Dictionary<int, ValRow>();
            foreach (string lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                string[] acfs = null;
                try { acfs = Directory.GetFiles(apps, "appmanifest_*.acf"); }
                catch { continue; }
                foreach (string acf in acfs)
                {
                    try
                    {
                        Dictionary<string, string> d = ValParseAcf(acf);
                        if (!d.ContainsKey("appid")) continue;
                        int appid = 0;
                        if (!int.TryParse(d["appid"], out appid) || appid <= 0) continue;
                        if (appid == 228980 || appid == 228990 || appid == 250820 || appid == 1070560) continue;
                        ValRow r = new ValRow();
                        r.AppId = appid;
                        r.Name = d.ContainsKey("name") && d["name"].Length > 0 ? d["name"] : ("App " + appid);
                        long sz = 0;
                        if (d.ContainsKey("sizeondisk")) long.TryParse(d["sizeondisk"], out sz);
                        r.TotalBytes = sz;
                        r.SizeText = ValFmtSize(sz);
                        map[appid] = r;
                    }
                    catch (Exception dg10821_1) { Diag.Swallow("MainForm.HomeValue.cs:1204", dg10821_1); }
                }
            }

            // 账号里玩过的 appid（localconfig），补上未安装的
            List<int> allIds = new List<int>();
            foreach (KeyValuePair<int, ValRow> kv in map) allIds.Add(kv.Key);
            string lcPath = ValUserConfigPath(root);
            int extraCount = 0;
            if (lcPath != null)
            {
                List<int> ids = ValAppIdsFromLocalConfig(lcPath);
                foreach (int id in ids)
                {
                    if (map.ContainsKey(id)) continue;
                    ValRow r = new ValRow();
                    r.AppId = id;
                    r.Name = "";
                    r.SizeText = T("未安装", "not installed");
                    map[id] = r;
                    allIds.Add(id);
                    extraCount++;
                }
            }

            if (allIds.Count == 0)
            {
                ValSetStatus(T("没有读到任何 Steam 游戏数据。", "No Steam game data found."), UiKit.A(240, 160, 90));
                ValFinish();
                return;
            }

            Dictionary<int, string[]> cache = new Dictionary<int, string[]>();
            ValCacheLoad(cache);

            // Steam 商店接口单次请求 3-5 秒且不支持批量，全量查价不现实：
            // 先查缓存命中的，再采样（已安装优先）补足到上限，用样本均价推算整个账号
            int sampleLimit = 100;
            List<int> sample = new List<int>();
            foreach (KeyValuePair<int, ValRow> kv in map)
            {
                if (kv.Value.TotalBytes > 0 && sample.Count < sampleLimit) sample.Add(kv.Key);
            }
            foreach (int id in allIds)
            {
                if (sample.Count >= sampleLimit) break;
                if (!sample.Contains(id)) sample.Add(id);
            }
            List<int> workIds = new List<int>();
            foreach (int id in allIds)
            {
                string[] cv = null;
                if (cache.TryGetValue(id, out cv)) workIds.Add(id);
            }
            foreach (int id in sample)
            {
                if (!workIds.Contains(id)) workIds.Add(id);
            }

            int total = workIds.Count;
            int cursor = -1;
            int doneCount = 0;
            double sum = 0;
            int known = 0, freeCnt = 0, failCnt = 0;
            int installedCount = 0;
            long installedBytes = 0;
            foreach (KeyValuePair<int, ValRow> kv in map)
            {
                if (kv.Value.TotalBytes > 0) { installedCount++; installedBytes += kv.Value.TotalBytes; }
            }
            object gate = new object();
            List<ValRow> processed = new List<ValRow>();
            int par = 6;
            List<Thread> ts = new List<Thread>();
            for (int w = 0; w < par; w++)
            {
                Thread th = new Thread(delegate ()
                {
                    while (true)
                    {
                        int idx;
                        lock (gate) { cursor++; idx = cursor; if (idx >= total) return; }
                        int appid = workIds[idx];
                        ValRow row;
                        lock (gate) { row = map[appid]; }

                        string nm = null, tp = "";
                        double price = -1;
                        bool ok = false;
                        string[] cv;
                        if (cache.TryGetValue(appid, out cv))
                        {
                            nm = cv[0];
                            tp = cv[1];
                            try { price = double.Parse(cv[2]); } catch { price = 0; }
                            ok = nm != null && nm.Length > 0;
                        }
                        else
                        {
                            ok = ValQueryApp(appid, out nm, out tp, out price);
                            if (!ok)
                            {
                                // 网络抖动重试一次，避免个别游戏被判成查不到
                                Thread.Sleep(300);
                                string n2 = null, t2 = "";
                                double p2 = -1;
                                if (ValQueryApp(appid, out n2, out t2, out p2))
                                {
                                    nm = n2;
                                    tp = t2;
                                    price = p2;
                                    ok = true;
                                }
                            }
                            if (ok)
                            {
                                lock (gate)
                                {
                                    cache[appid] = new string[] { nm, tp, price.ToString("0.00") };
                                }
                            }
                        }

                        if (!ok)
                        {
                            lock (gate)
                            {
                                failCnt++;
                                doneCount++;
                            }
                            if (row.Name.Length > 0 && !row.Name.StartsWith("App ")) ValAddRow(row);
                            continue;
                        }
                        // 只统计游戏本体：DLC、影视、硬件等不计入资产
                        if (tp.Length > 0 && tp != "game" && tp != "demo")
                        {
                            lock (gate) { doneCount++; }
                            continue;
                        }
                        if (nm != null && nm.Length > 0 && (row.Name.Length == 0 || row.Name.StartsWith("App "))) row.Name = nm;
                        lock (gate)
                        {
                            if (price > 0)
                            {
                                sum += price;
                                known++;
                                row.PriceText = "¥ " + price.ToString("0.00");
                            }
                            else
                            {
                                freeCnt++;
                                row.PriceText = T("免费", "Free");
                            }
                            processed.Add(row);
                            doneCount++;
                        }
                        ValAddRow(row);
                    }
                });
                th.IsBackground = true;
                ts.Add(th);
            }
            foreach (Thread th in ts) th.Start();
            while (true)
            {
                bool alive = false;
                foreach (Thread th in ts) if (th.IsAlive) alive = true;
                int d;
                lock (gate) d = doneCount;
                ValSetStatus(T("查询中 " + d + " / " + total + "（并发 8，已缓存的可秒出）…",
                    "Querying " + d + "/" + total + " ..."), UiKit.A(240, 160, 90));
                if (!alive) break;
                Thread.Sleep(700);
            }
            ValCacheSave(cache);

            double avg = known > 0 ? sum / known : 0;
            int cnt;
            lock (gate) cnt = processed.Count;

            // 按原价从高到低重排，方便看最值钱的游戏
            ValUi(delegate ()
            {
                lvValue.BeginUpdate();
                lvValue.Items.Clear();
                List<ValRow> sorted = new List<ValRow>();
                lock (gate) { foreach (ValRow r in processed) sorted.Add(r); }
                sorted.Sort(delegate (ValRow a, ValRow b)
                {
                    double pa = ValPriceOf(a), pb = ValPriceOf(b);
                    if (pb != pa) return pb.CompareTo(pa);
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                foreach (ValRow r in sorted)
                {
                    ListViewItem it = new ListViewItem(r.Name);
                    it.SubItems.Add(r.PriceText == null ? "--" : r.PriceText);
                    it.SubItems.Add(r.SizeText == null ? "--" : r.SizeText);
                    it.SubItems.Add(r.AppId.ToString());
                    lvValue.Items.Add(it);
                }
                lvValue.EndUpdate();

                lblValGames.Text = map.Count.ToString();
                lblValWorth.Text = known > 0 ? ("¥ " + (avg * map.Count).ToString("0")) : T("未知", "n/a");
                lblValAvg.Text = known > 0 ? ("¥ " + avg.ToString("0.0")) : "--";
                lblValSize.Text = ValFmtSize(installedBytes);
                StringBuilder sb = new StringBuilder();
                sb.Append(T("账号共 ", "Account: ") + map.Count + T(" 条游戏记录，已安装 ", " records, installed ") + installedCount + T(" 款。", "."));
                sb.AppendLine();
                sb.Append(T("采样 ", "sampled ") + cnt + T(" 款：有价 ", ", priced ") + known + T(" · 免费 ", ", free ") + freeCnt
                    + T(" · 未查到 ", ", unpriced ") + failCnt + T("，采样均价 ", ", avg ") 
                    + (known > 0 ? "¥ " + avg.ToString("0.0") : "--") + T("。", "."));
                sb.AppendLine();
                if (known == 0)
                    sb.Append(T("提示：商店接口当前连不上，只能统计数量；稍后再点一次扫描通常可恢复。",
                        "Tip: store API unreachable, only counts available."));
                else
                    sb.Append(T("按采样均价推算，全账号游戏资产约 ", "Estimated account value: ") + "¥ " + (avg * map.Count).ToString("0")
                        + T("（估算值，反映账号购买游戏的原价体量）。", " (estimate)."));
                lblValDetail.Text = sb.ToString();
                ValSetStatus(T("扫描完成：采样 " + cnt + " 款，账号 " + map.Count + " 条记录。", "Done: " + cnt + " sampled, " + map.Count + " records."), C_ON);
            });
            ValFinish();

            if (valAutoTest)
            {
                StringBuilder rep = new StringBuilder();
                rep.AppendLine("GAMES=" + cnt + " PRICED=" + known + " FREE=" + freeCnt + " UNPRICED=" + failCnt);
                rep.AppendLine("TOTAL=" + sum.ToString("0.00") + " AVG=" + avg.ToString("0.00") + " INSTALLED_BYTES=" + installedBytes);
                rep.AppendLine("LIB_ENTRIES=" + map.Count + " EXTRA=" + extraCount);
                for (int i = 0; i < lvValue.Items.Count && i < 12; i++)
                {
                    ListViewItem it = lvValue.Items[i];
                    rep.AppendLine(it.Text + " | " + it.SubItems[1].Text + " | " + it.SubItems[2].Text + " | " + it.SubItems[3].Text);
                }
                try { File.WriteAllText(Path.Combine(BASE_DIR, "_val_test.txt"), rep.ToString(), Encoding.UTF8); }
                catch (Exception dg11058_1) { Diag.Swallow("MainForm.HomeValue.cs:1441", dg11058_1); }
            }
        }

        double ValPriceOf(ValRow r)
        {
            if (r == null || r.PriceText == null) return 0;
            string t = r.PriceText;
            int i = t.IndexOf('¥');
            if (i < 0) return 0;
            double v = 0;
            if (double.TryParse(t.Substring(i + 1).Trim(), out v)) return v;
            return 0;
        }

        void ValAutoTestRun()
        {
            valAutoTest = true;
            SelectAction(Array.IndexOf(SLOT, "val"));
            ValStartScan();
        }

    }
}