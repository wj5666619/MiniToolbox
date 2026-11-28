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
    class BackupCore
    {
        public class Item
        {
            public string Name;
            public string Path;
            public long Size;
            public bool IsDir;
        }

        public static List<Item> Collect(string baseDir)
        {
            List<Item> list = new List<Item>();
            string[] files = new string[] { "hotkeys.json", "保险箱.box", "加速备份.tsv", "剪贴板历史.dat", "update_url.txt" };
            foreach (string f in files)
            {
                string p = Path.Combine(baseDir, f);
                if (!File.Exists(p)) continue;
                Item it = new Item();
                it.Name = f; it.Path = p; it.IsDir = false;
                try { it.Size = new FileInfo(p).Length; } catch (Exception dg43_1) { Diag.Swallow("BackupCore.cs:43", dg43_1); }
                list.Add(it);
            }
            string[] dirs = new string[] { "整理记录", "查重记录" };
            foreach (string d in dirs)
            {
                string p = Path.Combine(baseDir, d);
                if (!Directory.Exists(p)) continue;
                Item it = new Item();
                it.Name = d; it.Path = p; it.IsDir = true;
                it.Size = DirSize(p);
                list.Add(it);
            }
            return list;
        }

        public static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir)) { try { total += new FileInfo(f).Length; } catch (Exception dg64_1) { Diag.Swallow("BackupCore.cs:64", dg64_1); } }
                foreach (string d in Directory.GetDirectories(dir)) total += DirSize(d);
            }
            catch (Exception dg67_1) { Diag.Swallow("BackupCore.cs:67", dg67_1); }
            return total;
        }

        public static bool Create(string zipFile, List<Item> items, out string err)
        {
            err = "";
            string temp = Path.Combine(Path.GetTempPath(), "mtb_bk_" + DateTime.Now.ToString("HHmmssfff"));
            try
            {
                Directory.CreateDirectory(temp);
                foreach (Item it in items)
                {
                    if (it.IsDir)
                    {
                        string dst = Path.Combine(temp, it.Name);
                        if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
                        foreach (string f in Directory.GetFiles(it.Path)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                    }
                    else
                    {
                        File.Copy(it.Path, Path.Combine(temp, it.Name), true);
                    }
                }
                string dir = Path.GetDirectoryName(zipFile);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(zipFile)) File.Delete(zipFile);
                ZipFile.CreateFromDirectory(temp, zipFile);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
            finally
            {
                try { Directory.Delete(temp, true); } catch (Exception dg100_1) { Diag.Swallow("BackupCore.cs:100", dg100_1); }
            }
        }

        public static string[] Entries(string zipFile)
        {
            List<string> n = new List<string>();
            try
            {
                using (ZipArchive a = ZipFile.OpenRead(zipFile))
                    foreach (ZipArchiveEntry e in a.Entries) n.Add(e.FullName + " (" + e.Length + " B)");
            }
            catch (Exception dg112_1) { Diag.Swallow("BackupCore.cs:112", dg112_1); }
            return n.ToArray();
        }

        public static bool Restore(string zipFile, string baseDir, out string err, out string report)
        {
            err = ""; report = "";
            StringBuilder rep = new StringBuilder();
            string temp = Path.Combine(Path.GetTempPath(), "mtb_rs_" + DateTime.Now.ToString("HHmmssfff"));
            try
            {
                Directory.CreateDirectory(temp);
                ZipFile.ExtractToDirectory(zipFile, temp);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                foreach (string f in Directory.GetFiles(temp))
                {
                    string name = Path.GetFileName(f);
                    string dst = Path.Combine(baseDir, name);
                    if (File.Exists(dst)) { try { File.Copy(dst, dst + ".bak_" + stamp, true); } catch (Exception dg130_1) { Diag.Swallow("BackupCore.cs:130", dg130_1); } }
                    File.Copy(f, dst, true);
                    rep.AppendLine("还原 " + name);
                }
                foreach (string d in Directory.GetDirectories(temp))
                {
                    string name = Path.GetFileName(d);
                    string dst = Path.Combine(baseDir, name);
                    if (Directory.Exists(dst)) { try { Directory.Delete(dst, true); } catch (Exception dg138_1) { Diag.Swallow("BackupCore.cs:138", dg138_1); } }
                    Directory.CreateDirectory(dst);
                    foreach (string f in Directory.GetFiles(d))
                        File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                    rep.AppendLine("还原目录 " + name);
                }
                report = rep.ToString();
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
            finally
            {
                try { Directory.Delete(temp, true); } catch (Exception dg150_1) { Diag.Swallow("BackupCore.cs:150", dg150_1); }
            }
        }

        public static List<string> ListBackups(string dir)
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return list;
                foreach (string f in Directory.GetFiles(dir, "备份_*.zip")) list.Add(f);
            }
            catch (Exception dg162_1) { Diag.Swallow("BackupCore.cs:162", dg162_1); }
            list.Sort();
            list.Reverse();
            return list;
        }
    }
}