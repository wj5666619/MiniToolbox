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
    class VideoProForm : Form
    {
        bool en;
        string dir;
        Label st;
        ListBox lbox;

        public VideoProForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "视频进阶 · 小工具箱", "Video Pro · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(880, 600);
            MinimumSize = new Size(880, 600);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "视频进阶", "Video Pro"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "压缩：目标体积两遍精算（更贴目标）＋可选限高，支持批量；另有拼接、抽内嵌字幕 / 音轨。全部走本地 ffmpeg，不联网。",
                "Compress: two-pass target size (+ optional max height), batch supported; join clips, pull subtitles/audio. Local ffmpeg only."), 20, 48, 840, 40));

            Label ffp = V71Ui.Note("", 20, 88, 840, 22);
            Controls.Add(ffp);
            ffp.Text = VideoProCore.Ready(dir)
                ? V71Ui.TT(en, "ffmpeg 就绪：", "ffmpeg ready: ") + VideoProCore.Ffmpeg(dir)
                : V71Ui.TT(en, "未找到 ffmpeg：先在「视频下载」页用带FFmpeg的高清格式下一次，程序会自动部署。",
                    "ffmpeg missing: download one HD format marked FFmpeg to deploy it.");

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "已选视频（可多选，按住 Ctrl）", "Selected videos (Ctrl = multi)"), 20, 118, 500, 22, 9F, V71Ui.SUB));
            lbox = new ListBox();
            lbox.BackColor = V71Ui.INPUT;
            lbox.ForeColor = V71Ui.TEXT;
            lbox.BorderStyle = BorderStyle.FixedSingle;
            lbox.Font = new Font("Microsoft YaHei UI", 9F);
            lbox.Location = new Point(20, 144);
            lbox.Size = new Size(500, 300);
            lbox.SelectionMode = SelectionMode.MultiExtended;
            Controls.Add(lbox);

            Button bAdd = V71Ui.Btn(V71Ui.TT(en, "添加视频", "Add video"), UiKit.A(96, 165, 250), 536, 144, 130, 32);
            Button bClear = V71Ui.Btn(V71Ui.TT(en, "清空列表", "Clear"), UiKit.A(52, 58, 70), 674, 144, 110, 32);
            Controls.Add(bAdd); Controls.Add(bClear);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "压缩到（MB） / 限高", "Target MB / max height"), 536, 190, 300, 22, 9F, V71Ui.SUB));
            ComboBox cbMb = V71Ui.Combo(new string[] { "5", "8", "10", "15", "20", "30", "50", "100", "质量优先" }, 536, 214, 96);
            cbMb.SelectedIndex = 3;
            Controls.Add(cbMb);
            ComboBox cbH = V71Ui.Combo(new string[] { "原始", "1080p", "720p", "480p" }, 640, 214, 96);
            cbH.SelectedIndex = 0;
            Controls.Add(cbH);
            Button bCmp = V71Ui.Btn(V71Ui.TT(en, "压缩选中（批量）", "Compress selected"), UiKit.A(249, 115, 22), 536, 252, 248, 32);
            Controls.Add(bCmp);

            Button bJoin = V71Ui.Btn(V71Ui.TT(en, "拼接列表全部", "Join all"), UiKit.A(34, 197, 94), 536, 296, 248, 32);
            CheckBox cbRe = new RoundCheck();
            cbRe.Text = V71Ui.TT(en, "参数不一致时重编码（慢但稳）", "Re-encode when clips differ");
            cbRe.ForeColor = V71Ui.SUB;
            cbRe.BackColor = Color.Transparent;
            cbRe.Location = new Point(536, 334);
            cbRe.Size = new Size(260, 22);
            Controls.Add(cbRe);
            Controls.Add(bJoin);

            Button bSub = V71Ui.Btn(V71Ui.TT(en, "抽字幕(.srt)", "Extract .srt"), UiKit.A(14, 165, 233), 536, 366, 120, 32);
            Button bAud = V71Ui.Btn(V71Ui.TT(en, "抽音轨(.m4a)", "Extract audio"), UiKit.A(139, 92, 246), 664, 366, 120, 32);
            Controls.Add(bSub); Controls.Add(bAud);

            st = V71Ui.Note("", 20, 456, 840, 44);
            Controls.Add(st);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 760, 548, 100, 32);
            Controls.Add(bClose);
            bClose.Click += delegate { Close(); };

            bAdd.Click += delegate
            {
                OpenFileDialog od = new OpenFileDialog();
                od.Filter = "视频|*.mp4;*.mkv;*.avi;*.mov;*.flv;*.ts;*.wmv;*.webm|所有文件|*.*";
                od.Multiselect = true;
                if (od.ShowDialog(this) != DialogResult.OK) return;
                foreach (string p in od.FileNames)
                {
                    lbox.Items.Add(p);
                }
                st.Text = V71Ui.TT(en, "已添加 ", "added ") + od.FileNames.Length;
            };

            bClear.Click += delegate { lbox.Items.Clear(); };

            bCmp.Click += delegate
            {
                List<string> todo = new List<string>();
                if (lbox.SelectedItems.Count > 0) { foreach (object o in lbox.SelectedItems) todo.Add((string)o); }
                else if (lbox.Items.Count > 0) todo.Add((string)lbox.Items[0]);
                if (todo.Count == 0) { st.Text = V71Ui.TT(en, "先添加视频", "Add a video first"); return; }
                int mb = 0;
                string ms = cbMb.SelectedItem == null ? "15" : cbMb.SelectedItem.ToString();
                if (ms.IndexOf("质量") < 0) int.TryParse(ms, out mb);
                int maxH = 0;
                if (cbH.SelectedIndex == 1) maxH = 1080;
                else if (cbH.SelectedIndex == 2) maxH = 720;
                else if (cbH.SelectedIndex == 3) maxH = 480;
                bCmp.Enabled = false;
                st.Text = V71Ui.TT(en, "正在压缩……", "Compressing…");
                Action<Action> ui = delegate(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (Exception dg130_1) { Diag.Swallow("VideoProForm.cs:130", dg130_1); } };
                ThreadPool.QueueUserWorkItem(delegate
                {
                    int okN = 0, bad = 0;
                    long saved = 0, srcAll = 0;
                    for (int i = 0; i < todo.Count; i++)
                    {
                        int idx = i;
                        string src = todo[i];
                        string o = "", err = "";
                        long before = 0;
                        try { before = new FileInfo(src).Length; } catch (Exception dg141_1) { Diag.Swallow("VideoProForm.cs:141", dg141_1); }
                        srcAll += before;
                        string nm = Path.GetFileName(src);
                        ui(delegate { st.Text = V71Ui.TT(en, "压缩 ", "compress ") + (idx + 1) + "/" + todo.Count + " · " + nm; });
                        bool ok = VideoCmp.Compress(dir, src, mb, maxH, out o, out err, delegate(int pct, string txt)
                        {
                            ui(delegate
                            {
                                if (pct >= 0) st.Text = V71Ui.TT(en, "压缩 ", "compress ") + (idx + 1) + "/" + todo.Count + " · " + nm + " " + pct + "%";
                                else if (txt.Length > 0) st.Text = V71Ui.TT(en, "压缩 ", "compress ") + (idx + 1) + "/" + todo.Count + " · " + txt;
                            });
                        });
                        if (!ok)
                        {
                            bad++;
                            ui(delegate { st.Text = V71Ui.TT(en, "失败：", "failed: ") + nm + " — " + err; });
                            continue;
                        }
                        okN++;
                        long after = 0;
                        try { after = new FileInfo(o).Length; } catch (Exception dg161_1) { Diag.Swallow("VideoProForm.cs:161", dg161_1); }
                        saved += before - after;
                        string onm = Path.GetFileName(o);
                        string ab = (after / 1048576.0).ToString("0.00");
                        ui(delegate { st.Text = V71Ui.TT(en, "完成：", "done: ") + onm + "  " + ab + " MB"; });
                    }
                    ui(delegate
                    {
                        bCmp.Enabled = true;
                        string sum = V71Ui.TT(en, "压缩结束：成功 ", "done: ") + okN + " / " + todo.Count
                            + V71Ui.TT(en, "，共省 ", ", saved ") + (saved / 1048576.0).ToString("0.00") + " MB"
                            + (bad > 0 ? V71Ui.TT(en, "，失败 ", ", failed ") + bad : "")
                            + V71Ui.TT(en, "。输出在源文件旁 _压缩 目录。", ". Output in _压缩 next to source.");
                        st.Text = sum;
                        if (okN > 0 && MessageBox.Show(this, V71Ui.TT(en, "打开输出目录？", "Open output folder?"), "小工具箱",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            string odir = VideoCmp.OutDir(todo[0]);
                            try { Process.Start("explorer.exe", (char)34 + odir + (char)34); } catch (Exception dg179_1) { Diag.Swallow("VideoProForm.cs:179", dg179_1); }
                        }
                    });
                });
            };

            bJoin.Click += delegate
            {
                if (lbox.Items.Count < 2) { st.Text = V71Ui.TT(en, "至少加两段", "Need 2+ clips"); return; }
                List<string> files = new List<string>();
                foreach (object o in lbox.Items) files.Add((string)o);
                Cursor.Current = Cursors.WaitCursor;
                st.Text = V71Ui.TT(en, "正在拼接……", "Joining…");
                Application.DoEvents();
                string outFile = "", err = "";
                bool ok = VideoProCore.Concat(dir, files, cbRe.Checked, out outFile, out err, delegate(string line) { st.Text = line; Application.DoEvents(); });
                Cursor.Current = Cursors.Default;
                st.Text = ok ? (V71Ui.TT(en, "完成：", "done: ") + Path.GetFileName(outFile)) : err;
                if (ok && MessageBox.Show(this, V71Ui.TT(en, "打开所在文件夹？", "Open the folder?"), "小工具箱",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try { Process.Start("explorer.exe", "/select,\"" + outFile + "\""); } catch (Exception dg200_1) { Diag.Swallow("VideoProForm.cs:200", dg200_1); }
                }
            };

            bSub.Click += delegate
            {
                if (lbox.SelectedItems.Count == 0) { st.Text = V71Ui.TT(en, "先选中一条视频", "Select one video"); return; }
                string src = (string)lbox.SelectedItems[0];
                string outFile = "", err = "";
                bool ok = VideoProCore.ExtractSub(dir, src, out outFile, out err);
                st.Text = ok ? (V71Ui.TT(en, "字幕已导出：", "saved: ") + Path.GetFileName(outFile)) : err;
            };

            bAud.Click += delegate
            {
                if (lbox.SelectedItems.Count == 0) { st.Text = V71Ui.TT(en, "先选中一条视频", "Select one video"); return; }
                string src = (string)lbox.SelectedItems[0];
                string outFile = "", err = "";
                bool ok = VideoProCore.ExtractAudio(dir, src, out outFile, out err);
                st.Text = ok ? (V71Ui.TT(en, "音轨已导出：", "saved: ") + Path.GetFileName(outFile)) : err;
            };
        }
    }
}