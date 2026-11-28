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
    class FirstRunForm : Form
    {
        const string MARK = "首次引导.txt";
        public static int StartStep = 0;
        int step = 0, pickSkin = 3, pickPack = 1;
        bool soundOn = true;
        readonly Panel[] pages = new Panel[3];
        readonly Label[] dots = new Label[3];
        Label cap, sub;
        Button bBack, bNext, bSkip;

        public static bool Needed()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                return !File.Exists(Path.Combine(dir, MARK));
            }
            catch { return false; }
        }

        void MarkDone()
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), MARK),
                    "done", new UTF8Encoding(false));
            }
            catch (Exception dg51_1) { Diag.Swallow("FirstRunForm.cs:51", dg51_1); }
        }

        public FirstRunForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 470);
            BackColor = UiKit.A(16, 20, 27);
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            pickSkin = UiKit.SkinIdx;
            soundOn = Sfx.On;
            pickPack = Sfx.Pack;

            cap = new Label();
            cap.Text = "";
            cap.ForeColor = UiKit.Txt(Color.White);
            cap.Font = new Font(Ui2.FamilyName, 16F, FontStyle.Bold);
            cap.BackColor = Color.Transparent;
            cap.Location = new Point(32, 26);
            cap.AutoSize = true;
            Controls.Add(cap);

            sub = new Label();
            sub.Text = "";
            sub.ForeColor = UiKit.A(140, 152, 170);
            sub.Font = new Font(Ui2.FamilyName, 9F);
            sub.BackColor = Color.Transparent;
            sub.Location = new Point(34, 58);
            sub.Size = new Size(560, 22);
            Controls.Add(sub);

            Button bx = new RoundBtn(UiKit.A(38, 46, 58), Color.White);
            bx.Text = "✕";
            bx.Font = new Font(Ui2.FamilyName, 8.5F, FontStyle.Bold);
            bx.Size = new Size(30, 26);
            bx.Location = new Point(ClientSize.Width - 44, 18);
            bx.Click += delegate { Finish(false); };
            Controls.Add(bx);

            for (int i = 0; i < 3; i++)
            {
                Label d = new Label();
                d.Size = new Size(10, 10);
                d.Location = new Point(26, 122 + i * 22);
                d.BackColor = UiKit.A(70, 82, 100);
                Controls.Add(d);
                dots[i] = d;
            }

            for (int i = 0; i < 3; i++)
            {
                Panel p = new Panel();
                p.Location = new Point(48, 108);
                p.Size = new Size(576, 268);
                p.BackColor = Color.Transparent;
                p.Visible = false;
                Controls.Add(p);
                pages[i] = p;
            }

            BuildPage0();
            BuildPage1();
            BuildPage2();

            bBack = new RoundBtn(UiKit.A(36, 44, 56), Color.White);
            bBack.Text = Lang.T("上一步", "Back");
            bBack.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            bBack.Size = new Size(96, 36);
            bBack.Location = new Point(48, 396);
            bBack.Click += delegate { if (step > 0) ShowStep(step - 1); };
            Controls.Add(bBack);

            bSkip = new RoundBtn(UiKit.A(36, 44, 56), Color.White);
            bSkip.Text = Lang.T("跳过", "Skip");
            bSkip.Font = new Font(Ui2.FamilyName, 9F);
            bSkip.Size = new Size(88, 36);
            bSkip.Location = new Point(ClientSize.Width - 260, 396);
            bSkip.Click += delegate { Finish(true); };
            Controls.Add(bSkip);

            bNext = new RoundBtn(UiKit.A(30, 120, 106), Color.White);
            bNext.Text = Lang.T("下一步", "Next");
            bNext.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            bNext.Size = new Size(150, 36);
            bNext.Location = new Point(ClientSize.Width - 156, 396);
            bNext.Click += delegate
            {
                if (step < 2) ShowStep(step + 1);
                else Finish(true);
            };
            Controls.Add(bNext);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Finish(true);
            };
            // 顶部留白可拖动
            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < 72) DragWindow();
            };

            ShowStep(Math.Max(0, Math.Min(2, StartStep)));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        void DragWindow()
        {
            try { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } catch (Exception dg165_1) { Diag.Swallow("FirstRunForm.cs:165", dg165_1); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                using (GraphicsPath p = UiKit.RoundedPath(new Rectangle(0, 0, Width, Height), 18))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch (Exception dg180_1) { Diag.Swallow("FirstRunForm.cs:180", dg180_1); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle rc = ClientRectangle;
            using (System.Drawing.Drawing2D.LinearGradientBrush br = new System.Drawing.Drawing2D.LinearGradientBrush(
                rc, UiKit.A(24, 30, 40), UiKit.A(14, 18, 24), 90f))
                g.FillRectangle(br, rc);
            using (Pen edge = new Pen(UiKit.A(70, 88, 112), 1f))
                g.DrawRectangle(edge, 0, 0, rc.Width - 1, rc.Height - 1);
            using (SolidBrush ac = new SolidBrush(UiKit.A(52, 200, 176)))
                g.FillRectangle(ac, 32, 88, 56, 3);
            base.OnPaint(e);
        }

        void ClearPage(Panel p) { p.Controls.Clear(); }

        Button Chip(string text, int x, int y, int w, int h, Color bg, bool mark, EventHandler onClick)
        {
            RoundBtn b = new RoundBtn(bg, Color.White);
            b.Text = text;
            b.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            b.Size = new Size(w, h);
            b.Location = new Point(x, y);
            b.Mark = mark;
            b.Click += onClick;
            return b;
        }

        Label Line(Panel p, int x, int y, int w, string title, string desc, Color c)
        {
            Panel box = new Panel();
            box.Location = new Point(x, y);
            box.Size = new Size(w, 62);
            box.BackColor = Color.Transparent;
            p.Controls.Add(box);
            Panel bar = new Panel();
            bar.Location = new Point(0, 12);
            bar.Size = new Size(4, 38);
            bar.BackColor = c;
            box.Controls.Add(bar);
            Label t = new Label();
            t.Text = title;
            t.ForeColor = UiKit.Txt(Color.White);
            t.Font = new Font(Ui2.FamilyName, 10.5F, FontStyle.Bold);
            t.BackColor = Color.Transparent;
            t.Location = new Point(16, 8);
            t.AutoSize = true;
            box.Controls.Add(t);
            Label d = new Label();
            d.Text = desc;
            d.ForeColor = UiKit.A(142, 154, 172);
            d.Font = new Font(Ui2.FamilyName, 8.5F);
            d.BackColor = Color.Transparent;
            d.Location = new Point(16, 32);
            d.Size = new Size(w - 24, 26);
            box.Controls.Add(d);
            return d;
        }

        readonly RoundBtn[] skinBtns = new RoundBtn[4];

        void BuildPage0()
        {
            Panel p = pages[0];
            string[] ti = new string[]
            {
                Lang.T("9 个页面 + 16 个增强工具", "9 pages + 16 power tools"),
                Lang.T("定时关机 / 清理 / 下载 / Steam 全在一个程序里", "Timers, cleanup, downloads, Steam — one app"),
                Lang.T("关掉窗口只是进托盘，任务照跑", "Closing the window only hides it to the tray")
            };
            string[] de = new string[]
            {
                Lang.T("工作台、定时关机、C 盘清理、Steam 成就、视频/音乐下载、拼图、水印、汇率、配置诊断。", "Workbench, timer, cleanup, Steam, downloads, puzzle, watermark, FX, diagnostics."),
                Lang.T("不用再装一堆小软件；五套语言、四套界面风格、全程键盘可达。", "No more tool sprawl: 5 languages, 4 visual styles, full keyboard access."),
                Lang.T("托盘图标右键可看倒计时、切风格、调音效；Alt+1…9 直接切页。", "Tray menu: countdown, style, sounds. Alt+1…9 switches pages.")
            };
            Color[] cs = new Color[] { UiKit.A(52, 200, 176), UiKit.A(70, 130, 220), UiKit.A(230, 168, 72) };
            for (int i = 0; i < 3; i++) Line(p, 0, 6 + i * 78, 576, ti[i], de[i], cs[i]);
        }

        void BuildPage1()
        {
            Panel p = pages[1];
            string[] desc = new string[]
            {
                Lang.T("纯黑 + 亮黄，直角硬边", "Black + yellow, hard edges"),
                Lang.T("近黑 + 青洋红霓虹", "Near-black + cyan neon"),
                Lang.T("紫调 + 超大圆角", "Violet + huge radii"),
                Lang.T("深中性 + 青绿（推荐）", "Neutral + teal (recommended)")
            };
            for (int i = 0; i < 4; i++)
            {
                int si = i;
                int x = (i % 2) * 288, y = (i / 2) * 122;
                RoundBtn b = new RoundBtn(UiKit.A(30, 38, 48), Color.White);
                b.Text = UiKit.SKIN_NAMES[i] + "\r\n" + desc[i];
                b.Font = new Font(Ui2.FamilyName, 9.5F, FontStyle.Bold);
                b.Size = new Size(276, 108);
                b.Location = new Point(x, y);
                b.Mark = (i == pickSkin);
                b.TextAlign = ContentAlignment.MiddleCenter;
                b.Click += delegate
                {
                    pickSkin = si;
                    for (int k = 0; k < 4; k++) skinBtns[k].Mark = (k == si);
                    for (int k = 0; k < 4; k++) skinBtns[k].Invalidate();
                };
                skinBtns[i] = b;
                p.Controls.Add(b);
            }
            Label note = new Label();
            note.Text = Lang.T("选中的风格会在下次启动时完整生效（现在也可以先进入看看）。", "The chosen style applies fully on next launch.");
            note.ForeColor = UiKit.A(120, 132, 150);
            note.Font = new Font(Ui2.FamilyName, 8.5F);
            note.BackColor = Color.Transparent;
            note.Location = new Point(0, 248);
            note.Size = new Size(560, 20);
            p.Controls.Add(note);
        }

        readonly RoundBtn[] packBtns = new RoundBtn[3];
        Button bSound;

        void BuildPage2()
        {
            Panel p = pages[2];
            bSound = new RoundBtn(soundOn ? UiKit.A(44, 82, 70) : UiKit.A(60, 40, 44), Color.White);
            bSound.Text = soundOn ? Lang.T("界面音效：开", "Sounds: on") : Lang.T("界面音效：关", "Sounds: off");
            bSound.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
            bSound.Size = new Size(150, 34);
            bSound.Location = new Point(0, 0);
            bSound.Click += delegate
            {
                soundOn = !soundOn;
                bSound.Text = soundOn ? Lang.T("界面音效：开", "Sounds: on") : Lang.T("界面音效：关", "Sounds: off");
                bSound.BackColor = soundOn ? UiKit.A(44, 82, 70) : UiKit.A(60, 40, 44);
                if (soundOn) Sfx.Preview(0);
            };
            p.Controls.Add(bSound);

            Label pc = new Label();
            pc.Text = Lang.T("音色", "Tone");
            pc.ForeColor = UiKit.A(196, 206, 222);
            pc.Font = new Font(Ui2.FamilyName, 9F);
            pc.BackColor = Color.Transparent;
            pc.Location = new Point(176, 8);
            pc.AutoSize = true;
            p.Controls.Add(pc);

            for (int i = 0; i < 3; i++)
            {
                int pi = i;
                RoundBtn b = new RoundBtn(UiKit.A(44, 52, 66), Color.White);
                b.Text = new string[] { Lang.T("清亮", "Bright"), Lang.T("标准", "Normal"), Lang.T("沉稳", "Deep") }[i];
                b.Font = new Font(Ui2.FamilyName, 9F, FontStyle.Bold);
                b.Size = new Size(76, 32);
                b.Location = new Point(220 + i * 84, 1);
                b.BackColor = (i == pickPack) ? UiKit.A(72, 92, 62) : UiKit.A(44, 52, 66);
                b.Click += delegate
                {
                    pickPack = pi;
                    for (int k = 0; k < 3; k++) packBtns[k].BackColor = (k == pi) ? UiKit.A(72, 92, 62) : UiKit.A(44, 52, 66);
                    Sfx.SetPack(pi);
                    Sfx.Preview(0);
                };
                packBtns[i] = b;
                p.Controls.Add(b);
            }

            Line(p, 0, 64, 576,
                Lang.T("关闭窗口 = 进托盘", "Closing hides to the tray"),
                Lang.T("点右上角 ✕ 不是退出；在托盘图标右键「退出」才真正结束，倒计时任务继续跑。", "The X hides it; use tray → Exit to quit. Timers keep running."),
                UiKit.A(52, 200, 176));

            Line(p, 0, 142, 576,
                Lang.T("键盘直接走完全程", "Everything reachable by keyboard"),
                Lang.T("Alt+1…9 切页，Ctrl+Tab 循环，Esc 关弹窗，Tab 有清楚的焦点环。", "Alt+1…9 pages, Ctrl+Tab cycles, Esc closes popups, Tab shows a focus ring."),
                UiKit.A(70, 130, 220));
        }

        void ShowStep(int s)
        {
            step = s;
            string[] caps = new string[]
            {
                Lang.T("第一次用？三分钟就上手", "First time? Three quick steps"),
                Lang.T("挑一个顺眼的界面风格", "Pick a look that fits you"),
                Lang.T("音效，以及它的脾气", "Sounds, and how it behaves")
            };
            string[] subs = new string[]
            {
                Lang.T("下面三步都可以跳过，之后在托盘菜单里随时改。", "You can skip any step; everything stays changeable in the tray menu."),
                Lang.T("四套整站皮肤，配色、圆角、强调色一起换。", "Four full skins: colors, radii and accent shift together."),
                Lang.T("声音很轻，但关键时刻有存在感；夜间自动收敛。", "Soft by default, present when it matters, quiet at night.")
            };
            cap.Text = caps[s];
            sub.Text = subs[s];
            for (int i = 0; i < 3; i++) pages[i].Visible = (i == s);
            for (int i = 0; i < 3; i++)
                dots[i].BackColor = (i == s) ? UiKit.A(52, 200, 176) : UiKit.A(70, 82, 100);
            bBack.Visible = s > 0;
            bSkip.Visible = s < 2;
            bNext.Text = s < 2 ? Lang.T("下一步", "Next") : Lang.T("开始使用", "Start");
            bSkip.Location = new Point(ClientSize.Width - 260, 396);
            Invalidate();
        }

        void Finish(bool apply)
        {
            bool skinChanged = false;
            try
            {
                if (apply)
                {
                    skinChanged = pickSkin != UiKit.SkinIdx;
                    if (skinChanged) UiKit.SetSkin(UiKit.SKIN_NAMES[pickSkin], true);
                    Sfx.SetOn(soundOn);
                    Sfx.SetPack(pickPack);
                }
                MarkDone();
            }
            catch (Exception dg404_1) { Diag.Swallow("FirstRunForm.cs:404", dg404_1); }
            Close();
            if (skinChanged)
            {
                try
                {
                    if (MessageBox.Show("界面风格已保存。整站配色要重新绘一遍才会生效，现在重启吗？\n（选“否”也可以稍后自己重启）",
                        "界面风格", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        Application.Restart();
                }
                catch (Exception dg414_1) { Diag.Swallow("FirstRunForm.cs:414", dg414_1); }
            }
        }
    }
}