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
    class ClipHistStore
    {
        public class Entry
        {
            public string Time;
            public string Text;
            public bool Pinned;
            public string Kind = "t";   // v14.0：t = 文本，i = 图片
            public string Img = "";     // 图片项：剪贴板图片目录里的文件名
        }

        public List<Entry> Items = new List<Entry>();
        public string FilePath = "";
        public bool Enabled = true;
        public int Max = 200;
        string last = "";

        public ClipHistStore(string file)
        {
            FilePath = file;
            Load();
        }

        // DPAPI 当前用户加密，存盘不落明文
        public static string Prot(string s)
        {
            try
            {
                byte[] b = System.Security.Cryptography.ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(s), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(b);
            }
            catch
            {
                try { return "P:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
                catch { return ""; }
            }
        }

        public static string Unprot(string s)
        {
            if (s == null || s.Length == 0) return "";
            if (s.StartsWith("P:"))
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(2))); } catch { return ""; }
            }
            try
            {
                byte[] b = Convert.FromBase64String(s);
                return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                    b, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }
            catch { return ""; }
        }

        public void Capture()
        {
            if (!Enabled) return;
            // v14.0：先看剪贴板里是不是图片。图片按「尺寸 + 两个采样点」去重，
            // 每次轮询不做整图编码（4K 截图整图转 PNG 会把界面卡住）。
            bool isImg = false;
            try { isImg = Clipboard.ContainsImage(); } catch { return; }
            if (isImg)
            {
                try
                {
                    Bitmap im = Clipboard.GetImage() as Bitmap;
                    if (im == null) return;
                    int w0 = im.Width, h0 = im.Height;
                    if (w0 < 8 || h0 < 8) { im.Dispose(); return; }
                    Color c1 = im.GetPixel(w0 / 2, h0 / 2);
                    Color c2 = im.GetPixel(0, 0);
                    string key = "I:" + w0 + "x" + h0 + ":" + c1.ToArgb() + ":" + c2.ToArgb();
                    if (key == last) { im.Dispose(); return; }
                    last = key;
                    AddImage(im, w0, h0);
                }
                catch (Exception dg100_1) { Diag.Swallow("ClipHistStore.cs:100", dg100_1); }
                return;
            }
            string t = "";
            try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); }
            catch { return; }
            if (t == null || t.Length == 0 || t == last) return;
            last = t;
            Add(t);
        }

        public string ImgDir()
        {
            string d = Path.Combine(Path.GetDirectoryName(FilePath), "剪贴板图片");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg114_1) { Diag.Swallow("ClipHistStore.cs:114", dg114_1); }
            return d;
        }

        public string ImgFull(Entry e)
        {
            if (e == null || e.Img == null || e.Img.Length == 0) return "";
            return Path.Combine(ImgDir(), e.Img);
        }

        public void AddImage(Image im, int w, int h)
        {
            try
            {
                string name = "img_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                string full = Path.Combine(ImgDir(), name);
                im.Save(full, System.Drawing.Imaging.ImageFormat.Png);
                long sz = 0;
                try { sz = new FileInfo(full).Length; } catch (Exception dg132_1) { Diag.Swallow("ClipHistStore.cs:132", dg132_1); }
                Entry e = new Entry();
                e.Time = DateTime.Now.ToString("MM-dd HH:mm:ss");
                e.Kind = "i";
                e.Img = name;
                e.Text = "图片 " + w + "x" + h + " · " + (sz / 1024) + " KB";
                e.Pinned = false;
                Items.Insert(0, e);
                TrimAndDropImages();
                Save();
            }
            catch (Exception dg143_1) { Diag.Swallow("ClipHistStore.cs:143", dg143_1); }
            finally { try { im.Dispose(); } catch (Exception dg144_1) { Diag.Swallow("ClipHistStore.cs:144", dg144_1); } }
        }

        // 图片最多留 30 张（磁盘友好），被挤出去的图片连文件一起删
        void TrimAndDropImages()
        {
            try
            {
                int imgCount = 0;
                for (int i = 0; i < Items.Count; i++) if (Items[i].Kind == "i") imgCount++;
                for (int i = Items.Count - 1; i >= 0 && imgCount > 30; i--)
                {
                    if (Items[i].Kind != "i" || Items[i].Pinned) continue;
                    string f = ImgFull(Items[i]);
                    Items.RemoveAt(i);
                    imgCount--;
                    try { if (f.Length > 0 && File.Exists(f)) File.Delete(f); } catch (Exception dg160_1) { Diag.Swallow("ClipHistStore.cs:160", dg160_1); }
                }
            }
            catch (Exception dg163_1) { Diag.Swallow("ClipHistStore.cs:163", dg163_1); }
        }

        public void Add(string t)
        {
            if (t == null || t.Length == 0) return;
            Entry e = new Entry();
            e.Time = DateTime.Now.ToString("MM-dd HH:mm:ss");
            e.Text = t;
            e.Pinned = false;
            Items.Insert(0, e);
            int i = Items.Count - 1;
            while (Items.Count > Max && i >= 0)
            {
                if (!Items[i].Pinned) Items.RemoveAt(i);
                i--;
            }
            Save();
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"items\":[");
                for (int i = 0; i < Items.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("{\"t\":\"").Append(Items[i].Time).Append("\",\"x\":\"").Append(Prot(Items[i].Text))
                      .Append("\",\"p\":").Append(Items[i].Pinned ? "true" : "false")
                      .Append(",\"k\":\"").Append(Items[i].Kind).Append("\",\"f\":\"").Append(Items[i].Img).Append("\"}");
                }
                sb.Append("]}");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception dg199_1) { Diag.Swallow("ClipHistStore.cs:199", dg199_1); }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string raw = File.ReadAllText(FilePath, Encoding.UTF8);
                if (raw.Length < 10) return;
                JavaScriptSerializer js = new JavaScriptSerializer();
                object root = js.DeserializeObject(raw);
                System.Collections.Generic.IDictionary<string, object> rd = root as System.Collections.Generic.IDictionary<string, object>;
                if (rd == null || !rd.ContainsKey("items")) return;
                System.Collections.IEnumerable arr = rd["items"] as System.Collections.IEnumerable;
                if (arr == null) return;
                foreach (object o in arr)
                {
                    System.Collections.Generic.IDictionary<string, object> d = o as System.Collections.Generic.IDictionary<string, object>;
                    if (d == null) continue;
                    Entry e = new Entry();
                    e.Time = d.ContainsKey("t") ? Convert.ToString(d["t"]) : "";
                    e.Text = d.ContainsKey("x") ? Unprot(Convert.ToString(d["x"])) : "";
                    e.Pinned = d.ContainsKey("p") && Convert.ToBoolean(d["p"]);
                    if (d.ContainsKey("k")) e.Kind = Convert.ToString(d["k"]);
                    if (d.ContainsKey("f")) e.Img = Convert.ToString(d["f"]);
                    if (e.Text.Length > 0) Items.Add(e);
                }
            }
            catch
            {
                // v14.0：解析失败不要静默吞掉再被下一次 Save 覆盖 —— 先把坏文件备份成 .bad
                try
                {
                    if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bad", true);
                }
                catch (Exception dg235_1) { Diag.Swallow("ClipHistStore.cs:235", dg235_1); }
            }
        }

        public void ClearUnpinned()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].Pinned) continue;
                string f = ImgFull(Items[i]);
                if (Items[i].Kind == "i" && f.Length > 0) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception dg245_1) { Diag.Swallow("ClipHistStore.cs:245", dg245_1); } }
                Items.RemoveAt(i);
            }
            Save();
        }

        static bool NotPinned(Entry e) { return !e.Pinned; }

        public static string Preview(string s, int max)
        {
            if (s == null) return "";
            string t = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (t.IndexOf("  ") >= 0) t = t.Replace("  ", " ");
            if (t.Length > max) t = t.Substring(0, max) + "…";
            return t;
        }
    }
}