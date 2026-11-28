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
    static class PwdBookCore
    {
        const string MAGIC = "MTBP1";

        public static byte[] B32(string s)
        {
            if (s == null) return new byte[0];
            s = s.Trim().Replace(" ", "").Replace("-", "").ToUpper();
            if (s.EndsWith("=")) s = s.Substring(0, s.Length - 1);
            if (s.EndsWith("=")) s = s.Substring(0, s.Length - 1);
            string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            byte[] outb = new byte[(s.Length * 5) / 8];
            int bits = 0, value = 0, idx = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int v = alpha.IndexOf(s[i]);
                if (v < 0) continue;
                value = (value << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    outb[idx++] = (byte)((value >> (bits - 8)) & 0xFF);
                    bits -= 8;
                }
            }
            return outb;
        }

        public static string Totp(string secret, DateTime when, int digits, int step)
        {
            byte[] key = B32(secret);
            if (key.Length == 0) return "";
            long unix = (long)((when.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            long counter = unix / step;
            byte[] cb = new byte[8];
            for (int i = 7; i >= 0; i--)
            {
                cb[i] = (byte)(counter & 0xFF);
                counter >>= 8;
            }
            System.Security.Cryptography.HMACSHA1 h = new System.Security.Cryptography.HMACSHA1(key);
            byte[] hash = h.ComputeHash(cb);
            int off = hash[hash.Length - 1] & 0x0F;
            int bin = ((hash[off] & 0x7F) << 24) | ((hash[off + 1] & 0xFF) << 16) | ((hash[off + 2] & 0xFF) << 8) | (hash[off + 3] & 0xFF);
            int mod = 1;
            for (int i = 0; i < digits; i++) mod *= 10;
            int code = bin % mod;
            string s = code.ToString();
            while (s.Length < digits) s = "0" + s;
            return s;
        }

        public static int RemainSeconds(int step)
        {
            long unix = (long)((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            return step - (int)(unix % step);
        }

        static byte[] Derive(string pwd, byte[] salt)
        {
            System.Security.Cryptography.Rfc2898DeriveBytes d = new System.Security.Cryptography.Rfc2898DeriveBytes(pwd, salt, 12000);
            byte[] k = d.GetBytes(32);
            return k;
        }

        public static string Serialize(List<PwdEntry> list)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                PwdEntry e = list[i];
                sb.Append(Clean(e.Site)).Append('\t')
                  .Append(Clean(e.User)).Append('\t')
                  .Append(Clean(e.Pwd)).Append('\t')
                  .Append(Clean(e.Note)).Append('\t')
                  .Append(Clean(e.Secret)).Append('\n');
            }
            return sb.ToString();
        }

        static string Clean(string s)
        {
            if (s == null) return "";
            return s.Replace("\t", " ").Replace("\r", "").Replace("\n", " / ");
        }

        public static List<PwdEntry> Parse(string text)
        {
            List<PwdEntry> list = new List<PwdEntry>();
            if (text == null) return list;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] p = lines[i].Split('\t');
                PwdEntry e = new PwdEntry();
                if (p.Length > 0) e.Site = p[0];
                if (p.Length > 1) e.User = p[1];
                if (p.Length > 2) e.Pwd = p[2];
                if (p.Length > 3) e.Note = p[3];
                if (p.Length > 4) e.Secret = p[4];
                list.Add(e);
            }
            return list;
        }

        public static bool Save(string file, string masterPwd, List<PwdEntry> list, out string err)
        {
            err = "";
            try
            {
                byte[] salt = new byte[16];
                byte[] iv = new byte[16];
                System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                rng.GetBytes(salt);
                rng.GetBytes(iv);
                byte[] key = Derive(masterPwd, salt);
                byte[] plain = Encoding.UTF8.GetBytes(Serialize(list));
                byte[] cipher;
                System.Security.Cryptography.AesManaged aes = new System.Security.Cryptography.AesManaged();
                aes.KeySize = 256;
                aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                try
                {
                    using (System.Security.Cryptography.ICryptoTransform enc = aes.CreateEncryptor())
                        cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                }
                finally { aes.Clear(); }

                using (FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(Encoding.ASCII.GetBytes(MAGIC), 0, 5);
                    fs.Write(salt, 0, salt.Length);
                    fs.Write(iv, 0, iv.Length);
                    fs.Write(cipher, 0, cipher.Length);
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        public static List<PwdEntry> Load(string file, string masterPwd, out string err, out bool wrongPwd)
        {
            err = "";
            wrongPwd = false;
            List<PwdEntry> empty = new List<PwdEntry>();
            try
            {
                if (!File.Exists(file)) { err = "文件不存在"; return empty; }
                byte[] all = File.ReadAllBytes(file);
                if (all.Length < 5 + 32) { err = "文件损坏"; return empty; }
                string magic = Encoding.ASCII.GetString(all, 0, 5);
                if (magic != MAGIC) { err = "不是密码本文件"; return empty; }
                byte[] salt = new byte[16];
                byte[] iv = new byte[16];
                Array.Copy(all, 5, salt, 0, 16);
                Array.Copy(all, 21, iv, 0, 16);
                int clen = all.Length - 37;
                byte[] cipher = new byte[clen];
                Array.Copy(all, 37, cipher, 0, clen);
                byte[] key = Derive(masterPwd, salt);
                byte[] plain;
                System.Security.Cryptography.AesManaged aes = new System.Security.Cryptography.AesManaged();
                aes.KeySize = 256;
                aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                try
                {
                    using (System.Security.Cryptography.ICryptoTransform dec = aes.CreateDecryptor())
                        plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                }
                finally { aes.Clear(); }
                string text = Encoding.UTF8.GetString(plain);
                return Parse(text);
            }
            catch (Exception)
            {
                wrongPwd = true;
                err = "密码错误或文件损坏";
                return empty;
            }
        }
    }
}
