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
    class WishForm : Form
    {
        bool en;
        string dir;
        TextBox tbSid;
        ListView lv;
        Label st;
        List<SteamWishCore.Item> items = new List<SteamWishCore.Item>();
        bool busy = false;

        public WishForm(bool eng, string baseDir)
        {
            en = eng; dir = baseDir;
            Text = en ? "Wishlist price watch · Mini Toolbox" : "愿望单降价监控 · 小工具箱";
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(920, 620);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "愿望单降价监控", "Wishlist price watch"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "读公开愿望单 → 逐款查当前价 → 和第一次见到的价格比，降了多少一眼看到；第一次存下来的价就是基准",
                "Read a public wishlist, price every title, and compare with the first price ever seen (kept in steam_wishlist_price.txt)"), 20, 50, 960, 22));

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "SteamID", "SteamID"), 20, 86, 70, 20, 9F, V71Ui.SUB));
            tbSid = V71Ui.Box("", 92, 82, 300, 28);
            Controls.Add(tbSid);
            string auto = SteamWishCore.SteamId64();
            if (auto.Length > 0) tbSid.Text = auto;

            Button bAuto = V71Ui.Btn(V71Ui.TT(en, "自动识别", "Detect"), UiKit.A(59, 130, 246), 400, 80, 100, 30);
            bAuto.Click += delegate
            {
                string sid = SteamWishCore.SteamId64();
                if (sid.Length == 0) { st.Text = V71Ui.TT(en, "没在本机 Steam 里找到账号，请手动填 SteamID64 或自定义 ID", "No account found in local Steam; paste your SteamID64"); st.ForeColor = V71Ui.WARN; return; }
                tbSid.Text = sid;
                st.Text = V71Ui.TT(en, "已识别：" + sid, "detected: " + sid); st.ForeColor = V71Ui.GOOD;
            };
            Controls.Add(bAuto);
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "隐私设置里「愿望单」必须是公开，否则商店只回空", "Wishlist must be public in Steam privacy settings"), 512, 86, 440, 20));

            lv = V71Ui.List(new string[] { V71Ui.TT(en, "游戏", "Game"), "AppID", V71Ui.TT(en, "首见价", "First"), V71Ui.TT(en, "现价", "Now"), V71Ui.TT(en, "便宜了", "Drop"), V71Ui.TT(en, "折扣", "Off") },
                new int[] { 400, 80, 120, 130, 130, 70 }, 20, 126, 960, 400);
            Controls.Add(lv);

            st = V71Ui.Text(V71Ui.TT(en, "先点「读愿望单」", "Start with Load wishlist"), 20, 538, 960, 20, 9F, V71Ui.SUB);
            Controls.Add(st);

            Button bLoad = V71Ui.Btn(V71Ui.TT(en, "读愿望单", "Load wishlist"), UiKit.A(34, 197, 94), 20, 572, 120, 34);
            bLoad.Click += delegate { LoadWish(); };
            Controls.Add(bLoad);

            Button bPrice = V71Ui.Btn(V71Ui.TT(en, "查当前价格", "Fetch prices"), UiKit.A(250, 190, 90), 150, 572, 130, 34);
            bPrice.Click += delegate { FetchPrices(); };
            Controls.Add(bPrice);

            Button bCsv = V71Ui.Btn(V71Ui.TT(en, "导出降价 CSV", "Export drops"), UiKit.A(139, 92, 246), 290, 572, 130, 34);
            bCsv.Click += delegate
            {
                if (items.Count == 0) { V71Ui.Say(this, V71Ui.TT(en, "先读愿望单", "Load the wishlist first")); return; }
                string r = SteamWishCore.ExportCsv(items, dir, true);
                st.Text = r; st.ForeColor = V71Ui.GOOD;
            };
            Controls.Add(bCsv);

            Button bStop = V71Ui.Btn(V71Ui.TT(en, "停止", "Stop"), UiKit.A(120, 130, 145), 430, 572, 90, 34);
            bStop.Click += delegate { busy = false; };
            Controls.Add(bStop);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(43, 58, 80), 880, 572, 100, 34);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);
        }

        void Cart(string c)
        {
            st.Text = c;
            Application.DoEvents();
        }

        void LoadWish()
        {
            if (busy) return;
            busy = true;
            Cart(V71Ui.TT(en, "读愿望单中…", "Loading wishlist…"));
            string err = "";
            List<SteamWishCore.Item> res = SteamWishCore.Load(tbSid.Text, out err);
            SteamWishCore.ApplyCache(res, dir);
            items = res;
            Fill();
            busy = false;
            if (res.Count == 0) { st.Text = err; st.ForeColor = V71Ui.BAD; return; }
            st.Text = V71Ui.TT(en, "读到 " + res.Count + " 款愿望单；点「查当前价格」开始比价", res.Count + " wishlisted titles; fetch prices next");
            st.ForeColor = V71Ui.GOOD;
        }

        void Fill()
        {
            lv.Items.Clear();
            foreach (SteamWishCore.Item it in items)
            {
                ListViewItem row = new ListViewItem(it.Name);
                row.SubItems.Add(it.AppId.ToString());
                row.SubItems.Add(it.FirstV >= 0 ? it.First : "-");
                row.SubItems.Add(it.NowV >= 0 ? it.Now : "-");
                double drop = (it.FirstV >= 0 && it.NowV >= 0) ? it.FirstV - it.NowV : 0;
                row.SubItems.Add(drop > 0 ? ("省 ¥" + drop.ToString("0.00")) : (it.NowV >= 0 ? "持平" : "-"));
                row.SubItems.Add(it.Discount > 0 ? ("-" + it.Discount + "%") : "-");
                if (drop > 0) row.ForeColor = V71Ui.GOOD;
                else if (it.Now.IndexOf("免费") >= 0) row.ForeColor = V71Ui.WARN;
                row.Tag = it;
                lv.Items.Add(row);
            }
        }

        void FetchPrices()
        {
            if (busy) return;
            if (items.Count == 0) { V71Ui.Say(this, V71Ui.TT(en, "先读愿望单", "Load the wishlist first")); return; }
            busy = true;
            string err = "";
            int n = SteamWishCore.Prices(items, 10, delegate (int done, int total)
            {
                st.Text = V71Ui.TT(en, "查价 " + done + "/" + total + "…", "pricing " + done + "/" + total + "…");
                st.ForeColor = V71Ui.SUB;
                Application.DoEvents();
            }, delegate () { return !busy; }, out err);
            SteamWishCore.SaveCache(items, dir);
            Fill();
            busy = false;
            int drops = 0;
            double sumDrop = 0;
            foreach (SteamWishCore.Item it in items)
                if (it.FirstV >= 0 && it.NowV >= 0 && it.FirstV > it.NowV) { drops++; sumDrop += it.FirstV - it.NowV; }
            st.Text = (err.Length > 0 ? err + " · " : "") + V71Ui.TT(en,
                "读到价格 " + n + "/" + items.Count + " 款；降价 " + drops + " 款，合计能省 ¥" + sumDrop.ToString("0.00"),
                "priced " + n + "/" + items.Count + "; " + drops + " cheaper, save ¥" + sumDrop.ToString("0.00"));
            st.ForeColor = drops > 0 ? V71Ui.GOOD : V71Ui.SUB;
        }

        public static string CheckOverlaps(Form f)
        {
            StringBuilder sb = new StringBuilder();
            Control[] all = new Control[f.Controls.Count];
            f.Controls.CopyTo(all, 0);
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                {
                    Rectangle a = all[i].Bounds, b = all[j].Bounds;
                    Rectangle r = Rectangle.Intersect(a, b);
                    if (r.Width > 2 && r.Height > 2 && r.Width * r.Height > 40)
                        sb.Append("OVERLAP [" + all[i].GetType().Name + " " + a.X + "," + a.Y + " " + a.Width + "x" + a.Height
                            + "] vs [" + all[j].GetType().Name + " " + b.X + "," + b.Y + " " + b.Width + "x" + b.Height + "] = " + r.Width + "x" + r.Height + "\r\n");
                }
            if (sb.Length == 0) sb.Append("no-overlap\r\n");
            return sb.ToString();
        }

        public string DumpGeometry()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("WishForm " + ClientSize.Width + "x" + ClientSize.Height + "\r\n");
            foreach (Control c in Controls)
            {
                string nm = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
                if (nm.Length > 26) nm = nm.Substring(0, 26);
                sb.Append("  " + c.GetType().Name + " " + c.Left + "," + c.Top + " " + c.Width + "x" + c.Height + " \"" + nm + "\"\r\n");
            }
            return sb.ToString();
        }
    }
}
