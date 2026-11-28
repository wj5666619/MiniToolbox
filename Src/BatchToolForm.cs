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
    class BatchToolForm : Form
    {
        bool en;
        List<string> srcFiles = new List<string>();
        List<string> built = new List<string>();
        ListView lvSrc;
        ListView lvPrev;
        ListView lvConv;
        TextBox tbPrefix;
        TextBox tbSuffix;
        TextBox tbFind;
        TextBox tbRep;
        NumSpin nudStart;
        ComboBox cbMode;
        ComboBox cbTarget;
        ComboBox cbQuality;
        TextBox tbLog;
        Button btnApply;
        Label lblStat;
        string lastRecord = null;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, EntryPoint = "GetShortPathName")]
        static extern int GetShortPath(string longPath, StringBuilder shortPath, int bufSize);

        public BatchToolForm(bool langEn)
        {
            en = langEn;
            Text = en ? "Batch rename / convert · Mini Toolbox" : "批量重命名 / 格式转换 · 小工具箱";
            BackColor = UiKit.A(20, 24, 30);
            ClientSize = new Size(1020, 700);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(LblBar(en ? "Batch rename · Convert" : "批量重命名  ·  格式转换", 16F, Color.White, 20, 12));
            Label sub = LblBar(en ? "Left: rename rules with preview and undo. Right: image / video / audio conversion." : "左边按规则改名（带预览和撤销），右边把图片 / 视频 / 音频批量转格式。", 8.5F, UiKit.A(150, 160, 176), 20, 42);
            sub.Size = new Size(980, 20);
            Controls.Add(sub);

            Panel left = new Panel();
            left.Location = new Point(20, 72);
            left.Size = new Size(490, 600);
            left.BackColor = UiKit.A(26, 31, 39);
            Controls.Add(left);

            left.Controls.Add(LblBar(en ? "① Files" : "① 选择文件", 10F, UiKit.A(220, 230, 242), 12, 10));
            Button bDir = MkBtn(en ? "Pick folder" : "选文件夹", 12, 34, 110, 26);
            bDir.Click += delegate { PickFolder(); };
            left.Controls.Add(bDir);
            Button bFile = MkBtn(en ? "Pick files" : "选文件", 130, 34, 100, 26);
            bFile.Click += delegate { PickFiles(); };
            left.Controls.Add(bFile);
            Button bClear = MkBtn(en ? "Clear" : "清空", 238, 34, 80, 26);
            bClear.Click += delegate { srcFiles.Clear(); lvSrc.Items.Clear(); lvPrev.Items.Clear(); Stat(); };
            left.Controls.Add(bClear);

            lvSrc = MkList(12, 68, 466, 118);
            left.Controls.Add(lvSrc);

            left.Controls.Add(LblBar(en ? "② Rule" : "② 规则", 10F, UiKit.A(220, 230, 242), 12, 192));
            cbMode = new ComboBox();
            cbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMode.Items.Add(en ? "Prefix + original" : "前缀 + 原名");
            cbMode.Items.Add(en ? "Original + suffix" : "原名 + 后缀");
            cbMode.Items.Add(en ? "Serial number" : "序号");
            cbMode.Items.Add(en ? "Date + original" : "日期 + 原名");
            cbMode.Items.Add(en ? "Regex replace" : "正则替换");
            cbMode.Items.Add(en ? "Extension only" : "只改扩展名");
            cbMode.SelectedIndex = 0;
            cbMode.Location = new Point(12, 216);
            cbMode.Size = new Size(200, 24);
            Styl(cbMode);
            left.Controls.Add(cbMode);

            nudStart = new NumSpin();
            nudStart.Minimum = 0; nudStart.Maximum = 99999; nudStart.Value = 1;
            nudStart.Location = new Point(226, 216);
            nudStart.Size = new Size(80, 24);
            Styl(nudStart);
            left.Controls.Add(nudStart);

            tbPrefix = MkTb(12, 248, 220);
            tbPrefix.Text = en ? "new_" : "新_";
            left.Controls.Add(tbPrefix);
            tbSuffix = MkTb(244, 248, 220);
            tbSuffix.Text = "_bak";
            left.Controls.Add(tbSuffix);

            tbFind = MkTb(12, 278, 220);
            tbFind.Text = "(\\s)";
            left.Controls.Add(tbFind);
            tbRep = MkTb(244, 278, 220);
            tbRep.Text = "_";
            left.Controls.Add(tbRep);

            Button bPrev = MkBtn(en ? "Preview" : "预览", 12, 312, 100, 28);
            bPrev.Click += delegate { Preview(); };
            left.Controls.Add(bPrev);
            btnApply = MkBtn(en ? "Apply" : "应用改名", 120, 312, 110, 28);
            btnApply.Enabled = false;
            btnApply.Click += delegate { Apply(); };
            left.Controls.Add(btnApply);
            Button bUndo = MkBtn(en ? "Undo last" : "撤销上次", 238, 312, 110, 28);
            bUndo.Click += delegate { Undo(); };
            left.Controls.Add(bUndo);

            lvPrev = MkList(12, 348, 466, 240);
            left.Controls.Add(lvPrev);

            Panel right = new Panel();
            right.Location = new Point(524, 72);
            right.Size = new Size(476, 600);
            right.BackColor = UiKit.A(26, 31, 39);
            Controls.Add(right);

            right.Controls.Add(LblBar(en ? "③ Convert" : "③ 批量转换", 10F, UiKit.A(220, 230, 242), 12, 10));
            Button rFile = MkBtn(en ? "Pick files" : "选文件", 12, 34, 100, 26);
            rFile.Click += delegate { PickConv(); };
            right.Controls.Add(rFile);
            Button rDir = MkBtn(en ? "Pick folder" : "选文件夹", 120, 34, 110, 26);
            rDir.Click += delegate { PickConvFolder(); };
            right.Controls.Add(rDir);

            cbTarget = new ComboBox();
            cbTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            cbTarget.Items.Add("PNG"); cbTarget.Items.Add("JPG"); cbTarget.Items.Add("BMP"); cbTarget.Items.Add("GIF");
            cbTarget.Items.Add(en ? "MP4 (video)" : "MP4（视频）");
            cbTarget.Items.Add(en ? "MP3 (audio)" : "MP3（抽音频）");
            cbTarget.Items.Add(en ? "GIF (from video)" : "GIF（视频转）");
            cbTarget.SelectedIndex = 0;
            cbTarget.Location = new Point(12, 66);
            cbTarget.Size = new Size(180, 24);
            Styl(cbTarget);
            right.Controls.Add(cbTarget);

            cbQuality = new ComboBox();
            cbQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            cbQuality.Items.Add(en ? "JPG quality 95" : "JPG 质量 95");
            cbQuality.Items.Add(en ? "JPG quality 85" : "JPG 质量 85");
            cbQuality.Items.Add(en ? "JPG quality 70" : "JPG 质量 70");
            cbQuality.SelectedIndex = 1;
            cbQuality.Location = new Point(200, 66);
            cbQuality.Size = new Size(140, 24);
            Styl(cbQuality);
            right.Controls.Add(cbQuality);

            lvConv = MkList(12, 100, 452, 250);
            right.Controls.Add(lvConv);

            Button cStart = MkBtn(en ? "Start convert" : "开始转换", 12, 360, 130, 30);
            cStart.Click += delegate { Convert(); };
            right.Controls.Add(cStart);
            Label outInfo = LblBar(en ? "Output sits next to the source file with a new extension; originals stay." : "输出就在原文件旁边、换扩展名，原文件保留。", 8.5F, UiKit.A(140, 152, 168), 154, 366);
            outInfo.Size = new Size(312, 20);
            right.Controls.Add(outInfo);

            tbLog = new TextBox();
            tbLog.Multiline = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.BackColor = UiKit.A(18, 22, 28);
            tbLog.ForeColor = UiKit.A(180, 194, 210);
            tbLog.BorderStyle = BorderStyle.None;
            tbLog.Font = new Font("Consolas", 8.5F);
            tbLog.Location = new Point(12, 400);
            tbLog.Size = new Size(452, 188);
            tbLog.ReadOnly = true;
            right.Controls.Add(tbLog);

            lblStat = LblBar("", 9F, UiKit.A(120, 170, 220), 20, 676);
            lblStat.Size = new Size(980, 20);
            Controls.Add(lblStat);

            Stat();
        }

        Label LblBar(string text, float size, Color color, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = new Font("Microsoft YaHei UI", size, size >= 12F ? FontStyle.Bold : FontStyle.Regular);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        Button MkBtn(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderColor = UiKit.A(70, 84, 104);
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = UiKit.A(40, 48, 60);
            b.ForeColor = UiKit.A(228, 236, 246);
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.Cursor = Cursors.Hand;
            return b;
        }

        TextBox MkTb(int x, int y, int w)
        {
            TextBox tb = new TextBox();
            tb.BackColor = UiKit.A(34, 40, 50);
            tb.ForeColor = UiKit.A(232, 240, 250);
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = new Font("Microsoft YaHei UI", 9F);
            tb.Location = new Point(x, y);
            tb.Size = new Size(w, 24);
            return tb;
        }

        void Styl(Control c)
        {
            c.BackColor = UiKit.A(34, 40, 50);
            c.ForeColor = UiKit.A(232, 240, 250);
            c.Font = new Font("Microsoft YaHei UI", 9F);
        }

        ListView MkList(int x, int y, int w, int h)
        {
            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.HeaderStyle = ColumnHeaderStyle.None;
            lv.BackColor = UiKit.A(18, 22, 28);
            lv.ForeColor = UiKit.A(200, 212, 228);
            lv.BorderStyle = BorderStyle.None;
            lv.Font = new Font("Microsoft YaHei UI", 8.5F);
            lv.Location = new Point(x, y);
            lv.Size = new Size(w, h);
            lv.Columns.Add("a", (int)(w * 0.40));
            lv.Columns.Add("b", (int)(w * 0.34));
            lv.Columns.Add("c", (int)(w * 0.22));
            return lv;
        }

        void Log(string s)
        {
            tbLog.AppendText(s + "\r\n");
        }

        void Stat()
        {
            lblStat.Text = (en ? "rename targets: " : "待改名：") + srcFiles.Count.ToString()
                + (en ? "   convert targets: " : "   待转换：") + lvConv.Items.Count.ToString()
                + (en ? "   (nothing is overwritten; originals stay)" : "   （不覆盖原文件）");
        }

        void PickFolder()
        {
            FolderBrowserDialog fd = new FolderBrowserDialog();
            if (fd.ShowDialog(this) != DialogResult.OK) return;
            srcFiles.Clear();
            lvSrc.Items.Clear();
            try
            {
                string[] all = Directory.GetFiles(fd.SelectedPath);
                for (int i = 0; i < all.Length; i++) srcFiles.Add(all[i]);
            }
            catch (Exception ex) { Log(ex.Message); }
            FillSrc();
        }

        void PickFiles()
        {
            OpenFileDialog of = new OpenFileDialog();
            of.Multiselect = true;
            of.Filter = en ? "All files|*.*" : "所有文件|*.*";
            if (of.ShowDialog(this) != DialogResult.OK) return;
            srcFiles.Clear();
            lvSrc.Items.Clear();
            for (int i = 0; i < of.FileNames.Length; i++) srcFiles.Add(of.FileNames[i]);
            FillSrc();
        }

        void FillSrc()
        {
            lvSrc.Items.Clear();
            for (int i = 0; i < srcFiles.Count; i++)
            {
                ListViewItem it = new ListViewItem(Path.GetFileName(srcFiles[i]));
                it.SubItems.Add(Path.GetExtension(srcFiles[i]));
                long sz = 0;
                try { sz = new FileInfo(srcFiles[i]).Length; } catch (Exception dg310_1) { Diag.Swallow("BatchToolForm.cs:310", dg310_1); }
                it.SubItems.Add(FmtSize(sz));
                lvSrc.Items.Add(it);
            }
            Stat();
        }

        string FmtSize(long b)
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("0.0") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return (b / 1024.0).ToString("0.0") + " KB";
            return b.ToString() + " B";
        }

        string TargetName(string src, int index)
        {
            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src);
            string ext = Path.GetExtension(src);
            int mode = cbMode.SelectedIndex;
            string prefix = tbPrefix.Text;
            string suffix = tbSuffix.Text;
            int start = (int)nudStart.Value;
            string outName = name;

            if (mode == 0) outName = prefix + name;
            else if (mode == 1) outName = name + suffix;
            else if (mode == 2) outName = prefix + (start + index).ToString("000");
            else if (mode == 3) outName = DateTime.Now.ToString("yyyyMMdd") + "_" + name;
            else if (mode == 4)
            {
                try { outName = System.Text.RegularExpressions.Regex.Replace(name, tbFind.Text, tbRep.Text); }
                catch { outName = name; }
            }
            else if (mode == 5)
            {
                string ne = tbFind.Text.Trim();
                if (ne.Length == 0) return Path.Combine(dir, name + ext);
                if (!ne.StartsWith(".")) ne = "." + ne;
                return Path.Combine(dir, name + ne);
            }
            return Path.Combine(dir, outName + ext);
        }

        void Preview()
        {
            lvPrev.Items.Clear();
            built.Clear();
            if (srcFiles.Count == 0) { Log(en ? "no files" : "没有文件"); return; }
            int conflicts = 0;
            for (int i = 0; i < srcFiles.Count; i++)
            {
                string dst = TargetName(srcFiles[i], i);
                built.Add(dst);
                bool exist = File.Exists(dst) && !string.Equals(dst, srcFiles[i], StringComparison.OrdinalIgnoreCase);
                if (exist) conflicts++;
                ListViewItem it = new ListViewItem(Path.GetFileName(srcFiles[i]));
                it.SubItems.Add(Path.GetFileName(dst));
                it.SubItems.Add(exist ? (en ? "EXISTS" : "已存在") : (en ? "ok" : "可改名"));
                lvPrev.Items.Add(it);
            }
            btnApply.Enabled = true;
            Log((en ? "preview: " : "预览：") + srcFiles.Count.ToString() + (en ? " files, conflicts " : " 个文件，冲突 ") + conflicts.ToString());
            Stat();
        }

        void Apply()
        {
            if (built.Count != srcFiles.Count) Preview();
            int ok = 0, skip = 0;
            List<string> rec = new List<string>();
            for (int i = 0; i < srcFiles.Count; i++)
            {
                string s = srcFiles[i];
                string d = built[i];
                if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase)) { skip++; continue; }
                try
                {
                    if (File.Exists(d)) { skip++; Log((en ? "skip exists: " : "跳过（已存在）：") + Path.GetFileName(d)); continue; }
                    File.Move(s, d);
                    rec.Add(s + "\t" + d);
                    ok++;
                }
                catch (Exception ex)
                {
                    skip++;
                    Log((en ? "fail: " : "失败：") + Path.GetFileName(s) + " " + ex.Message);
                }
            }
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "重命名记录");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "rename_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".tsv");
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < rec.Count; i++) sb.AppendLine(rec[i]);
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                lastRecord = file;
                Log((en ? "undo record: " : "撤销记录：") + Path.GetFileName(file));
            }
            catch (Exception ex) { Log(ex.Message); }
            Log((en ? "renamed " : "改名完成 ") + ok.ToString() + (en ? ", skipped " : " 个，跳过 ") + skip.ToString());

            List<string> now = new List<string>();
            for (int i = 0; i < srcFiles.Count; i++)
            {
                if (built.Count > i && File.Exists(built[i])) now.Add(built[i]);
                else if (File.Exists(srcFiles[i])) now.Add(srcFiles[i]);
            }
            srcFiles.Clear();
            for (int i = 0; i < now.Count; i++) srcFiles.Add(now[i]);
            FillSrc();
            lvPrev.Items.Clear();
            built.Clear();
            btnApply.Enabled = false;
        }

        void Undo()
        {
            string file = lastRecord;
            if (file == null)
            {
                try
                {
                    string dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "重命名记录");
                    if (Directory.Exists(dir))
                    {
                        string[] fs = Directory.GetFiles(dir, "rename_*.tsv");
                        Array.Sort(fs);
                        if (fs.Length > 0) file = fs[fs.Length - 1];
                    }
                }
                catch (Exception dg443_1) { Diag.Swallow("BatchToolForm.cs:443", dg443_1); }
            }
            if (file == null || !File.Exists(file))
            {
                Log(en ? "nothing to undo" : "没有可撤销的记录");
                return;
            }
            int ok = 0, fail = 0;
            try
            {
                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    if (ln.Trim().Length == 0) continue;
                    string[] parts = ln.Split('\t');
                    if (parts.Length < 2) continue;
                    string orig = parts[0];
                    string nw = parts[1];
                    try
                    {
                        if (File.Exists(nw) && !File.Exists(orig))
                        {
                            File.Move(nw, orig);
                            ok++;
                        }
                        else fail++;
                    }
                    catch { fail++; }
                }
                File.Delete(file);
                lastRecord = null;
            }
            catch (Exception ex) { Log(ex.Message); }
            Log((en ? "undo done: " : "撤销完成：") + ok.ToString() + (en ? " restored, " : " 个已还原，") + fail.ToString() + (en ? " skipped" : " 个跳过"));
            srcFiles.Clear();
            lvSrc.Items.Clear();
            Stat();
        }

        void PickConv()
        {
            OpenFileDialog of = new OpenFileDialog();
            of.Multiselect = true;
            of.Filter = en ? "Media|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.mp4;*.avi;*.mkv;*.mov;*.mp3;*.wav;*.flac;*.m4a|All files|*.*" : "媒体文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.mp4;*.avi;*.mkv;*.mov;*.mp3;*.wav;*.flac;*.m4a|所有文件|*.*";
            if (of.ShowDialog(this) != DialogResult.OK) return;
            lvConv.Items.Clear();
            for (int i = 0; i < of.FileNames.Length; i++) AddConvRow(of.FileNames[i]);
            Stat();
        }

        void PickConvFolder()
        {
            FolderBrowserDialog fd = new FolderBrowserDialog();
            if (fd.ShowDialog(this) != DialogResult.OK) return;
            lvConv.Items.Clear();
            try
            {
                string[] all = Directory.GetFiles(fd.SelectedPath);
                for (int i = 0; i < all.Length; i++)
                {
                    string ext = Path.GetExtension(all[i]).ToLower();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif"
                        || ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov"
                        || ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".m4a")
                        AddConvRow(all[i]);
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            Stat();
        }

        void AddConvRow(string path)
        {
            ListViewItem it = new ListViewItem(Path.GetFileName(path));
            it.SubItems.Add(Path.GetExtension(path));
            it.SubItems.Add("");
            it.Tag = path;
            lvConv.Items.Add(it);
        }

        // ---- 自测钩子（/batchtest 使用，正常界面不碰这些方法）----
        public void TestSetMode(int mode, string prefix, string suffix)
        {
            try { if (cbMode.Items.Count > mode) cbMode.SelectedIndex = mode; } catch (Exception dg527_1) { Diag.Swallow("BatchToolForm.cs:527", dg527_1); }
            tbPrefix.Text = prefix;
            tbSuffix.Text = suffix;
        }

        public void TestSetTarget(int idx)
        {
            try { if (cbTarget.Items.Count > idx) cbTarget.SelectedIndex = idx; } catch (Exception dg534_1) { Diag.Swallow("BatchToolForm.cs:534", dg534_1); }
        }

        public void TestLoadFolder(string folder)
        {
            srcFiles.Clear();
            lvSrc.Items.Clear();
            try
            {
                string[] all = Directory.GetFiles(folder);
                for (int i = 0; i < all.Length; i++) srcFiles.Add(all[i]);
            }
            catch (Exception dg546_1) { Diag.Swallow("BatchToolForm.cs:546", dg546_1); }
            FillSrc();
        }

        public string TestPreview()
        {
            Preview();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < built.Count && i < srcFiles.Count; i++)
                sb.AppendLine(Path.GetFileName(srcFiles[i]) + " -> " + Path.GetFileName(built[i]));
            return sb.ToString();
        }

        public void TestApply() { Apply(); }

        public void TestUndo() { Undo(); }

        public void TestAddConv(string path) { AddConvRow(path); }

        public void TestClearConv() { lvConv.Items.Clear(); Stat(); }

        public void TestConvert() { Convert(); }

        public string TestLog() { return tbLog.Text; }

        void Convert()
        {
            if (lvConv.Items.Count == 0) { Log(en ? "no convert targets" : "没有待转换文件"); return; }
            string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
            string ffmpeg = Path.Combine(Path.Combine(baseDir, "video_tool"), "ffmpeg.exe");
            if (!File.Exists(ffmpeg)) ffmpeg = "ffmpeg";
            int target = cbTarget.SelectedIndex;
            int quality = 95;
            if (cbQuality.SelectedIndex == 1) quality = 85;
            if (cbQuality.SelectedIndex == 2) quality = 70;

            string ff = ShortOr(ffmpeg);
            int ok = 0, fail = 0;
            for (int i = 0; i < lvConv.Items.Count; i++)
            {
                string src = (string)lvConv.Items[i].Tag;
                string ext = Path.GetExtension(src).ToLower();
                bool isImg = (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif");
                string outPath = "";
                string err = "";
                bool done = false;

                if (target <= 3)
                {
                    if (!isImg) { Log((en ? "skip non-image: " : "跳过非图片：") + Path.GetFileName(src)); fail++; continue; }
                    string[] e = { ".png", ".jpg", ".bmp", ".gif" };
                    outPath = Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_conv" + e[target]);
                    done = MediaCore.ConvertImage(src, outPath, quality, out err);
                }
                else if (target == 4)
                {
                    outPath = Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_conv.mp4");
                    done = MediaCore.Run(ff, "-y -hide_banner -loglevel error -i \"" + ShortOr(src) + "\" -c:v libx264 -preset veryfast -crf 24 -c:a aac -b:a 128k \"" + ShortOr(outPath) + "\"", 1800000, out err);
                }
                else if (target == 5)
                {
                    outPath = Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_audio.mp3");
                    done = MediaCore.Run(ff, "-y -hide_banner -loglevel error -i \"" + ShortOr(src) + "\" -vn -c:a libmp3lame -b:a 192k \"" + ShortOr(outPath) + "\"", 1800000, out err);
                }
                else
                {
                    outPath = Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_conv.gif");
                    string gerr = "";
                    done = RecorderCore.ToGif(ff, ShortOr(src), ShortOr(outPath), 10, 480, out gerr);
                    err = gerr;
                }

                if (done)
                {
                    ok++;
                    long sz = 0;
                    try { sz = new FileInfo(outPath).Length; } catch (Exception dg622_1) { Diag.Swallow("BatchToolForm.cs:622", dg622_1); }
                    lvConv.Items[i].SubItems[2].Text = "OK " + FmtSize(sz);
                    Log("OK " + Path.GetFileName(src) + " -> " + Path.GetFileName(outPath) + "  " + FmtSize(sz));
                }
                else
                {
                    fail++;
                    lvConv.Items[i].SubItems[2].Text = en ? "failed" : "失败";
                    Log("X " + Path.GetFileName(src) + "  " + err);
                }
                Application.DoEvents();
            }
            Log((en ? "convert finished: " : "转换结束：") + ok.ToString() + (en ? " ok, " : " 成功，") + fail.ToString() + (en ? " failed" : " 失败"));
            Stat();
        }

        static string ShortOr(string p)
        {
            try
            {
                StringBuilder sb = new StringBuilder(4096);
                int r = GetShortPath(p, sb, sb.Capacity);
                if (r > 0 && r < sb.Capacity) return sb.ToString();
            }
            catch (Exception dg646_1) { Diag.Swallow("BatchToolForm.cs:646", dg646_1); }
            return p;
        }
    }
}