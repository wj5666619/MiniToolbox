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
    // v19.8：从 MainForm.cs 原样迁出（界面构建与页面切换），仅为拆分文件，逻辑未改
    partial class MainForm
    {
        // ================= 界面 =================
        void InitUI()
        {
            try { UiType.SetLanguage(langEn); } catch (Exception dg1383_1) { Diag.Swallow("MainForm.Ui.cs:29", dg1383_1); }
            Text = "小工具箱";
            // 宽度必须给足 960，C盘清理页右侧两张卡片才不会贴边被裁；
            // 高度给 661 是历史版面调好的实际可视高度，多给会在页面底部留一条空带。
            ClientSize = new Size(1120, 815);   // v17.6：加高，容下顶部 Header 与底部状态条
            // 全部页面都按绝对坐标摆放。窗口一旦被拉伸，右侧卡片会被裁掉，
            // 拖拽过程中还会因为绝对布局不重排而留下重绘残影。因此锁成固定尺寸：
            // 不给拖拽边框，也不给最大化按钮。（原先的 MinimumSize 会被当成外框尺寸，
            // 反而把客户区压成 944×661，是右边缘裁切的来源，这里去掉。）
            // v17.9：方案四是整张圆角卡片，没有系统标题栏；尺寸本来固定，去掉边框不影响缩放
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;   // 禁用自动缩放，防止控件位置漂移
            BackColor = C_BG;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;

            // v8.7：窗口内 Ctrl+K 直接唤起指令舱（全局热键依旧是 Ctrl+Alt+Space，二者不冲突）
            KeyPreview = true;
            KeyDown += delegate (object s9, KeyEventArgs e9)
            {
                if (e9.Control && e9.KeyCode == Keys.K)
                {
                    e9.Handled = true;
                    e9.SuppressKeyPress = true;
                    ShowPalette();
                }
            };

            // ---------- 顶部 Header（v17.6 · 方案四 全宽头部）----------
            header = new HdrPanel();
            header.BackColor = C_SIDE;
            // v17.6：Header 是自绘渐变 + 一堆子控件叠着，不开双缓冲的话重绘时
            // 父控件的渐变会"擦"过子控件，右侧那颗青绿按钮就只剩一条斜边
            header.Height = 116;   // 逻辑高度；Dock=Top 由窗口自己按 DPI 换算
            header.Paint += delegate (object hs, PaintEventArgs he)
            {
                try
                {
                    Rectangle hr = header.ClientRectangle;
                    if (UiKit.Paper)
                    {
                        // v19.16 纸感：暖墨抬头 + 黄铜细线，去掉刺眼的荧光黄大粗边
                        using (LinearGradientBrush pb = new LinearGradientBrush(hr,
                            Color.FromArgb(255, 34, 31, 26), Color.FromArgb(255, 21, 20, 17), 90f))
                            he.Graphics.FillRectangle(pb, hr);
                        using (Pen acp = new Pen(Color.FromArgb(255, 214, 176, 86), 2f))
                            he.Graphics.DrawLine(acp, 0, hr.Height - 2, hr.Width, hr.Height - 2);
                        using (Pen vr = new Pen(Color.FromArgb(120, 214, 176, 86), 2f))
                            he.Graphics.DrawLine(vr, 181, 24, 181, hr.Height - 22);
                        if (hName != null) hName.ForeColor = Color.FromArgb(255, 248, 242, 228);
                        if (hCode != null) hCode.ForeColor = Color.FromArgb(255, 220, 184, 96);
                        if (hDesc != null) hDesc.ForeColor = Color.FromArgb(198, 236, 228, 210);
                        return;
                    }
                    // v19.16 深色皮肤：收掉右上角三层多边形，改成安静的深青玻璃抬头
                    using (LinearGradientBrush hb = new LinearGradientBrush(hr,
                        UiKit.A(24, 43, 54), UiKit.A(9, 14, 21), 18f))
                        he.Graphics.FillRectangle(hb, hr);
                    using (LinearGradientBrush glow = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(1, hr.Width), 96),
                        UiKit.AA(0, 45, 212, 191), UiKit.AA(0, 45, 212, 191), 0f))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Colors = new Color[] { UiKit.AA(0, 45, 212, 191), UiKit.AA(18, 45, 212, 191), UiKit.AA(0, 45, 212, 191) };
                        cb.Positions = new float[] { 0f, 0.55f, 1f };
                        glow.InterpolationColors = cb;
                        he.Graphics.FillRectangle(glow, 0, 0, hr.Width, Math.Min(96, hr.Height));
                    }
                    using (Pen top = new Pen(UiKit.AA(70, 120, 230, 220), 1f))
                        he.Graphics.DrawLine(top, 0, 0, hr.Width, 0);
                    using (Pen vr = new Pen(UiKit.AA(58, 45, 212, 191), 1f))
                        he.Graphics.DrawLine(vr, 181, 24, 181, hr.Height - 22);
                    using (Pen ln = new Pen(UiKit.AA(90, 45, 212, 191), 1F))
                        he.Graphics.DrawLine(ln, 0, hr.Height - 1, hr.Width, hr.Height - 1);
                    if (hName != null) hName.ForeColor = Color.White;
                    if (hCode != null) hCode.ForeColor = UiKit.A(45, 212, 191);
                    if (hDesc != null) hDesc.ForeColor = UiKit.A(142, 162, 181);
                }
                catch (Exception dg1478_1) { Diag.Swallow("MainForm.Ui.cs:124", dg1478_1); }
            };
            // header 位置由 LayoutShell 统一分配（Dock 在缩放后不可靠）

            // Header 右侧信息区（v17.6 · 方案四）：页面名 + 编号 + 一句描述 + 三个胶囊按钮
            hName = new Label();
            hName.Text = T("工作台", "Dashboard");
            hName.ForeColor = UiKit.Txt(Color.White);
            hName.Font = new Font("Microsoft YaHei UI", 26F, FontStyle.Bold);
            hName.AutoSize = false;
            hName.BackColor = Color.Transparent;
            hName.Size = new Size(420, 46);
            hName.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(hName);

            hCode = new Label();
            hCode.Text = "00 / DASHBOARD";
            hCode.ForeColor = UiKit.A(45, 212, 191);
            hCode.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            hCode.AutoSize = true;
            hCode.BackColor = Color.Transparent;
            header.Controls.Add(hCode);

            hDesc = new Label();
            hDesc.Text = "";
            hDesc.ForeColor = UiKit.A(139, 156, 176);
            hDesc.Font = new Font("Microsoft YaHei UI", 9F);
            hDesc.AutoSize = true;
            hDesc.BackColor = Color.Transparent;
            header.Controls.Add(hDesc);

            // v17.6：Header 第二行的「更新公告」与状态栏右下角那颗完全重复，按用户要求只留最下面那颗

            hbBg = MakeBtn(T("自定义背景", "Wallpaper"), UiKit.A(24, 31, 40), UiKit.A(214, 224, 236), 0, 0, 100, 32);
            hbBg.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            ((RoundBtn)hbBg).Ghost = true;
            ((RoundBtn)hbBg).NoGloss = true;
            hbBg.Click += (s, e) => ShowBgDialog();
            header.Controls.Add(hbBg);

            hbCmd = MakeBtn(T("命令面板", "Command"), UiKit.A(24, 31, 40), UiKit.A(214, 224, 236), 0, 0, 92, 32);
            hbCmd.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            ((RoundBtn)hbCmd).Ghost = true;
            ((RoundBtn)hbCmd).NoGloss = true;
            hbCmd.Click += (s, e) => ShowPalette();

            // v19.14 控制中心：任务 / 清理 / 网络 / 策略 / 音效 / 插件 / 统计
            hbCenter = MakeBtn(T("控制中心", "Control"),
                UiKit.Paper ? Color.FromArgb(214, 176, 86) : UiKit.A(18, 92, 88),
                UiKit.Paper ? UiKit.Ink : Color.White, 0, 0, 96, 32);
            hbCenter.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            ((RoundBtn)hbCenter).NoGloss = true;
            hbCenter.Click += (s, e) => OpenControlCenter();

            // v17.6：方案四草稿把「开始任务 / 取消任务 / 立即执行 / EN」挂在 Header 右侧，
            // 这里做成代理按钮，点下去转发给页面里真正的三颗，逻辑一行不动。
            // v17.6：这颗实心青绿不再走 RoundBtn 自绘（它的顶端高光/底端阴影在亮色上会把白字切断），
            // 改用系统 Flat 按钮 + 自身圆角由 header 的柔和描边兜住，文字保证一行干净可读
            // v17.6：这颗在 Header 里试过 RoundBtn 自绘、也试过系统 Flat 按钮，都会画成一条斜线/半截，
            // 最后用 Label 兜住：实心青绿底 + 居中白字 + 手型光标，点击转发给页内「开始任务」，一定能画出来
            // v17.6：z 序问题已解决，这颗改回 RoundBtn，才能跟取消 / 立即执行 / EN 一样是
            // 方案四草稿里那种圆角胶囊；Label 画出来是直角方块，四角还会顶到 Header 渐变上
            Button lStart = MakeBtn(T("▶ 开始任务", "▶ Start"), UiKit.A(20, 122, 110), Color.White, 0, 0, 150, 34);
            hbStart1 = lStart;
            hbStart1.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            hbStart1.Click += (s, e) => { try { btnStart.PerformClick(); } catch (Exception dg1531_1) { Diag.Swallow("MainForm.Ui.cs:177", dg1531_1); } };
            header.Controls.Add(hbStart1);

            hbCancel1 = MakeBtn(T("取消任务", "Cancel"), UiKit.A(26, 36, 48), Color.White, 0, 0, 112, 34);
            hbCancel1.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            ((RoundBtn)hbCancel1).Ghost = true;
            ((RoundBtn)hbCancel1).NoGloss = true;
            hbCancel1.Click += (s, e) => { try { btnCancel.PerformClick(); } catch (Exception dg1536_1) { Diag.Swallow("MainForm.Ui.cs:182", dg1536_1); } };
            header.Controls.Add(hbCancel1);

            hbImm1 = MakeBtn(T("⚡ 立即执行", "⚡ Run"), UiKit.A(196, 61, 43), Color.White, 0, 0, 118, 34);
            hbImm1.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            hbImm1.Click += (s, e) => { try { btnImm.PerformClick(); } catch (Exception dg1541_1) { Diag.Swallow("MainForm.Ui.cs:187", dg1541_1); } };
            header.Controls.Add(hbImm1);

            hbEn = MakeBtn(Lang.SHORT[Lang.Id] + " ⌄", UiKit.A(30, 40, 52), UiKit.A(214, 224, 238), 0, 0, 62, 32);
            hbEn.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            ((RoundBtn)hbEn).Ghost = true;
            ((RoundBtn)hbEn).NoGloss = true;
            hbEn.Click += (s, e) => { try { ShowLangMenu(hbEn); } catch (Exception dg1546_1) { Diag.Swallow("MainForm.Ui.cs:192", dg1546_1); } };
            header.Controls.Add(hbEn);
            header.Controls.Add(hbCenter);
            header.Controls.Add(hbCmd);
            header.Resize += delegate { HeaderArrange(); };   // Header 尺寸落定后重排一次

            // ---------- 底部状态条（v17.6）----------
            statusbar = new Panel();
            statusbar.BackColor = C_SIDE;
            statusbar.Height = 30;   // 位置由 LayoutShell 统一分配
            statusbar.Paint += delegate (object ss, PaintEventArgs se)
            {
                try
                {
                    if (UiKit.Paper)
                    {
                        using (LinearGradientBrush sb2 = new LinearGradientBrush(statusbar.ClientRectangle,
                            Color.FromArgb(30, 28, 23), Color.FromArgb(17, 16, 14), 90f))
                            se.Graphics.FillRectangle(sb2, statusbar.ClientRectangle);
                        using (Pen ln = new Pen(Color.FromArgb(110, 214, 176, 86), 1F))
                            se.Graphics.DrawLine(ln, 0, 0, statusbar.ClientSize.Width, 0);
                        using (SolidBrush d1 = new SolidBrush(Color.FromArgb(214, 176, 86)))
                            se.Graphics.FillEllipse(d1, 14, 12, 6, 6);
                        return;
                    }
                    using (LinearGradientBrush sb2 = new LinearGradientBrush(statusbar.ClientRectangle,
                        UiKit.A(16, 26, 38), UiKit.A(9, 13, 19), 90f))
                        se.Graphics.FillRectangle(sb2, statusbar.ClientRectangle);
                    using (Pen ln = new Pen(UiKit.AA(40, 45, 212, 191), 1F))
                        se.Graphics.DrawLine(ln, 0, 0, statusbar.ClientSize.Width, 0);
                    using (SolidBrush d1 = new SolidBrush(UiKit.A(45, 212, 191)))
                        se.Graphics.FillEllipse(d1, 14, 12, 6, 6);
                }
                catch (Exception dg1567_1) { Diag.Swallow("MainForm.Ui.cs:213", dg1567_1); }
            };
            Label stVer = new Label();
            stVer.Text = "小工具箱 v" + UiKit.VER;
            stVer.ForeColor = UiKit.Paper ? Color.FromArgb(204, 193, 172) : UiKit.A(120, 134, 152);
            stVer.Font = new Font("Microsoft YaHei UI", 7.5F);
            stVer.Location = new Point(26, 7);
            stVer.AutoSize = true;
            statusbar.Controls.Add(stVer);
            Label stSlogan = new Label();
            stSlogan.Text = T("让电脑使用更简单", "Make the PC simpler");
            stSlogan.ForeColor = UiKit.Paper ? Color.FromArgb(154, 144, 125) : UiKit.A(96, 110, 128);
            stSlogan.Font = new Font("Microsoft YaHei UI", 7.5F);
            stSlogan.AutoSize = true;
            statusbar.Controls.Add(stSlogan);
            // v17.6：右下角这颗原先只是纯文字 Label，写好了 Cursor=Hand 却没绑 Click，
            // 点了没有任何反应。改成 22px 高的青绿胶囊按钮，连到 ShowAbout()，一眼就知道能点。
            RoundBtn stNews = new RoundBtn(UiKit.A(18, 38, 46), UiKit.A(45, 212, 191));
            stNews.Text = T("更新公告", "Changelog");
            stNews.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
            stNews.Size = new Size(84, 22);
            stNews.TabStop = false;
            stNews.NoGloss = true;
            stNews.Cursor = Cursors.Hand;
            stNews.Click += (s, e) => ShowAbout();
            statusbar.Controls.Add(stNews);

            // v19.14 底部常驻入口：任何页面都能开控制中心，不跟页头的主操作抢位置
            RoundBtn stCenter = new RoundBtn(UiKit.A(18, 38, 46), UiKit.A(139, 205, 220));
            stCenter.Text = T("控制中心", "Control");
            stCenter.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
            stCenter.Size = new Size(78, 22);
            stCenter.TabStop = false;
            stCenter.NoGloss = true;
            stCenter.Cursor = Cursors.Hand;
            stCenter.Click += (s, e) => OpenControlCenter();
            statusbar.Controls.Add(stCenter);

            statusbar.Resize += delegate
            {
                try
                {
                    stSlogan.Left = (statusbar.ClientSize.Width - stSlogan.Width) / 2;
                    stSlogan.Top = 7;
                    stNews.Left = statusbar.ClientSize.Width - stNews.Width - 10;
                    stNews.Top = 4;
                    stCenter.Left = stNews.Left - stCenter.Width - 8;
                    stCenter.Top = 4;
                }
                catch (Exception dg1602_1) { Diag.Swallow("MainForm.Ui.cs:248", dg1602_1); }
            };
            // （statusbar 同上，稍后统一添加）

            // ---------- 左侧栏 ----------
            sidebar = new Panel();
            sidebar.BackColor = C_SIDE;
            sidebar.Paint += delegate (object s9, PaintEventArgs e9)
            {
                // v18.7 纸感：侧栏整条做成暖墨立柱，右侧压一道黄铜细线，白字全部保住
                if (UiKit.Paper)
                {
                    using (LinearGradientBrush sbw = new LinearGradientBrush(sidebar.ClientRectangle,
                        Color.FromArgb(255, 37, 34, 28), Color.FromArgb(255, 19, 18, 16), 90f))
                        e9.Graphics.FillRectangle(sbw, sidebar.ClientRectangle);
                    using (Pen acp = new Pen(Color.FromArgb(255, 214, 176, 86), 2f))
                        e9.Graphics.DrawLine(acp, sidebar.ClientSize.Width - 2, 0, sidebar.ClientSize.Width - 2, sidebar.ClientSize.Height);
                    using (Pen gl = new Pen(Color.FromArgb(42, 255, 246, 226), 1f))
                        e9.Graphics.DrawLine(gl, 0, 0, 0, sidebar.ClientSize.Height);
                    Ui2.PaperWhiteText(sidebar);
                    return;
                }
                // v19.16：深色皮肤也给侧栏一层纵向渐变，不再是死板纯色
                using (LinearGradientBrush sbg = new LinearGradientBrush(sidebar.ClientRectangle,
                    UiKit.A(14, 21, 29), UiKit.A(7, 10, 15), 90f))
                    e9.Graphics.FillRectangle(sbg, sidebar.ClientRectangle);
                using (Pen ar = new Pen(UiKit.AA(46, 45, 212, 191), 1f))
                    e9.Graphics.DrawLine(ar, sidebar.ClientSize.Width - 1, 0, sidebar.ClientSize.Width - 1, sidebar.ClientSize.Height);
                // v8.1：Logo 区与导航区之间一条极淡的渐变分隔线，只画在空隙里，不碰任何控件
                try
                {
                    Rectangle rr = new Rectangle(10, 64, sidebar.ClientSize.Width - 20, 1);
                    using (LinearGradientBrush lb = new LinearGradientBrush(rr,
                        UiKit.AA(0, 255, 255, 255), UiKit.AA(54, 172, 196, 232), 0f))
                        e9.Graphics.FillRectangle(lb, rr);
                    // v17.6：底部标语上方一条同样的极淡线，让侧栏上下有收口，视觉不再悬空
                    int bh2 = sidebar.ClientSize.Height - 84;
                    if (bh2 > 200)
                        using (LinearGradientBrush lb3 = new LinearGradientBrush(
                            new Rectangle(14, bh2, Math.Max(8, sidebar.ClientSize.Width - 28), 1),
                            UiKit.AA(0, 255, 255, 255), UiKit.AA(46, 172, 196, 232), 0f))
                            e9.Graphics.FillRectangle(lb3, 14, bh2, Math.Max(8, sidebar.ClientSize.Width - 28), 1);
                }
                catch (Exception dg1636_1) { Diag.Swallow("MainForm.Ui.cs:282", dg1636_1); }
            };
            sidebar.Resize += delegate { sidebar.Invalidate(); };   // 尺寸定下来后重画暖墨立柱，否则只剩创建时的纸白底
            // 位置由 LayoutShell 统一分配
            sidebar.Width = 168;

            Panel sbCard = new Panel();
            sbCard.Size = new Size(148, 66);
            sbCard.Location = new Point(10, 12);
            sbCard.BackColor = UiKit.Paper ? Color.FromArgb(31, 29, 24) : C_MAIN;
            sbCard.Paint += delegate (object cs, PaintEventArgs ce)
            {
                if (UiKit.Paper)
                {
                    // 纸感：品牌区不再套一个方盒子，Logo 与文字直接落在暖墨侧栏上
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(37, 34, 28)))
                        ce.Graphics.FillRectangle(b, new Rectangle(0, 0, sbCard.Width, sbCard.Height));
                    return;
                }
                DrawGlassCard(ce.Graphics, new Rectangle(0, 0, 147, 65), 12,
                    UiKit.A(38, 62, 72), UiKit.A(22, 38, 44));
            };
            sidebar.Controls.Add(sbCard);

            // v17.8：侧栏头部图标按方案四重画 —— 圆形青绿渐变底 + 白色实心闪电，放在文字左侧
            // v17.8：侧栏头部 Logo 用方案四 03 号六边形标
            Label sbLogo = new Label();
            sbLogo.Text = "";
            sbLogo.BackColor = Color.Transparent;
            sbLogo.Location = new Point(9, 13);
            sbLogo.Size = new Size(42, 42);
            sbLogo.Paint += delegate (object logS, PaintEventArgs logE)
            {
                try { UiKit.DrawLogo(logE.Graphics, new Rectangle(2, 2, 38, 38)); }
                catch (Exception dg1662_1) { Diag.Swallow("MainForm.Ui.cs:308", dg1662_1); }
            };
            sbCard.Controls.Add(sbLogo);

            Label sbName = new Label();
            sbName.Text = "小工具箱";
            sbName.ForeColor = UiKit.Txt(Color.White);
            sbName.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            sbName.BackColor = Color.Transparent;
            sbName.AutoSize = true;
            sbName.Location = new Point(56, 14);
            sbCard.Controls.Add(sbName);

            Label sbVer = new Label();
            sbVer.Text = "v" + UiKit.VER;
            sbVer.ForeColor = UiKit.A(120, 140, 160);
            sbVer.Font = new Font("Microsoft YaHei UI", 7F);
            sbVer.BackColor = Color.Transparent;
            sbVer.AutoSize = true;
            sbVer.Location = new Point(57, 34);
            sbCard.Controls.Add(sbVer);

            sbSlogan1 = new Label();
            sbSlogan1.Text = "SMALL TOOLS";
            sbSlogan1.ForeColor = UiKit.A(112, 132, 152);
            sbSlogan1.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
            sbSlogan1.AutoSize = true;
            sbSlogan1.BackColor = Color.Transparent;
            sbSlogan1.Location = new Point(16, 800);
            sidebar.Controls.Add(sbSlogan1);

            sbSlogan2 = new Label();
            sbSlogan2.Text = "BIGGER POSSIBILITIES.";
            sbSlogan2.ForeColor = UiKit.A(78, 94, 112);
            sbSlogan2.Font = new Font("Microsoft YaHei UI", 7.5F);
            sbSlogan2.AutoSize = true;
            sbSlogan2.BackColor = Color.Transparent;
            sbSlogan2.Location = new Point(16, 816);
            sidebar.Controls.Add(sbSlogan2);   // v16.1：加回副标，168 刚好两行

            // v17.9：品牌区不再垫 Panel —— Panel 设透明会退化成系统灰底，改成控件直接压在 Header 渐变上

            Label meAvatar = new Label();
            meAvatar.BackColor = Color.Transparent;
            meAvatar.Location = new Point(12, 11);
            meAvatar.Size = new Size(50, 50);
            meAvatar.Paint += (s, e) =>
            {
                // v17.8：与侧栏同一枚六边形 Logo
                try { UiKit.DrawLogo(e.Graphics, new Rectangle(2, 2, 46, 46)); }
                catch (Exception dg1712_1) { Diag.Swallow("MainForm.Ui.cs:358", dg1712_1); }
            };
            header.Controls.Add(meAvatar);
            Label meVer = new Label();
            meVer.Text = "v" + UiKit.VER + "  ·  SMALL TOOLS";
            meVer.BackColor = Color.Transparent;
            meVer.ForeColor = UiKit.Paper ? Color.FromArgb(190, 196, 186, 166) : UiKit.AA(190, 176, 208, 232);
            meVer.Font = new Font("Microsoft YaHei UI", 7.5F);
            meVer.Location = new Point(13, 93);
            meVer.AutoSize = true;
            header.Controls.Add(meVer);

            // v17.9：无边框后自己画最小化 / 关闭，位置由 HeaderArrange 贴右上
            winClose = new Label();
            winClose.Text = "✕";
            winClose.ForeColor = UiKit.A(206, 216, 230);
            winClose.BackColor = Color.Transparent;
            winClose.Font = new Font("Segoe UI", 9.5F);
            winClose.TextAlign = ContentAlignment.MiddleCenter;
            winClose.Size = new Size(32, 32);
            winClose.Paint += delegate (object s3, PaintEventArgs e3)
            {
                e3.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool hp = winClose.Tag != null;
                Color hbFill = UiKit.Paper
                    ? (hp ? Color.FromArgb(234, 224, 204) : Color.FromArgb(250, 245, 233))
                    : (hp ? UiKit.A(62, 76, 98) : UiKit.A(44, 56, 74));
                using (GraphicsPath hp2 = UiKit.RoundedPath(new Rectangle(0, 0, winClose.Width - 1, winClose.Height - 1), 8))
                {
                    using (SolidBrush hb2 = new SolidBrush(hbFill))
                        e3.Graphics.FillPath(hb2, hp2);
                    if (UiKit.Paper)
                        using (Pen hpn = new Pen(Color.FromArgb(178, 165, 140), 1f))
                            e3.Graphics.DrawPath(hpn, hp2);
                }
                if (UiKit.Paper)
                {
                    e3.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font wf = new Font("Segoe UI", 9.5F))
                    using (SolidBrush wb = new SolidBrush(UiKit.Ink))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        e3.Graphics.DrawString("✕", wf, wb, new RectangleF(0, 0, winClose.Width, winClose.Height), sf);
                    }
                }
                else
                {
                    TextRenderer.DrawText(e3.Graphics, "✕", new Font("Segoe UI", 9F), new Rectangle(0, 0, winClose.Width, winClose.Height), winClose.Tag == null ? UiKit.A(214, 224, 238) : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            };
            winClose.Cursor = Cursors.Hand;
            winClose.MouseEnter += (s1, e1) => { winClose.Tag = "1"; winClose.ForeColor = UiKit.Txt(Color.White); winClose.Invalidate(); };
            winClose.MouseLeave += (s1, e1) => { winClose.Tag = null; winClose.ForeColor = UiKit.A(206, 216, 230); winClose.Invalidate(); };
            winClose.Click += delegate { Close(); };
            header.Controls.Add(winClose);

            winMin = new Label();
            winMin.Text = "—";
            winMin.ForeColor = UiKit.A(206, 216, 230);
            winMin.BackColor = Color.Transparent;
            winMin.Font = new Font("Segoe UI", 9.5F);
            winMin.TextAlign = ContentAlignment.MiddleCenter;
            winMin.Size = new Size(32, 32);
            winMin.Paint += delegate (object s3, PaintEventArgs e3)
            {
                e3.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool hp = winMin.Tag != null;
                Color hbFill = UiKit.Paper
                    ? (hp ? Color.FromArgb(234, 224, 204) : Color.FromArgb(250, 245, 233))
                    : (hp ? UiKit.A(62, 76, 98) : UiKit.A(44, 56, 74));
                using (GraphicsPath hp2 = UiKit.RoundedPath(new Rectangle(0, 0, winMin.Width - 1, winMin.Height - 1), 8))
                {
                    using (SolidBrush hb2 = new SolidBrush(hbFill))
                        e3.Graphics.FillPath(hb2, hp2);
                    if (UiKit.Paper)
                        using (Pen hpn = new Pen(Color.FromArgb(178, 165, 140), 1f))
                            e3.Graphics.DrawPath(hpn, hp2);
                }
                if (UiKit.Paper)
                {
                    e3.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font wf = new Font("Segoe UI", 10F))
                    using (SolidBrush wb = new SolidBrush(UiKit.Ink))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        e3.Graphics.DrawString("—", wf, wb, new RectangleF(0, 0, winMin.Width, winMin.Height), sf);
                    }
                }
                else
                {
                    TextRenderer.DrawText(e3.Graphics, "—", new Font("Segoe UI", 9F), new Rectangle(0, 0, winMin.Width, winMin.Height), winMin.Tag == null ? UiKit.A(214, 224, 238) : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            };
            winMin.Cursor = Cursors.Hand;
            winMin.MouseEnter += (s1, e1) => { winMin.Tag = "1"; winMin.ForeColor = UiKit.Txt(Color.White); winMin.Invalidate(); };
            winMin.MouseLeave += (s1, e1) => { winMin.Tag = null; winMin.ForeColor = UiKit.A(206, 216, 230); winMin.Invalidate(); };
            winMin.Click += delegate { try { WindowState = FormWindowState.Minimized; } catch (Exception dg1764_1) { Diag.Swallow("MainForm.Ui.cs:410", dg1764_1); } };
            header.Controls.Add(winMin);

            // v17.9：这些区域留白最多，整块都能拖窗口
            AttachWindowDrag(header);
            Label meName = new Label();
            meName.Text = "小工具箱";
            meName.ForeColor = UiKit.Txt(Color.White);
            meName.BackColor = Color.Transparent;
            meName.Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold);
            meName.Location = new Point(12, 56);
            meName.Size = new Size(164, 34);
            meName.TextAlign = ContentAlignment.MiddleLeft;
            meName.AutoSize = false;
            header.Controls.Add(meName);

            // v14.3：10 项分三组，每组前一行小标题（20px），条目压到 34px 才排得下
            int y = 86;
            int[] navTop = new int[ACTIONS.Length];
            bool[] hov = new bool[ACTIONS.Length];
            navHovT = new float[ACTIONS.Length];
            navHovTarget = new bool[ACTIONS.Length];
            navTip = new ToolTip();
            navTip.AutoPopDelay = 4000; navTip.InitialDelay = 500;
            int gAt = 0;
            for (int i = 0; i < ACTIONS.Length; i++)
            {
                if (gAt < NAV_GROUP_AT.Length && NAV_GROUP_AT[gAt] == i)
                {
                    int gi = gAt;
                    Panel gp = new Panel();
                    gp.BackColor = C_SIDE;
                    gp.Location = new Point(0, y);
                    gp.Size = new Size(168, 16);
                    gp.Paint += (s, e) =>
                    {
                        if (UiKit.Paper)
                            using (SolidBrush bdark = new SolidBrush(Color.FromArgb(31, 29, 24)))
                                e.Graphics.FillRectangle(bdark, gp.ClientRectangle);
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        int tw = 40;
                        using (Font f = new Font(Ui2.FamilyName, 7F, FontStyle.Bold))
                        {
                            TextRenderer.DrawText(e.Graphics, NAV_GROUP[gi], f, new Rectangle(14, 3, 140, 12),
                                UiKit.Paper ? Color.FromArgb(196, 184, 158) : UiKit.A(132, 144, 164), TextFormatFlags.NoPadding);
                            tw = TextRenderer.MeasureText(e.Graphics, NAV_GROUP[gi], f, new Size(168, 12), TextFormatFlags.NoPadding).Width;
                        }
                        using (Pen ln = new Pen(UiKit.Paper ? Color.FromArgb(42, 196, 166, 104) : UiKit.AA(52, 120, 134, 156), 1F))
                            e.Graphics.DrawLine(ln, 14 + tw + 6, 10, 160, 10);
                    };
                    sidebar.Controls.Add(gp);
                    y += 16;
                    gAt++;
                }
                navTop[i] = y;
                Panel item = new Panel();
                item.BackColor = UiKit.Paper ? Color.FromArgb(31, 29, 24) : C_SIDE;
                item.Tag = "sidenav";   // 纸感不许把导航条自身刷成纸白，否则白字压白底
                item.Location = new Point(0, y);
                item.Size = new Size(168, 44);
                item.Cursor = Cursors.Hand;
                int idx = i;
                int pi = i;
                item.Click += (s, e) => SelectAction(idx);
                item.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (UiKit.Paper)
                        using (SolidBrush bdark = new SolidBrush(Color.FromArgb(31, 29, 24)))
                            e.Graphics.FillRectangle(bdark, item.ClientRectangle);   // 导航条自身底色，防被纸白覆盖
                    Rectangle pr = new Rectangle(8, 3, 152, 40);
                    using (GraphicsPath path = UiKit.RoundedPath(pr, 10))
                    {
                        if (pi == sidebarSel)
                        {
                            Color st1 = UiKit.Paper ? Color.FromArgb(54, 49, 40) : UiKit.A(26, 60, 74);
                            Color st2 = UiKit.Paper ? Color.FromArgb(38, 35, 29) : UiKit.A(13, 21, 31);
                            using (LinearGradientBrush br = new LinearGradientBrush(pr, st1, st2, 0f))
                                e.Graphics.FillPath(br, path);
                            Rectangle kbsh = new Rectangle(pr.X, pr.Y + pr.Height * 2 / 3, pr.Width, Math.Max(3, pr.Height / 3));
                            using (GraphicsPath kbsP = UiKit.RoundedPath(pr, 10))
                            using (LinearGradientBrush kbsb = new LinearGradientBrush(kbsh, UiKit.AA(0, 0, 0, 0), UiKit.AA(44, 0, 0, 0), 90f))
                                e.Graphics.FillPath(kbsb, kbsP);
                            Color edge = UiKit.Paper ? Color.FromArgb(110, 168, 137, 72) : UiKit.AA(110, 88, 220, 236);
                            using (Pen pp = new Pen(edge, 1F))
                                e.Graphics.DrawPath(pp, path);
                            using (Pen hl = new Pen(UiKit.Paper ? Color.FromArgb(38, 255, 246, 226) : UiKit.AA(64, 255, 255, 255), 1F))
                                e.Graphics.DrawLine(hl, 14, 1, pr.Width - 14, 1);
                            // v19.16：选中态改成左侧短竖条 + 底线，不再整块高亮抢戏
                            using (Pen ul = new Pen(UiKit.Paper ? Color.FromArgb(210, 214, 176, 86) : UiKit.AA(180, 58, 198, 232), 1F))
                                e.Graphics.DrawLine(ul, pr.X + 14, pr.Bottom - 1, pr.Right - 14, pr.Bottom - 1);
                            // v9.1：位置指示针。纵向坐标是全栏共用的 navY，这里换成自己的局部坐标再画，
                            // 跨条目时被各自的裁剪边自然分成两半，拼起来仍是连续的一根。
                            // 位置放在胶囊左外侧（x=2），跟胶囊描边有 5px 空隙，1x 下才看得清
                            int byy = (int)Math.Round(navY - navTop[pi]);
                            if (byy > -20 && byy < 50)
                            {
                                Rectangle brc = new Rectangle(2, byy, 3, 30);
                                using (GraphicsPath bp = UiKit.RoundedPath(brc, 2))
                                {
                                    Color c1 = UiKit.Paper ? Color.FromArgb(238, 224, 184, 92) : UiKit.AA(238, 158, 206, 255);
                                    Color c2 = UiKit.Paper ? Color.FromArgb(150, 168, 132, 52) : UiKit.AA(206, 72, 134, 228);
                                    using (LinearGradientBrush gb = new LinearGradientBrush(brc, c1, c2, 90f))
                                        e.Graphics.FillPath(gb, bp);
                                }
                                Rectangle glw = new Rectangle(brc.X + 3, brc.Y + 3, 2, brc.Height - 6);
                                using (SolidBrush gs = new SolidBrush(UiKit.Paper ? Color.FromArgb(44, 238, 224, 184) : UiKit.AA(46, 108, 168, 255)))
                                    e.Graphics.FillRectangle(gs, glw);
                            }
                        }
                        else if (navHovT != null && navHovT[pi] > 0.02f)
                        {
                            // v18.2：悬停不再是"啪"地点亮，0→1 用 0.24 的追尾系数渐入，离开时同样淡出
                            float ht = Math.Min(1f, navHovT[pi]);
                            int ha = (int)(255 * ht);
                            using (LinearGradientBrush br = new LinearGradientBrush(pr,
                                UiKit.AA(ha, 43, 50, 64), UiKit.AA(ha, 28, 33, 42), 0f))
                                e.Graphics.FillPath(br, path);
                            using (Pen kbp = new Pen(UiKit.AA((int)(38 * ht), 255, 255, 255), 1F))
                                e.Graphics.DrawPath(kbp, path);
                        }
                    }
                    if (UiKit.Paper)
                    {
                        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        Color nc = (pi == sidebarSel) ? Color.FromArgb(255, 252, 243) : Color.FromArgb(226, 217, 198);
                        Color sc = (pi == sidebarSel) ? Color.FromArgb(208, 195, 170) : Color.FromArgb(182, 172, 151);
                        using (Font fn = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold))
                        using (SolidBrush nb = new SolidBrush(nc))
                            e.Graphics.DrawString(NavShort()[pi], fn, nb, new PointF(50f, 1f));
                        using (Font fs = new Font("Microsoft YaHei UI", 7F))
                        using (SolidBrush sb3 = new SolidBrush(sc))
                            e.Graphics.DrawString(NavSubShort()[pi], fs, sb3, new PointF(50f, 20f));
                    }
                };
                item.MouseEnter += (s, e) =>
                {
                    hov[pi] = true; if (navHovTarget != null) navHovTarget[pi] = true; NudgeNavHover(pi);
                };
                item.MouseLeave += (s, e) =>
                {
                    hov[pi] = false; if (navHovTarget != null) navHovTarget[pi] = false; NudgeNavHover(pi);
                };
                sidebar.Controls.Add(item);
                if (i < 9 && navTip != null)
                    navTip.SetToolTip(item, T(NavShort()[i] + "　Alt+" + (i + 1) + " 直达", NavShort()[i] + "   Alt+" + (i + 1)));

                // v9.1：原来每个条目各挂一条 3px 竖条，切页时"啪"地瞬移。
                // 改成全栏只留一条（见循环后面 navSlide），切换时滑过去。

                // 彩色渐变图标：每个模块专属色，亮色方块 + 玻璃高光
                Panel av = new Panel();
                av.BackColor = Color.Transparent;
                av.Location = new Point(12, 9);
                av.Size = new Size(28, 28);
                int ai = i;
                av.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle avr = new Rectangle(0, 0, 28, 28);
                    using (GraphicsPath path = UiKit.RoundedPath(avr, 8))
                    {
                        e.Graphics.SetClip(path);
                        // 非当前页的图标压暗一档；v19.16 起纸感侧栏统一降饱和，选中项才保留马克笔色
                        Color ac = ACTION_COLOR[ai];
                        if (UiKit.Paper)
                        {
                            // 侧栏是暖墨立柱：图标块整体压深，白色线条图标才立得住；
                            // 选中项比未选中项亮一档，仍保留彩色。
                            ac = UiKit.Darken(ac, pi == sidebarSel ? 42 : 54);
                        }
                        else if (pi != sidebarSel) ac = UiKit.Darken(ac, 26);
                        using (LinearGradientBrush br = new LinearGradientBrush(avr,
                            UiKit.Lighten(ac, 20),
                            UiKit.Darken(ac, 48), 90F))
                            e.Graphics.FillRectangle(br, avr);
                        using (LinearGradientBrush hl = new LinearGradientBrush(
                            new Rectangle(0, 0, 28, 14), UiKit.AA(76, 255, 255, 255), UiKit.AA(0, 255, 255, 255), 90F))
                            e.Graphics.FillRectangle(hl, 0, 0, 28, 14);
                        e.Graphics.ResetClip();
                        using (Pen pp = new Pen(UiKit.AA(64, 255, 255, 255), 1F))
                            e.Graphics.DrawPath(pp, path);
                    }
                    UiKit.Glyph(e.Graphics, new Rectangle(0, 0, 28, 28), ai);
                };
                item.Controls.Add(av);

                Label nm = new Label();
                nm.Text = NavShort()[i];
                nm.BackColor = UiKit.Paper ? Color.FromArgb(31, 29, 24) : Color.Transparent;
                nm.Tag = "sidenav";
                nm.ForeColor = UiKit.Paper
                    ? (i == sidebarSel ? Color.FromArgb(255, 252, 243) : Color.FromArgb(224, 215, 196))
                    : ((i == sidebarSel) ? UiKit.Txt(Color.White) : C_TEXT);
                nm.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
                nm.Location = new Point(50, 4);
                nm.AutoSize = false;
                nm.Size = new Size(112, 16);
                nm.AutoEllipsis = true;
                nm.TextAlign = ContentAlignment.MiddleLeft;
                item.Controls.Add(nm);
                if (UiKit.Paper) nm.Visible = false;   // 纸感导航文字由 item 自绘，避免透明/GDI 洗白

                Label sub = new Label();
                sub.Text = NavSubShort()[i];
                sub.BackColor = UiKit.Paper ? Color.FromArgb(31, 29, 24) : Color.Transparent;
                sub.Tag = "sidenav";
                sub.ForeColor = UiKit.Paper
                    ? (i == sidebarSel ? Color.FromArgb(205, 192, 168) : Color.FromArgb(178, 168, 148))
                    : ((i == sidebarSel) ? UiKit.A(170, 184, 207) : C_SUB);
                sub.Font = new Font("Microsoft YaHei UI", 7F);
                sub.Location = new Point(50, 22);
                sub.AutoSize = false;
                sub.Size = new Size(112, 16);
                sub.AutoEllipsis = true;
                sub.Visible = true;
                sub.AutoSize = true;
                item.Controls.Add(sub);
                if (UiKit.Paper) sub.Visible = false;

                // 子控件点击统一转发到侧栏项（Label/Panel 会拦截鼠标事件）
                nm.Click += (s, e) => SelectAction(idx);
                sub.Click += (s, e) => SelectAction(idx);
                av.Click += (s, e) => SelectAction(idx);
                item.Click += (s, e) => SelectAction(idx);

                convItem[i] = item;
                convName[i] = nm;
                y += 44;   // v16.1：条目高（名称 + 副标两行）
            }

            // v19.10：撤掉侧栏「投递文件」入口与整窗拖放，投递舱功能下线。

            // v9.1：侧栏位置指示条。全栏只有这一根，切页时从旧位置滑到新位置（缓出），
            // 视线顺着它走就知道"我从哪一页到了哪一页"。
            // 做法上不做成独立 Panel：Panel 会被后加的条目盖住（WinForms 里先加的在最上层），
            // 改成"一根针的纵向坐标 navY"，由每个条目在 Paint 里按同一坐标画出来——
            // 针跨在两条目之间时，上下两半各画各的一半，但 x 相同、渐变相同，看上去是连着的。
            navY = navTop[Math.Max(0, sidebarSel)] + 4;
            navTargetY = navY;

            navSlideTimer = new System.Windows.Forms.Timer();
            navSlideTimer.Interval = 14;
            navSlideTimer.Tick += delegate
            {
                try
                {
                    navTargetY = navTop[Math.Max(0, sidebarSel)] + 4;
                    double dy = navTargetY - navY;
                    if (Math.Abs(dy) <= 0.6) { navY = navTargetY; navSlideTimer.Stop(); }
                    else navY += dy * 0.30;                       // 缓出
                    for (int k = 0; k < ACTIONS.Length; k++) if (convItem[k] != null) convItem[k].Invalidate();
                }
                catch { try { navSlideTimer.Stop(); } catch (Exception dg2064_1) { Diag.Swallow("MainForm.Ui.cs:710", dg2064_1); } }
            };

            chkAutorun = new RoundCheck();
            chkAutorun.Text = T("开机自启", "Auto-start");
            chkAutorun.ForeColor = UiKit.Paper ? Color.FromArgb(196, 186, 166) : UiKit.A(150, 160, 176);
            if (UiKit.Paper) { chkAutorun.BackColor = Color.FromArgb(31, 29, 24); chkAutorun.Tag = "sidenav"; }
            chkAutorun.Font = new Font("Microsoft YaHei UI", 7.5F);
            chkAutorun.Location = new Point(10, y + 10);
            chkAutorun.AutoSize = true;
            chkAutorun.CheckedChanged += (s, e) => ToggleAutorun();
            sidebar.Controls.Add(chkAutorun);

            chkTray = new RoundCheck();
            chkTray.Text = T("关闭进托盘", "Tray on close");
            chkTray.ForeColor = UiKit.Paper ? Color.FromArgb(196, 186, 166) : UiKit.A(150, 160, 176);
            if (UiKit.Paper) { chkTray.BackColor = Color.FromArgb(31, 29, 24); chkTray.Tag = "sidenav"; }
            chkTray.Font = new Font("Microsoft YaHei UI", 7.5F);
            chkTray.Location = new Point(10, y + 28);
            chkTray.AutoSize = true;
            chkTray.CheckedChanged += (s, e) => cfg.Tray = chkTray.Checked;
            sidebar.Controls.Add(chkTray);

            // v17.6：这一块原来是「自定义背景」+「小工具箱 vX · 更新公告」两个文字入口，
            // 和 Header 第二行的「自定义背景」「更新公告」完全重复，还挤在 636–670 这一小块里。
            // 整块撤掉：自定义背景只在 Header 一处，版本号由侧栏顶部卡承担，
            // 更新公告由 Header + 状态栏右下角两处承担，侧栏底部留给 SMALL TOOLS 标语。

            // ---------- 右侧主区 ----------
            mainArea = new BgPanel();
            mainArea.BackColor = C_MAIN;
            ((BgPanel)mainArea).Bg = bgImage;
            ((BgPanel)mainArea).ShowBg = false;   // 主区不铺底图：页面自己铺深色，底图透进来会把控件洗灰
            ((BgPanel)mainArea).BaseColor = C_MAIN;
            PagePanel.Wall = bgImage;
            PagePanel.WallOn = (bgOn && bgImage != null);
            // v17.6：外壳用父子两级 Dock。同层 Fill 扣不掉 Top/Bottom，
            // 但放进 mainArea 内部就可靠：Header 顶、状态条底、content 补满。
            Controls.Add(sidebar);
            Controls.Add(mainArea);

            // v19.10：投递舱下线（入口、遮罩、整窗拖放一并撤掉）。

            // v16.0：内容容器。宽 760 在主区里水平居中，页面 Panel 仍 Dock=Fill，
            // 所以十页内部的绝对坐标一个都不用改，只是整体换了位置与底色。
            Resize += delegate { LayoutShell(); };
            content = new Panel();
            content.BackColor = Color.Transparent;
            Controls.Add(header);        // v17.9：Header 升到窗体级，横贯全宽（方案四）
            mainArea.Controls.Add(statusbar);
            mainArea.Controls.Add(content);
            mainArea.Resize += delegate { CenterContent(); };

            pageTimer = new PagePanel();
            pageTimer.Dock = DockStyle.Fill;
            pageTimer.BackColor = Color.Transparent;
            content.Controls.Add(pageTimer);

            // v14.2 工作台首页：新增一页，不动任何现有页面的布局
            pageHome = new PagePanel();
            pageHome.Dock = DockStyle.Fill;
            pageHome.BackColor = Color.Transparent;
            pageHome.Visible = false;
            content.Controls.Add(pageHome);
            BuildHomePage();


            pageReset = new PagePanel();
            pageReset.Dock = DockStyle.Fill;
            pageReset.BackColor = Color.Transparent;
            pageReset.Visible = false;
            // v17.9：一键重装已下线，页面不再挂进内容区（构建代码保留，避免连带改动整页逻辑）

            Label rTitle = new Label();
            rTitle.Text = T("重装方式", "Reinstall Methods");
            rTitle.ForeColor = C_TEXT;
            rTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            rTitle.Location = new Point(30, 26);
            rTitle.AutoSize = true;
            pageReset.Controls.Add(rTitle);

            Label rSub = new Label();
            rSub.Text = T("使用 Windows 官方恢复机制，安全重装或修复系统", "Use official Windows recovery to reinstall or repair safely");
            rSub.ForeColor = C_SUB;
            rSub.Font = new Font("Microsoft YaHei UI", 9F);
            rSub.Location = new Point(30, 68);
            rSub.AutoSize = true;
            pageReset.Controls.Add(rSub);

            Button rBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            rBack.Click += (s, e) => ShowResetPage(false);
            pageReset.Controls.Add(rBack);

            Button rCheck = MakeBtn(T("环境检测", "Check WinRE"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 496, 26, 116, 28);
            rCheck.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            rCheck.Click += (s, e) => CheckRecoveryEnv();
            pageReset.Controls.Add(rCheck);

            string[,] resets = langEn
                ? new string[,] {
                    { "Open Reset Wizard", "Keep files or remove everything, follow the wizard", "wizard" },
                    { "Restart to Recovery (WinRE)", "Reset this PC, fix startup, open Command Prompt", "winre" },
                    { "Open Recovery Settings", "Settings → System → Recovery: reset & advanced startup", "settings" },
                }
                : new string[,] {
                    { "打开重装向导", "保留文件或删除所有内容，跟随向导完成系统重装", "wizard" },
                    { "重启进入恢复环境", "进入 WinRE：可重置此电脑、修复启动、进入命令提示符", "winre" },
                    { "打开系统恢复设置", "打开「设置 → 系统 → 恢复」，查看重置与高级启动选项", "settings" },
                };
            int ry = 96;   // v15.1：与清理页/配置页首卡同一条 96 基线（原 100）
            for (int i = 0; i < 3; i++)
            {
                Panel card = new Panel();
                card.BackColor = C_BUBBLE;
                card.Location = new Point(30, ry);
                card.Size = new Size(700, 84);
                UiKit.CardBorder(card, 12, UiKit.A(34, 197, 94));   // v15.2：绿细条标记"重装/恢复"
                pageReset.Controls.Add(card);

                Label ct = new Label();
                ct.Text = resets[i, 0];
                ct.ForeColor = C_TEXT;
                ct.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
                ct.Location = new Point(20, 12);
                ct.AutoSize = true;
                card.Controls.Add(ct);

                Label cd = new Label();
                cd.Text = resets[i, 1];
                cd.ForeColor = C_SUB;
                cd.Font = new Font("Microsoft YaHei UI", 8.5F);
                cd.Location = new Point(20, 44);
                cd.AutoSize = true;
                card.Controls.Add(cd);

                string mode = resets[i, 2];
                Button go = MakeBtn("开始", C_ON, Color.Black, 580, 26, 90, 32);
                go.Click += (s, e) => ConfirmReset(mode);
                card.Controls.Add(go);

                ry += 96;
            }

            Label warn = new Label();
            warn.Text = "⚠ 重装系统会删除或重置大量数据，操作前请务必备份重要文件，并确保设备已连接电源。";
            warn.ForeColor = UiKit.A(240, 160, 90);
            warn.Font = new Font("Microsoft YaHei UI", 9F);
            warn.Location = new Point(30, ry + 6);
            warn.AutoSize = true;
            pageReset.Controls.Add(warn);

            Label tip = new Label();
            tip.Text = "提示：系统重装依赖 Windows 恢复环境（WinRE）。若恢复环境缺失，可在「命令提示符」中执行：reagentc /enable";
            tip.ForeColor = C_SUB;
            tip.Font = new Font("Microsoft YaHei UI", 8F);
            tip.Location = new Point(30, ry + 32);
            tip.AutoSize = true;
            pageReset.Controls.Add(tip);

            // v9.0：空态插画（纯矢量）。放在提示行下方那片空区里，居中，不压任何文字。
            PictureBox resetArt = new PictureBox();
            resetArt.BackColor = Color.Transparent;
            resetArt.Bounds = new Rectangle(330, ry + 66, 300, 150);
            resetArt.Paint += delegate (object s1, PaintEventArgs e1)
            {
                EmptyArt.ResetArt(e1.Graphics, resetArt.ClientRectangle);
            };
            pageReset.Controls.Add(resetArt);

            // ===== C盘清理页 =====
            pageClean = new PagePanel();
            pageClean.Dock = DockStyle.Fill;
            pageClean.BackColor = Color.Transparent;
            pageClean.Visible = false;
            content.Controls.Add(pageClean);

            Label cTitle = new Label();
            cTitle.Text = "磁盘占用";
            cTitle.ForeColor = C_TEXT;
            cTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            cTitle.Location = new Point(30, 26);
            cTitle.AutoSize = true;
            pageClean.Controls.Add(cTitle);

            Label cSub = new Label();
            cSub.Text = T("扫描磁盘占用热力图、定位大文件，并调用清理引擎回收空间", "Map disk usage, find large files, then reclaim space");
            cSub.ForeColor = C_SUB;
            cSub.Font = new Font("Microsoft YaHei UI", 9F);
            cSub.Location = new Point(30, 68);
            cSub.Size = new Size(450, 18);
            cSub.AutoEllipsis = true;
            pageClean.Controls.Add(cSub);

            Button cBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 10, 90, 26);
            cBack.Click += (s, e) => ShowCleanPage(false);
            pageClean.Controls.Add(cBack);

            Button cScan = MakeBtn(T("扫描空间", "Scan disk"), UiKit.A(56, 189, 248), Color.White, 492, 50, 116, 32);
            cScan.Click += (s, e) => CleanScanStart();
            pageClean.Controls.Add(cScan);

            Button cStop = MakeBtn(T("停止", "Stop"), UiKit.A(52, 44, 46), UiKit.A(242, 150, 140), 616, 50, 96, 32);
            cStop.Click += (s, e) => { cleanStopFlag = true; };
            pageClean.Controls.Add(cStop);

            // —— 三张统计卡 ——
            string[] cZh = { "C 盘空间", "大文件占用", "已索引文件" };
            string[] cEn = { "Disk space", "Large files", "Indexed files" };
            Label[] cVal = new Label[3];
            Label[] cSml = new Label[3];
            for (int i = 0; i < 3; i++)
            {
                Panel cd = new Panel();
                cd.BackColor = Color.Transparent;
                cd.Location = new Point(30 + i * 238, 96);
                cd.Size = new Size(224, 78);
                int ci = i;
                cd.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle cr = new Rectangle(0, 0, cd.Width - 1, cd.Height - 1);
                    using (GraphicsPath path = UiKit.RoundedPath(cr, 12))
                    {
                        e.Graphics.SetClip(path);
                        if (UiKit.Paper)
                        {
                            using (LinearGradientBrush pbr = new LinearGradientBrush(cr,
                                Color.FromArgb(255, 252, 243), Color.FromArgb(247, 241, 228), 90F))
                                e.Graphics.FillRectangle(pbr, cr);
                        }
                        else
                        using (LinearGradientBrush br = new LinearGradientBrush(cr,
                            UiKit.A(60, 69, 86), UiKit.A(37, 43, 56), 90F))
                            e.Graphics.FillRectangle(br, cr);
                        Rectangle tr = new Rectangle(0, 0, cd.Width, 3);
                        using (LinearGradientBrush tb = new LinearGradientBrush(tr,
                            UiKit.A(56, 189, 248), UiKit.A(147, 197, 253), 0F))
                            e.Graphics.FillRectangle(tb, tr);
                        e.Graphics.ResetClip();
                        using (Pen pp = new Pen(UiKit.AA(52, 255, 255, 255), 1f))
                            e.Graphics.DrawPath(pp, path);
                    }
                    if (ci == 0 && cleanDiskTotal > 0)
                    {
                        int barW = cd.Width - 28;
                        int used = (int)Math.Round((double)barW * cleanDiskUsed / cleanDiskTotal);
                        using (SolidBrush b1 = new SolidBrush(UiKit.A(40, 48, 60)))
                            e.Graphics.FillRectangle(b1, 14, cd.Height - 8, barW, 4);
                        using (SolidBrush b2 = new SolidBrush(UiKit.A(56, 189, 248)))
                            e.Graphics.FillRectangle(b2, 14, cd.Height - 8, Math.Max(2, used), 4);
                    }
                };
                Label cap = new Label();
                cap.Text = T(cZh[i], cEn[i]);
                cap.BackColor = Color.Transparent;
                cap.ForeColor = UiKit.A(166, 180, 200);
                cap.Font = new Font("Microsoft YaHei UI", 8.5F);
                cap.Location = new Point(13, 4);
                cap.AutoSize = true;
                cd.Controls.Add(cap);
                Label vv = new Label();
                vv.Text = "--";
                vv.BackColor = Color.Transparent;
                vv.ForeColor = UiKit.A(238, 244, 255);
                vv.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
                vv.Location = new Point(12, 22);
                vv.Size = new Size(150, 24);
                cd.Controls.Add(vv);
                Label sm = new Label();
                sm.Text = T("等待扫描", "not scanned");
                sm.BackColor = Color.Transparent;
                sm.ForeColor = UiKit.A(126, 140, 160);
                sm.Font = new Font("Microsoft YaHei UI", 7.5F);
                sm.Location = new Point(13, 48);
                sm.Size = new Size(150, 14);
                cd.Controls.Add(sm);
                if (true)
                {
                    foreach (Control c0 in cd.Controls) if (c0 is Label) c0.Visible = false;
                    cd.Paint += delegate (object s3, PaintEventArgs e3)
                    {
                        Graphics g3 = e3.Graphics;
                        g3.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        foreach (Control c0 in cd.Controls)
                        {
                            Label l0 = c0 as Label; if (l0 == null) continue;
                            Color lc = UiKit.Paper ? ((l0 == vv) ? Color.FromArgb(32, 28, 24)
                                     : (l0 == sm) ? Color.FromArgb(96, 86, 72)
                                     : Color.FromArgb(86, 76, 62)) : l0.ForeColor;
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
                pageClean.Controls.Add(cd);
                cVal[i] = vv;
                cSml[i] = sm;
            }
            lblDiskCap = cVal[0];
            lblDiskSub = cSml[0];
            lblBigCap = cVal[1];
            lblBigSub = cSml[1];
            lblJunkCap = cVal[2];
            lblJunkSub = cSml[2];

            try
            {
                DriveInfo di = new DriveInfo("C");
                cleanDiskTotal = di.TotalSize;
                cleanDiskUsed = di.TotalSize - di.TotalFreeSpace;
                lblDiskCap.Text = FmtSize(cleanDiskUsed);
                lblDiskSub.Text = T("已用 / 共 ", "used of ") + FmtSize(cleanDiskTotal);
            }
            catch (Exception dg2357_1) { Diag.Swallow("MainForm.Ui.cs:1003", dg2357_1); }

            // —— 热力图 ——
            pnlTree = new PictureBox();
            pnlTree.BackColor = UiKit.Paper ? Color.FromArgb(250, 245, 233) : UiKit.A(20, 26, 36);
            pnlTree.Location = new Point(30, 188);
            pnlTree.Size = new Size(440, 230);
            pnlTree.Paint += (s, e) =>
            {
                // v8.7：同样的深色，Panel 底会被系统提亮成一整块灰；PictureBox 先 Clear 再画才真正贴住页面底。
                e.Graphics.Clear(UiKit.Paper ? Color.FromArgb(250, 245, 233) : UiKit.A(20, 26, 36));
                CleanPaintTree(e.Graphics, pnlTree.ClientRectangle);
            };
            pnlTree.MouseMove += (s, e) =>
            {
                int hit = CleanHitTest(e.Location);
                if (hit != cleanHoverBlk) { cleanHoverBlk = hit; pnlTree.Invalidate(); }
            };
            pnlTree.MouseLeave += (s, e) => { cleanHoverBlk = -1; pnlTree.Invalidate(); };
            pnlTree.Resize += (s, e) =>
            {
                if (cleanBlks != null && cleanBlks.Count > 0)
                    CleanLayoutTree(new Rectangle(4, 4, pnlTree.ClientSize.Width - 8, pnlTree.ClientSize.Height - 8));
                pnlTree.Invalidate();
            };
            pageClean.Controls.Add(pnlTree);

            // —— 右侧两张清理卡 ——
            string[,] cleans = {
                { "平衡保护清理", "白名单保护浏览器与办公数据", "v8", "低" },
                { "极致深度清理", "全量清空用户与系统缓存", "full", "中" },
            };
            for (int i = 0; i < 2; i++)
            {
                int ri = i;
                Panel card = new Panel();
                // v8.7：卡片底不再用亮一档的 C_BUBBLE——圆角路径外那 1px 会露出它，
                // 在深色页上就成了一圈发光的外轮廓；换成页面同调的深色，漏边就看不见了。
                card.BackColor = Ui2.CtlInk(UiKit.A(24, 28, 34));   // v8.8：Panel 会+34，反补后圆角外才与页面同色
                card.Location = new Point(482, 188 + i * 119);
                card.Size = new Size(248, 111);
                card.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle cr = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (GraphicsPath path = UiKit.RoundedPath(cr, 10))
                    {
                        if (UiKit.Paper)
                        {
                            using (LinearGradientBrush pbr = new LinearGradientBrush(cr,
                                Color.FromArgb(255, 252, 243), Color.FromArgb(238, 230, 212), 90F))
                                e.Graphics.FillPath(pbr, path);
                            using (Pen pl = new Pen(Color.FromArgb(255, 178, 165, 141), 1.1f))
                                e.Graphics.DrawPath(pl, path);
                        }
                        else
                        {
                            using (LinearGradientBrush br = new LinearGradientBrush(cr,
                                UiKit.A(52, 60, 73), UiKit.A(39, 45, 56), 90F))
                                e.Graphics.FillPath(br, path);
                            using (Pen pp = new Pen(UiKit.AA(34, 255, 255, 255), 1f))
                                e.Graphics.DrawPath(pp, path);
                        }
                    }
                };
                Label ctn = new Label();
                ctn.Text = T(cleans[i, 0], i == 0 ? "Balanced clean" : "Deep clean");
                ctn.BackColor = Color.Transparent;
                ctn.ForeColor = C_TEXT;
                ctn.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                ctn.Location = new Point(16, 10);
                ctn.AutoSize = true;
                card.Controls.Add(ctn);

                Label ctd = new Label();
                ctd.Text = T(cleans[i, 1], i == 0 ? "Whitelisted, safe" : "Full cache wipe");
                ctd.BackColor = Color.Transparent;
                ctd.ForeColor = C_SUB;
                ctd.Font = new Font("Microsoft YaHei UI", 8F);
                ctd.Location = new Point(16, 34);
                ctd.AutoSize = true;
                card.Controls.Add(ctd);

                Label risk = new Label();
                risk.Text = T("风险：", "Risk: ") + T(cleans[i, 3], i == 0 ? "low" : "medium");
                risk.BackColor = Color.Transparent;
                risk.ForeColor = (cleans[i, 3] == "低") ? C_ON : UiKit.A(249, 115, 22);
                risk.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
                risk.Location = new Point(16, 56);
                risk.AutoSize = true;
                card.Controls.Add(risk);

                string cmode = cleans[i, 2];
                Button cgo = MakeBtn(T("一键清理", "Clean"), UiKit.A(22, 140, 72), Color.White, 132, 74, 100, 28);
                cgo.Click += (s, e) => RunClean(cmode);
                card.Controls.Add(cgo);

                pageClean.Controls.Add(card);
                if (true)
                {
                    foreach (Control c0 in card.Controls) if (c0 is Label) c0.Visible = false;
                    card.Paint += delegate (object s3, PaintEventArgs e3)
                    {
                        Graphics g3 = e3.Graphics;
                        g3.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        foreach (Control c0 in card.Controls)
                        {
                            Label l0 = c0 as Label; if (l0 == null) continue;
                            Color lc = UiKit.Paper ? ((l0 == ctn) ? Color.FromArgb(32, 28, 24)
                                     : (l0 == ctd) ? Color.FromArgb(86, 76, 62)
                                     : (cleans[ri, 3] == "低" ? Color.FromArgb(44, 108, 66) : Color.FromArgb(150, 92, 40))) : l0.ForeColor;
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
            }

            lblCleanScan = new Label();
            lblCleanScan.Text = T("尚未扫描 · 点右上角「扫描空间」生成 C 盘占用热力图", "Not scanned · click Scan disk");
            lblCleanScan.ForeColor = C_SUB;
            lblCleanScan.Font = new Font("Microsoft YaHei UI", 8F);
            lblCleanScan.Location = new Point(30, 424);
            lblCleanScan.Size = new Size(430, 16);
            lblCleanScan.AutoEllipsis = true;
            pageClean.Controls.Add(lblCleanScan);

            Button cExp = MakeBtn(T("导出报告", "Export"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 470, 420, 108, 24);
            cExp.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            cExp.Click += (s, e) => CleanExportReport();
            pageClean.Controls.Add(cExp);

            Button cOpenSel = MakeBtn(T("定位文件", "Locate"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 588, 420, 108, 24);
            cOpenSel.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            cOpenSel.Click += (s, e) => CleanOpenBig();
            pageClean.Controls.Add(cOpenSel);

            lvCleanBig = MakeDarkList(new string[] { T("大小", "Size"), T("文件名", "File"), T("所在目录", "Folder"), T("修改", "Date") },
                new int[] { 84, 210, 300, 86 }, 30, 446, 700, 122);
            lvCleanBig.DoubleClick += (s, e) => CleanOpenBig();
            pageClean.Controls.Add(lvCleanBig);

            Label cwarn = new Label();
            cwarn.Text = T("⚠ 清理只针对可再生的缓存与临时文件，不会触碰个人文档；首次执行会弹出系统 UAC 授权。热力图仅统计文件占用，不读取内容。",
                "⚠ Only regenerable caches are removed; personal files stay untouched. The heatmap only measures sizes.");
            cwarn.ForeColor = UiKit.A(240, 160, 90);
            cwarn.Font = new Font("Microsoft YaHei UI", 8F);
            cwarn.Location = new Point(30, 580);
            cwarn.Size = new Size(700, 16);
            pageClean.Controls.Add(cwarn);

            Label cnote = new Label();
            cnote.Text = T("清理引擎基于开源项目 Light-Help（Cotton059 · GPL-3.0）本地化集成；占用统计为本工具自研的一次性脚本遍历。",
                "Cleanup engine: Light-Help (GPL-3.0). Disk usage scan is built in.");
            cnote.ForeColor = C_SUB;
            cnote.Font = new Font("Microsoft YaHei UI", 7.5F);
            cnote.Location = new Point(30, 602);
            cnote.Size = new Size(700, 16);
            pageClean.Controls.Add(cnote);

            // ===== Steam 成就页（一体式工作台：一个按钮跑完全库，结果直接落在页内控制台） =====
            pageSam = new PagePanel();
            pageSam.Dock = DockStyle.Fill;
            pageSam.BackColor = Color.Transparent;
            pageSam.Visible = false;
            content.Controls.Add(pageSam);

            Label sTitle = new Label();
            sTitle.Text = T("成就总览", "Achievements");
            sTitle.ForeColor = C_TEXT;
            sTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            sTitle.Location = new Point(30, 26);
            sTitle.AutoSize = true;
            pageSam.Controls.Add(sTitle);

            Label sSub = new Label();
            sSub.Text = T("一体式：点一次就跑完「检查 Steam → 扫全库 → 逐款写入 → 出结果」，跑批中再点即停，全程不用另开窗口",
                "All-in-one: one click runs check > scan > write > report; click again to stop, no extra window needed.");
            sSub.ForeColor = C_SUB;
            sSub.Font = new Font("Microsoft YaHei UI", 9F);
            sSub.Location = new Point(30, 68);
            sSub.AutoSize = true;
            pageSam.Controls.Add(sSub);

            Button sBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            sBack.Click += (s, e) => ShowSamPage(false);
            pageSam.Controls.Add(sBack);

            Panel sCard = new Panel();
            sCard.BackColor = UiKit.Paper ? Color.FromArgb(250, 245, 233) : C_BUBBLE;
            sCard.Location = new Point(30, 96);
            sCard.Size = new Size(726, 104);
            UiKit.CardBorder(sCard, 12, UiKit.A(249, 115, 22));   // v15.2：橙细条标记"成就"
            pageSam.Controls.Add(sCard);

            Label sN = new Label();
            sN.Text = T("一键全成就（全库直连）", "Unlock everything (direct)");
            sN.ForeColor = C_TEXT;
            sN.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            sN.Location = new Point(20, 12);
            sN.AutoSize = true;
            sCard.Controls.Add(sN);

            Label sD = new Label();
            sD.Text = T("自动检查 Steam 与 steam_api64.dll、读成就定义、逐款直连写入。\n结果实时落到下方控制台；失败原地重试一次、整轮跑完再补跑。\n个别被拒的游戏再走「进游戏页解锁（旧）」。",
                "Checks Steam and steam_api64.dll, reads schemas, writes each title directly.\nResults stream below; failures retry once in place plus one final pass.\nRefused titles fall back to the game-page path.");
            sD.ForeColor = C_SUB;
            sD.Font = new Font("Microsoft YaHei UI", 8F);
            sD.Location = new Point(20, 38);
            sD.AutoSize = false;
            sD.Size = new Size(486, 64);
            sCard.Controls.Add(sD);

            Button sUnlock = MakeBtn(T("一键全成就\n（跑完全库）", "Unlock all\n(full library)"), UiKit.A(255, 92, 92), Color.White, 520, 18, 186, 68);
            sUnlock.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            sUnlock.Click += (s, e) => DirectUnlockAll();
            sCard.Controls.Add(sUnlock);
            btnUnlockDirect = sUnlock;

            Button sUnlockOld = MakeBtn(T("进游戏页解锁（旧）", "Via game page"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 30, 210, 150, 30);
            sUnlockOld.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            sUnlockOld.Click += (s, e) => UnlockAllSam();
            pageSam.Controls.Add(sUnlockOld);

            Button sExp = MakeBtn(T("导出全库清单", "Export list"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 186, 210, 120, 30);
            sExp.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            sExp.Click += (s, e) => SamExportLibraryList();
            pageSam.Controls.Add(sExp);

            Button sOpenRep = MakeBtn(T("打开上次报告", "Last report"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 312, 210, 118, 30);
            sOpenRep.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            sOpenRep.Click += (s, e) =>
            {
                string p = Path.Combine(BASE_DIR, "_batch_report.txt");
                if (!File.Exists(p))
                {
                    SamConsoleSet("还没有生成过解锁报告，先点一次「一键全成就」。");
                    return;
                }
                try { Process.Start("notepad.exe", "\"" + p + "\""); } catch { try { Process.Start("explorer.exe", "/select," + p); } catch (Exception dg2566_1) { Diag.Swallow("MainForm.Ui.cs:1212", dg2566_1); } }
            };
            pageSam.Controls.Add(sOpenRep);

            Button sClr = MakeBtn(T("清空控制台", "Clear"), UiKit.A(48, 56, 70), C_SUB, 436, 210, 96, 30);
            sClr.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            sClr.Click += (s, e) => { SamConsoleSet(""); SamProgress(0, 0); lblSamState.Text = T("待命：点「一键全成就」开始", "Idle: press Unlock all"); };
            pageSam.Controls.Add(sClr);

            // v7.7：成就完成度面板（只读统计，不写账号）
            Button sPct = MakeBtn(T("成就完成度", "Completion"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 542, 210, 110, 30);
            sPct.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            sPct.Click += (s, e) => SamCompletionScan();
            pageSam.Controls.Add(sPct);

            samConsole = new TextBox();
            samConsole.Multiline = true;
            samConsole.ReadOnly = true;
            samConsole.ScrollBars = ScrollBars.None;   // 原生竖条压不黑，白条截在深色日志区右缘很扎眼；改为自动跟末尾
            samConsole.BorderStyle = BorderStyle.None;
            samConsole.BackColor = UiKit.Paper ? Color.FromArgb(40, 37, 32) : Ui2.CtlInk(UiKit.A(20, 24, 30));   // v8.8：反补，渲染后才是 20,24,30
            samConsole.ForeColor = UiKit.Paper ? Color.FromArgb(224, 218, 203) : UiKit.A(206, 222, 238);
            samConsole.Font = new Font("Consolas", 9F);
            samConsole.Location = new Point(30, 250);
            samConsole.Size = new Size(726, 340);
            samConsole.Text = "";
            samConsole.TextChanged += delegate
            {
                try { samConsole.SelectionStart = samConsole.TextLength; samConsole.ScrollToCaret(); }
                catch (Exception dg2595_1) { Diag.Swallow("MainForm.Ui.cs:1241", dg2595_1); }
            };
            pageSam.Controls.Add(samConsole);

            // v8.7：控制台空着的时候不再是一大片纯黑 + 左上角一行小字，改成居中一行淡提示；
            // 一旦有输出自动让位（透明 Label，不铺色块，不会遮住后面的日志）。
            Label samEmpty = new Label();
            samEmpty.Text = T("控制台就绪 · 点上方「一键全成就」后，进度与逐款结果会实时打在这里",
                "Console ready · press Unlock all, progress streams here");
            samEmpty.AutoSize = false;
            samEmpty.BackColor = Color.Transparent;
            samEmpty.ForeColor = UiKit.Paper ? Color.FromArgb(206, 198, 181) : UiKit.A(146, 158, 178);
            samEmpty.Font = new Font("Microsoft YaHei UI", 9.5F);
            samEmpty.TextAlign = ContentAlignment.MiddleCenter;
            samEmpty.Location = new Point(30, 250);
            samEmpty.Size = new Size(726, 340);
            pageSam.Controls.Add(samEmpty);
            samEmpty.BringToFront();
            samConsole.TextChanged += delegate
            {
                try { samEmpty.Visible = samConsole.TextLength == 0; } catch (Exception dg2615_1) { Diag.Swallow("MainForm.Ui.cs:1261", dg2615_1); }
            };

            samHost = new Panel();
            samHost.BackColor = UiKit.A(28, 32, 39);
            samHost.Location = new Point(30, 250);
            samHost.Size = new Size(726, 340);
            samHost.Visible = false;
            samHost.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawRectangle(p, 0, 0, samHost.Width - 1, samHost.Height - 1); };
            pageSam.Controls.Add(samHost);
            // 默认回到一体式控制台形态：宿主面板只在真正嵌入 SAM 时才占位，
            // 否则它会以一张空白纸卡盖住下面的墨色控制台。
            SamShowConsoleMode();

            samBarTrack = new Panel();
            samBarTrack.BackColor = UiKit.A(44, 52, 66);
            samBarTrack.Location = new Point(30, 598);
            samBarTrack.Size = new Size(726, 6);
            pageSam.Controls.Add(samBarTrack);

            samBarFill = new Panel();
            samBarFill.BackColor = UiKit.A(103, 232, 249);
            samBarFill.Location = new Point(0, 0);
            samBarFill.Size = new Size(0, 6);
            samBarTrack.Controls.Add(samBarFill);

            lblSamState = new Label();
            lblSamState.Text = T("待命 · 未开始", "Idle · not started");
            lblSamState.ForeColor = C_SUB;
            lblSamState.Font = new Font("Microsoft YaHei UI", 8F);
            lblSamState.Location = new Point(30, 610);
            lblSamState.AutoSize = true;
            pageSam.Controls.Add(lblSamState);

            Label sNote = new Label();
            sNote.Text = T("⚠ 修改成就属于作弊行为，部分游戏在线封禁，请仅在单机 / 自己账号下使用。", "⚠ Achievement editing may violate game ToS; use offline / on your own account only.");
            sNote.ForeColor = UiKit.A(240, 160, 90);
            sNote.Font = new Font("Microsoft YaHei UI", 8F);
            sNote.Location = new Point(30, 632);
            sNote.AutoSize = true;
            pageSam.Controls.Add(sNote);

            // ---------- 视频下载页 ----------
            pageVideo = new PagePanel();
            pageVideo.Dock = DockStyle.Fill;
            pageVideo.BackColor = Color.Transparent;
            pageVideo.Visible = false;
            content.Controls.Add(pageVideo);

            Label vTitle = new Label();
            vTitle.Text = T("视频解析", "Video Parser");
            vTitle.ForeColor = C_TEXT;
            vTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            vTitle.Location = new Point(30, 26);
            vTitle.AutoSize = true;
            pageVideo.Controls.Add(vTitle);

            Label vSub = new Label();
            vSub.Text = T("支持 B站 / 抖音 / 快手 / YouTube / 微博 / 腾讯 / 爱奇艺 / 优酷等 1000+ 平台 · 粘贴链接即可解析下载", "Supports 1000+ sites: Bilibili, Douyin, Kuaishou, YouTube, Weibo, Tencent, iQiyi, Youku... Paste a link to parse");
            vSub.ForeColor = C_SUB;
            vSub.Font = new Font("Microsoft YaHei UI", 9F);
            vSub.Location = new Point(30, 68);
            vSub.AutoSize = true;
            pageVideo.Controls.Add(vSub);

            Button vBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            vBack.Click += (s, e) => ShowVideoPage(false);
            pageVideo.Controls.Add(vBack);

            Label vUrlL = new Label();
            vUrlL.Text = T("视频链接", "Video URL");
            vUrlL.ForeColor = C_TEXT;
            vUrlL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vUrlL.Location = new Point(30, 100);
            vUrlL.AutoSize = true;
            pageVideo.Controls.Add(vUrlL);

            txtVUrl = MakeInput(30, 124, 560, 34, T("粘贴视频 / 播放列表链接，如 https://www.bilibili.com/video/BVxxxx", "Paste video / playlist URL, e.g. https://www.youtube.com/watch?v=xxxx"));
            pageVideo.Controls.Add(txtVUrl);

            btnVParse = MakeBtn(T("解析", "Parse"), C_BUBBLE_ALT(), Color.White, 602, 124, 80, 34);
            btnVParse.Click += (s, e) => VideoParse();
            pageVideo.Controls.Add(btnVParse);

            lblVInfo = new Label();
            lblVInfo.Text = T("尚未解析 · 输入链接后点击「解析」获取清晰度列表", "Not parsed yet · paste a link and press Parse");
            lblVInfo.ForeColor = C_SUB;
            lblVInfo.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblVInfo.Location = new Point(30, 168);
            lblVInfo.AutoSize = true;
            pageVideo.Controls.Add(lblVInfo);

            Label vFmtL = new Label();
            vFmtL.Text = T("清晰度 / 格式", "Quality / Format");
            vFmtL.ForeColor = C_TEXT;
            vFmtL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vFmtL.Location = new Point(30, 200);
            vFmtL.AutoSize = true;
            pageVideo.Controls.Add(vFmtL);

            cmbVFormat = new ComboBox();
            cmbVFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVFormat.BackColor = C_INPUT;
            cmbVFormat.ForeColor = C_INPUT_TEXT;
            cmbVFormat.FlatStyle = FlatStyle.Standard;
            cmbVFormat.Location = new Point(30, 224);
            cmbVFormat.Size = new Size(430, 30);
            cmbVFormat.Visible = false;
            pageVideo.Controls.Add(cmbVFormat);
            pageVideo.Controls.Add(MakeDropMain(cmbVFormat, 30, 224, 430, 30));

            btnVDl = MakeBtn(T("开始下载", "Download"), UiKit.ThemeColor, Color.White, 472, 224, 100, 30);
            btnVDl.Click += (s, e) => VideoDownload();
            pageVideo.Controls.Add(btnVDl);

            Label vDirL = new Label();
            vDirL.Text = T("保存到", "Save to");
            vDirL.ForeColor = C_TEXT;
            vDirL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vDirL.Location = new Point(30, 270);
            vDirL.AutoSize = true;
            pageVideo.Controls.Add(vDirL);

            lblVDir = new Label();
            lblVDir.Text = vOutDir;
            lblVDir.ForeColor = C_SUB;
            lblVDir.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblVDir.Location = new Point(30, 294);
            lblVDir.AutoSize = true;
            pageVideo.Controls.Add(lblVDir);

            Label vProxyL = new Label();
            vProxyL.Text = T("代理（可选）", "Proxy (optional)");
            vProxyL.ForeColor = C_TEXT;
            vProxyL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            vProxyL.Location = new Point(30, 326);
            pageVideo.Controls.Add(vProxyL);

            txtVProxy = MakeInput(30, 350, 390, 30, T("留空自动探测本机代理（10808/10809/7890）", "Leave empty to auto-detect local proxy (10808/10809/7890)"));
            pageVideo.Controls.Add(txtVProxy);

            btnVProxyAuto = MakeBtn(T("自动检测", "Auto"), C_BUBBLE_ALT(), Color.White, 432, 350, 90, 30);
            btnVProxyAuto.Click += (s, e) =>
            {
                string p = DetectProxy();
                txtVProxy.Text = p;
                lblVTip.Text = p.Length > 0 ? T("已检测到代理：" + p, "Detected proxy: " + p)
                                            : T("未检测到可用代理（检查代理软件是否开启），可手动填写。", "No proxy detected (check your proxy app), or enter manually.");
            };
            pageVideo.Controls.Add(btnVProxyAuto);

            // v12.0：市面级下载器都有的「只取音频」开关，勾上后不挑清晰度，直接抓最佳音轨转 mp3
            vAudioOnly = new RoundCheck();
            vAudioOnly.Text = T("仅音频 (mp3)", "Audio only (mp3)");
            vAudioOnly.ForeColor = UiKit.A(186, 196, 210);
            vAudioOnly.Font = new Font("Microsoft YaHei UI", 8.5F);
            vAudioOnly.AutoSize = true;
            vAudioOnly.Location = new Point(534, 354);
            pageVideo.Controls.Add(vAudioOnly);

            Button vBrowse = MakeBtn(T("更改目录", "Change"), C_BUBBLE_ALT(), Color.White, 470, 290, 90, 26);
            vBrowse.Click += (s, e) => PickVideoDir();
            pageVideo.Controls.Add(vBrowse);

            Button vOpen = MakeBtn(T("打开文件夹", "Open Folder"), C_BUBBLE_ALT(), Color.White, 572, 290, 98, 26);
            vOpen.Click += (s, e) => OpenVideoDir();
            pageVideo.Controls.Add(vOpen);

            Button vQueue = MakeBtn(T("批量队列", "Batch queue"), C_BUBBLE_ALT(), Color.White, 584, 224, 96, 30);
            vQueue.Click += (s, e) => ShowVideoQueue();
            pageVideo.Controls.Add(vQueue);

            lvVideos = MakeDarkList(langEn ? new string[] { "File", "Size", "Progress", "Speed", "Status" } : new string[] { "文件名", "大小", "进度", "速度", "状态" }, new int[] { 330, 80, 90, 90, 100 }, 30, 396, 700, 160);
            pageVideo.Controls.Add(lvVideos);

            // v13.2：压缩条——「市面级」下载器都带体积压制，这里做成一行，下载完就地压
            vCmpOn = new RoundCheck();
            vCmpOn.Text = T("下载后自动压缩", "Auto-compress");
            vCmpOn.ForeColor = UiKit.A(186, 196, 210);
            vCmpOn.Font = new Font("Microsoft YaHei UI", 8.5F);
            vCmpOn.AutoSize = true;
            vCmpOn.Location = new Point(30, 572);
            pageVideo.Controls.Add(vCmpOn);

            cmbVCmpMb = new ComboBox();
            cmbVCmpMb.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVCmpMb.BackColor = C_INPUT;
            cmbVCmpMb.ForeColor = C_INPUT_TEXT;
            cmbVCmpMb.FlatStyle = FlatStyle.Standard;
            cmbVCmpMb.Font = new Font("Microsoft YaHei UI", 9F);
            cmbVCmpMb.Items.AddRange(new object[] { "8 MB", "10 MB", "15 MB", "20 MB", "30 MB", "50 MB", "质量优先" });
            cmbVCmpMb.SelectedIndex = 3;
            cmbVCmpMb.Location = new Point(176, 566);
            cmbVCmpMb.Size = new Size(96, 26);
            pageVideo.Controls.Add(cmbVCmpMb);
            pageVideo.Controls.Add(MakeDropMain(cmbVCmpMb, 176, 566, 96, 26));

            btnVCmp = MakeBtn(T("压缩视频", "Compress"), UiKit.A(249, 115, 22), Color.White, 280, 564, 88, 28);
            btnVCmp.Click += (s, e) => VideoCompressPick();
            pageVideo.Controls.Add(btnVCmp);

            lblVCmp = new Label();
            lblVCmp.Text = T("结果存 _压缩 子目录", "Output to _压缩");
            lblVCmp.ForeColor = C_SUB;
            lblVCmp.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblVCmp.Location = new Point(376, 570);
            lblVCmp.AutoSize = false;
            lblVCmp.Size = new Size(348, 20);
            lblVCmp.TextAlign = ContentAlignment.MiddleLeft;
            pageVideo.Controls.Add(lblVCmp);

            lblVTip = new Label();
            lblVTip.Text = T("提示：单文件格式无需组件；「需FFmpeg」的高清格式首次会自动下载合并工具；国外平台需代理。", "Tip: single-file formats need no tools; FFmpeg-marked HD formats auto-download the merger; overseas sites need a proxy (blank = auto).");
            lblVTip.ForeColor = UiKit.A(240, 160, 90);
            lblVTip.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblVTip.Location = new Point(30, 604);
            lblVTip.AutoSize = true;
            pageVideo.Controls.Add(lblVTip);

            // v15.2：整块表单垫一张玻璃卡（链接解析 / 下载设置同一张，卡内画一条细分隔线分区），
            // 与列表之间留 12px。必须在 pageVideo 所有控件 Add 之后再加，否则会被后加的控件压住。
            Panel vCardA = new Panel();
            vCardA.BackColor = UiKit.Paper ? Color.FromArgb(238, 230, 212) : C_BUBBLE;
            vCardA.Location = new Point(30, 88);
            vCardA.Size = new Size(700, 300);
            vCardA.Paint += (s2, e2) =>
            {
                DrawGlassCard(e2.Graphics, new Rectangle(0, 0, 699, 299), 12,
                    UiKit.A(57, 66, 82), UiKit.A(34, 40, 52));
                using (Pen dp = new Pen(UiKit.Paper ? Color.FromArgb(206, 196, 177) : UiKit.AA(44, 255, 255, 255), 1f))
                    e2.Graphics.DrawLine(dp, 16, 104, 684, 104);
            };
            pageVideo.Controls.Add(vCardA);
            vCardA.SendToBack();

            // ---------- 音乐下载页 ----------
            pageMusic = new PagePanel();
            pageMusic.Dock = DockStyle.Fill;
            pageMusic.BackColor = Color.Transparent;
            pageMusic.Visible = false;
            content.Controls.Add(pageMusic);

            Label mTitle = new Label();
            mTitle.Text = T("音乐解析", "Music Parser");
            mTitle.ForeColor = C_TEXT;
            mTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            mTitle.Location = new Point(30, 26);
            mTitle.AutoSize = true;
            pageMusic.Controls.Add(mTitle);

            Label mSub = new Label();
            mSub.Text = T("网易云曲库在线搜索 · 一键下载 MP3 · 也支持粘贴网易云/QQ音乐等歌曲链接", "Search NetEase library & download MP3 · paste a song link to download");
            mSub.ForeColor = C_SUB;
            mSub.Font = new Font("Microsoft YaHei UI", 9F);
            mSub.Location = new Point(30, 68);
            mSub.AutoSize = true;
            pageMusic.Controls.Add(mSub);

            Button mBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            mBack.Click += (s, e) => ShowMusicPage(false);
            pageMusic.Controls.Add(mBack);

            // 输入框底板：单行 TextBox 会被字体压扁，套一层深色框让高度和按钮对齐
            Panel mInBg = new Panel();
            mInBg.BackColor = Ui2.CtlInk(C_INPUT);   // v8.8：Panel 同样被提亮，一并反补
            mInBg.Location = new Point(30, 100);
            mInBg.Size = new Size(236, 34);
            pageMusic.Controls.Add(mInBg);
            txtMSearch = MakeInput(40, 108, 216, 20, T("歌名 / 歌手", "Song / artist"));
            pageMusic.Controls.Add(txtMSearch);
            txtMSearch.BringToFront();
            Ui2.FrontPlaceholders(pageMusic);   // v8.8：上面这行会把占位提示压到下层，这里补回来

            // 平台切换用三段深色按钮，避免系统下拉框在深色界面里露白
            btnPlat0 = MakeBtn(T("网易云", "NetEase"), C_BUBBLE_ALT(), Color.White, 274, 100, 84, 34);
            btnPlat0.Click += (s, e) => SetPlat(0);
            pageMusic.Controls.Add(btnPlat0);

            btnPlat1 = MakeBtn(T("酷狗", "Kugou"), C_BUBBLE_ALT(), Color.White, 362, 100, 64, 34);
            btnPlat1.Click += (s, e) => SetPlat(1);
            pageMusic.Controls.Add(btnPlat1);

            btnPlat2 = MakeBtn(T("一起搜", "Both"), C_BUBBLE_ALT(), Color.White, 430, 100, 74, 34);
            btnPlat2.Click += (s, e) => SetPlat(2);
            pageMusic.Controls.Add(btnPlat2);

            btnMSearch = MakeBtn(T("搜索", "Search"), UiKit.A(59, 130, 246), Color.White, 512, 100, 84, 34);
            btnMSearch.Click += (s, e) => MusicSearch();
            pageMusic.Controls.Add(btnMSearch);

            btnMUrl = MakeBtn(T("粘贴链接下载", "Link Download"), C_BUBBLE_ALT(), Color.White, 596, 100, 128, 34);
            btnMUrl.Click += (s, e) => MusicByUrl();
            pageMusic.Controls.Add(btnMUrl);
            SetPlat(0);

            lvSongs = MakeDarkList(langEn ? new string[] { "Song", "Artist", "Album", "Length", "Source" } : new string[] { "歌曲", "歌手", "专辑", "时长", "平台" }, new int[] { 230, 140, 180, 60, 90 }, 30, 160, 700, 306);
            lvSongs.MouseDoubleClick += (s, e) => MusicDownloadSelected();
            pageMusic.Controls.Add(lvSongs);

            btnMDl = MakeBtn(T("下载选中歌曲", "Download Selected"), UiKit.ThemeColor, Color.White, 30, 480, 160, 34);
            btnMDl.Click += (s, e) => MusicDownloadSelected();
            pageMusic.Controls.Add(btnMDl);

            btnMOpen = MakeBtn(T("打开下载目录", "Open Folder"), C_BUBBLE_ALT(), Color.White, 200, 480, 130, 34);
            btnMOpen.Click += (s, e) => OpenMusicDir();
            pageMusic.Controls.Add(btnMOpen);

            Button btnMAll = MakeBtn(T("下载全部结果", "Download All"), C_BUBBLE_ALT(), Color.White, 340, 480, 160, 34);
            btnMAll.Click += (s, e) => MusicDownloadAll();
            pageMusic.Controls.Add(btnMAll);

            // 一起听：多人在同一房间同步听歌
            Button btnTing = MakeBtn(T("一起听 · 多人同步", "Listen Together"), UiKit.A(52, 60, 74), UiKit.A(198, 208, 222), 510, 480, 190, 34);
            btnTing.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnTing.Click += (s, e) =>
            {
                TingForm tf = new TingForm();
                tf.Show(this);
            };
            pageMusic.Controls.Add(btnTing);

            pbMusic = new ProgressBar();
            pbMusic.Minimum = 0;
            pbMusic.Maximum = 100;
            pbMusic.Style = ProgressBarStyle.Continuous;
            pbMusic.Location = new Point(30, 534);
            pbMusic.Size = new Size(700, 16);
            pbMusic.Visible = false;
            pageMusic.Controls.Add(pbMusic);

            lblMStatus = new Label();
            lblMStatus.Text = T("搜索后双击歌曲即可下载，或选中后点击「下载选中歌曲」", "Double-click a song to download, or select & press Download Selected");
            lblMStatus.ForeColor = C_SUB;
            lblMStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblMStatus.Location = new Point(30, 562);
            lblMStatus.AutoSize = true;
            pageMusic.Controls.Add(lblMStatus);

            // v15.2：搜索操作区垫一张玻璃卡（与去水印右栏同一套底），整排控件不再浮在背景上。
            // 注意 Controls.Add 是往 z-order 末尾（最底）插，所以必须放在本页所有控件之后再加。
            Panel mSearchCard = new Panel();
            mSearchCard.BackColor = C_BUBBLE;
            mSearchCard.Location = new Point(30, 88);
            mSearchCard.Size = new Size(700, 58);
            mSearchCard.Paint += (s2, e2) => DrawGlassCard(e2.Graphics, new Rectangle(0, 0, 699, 57), 12,
                UiKit.A(57, 66, 82), UiKit.A(34, 40, 52));
            pageMusic.Controls.Add(mSearchCard);
            mSearchCard.SendToBack();

            // ---------- 去水印页 ----------
            pageWatermark = new PagePanel();
            pageWatermark.Dock = DockStyle.Fill;
            pageWatermark.BackColor = Color.Transparent;
            pageWatermark.Visible = false;
            content.Controls.Add(pageWatermark);

            Label wTitle = new Label();
            wTitle.Text = T("水印擦除", "Watermark Removal");
            wTitle.ForeColor = C_TEXT;
            wTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            wTitle.Location = new Point(30, 26);
            wTitle.AutoSize = true;
            pageWatermark.Controls.Add(wTitle);

            Label wSub = new Label();
            wSub.Text = T("视频用 FFmpeg delogo 权重滤波，图片用周围像素智能填补 · 在预览图上按住鼠标拖动，框出水印区域即可",
                "Video: FFmpeg delogo · Image: smart inpaint · drag a box over the watermark in the preview");
            wSub.ForeColor = C_SUB;
            wSub.Font = new Font("Microsoft YaHei UI", 9F);
            wSub.Location = new Point(30, 68);
            wSub.AutoSize = true;
            pageWatermark.Controls.Add(wSub);

            Button wBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            wBack.Click += (s, e) => ShowWmPage(false);
            pageWatermark.Controls.Add(wBack);

            btnWmPick = MakeBtn(T("选择文件", "Pick File"), UiKit.A(59, 130, 246), Color.White, 30, 96, 106, 34);
            btnWmPick.Click += (s, e) => WmPickFile();
            pageWatermark.Controls.Add(btnWmPick);

            btnWmClear = MakeBtn(T("清除选区", "Clear Box"), C_BUBBLE_ALT(), Color.White, 144, 96, 96, 34);
            btnWmClear.Click += (s, e) =>
            {
                wmSel = Rectangle.Empty;
                if (wmPreviewBmp != null) wmPreview.Image = wmPreviewBmp;
                WmClearCoordBoxes();
                wmPreview.Invalidate();
                WmSetStatus(T("请按住鼠标左键在预览图上拖动，框出水印区域。", "Drag with the left mouse button over the preview to box the watermark."), C_SUB);
            };
            pageWatermark.Controls.Add(btnWmClear);

            lblWmFile = new Label();
            lblWmFile.Text = T("尚未选择文件 · 支持 MP4 / MKV / MOV / AVI / FLV / WEBM，以及 JPG / PNG / BMP / WEBP",
                "No file yet · Supports MP4/MKV/MOV/AVI/FLV/WEBM and JPG/PNG/BMP/WEBP");
            lblWmFile.ForeColor = C_SUB;
            lblWmFile.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblWmFile.Location = new Point(252, 104);
            lblWmFile.Size = new Size(478, 36);
            lblWmFile.AutoEllipsis = true;
            pageWatermark.Controls.Add(lblWmFile);

            wmPreview = new PictureBox();
            wmPreview.Location = new Point(30, 146);
            wmPreview.Size = new Size(420, 330);
            wmPreview.BackColor = UiKit.Paper ? Color.FromArgb(242, 235, 221) : UiKit.A(22, 26, 32);
            wmPreview.SizeMode = PictureBoxSizeMode.Zoom;
            wmPreview.BorderStyle = BorderStyle.FixedSingle;
            wmPreview.Cursor = Cursors.Cross;
            wmPreview.MouseDown += (s, e) => WmMouseDown(e);
            wmPreview.MouseMove += (s, e) => WmMouseMove(e);
            wmPreview.MouseUp += (s, e) => WmMouseUp(e);
            wmPreview.Paint += (s, e) =>
            {
                // 系统会把 PictureBox 的底色画成浅灰，这里强制铺一层纸面/深色，保证预览区不被系统白块占掉
                e.Graphics.Clear(UiKit.Paper ? Color.FromArgb(242, 235, 221) : UiKit.A(22, 26, 32));
                WmPaint(e);
            };
            wmPreview.AllowDrop = true;
            wmPreview.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
                else e.Effect = DragDropEffects.None;
            };
            wmPreview.DragDrop += (s, e) =>
            {
                try
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0) WmLoadFile(files[0]);
                }
                catch (Exception dg3043_1) { Diag.Swallow("MainForm.Ui.cs:1689", dg3043_1); }
            };
            pageWatermark.Controls.Add(wmPreview);

            Label wModeL = new Label();
            wModeL.Text = T("处理模式", "Mode");
            wModeL.ForeColor = C_TEXT;
            wModeL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            wModeL.Location = new Point(470, 146);
            wModeL.AutoSize = true;
            pageWatermark.Controls.Add(wModeL);

            cmbWmMode = new ComboBox();
            cmbWmMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbWmMode.BackColor = C_INPUT;
            cmbWmMode.ForeColor = C_INPUT_TEXT;
            cmbWmMode.FlatStyle = FlatStyle.Standard;
            cmbWmMode.Font = new Font("Microsoft YaHei UI", 9F);
            cmbWmMode.Location = new Point(470, 170);
            cmbWmMode.Size = new Size(252, 30);
            cmbWmMode.Items.Add(T("自动识别（推荐）", "Auto (recommended)"));
            cmbWmMode.Items.Add(T("视频 · delogo 滤波", "Video · delogo"));
            cmbWmMode.Items.Add(T("图片 · 智能填补", "Image · smart inpaint"));
            cmbWmMode.Items.Add(T("图片 · 高斯模糊", "Image · blur"));
            cmbWmMode.Items.Add(T("图片 · 马赛克", "Image · mosaic"));
            cmbWmMode.SelectedIndex = 0;
            pageWatermark.Controls.Add(cmbWmMode);
            cmbWmMode.Visible = false;
            pageWatermark.Controls.Add(MakeDropMain(cmbWmMode, 470, 170, 252, 30));

            Label wAreaL = new Label();
            wAreaL.Text = T("水印区域（图像像素）", "Region (image pixels)");
            wAreaL.ForeColor = C_TEXT;
            wAreaL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            wAreaL.Location = new Point(470, 212);
            wAreaL.AutoSize = true;
            pageWatermark.Controls.Add(wAreaL);
            lblWmAreaTitle = wAreaL;

            // v8.7：X/Y/W/H 由"一行三个再拐一行"改成对齐的 2x2 网格，输入框从 54 加宽到 100，
            // 行末那个空竖条和歪掉的高/宽一行一起消失。
            Label wXL = WmMiniLabel("X", 470, 240);
            pageWatermark.Controls.Add(wXL);
            txtWmX = MakeInput(486, 236, 100, 28, null);
            txtWmX.Text = "0";
            txtWmX.TextChanged += (s, e) => WmBoxesToSel();
            pageWatermark.Controls.Add(txtWmX);

            Label wYL = WmMiniLabel("Y", 600, 240);
            pageWatermark.Controls.Add(wYL);
            txtWmY = MakeInput(616, 236, 100, 28, null);
            txtWmY.Text = "0";
            txtWmY.TextChanged += (s, e) => WmBoxesToSel();
            pageWatermark.Controls.Add(txtWmY);

            Label wWL = WmMiniLabel(T("宽", "W"), 470, 272);
            pageWatermark.Controls.Add(wWL);
            txtWmW = MakeInput(486, 268, 100, 28, null);
            txtWmW.Text = "0";
            txtWmW.TextChanged += (s, e) => WmBoxesToSel();
            pageWatermark.Controls.Add(txtWmW);

            Label wHL = WmMiniLabel(T("高", "H"), 600, 272);
            pageWatermark.Controls.Add(wHL);
            txtWmH = MakeInput(616, 268, 100, 28, null);
            txtWmH.Text = "0";
            txtWmH.TextChanged += (s, e) => WmBoxesToSel();
            pageWatermark.Controls.Add(txtWmH);

            Label wOutL = new Label();
            wOutL.Text = T("输出到", "Output");
            wOutL.ForeColor = C_TEXT;
            wOutL.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            wOutL.Location = new Point(470, 306);
            wOutL.AutoSize = true;
            pageWatermark.Controls.Add(wOutL);

            lblWmOut = new Label();
            lblWmOut.Text = wmOutDir;
            lblWmOut.ForeColor = C_SUB;
            lblWmOut.Font = new Font("Microsoft YaHei UI", 8F);
            lblWmOut.Location = new Point(470, 328);
            lblWmOut.Size = new Size(252, 18);
            lblWmOut.AutoEllipsis = true;
            pageWatermark.Controls.Add(lblWmOut);

            btnWmOut = MakeBtn(T("更改目录", "Change"), C_BUBBLE_ALT(), Color.White, 470, 350, 92, 28);
            btnWmOut.Click += (s, e) => WmPickOutDir();
            pageWatermark.Controls.Add(btnWmOut);

            btnWmOpen = MakeBtn(T("打开", "Open"), C_BUBBLE_ALT(), Color.White, 566, 350, 68, 28);
            btnWmOpen.Click += (s, e) => WmOpenDir();
            pageWatermark.Controls.Add(btnWmOpen);

            btnWmAddArea = MakeBtn(T("＋区域", "+Area"), C_BUBBLE_ALT(), Color.White, 640, 350, 80, 28);
            btnWmAddArea.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnWmAddArea.Click += (s, e) => WmAddAreaNow();
            pageWatermark.Controls.Add(btnWmAddArea);

            Label wTimeL = new Label();
            wTimeL.Text = T("视频时间段（秒，0 = 全片）", "Video range (sec, 0 = whole)");
            wTimeL.ForeColor = C_TEXT;
            wTimeL.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            wTimeL.Location = new Point(470, 440);
            wTimeL.AutoSize = true;
            pageWatermark.Controls.Add(wTimeL);

            txtWmStart = MakeInput(470, 458, 96, 26, null);
            pageWatermark.Controls.Add(txtWmStart);
            Label wArrow = WmMiniLabel("→", 572, 460);
            pageWatermark.Controls.Add(wArrow);
            txtWmDur = MakeInput(592, 458, 96, 26, null);
            pageWatermark.Controls.Add(txtWmDur);
            Label wDurHint = new Label();
            wDurHint.Text = T("时长", "len");
            wDurHint.ForeColor = C_SUB;
            wDurHint.Font = new Font("Microsoft YaHei UI", 7.5F);
            wDurHint.Location = new Point(694, 462);
            wDurHint.AutoSize = true;
            pageWatermark.Controls.Add(wDurHint);

            btnWmRun = MakeBtn(T("开始处理", "Process"), UiKit.ThemeColor, Color.White, 470, 392, 128, 44);
            btnWmRun.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            btnWmRun.Click += (s, e) => WmRun();
            pageWatermark.Controls.Add(btnWmRun);

            btnWmBatch = MakeBtn(T("批量队列", "Batch"), C_BUBBLE_ALT(), Color.White, 600, 392, 120, 44);
            btnWmBatch.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btnWmBatch.Click += (s, e) => WmBatchPick();
            pageWatermark.Controls.Add(btnWmBatch);

            lblWmStatus = new Label();
            lblWmStatus.Text = "";   // v17.9：拖动说明在标题下面已经写了，这里只做进度/结果行，不再重复
            lblWmStatus.ForeColor = C_SUB;
            lblWmStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblWmStatus.Location = new Point(30, 490);
            lblWmStatus.Size = new Size(700, 18);
            lblWmStatus.AutoEllipsis = true;
            pageWatermark.Controls.Add(lblWmStatus);

            lvWm = MakeDarkList(langEn ? new string[] { "File", "Mode", "Output", "Status" } : new string[] { "文件", "模式", "输出", "状态" },
                new int[] { 300, 150, 190, 60 }, 30, 514, 700, 112);
            pageWatermark.Controls.Add(lvWm);

            Label wTip = new Label();
            wTip.Text = T("提示：视频去水印会用 FFmpeg 重新编码（画质基本无损，耗时与视频长度相关）；图片处理直接写新文件，不会覆盖原图。",
                "Tip: video re-encodes with FFmpeg (near-lossless, time grows with length); images are written to a new file, originals untouched.");
            wTip.ForeColor = UiKit.A(240, 160, 90);
            wTip.Font = new Font("Microsoft YaHei UI", 8F);
            wTip.Location = new Point(30, 628);
            wTip.Size = new Size(700, 18);
            wTip.AutoEllipsis = true;
            pageWatermark.Controls.Add(wTip);

            // v15.1：右侧设置区原来直接浮在页面背景上，控件像散在空地上；这里补一块玻璃卡托住，
            // 只加底、不动任何控件坐标（处理模式 / 水印区域 / 输出到 / 时间段都落在 (458,134)-(730,506) 内）。
            // 必须放在所有右栏控件之后 Add：Controls.Add 是往 z-order 末尾（最底）插，先加会被后加的控件压住。
            Panel wmSide = new Panel();
            wmSide.BackColor = C_BUBBLE;
            wmSide.Location = new Point(458, 128);
            wmSide.Size = new Size(272, 382);
            wmSide.Paint += (s2, e2) => DrawGlassCard(e2.Graphics,
                new Rectangle(0, 0, Math.Max(8, wmSide.Width - 1), Math.Max(8, wmSide.Height - 1)), 12,
                UiKit.A(57, 66, 82), UiKit.A(34, 40, 52));
            pageWatermark.Controls.Add(wmSide);
            wmSide.SendToBack();
            // v17.9：右栏那堆控件原来是页面的兄弟控件，标签透出的是"页面底色"而不是卡片玻璃，
            // 在浅一档的卡片上就成了一块块黑底。改挂在卡片里，坐标换算成卡片内部坐标，
            // 卡片四角再用圆角 Region 裁掉，边角也就干净了。
            try
            {
                System.Collections.Generic.List<Control> mv = new System.Collections.Generic.List<Control>();
                foreach (Control c in pageWatermark.Controls)
                {
                    if (c == wmSide) continue;
                    if (c.Left >= 458 && c.Left <= 745 && c.Top >= 128 && c.Top <= 512) mv.Add(c);
                }
                foreach (Control c in mv)
                {
                    int lx = c.Left - 458, ty = c.Top - 128;
                    c.Parent = wmSide;
                    c.Left = lx;
                    c.Top = ty;
                }
                wmSide.SizeChanged += delegate
                {
                    try
                    {
                        using (GraphicsPath p3 = UiKit.RoundedPath(new Rectangle(0, 0, wmSide.Width - 1, wmSide.Height - 1), 12))
                        {
                            Region r3 = wmSide.Region;
                            wmSide.Region = new Region(p3);
                            if (r3 != null) r3.Dispose();
                        }
                    }
                    catch (Exception dg3238_1) { Diag.Swallow("MainForm.Ui.cs:1884", dg3238_1); }
                };
                pageWatermark.Layout += (sx, ex) =>
                {
                    try
                    {
                        if (lvWm != null && lvWm.Width > 120 && lvWm.Right > wmSide.Left + 180)
                            wmSide.Width = lvWm.Right - wmSide.Left;
                    }
                    catch (Exception dg3247_1) { Diag.Swallow("MainForm.Ui.cs:1893", dg3247_1); }
                };
                if (wmSide.Width > 8)
                {
                    using (GraphicsPath p3 = UiKit.RoundedPath(new Rectangle(0, 0, wmSide.Width - 1, wmSide.Height - 1), 12))
                        wmSide.Region = new Region(p3);
                }
            }
            catch (Exception dg3255_1) { Diag.Swallow("MainForm.Ui.cs:1901", dg3255_1); }

            // ================= Steam 账号价值 =================
            pageValue = new PagePanel();
            pageValue.Dock = DockStyle.Fill;
            pageValue.BackColor = Color.Transparent;
            pageValue.Visible = false;
            content.Controls.Add(pageValue);

            Label vaTitle = new Label();
            vaTitle.Text = T("资产估值", "Account Value");
            vaTitle.ForeColor = C_TEXT;
            vaTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            vaTitle.Location = new Point(30, 26);
            vaTitle.AutoSize = true;
            pageValue.Controls.Add(vaTitle);

            Label vaSub = new Label();
            vaSub.Text = T("读取本机 Steam 账号的全部游戏清单，采样中国区商店原价推算账号资产价值",
                "Scan your local Steam library and estimate game value with CN store original prices");
            vaSub.ForeColor = C_SUB;
            vaSub.Font = new Font("Microsoft YaHei UI", 9F);
            vaSub.Location = new Point(30, 68);
            vaSub.AutoSize = true;
            pageValue.Controls.Add(vaSub);

            Button vaBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            vaBack.Click += (s, e) => ShowValuePage(false);
            pageValue.Controls.Add(vaBack);

            btnValScan = MakeBtn(T("扫描账号", "Scan Library"), UiKit.A(59, 130, 246), Color.White, 30, 96, 124, 34);
            btnValScan.Click += (s, e) => ValStartScan();
            pageValue.Controls.Add(btnValScan);

            btnValReal = MakeBtn(T("查真实价", "Real Prices"), UiKit.A(16, 185, 129), Color.White, 162, 96, 124, 34);
            btnValReal.Click += (s, e) => ValRealPrices(false);
            pageValue.Controls.Add(btnValReal);

            Button vStore = MakeBtn(T("打开 Steam 库", "Open Steam"), C_BUBBLE_ALT(), Color.White, 294, 96, 124, 34);
            vStore.Click += (s, e) =>
            {
                try { Process.Start("https://store.steampowered.com/account/licenses/"); }
                catch
                {
                    try { Process.Start("steam://open/games"); } catch (Exception dg3299_1) { Diag.Swallow("MainForm.Ui.cs:1945", dg3299_1); }
                }
            };
            pageValue.Controls.Add(vStore);

            Button vCsv = MakeBtn(T("导出清单", "Export CSV"), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 426, 96, 124, 34);
            vCsv.Click += (s, e) => ValExportCsv();
            pageValue.Controls.Add(vCsv);

            lblValPath = new Label();
            lblValPath.Text = T("尚未扫描 · 将读取本机 Steam 账号游戏清单并采样查价", "Not scanned yet");
            lblValPath.ForeColor = C_SUB;
            lblValPath.Font = new Font("Microsoft YaHei UI", 8F);
            lblValPath.Location = new Point(30, 132);
            lblValPath.Size = new Size(690, 14);
            lblValPath.AutoEllipsis = true;
            pageValue.Controls.Add(lblValPath);

            string[] vCapZh = { "账号游戏", "估算总价值", "采样均价", "库占容量" };
            string[] vCapEn = { "Games", "Est. value", "Avg price", "Library size" };
            Label[] vVals = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                Panel cd = new Panel();
                cd.BackColor = Color.Transparent;
                cd.Location = new Point(30 + i * 175, 146);
                cd.Size = new Size(165, 82);
                cd.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle cr = new Rectangle(0, 0, cd.Width - 1, cd.Height - 1);
                    using (GraphicsPath path = UiKit.RoundedPath(cr, 12))
                    {
                        e.Graphics.SetClip(path);
                        if (UiKit.Paper)
                        {
                            using (LinearGradientBrush pbr = new LinearGradientBrush(cr,
                                Color.FromArgb(255, 252, 243), Color.FromArgb(247, 241, 228), 90F))
                                e.Graphics.FillRectangle(pbr, cr);
                        }
                        else
                        using (LinearGradientBrush br = new LinearGradientBrush(cr,
                            UiKit.A(60, 69, 86), UiKit.A(37, 43, 56), 90F))
                            e.Graphics.FillRectangle(br, cr);
                        // 顶部 3px 彩色条
                        Rectangle tr = new Rectangle(0, 0, cd.Width, 3);
                        using (LinearGradientBrush tb = new LinearGradientBrush(tr,
                            UiKit.A(96, 165, 250), UiKit.A(147, 197, 253), 0F))
                            e.Graphics.FillRectangle(tb, tr);
                        // 顶部玻璃高光
                        using (LinearGradientBrush hl = new LinearGradientBrush(
                            new Rectangle(0, 0, cd.Width, 40), UiKit.AA(34, 255, 255, 255), UiKit.AA(0, 255, 255, 255), 90F))
                            e.Graphics.FillRectangle(hl, 0, 0, cd.Width, 40);
                        e.Graphics.ResetClip();
                        using (Pen pp = new Pen(UiKit.AA(52, 255, 255, 255), 1f))
                            e.Graphics.DrawPath(pp, path);
                    }
                };
                Label cap = new Label();
                cap.Text = T(vCapZh[i], vCapEn[i]);
                cap.BackColor = Color.Transparent;
                cap.ForeColor = UiKit.A(166, 180, 200);
                cap.Font = new Font("Microsoft YaHei UI", 8.5F);
                cap.Location = new Point(13, 12);
                cap.AutoSize = true;
                cd.Controls.Add(cap);
                Label vv = new Label();
                vv.Text = "--";
                vv.BackColor = Color.Transparent;
                vv.ForeColor = UiKit.A(238, 244, 255);
                vv.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
                vv.Location = new Point(12, 36);
                vv.Size = new Size(143, 32);
                vv.AutoEllipsis = true;
                cd.Controls.Add(vv);
                cap.BackColor = Color.Transparent; vv.BackColor = Color.Transparent;
                cap.UseCompatibleTextRendering = true; vv.UseCompatibleTextRendering = true;
                pageValue.Controls.Add(cd);
                vVals[i] = vv;
            }
            lblValGames = vVals[0];
            lblValWorth = vVals[1];
            lblValAvg = vVals[2];
            lblValSize = vVals[3];

            lvValue = MakeDarkList(langEn ? new string[] { "Game", "Original price", "Size", "AppID" } : new string[] { "游戏", "商店原价", "占用", "AppID" },
                new int[] { 330, 130, 120, 90 }, 30, 246, 700, 228);
            pageValue.Controls.Add(lvValue);

            lblValStatus = new Label();
            lblValStatus.Text = "";   // v17.9：空态里已有"这里还没有内容"，这句只留给扫描进度用
            lblValStatus.ForeColor = C_SUB;
            lblValStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblValStatus.Location = new Point(30, 484);
            lblValStatus.Size = new Size(700, 18);
            lblValStatus.AutoEllipsis = true;
            pageValue.Controls.Add(lblValStatus);

            // v15.2：实测 ReadOnly=true 的多行 TextBox 会忽略 BackColor、直接从系统色画成一块灰砖（整页最亮）
            // （v8.x 注释"连设成纯红都不生效"就是同一个原因）。这里改成"非 ReadOnly + 屏蔽键入"：
            // 底色恢复受控，选中 / Ctrl+C 复制照旧，只是不能往里打字。
            lblValDetail = new TextBox();
            lblValDetail.Multiline = true;
            lblValDetail.ReadOnly = true;   // v17.9：可编辑态下这个 Edit 控件会被主题刷成浅灰，改成只读才是深色
            lblValDetail.ScrollBars = ScrollBars.None;   // 原生竖滚动条压不黑，白条很扎眼；改为自动跟到末尾
            lblValDetail.WordWrap = true;
            lblValDetail.Text = "";
            lblValDetail.KeyDown += delegate (object s3, KeyEventArgs e3)
            { if (!e3.Control) e3.SuppressKeyPress = true; };   // 只放行 Ctrl 组合（Ctrl+C / A），其余一律不落字
            lblValDetail.BackColor = Ui2.CtlInk(UiKit.A(14, 18, 26));   // v17.9：日志凹槽比玻璃卡沉一档
            lblValDetail.ForeColor = UiKit.A(196, 208, 226);
            lblValDetail.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblValDetail.BorderStyle = BorderStyle.None;
            lblValDetail.Location = new Point(42, 517);
            lblValDetail.Size = new Size(676, 102);
            lblValDetail.TextChanged += delegate
            {
                try { lblValDetail.SelectionStart = lblValDetail.TextLength; lblValDetail.ScrollToCaret(); }
                catch (Exception dg3408_1) { Diag.Swallow("MainForm.Ui.cs:2054", dg3408_1); }
            };
            pageValue.Controls.Add(lblValDetail);

            Label vTip = new Label();
            vTip.Text = T("提示：清单取自本机 Steam 配置（含未安装），价格按中国区原价、免费计 0；接口不支持批量，总值按采样均价推算。", "Tip: list from local Steam config (incl. uninstalled); CN original prices, free = 0; total estimated from a sample.");
            vTip.ForeColor = UiKit.A(240, 160, 90);
            vTip.Font = new Font("Microsoft YaHei UI", 8F);
            vTip.Location = new Point(30, 638);
            vTip.Size = new Size(700, 18);
            vTip.AutoEllipsis = true;
            pageValue.Controls.Add(vTip);

            // v15.2：扫描详情那块灰板原来直接压在页面底上，像一块没上漆的砖；外面补一张玻璃卡，
            // 灰板落进卡里就成了"日志凹槽"，与全站输入框同一档深色。
            Panel vaLog = new Panel();
            vaLog.BackColor = C_BUBBLE;
            vaLog.Location = new Point(30, 508);
            vaLog.Size = new Size(700, 120);
            vaLog.Paint += (s2, e2) =>
            {
                DrawGlassCard(e2.Graphics,
                    new Rectangle(0, 0, Math.Max(8, vaLog.Width - 1), Math.Max(8, vaLog.Height - 1)), 12,
                    UiKit.A(57, 66, 82), UiKit.A(34, 40, 52));
                // v17.9：这个 Edit 控件在深色主题下会被系统刷成一块浅灰（改底色、改只读都无效），
                // 干脆让它只当文本载体，日志由卡片自绘，贴底显示最新几行，和全站深色凹槽一致。
                try
                {
                    string tx = lblValDetail.Text;
                    if (!string.IsNullOrEmpty(tx))
                    {
                        Rectangle ir = new Rectangle(14, 10, Math.Max(20, vaLog.Width - 28), Math.Max(16, vaLog.Height - 20));
                        if (UiKit.Paper)
                        {
                            using (SolidBrush vb = new SolidBrush(Color.FromArgb(62, 56, 45)))
                                e2.Graphics.DrawString(tx, lblValDetail.Font, vb, ir);
                        }
                        else
                            TextRenderer.DrawText(e2.Graphics, tx, lblValDetail.Font, ir, UiKit.A(196, 208, 226),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Bottom);
                    }
                }
                catch (Exception dg3444_1) { Diag.Swallow("MainForm.Ui.cs:2090", dg3444_1); }
            };
            pageValue.Controls.Add(vaLog);
            vaLog.SendToBack();
            lblValDetail.Visible = false;                       // 只做文本载体，不自己上屏
            vaLog.Visible = false;                              // 空态不铺一张空卡，有扫描输出时再出现
            lblValDetail.TextChanged += (sx, ex) => { try { vaLog.Visible = lblValDetail.TextLength > 0; vaLog.Invalidate(); } catch (Exception dg3449_1) { Diag.Swallow("MainForm.Ui.cs:2095", dg3449_1); } };

            // ================= 电脑配置 =================
            pageSys = new PagePanel();
            pageSys.Dock = DockStyle.Fill;
            pageSys.BackColor = Color.Transparent;
            pageSys.Visible = false;
            content.Controls.Add(pageSys);

            Label syTitle = new Label();
            syTitle.Text = T("硬件信息", "Hardware Info");
            syTitle.ForeColor = C_TEXT;
            syTitle.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);   // v17.6：页名已上移 Header，页内降为小节标题
            syTitle.Location = new Point(30, 26);
            syTitle.AutoSize = true;
            pageSys.Controls.Add(syTitle);

            Label sySub = new Label();
            sySub.Text = T("读取本机 CPU / 显卡 / 内存 / 硬盘 / 主板 / 系统信息，可一键复制成文本发给别人",
                "Read CPU / GPU / RAM / disk / motherboard / OS, copy as text in one click");
            sySub.ForeColor = C_SUB;
            sySub.Font = new Font("Microsoft YaHei UI", 9F);
            sySub.Location = new Point(30, 68);
            sySub.AutoSize = true;
            pageSys.Controls.Add(sySub);

            Button syBack = MakeBtn(T("返回定时", "Back"), UiKit.A(34, 41, 53), UiKit.A(198, 208, 222), 620, 26, 90, 28);
            syBack.Click += (s, e) => ShowSysPage(false);
            pageSys.Controls.Add(syBack);

            btnSysScan = MakeBtn(T("重新检测", "Detect Again"), UiKit.A(59, 130, 246), Color.White, 30, 96, 116, 34);
            btnSysScan.Click += (s, e) => SysStartScan();
            pageSys.Controls.Add(btnSysScan);

            btnSysCopy = MakeBtn(T("复制配置", "Copy"), C_BUBBLE_ALT(), Color.White, 154, 96, 110, 34);
            btnSysCopy.Click += (s, e) => SysCopy();
            pageSys.Controls.Add(btnSysCopy);

            Button btnSysReport = MakeBtn(T("导出报告图", "Export Card"), UiKit.A(52, 60, 74), UiKit.A(198, 208, 222), 272, 96, 128, 34);
            btnSysReport.Click += (s, e) => SysExportReport();
            pageSys.Controls.Add(btnSysReport);

            Button btnCheckup = MakeBtn(T("一键体检", "Checkup"), UiKit.A(52, 60, 74), UiKit.A(198, 208, 222), 410, 96, 110, 34);
            btnCheckup.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnCheckup.Click += (s, e) => ShowCheckup();
            pageSys.Controls.Add(btnCheckup);

            Button btnBoot = MakeBtn(T("开机启动项", "Startup"), UiKit.A(52, 60, 74), UiKit.A(198, 208, 222), 526, 96, 110, 34);
            btnBoot.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnBoot.Click += (s, e) => ShowStartupManager();
            pageSys.Controls.Add(btnBoot);

            Button btnXfer = MakeBtn(T("手机互传", "Phone Xfer"), UiKit.A(52, 60, 74), UiKit.A(198, 208, 222), 642, 96, 90, 34);
            btnXfer.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnXfer.Click += (s, e) => ShowTransfer();
            pageSys.Controls.Add(btnXfer);

            Button btnPlus = MakeBtn(T("▸ 增强工具", "▸ Power Tools"), UiKit.A(96, 165, 250), Color.White, 452, 26, 150, 28);
            btnPlus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnPlus.Click += (s, e) => ShowPlusCenter();
            pageSys.Controls.Add(btnPlus);

            // —— 实时负载三卡 ——
            string[] ldZh = { "CPU 使用率", "内存占用", "C 盘可用" };
            string[] ldEn = { "CPU usage", "Memory", "C: free" };
            for (int i = 0; i < 3; i++)
            {
                Panel cd = new Panel();
                cd.BackColor = Color.Transparent;
                cd.Location = new Point(30 + i * 238, 146);
                cd.Size = new Size(224, 74);
                int ci = i;
                cd.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle cr = new Rectangle(0, 0, cd.Width - 1, cd.Height - 1);
                    Color[] lc = { UiKit.A(96, 165, 250), UiKit.A(16, 185, 129), UiKit.A(249, 115, 22) };
                    using (GraphicsPath path = UiKit.RoundedPath(cr, 12))
                    {
                        e.Graphics.SetClip(path);
                        if (UiKit.Paper)
                        {
                            using (LinearGradientBrush pbr = new LinearGradientBrush(cr,
                                Color.FromArgb(255, 252, 243), Color.FromArgb(247, 241, 228), 90F))
                                e.Graphics.FillRectangle(pbr, cr);
                        }
                        else
                        using (LinearGradientBrush br = new LinearGradientBrush(cr,
                            UiKit.A(60, 69, 86), UiKit.A(37, 43, 56), 90F))
                            e.Graphics.FillRectangle(br, cr);
                        Rectangle tr = new Rectangle(0, 0, cd.Width, 3);
                        using (LinearGradientBrush tb = new LinearGradientBrush(tr, lc[ci], UiKit.Lighten(lc[ci], 65), 0F))
                            e.Graphics.FillRectangle(tb, tr);
                        int barW = cd.Width - 28;
                        double pv = sysPct[ci];
                        if (pv < 0) pv = 0; if (pv > 100) pv = 100;
                        int fill = (int)Math.Round(barW * pv / 100.0);
                        using (SolidBrush bgB = new SolidBrush(UiKit.A(40, 48, 60)))
                            e.Graphics.FillRectangle(bgB, 14, cd.Height - 8, barW, 4);
                        using (SolidBrush fgB = new SolidBrush(lc[ci]))
                            e.Graphics.FillRectangle(fgB, 14, cd.Height - 8, Math.Max(2, fill), 4);
                        e.Graphics.ResetClip();
                        using (Pen pp = new Pen(UiKit.AA(52, 255, 255, 255), 1f))
                            e.Graphics.DrawPath(pp, path);
                    }
                };
                Label cap = new Label();
                cap.Text = T(ldZh[i], ldEn[i]);
                cap.BackColor = Color.Transparent;
                cap.ForeColor = UiKit.A(166, 180, 200);
                cap.Font = new Font("Microsoft YaHei UI", 8.5F);
                cap.Location = new Point(13, 4);
                cap.AutoSize = true;
                cd.Controls.Add(cap);
                Label vv = new Label();
                vv.Text = "--";
                vv.BackColor = Color.Transparent;
                vv.ForeColor = UiKit.A(238, 244, 255);
                vv.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
                vv.Location = new Point(12, 22);
                vv.Size = new Size(150, 24);
                cd.Controls.Add(vv);
                Label sm = new Label();
                sm.Text = T("采样中", "sampling");
                sm.BackColor = Color.Transparent;
                sm.ForeColor = UiKit.A(126, 140, 160);
                sm.Font = new Font("Microsoft YaHei UI", 7.5F);
                sm.Location = new Point(13, 48);
                sm.Size = new Size(150, 14);
                cd.Controls.Add(sm);
                pageSys.Controls.Add(cd);
                sysBars[i] = cd;
                sysVals[i] = vv;
                sysSubs[i] = sm;
            }

            lvSys = MakeDarkList(langEn ? new string[] { "Item", "Details" } : new string[] { "项目", "详情" },
                new int[] { 180, 510 }, 30, 232, 700, 284);
            pageSys.Controls.Add(lvSys);

            lblSysStatus = new Label();
            lblSysStatus.Text = T("正在读取硬件信息…", "Reading hardware info...");
            lblSysStatus.ForeColor = C_SUB;
            lblSysStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSysStatus.Location = new Point(30, 522);
            lblSysStatus.Size = new Size(700, 18);
            lblSysStatus.AutoEllipsis = true;
            pageSys.Controls.Add(lblSysStatus);

            lblSysGpu = new Label();
            lblSysGpu.Text = T("显卡读数准备中…", "Reading GPU...");
            lblSysGpu.ForeColor = UiKit.A(192, 132, 252);
            lblSysGpu.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            lblSysGpu.Location = new Point(30, 546);
            lblSysGpu.Size = new Size(700, 18);
            lblSysGpu.AutoEllipsis = true;
            pageSys.Controls.Add(lblSysGpu);

            Label sTip = new Label();
            sTip.Text = T("提示：硬件信息由 Windows WMI 采集，负载数值每 2 秒实时刷新；导出报告会在程序目录生成一张 PNG 卡片。",
                "Tip: specs come from WMI, load meters refresh every 2s; export writes a PNG card.");
            sTip.ForeColor = UiKit.A(240, 160, 90);
            sTip.Font = new Font("Microsoft YaHei UI", 8F);
            sTip.Location = new Point(30, 570);

            Button btnIso = MakeBtn(T("系统镜像下载", "ISO Download"), UiKit.A(139, 92, 246), Color.White, 30, 594, 170, 34);
            btnIso.Click += (s, e) => OpenIsoDialog();
            pageSys.Controls.Add(btnIso);

            Button btnWg = MakeBtn(T("常用软件一键装", "Install Apps"), UiKit.A(45, 212, 191), Color.Black, 210, 594, 180, 34);
            btnWg.Click += (s, e) => OpenWingetDialog();
            pageSys.Controls.Add(btnWg);
            sTip.Size = new Size(700, 18);
            sTip.AutoEllipsis = true;
            pageSys.Controls.Add(sTip);

            sysLoadTimer = new System.Windows.Forms.Timer();
            sysLoadTimer.Interval = 2000;
            sysLoadTimer.Tick += (s, e) => SysLoadTick();
            sysLoadTimer.Start();


            Panel topbar = new Panel();
            topbar.BackColor = C_MAIN;
            topbar.Location = new Point(0, 0);
            topbar.Size = new Size(760, 48);
            topbar.Paint += (s, e) =>
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 46, 760, 2), UiKit.A(61, 126, 255), UiKit.A(38, 43, 51), 0f))
                    e.Graphics.FillRectangle(br, 0, 46, 760, 2);
            };
            pageTimer.Controls.Add(topbar);
            topbar.Location = new Point(0, 2000);   // v17.6：草稿页顶无状态条，EN 已进 Header

            // 页面标题左侧天蓝强调条
            Panel peerBar = new Panel();
            peerBar.BackColor = C_ACCENT;
            peerBar.Location = new Point(8, 14);
            peerBar.Size = new Size(4, 20);
            peerBar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = UiKit.RoundedPath(new Rectangle(0, 0, 3, 19), 2))
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 3, 19), UiKit.A(91, 150, 255), UiKit.A(49, 102, 225), 90f))
                    e.Graphics.FillPath(br, path);
            };
            topbar.Controls.Add(peerBar);

            lblPeer = new Label();
            lblPeer.Text = T("关机", "Shutdown");
            lblPeer.ForeColor = C_TEXT;
            lblPeer.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblPeer.Location = new Point(22, 13);
            lblPeer.AutoSize = true;
            topbar.Controls.Add(lblPeer);

            lblTyping = new Label();
            lblTyping.Text = T("任务进行中 ···", "Task running ···");
            lblTyping.ForeColor = C_ON;
            lblTyping.Font = new Font("Microsoft YaHei UI", 8F);
            lblTyping.Location = new Point(84, 17);
            lblTyping.AutoSize = true;
            lblTyping.Visible = false;
            topbar.Controls.Add(lblTyping);

            // 服务运行中状态胶囊（深绿圆角底 + 绿字 + 电源图标）
            Panel pillRun = new Panel();
            pillRun.Location = new Point(452, 9);
            pillRun.Size = new Size(172, 30);
            pillRun.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle pr = new Rectangle(0, 0, 171, 29);
                using (GraphicsPath path = UiKit.RoundedPath(pr, 15))
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(pr, UiKit.A(28, 70, 46), UiKit.A(18, 48, 32), 90f))
                        e.Graphics.FillPath(br, path);
                    // 液态玻璃高光
                    e.Graphics.SetClip(path);
                    using (LinearGradientBrush hl = new LinearGradientBrush(new Rectangle(0, 0, 171, 15), UiKit.AA(70, 180, 255, 200), UiKit.AA(0, 180, 255, 200), 90f))
                        e.Graphics.FillRectangle(hl, 0, 0, 171, 15);
                    e.Graphics.ResetClip();
                    using (Pen pp = new Pen(UiKit.A(52, 180, 110), 1.2f))
                        e.Graphics.DrawPath(pp, path);
                }
                string stRun = (lblService != null && lblService.Text != null && lblService.Text.Length > 0) ? lblService.Text : T("服务运行中", "Service Running");
                Font fRun = (lblService != null && lblService.Font != null) ? lblService.Font : new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, stRun, fRun, pr, C_ON, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            topbar.Controls.Add(pillRun);

            lblService = new Label();
            lblService.Text = T("服务运行中", "Service Running");
            lblService.ForeColor = C_ON;
            lblService.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            lblService.Location = new Point(462, 16);
            lblService.AutoSize = true;
            lblService.Visible = false;
            topbar.Controls.Add(lblService);

            // 运行状态图标（绿色电脑 + 电源符号，表示服务运行中）
            PictureBox picRun = new PictureBox();
            picRun.Location = new Point(lblService.Right + 3, 12);
            picRun.Size = new Size(30, 26);
            picRun.BackColor = Color.Transparent;
            picRun.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(C_ON, 1.7f))
                {
                    // 显示器屏幕
                    g.DrawRectangle(p, 2, 1, 20, 13);
                    // 底座
                    g.DrawLine(p, 7, 17, 17, 17);
                    g.DrawLine(p, 9, 20, 15, 20);
                    g.DrawLine(p, 7, 17, 9, 20);
                    g.DrawLine(p, 17, 17, 15, 20);
                    // 屏幕内电源符号（电脑关机/电源图标）
                    g.DrawEllipse(p, 8, 3, 8, 8);
                    g.DrawLine(p, 12, 1, 12, 4);
                }
            };
            topbar.Controls.Add(picRun);

            // 语言切换按钮（中 / EN）
            Button btnLang = new RoundBtn(UiKit.A(43, 58, 80), C_TEXT);
            btnLang.Text = langEn ? "中" : "EN";
            btnLang.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            btnLang.FlatStyle = FlatStyle.Standard;
            btnLang.FlatAppearance.BorderSize = 0;
            btnLang.Cursor = Cursors.Hand;
            btnLang.Size = new Size(38, 26);
            btnLang.Location = new Point(624, 11);
            btnLang.Click += (s, e) => { try { ShowLangMenu(btnLang); } catch (Exception dg3736_1) { Diag.Swallow("MainForm.Ui.cs:2382", dg3736_1); } };
            topbar.Controls.Add(btnLang);
            ToolTip tipTop = new ToolTip();
            tipTop.SetToolTip(btnLang, langEn ? "Switch to Chinese" : "切换到英文");

            // 任务卡（液态玻璃）
            Panel bubble1 = new Panel();
            bubble1.BackColor = C_BUBBLE;
            bubble1.Location = new Point(30, 58);
            bubble1.Size = new Size(560, 232);
            bubble1.Paint += (bs, be) =>
                DrawGlassCard(be.Graphics, new Rectangle(0, 0, Math.Max(10, ((Panel)bs).ClientSize.Width - 1), Math.Max(10, ((Panel)bs).ClientSize.Height - 1)), 12,
                    UiKit.A(50, 56, 68),
                    UiKit.A(34, 39, 47));
            pageTimer.Paint += delegate (object gs, PaintEventArgs ge)
            {
                try
                {
                    Rectangle br = pageTimer.ClientRectangle;
                    if (br.Width < 100 || br.Height < 100) return;
                    ge.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle g1 = new Rectangle(-120, br.Height - 420, 720, 640);
                    using (LinearGradientBrush lg = new LinearGradientBrush(g1,
                        Color.FromArgb(30, 45, 212, 191), Color.FromArgb(0, 45, 212, 191), 35f))
                        ge.Graphics.FillEllipse(lg, g1);
                    Rectangle g2 = new Rectangle(br.Width - 420, -180, 560, 460);
                    using (LinearGradientBrush lg2 = new LinearGradientBrush(g2,
                        Color.FromArgb(22, 56, 132, 176), Color.FromArgb(0, 56, 132, 176), 55f))
                        ge.Graphics.FillEllipse(lg2, g2);
                }
                catch (Exception dg3766_1) { Diag.Swallow("MainForm.Ui.cs:2412", dg3766_1); }
            };
            pageTimer.Controls.Add(bubble1);

            Label go1 = new Label();
            go1.Text = "›";
            go1.ForeColor = UiKit.A(126, 142, 162);
            go1.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            go1.BackColor = Color.Transparent;
            go1.TextAlign = ContentAlignment.MiddleCenter;
            go1.Location = new Point(528, 10);
            go1.Size = new Size(26, 26);
            bubble1.Controls.Add(go1);

            Label t1 = new Label();
            t1.Text = T("当前任务", "Current Task");
            t1.ForeColor = C_SUB;
            t1.Font = new Font("Microsoft YaHei UI", 8F);
            t1.Location = new Point(16, 10);
            t1.AutoSize = true;
            bubble1.Controls.Add(t1);
            Panel line1 = new Panel();
            line1.BackColor = C_BORDER;
            line1.Location = new Point(70, 17);
            line1.Size = new Size(474, 1);
            line1.Tag = "hairline";
            bubble1.Controls.Add(line1);

            ringPanel = new Panel();
            ringPanel.BackColor = Color.Transparent;
            ringPanel.Location = new Point(52, 50);
            ringPanel.Size = new Size(116, 116);
            ringPanel.Paint += RingPaint;
            bubble1.Controls.Add(ringPanel);

            lblTime = new Label();
            lblTime.Text = "--:--:--";
            lblTime.ForeColor = C_TEXT;
            lblTime.Font = new Font("Consolas", 22F, FontStyle.Bold);
            lblTime.Location = new Point(262, 66);
            lblTime.AutoSize = true;
            bubble1.Controls.Add(lblTime);

            lblTimeSub = new Label();
            lblTimeSub.Text = T("空闲 · 未设置", "Idle · Not set");
            lblTimeSub.ForeColor = C_SUB;
            lblTimeSub.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblTimeSub.Location = new Point(266, 108);
            lblTimeSub.AutoSize = true;
            bubble1.Controls.Add(lblTimeSub);

            lblStatus = new Label();
            lblStatus.Text = T("状态：空闲", "Status: Idle");
            lblStatus.ForeColor = C_SUB;
            lblStatus.Font = new Font("Microsoft YaHei UI", 10F);
            lblStatus.Location = new Point(262, 134);
            lblStatus.AutoSize = true;
            bubble1.Controls.Add(lblStatus);

            lblDetail = new Label();
            lblDetail.Text = T("选择操作与计时方式，点击「开始任务」", "Choose an action & timer mode, then press Start");
            lblDetail.ForeColor = UiKit.A(150, 162, 182);
            lblDetail.Font = new Font("Microsoft YaHei UI", 9F);
            lblDetail.Location = new Point(266, 168);
            lblDetail.Size = new Size(370, 40);
            lblDetail.TextAlign = ContentAlignment.TopLeft;
            bubble1.Controls.Add(lblDetail);
            foreach (Control c0 in bubble1.Controls) { if (c0 is Label) c0.BackColor = Color.Transparent; }
            if (UiKit.Paper)
            {
                foreach (Control c0 in bubble1.Controls) if (c0 is Label) c0.Visible = false;
                bubble1.Paint += delegate (object gs, PaintEventArgs ge)
                {
                    try
                    {
                        ge.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        ge.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        foreach (Control c0 in bubble1.Controls)
                        {
                            if (!(c0 is Label)) continue;
                            Label l0 = (Label)c0;
                            if (l0.Text == null || l0.Text.Length == 0) continue;
                            StringFormat sf0 = new StringFormat(StringFormatFlags.NoWrap);
                            sf0.Trimming = StringTrimming.EllipsisCharacter;
                            if (l0.TextAlign == ContentAlignment.MiddleCenter || l0.TextAlign == ContentAlignment.MiddleLeft || l0.TextAlign == ContentAlignment.MiddleRight)
                                sf0.LineAlignment = StringAlignment.Center;
                            if (l0.TextAlign == ContentAlignment.MiddleCenter || l0.TextAlign == ContentAlignment.TopCenter || l0.TextAlign == ContentAlignment.BottomCenter)
                                sf0.Alignment = StringAlignment.Center;
                            else if (l0.TextAlign == ContentAlignment.MiddleRight || l0.TextAlign == ContentAlignment.TopRight || l0.TextAlign == ContentAlignment.BottomRight)
                                sf0.Alignment = StringAlignment.Far;
                            using (SolidBrush b0 = new SolidBrush(l0.ForeColor))
                                ge.Graphics.DrawString(l0.Text, l0.Font, b0, new RectangleF(l0.Left, l0.Top, l0.Width, l0.Height), sf0);
                        }
                    }
                    catch (Exception dgc0) { Diag.Swallow("MainForm.Ui.cs:cardtext", dgc0); }
                };
            }

            // 计时方式卡（液态玻璃）
            Panel bubble2 = new Panel();
            bubble2.BackColor = C_BUBBLE;
            bubble2.Location = new Point(30, 302);
            bubble2.Size = new Size(560, 200);
            bubble2.Paint += (bs, be) =>
                DrawGlassCard(be.Graphics, new Rectangle(0, 0, Math.Max(10, ((Panel)bs).ClientSize.Width - 1), Math.Max(10, ((Panel)bs).ClientSize.Height - 1)), 12,
                    UiKit.A(50, 56, 68),
                    UiKit.A(34, 39, 47));
            foreach (Control c1 in bubble2.Controls) { if (c1 is Label) c1.BackColor = Color.Transparent; }
            pageTimer.Controls.Add(bubble2);

            Label go2 = new Label();
            go2.Text = "›";
            go2.ForeColor = UiKit.A(126, 142, 162);
            go2.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            go2.BackColor = Color.Transparent;
            go2.TextAlign = ContentAlignment.MiddleCenter;
            go2.Location = new Point(528, 10);
            go2.Size = new Size(26, 26);
            bubble2.Controls.Add(go2);

            Label t2 = new Label();
            t2.Text = T("计时方式", "Timer Mode");
            t2.ForeColor = C_SUB;
            t2.Font = new Font("Microsoft YaHei UI", 8F);
            t2.Location = new Point(16, 10);
            t2.AutoSize = true;
            bubble2.Controls.Add(t2);
            Panel line2 = new Panel();
            line2.BackColor = C_BORDER;
            line2.Location = new Point(70, 17);
            line2.Size = new Size(474, 1);
            bubble2.Controls.Add(line2);

            rbCountdown = new RoundRadio();
            rbCountdown.Text = T("倒计时", "Countdown");
            rbCountdown.ForeColor = C_TEXT;
            rbCountdown.AutoSize = false;
            rbCountdown.Size = new Size(112, 24);
            rbCountdown.Location = new Point(18, 30);
            rbCountdown.Checked = true;
            rbCountdown.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbCountdown);

            rbDatetime = new RoundRadio();
            rbDatetime.Text = T("指定时间", "At Time");
            rbDatetime.ForeColor = C_TEXT;
            rbDatetime.AutoSize = false;
            rbDatetime.Size = new Size(112, 24);
            rbDatetime.Location = new Point(138, 30);
            rbDatetime.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbDatetime);

            rbDaily = new RoundRadio();
            rbDaily.Text = T("每天重复", "Daily");
            rbDaily.ForeColor = C_TEXT;
            rbDaily.AutoSize = false;
            rbDaily.Size = new Size(112, 24);
            rbDaily.Location = new Point(258, 30);
            rbDaily.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbDaily);

            rbWeekly = new RoundRadio();
            rbWeekly.Text = T("每周指定", "Weekly");
            rbWeekly.ForeColor = C_TEXT;
            rbWeekly.AutoSize = false;
            rbWeekly.Size = new Size(112, 24);
            rbWeekly.Location = new Point(378, 30);
            rbWeekly.CheckedChanged += (s, e) => UpdateModeUI();
            bubble2.Controls.Add(rbWeekly);

            // v15.3：原来底色 C_BUBBLE 会被系统提亮，在渐变卡上沿左缘压出一根 1px 亮线；改透明直接透卡底
            pCountdown = new Panel();
            pCountdown.BackColor = Color.Transparent;
            pCountdown.Location = new Point(12, 58);
            pCountdown.Size = new Size(536, 118);
            bubble2.Controls.Add(pCountdown);
            pCountdown.SendToBack();
            numH = MakeNum(0, 999, 0);
            numM = MakeNum(0, 59, 30);
            numS = MakeNum(0, 59, 0);
            AddField(pCountdown, T("小时", "Hours"), numH, 30, 70);
            AddField(pCountdown, T("分钟", "Minutes"), numM, 180, 70);
            AddField(pCountdown, T("秒", "Seconds"), numS, 330, 70);
            Label cdHint = new Label();
            cdHint.Text = T("从点击「开始任务」起倒计时", "Counts down from pressing Start");
            cdHint.ForeColor = UiKit.A(150, 162, 182);
            cdHint.Font = new Font("Microsoft YaHei UI", 8F);
            cdHint.Location = new Point(30, 24);
            cdHint.AutoSize = true;
            pCountdown.Controls.Add(cdHint);

            // —— 快捷时长预设（点一下即设好倒计时并开始）——
            Label cdQuickTip = new Label();
            cdQuickTip.Text = T("快捷：", "Quick: ");
            cdQuickTip.ForeColor = UiKit.A(150, 162, 182);
            cdQuickTip.Font = new Font("Microsoft YaHei UI", 8F);
            cdQuickTip.Location = new Point(252, 24);
            cdQuickTip.AutoSize = true;
            pCountdown.Controls.Add(cdQuickTip);
            int[] qMin = { 10, 30, 60, 120 };
            string[] qZh = { "10分", "30分", "1时", "2时" };
            string[] qEn = { "10m", "30m", "1h", "2h" };
            for (int qi = 0; qi < qMin.Length; qi++)
            {
                int mins = qMin[qi];
                Button qb = MakeBtn(T(qZh[qi], qEn[qi]), UiKit.A(48, 56, 70), UiKit.A(160, 200, 255), 300 + qi * 50, 20, 44, 22);
                qb.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
                RoundBtn qr = (RoundBtn)qb;
                pstBtns.Add(qr);
                qb.Click += (s, e) => { if (!taskActive) MarkPreset(qr); PresetCountdown(mins); };
                pCountdown.Controls.Add(qb);
            }

            // 指定时间输入组
            pDatetime = new Panel();
            pDatetime.BackColor = C_BUBBLE;
            pDatetime.Location = new Point(12, 58);
            pDatetime.Size = new Size(536, 118);
            pDatetime.Visible = false;
            bubble2.Controls.Add(pDatetime);
            dtpTime = MakeTimePicker(false);
            dtpTime.Location = new Point(30, 40);
            dtpTime.Size = new Size(260, 28);
            pDatetime.Controls.Add(dtpTime);
            Label dtHint = new Label();
            dtHint.Text = T("到指定日期时间执行（仅一次）", "Runs once at the chosen date & time");
            dtHint.ForeColor = UiKit.A(150, 162, 182);
            dtHint.Font = new Font("Microsoft YaHei UI", 8F);
            dtHint.Location = new Point(30, 78);
            dtHint.AutoSize = true;
            pDatetime.Controls.Add(dtHint);

            // 每天重复输入组
            pDaily = new Panel();
            pDaily.BackColor = C_BUBBLE;
            pDaily.Location = new Point(12, 58);
            pDaily.Size = new Size(536, 118);
            pDaily.Visible = false;
            bubble2.Controls.Add(pDaily);
            dtpDaily = MakeTimePicker(true);
            dtpDaily.Location = new Point(30, 40);
            dtpDaily.Size = new Size(160, 28);
            pDaily.Controls.Add(dtpDaily);
            Label dyHint = new Label();
            dyHint.Text = T("每天这个时间自动执行，可持续运行", "Runs every day at this time");
            dyHint.ForeColor = UiKit.A(150, 162, 182);
            dyHint.Font = new Font("Microsoft YaHei UI", 8F);
            dyHint.Location = new Point(30, 78);
            dyHint.AutoSize = true;
            pDaily.Controls.Add(dyHint);

            // 每周指定输入组
            pWeekly = new Panel();
            pWeekly.BackColor = C_BUBBLE;
            pWeekly.Location = new Point(12, 58);
            pWeekly.Size = new Size(536, 118);
            pWeekly.Visible = false;
            bubble2.Controls.Add(pWeekly);
            string[] wd = langEn ? new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" } : new[] { "一", "二", "三", "四", "五", "六", "日" };
            for (int i = 0; i < 7; i++)
            {
                CheckBox cb = new RoundCheck();
                cb.Text = langEn ? wd[i] : "周" + wd[i];
                cb.ForeColor = C_SUB;
                cb.Font = new Font("Microsoft YaHei UI", 8.5F);
                cb.Location = new Point(30 + i * 70, 30);
                cb.AutoSize = true;
                weekCb[i] = cb;
                pWeekly.Controls.Add(cb);
            }
            dtpWeekly = MakeTimePicker(true);
            dtpWeekly.Location = new Point(30, 68);
            dtpWeekly.Size = new Size(160, 28);
            pWeekly.Controls.Add(dtpWeekly);
            Label wkHint = new Label();
            wkHint.Text = T("选中的星期到点自动执行", "Runs on the selected weekdays");
            wkHint.ForeColor = UiKit.A(150, 162, 182);
            wkHint.Font = new Font("Microsoft YaHei UI", 8F);
            wkHint.Location = new Point(200, 72);
            wkHint.AutoSize = true;
            pWeekly.Controls.Add(wkHint);

            // 设置卡（液态玻璃）
            Panel bubble3 = new Panel();
            bubble3.BackColor = C_BUBBLE;
            bubble3.Location = new Point(572, 390);
            bubble3.Size = new Size(270, 212);
            bubble3.Paint += (s, e) =>
                DrawGlassCard(e.Graphics, new Rectangle(0, 0, 269, 211), 14,
                    UiKit.A(50, 56, 68),
                    UiKit.A(34, 39, 47));
            pageTimer.Controls.Add(bubble3);

            Label t3 = new Label();
            t3.Text = T("进阶设置", "Advanced");
            t3.ForeColor = UiKit.A(214, 226, 240);
            t3.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            t3.Location = new Point(14, 10);
            t3.AutoSize = true;
            bubble3.Controls.Add(t3);

            chkConfirm = new RoundCheck();
            chkConfirm.Text = T("到点弹窗确认", "Confirm popup at time");
            chkConfirm.ForeColor = UiKit.A(186, 196, 210);
            chkConfirm.Font = new Font("Microsoft YaHei UI", 8.5F);
            chkConfirm.Checked = true;
            chkConfirm.Location = new Point(14, 36);
            chkConfirm.AutoSize = true;
            bubble3.Controls.Add(chkConfirm);

            chkSound = new RoundCheck();
            chkSound.Text = T("执行提示音", "Play sound");
            chkSound.ForeColor = UiKit.A(186, 196, 210);
            chkSound.Font = new Font("Microsoft YaHei UI", 8.5F);
            chkSound.Checked = true;
            chkSound.Location = new Point(126, 36);
            chkSound.AutoSize = true;
            bubble3.Controls.Add(chkSound);

            Label ls = new Label();
            ls.Text = T("确认超时", "Confirm timeout");
            ls.ForeColor = C_SUB;
            ls.Font = new Font("Microsoft YaHei UI", 8.5F);
            ls.Location = new Point(14, 72);
            ls.AutoSize = true;
            bubble3.Controls.Add(ls);

            numConfirm = new NumSpin();
            numConfirm.Minimum = 10; numConfirm.Maximum = 600; numConfirm.Value = 60;
            numConfirm.BorderStyle = BorderStyle.None;
            numConfirm.Font = new Font("Consolas", 10F);
            numConfirm.BackColor = C_INPUT;
            numConfirm.ForeColor = C_TEXT;
            numConfirm.Location = new Point(86, 66);
            numConfirm.Size = new Size(58, 26);
            // v8.2：自绘控件不再需要刷内部子控件
            bubble3.Controls.Add(numConfirm);

            Label ls2 = new Label();
            ls2.Text = T("秒自动执行", "s auto-execute");
            ls2.ForeColor = C_SUB;
            ls2.Font = new Font("Microsoft YaHei UI", 8.5F);
            ls2.Location = new Point(142, 72);
            ls2.AutoSize = true;
            bubble3.Controls.Add(ls2);

            btnLog = MakeBtn(T("任务日志", "Task Log"), UiKit.A(51, 58, 70), C_TEXT, 14, 102, 114, 28);
            btnLog.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnLog.Click += (s, e) => ShowLog();
            bubble3.Controls.Add(btnLog);

            // 定时提醒入口：随时给自己定一个到点弹窗的提醒
            Button btnRem = MakeBtn(T("定时提醒", "Reminders"), UiKit.A(51, 58, 70), C_TEXT, 140, 102, 114, 28);
            btnRem.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnRem.Click += (s, e) => ShowReminders();
            bubble3.Controls.Add(btnRem);

            // v10.0：完成动作链入口。到点先做静音/锁屏/收进程/清临时，再把主任务交给系统。
            Button btnChain = MakeBtn(T("完成动作", "Action chain"), UiKit.A(51, 58, 70), C_TEXT, 140, 138, 114, 28);
            btnChain.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnChain.Click += (s, e) => ShowChainDlg();
            bubble3.Controls.Add(btnChain);

            Label advTip = new Label();
            advTip.Text = T("ⓘ 建议开启「到点弹窗确认」", "ⓘ Keep confirm on");
            advTip.ForeColor = UiKit.A(150, 164, 182);
            advTip.Font = new Font("Microsoft YaHei UI", 8F);
            advTip.BackColor = UiKit.A(15, 23, 33);
            advTip.TextAlign = ContentAlignment.MiddleLeft;
            advTip.Padding = new Padding(8, 0, 6, 0);
            advTip.Location = new Point(14, 174);
            advTip.Size = new Size(242, 28);
            bubble3.Controls.Add(advTip);

            Label go3 = new Label();
            go3.Text = "›";
            go3.ForeColor = UiKit.A(126, 142, 162);
            go3.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            go3.BackColor = Color.Transparent;
            go3.TextAlign = ContentAlignment.MiddleCenter;
            go3.Location = new Point(228, 8);
            go3.Size = new Size(26, 26);
            bubble3.Controls.Add(go3);

            // 快捷预设面板（右侧补充功能区，一键开始常用倒计时/定时任务）
            Panel bubbleQ = new Panel();
            bubbleQ.BackColor = C_BUBBLE;
            bubbleQ.Location = new Point(572, 58);
            bubbleQ.Size = new Size(270, 320);
            bubbleQ.Paint += (s, e) =>
                DrawGlassCard(e.Graphics, new Rectangle(0, 0, 269, 319), 14,
                    UiKit.A(50, 56, 68),
                    UiKit.A(34, 39, 47));
            pageTimer.Controls.Add(bubbleQ);

            Label qTitle = new Label();
            qTitle.Text = T("快捷预设", "Quick Presets");
            qTitle.ForeColor = UiKit.A(214, 226, 240);
            qTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            qTitle.Location = new Point(12, 10);
            qTitle.AutoSize = true;
            bubbleQ.Controls.Add(qTitle);
            Panel qLine = new Panel();
            qLine.BackColor = C_BORDER;
            qLine.Location = new Point(72, 17);
            qLine.Size = new Size(206, 1);
            bubbleQ.Controls.Add(qLine);

            AddQuickBtn(bubbleQ, T("30 分钟", "30 min"), 42, 0, 30, 0);
            AddQuickBtn(bubbleQ, T("1 小时", "1 hour"), 84, 1, 0, 0);
            AddQuickBtn(bubbleQ, T("2 小时", "2 hours"), 126, 2, 0, 0);
            AddQuickBtn(bubbleQ, T("3 小时", "3 hours"), 168, 3, 0, 0);
            AddQuickBtnAt(bubbleQ, T("今晚 22:00", "Tonight 22:00"), 210, 22, 0);
            AddQuickBtnAt(bubbleQ, T("明天 08:00", "Tomorrow 08:00"), 252, 8, 0);

            Label qTip = new Label();
            qTip.Text = T("点击即开始 · 建议开启确认弹窗", "Click to start · keep confirm on");
            qTip.ForeColor = UiKit.A(150, 162, 182);
            qTip.Font = new Font("Microsoft YaHei UI", 8F);
            qTip.Location = new Point(12, 296);
            qTip.Size = new Size(246, 20);
            bubbleQ.Controls.Add(qTip);

            Label goQ = new Label();
            goQ.Text = "›";
            goQ.ForeColor = UiKit.A(126, 142, 162);
            goQ.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            goQ.BackColor = Color.Transparent;
            goQ.TextAlign = ContentAlignment.MiddleCenter;
            goQ.Location = new Point(228, 10);
            goQ.Size = new Size(26, 26);
            bubbleQ.Controls.Add(goQ);

            // 底部操作栏
            Panel inputbar = new Panel();
            inputbar.BackColor = UiKit.A(30, 34, 41);
            inputbar.Location = new Point(0, 540);
            inputbar.Size = new Size(898, 100);
            inputbar.Paint += (s, e) => { using (Pen p = new Pen(C_BORDER)) e.Graphics.DrawLine(p, 0, 0, 898, 0); };
            pageTimer.Controls.Add(inputbar);
            inputbar.Location = new Point(0, 2000);   // v17.6：三颗主操作已上移 Header，页内底栏移出可视区（保持可选，Header 代理按钮才能转发点击）

            // v17.9：四张卡登记成字段，交给 LayoutTimerPage 按窗口宽度铺满
            tCard1 = bubble1; tCard2 = bubble2; tCard3 = bubble3; tQuick = bubbleQ;

            // v17.9：左列卡片拉高后底部是空的，放一行随卡片高度贴底的提示，不留白板
            Label tFillTip = new Label();
            tFillTip.Text = T("「到点弹窗确认」会在执行前弹窗，超时按设定秒数自动执行；「执行提示音」会在动作前响一声。",
                "Confirm popup asks before it fires (auto-runs after the timeout); the sound plays right before the action.");
            tFillTip.BackColor = Color.Transparent;
            tFillTip.ForeColor = UiKit.A(146, 158, 178);
            tFillTip.Font = new Font("Microsoft YaHei UI", 8.5F);
            tFillTip.AutoSize = false;
            tFillTip.Size = new Size(500, 20);
            tFillTip.Location = new Point(24, 166);
            tFillTip.Tag = "fillbottom";
            bubble2.Controls.Add(tFillTip);
            foreach (Control cc in bubble1.Controls) if (cc is Label && cc.Text == "›") cc.Tag = "chev";
            foreach (Control cc in bubble2.Controls) if (cc is Label && cc.Text == "›") cc.Tag = "chev";
            foreach (Control cc in bubbleQ.Controls) if (cc is Label && cc.Text == "›") cc.Tag = "chev";
            foreach (Control cc in bubble3.Controls) if (cc is Label && cc.Text == "›") cc.Tag = "chev";

            if (UiKit.Paper)
            {
                Panel[] paperCards = new Panel[] { bubble2, bubble3, bubbleQ };
                foreach (Panel pc0 in paperCards)
                {
                    foreach (Control cc0 in pc0.Controls) if (cc0 is Label) cc0.Visible = false;
                    Panel pcap = pc0;
                    pcap.Paint += delegate (object gs, PaintEventArgs ge)
                    {
                        try
                        {
                            ge.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            foreach (Control cc1 in pcap.Controls)
                            {
                                if (!(cc1 is Label)) continue;
                                Label l1 = (Label)cc1;
                                if (l1.Text == null || l1.Text.Length == 0) continue;
                                StringFormat sf1 = new StringFormat(StringFormatFlags.NoWrap);
                                sf1.Trimming = StringTrimming.EllipsisCharacter;
                                if (l1.TextAlign == ContentAlignment.MiddleCenter || l1.TextAlign == ContentAlignment.MiddleLeft || l1.TextAlign == ContentAlignment.MiddleRight)
                                    sf1.LineAlignment = StringAlignment.Center;
                                if (l1.TextAlign == ContentAlignment.MiddleCenter || l1.TextAlign == ContentAlignment.TopCenter || l1.TextAlign == ContentAlignment.BottomCenter)
                                    sf1.Alignment = StringAlignment.Center;
                                else if (l1.TextAlign == ContentAlignment.MiddleRight || l1.TextAlign == ContentAlignment.TopRight || l1.TextAlign == ContentAlignment.BottomRight)
                                    sf1.Alignment = StringAlignment.Far;
                                using (SolidBrush b1 = new SolidBrush(l1.ForeColor))
                                    ge.Graphics.DrawString(l1.Text, l1.Font, b1, new RectangleF(l1.Left, l1.Top, l1.Width, l1.Height), sf1);
                            }
                        }
                        catch (Exception dgc1) { Diag.Swallow("MainForm.Ui.cs:cardtext2", dgc1); }
                    };
                }
            }
            LayoutTimerPage();

            Label hint = new Label();
            hint.Text = T("提示：建议开启「到点弹窗确认」，防止误操作", "Tip: enable Confirm popup to avoid accidental shutdown");
            hint.ForeColor = UiKit.A(150, 162, 182);
            hint.Font = new Font("Microsoft YaHei UI", 8F);
            hint.Location = new Point(20, 10);
            hint.AutoSize = true;
            inputbar.Controls.Add(hint);

            btnStart = MakeBtn(T("▶ 开始任务", "▶ Start Task"), UiKit.A(43, 95, 208), Color.White, 130, 38, 190, 48);
            btnStart.Click += (s, e) => StartTask();
            inputbar.Controls.Add(btnStart);

            btnCancel = MakeBtn(T("✖ 取消任务", "✖ Cancel"), C_BUBBLE_ALT(), UiKit.A(154, 164, 178), 350, 38, 190, 48);
            btnCancel.Click += (s, e) => CancelTask();
            inputbar.Controls.Add(btnCancel);

            btnImm = MakeBtn(T("⚡ 立即执行", "⚡ Run Now"), UiKit.A(196, 61, 43), Color.White, 570, 38, 190, 48);
            btnImm.Click += (s, e) => Immediate();
            inputbar.Controls.Add(btnImm);

            btnCond = MakeBtn(T("⚙ 智能条件", "⚙ Triggers"), C_BUBBLE_ALT(), UiKit.A(160, 200, 255), 0, 0, 132, 30);
            btnCond.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnCond.Click += (s, e) =>
            {
                CondForm cf = new CondForm(langEn, RunAnyAction);
                cf.ShowDialog(this);
                Log("[条件触发] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 窗口已打开");
            };
            bubble3.Controls.Add(btnCond);
            btnCond.Size = new Size(114, 28);
            btnCond.Location = new Point(14, 138);

            UpdateModeUI();
        }

        NumSpin MakeNum(int min, int max, int val)
        {
            // v8.2：改用自绘深色微调框——原来的原生控件上下按钮永远浅色，深色界面里压不住
            NumSpin n = new NumSpin();
            n.Minimum = min; n.Maximum = max; n.Value = val;
            n.Width = 74; n.Height = 28;
            n.BackColor = C_INPUT;
            n.ForeColor = C_TEXT;
            return n;
        }

        void AddQuickBtn(Panel parent, string text, int y, int h, int m, int s)
        {
            Button b = MakeBtn(text, UiKit.A(24, 34, 46), UiKit.A(210, 222, 236), 10, y, Math.Max(104, parent.ClientSize.Width - 20), 36);
            b.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            int bh = h, bm = m, bs = s;
            RoundBtn rb = (RoundBtn)b;
            pstBtns.Add(rb);
            b.Click += (s2, e2) => { if (!taskActive) MarkPreset(rb); QuickPreset(bh, bm, bs); };
            parent.Controls.Add(b);
        }

        void AddQuickBtnAt(Panel parent, string text, int y, int hour, int minute)
        {
            Button b = MakeBtn(text, UiKit.A(28, 40, 54), UiKit.A(210, 222, 236), 10, y, Math.Max(104, parent.ClientSize.Width - 20), 36);
            b.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            int bh = hour, bm = minute;
            RoundBtn rb = (RoundBtn)b;
            pstBtns.Add(rb);
            b.Click += (s2, e2) => { if (!taskActive) MarkPreset(rb); QuickPresetAt(bh, bm); };
            parent.Controls.Add(b);
        }

        // v9.2：快捷预设打过标就留一圈收束光环，回来一眼看出上次点的是哪一颗；同一时刻只有一颗亮。
        void MarkPreset(RoundBtn b)
        {
            try
            {
                for (int i = 0; i < pstBtns.Count; i++)
                {
                    RoundBtn x = pstBtns[i];
                    if (x == null || x.IsDisposed) continue;
                    bool want = (x == b);
                    if (x.Mark != want) { x.Mark = want; x.Invalidate(); }
                }
            }
            catch (Exception dg4281_1) { Diag.Swallow("MainForm.Ui.cs:2927", dg4281_1); }
        }

        void ClearPresetMarks()
        {
            try
            {
                for (int i = 0; i < pstBtns.Count; i++)
                {
                    RoundBtn x = pstBtns[i];
                    if (x == null || x.IsDisposed) continue;
                    if (x.Mark) { x.Mark = false; x.Invalidate(); }
                }
            }
            catch (Exception dg4295_1) { Diag.Swallow("MainForm.Ui.cs:2941", dg4295_1); }
        }

        void QuickPreset(int h, int m, int s)
        {
            if (taskActive)
            {
                MessageBox.Show(this, T("已有任务在进行中，请先取消。", "A task is already running. Cancel it first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            rbCountdown.Checked = true;
            numH.Value = Math.Min(Math.Max(h, 0), 999);
            numM.Value = Math.Min(Math.Max(m, 0), 59);
            numS.Value = Math.Min(Math.Max(s, 0), 59);
            UpdateModeUI();
            StartTask();
        }

        void QuickPresetAt(int hour, int minute)
        {
            if (taskActive)
            {
                MessageBox.Show(this, T("已有任务在进行中，请先取消。", "A task is already running. Cancel it first."), T("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            rbDatetime.Checked = true;
            DateTime nt = DateTime.Today.AddHours(hour).AddMinutes(minute);
            if (nt <= DateTime.Now) nt = nt.AddDays(1);
            dtpTime.Value = nt;
            UpdateModeUI();
            StartTask();
        }

        DateTimePicker MakeTimePicker(bool timeOnly)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Custom;
            d.CustomFormat = timeOnly ? "HH:mm" : "yyyy-MM-dd HH:mm:ss";
            d.ShowUpDown = timeOnly;
            d.BackColor = Ui2.CtlInk(C_INPUT);
            d.ForeColor = C_TEXT;
            return d;
        }

        void AddField(Control parent, string label, Control ctrl, int x, int y)
        {
            Label l = new Label();
            l.Text = label;
            l.ForeColor = C_SUB;
            l.Font = new Font("Microsoft YaHei UI", 8F);
            l.Location = new Point(x, y - 18);
            l.AutoSize = true;
            parent.Controls.Add(l);
            ctrl.Location = new Point(x, y);
            ctrl.Size = new Size(120, 24);
            parent.Controls.Add(ctrl);
        }

        // 主界面深色下拉：隐藏原生 ComboBox，用自绘按钮 + 深色右键菜单代替。
        // WinForms 的 ComboBox 在 DropDownList + FlatStyle.Flat 组合下背景由系统绘制，
        // 设 BackColor 无效，唯一能稳定深色化的做法就是换成自绘按钮。
        Button MakeDropMain(ComboBox cb, int x, int y, int w, int h)
        {
            string initDropTxt = string.IsNullOrEmpty(cb.Text) ? T("请先解析链接", "Parse link first") : cb.Text;
            Button b = MakeBtn(initDropTxt + "   v", C_INPUT, C_INPUT_TEXT, x, y, w, h);
            b.Font = new Font("Microsoft YaHei UI", 9.5F);
            b.TextAlign = ContentAlignment.MiddleLeft;
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.BackColor = UiKit.A(32, 36, 45);
            cms.ForeColor = UiKit.A(232, 236, 242);
            cms.Font = new Font("Microsoft YaHei UI", 9.5F);
            cms.Renderer = new DarkMenuRenderer();
            cms.ShowImageMargin = false;
            cms.Padding = new Padding(0, 4, 0, 4);
            foreach (object o in cb.Items)
            {
                string txt = o.ToString();
                cms.Items.Add(txt, null, (s2, e2) =>
                {
                    int idx = cb.Items.IndexOf(txt);
                    if (idx >= 0) cb.SelectedIndex = idx;
                    b.Text = cb.Text + "   v";
                });
            }
            b.Click += (s2, e2) => cms.Show(b, new Point(0, b.Height + 2));
            cb.SelectedIndexChanged += (s2, e2) => { if (!string.IsNullOrEmpty(cb.Text)) b.Text = cb.Text + "   v"; };
            return b;
        }

        Button MakeBtn(string text, Color bg, Color fg, int x, int y, int w, int h)
        {
            RoundBtn b = new RoundBtn(bg, fg);
            b.Text = text;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            return b;
        }

        Color C_BUBBLE_ALT() { return UiKit.A(51, 58, 70); }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            GlassLabelFix(this);
            FitToScreenOnce();
            MakeDoubleBuffered(this);
            StartWallAnim();
            LayoutShell();
            BeginInvoke(new MethodInvoker(delegate { LayoutShell(); }));
            EnableFileDrop(this);
        }

        // v16.0：内容区在主区里居中，两侧留白随窗口宽度自动分配
        // v17.6：外壳四块（侧栏 / 顶部 Header / 底部状态条 / 主区）手动分配，
        // Dock 在同一层里无法可靠地让 Fill 扣掉 Top+Bottom，改成按比例直接算。
        // Header 内部排布：随 Header 实际尺寸换算逻辑坐标，右侧按钮贴右。
        // 缩放系数从 Header 高度反推（116 是设计高度），窗口宽度变化也不受影响。
        void HeaderArrange()
        {
            try
            {
                if (hName == null || hbCmd == null) return;
                int W = header.ClientSize.Width, H = header.ClientSize.Height;
                if (W < 600 || H < 40) return;
                // v17.6：W 取 header 自身宽度，但实测它有时比窗口可见宽多出上百像素
                //（LayoutShell 在窗口尺寸未落定时跑过一次），右侧按钮就被推到可视区外。
                // 这里再按「客户区宽 - 侧栏宽」夹一道，保证最右那颗一定贴得住窗口右边。
                // v17.7：右边界必须用「窗口客户区宽 - 侧栏宽」。
                // 之前取 header 自身宽度，它在 LayoutShell 未落定时会比可见区宽出一截，
                // 法 / 俄 / 西 文案一长，第二排三颗就被推出窗口右边。
                int sideW = (sidebar != null && sidebar.Width > 60) ? sidebar.Width : 168;
                int lw = this.ClientSize.Width - sideW;
                int x0 = 168 + 30;
                hName.Location = new Point(x0, 6);
                hCode.Location = new Point(x0 + 1, 56);
                hDesc.Location = new Point(x0 + 1, 78);
                int by = 24;
                if (winClose != null)
                {
                    winClose.Location = new Point(lw - 22 - 30, 20);
                    winMin.Location = new Point(lw - 22 - 60, 20);
                    winClose.BringToFront();
                    winMin.BringToFront();
                }
                if (hbEn != null)
                {
                    int rx = lw - 22 - 72;
                    // v17.6：四颗主操作的尺寸每帧钉死，免得别处改过 Size 后圆角路径退化成一条线
                    hbStart1.Size = new Size(150, 34);
                    hbCancel1.Size = new Size(112, 34);
                    hbImm1.Size = new Size(118, 34);
                    hbEn.Size = new Size(Math.Max(62, TextRenderer.MeasureText(hbEn.Text, hbEn.Font).Width + 22), 34);
                    // v17.6：Header 里后加进来的装饰控件（Logo 块等）会压在按钮上，统一提到最前
                    hbStart1.BringToFront();
                    hbCancel1.BringToFront();
                    hbImm1.BringToFront();
                    hbEn.BringToFront();
                    hbEn.Location = new Point(rx - hbEn.Width, by);
                    hbImm1.Location = new Point(hbEn.Left - 14 - hbImm1.Width, by);
                    hbCancel1.Location = new Point(hbImm1.Left - 10 - hbCancel1.Width, by);
                    hbStart1.Location = new Point(hbCancel1.Left - 10 - hbStart1.Width, by);
                }
                int bx = lw - 22 - 72;
                int by2 = 72;
                // v17.7：法 / 俄 / 西的按钮文案比中文长，宽度按文字量出来，不然文字会溢出胶囊压到隔壁
                int wCmd = Math.Max(92, TextRenderer.MeasureText(hbCmd.Text, hbCmd.Font).Width + 30);
                hbCmd.Size = new Size(wCmd, 34);
                hbCmd.Location = new Point(bx - wCmd, by2); bx -= wCmd + 10;
                if (hbCenter != null)
                {
                    int wCc = Math.Max(96, TextRenderer.MeasureText(hbCenter.Text, hbCenter.Font).Width + 30);
                    hbCenter.Size = new Size(wCc, 34);
                    hbCenter.Location = new Point(bx - wCc, by2);
                    hbCenter.BringToFront();
                    bx -= wCc + 10;
                }
                int wBg = Math.Max(100, TextRenderer.MeasureText(hbBg.Text, hbBg.Font).Width + 30);
                hbBg.Size = new Size(wBg, 34);
                hbBg.Location = new Point(bx - wBg, by2); bx -= wBg + 10;
                if (btnCond != null && btnCond.Parent == header)
                {
                    int wCo = Math.Max(132, TextRenderer.MeasureText(btnCond.Text, btnCond.Font).Width + 30);
                    btnCond.Size = new Size(wCo, 34);
                    btnCond.Location = new Point(bx - wCo, by2);
                }
            }
            catch (Exception dg4477_1) { Diag.Swallow("MainForm.Ui.cs:3123", dg4477_1); }
        }

        // 当前页 → Header 文案（页面名 / 编号 / 一句说明）
        void SyncHeader(Panel pg)
        {
            try
            {
                if (hName == null) return;
                string n = T("工作台", "Dashboard"), code = "00 / DASHBOARD", d = T("状态一览 · 常用入口", "Overview");
                if (pg == pageTimer) { n = T("定时关机", "Scheduler"); code = "01 / TIMER"; d = T("定时关机 · 到点提醒 · 执行动作", "Schedule shutdown"); }
                else if (pg == pageClean) { n = T("C盘清理", "Disk Clean"); code = "02 / CLEAN"; d = T("清理系统缓存垃圾", "Clean caches"); }
                else if (pg == pageSam) { n = T("Steam成就", "Achievements"); code = "03 / STEAM"; d = T("全库一键解锁成就", "Unlock achievements"); }
                else if (pg == pageVideo) { n = T("视频下载", "Video"); code = "04 / VIDEO"; d = T("全网视频链接一键下载", "Download videos"); }
                else if (pg == pageMusic) { n = T("音乐下载", "Music"); code = "05 / MUSIC"; d = T("网易云曲库在线下载", "Download music"); }
                else if (pg == pageWatermark) { n = T("去水印", "Watermark"); code = "06 / WATERMARK"; d = T("视频 / 图片去水印", "Remove watermark"); }
                else if (pg == pageValue) { n = T("Steam价值", "Value"); code = "07 / VALUE"; d = T("账号资产估价", "Account value"); }
                else if (pg == pageSys) { n = T("电脑配置", "System"); code = "08 / SYSTEM"; d = T("硬件配置一览", "Hardware info"); }
                hName.Text = n;
                hCode.Text = code;
                hDesc.Text = d;
                // 方案四：三颗主操作只在定时页出现在 Header，其它页收起来；
                // 反过来说「自定义背景 / 命令面板」只在首页出现，别和主操作抢那一行
                bool showOps = (pg == pageTimer);
                bool showTools = (pg == null || pg == pageHome);
                if (hbBg != null) hbBg.Visible = showTools;
                if (hbCmd != null) hbCmd.Visible = showTools;
                if (hbCenter != null) hbCenter.Visible = showTools;
                if (hbStart1 != null) hbStart1.Visible = showOps;
                if (hbCancel1 != null) hbCancel1.Visible = showOps;
                if (hbImm1 != null) hbImm1.Visible = showOps;
                if (btnCond != null && btnCond.Parent == header) btnCond.Visible = showOps;
                UiKit.SkinButtons(pg);   // v17.6：每次切页把该页的原生按钮重新刷一遍皮肤
            }
            catch (Exception dg4510_1) { Diag.Swallow("MainForm.Ui.cs:3156", dg4510_1); }
        }

        // v17.9：动态壁纸每秒重绘十几次，子面板不开双缓冲会闪
        static void MakeDoubleBuffered(Control root)
        {
            try
            {
                System.Reflection.PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pi != null && root is Panel) pi.SetValue(root, true, null);
            }
            catch (Exception dg4522_1) { Diag.Swallow("MainForm.Ui.cs:3168", dg4522_1); }
            try
            {
                foreach (Control c in root.Controls) MakeDoubleBuffered(c);
            }
            catch (Exception dg4527_1) { Diag.Swallow("MainForm.Ui.cs:3173", dg4527_1); }
        }

        void LayoutShell()
        {
            try
            {
                if (header == null || mainArea == null || statusbar == null || content == null) return;
                int W = ClientSize.Width, H = ClientSize.Height;
                if (W < 400 || H < 400) return;
                int sw = sidebar.Width;            // 已按 DPI 落定的实际宽度
                int hh = header.Height;
                int sh = statusbar.Height;
                if (sw <= 0 || hh <= 0 || sh <= 0) return;
                Rectangle rSide = new Rectangle(0, hh, sw, Math.Max(0, H - hh));
                Rectangle rMain = new Rectangle(sw, 0, W - sw, H);
                if (sidebar.Bounds != rSide) sidebar.SetBounds(rSide.X, rSide.Y, rSide.Width, rSide.Height);
                if (mainArea.Bounds != rMain) mainArea.SetBounds(rMain.X, rMain.Y, rMain.Width, rMain.Height);
                header.SetBounds(0, 0, W, hh);
                header.BringToFront();
                statusbar.SetBounds(0, H - sh, rMain.Width, sh);
                content.SetBounds(0, hh, rMain.Width, Math.Max(0, H - hh - sh));
                HeaderArrange();
                BeginInvoke(new MethodInvoker(delegate { HeaderArrange(); }));   // Header 尺寸落定后再贴右一次
                if (sbSlogan1 != null)
                {
                    int sH = Math.Max(200, sidebar.ClientSize.Height);
                    sbSlogan1.Location = new Point(16, sH - 30);
                    sbSlogan2.Location = new Point(16, sH - 14);
                }
                CenterContent();
                shellLaid = true;
                LayoutTimerPage();
                LayoutHomePage();
                try
                {
                    using (GraphicsPath rr = UiKit.RoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 14))
                        Region = new Region(rr);
                }
                catch (Exception dg4566_1) { Diag.Swallow("MainForm.Ui.cs:3212", dg4566_1); }
                UiKit.SkinButtons(content);   // v17.6：新页面上来先统一接管原生按钮
            }
            catch (Exception dg4569_1) { Diag.Swallow("MainForm.Ui.cs:3215", dg4569_1); }
        }

        // v17.9：首页三列铺满（方案四内容区几乎不留白）
        void LayoutHomePage()
        {
            try
            {
                if (!shellLaid || pageHome == null) return;
                int pw = pageHome.ClientSize.Width;
                if (pageHome.Parent != null)
                {
                    Padding pp = pageHome.Parent.Padding;
                    pw = pageHome.Parent.ClientSize.Width - pp.Left - pp.Right;
                }
                if (pw < 620) return;
                int m = 30, gap = 14;
                int w3 = (pw - 2 * m - 2 * gap) / 3;
                if (w3 < 180) return;
                if (homeCards != null)
                    for (int i = 0; i < homeCards.Length; i++)
                        if (homeCards[i] != null) { homeCards[i].Left = m + i * (w3 + gap); homeCards[i].Width = w3; }
                if (homeRecent != null) homeRecent.Width = pw - 2 * m;
                int tileN = 0;
                foreach (Control c in pageHome.Controls)
                    if (c.Name != null && c.Name.StartsWith("homeTile")) tileN++;
                int tileCols = (tileN > 0 && tileN % 3 == 0) ? 3 : 4;
                if (tileCols < 1) tileCols = 3;
                int wT = (pw - 2 * m - (tileCols - 1) * gap) / tileCols;
                foreach (Control c in pageHome.Controls)
                {
                    string nm = c.Name;
                    if (nm == null) continue;
                    if (nm == "homeQuick") { c.Left = m; c.Width = pw - 2 * m; }
                    else if (nm == "homeWide0" || nm == "homeWide1")
                    {
                        int wi = nm.EndsWith("0") ? 0 : 1;
                        int wW = (pw - 2 * m - gap) / 2;
                        c.Width = wW;
                        c.Left = m + wi * (wW + gap);
                    }
                    else if (nm.StartsWith("homeTile"))
                    {
                        int n = 0;
                        int.TryParse(nm.Substring(8), out n);
                        int col = n % tileCols;
                        c.Left = m + col * (wT + gap);
                        c.Width = wT;
                    }
                }
            }
            catch (Exception dg4613_1) { Diag.Swallow("MainForm.Ui.cs:3259", dg4613_1); }
        }

        // v17.9：定时页两列铺满 —— 右列贴页右边缘，左列吃掉剩余宽度，跟方案四一样几乎不留白
        void LayoutTimerPage()
        {
            try
            {
                if (!shellLaid || pageTimer == null || tCard1 == null || tCard2 == null) return;
                int pw = pageTimer.ClientSize.Width;
                if (pageTimer.Parent != null)
                {
                    Padding pp = pageTimer.Parent.Padding;
                    pw = pageTimer.Parent.ClientSize.Width - pp.Left - pp.Right;
                }
                if (pw < 460) return;
                int padL = 30, rw = 270, gap = 12;
                int rx = pw - padL - rw;
                int lw = rx - gap - padL;
                if (lw < 320) { lw = 320; rx = padL + lw + gap; }
                if (tCard1.Width != lw) tCard1.Width = lw;
                if (tCard2.Width != lw) tCard2.Width = lw;
                // v17.9：左列「计时方式」拉到和右列底板齐平，页底不再留一块空的豁口
                if (tCard3 != null && tCard2.Height > 0)
                {
                    int tb = tCard3.Top + tCard3.Height;
                    if (tb > tCard2.Bottom) tCard2.Height = tb - tCard2.Top;
                }
                if (tQuick != null && tQuick.Left != rx) tQuick.Left = rx;
                if (tCard3 != null && tCard3.Left != rx) tCard3.Left = rx;
                if (tCard2.Width > 40)
                {
                    foreach (Control c in tCard2.Controls)
                    {
                        Panel p = c as Panel;
                        if (p != null && p.Left == 12 && p.Height >= 110 && p.Width > 100) p.Width = lw - 24;
                    }
                }
                foreach (Control c in tCard2.Controls)
                    if ((c.Tag as string) == "fillbottom") { c.Width = Math.Max(120, lw - 48); c.Top = Math.Max(120, tCard2.Height - 46); }
                foreach (Control c in tCard1.Controls)
                    if ((c.Tag as string) == "chev") c.Left = lw - 32;
                foreach (Control c in tCard2.Controls)
                    if ((c.Tag as string) == "chev") c.Left = lw - 32;
                foreach (Control c in tCard1.Controls)
                    if ((c.Tag as string) == "hairline") c.Width = Math.Max(10, lw - 88);
                // v17.9：两枚勾选原来写死 14 / 100，中文就压在一起，英文更长直接叠字。
                // 这里按实测文字宽度把第二枚推到第一枚右侧，放不下才换行。
                if (chkConfirm != null && chkSound != null && chkConfirm.Parent != null && chkConfirm.Parent == chkSound.Parent)
                {
                    int w1 = chkConfirm.Width;
                    if (w1 <= 0) w1 = TextRenderer.MeasureText(chkConfirm.Text, chkConfirm.Font).Width + 24;
                    int need = chkConfirm.Left + w1 + 16;
                    int pv = chkConfirm.Parent.ClientSize.Width;
                    if (need + chkSound.Width > pv - 6)
                    {
                        chkSound.Left = chkConfirm.Left;
                        chkSound.Top = chkConfirm.Top + 24;
                    }
                    else
                    {
                        chkSound.Left = need;
                        chkSound.Top = chkConfirm.Top;
                    }
                }
            }
            catch (Exception dg4679_1) { Diag.Swallow("MainForm.Ui.cs:3325", dg4679_1); }
        }

        // v16.0：内容区在主区里居中，两侧留白随窗口宽度自动分配
        void CenterContent()
        {
            if (content == null || mainArea == null) return;
            int w = content.Width - 2;
            if (w < 40) return;
            // v17.6：content 改成 Dock=Fill，水平居中的活儿交给内边距，
            // 页面自己 Dock=Fill 填进内区，页内绝对坐标一个都不用动。
            int cw = Math.Max(320, Math.Min(1000, w - 24));
            int pad = Math.Max(0, (w - cw) / 2);
            if (content.Padding.Left != pad)
                content.Padding = new Padding(pad, 0, pad, 0);
        }

        bool fitDone = false;

        // v8.7：界面全部按 960x661 绝对坐标摆，硬拉伸会错位，所以只做一件事——
        // 所在屏幕的工作区确实放不下时，整窗等比缩一次（按短边比例，最少 60%），并重新居中。
        // 1920x1080 / 2560x1440 上比例≥1，这里什么都不做，观感与之前完全一致。
        void FitToScreenOnce()
        {
            if (fitDone) return;
            fitDone = true;
            try
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                float kw = (float)(wa.Width - 16) / 972f;
                float kh = (float)(wa.Height - 16) / 700f;
                float k = kw < kh ? kw : kh;
                if (k >= 0.999f) return;
                if (k < 0.6f) k = 0.6f;
                Scale(new SizeF(k, k));
                Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
                Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
                Log("[适配] 屏幕 " + wa.Width + "x" + wa.Height + " 放不下原尺寸，整体缩到 " + (int)(k * 100) + "%");
            }
            catch (Exception dg4718_1) { Diag.Swallow("MainForm.Ui.cs:3364", dg4718_1); }
        }

        // 玻璃卡片内的 Label 统一透明：避免 Label 继承父容器纯色底色，
        // 在 DrawGlassCard 画的渐变上铺出不透明矩形（表现为灰块压住文字）。
        void GlassLabelFix(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Label)
                {
                    Control p = c.Parent;
                    if (p != null)
                    {
                        Color pb = p.BackColor;
                        // v8.8：页面是纵向渐变，直属说明文字原来按"所在那一行取一个纯色"来铺底，
                        // 只要渐变走到下一行，就会在字后面露出一块比底色亮的方板（最典型的是
                        // 「一体式：点一次就…」这类副标题）。页面直属的 Label 一律交给父容器透出，
                        // 渐变就是连续的，再不会有方板边。
                        if (p is PagePanel || pb == C_BUBBLE || pb == C_BUBBLE_ALT() || pb == Color.Transparent)
                            c.BackColor = Color.Transparent;
                    }
                }
                if (c.HasChildren) GlassLabelFix(c);
            }
        }

        void StylePrevBox(TextBox t)
        {
            try
            {
                t.Font = new Font("Microsoft YaHei UI", 9.5F);
                t.WordWrap = true;
            }
            catch (Exception dg4752_1) { Diag.Swallow("MainForm.Ui.cs:3398", dg4752_1); }
        }

        TextBox MakeInput(int x, int y, int w, int h, string hint)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, h);
            // v8.8：反补后渲染出来才是 C_INPUT(38,45,58)，不再是那块浅灰
            t.BackColor = Ui2.CtlInk(C_INPUT);
            t.ForeColor = C_INPUT_TEXT;
            t.BorderStyle = BorderStyle.None;
            if (UiKit.Paper)
            {
                t.BackColor = Color.FromArgb(245, 238, 223);
                t.ForeColor = Color.FromArgb(18, 16, 14);
            }
            t.Font = new Font("Microsoft YaHei UI", 9.5F);
            // 单行 TextBox 的高度由字体决定（9.5F 下是 17px），在 28px 的槽位里靠上会显得字顶到框沿，
            // 这里统一按槽位垂直居中；左右再补 7px 内边距，字符不贴边。
            t.Top = y + Math.Max(0, (h - t.PreferredHeight) / 2);
            Ui2.PadTextBox(t, 7);
            if (hint != null && hint.Length > 0)
            {
                Label ph = new Label();
                ph.Text = hint;
                ph.ForeColor = UiKit.Paper ? Color.FromArgb(126, 114, 94) : UiKit.A(140, 152, 172);   // v10.1：占位文字 3.6:1 → 4.7:1，深色框里也读得清
                ph.Font = new Font("Microsoft YaHei UI", 9F);
                ph.BackColor = Color.Transparent;
                ph.AutoSize = false;
                ph.TextAlign = ContentAlignment.MiddleLeft;
                ph.Size = new Size(Math.Max(w - 14, 20), 20);
                ph.Cursor = Cursors.IBeam;
                ph.Tag = "placeholder";
                ph.Click += (s9, e9) => { t.Focus(); };
                t.TextChanged += (s9, e9) => { if (ph.Parent != null) ph.Visible = (t.Text.Length == 0); };
                t.GotFocus += (s9, e9) => { if (ph.Parent != null) ph.Visible = false; };
                t.LostFocus += (s9, e9) => { if (ph.Parent != null) ph.Visible = (t.Text.Length == 0); };
                t.ParentChanged += (s9, e9) =>
                {
                    if (t.Parent == null || ph.Parent != null) return;
                    ph.Location = new Point(t.Left + 7, t.Top + (t.Height - 20) / 2);
                    t.Parent.Controls.Add(ph);
                    ph.BringToFront();
                };
            }
            t.ParentChanged += (s9, e9) => { if (UiKit.Paper) Ui2.InstallPaperInputVeil(t); };
            return t;
        }

        ListView MakeDarkList(string[] cols, int[] widths, int x, int y, int w, int h)
        {
            DarkListView lv = new DarkListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HideSelection = false;   // v12.7：焦点跑到按钮/输入框上也留着选中底色，不然像"点了没反应"
            lv.GridLines = false;
            lv.BorderStyle = BorderStyle.None;
            // v8.9：列表客户区底色也是经系统刷的，会被提亮（实测设 38,45,58 落成 72,73,83）。
            // 空列表时表头正下方会留一条比页面亮的横带，就是它。这里反补。
            lv.BackColor = UiKit.Paper ? Color.FromArgb(250, 245, 233) : Ui2.CtlInk(UiKit.A(46, 54, 68));   // 与第一行同色，表头下方不留亮带
            lv.ForeColor = C_INPUT_TEXT;
            lv.Font = new Font("Microsoft YaHei UI", 9.5F);   // v12.8：9 → 9.5，中文在大屏上更清楚
            lv.OwnerDraw = true;
            // Win11 下系统表头是纯白、空行区会跟着主题发灰，与深色界面打架。
            // 这里整表自绘：表头、行底、单元格文字全部自己画，颜色完全可控。
            // v8.9：表头原来比行还暗一点点，扫视时"表头—数据"贴在一起分不开。
            // 改成：表头压深、分隔线提亮、斑马纹对比加强，三层一眼分开。
            // 另外实测：ListView 里的自绘（含表头、行底、表头文字）一样会被那套 +34/+28/+25 提亮
            // ——用洋红 (255,0,255) 试画，落在屏幕上是 (255,29,255)，确实被抬了。
            // 所以这一组颜色也要先反补再画，屏幕上的颜色才是这里写的值。
            Color headBg = UiKit.Paper ? Color.FromArgb(238, 230, 212) : Ui2.CtlInk(UiKit.A(26, 31, 40));
            Color headLine = UiKit.Paper ? Color.FromArgb(205, 196, 176) : Ui2.CtlInk(UiKit.A(74, 86, 106));
            Color headText = UiKit.Paper ? Color.FromArgb(76, 69, 55) : Ui2.CtlInk(UiKit.A(150, 162, 180));
            Color rowBg = UiKit.Paper ? Color.FromArgb(250, 245, 233) : Ui2.CtlInk(UiKit.A(46, 54, 68));
            Color rowAlt = UiKit.Paper ? Color.FromArgb(245, 239, 225) : Ui2.CtlInk(UiKit.A(39, 45, 57));
            Color selBg = UiKit.Paper ? Color.FromArgb(233, 222, 199) : Ui2.CtlInk(UiKit.A(46, 72, 118));
            // v8.6：系统会把列表里那截"没有行"的区域涂成灰白，LVM_SETBKCOLOR 和 WM_ERASEBKGND 都压不住
            //（它在 WM_PAINT 里自己画）。所以空列表的下半截直接盖一块自绘深色面板，里面写一行「暂无数据」；
            // 有数据时面板自动隐藏，见到的是真正的列表行。
            // 用 Label 而不是 Panel：Label 的背景在本程序里既不刷白也不刷灰，读出来就是页面本身的颜色，
            // 这样空列表下半截和页面连成一片，只有一行淡淡的提示字。
            Label cover = new Label();
            cover.AutoSize = false;
            cover.BackColor = UiKit.Paper ? Color.FromArgb(250, 245, 233) : Color.Transparent;
            // v10.1：空态那行字原来 116,128,146，实测对比度只有 3.6:1（全界面最低），提亮到 4.9:1
            cover.ForeColor = UiKit.A(146, 158, 178);
            cover.Font = new Font("Microsoft YaHei UI", 9F);
            cover.TextAlign = ContentAlignment.MiddleCenter;
            // v15.1：空态从"一行灰字"升级成「矢量图标 + 主文 + 副文」三件套。
            // 文字改由 Paint 里自己画（Label 的 Text 清空），这样图标与文字能各占一行、字号也能分两档。
            cover.Text = "";
            Font emptyMainFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            Font emptySubFont = new Font("Microsoft YaHei UI", 8.5F);
            cover.Paint += delegate (object s2, PaintEventArgs e2)
            {
                try
                {
                    Graphics g2 = e2.Graphics;
                    if (UiKit.Paper) g2.Clear(Color.FromArgb(250, 245, 233));
                    g2.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle cr2 = cover.ClientRectangle;
                    int cx2 = cr2.X + cr2.Width / 2;
                    int cy2 = cr2.Y + cr2.Height / 2;
                    if (cr2.Height >= 110)
                    {
                        // 空列表图标：圆角方框 + 三条渐短的横线
                        int iw = 38, ih = 28;
                        Rectangle ib = new Rectangle(cx2 - iw / 2, cy2 - 56, iw, ih);
                        using (GraphicsPath ip = UiKit.RoundedPath(ib, 7))
                        using (Pen bp2 = new Pen(UiKit.AA(78, 152, 168, 194), 1.2f))
                            g2.DrawPath(bp2, ip);
                        using (Pen ln = new Pen(UiKit.AA(52, 152, 168, 194), 1f))
                        {
                            g2.DrawLine(ln, ib.Left + 8, ib.Top + 9, ib.Right - 8, ib.Top + 9);
                            g2.DrawLine(ln, ib.Left + 8, ib.Top + 14, ib.Right - 13, ib.Top + 14);
                            g2.DrawLine(ln, ib.Left + 8, ib.Top + 19, ib.Right - 18, ib.Top + 19);
                        }
                    }
                    if (UiKit.Paper)
                    {
                        g2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        using (StringFormat sf2 = new StringFormat())
                        {
                            sf2.Alignment = StringAlignment.Center;
                            sf2.LineAlignment = StringAlignment.Center;
                            sf2.FormatFlags = StringFormatFlags.NoWrap;
                            sf2.Trimming = StringTrimming.EllipsisCharacter;
                            using (SolidBrush b2 = new SolidBrush(Color.FromArgb(70, 63, 50)))
                                g2.DrawString(T("这里还没有内容", "Nothing here yet"), emptyMainFont, b2,
                                    new RectangleF(cr2.X, cy2 - 14, cr2.Width, 24), sf2);
                            if (cr2.Height >= 70)
                            {
                                using (SolidBrush b3 = new SolidBrush(Color.FromArgb(104, 94, 78)))
                                    g2.DrawString(T("按上方的操作开始，结果会出现在这里", "Run the action above and results show up here"), emptySubFont, b3,
                                        new RectangleF(cr2.X, cy2 + 12, cr2.Width, 20), sf2);
                            }
                        }
                    }
                    else
                    {
                    TextRenderer.DrawText(g2, T("这里还没有内容", "Nothing here yet"), emptyMainFont,
                        new Rectangle(cr2.X, cy2 - 14, cr2.Width, 24), UiKit.A(176, 188, 206),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (cr2.Height >= 70)
                        TextRenderer.DrawText(g2, T("按上方的操作开始，结果会出现在这里", "Run the action above and results show up here"), emptySubFont,
                            new Rectangle(cr2.X, cy2 + 12, cr2.Width, 20), UiKit.A(128, 140, 158),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    }
                }
                catch (Exception dg4872_1) { Diag.Swallow("MainForm.Ui.cs:3518", dg4872_1); }
            };
            cover.Visible = false;
            Action syncCover = null;
            syncCover = delegate
            {
                try
                {
                    if (cover.Parent == null) return;
                    // v9.0：空态盖板原来从表头下 31px 起，表头实际只有约 19-20px，
                    // 中间就露出 8px 行色横带，看着像多了一张空行。改成紧跟表头起。
                    int top2 = lv.Top + 20;
                    int cvH = lv.Height - 20;
                    cover.Bounds = new Rectangle(lv.Left, top2, lv.Width, Math.Max(0, cvH));
                    cover.Visible = (lv.Items.Count == 0) && lv.Visible && cvH >= 24;
                    cover.Invalidate();
                    Ui2.SyncListActions(lv.Parent);   // v8.7：空列表时"导出/清空/下载选中"等按钮跟着置灰
                }
                catch (Exception dg4890_1) { Diag.Swallow("MainForm.Ui.cs:3536", dg4890_1); }
            };
            lv.SizeChanged += delegate { syncCover(); };
            lv.VisibleChanged += delegate { syncCover(); };
            lv.ParentChanged += delegate
            {
                try
                {
                    if (lv.Parent == null) return;
                    if (cover.Parent != lv.Parent)
                    {
                        lv.Parent.Controls.Add(cover);
                        cover.BringToFront();
                    }
                    // v12.7：窗口被切回来/激活时系统只补画了一部分，自绘列表就留下「半行」。这里整表重绘一次。
                    Form pf = lv.Parent as Form;
                    if (pf != null && lv.Tag == null)
                    {
                        lv.Tag = "v127-activated";
                        pf.Activated += delegate { try { lv.Invalidate(); } catch (Exception dg4909_1) { Diag.Swallow("MainForm.Ui.cs:3555", dg4909_1); } };
                        pf.Resize += delegate { try { lv.Invalidate(); } catch (Exception dg4910_1) { Diag.Swallow("MainForm.Ui.cs:3556", dg4910_1); } };
                    }
                    syncCover();
                }
                catch (Exception dg4914_1) { Diag.Swallow("MainForm.Ui.cs:3560", dg4914_1); }
            };
            lv.DrawColumnHeader += (s, e) =>
            {
                Rectangle r = e.Bounds;
                // v17.9：最后一列右边到列表右缘这一条，系统会拿主题白填，看着就是表格右上角一块白斑。
                // 这里把最后一列的表头向右铺到控件右边界，把那块白一起盖掉。
                int tail = 0;
                try
                {
                    if (lv.Columns.Count > 0 && e.Header == lv.Columns[lv.Columns.Count - 1])
                        tail = Math.Max(0, lv.ClientSize.Width - r.Right);
                }
                catch (Exception dg4927_1) { Diag.Swallow("MainForm.Ui.cs:3573", dg4927_1); }
                int fullW = r.Width + tail;
                using (SolidBrush b = new SolidBrush(headBg))
                    e.Graphics.FillRectangle(b, r.Left, r.Top, fullW, r.Height);
                using (Pen p = new Pen(headLine))
                    e.Graphics.DrawLine(p, r.Left, r.Bottom - 1, r.Left + fullW, r.Bottom - 1);
                using (Font f = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(headText))
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    e.Graphics.DrawString(e.Header.Text, f, b,
                        new RectangleF(r.X + 10, r.Y, r.Width - 18, r.Height), sf);
                }
            };
            // v14.0：行悬停高亮 + 选中行左侧主题色竖条，扫视时"鼠标在哪、选了哪"一眼看清
            Color hoverBg = Ui2.CtlInk(UiKit.A(58, 69, 88));
            int hv = -1;
            lv.MouseMove += delegate(object s0, MouseEventArgs e0)
            {
                ListViewItem hit = null;
                try { hit = lv.GetItemAt(e0.X, e0.Y); } catch (Exception dg4950_1) { Diag.Swallow("MainForm.Ui.cs:3596", dg4950_1); }
                int idx = (hit == null) ? -1 : hit.Index;
                if (idx != hv) { hv = idx; try { lv.Invalidate(); } catch (Exception dg4952_1) { Diag.Swallow("MainForm.Ui.cs:3598", dg4952_1); } }
            };
            lv.MouseLeave += delegate { if (hv != -1) { hv = -1; try { lv.Invalidate(); } catch (Exception dg4954_1) { Diag.Swallow("MainForm.Ui.cs:3600", dg4954_1); } } };
            lv.DrawItem += (s, e) =>
            {
                // v12.7：选中底色一律以 Item.Selected 为准（原来读 e.State，Win11 下偶尔把相邻两行都判成选中）
                bool sel = (e.Item != null) ? e.Item.Selected : ((e.State & ListViewItemStates.Selected) != 0);
                bool hov = !sel && e.ItemIndex == hv;
                Color bg = sel ? selBg : (hov ? hoverBg : (e.ItemIndex % 2 == 1 ? rowAlt : rowBg));
                using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
                if (sel)
                {
                    using (SolidBrush ab = new SolidBrush(Ui2.CtlInk(UiKit.ThemeColor)))
                        e.Graphics.FillRectangle(ab, e.Bounds.X, e.Bounds.Y + 1, 3, Math.Max(0, e.Bounds.Height - 2));
                }
            };
            lv.DrawSubItem += (s, e) =>
            {
                bool sel = (e.Item != null) ? e.Item.Selected : ((e.ItemState & ListViewItemStates.Selected) != 0);
                bool hov2 = !sel && e.ItemIndex == hv;
                Color bg = sel ? selBg : (hov2 ? hoverBg : (e.ItemIndex % 2 == 1 ? rowAlt : rowBg));
                using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
                Color fg = e.SubItem.ForeColor;
                using (SolidBrush b = new SolidBrush(fg))
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    e.Graphics.DrawString(e.SubItem.Text, lv.Font, b,
                        new RectangleF(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height), sf);
                }
            };
            // 让系统层面也切成深色：关掉视觉样式（Win11 会给空列表蒙一层白），再强制列表底色
            lv.HandleCreated += (s, e) =>
            {
                BeginInvoke((Action)delegate
                {
                    try { SetWindowTheme(lv.Handle, "DarkMode_Explorer", "DarkMode_Explorer"); } catch (Exception dg4990_1) { Diag.Swallow("MainForm.Ui.cs:3636", dg4990_1); }
                    // v12.8：系统滚动槽换成自绘深色（部分 Win11 版本主题 API 压不住浅灰槽）
                    try { DarkScrollBar.Attach(lv); } catch (Exception dg4992_1) { Diag.Swallow("MainForm.Ui.cs:3638", dg4992_1); }
                    // v8.9：这里原来把 lv.BackColor 又改回 C_INPUT，把上面反补的值覆盖掉了，
                    // 于是表头正下方总留一条比行亮的横带（实测 73,74,84）。改成同样反补。
                    Color cb0 = Ui2.CtlInk(UiKit.A(46, 54, 68));
                    int cref = cb0.R | (cb0.G << 8) | (cb0.B << 16);
                    try
                    {
                        SendMessage(lv.Handle, LVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)cref);
                        SendMessage(lv.Handle, LVM_SETTEXTBKCOLOR, IntPtr.Zero, (IntPtr)cref);
                        // v12.7：原生层也开双击缓冲（LVM_SETEXTENDEDLISTVIEWSTYLE / LVS_EX_DOUBLEBUFFER）
                        try { SendMessage(lv.Handle, 0x1036, (IntPtr)0x00010000, (IntPtr)0x00010000); } catch (Exception dg5002_1) { Diag.Swallow("MainForm.Ui.cs:3648", dg5002_1); }
                        lv.BackColor = cb0;
                        lv.Invalidate(true);
                        lv.Update();
                    }
                    catch (Exception dg5007_1) { Diag.Swallow("MainForm.Ui.cs:3653", dg5007_1); }
                });
            };
            // 用 1px 宽的空图片列表把行高撑到 26px，观感更松弛
            ImageList il = new ImageList();
            il.ImageSize = new Size(1, 24);
            lv.SmallImageList = il;
            int sumW = 0;
            for (int i = 0; i < cols.Length; i++)
            {
                lv.Columns.Add(cols[i], widths[i]);
                sumW += widths[i];
            }
            // 补一列把剩余宽度占满：否则表头右侧会留一块系统绘制的纯白方块
            if (w - sumW > 26) { lv.Columns.Add("", w - sumW - 20); }   // v12.7：留 20px 给竖向滚动条，免得横向滚条挤掉一行
            lv.Location = new Point(x, y);
            lv.MaxHeight = h;
            lv.Size = new Size(w, h);
            lv.FitHeight();
            if (UiKit.Paper)
            {
                // 原生列表头在浅色主题下仍是系统白，和纸面打架。纸感统一盖一条自绘表头，
                // 颜色、字号、列位全部跟随列表，窗口缩放时靠 LocationChanged/SizeChanged 跟随。
                Panel hdr = new Panel();
                hdr.BackColor = Color.FromArgb(238, 230, 212);
                hdr.Height = 25;
                hdr.Paint += (s, e) =>
                {
                    try
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.Clear(Color.FromArgb(238, 230, 212));
                        int cx = 10;
                        using (Font hf = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(76, 69, 55)))
                        {
                            for (int ci = 0; ci < lv.Columns.Count; ci++)
                            {
                                int cw = lv.Columns[ci].Width;
                                if (cw <= 0) break;
                                Rectangle rr = new Rectangle(cx, 0, Math.Max(0, cw - 18), hdr.Height);
                                StringFormat sf = new StringFormat();
                                sf.LineAlignment = StringAlignment.Center;
                                sf.FormatFlags = StringFormatFlags.NoWrap;
                                sf.Trimming = StringTrimming.EllipsisCharacter;
                                e.Graphics.DrawString(lv.Columns[ci].Text, hf, hb, rr, sf);
                                cx += cw;
                            }
                        }
                        using (Pen pl = new Pen(Color.FromArgb(205, 196, 176), 1f))
                            e.Graphics.DrawLine(pl, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1);
                    }
                    catch (Exception dgHdr_1) { Diag.Swallow("MainForm.Ui.cs:hdr", dgHdr_1); }
                };
                Action placeHdr = delegate
                {
                    try
                    {
                        if (lv.Parent == null) return;
                        if (hdr.Parent != lv.Parent) { lv.Parent.Controls.Add(hdr); }
                        hdr.Bounds = new Rectangle(lv.Left, lv.Top, lv.Width, 25);
                        hdr.BringToFront();
                    }
                    catch (Exception dgHdr_2) { Diag.Swallow("MainForm.Ui.cs:hdr2", dgHdr_2); }
                };
                lv.ParentChanged += (s, e) => placeHdr();
                lv.LocationChanged += (s, e) => placeHdr();
                lv.SizeChanged += (s, e) => { placeHdr(); hdr.Invalidate(); };
            }
            return lv;
        }

        /// <summary>高度跟着条数走的深色列表：没数据时只留一条表头，避免系统把空区涂成灰白色。</summary>
        // v9.0：空态插画。全部走 GDI+，不引入任何图片资源，安装包体积一分不涨。
    // 一键重装页画"显示器 + 四格窗 + 环形箭头"，C盘清理页画"盘片格 + 扫帚"。
    // 统一低对比度（alpha 90 上下），只当背景氛围，任何字号都不会被它压住。
    static class EmptyArt
    {
        static Color Ink(int a) { return Color.FromArgb(a, 104, 126, 158); }

        // 一键重装：一台显示器，屏里四个小方块（系统盘那种"格"），外圈一个回转箭头
        public static void ResetArt(Graphics g, Rectangle rc)
        {
            if (rc.Width < 170 || rc.Height < 100) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState st = g.Save();
            g.TranslateTransform(rc.X + rc.Width / 2f, rc.Y + rc.Height / 2f + 2);
            try
            {
                float w = 118f, h = 76f;
                Rectangle mr = new Rectangle((int)(-w / 2), (int)(-h / 2) - 4, (int)w, (int)h);
                using (GraphicsPath mp = UiKit.RoundedPath(mr, 8))
                {
                    using (SolidBrush b = new SolidBrush(UiKit.AA(24, 118, 148, 190))) g.FillPath(b, mp);
                    using (Pen p = new Pen(Ink(120), 1.4f)) g.DrawPath(p, mp);
                }
                // 屏（内缩一圈，稍微亮一点点，读得出来是"屏幕"而不是一块板）
                Rectangle inr = new Rectangle(mr.X + 8, mr.Y + 8, mr.Width - 16, mr.Height - 16);
                using (SolidBrush b = new SolidBrush(UiKit.AA(22, 86, 140, 222))) g.FillRectangle(b, inr);
                using (Pen p = new Pen(UiKit.AA(70, 86, 140, 222), 1f)) g.DrawRectangle(p, inr.X, inr.Y, inr.Width - 1, inr.Height - 1);
                // 屏里 2×2 的四个小格
                int cw = 11, gap = 5;
                int cx0 = inr.X + inr.Width / 2 - (cw * 2 + gap) / 2;
                int cy0 = inr.Y + inr.Height / 2 - (cw * 2 + gap) / 2;
                for (int r0 = 0; r0 < 2; r0++)
                    for (int c0 = 0; c0 < 2; c0++)
                        using (SolidBrush b = new SolidBrush(UiKit.AA(120, 86, 140, 222)))
                            g.FillRectangle(b, cx0 + c0 * (cw + gap), cy0 + r0 * (cw + gap), cw, cw);
                // 支架：脖子 + 底座
                int neckTop = mr.Bottom;
                using (Pen p = new Pen(Ink(92), 1.2f))
                {
                    g.DrawLine(p, 0, neckTop, 0, neckTop + 14);
                    g.DrawLine(p, -26, neckTop + 14, 26, neckTop + 14);
                }
                // 回转箭头：从右上起，绕一圈，收在左上
                float R = 74f;
                using (Pen p = new Pen(UiKit.AA(132, 86, 140, 222), 1.8f))
                    g.DrawArc(p, -R, -R, R * 2, R * 2, -22, 300);
                double aEnd = (300 - 22) * Math.PI / 180.0;
                float ex = (float)(R * Math.Cos(aEnd)), ey = (float)(R * Math.Sin(aEnd));
                float tx = (float)(-Math.Sin(aEnd)), ty = (float)(Math.Cos(aEnd));
                float nx = (float)(Math.Cos(aEnd)), ny = (float)(Math.Sin(aEnd));
                PointF apex = new PointF(ex + tx * 8f, ey + ty * 8f);
                PointF b1 = new PointF(apex.X - tx * 14f + nx * 8.5f, apex.Y - ty * 14f + ny * 8.5f);
                PointF b2 = new PointF(apex.X - tx * 14f - nx * 8.5f, apex.Y - ty * 14f - ny * 8.5f);
                using (SolidBrush b = new SolidBrush(UiKit.AA(176, 86, 140, 222)))
                    g.FillPolygon(b, new PointF[] { apex, b1, b2 });
                // 底部三个小点，收个尾
                for (int i2 = 0; i2 < 3; i2++)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(84 - i2 * 22, 104, 126, 158)))
                        g.FillEllipse(b, -22 + i2 * 16, (int)(h / 2) + 26, 5, 5);
            }
            finally { g.Restore(st); }
        }

        // C盘清理：一块盘片（里面几根高低不同的柱子＝占用量），旁边一把扫帚
        public static void CleanArt(Graphics g, Rectangle rc)
        {
            if (rc.Width < 210 || rc.Height < 110) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState st = g.Save();
            // 整组图形横向重心在 rc 中心：盘片偏左、扫帚偏右，合起来以 -6 为心
            g.TranslateTransform(rc.X + rc.Width / 2f - 6, rc.Y + rc.Height / 2f + 2);
            try
            {
                // ---- 盘片 ----
                Rectangle dr = new Rectangle(-112, -40, 148, 82);
                using (GraphicsPath dp = UiKit.RoundedPath(dr, 9))
                {
                    using (SolidBrush b = new SolidBrush(UiKit.AA(20, 118, 148, 190))) g.FillPath(b, dp);
                    using (Pen p = new Pen(Ink(102), 1.3f)) g.DrawPath(p, dp);
                }
                int[] hs = { 22, 44, 30, 50 };
                Color[] bc = { UiKit.AA(118, 86, 140, 222), UiKit.AA(118, 234, 172, 96),
                               UiKit.AA(108, 108, 200, 172), UiKit.AA(94, 228, 128, 156) };
                int bw = 15, bgap = 11, bx0 = dr.X + 16, byBottom = dr.Bottom - 13;
                for (int i3 = 0; i3 < 4; i3++)
                    using (SolidBrush b = new SolidBrush(bc[i3]))
                        g.FillRectangle(b, bx0 + i3 * (bw + bgap), byBottom - hs[i3], bw, hs[i3]);
                using (Pen p = new Pen(Ink(84), 1f))
                    g.DrawLine(p, dr.X + 11, byBottom + 1, dr.Right - 11, byBottom + 1);

                // ---- 右侧：三道"扫过去"的弧 + 几粒被带走的星屑 ----
                // 原来画的那把写实扫帚在 1× 下读成"旗子/锤子"，换成更符号化的清扫轨迹：
                // 三道同心的弧（半径越大越淡）+ 三粒小十字，安静、现代，也不会跟按钮抢注意力。
                float ox = 92f, oy = -6f;
                for (int k = 0; k < 3; k++)
                {
                    float rr = 20f + k * 12f;
                    using (Pen p = new Pen(Color.FromArgb(120 - k * 32, 108, 200, 172), 1.6f))
                        g.DrawArc(p, ox - rr, oy - rr, rr * 2, rr * 2, -58, 116);
                }
                // 星屑：三粒小十字，alpha 递减，斜着一串出去
                for (int k = 0; k < 3; k++)
                {
                    float sx0 = ox + 30f + k * 15f, sy0 = oy - 22f - k * 9f;
                    float ln = 5f - k;
                    using (Pen p = new Pen(Color.FromArgb(104 - k * 26, 234, 172, 96), 1.2f))
                    {
                        g.DrawLine(p, sx0 - ln, sy0, sx0 + ln, sy0);
                        g.DrawLine(p, sx0, sy0 - ln, sx0, sy0 + ln);
                    }
                }
            }
            finally { g.Restore(st); }
        }
    }

    class DarkListView : ListView
        {
            public int MaxHeight = 250;

            public DarkListView()
            {
                // v12.7：整表双击缓冲。之前点一下窗口/滚动一格，就会出现「整行为底色、子项文字没画」的空白行
                //（自绘分手：NM_CUSTOMDRAW 对未失效的子项只给 DrawItem 不给 DrawSubItem）。开缓冲后一次画全。
                try { DoubleBuffered = true; } catch (Exception dg5154_1) { Diag.Swallow("MainForm.Ui.cs:3800", dg5154_1); }
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            }
            const int LVM_INSERTITEM_M = 0x104D;
            const int LVM_DELETEITEM_M = 0x1008;
            const int LVM_DELETEALLITEMS_M = 0x1009;

            public void FitHeight()
            {
                int want = 30 + Items.Count * 26 + 2;
                int target = want > MaxHeight ? MaxHeight : want;
                // v8.6：空列表不再只留一条表头（那样看着像"表格坏了"）。下方有空位时给一块 96px 的空面板，
                // 由 MakeDarkList 的空态绘制在里面写一行「暂无数据」；下方真没地方就维持一条表头。
                if (Items.Count == 0)
                {
                    int room = 96;
                    try
                    {
                        if (Parent != null) room = Parent.ClientSize.Height - Top - 12;
                    }
                    catch (Exception dg5174_1) { Diag.Swallow("MainForm.Ui.cs:3820", dg5174_1); }
                    if (room >= 56 && room > target)
                    {
                        int want2 = (room > 96 ? 96 : room);
                        // 长高之后不许压到任何同级控件（按钮、提示行），压到就维持一条表头
                        bool clash = false;
                        try
                        {
                            Rectangle willBe = new Rectangle(Left, Top, Width, want2);
                            for (int i2 = 0; i2 < Parent.Controls.Count; i2++)
                            {
                                Control sb = Parent.Controls[i2];
                                if (sb == this || !sb.Visible || sb.Width < 8 || sb.Height < 8) continue;
                                if (sb.Dock != DockStyle.None) continue;
                                if (willBe.IntersectsWith(sb.Bounds)) { clash = true; break; }
                            }
                        }
                        catch (Exception dg5191_1) { Diag.Swallow("MainForm.Ui.cs:3837", dg5191_1); }
                        if (!clash) target = want2;
                    }
                }
                if (Height != target) Height = target;
            }

            protected override void WndProc(ref Message m)
            {
                // v8.6：列表一旦比"表头"高，系统就会把下面那段空行区涂成灰白（LVM_SETBKCOLOR 治不了）。
                // 这里直接接管 WM_ERASEBKGND，用控件底色铺一遍并宣告已处理，灰块就从根上没有了。
                if (m.Msg == 0x0014)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHdc(m.WParam))
                        using (SolidBrush b = new SolidBrush(BackColor))
                            g.FillRectangle(b, ClientRectangle);
                        m.Result = (IntPtr)1;
                        return;
                    }
                    catch (Exception dg5212_1) { Diag.Swallow("MainForm.Ui.cs:3858", dg5212_1); }
                }
                base.WndProc(ref m);
                if (m.Msg == LVM_INSERTITEM_M || m.Msg == LVM_DELETEITEM_M || m.Msg == LVM_DELETEALLITEMS_M)
                {
                    try { BeginInvoke((Action)FitHeight); } catch (Exception dg5217_1) { Diag.Swallow("MainForm.Ui.cs:3863", dg5217_1); }
                }
            }
        }

        void DrawCentered(Graphics g, Control c, string text, float size, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Font f = new Font("Microsoft YaHei UI", size, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(color))
                    g.DrawString(text, f, b, new RectangleF(0, 0, c.Width, c.Height), sf);
            }
        }

        // 液态玻璃卡片：半透明渐变底 + 顶部高光反射 + 冷色描边
        // v15.0：卡面统一到工作台三卡同一色阶（57,66,82 → 34,40,52），高光从 42 降到 16。
        // 之前各页传的底色偏亮（50,56,68）再叠 42 的高光，卡面被抬到 75,80,90，和首页比"发灰发脏"。
        void DrawGlassCard(Graphics g, Rectangle rc, int radius, Color baseTop, Color baseBottom)
        {
            // v18.7 纸感手绘：纸白卡 + 墨线描边 + 右下硬阴影，不做玻璃和渐变
            if (UiKit.Paper)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int rr2 = 7;
                Rectangle shr = new Rectangle(rc.X + 3, rc.Y + 4, rc.Width, rc.Height);
                using (GraphicsPath sp0 = UiKit.RoundedPath(shr, rr2))
                using (SolidBrush sb0 = new SolidBrush(Color.FromArgb(24, 96, 78, 54)))
                    g.FillPath(sb0, sp0);
                using (GraphicsPath path0 = UiKit.RoundedPath(rc, rr2))
                {
                    using (SolidBrush fb = new SolidBrush(Color.FromArgb(255, 243, 236, 220)))
                        g.FillPath(fb, path0);
                    using (Pen inkp = new Pen(Color.FromArgb(255, 178, 165, 141), 1.15f))
                        g.DrawPath(inkp, path0);
                    using (Pen hl = new Pen(Color.FromArgb(70, 255, 255, 250), 1f))
                        g.DrawLine(hl, rc.X + 12, rc.Y + 1, rc.Right - 12, rc.Y + 1);
                }
                return;
            }
            // v16.0 玻璃卡：半透明深青底 + 内部柔光 + 冷白细边，靠明度差分层，不做彩色描边
            baseTop = UiKit.A(31, 47, 62);
            baseBottom = UiKit.A(16, 27, 40);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiKit.RoundedPath(rc, radius))
            {
                g.SetClip(path);
                using (LinearGradientBrush br = new LinearGradientBrush(rc, baseTop, baseBottom, 90f))
                    g.FillRectangle(br, rc);
                Rectangle gl = new Rectangle(rc.X, rc.Y, rc.Width, Math.Max(28, rc.Height / 2));
                using (LinearGradientBrush hl = new LinearGradientBrush(gl,
                    UiKit.AA(22, 255, 255, 255), UiKit.AA(0, 255, 255, 255), 90f))
                    g.FillRectangle(hl, gl);
                g.ResetClip();
                using (Pen pp = new Pen(UiKit.AA(34, 255, 255, 255), 1f))
                    g.DrawPath(pp, path);
                Rectangle sh = new Rectangle(rc.X + 3, rc.Y + rc.Height - 4, rc.Width - 6, 4);
                using (GraphicsPath sp = UiKit.RoundedPath(rc, radius))
                using (LinearGradientBrush sb = new LinearGradientBrush(sh,
                    UiKit.AA(0, 0, 0, 0), UiKit.AA(34, 0, 0, 0), 90f))
                    g.FillRectangle(sb, sh);
            }
        }

        void RingPaint(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(6, 6, ringPanel.Width - 12, ringPanel.Height - 12);
            float w = 11F;
            // 背景双环：外深内浅，增强层次
            using (Pen p1 = new Pen(UiKit.A(48, 55, 68), w)) { g.DrawArc(p1, rect, 0, 360); }
            using (Pen p2 = new Pen(UiKit.A(70, 79, 96), 2.2F)) { g.DrawArc(p2, rect, 0, 360); }
            float frac = 0F;
            if (taskActive && totalSec > 0)
            {
                double rem = (endTime - DateTime.Now).TotalSeconds;
                frac = (float)Math.Max(0, Math.Min(1, rem / totalSec));
            }
            if (frac > 0)
            {
                float sweep = (float)(frac * 360);
                using (Pen fgPen = new Pen(curActionColor, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(fgPen, rect, -90, sweep);
                // 弧线头部亮点，增加精致感
                double head = (-90 + sweep) * Math.PI / 180.0;
                float hx = rect.X + rect.Width / 2F + (float)(Math.Cos(head) * (rect.Width / 2F));
                float hy = rect.Y + rect.Height / 2F + (float)(Math.Sin(head) * (rect.Height / 2F));
                using (SolidBrush hb = new SolidBrush(UiKit.Lighten(curActionColor, 70)))
                    g.FillEllipse(hb, hx - 3.5F, hy - 3.5F, 7F, 7F);
            }
            else
            {
                // v9.1：空盘不再是一具"死表"。极淡的内圈呼吸光（alpha 12→38 正弦起伏）
                // 配一道约 10 秒一圈的细亮弧，把"空闲"画成"待命"。
                double pulse = (Math.Sin(breathPh) + 1) * 0.5;
                int a1 = (int)(12 + 26 * pulse);
                Rectangle ri = Rectangle.Inflate(rect, -5, -5);
                using (Pen pa = new Pen(Color.FromArgb(a1, 45, 206, 196), 16f))
                    g.DrawArc(pa, ri, 0, 360);
                float ang = (float)((breathPh * 20.0) % 360);
                using (Pen pg = new Pen(UiKit.AA(126, 56, 226, 214), 2f))
                    g.DrawArc(pg, Rectangle.Inflate(rect, -2, -2), ang, 52);
            }
            // 中间百分比
            string pct = taskActive ? ((int)(frac * 100)).ToString() + "%" : "--";
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Font f = new Font("Consolas", 20F, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(taskActive ? curActionColor : UiKit.A(152, 176, 206)))
                    g.DrawString(pct, f, b, new RectangleF(0, 0, ringPanel.Width, ringPanel.Height), sf);
            }
        }

        void UpdateModeUI()
        {
            pCountdown.Visible = rbCountdown.Checked;
            pDatetime.Visible = rbDatetime.Checked;
            pDaily.Visible = rbDaily.Checked;
            pWeekly.Visible = rbWeekly.Checked;
        }

                void ShowSfxPanel()
        {
            Form f = new Form();
            f.Text = T("音效设置 · 小工具箱", "Sounds · Mini Toolbox");
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(620, 516);
            f.BackColor = UiKit.A(18, 22, 29);
            f.ShowIcon = false;
            f.MaximizeBox = false;
            f.Font = new Font(Ui2.FamilyName, 9F);

            Label ttl = new Label();
            ttl.Text = T("音效设置", "Sound settings");
            ttl.ForeColor = UiKit.Txt(Color.White);
            ttl.Font = new Font(Ui2.FamilyName, 15F, FontStyle.Bold);
            ttl.BackColor = Color.Transparent;
            ttl.Location = new Point(20, 14);
            ttl.AutoSize = true;
            f.Controls.Add(ttl);

            Label sub = new Label();
            sub.Text = T("12 个提示音都重新调过：采样率 44.1k、加短尾音、去掉尖锐的锯齿味。点下面的按钮直接试听。",
                "All 12 cues retuned: 44.1 kHz, short room tail, no harsh sawtooth.");
            sub.ForeColor = UiKit.A(142, 154, 172);
            sub.Font = new Font(Ui2.FamilyName, 8.5F);
            sub.BackColor = Color.Transparent;
            sub.Location = new Point(20, 46);
            sub.Size = new Size(576, 22);
            f.Controls.Add(sub);

            Button bOn = new RoundBtn(UiKit.A(44, 52, 66), Color.White);
            bOn.Text = Sfx.On ? T("界面音效：开", "UI sounds: on") : T("界面音效：关", "UI sounds: off");
            bOn.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            bOn.Location = new Point(20, 84);
            bOn.Size = new Size(136, 34);
            bOn.Click += delegate
            {
                Sfx.Toggle();
                bOn.Text = Sfx.On ? T("界面音效：开", "UI sounds: on") : T("界面音效：关", "UI sounds: off");
                bOn.BackColor = Sfx.On ? UiKit.A(44, 82, 70) : UiKit.A(60, 40, 44);
                if (Sfx.On) Sfx.Click();
            };
            bOn.BackColor = Sfx.On ? UiKit.A(44, 82, 70) : UiKit.A(60, 40, 44);
            f.Controls.Add(bOn);

            Button bNight = new RoundBtn(UiKit.A(44, 52, 66), Color.White);
            bNight.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            bNight.Location = new Point(166, 84);
            bNight.Size = new Size(120, 34);

            // v19.6：夜间时段不再写死 22:00–08:00，改成两个可点的时间胶囊（点开是深色时间菜单）
            int qsH = 22, qeH = 8;
            string[] qr = Sfx.QuietRange.Split('–');
            if (qr.Length == 2)
            {
                int a0 = 0, b0 = 0;
                int.TryParse(qr[0].Substring(0, 2), out a0);
                int.TryParse(qr[1].Substring(0, 2), out b0);
                qsH = Math.Max(0, Math.Min(23, a0));
                qeH = Math.Max(0, Math.Min(23, b0));
            }
            Button cbQS = new RoundBtn(UiKit.A(34, 41, 53), Color.White);
            Button cbQE = new RoundBtn(UiKit.A(34, 41, 53), Color.White);
            foreach (Button qb in new Button[] { cbQS, cbQE })
            {
                qb.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                qb.Size = new Size(64, 26);
            }
            cbQS.Location = new Point(292, 90);
            cbQE.Location = new Point(368, 90);
            f.Controls.Add(cbQS);
            f.Controls.Add(cbQE);

            Action qText = delegate
            {
                cbQS.Text = string.Format("{0:00}:00", qsH);
                cbQE.Text = string.Format("{0:00}:00", qeH);
            };
            qText();

            EventHandler pickHour = delegate (object sp, EventArgs ep)
            {
                Button b2 = (Button)sp;
                bool isStart = (b2 == cbQS);
                int cur = isStart ? qsH : qeH;
                ContextMenuStrip qm = new ContextMenuStrip();
                qm.BackColor = UiKit.A(28, 32, 39);
                qm.ForeColor = UiKit.Txt(Color.White);
                qm.ShowImageMargin = false;
                qm.Padding = new Padding(6, 4, 6, 4);
                for (int hh = 0; hh < 24; hh++)
                {
                    int hv = hh;
                    var it = new ToolStripMenuItem(string.Format("{0:00}:00", hh) + (hh == cur ? "  ✓" : ""));
                    it.ForeColor = (hh == cur) ? UiKit.A(45, 212, 191) : UiKit.Txt(Color.White);
                    it.Padding = new Padding(8, 3, 8, 3);
                    it.Click += delegate
                    {
                        if (isStart) qsH = hv; else qeH = hv;
                        qText();
                        Sfx.SetQuietRange(qsH * 60, qeH * 60);
                        Sfx.Click();
                    };
                    qm.Items.Add(it);
                }
                qm.Show(b2, new Point(0, b2.Height + 4));
            };
            cbQS.Click += pickHour;
            cbQE.Click += pickHour;

            Label qTo = new Label();
            qTo.Text = "–";
            qTo.ForeColor = UiKit.A(150, 162, 180);
            qTo.BackColor = Color.Transparent;
            qTo.Location = new Point(358, 92);
            qTo.AutoSize = true;
            f.Controls.Add(qTo);

            Action syncNightText = delegate
            {
                bNight.Text = Sfx.Night ? T("夜间静默：开", "Quiet hours: on") : T("夜间静默：关", "Quiet hours: off");
                bNight.BackColor = Sfx.Night ? UiKit.A(40, 60, 92) : UiKit.A(44, 52, 66);
                cbQS.Enabled = cbQE.Enabled = Sfx.Night;
                cbQS.ForeColor = cbQE.ForeColor = Sfx.Night ? Color.White : UiKit.A(140, 150, 166);
                cbQS.BackColor = cbQE.BackColor = Sfx.Night ? UiKit.A(34, 41, 53) : UiKit.A(28, 33, 42);
                qTo.ForeColor = Sfx.Night ? UiKit.A(150, 162, 180) : UiKit.A(96, 106, 122);
            };
            bNight.Click += delegate
            {
                Sfx.SetNight(!Sfx.Night);
                syncNightText();
            };
            f.Controls.Add(bNight);
            syncNightText();

            Label qCap = new Label();
            qCap.Text = T("起止时段", "Window");
            qCap.ForeColor = UiKit.A(140, 152, 170);
            qCap.BackColor = Color.Transparent;
            qCap.Font = new Font(Ui2.FamilyName, 8F);
            qCap.Location = new Point(294, 68);
            qCap.AutoSize = true;
            f.Controls.Add(qCap);

            Label hint = new Label();
            hint.Text = T("夜间静默只关掉点击 / 切页 / 复制 / 投递 / 倒计时滴，完成、提醒、警告、出错、到点照常响。",
                "Quiet hours only mute the soft UI ticks; alerts still play.");
            hint.ForeColor = UiKit.A(116, 128, 146);
            hint.Font = new Font(Ui2.FamilyName, 8F);
            hint.BackColor = Color.Transparent;
            hint.Location = new Point(20, 126);
            hint.Size = new Size(580, 20);
            f.Controls.Add(hint);

            Label vcap = new Label();
            vcap.Text = T("音量", "Volume");
            vcap.ForeColor = UiKit.A(196, 206, 222);
            vcap.Font = new Font(Ui2.FamilyName, 9F);
            vcap.BackColor = Color.Transparent;
            vcap.Location = new Point(20, 158);
            vcap.AutoSize = true;
            f.Controls.Add(vcap);

            Button[] volBtns = new Button[3];
            string[] vn = new string[] { T("轻", "Soft"), T("中", "Normal"), T("响", "Loud") };
            for (int vi = 0; vi < 3; vi++)
            {
                int vv = vi;
                Button vb = new RoundBtn(UiKit.A(44, 52, 66), Color.White);
                vb.Text = vn[vi];
                vb.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
                vb.Location = new Point(80 + vi * 78, 152);
                vb.Size = new Size(70, 30);
                vb.BackColor = (Sfx.Volume == vi) ? UiKit.A(60, 110, 190) : UiKit.A(44, 52, 66);
                vb.Click += delegate
                {
                    Sfx.SetVolume(vv);
                    for (int k = 0; k < 3; k++) volBtns[k].BackColor = (k == vv) ? UiKit.A(60, 110, 190) : UiKit.A(44, 52, 66);
                    Sfx.Click();
                };
                volBtns[vi] = vb;
                f.Controls.Add(vb);
            }

            Label pcap = new Label();
            pcap.Text = T("音色", "Tone");
            pcap.ForeColor = UiKit.A(196, 206, 222);
            pcap.Font = new Font(Ui2.FamilyName, 9F);
            pcap.BackColor = Color.Transparent;
            pcap.Location = new Point(336, 158);
            pcap.AutoSize = true;
            f.Controls.Add(pcap);

            Button[] packBtns = new Button[3];
            for (int pi = 0; pi < 3; pi++)
            {
                int pv = pi;
                Button pb = new RoundBtn(UiKit.A(44, 52, 66), Color.White);
                pb.Text = T(Sfx.PackNames[pi], pi == 0 ? "Bright" : (pi == 1 ? "Normal" : "Deep"));
                pb.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
                pb.Location = new Point(378 + pi * 76, 152);
                pb.Size = new Size(70, 30);
                pb.BackColor = (Sfx.Pack == pi) ? UiKit.A(72, 92, 62) : UiKit.A(44, 52, 66);
                pb.Click += delegate
                {
                    Sfx.SetPack(pv);
                    System.Windows.Forms.Timer ptone = new System.Windows.Forms.Timer();
                    ptone.Interval = 360;
                    ptone.Tick += delegate { ptone.Stop(); ptone.Dispose(); Sfx.Preview(5); };
                    ptone.Start();
                    for (int k = 0; k < 3; k++) packBtns[k].BackColor = (k == pv) ? UiKit.A(72, 92, 62) : UiKit.A(44, 52, 66);
                    Sfx.Preview(0);
                };
                packBtns[pi] = pb;
                f.Controls.Add(pb);
            }

            Label lcap = new Label();
            lcap.Text = T("发声", "Level");
            lcap.ForeColor = UiKit.A(196, 206, 222);
            lcap.Font = new Font(Ui2.FamilyName, 9F);
            lcap.BackColor = Color.Transparent;
            lcap.Location = new Point(20, 196);
            lcap.AutoSize = true;
            f.Controls.Add(lcap);

            Button[] lvBtns = new Button[3];
            string[] ln = new string[] { T("全部", "All"), T("只重要", "Key only"), T("静默", "Silent") };
            for (int li = 0; li < 3; li++)
            {
                int lvv = li;
                Button lb = new RoundBtn(UiKit.A(44, 52, 66), Color.White);
                lb.Text = ln[li];
                lb.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
                lb.Location = new Point(80 + li * 90, 190);
                lb.Size = new Size(84, 30);
                lb.BackColor = (Sfx.Level == li) ? UiKit.A(60, 110, 190) : UiKit.A(44, 52, 66);
                lb.Click += delegate
                {
                    Sfx.SetLevel(lvv);
                    for (int k = 0; k < 3; k++) lvBtns[k].BackColor = (k == lvv) ? UiKit.A(60, 110, 190) : UiKit.A(44, 52, 66);
                    Sfx.Click();
                };
                lvBtns[li] = lb;
                f.Controls.Add(lb);
            }

            // 试听网格：3 列 × 4 行
            string[] names = new string[]
            {
                T("点击", "Click"), T("切页", "Switch"), T("复制", "Copy"),
                T("投递", "Drop"), T("保存", "Save"), T("完成", "Done"),
                T("提醒", "Bell"), T("警告", "Warn"), T("出错", "Error"),
                T("到点", "Alarm"), T("停止", "Stop"), T("启动", "Launch")
            };
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 3, row = i / 3;
                int ii = i;
                Button b = new RoundBtn(UiKit.A(36, 44, 57), Color.White);
                b.Text = names[i] + "  ▸";
                b.Font = new Font(Ui2.FamilyName, 9F);
                b.Location = new Point(20 + col * 196, 240 + row * 52);
                b.Size = new Size(186, 42);
                b.Click += delegate { Sfx.Preview(ii); };
                f.Controls.Add(b);
            }

            Button close = MakeBtn(T("关闭", "Close"), UiKit.A(38, 45, 58), Color.White, 500, 468, 96, 34);
            close.Click += delegate { f.Close(); };
            f.Controls.Add(close);

            Label tip2 = new Label();
            tip2.Text = T("提示：托盘右键 →「音效设置…」也能打开这个面板。", "Tip: tray right-click → Sound settings.");
            tip2.ForeColor = UiKit.A(110, 122, 138);
            tip2.Font = new Font(Ui2.FamilyName, 8F);
            tip2.BackColor = Color.Transparent;
            tip2.Location = new Point(22, 474);
            tip2.AutoSize = true;
            f.Controls.Add(tip2);

            f.KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Escape) f.Close(); };
            f.KeyPreview = true;
            f.Shown += delegate { Sfx.Preview(11); try { f.ActiveControl = null; } catch (Exception dg5625_1) { Diag.Swallow("MainForm.Ui.cs:4271", dg5625_1); } };
            f.ShowDialog(this);
        }

    }
}



