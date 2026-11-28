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
    static class ClipMask
    {
        // 按出现顺序打码：先长后短（身份证 18 位比手机号 11 位长，避免被吃掉）
        public static string Text(string s)
        {
            if (s == null || s.Length == 0) return "";
            string t = s;
            try
            {
                // 身份证（18 位，末尾可能是 X）
                t = System.Text.RegularExpressions.Regex.Replace(t, "([1-9][0-9]{5})([0-9]{8})([0-9]{3}[0-9Xx])", "$1********$3");
                // 银行卡（16~19 位连续数字，前后留 4 位）
                t = System.Text.RegularExpressions.Regex.Replace(t, "\\b([0-9]{4})([0-9]{8,11})([0-9]{4})\\b", "$1 **** **** $3");
                // 手机号
                t = System.Text.RegularExpressions.Regex.Replace(t, "\\b(1[3-9][0-9])([0-9]{4})([0-9]{4})\\b", "$1****$3");
                // 邮箱
                t = System.Text.RegularExpressions.Regex.Replace(t, "([A-Za-z0-9._%+-]{1,2})[A-Za-z0-9._%+-]*@([A-Za-z0-9.-]+)", "$1***@$2");
                // IPv4
                t = System.Text.RegularExpressions.Regex.Replace(t, "\\b([0-9]{1,3})\\.([0-9]{1,3})\\.([0-9]{1,3})\\.([0-9]{1,3})\\b", "$1.$2.*.*");
            }
            catch (Exception dg43_1) { Diag.Swallow("ClipMask.cs:43", dg43_1); }
            return t;
        }

        // 命中了几类（给界面显示"共打码 N 处"）
        public static int Hits(string s)
        {
            if (s == null || s.Length == 0) return 0;
            int n = 0;
            n += System.Text.RegularExpressions.Regex.Matches(s, "([1-9][0-9]{5})([0-9]{8})([0-9]{3}[0-9Xx])").Count;
            n += System.Text.RegularExpressions.Regex.Matches(s, "\\b(1[3-9][0-9])[0-9]{4}[0-9]{4}\\b").Count;
            n += System.Text.RegularExpressions.Regex.Matches(s, "\\b[0-9]{4}[0-9]{8,11}[0-9]{4}\\b").Count;
            n += System.Text.RegularExpressions.Regex.Matches(s, "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+").Count;
            n += System.Text.RegularExpressions.Regex.Matches(s, "\\b[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}\\b").Count;
            return n;
        }
    }
}