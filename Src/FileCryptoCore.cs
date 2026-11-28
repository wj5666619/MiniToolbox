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
    class FileCryptoCore
    {
        public static bool Encrypt(string src, string dst, string pwd, out string err)
        {
            err = "";
            try
            {
                if (!File.Exists(src)) { err = "文件不存在"; return false; }
                byte[] salt = new byte[16];
                byte[] iv = new byte[16];
                System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                rng.GetBytes(salt);
                rng.GetBytes(iv);
                byte[] nb = Encoding.UTF8.GetBytes(Path.GetFileName(src));
                using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                {
                    byte[] magic = Encoding.ASCII.GetBytes("MTBX1");
                    fs.Write(magic, 0, 5);
                    byte[] nl = BitConverter.GetBytes((ushort)nb.Length);
                    fs.Write(nl, 0, 2);
                    fs.Write(nb, 0, nb.Length);
                    fs.Write(salt, 0, 16);
                    fs.Write(iv, 0, 16);
                    System.Security.Cryptography.Rfc2898DeriveBytes k =
                        new System.Security.Cryptography.Rfc2898DeriveBytes(pwd, salt, 12000);
                    using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                        aes.Key = k.GetBytes(32);
                        aes.IV = iv;
                        using (System.Security.Cryptography.CryptoStream cs =
                            new System.Security.Cryptography.CryptoStream(fs, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                        using (FileStream input = new FileStream(src, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buf = new byte[65536];
                            int n;
                            while ((n = input.Read(buf, 0, buf.Length)) > 0) cs.Write(buf, 0, n);
                            cs.FlushFinalBlock();
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static string OriginalName(string boxFile)
        {
            try
            {
                using (FileStream fs = new FileStream(boxFile, FileMode.Open, FileAccess.Read))
                {
                    byte[] b5 = new byte[5];
                    fs.Read(b5, 0, 5);
                    if (Encoding.ASCII.GetString(b5) != "MTBX1") return "";
                    byte[] b2 = new byte[2];
                    fs.Read(b2, 0, 2);
                    int len = BitConverter.ToUInt16(b2, 0);
                    byte[] nb = new byte[len];
                    fs.Read(nb, 0, len);
                    return Encoding.UTF8.GetString(nb);
                }
            }
            catch { return ""; }
        }

        public static bool Decrypt(string src, string dst, string pwd, out string err)
        {
            err = "";
            try
            {
                if (!File.Exists(src)) { err = "文件不存在"; return false; }
                using (FileStream fs = new FileStream(src, FileMode.Open, FileAccess.Read))
                {
                    byte[] magic = new byte[5];
                    fs.Read(magic, 0, 5);
                    if (Encoding.ASCII.GetString(magic) != "MTBX1") { err = "不是 .mtbx 加密文件"; return false; }
                    byte[] b2 = new byte[2];
                    fs.Read(b2, 0, 2);
                    int len = BitConverter.ToUInt16(b2, 0);
                    byte[] nb = new byte[len];
                    fs.Read(nb, 0, len);
                    byte[] salt = new byte[16];
                    byte[] iv = new byte[16];
                    fs.Read(salt, 0, 16);
                    fs.Read(iv, 0, 16);
                    System.Security.Cryptography.Rfc2898DeriveBytes k =
                        new System.Security.Cryptography.Rfc2898DeriveBytes(pwd, salt, 12000);
                    using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                        aes.Key = k.GetBytes(32);
                        aes.IV = iv;
                        using (FileStream outFs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                        using (System.Security.Cryptography.CryptoStream cs =
                            new System.Security.Cryptography.CryptoStream(fs, aes.CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Read))
                        {
                            byte[] buf = new byte[65536];
                            int n;
                            while ((n = cs.Read(buf, 0, buf.Length)) > 0) outFs.Write(buf, 0, n);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(dst)) File.Delete(dst); } catch (Exception dg134_1) { Diag.Swallow("FileCryptoCore.cs:134", dg134_1); }
                err = "密码错误或文件损坏";
                if (ex is System.Security.Cryptography.CryptographicException) return false;
                err = ex.Message;
                return false;
            }
        }
    }
}