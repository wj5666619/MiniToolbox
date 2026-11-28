using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace ShutdownTool
{
    /// <summary>
    /// v19.9：更新包验签。下载的更新必须与当前运行程序出自同一张证书；
    /// 当前程序未签名（开发构建）时放宽为只校验 SHA256，避免自锁。
    /// </summary>
    internal static class Sign
    {
        public static string Thumbprint(string file)
        {
            try
            {
                if (file == null || !File.Exists(file)) return "";
                X509Certificate cert = X509Certificate.CreateFromSignedFile(file);
                if (cert == null) return "";
                return new X509Certificate2(cert).Thumbprint ?? "";
            }
            catch (Exception ex)
            {
                Diag.Swallow("Sign.cs:Thumbprint", ex);
                return "";
            }
        }

        public static bool SameSigner(string candidate, out string note)
        {
            note = "";
            string mine = Thumbprint(Application.ExecutablePath);
            string other = Thumbprint(candidate);
            if (mine.Length == 0)
            {
                note = "当前程序未签名，跳过验签";
                return true;
            }
            if (other.Length == 0)
            {
                note = "更新包没有数字签名";
                return false;
            }
            if (!string.Equals(mine, other, StringComparison.OrdinalIgnoreCase))
            {
                note = "更新包证书指纹不一致（期望 " + Short(mine) + "，实际 " + Short(other) + "）";
                return false;
            }
            note = "签名指纹一致（" + Short(other) + "）";
            return true;
        }

        static string Short(string s)
        {
            if (s == null) return "";
            return s.Length <= 12 ? s : s.Substring(0, 12) + "…";
        }
    }
}
