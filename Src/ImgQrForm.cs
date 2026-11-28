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
    class ImgQrForm : Form
    {
        bool en;
        string dir;
        Label st;
        ListView lv;
        PictureBox pv;
        TextBox tbQr;
        ComboBox cbPixel;
        int qrVer = 0;

        public ImgQrForm(bool eng, string baseDir)
        {
            en = eng;
            dir = baseDir;
            Text = V71Ui.TT(en, "图片批量压缩 / 二维码 · 小工具箱", "Image compress & QR · Mini Toolbox");
            BackColor = V71Ui.BG;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(940, 640);
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(V71Ui.Title(V71Ui.TT(en, "图片批量压缩 / 二维码", "Image compress & QR"), 20, 12));

            // ---- 左：批量压缩 ----
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "批量压缩：选一个文件夹，按目标体积逐张压，结果另存子目录，原图不动。",
                "Batch: pick a folder, squeeze each image under the target size into a new subfolder."), 20, 44, 460, 36));

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "目标体积 KB（0=只优化）", "Target KB (0 = optimize only)"), 20, 86, 220, 22, 9F, V71Ui.SUB));
            ComboBox cbKB = V71Ui.Combo(new string[] { "0", "50", "100", "200", "300", "500", "800", "1024" }, 20, 110, 110);
            cbKB.SelectedIndex = 3;
            Controls.Add(cbKB);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "最长边（0=不改尺寸）", "Max side (0 = keep)"), 250, 86, 200, 22, 9F, V71Ui.SUB));
            ComboBox cbSide = V71Ui.Combo(new string[] { "0", "1080", "1440", "1920", "2560", "3840" }, 250, 110, 110);
            cbSide.SelectedIndex = 0;
            Controls.Add(cbSide);

            Controls.Add(V71Ui.Text(V71Ui.TT(en, "输出格式", "Format"), 380, 86, 110, 22, 9F, V71Ui.SUB));
            ComboBox cbFmt = V71Ui.Combo(new string[] { "jpg", "png", "bmp" }, 380, 110, 100);
            Controls.Add(cbFmt);

            CheckBox chkKeep = new RoundCheck();
            chkKeep.Text = V71Ui.TT(en, "压缩反而变大时保留原图", "Keep original if bigger");
            chkKeep.ForeColor = V71Ui.SUB;
            chkKeep.BackColor = Color.Transparent;
            chkKeep.Checked = true;
            chkKeep.Location = new Point(20, 146);
            chkKeep.Size = new Size(260, 22);
            Controls.Add(chkKeep);

            Button bDir = V71Ui.Btn(V71Ui.TT(en, "选择文件夹并开始", "Pick folder & run"), UiKit.A(34, 197, 94), 20, 176, 180, 34);
            Button bFiles = V71Ui.Btn(V71Ui.TT(en, "选图片文件", "Pick files"), UiKit.A(96, 165, 250), 210, 176, 150, 34);
            Controls.Add(bDir); Controls.Add(bFiles);

            lv = V71Ui.List(new string[] { V71Ui.TT(en, "文件", "File"), V71Ui.TT(en, "原大小", "Before"), V71Ui.TT(en, "新大小", "After"), V71Ui.TT(en, "状态", "State") },
                new int[] { 190, 80, 80, 110 }, 20, 222, 480, 300);
            Controls.Add(lv);

            st = V71Ui.Note("", 20, 530, 480, 60);
            Controls.Add(st);

            // ---- 右：二维码 ----
            Controls.Add(V71Ui.Note(V71Ui.TT(en, "二维码：文本 / 网址 / WiFi / 名片，自动选最小版本（最多 213 字节）。",
                "QR: text, URL, WiFi or vCard; smallest version auto-picked (max 213 bytes)."), 520, 44, 400, 36));

            tbQr = V71Ui.Box("https://github.com/wj5666619/MiniToolbox", 520, 86, 400, 26);
            Controls.Add(V71Ui.Text(V71Ui.TT(en, "内容", "Content"), 520, 118, 60, 20, 8.5F, V71Ui.SUB));
            Controls.Add(tbQr);

            cbPixel = V71Ui.Combo(new string[] { "4", "6", "8", "10", "12" }, 520, 148, 70);
            cbPixel.SelectedIndex = 2;
            Controls.Add(V71Ui.Text(V71Ui.TT(en, "像素/格", "Px/module"), 596, 150, 90, 20, 8.5F, V71Ui.SUB));

            Button bWifi = V71Ui.Btn(V71Ui.TT(en, "WiFi 卡片", "WiFi card"), UiKit.A(14, 165, 233), 700, 146, 110, 30);
            Button bVcard = V71Ui.Btn(V71Ui.TT(en, "名片", "vCard"), UiKit.A(139, 92, 246), 816, 146, 100, 30);
            Controls.Add(bWifi); Controls.Add(bVcard);

            pv = new PictureBox();
            pv.BackColor = Color.White;
            pv.BorderStyle = BorderStyle.FixedSingle;
            pv.Location = new Point(520, 186);
            pv.Size = new Size(400, 300);
            pv.SizeMode = PictureBoxSizeMode.Zoom;
            Controls.Add(pv);

            Button bGen = V71Ui.Btn(V71Ui.TT(en, "生成", "Generate"), UiKit.A(34, 197, 94), 520, 496, 110, 34);
            Button bSave = V71Ui.Btn(V71Ui.TT(en, "保存 PNG", "Save PNG"), UiKit.A(251, 146, 60), 640, 496, 120, 34);
            Button bCopy = V71Ui.Btn(V71Ui.TT(en, "复制图片", "Copy image"), UiKit.A(96, 165, 250), 770, 496, 120, 34);
            Button bClose = V71Ui.Btn(V71Ui.TT(en, "关闭", "Close"), UiKit.A(52, 58, 70), 810, 594, 100, 34);
            Controls.Add(bGen); Controls.Add(bSave); Controls.Add(bCopy); Controls.Add(bClose);
            bClose.Click += delegate { Close(); };

            bWifi.Click += delegate { tbQr.Text = ImgQrCore.WifiPayload("MyWiFi", "12345678", "WPA"); Generate(); };
            bVcard.Click += delegate { tbQr.Text = ImgQrCore.VCardPayload("张三", "13800000000", "zhang@example.com"); Generate(); };

            bGen.Click += delegate { Generate(); };

            bSave.Click += delegate
            {
                if (pv.Image == null) { st.Text = V71Ui.TT(en, "先生成二维码", "Generate first"); return; }
                SaveFileDialog sd = new SaveFileDialog();
                sd.Filter = "PNG|*.png";
                sd.FileName = "二维码_" + V71Ui.Stamp() + ".png";
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                pv.Image.Save(sd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                st.Text = V71Ui.TT(en, "已保存：", "saved: ") + sd.FileName;
            };

            bCopy.Click += delegate
            {
                if (pv.Image == null) { st.Text = V71Ui.TT(en, "先生成二维码", "Generate first"); return; }
                try
                {
                    Clipboard.SetImage(pv.Image);
                    st.Text = V71Ui.TT(en, "已复制到剪贴板", "copied to clipboard");
                }
                catch (Exception ex) { st.Text = ex.Message; }
            };

            Action<string[]> runFiles = delegate(string[] files)
            {
                if (files.Length == 0) { st.Text = V71Ui.TT(en, "没找到图片", "no images"); return; }
                int kb = 0, side = 0;
                int.TryParse((string)cbKB.SelectedItem, out kb);
                int.TryParse((string)cbSide.SelectedItem, out side);
                string fmt = (string)cbFmt.SelectedItem;
                string outDir = ImgQrCore.OutDir(Path.GetDirectoryName(files[0]));
                Directory.CreateDirectory(outDir);
                lv.Items.Clear();
                long totalBefore = 0, totalAfter = 0;
                Cursor.Current = Cursors.WaitCursor;
                int done = 0;
                foreach (string f in files)
                {
                    ImgQrCore.Job j = ImgQrCore.CompressOne(f, outDir, kb, side, fmt, chkKeep.Checked);
                    ListViewItem li = new ListViewItem(new string[] {
                        Path.GetFileName(f), SizeText(j.Before), SizeText(j.After),
                        j.Ok ? (j.Before > 0 ? (100 - (int)(j.After * 100 / j.Before)) + "%" : "OK") : j.Msg });
                    if (!j.Ok) li.ForeColor = V71Ui.BAD;
                    lv.Items.Add(li);
                    totalBefore += j.Before;
                    totalAfter += j.After;
                    done++;
                    if (done % 5 == 0) Application.DoEvents();
                }
                Cursor.Current = Cursors.Default;
                st.Text = V71Ui.TT(en, "完成 ", "done ") + files.Length + V71Ui.TT(en, " 张：", " imgs: ")
                    + SizeText(totalBefore) + " → " + SizeText(totalAfter)
                    + V71Ui.TT(en, "  输出：", "  out: ") + outDir;
            };

            bDir.Click += delegate
            {
                FolderBrowserDialog fd = new FolderBrowserDialog();
                if (fd.ShowDialog(this) != DialogResult.OK) return;
                runFiles(ImgQrCore.PickImages(fd.SelectedPath));
            };

            bFiles.Click += delegate
            {
                OpenFileDialog od = new OpenFileDialog();
                od.Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*";
                od.Multiselect = true;
                if (od.ShowDialog(this) != DialogResult.OK) return;
                runFiles(od.FileNames);
            };

            Generate();
        }

        void Generate()
        {
            string err = "";
            int ver = 0;
            Bitmap bmp = QrCore.Render(tbQr.Text, PixelOf(cbPixel), UiKit.A(16, 20, 26), Color.White, out err);
            if (bmp == null)
            {
                pv.Image = null;
                st.Text = err;
                return;
            }
            qrVer = ver;
            Image old = pv.Image;
            pv.Image = bmp;
            if (old != null) old.Dispose();
            st.Text = V71Ui.TT(en, "二维码已生成 · 版本 v", "QR ready · version v") + qrVer
                + V71Ui.TT(en, " · ", " · ") + Encoding.UTF8.GetByteCount(tbQr.Text) + "/" + QrCore.MaxCapacity() + " bytes";
        }

        int PixelOf(ComboBox cb)
        {
            int p = 4;
            if (cb.SelectedItem != null) int.TryParse((string)cb.SelectedItem, out p);
            if (p < 1) p = 1;
            if (p > 20) p = 20;
            return p;
        }

        static string SizeText(long b)
        {
            if (b >= 1048576) return (b / 1048576.0).ToString("0.00") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0.0") + " KB";
            return b + " B";
        }
    }
}
