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
    class VaultStore
    {
        public class Entry
        {
            public string id = "";
            public string cat = "";
            public string title = "";
            public string user = "";
            public string pass = "";
            public string note = "";
            public string upd = "";
        }

        public List<Entry> Entries = new List<Entry>();

        public static string Esc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static byte[] Derive(string pwd, byte[] salt)
        {
            using (System.Security.Cryptography.Rfc2898DeriveBytes d = new System.Security.Cryptography.Rfc2898DeriveBytes(pwd, salt, 12000))
                return d.GetBytes(32);
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"entries\":[");
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"id\":\"").Append(Esc(e.id)).Append("\",\"cat\":\"").Append(Esc(e.cat))
                  .Append("\",\"title\":\"").Append(Esc(e.title)).Append("\",\"user\":\"").Append(Esc(e.user))
                  .Append("\",\"pass\":\"").Append(Esc(e.pass)).Append("\",\"note\":\"").Append(Esc(e.note))
                  .Append("\",\"upd\":\"").Append(Esc(e.upd)).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public void FromJson(string json)
        {
            Entries = new List<Entry>();
            JavaScriptSerializer js = new JavaScriptSerializer();
            object root = js.DeserializeObject(json);
            System.Collections.Generic.IDictionary<string, object> rd = root as System.Collections.Generic.IDictionary<string, object>;
            if (rd == null) return;
            System.Collections.IEnumerable arr = rd["entries"] as System.Collections.IEnumerable;
            if (arr == null) return;
            foreach (object o in arr)
            {
                System.Collections.Generic.IDictionary<string, object> d = o as System.Collections.Generic.IDictionary<string, object>;
                if (d == null) continue;
                Entry e = new Entry();
                if (d.ContainsKey("id")) e.id = Convert.ToString(d["id"]);
                if (d.ContainsKey("cat")) e.cat = Convert.ToString(d["cat"]);
                if (d.ContainsKey("title")) e.title = Convert.ToString(d["title"]);
                if (d.ContainsKey("user")) e.user = Convert.ToString(d["user"]);
                if (d.ContainsKey("pass")) e.pass = Convert.ToString(d["pass"]);
                if (d.ContainsKey("note")) e.note = Convert.ToString(d["note"]);
                if (d.ContainsKey("upd")) e.upd = Convert.ToString(d["upd"]);
                Entries.Add(e);
            }
        }

        public bool Load(string path, string pwd, out string err)
        {
            err = "";
            Entries = new List<Entry>();
            try
            {
                byte[] all = File.ReadAllBytes(path);
                if (all.Length < 5 + 16 + 16 + 16) { err = "文件损坏"; return false; }
                if (!(all[0] == 'M' && all[1] == 'T' && all[2] == 'B' && all[3] == 'V' && all[4] == '1')) { err = "不是小工具箱保险箱文件"; return false; }
                byte[] salt = new byte[16]; Buffer.BlockCopy(all, 5, salt, 0, 16);
                byte[] iv = new byte[16]; Buffer.BlockCopy(all, 21, iv, 0, 16);
                byte[] ct = new byte[all.Length - 37]; Buffer.BlockCopy(all, 37, ct, 0, ct.Length);
                byte[] plain;
                using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                    aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                    aes.Key = Derive(pwd, salt);
                    aes.IV = iv;
                    using (System.Security.Cryptography.ICryptoTransform dec = aes.CreateDecryptor())
                        plain = dec.TransformFinalBlock(ct, 0, ct.Length);
                }
                FromJson(Encoding.UTF8.GetString(plain));
                return true;
            }
            catch
            {
                err = "密码错误或文件损坏";
                Entries = new List<Entry>();
                return false;
            }
        }

        public bool Save(string path, string pwd, out string err)
        {
            err = "";
            try
            {
                byte[] salt = new byte[16];
                byte[] iv = new byte[16];
                using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                    rng.GetBytes(iv);
                }
                byte[] plain = Encoding.UTF8.GetBytes(ToJson());
                byte[] ct;
                using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                    aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                    aes.Key = Derive(pwd, salt);
                    aes.IV = iv;
                    using (System.Security.Cryptography.ICryptoTransform enc = aes.CreateEncryptor())
                        ct = enc.TransformFinalBlock(plain, 0, plain.Length);
                }
                byte[] outb = new byte[37 + ct.Length];
                outb[0] = (byte)'M'; outb[1] = (byte)'T'; outb[2] = (byte)'B'; outb[3] = (byte)'V'; outb[4] = (byte)'1';
                Buffer.BlockCopy(salt, 0, outb, 5, 16);
                Buffer.BlockCopy(iv, 0, outb, 21, 16);
                Buffer.BlockCopy(ct, 0, outb, 37, ct.Length);
                string dir = Path.GetDirectoryName(path);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, outb);
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }
    }
}
