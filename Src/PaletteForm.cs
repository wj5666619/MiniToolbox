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
    class PaletteForm : Form
    {
        TextBox box;
        Label ph;
        Label cnt;
        Label tip;
        Label glyph;
        PalList list;
        List<PaletteItem> all;
        List<PaletteItem> view = new List<PaletteItem>();
        bool en;
        string tipDefault;

        public Func<string, PaletteItem[]> BuildIntent;

        // v11.0：全局搜索复用同一个面板外壳，只把提示语换掉
        public void SetHint(string s)
        {
            try { if (ph != null) ph.Text = s; } catch (Exception dg41_1) { Diag.Swallow("PaletteForm.cs:41", dg41_1); }
        }
        public Func<List<string>> RecentProvider;
        public Action<PaletteItem> AfterRun;

        public PaletteForm(List<PaletteItem> items, bool langEn)
        {
            en = langEn;
            all = items;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = UiKit.A(20, 24, 30);
            ClientSize = new Size(624, 474);
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (int)(wa.Height * 0.20));
            }
            catch (Exception dg61_1) { Diag.Swallow("PaletteForm.cs:61", dg61_1); }

            Panel edge = new Panel();
            edge.Dock = DockStyle.Fill;
            edge.BackColor = UiKit.A(20, 24, 30);
            edge.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(UiKit.A(70, 84, 104)))
                    g.DrawRectangle(pen, 0, 0, edge.Width - 1, edge.Height - 1);
                Rectangle rc = new Rectangle(14, 12, edge.Width - 28, 40);
                using (GraphicsPath path = UiKit.RoundedPath(rc, 8))
                {
                    using (SolidBrush br = new SolidBrush(UiKit.A(30, 36, 45))) g.FillPath(br, path);
                    using (Pen p2 = new Pen(UiKit.A(64, 76, 94))) g.DrawPath(p2, path);
                }
            };
            Controls.Add(edge);

            glyph = new Label();
            glyph.Text = "\uE721";
            glyph.Font = new Font("Segoe MDL2 Assets", 11F);
            glyph.ForeColor = UiKit.A(120, 134, 154);
            glyph.BackColor = UiKit.A(30, 36, 45);
            glyph.Location = new Point(26, 25);
            glyph.AutoSize = true;
            glyph.Cursor = Cursors.IBeam;
            glyph.Click += delegate { box.Focus(); };
            edge.Controls.Add(glyph);

            box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.BackColor = UiKit.A(30, 36, 45);
            box.ForeColor = UiKit.A(238, 244, 252);
            box.Font = new Font("Microsoft YaHei UI", 13F);
            box.Location = new Point(52, 19);
            box.Size = new Size(543, 23);
            box.TextChanged += delegate { ph.Visible = (box.Text.Length == 0); Filter(); };
            box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { RunSel(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { Sfx.Click(); Close(); }
                else if (e.KeyCode == Keys.Tab)
                {
                    if (list.SelectedIndex >= 0 && list.SelectedIndex < view.Count)
                    {
                        PaletteItem it = view[list.SelectedIndex];
                        box.Text = en ? it.En : it.Zh;
                        box.SelectionStart = box.Text.Length;
                        Filter();
                    }
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down) { list.Step(1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { list.Step(-1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.PageDown) { list.Step(6); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.PageUp) { list.Step(-6); e.SuppressKeyPress = true; }
            };
            box.Leave += delegate { ph.Visible = (box.Text.Length == 0); };
            edge.Controls.Add(box);

            ph = new Label();
            ph.Text = en ? "Function name / pinyin / try: shutdown 30" : "功能名 · 拼音首字母 · 试试「关机 30」";
            ph.ForeColor = UiKit.A(118, 132, 152);
            ph.BackColor = UiKit.A(30, 36, 45);
            ph.Font = new Font("Microsoft YaHei UI", 10.5F);
            ph.AutoSize = true;
            ph.Location = new Point(56, 25);
            ph.Cursor = Cursors.IBeam;
            ph.Click += delegate { box.Focus(); };
            edge.Controls.Add(ph);
            ph.BringToFront();

            list = new PalList();
            list.Font = new Font("Microsoft YaHei UI", 10F);
            list.Location = new Point(16, 60);
            list.Size = new Size(592, 360);
            list.RunRequested = delegate { RunSel(); };
            edge.Controls.Add(list);

            tipDefault = en ? "> actions  / pages  ? help  ·  qdql  ·  Up/Down  Tab  Enter"
                           : "「>」动作「/」页面「?」语法 · 拼音 qdql · ↑↓ · Tab 补全 · Enter 执行";
            tip = new Label();
            tip.Text = tipDefault;
            tip.ForeColor = UiKit.A(150, 162, 182);
            tip.Font = new Font("Microsoft YaHei UI", 8F);
            tip.Location = new Point(18, 433);
            tip.AutoSize = true;
            edge.Controls.Add(tip);

            cnt = new Label();
            cnt.Text = "";
            cnt.ForeColor = UiKit.A(150, 162, 182);
            cnt.Font = new Font("Microsoft YaHei UI", 8.5F);
            cnt.TextAlign = ContentAlignment.MiddleRight;
            cnt.AutoSize = false;
            cnt.Location = new Point(470, 431);
            cnt.Size = new Size(134, 18);
            edge.Controls.Add(cnt);

            Shown += delegate
            {
                Filter();
                box.Focus();
                Sfx.Click();
            };
        }

        // v12.0：子序列模糊匹配打分。字符按顺序出现即算命中，按「跳跃量」和「末尾偏移」扣分；返回 -1 表示不命中。
        static int FuzzySeq(string hay, string q)
        {
            if (q.Length == 0) return 0;
            int i = 0, gaps = 0, last = -1;
            for (int k = 0; k < q.Length; k++)
            {
                int p = hay.IndexOf(q[k], i);
                if (p < 0) return -1;
                if (last >= 0) gaps += (p - last - 1);
                last = p;
                i = p + 1;
            }
            int sc = 34 - Math.Min(30, gaps) - Math.Min(12, last - q.Length);
            return sc < 1 ? 1 : sc;
        }

        // ---------- 过滤：意图条目置顶 → 最近用过 → 常规匹配（多词“与”匹配） ----------
        void Filter()
        {
            string raw = box.Text.Trim();
            string mode = "";
            if (raw.Length > 0 && (raw[0] == '>' || raw[0] == '/'))
            {
                mode = raw[0] == '>' ? "act" : "page";
                raw = raw.Substring(1).Trim();
            }
            if (raw == "?")
            {
                // v13.0：语法帮助直接用同一个列表渲染，按 Esc / 回车都不执行任何动作
                string[] hz = { "「>」只在动作里找（如 >关机）", "「/」只在页面跳转里找（如 /视频）",
                                "空格分词＝都要匹配（如 截图 延时）", "中文 / 英文 / 拼音缩写都能搜（qdql = 清盘清理）",
                                "↑↓ 逐条 · PgUp/PgDn 翻六条 · Tab 补全 · Enter 执行", "Esc 或点空白处关闭面板" };
                for (int h = 0; h < hz.Length; h++)
                {
                    PaletteItem hp = new PaletteItem();
                    hp.Zh = hz[h]; hp.En = hz[h]; hp.Hay = ""; hp.Kind = "help"; hp.Run = null;
                    view.Add(hp);
                }
                list.SetItems(view, "");
                cnt.Text = view.Count + (en ? " tips" : " 条提示");
                cnt.ForeColor = UiKit.A(150, 162, 182);
                tip.Text = tipDefault;
                return;
            }
            string q = raw.ToLowerInvariant();
            string[] parts = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            view.Clear();

            PaletteItem[] intents = null;
            if (raw.Length > 0 && mode.Length == 0 && BuildIntent != null)
            {
                try { intents = BuildIntent(raw); } catch (Exception dg222_1) { Diag.Swallow("PaletteForm.cs:222", dg222_1); }
            }
            if (intents != null)
            {
                for (int i = 0; i < intents.Length; i++)
                {
                    if (intents[i] == null) continue;
                    intents[i].Intent = true;
                    view.Add(intents[i]);
                }
            }

            List<string> recent = null;
            if (q.Length == 0 && RecentProvider != null)
            {
                try { recent = RecentProvider(); } catch (Exception dg237_1) { Diag.Swallow("PaletteForm.cs:237", dg237_1); }
            }
            if (recent != null)
            {
                for (int r = 0; r < recent.Count; r++)
                {
                    for (int k = 0; k < all.Count; k++)
                    {
                        PaletteItem it = all[k];
                        if (view.Contains(it)) continue;
                        string nm = en ? it.En : it.Zh;
                        if (nm == recent[r])
                        {
                            it.Recent = true;
                            view.Add(it);
                        }
                    }
                }
            }

            // v12.0：市面级搜索——子串命中按位置给分，子串不中时退化为子序列模糊匹配（qdql / shp 这类缩写与漏字也能命中），
            // 再按相关度排序；同分保持原顺序，避免结果跳动。
            List<PaletteItem> hit = new List<PaletteItem>();
            List<int> score = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                PaletteItem it = all[i];
                if (view.Contains(it)) continue;
                if (mode.Length > 0 && (mode == "page" ? it.Kind != "page" : it.Kind == "page")) continue;
                bool ok = true;
                int sc = 0;
                if (q.Length > 0)
                {
                    string nm = ((en ? it.En : it.Zh) ?? "").ToLowerInvariant();
                    for (int k = 0; k < parts.Length; k++)
                    {
                        string hay = (it.Hay ?? "").ToLowerInvariant();
                        int pos = hay.IndexOf(parts[k], StringComparison.Ordinal);
                        if (pos >= 0) sc += 60 - Math.Min(48, pos);
                        else
                        {
                            int fz = FuzzySeq(hay, parts[k]);
                            if (fz < 0) { ok = false; break; }
                            sc += fz;
                        }
                    }
                    if (ok)
                    {
                        if (parts.Length > 0 && nm.StartsWith(parts[0])) sc += 30;
                        else if (parts.Length > 0 && nm.IndexOf(parts[0], StringComparison.Ordinal) >= 0) sc += 10;
                    }
                }
                if (ok) { hit.Add(it); score.Add(sc); }
            }
            if (q.Length > 0 && hit.Count > 1)
            {
                List<KeyValuePair<PaletteItem, int>> kv = new List<KeyValuePair<PaletteItem, int>>();
                for (int i = 0; i < hit.Count; i++) kv.Add(new KeyValuePair<PaletteItem, int>(hit[i], score[i]));
                kv.Sort(delegate(KeyValuePair<PaletteItem, int> a, KeyValuePair<PaletteItem, int> b)
                {
                    return a.Value == b.Value ? 0 : b.Value.CompareTo(a.Value);
                });
                for (int i = 0; i < kv.Count; i++) view.Add(kv[i].Key);
            }
            else
            {
                for (int i = 0; i < hit.Count; i++) view.Add(hit[i]);
            }

            list.SetItems(view, box.Text.Trim());
            int n = view.Count;
            if (q.Length == 0) cnt.Text = n + (en ? " items" : " 项");
            else cnt.Text = n + (en ? " matches" : " 项匹配");
            cnt.ForeColor = n == 0 ? UiKit.A(240, 180, 70) : UiKit.A(150, 162, 182);
            tip.Text = tipDefault;
        }

        void RunSel()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= view.Count)
            {
                if (box.Text.Trim().Length > 0)
                {
                    Sfx.Warn();
                    cnt.ForeColor = UiKit.A(240, 180, 70);
                    cnt.Text = en ? "nothing to run" : "没有可执行项";
                }
                return;
            }
            PaletteItem it = view[list.SelectedIndex];
            Close();
            Sfx.Ok();
            if (AfterRun != null) { try { AfterRun(it); } catch (Exception dg329_1) { Diag.Swallow("PaletteForm.cs:329", dg329_1); } }
            if (it.Run != null) { try { it.Run(); } catch (Exception dg330_1) { Diag.Swallow("PaletteForm.cs:330", dg330_1); } }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Sfx.Click(); Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- 自测辅助 ----------
        public string DumpGeometry()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("client=" + ClientSize.Width + "x" + ClientSize.Height);
            sb.Append("  box=" + box.Left + "," + box.Top + " " + box.Width + "x" + box.Height);
            sb.Append("  ph=" + ph.Left + "," + ph.Top + (ph.Visible ? " shown" : " hidden"));
            sb.Append("  list=" + list.Left + "," + list.Top + " " + list.Width + "x" + list.Height + " rows=" + list.VisibleCount);
            sb.Append("  tip=" + tip.Left + "," + tip.Top + "  cnt=" + cnt.Left + "," + cnt.Top + " " + cnt.Width + "x" + cnt.Height);
            sb.Append("  glyph=" + glyph.Left + "," + glyph.Top);
            return sb.ToString();
        }

        public int ItemCount { get { return list.items.Count; } }
        public int SelIndex { get { return list.SelectedIndex; } }
        public string CountText { get { return cnt.Text; } }

        public void SetQueryForTest(string q)
        {
            box.Text = q;
            Filter();
            list.Refresh();
        }

        public void MoveSelForTest(int d)
        {
            list.Step(d);
        }
    }
}