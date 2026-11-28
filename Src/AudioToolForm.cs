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
    class AudioToolForm : Form
    {
        bool en;
        string dir;
        Label st;
        ListBox lbox;
        ComboBox cbFmt;
        ComboBox cbBr;
        ComboBox cbLufs;   // v12.0：归一目标响度

        public AudioToolForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "音频工具 · 小工具箱", "Audio Tools · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(900, 640);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "音频工具", "Audio tools"), 20, 14));
            Controls.Add(V71Ui.Note(V71Ui.TT(en,
                "批量转码、响度归一（LUFS 可选 -14 / -16 / -20）、简易降噪、去静音、多段合并。结果存成 _转码 / _归一 / _降噪 / _去静音 副本，原文件不动。",
                "Batch convert, loudness normalize (LUFS -14/-16/-20), denoise, silence trim, merge. Outputs are copies; originals untouched."), 20, 48, 860, 24));

            Label lff = V71Ui.Note("", 20, 88, 860, 22);
            string ff = VideoProCore.Ffmpeg(dir);
            lff.Text = ff.Length > 0 ? V71Ui.TT(en, "ffmpeg 就绪：", "ffmpeg ready: ") + ff
                : V71Ui.TT(en, "未找到 ffmpeg（视频下载页下载一次带 FFmpeg 的高清格式即可部署）", "ffmpeg missing.");
            lff.ForeColor = ff.Length > 0 ? V71Ui.SUB : V71Ui.WARN;
            Controls.Add(lff);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "音频文件（Ctrl 可多选）", "Audio files (Ctrl = multi)"), 20, 120, 520, 22, 9F, V71Ui.SUB));
            lbox = new ListBox();
            lbox.BackColor = V71Ui.INPUT;
            lbox.ForeColor = V71Ui.TEXT;
            lbox.BorderStyle = BorderStyle.FixedSingle;
            lbox.Font = new Font("Microsoft YaHei UI", 9F);
            lbox.Location = new Point(20, 144);
            lbox.Size = new Size(520, 380);
            lbox.SelectionMode = SelectionMode.MultiExtended;
            Controls.Add(lbox);

            Button bAdd = V71Ui.Btn(V71Ui.TT(en, "添加音频", "Add"), UiKit.A(96, 165, 250), 556, 144, 130, 32);
            Button bClear = V71Ui.Btn(V71Ui.TT(en, "清空", "Clear"), UiKit.A(52, 58, 70), 694, 144, 110, 32);
            Controls.Add(bAdd); Controls.Add(bClear);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "目标格式", "Format"), 556, 192, 120, 22, 9F, V71Ui.SUB));
            cbFmt = V71Ui.Combo(new string[] { "mp3", "m4a", "wav", "flac" }, 556, 216, 100);
            Controls.Add(cbFmt);
            Controls.Add(V71Ui.Text(V71Ui.TT(en, "码率", "Bitrate"), 668, 192, 120, 22, 9F, V71Ui.SUB));
            cbBr = V71Ui.Combo(new string[] { "128", "192", "256", "320" }, 668, 216, 100);
            cbBr.SelectedIndex = 1;
            Controls.Add(cbBr);

            Button bConv = V71Ui.Btn(V71Ui.TT(en, "批量转码", "Convert"), UiKit.A(34, 197, 94), 556, 258, 212, 34);
            Button bNorm = V71Ui.Btn(V71Ui.TT(en, "音量归一 (-16 LUFS)", "Normalize"), UiKit.A(14, 165, 233), 556, 300, 212, 34);
            Button bDen = V71Ui.Btn(V71Ui.TT(en, "简易降噪", "Denoise"), UiKit.A(250, 190, 90), 556, 342, 212, 34);
            Button bMrg = V71Ui.Btn(V71Ui.TT(en, "合并所选（按顺序）", "Merge selected"), UiKit.A(139, 92, 246), 556, 384, 212, 34);
            Button bTrim = V71Ui.Btn(V71Ui.TT(en, "去静音（掐头去尾 + 压中段空白）", "Trim silence"), UiKit.A(14, 116, 144), 556, 426, 212, 34);
            Controls.Add(bConv); Controls.Add(bNorm); Controls.Add(bDen); Controls.Add(bMrg); Controls.Add(bTrim);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "归一目标响度", "Target loudness"), 556, 472, 200, 22, 8.5F, V71Ui.SUB));
            cbLufs = V71Ui.Combo(new string[] { "-14 LUFS · 音乐", "-16 LUFS · 通用", "-20 LUFS · 播客语音" }, 556, 496, 212);
            cbLufs.SelectedIndex = 1;
            Controls.Add(cbLufs);

            st = V71Ui.Note("", 20, 530, 860, 42);
            Controls.Add(st);

            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 780, 588, 100, 32);
            Controls.Add(bClose);
            bClose.Click += delegate { Close(); };

            bAdd.Click += delegate
            {
                OpenFileDialog od = new OpenFileDialog();
                od.Filter = "音频|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.opus|所有文件|*.*";
                od.Multiselect = true;
                if (od.ShowDialog(this) != DialogResult.OK) return;
                foreach (string p in od.FileNames) lbox.Items.Add(p);
            };
            bClear.Click += delegate { lbox.Items.Clear(); };

            bConv.Click += delegate
            {
                string ext = cbFmt.SelectedItem.ToString();
                int br = 192;
                try { br = int.Parse(cbBr.SelectedItem.ToString()); } catch (Exception dg113_1) { Diag.Swallow("AudioToolForm.cs:113", dg113_1); }
                string err = "", dst = "";
                int ok = 0, bad = 0;
                for (int i = 0; i < lbox.Items.Count; i++)
                {
                    string p = lbox.Items[i].ToString();
                    st.Text = V71Ui.TT(en, "转码中 ", "Converting ") + Path.GetFileName(p);
                    Application.DoEvents();
                    if (AudioCore.Convert(dir, p, ext, br, out dst, out err)) ok++; else bad++;
                }
                st.Text = V71Ui.TT(en, "完成：成功 ", "Done: ok ") + ok + V71Ui.TT(en, "，失败 ", ", failed ") + bad;
            };

            bNorm.Click += delegate
            {
                string err = "", dst = "";
                int ok = 0, bad = 0;
                foreach (object o in lbox.Items)
                {
                    string p = o.ToString();
                    st.Text = V71Ui.TT(en, "归一化 ", "Normalizing ") + Path.GetFileName(p);
                    Application.DoEvents();
                    int lufs = -16;
                    if (cbLufs != null && cbLufs.SelectedIndex == 0) lufs = -14;
                    else if (cbLufs != null && cbLufs.SelectedIndex == 2) lufs = -20;
                    if (AudioCore.Normalize(dir, p, lufs, out dst, out err)) ok++; else bad++;
                }
                st.Text = V71Ui.TT(en, "完成：成功 ", "Done: ok ") + ok + V71Ui.TT(en, "，失败 ", ", failed ") + bad;
            };

            bDen.Click += delegate
            {
                string err = "", dst = "";
                int ok = 0, bad = 0;
                foreach (object o in lbox.Items)
                {
                    string p = o.ToString();
                    st.Text = V71Ui.TT(en, "降噪中 ", "Denoising ") + Path.GetFileName(p);
                    Application.DoEvents();
                    if (AudioCore.Denoise(dir, p, out dst, out err)) ok++; else bad++;
                }
                st.Text = V71Ui.TT(en, "完成：成功 ", "Done: ok ") + ok + V71Ui.TT(en, "，失败 ", ", failed ") + bad;
            };

            bTrim.Click += delegate
            {
                string err = "", dst = "";
                int ok = 0, bad = 0;
                foreach (object o in lbox.Items)
                {
                    string p = o.ToString();
                    st.Text = V71Ui.TT(en, "去静音 ", "Trimming ") + Path.GetFileName(p);
                    Application.DoEvents();
                    if (AudioCore.TrimSilence(dir, p, out dst, out err)) ok++; else bad++;
                }
                st.Text = V71Ui.TT(en, "完成：成功 ", "Done: ok ") + ok + V71Ui.TT(en, "，失败 ", ", failed ") + bad;
            };

            bMrg.Click += delegate
            {
                List<string> sel = new List<string>();
                if (lbox.SelectedItems.Count > 0)
                {
                    foreach (object o in lbox.SelectedItems) sel.Add(o.ToString());
                }
                else
                {
                    foreach (object o in lbox.Items) sel.Add(o.ToString());
                }
                if (sel.Count < 2) { V71Ui.Say(this, V71Ui.TT(en, "至少选两段音频。", "Pick at least two files.")); return; }
                st.Text = V71Ui.TT(en, "合并中…", "Merging…");
                Application.DoEvents();
                string dst = "", err = "";
                if (AudioCore.Merge(dir, sel, out dst, out err)) st.Text = V71Ui.TT(en, "已合并：", "Merged: ") + dst;
                else st.Text = V71Ui.TT(en, "合并失败：", "Merge failed: ") + err;
            };
        }
    }
}