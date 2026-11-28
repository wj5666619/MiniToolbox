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
    static class ClipKind
    {
        public static string Of(string s)
        {
            if (string.IsNullOrEmpty(s)) return "text";
            string t = s.Trim();
            if (t.Length == 0) return "text";
            if (t.StartsWith("http://") || t.StartsWith("https://") || t.StartsWith("www.")
                || (t.IndexOf("://") > 0 && t.IndexOf(' ') < 0)) return "link";
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{4,8}$")) return "otp";
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z]:\\")
                || (t.IndexOf('\\') > 0 && t.IndexOf(' ') < 0 && t.Length < 260)) return "path";
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,6}$")) return "mail";
            if ((t.IndexOf('{') >= 0 && t.IndexOf('}') >= 0) || t.IndexOf("=>") > 0
                || (t.IndexOf(';') > 0 && t.IndexOf('(') > 0)) return "code";
            return "text";
        }

        public static string Label(string kind, bool en)
        {
            switch (kind)
            {
                case "link": return en ? "link" : "链接";
                case "otp": return en ? "otp" : "验证码";
                case "path": return en ? "path" : "路径";
                case "mail": return en ? "mail" : "邮箱";
                case "code": return "code";
                default: return en ? "text" : "文本";
            }
        }

        public static Color Tint(string kind)
        {
            switch (kind)
            {
                case "link": return UiKit.A(96, 165, 250);
                case "otp": return UiKit.A(250, 130, 120);
                case "path": return UiKit.A(250, 190, 90);
                case "mail": return UiKit.A(140, 220, 180);
                case "code": return UiKit.A(120, 220, 160);
                default: return UiKit.A(150, 162, 182);
            }
        }
    }
}
