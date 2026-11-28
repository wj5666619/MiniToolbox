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
    static class PluginCfg
    {
        public static string Dir = "";

        public static void Init(string baseDir)
        {
            Dir = Path.Combine(baseDir, "插件");
            try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); } catch (Exception dg30_1) { Diag.Swallow("PluginCfg.cs:30", dg30_1); }
            try
            {
                if (Directory.GetFiles(Dir).Length == 0)
                {
                    File.WriteAllText(Path.Combine(Dir, "示例_打开记事本.ps1"),
                        "# 插件示例：文件名就是功能名，面板里选中即执行\r\n# 描述: 打开记事本\r\n# 支持 .ps1 / .py / .cmd，工作目录为本工具所在目录\r\nStart-Process notepad.exe\r\n",
                        new UTF8Encoding(false));
                }
            }
            catch (Exception dg40_1) { Diag.Swallow("PluginCfg.cs:40", dg40_1); }
        }

        public static string[] Files()
        {
            try
            {
                if (Dir.Length == 0 || !Directory.Exists(Dir)) return new string[0];
                return Directory.GetFiles(Dir, "*.*");
            }
            catch { return new string[0]; }
        }

        public static string Title(string fp)
        {
            string nm = Path.GetFileNameWithoutExtension(fp);
            try
            {
                using (StreamReader sr = new StreamReader(fp, Encoding.UTF8))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        string ln = sr.ReadLine();
                        if (ln == null) break;
                        string t = ln.Trim();
                        if (!(t.StartsWith("#") || t.StartsWith("//"))) continue;
                        int p2 = t.IndexOf("描述");
                        if (p2 < 0) continue;
                        string v = t.Substring(p2 + 2).TrimStart(':', '：').Trim();
                        if (v.Length > 0) return v;
                    }
                }
            }
            catch (Exception dg73_1) { Diag.Swallow("PluginCfg.cs:73", dg73_1); }
            return nm;
        }

        public static void Run(string fp, string workDir)
        {
            string ext = Path.GetExtension(fp).ToLowerInvariant();
            ProcessStartInfo psi;
            if (ext == ".ps1") psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + fp + "\"");
            else if (ext == ".py") psi = new ProcessStartInfo("python", "\"" + fp + "\"");
            else if (ext == ".cmd" || ext == ".bat") psi = new ProcessStartInfo("cmd.exe", "/c \"" + fp + "\"");
            else psi = new ProcessStartInfo(fp);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = workDir;
            try { Process.Start(psi); }
            catch (Exception ex)
            {
                MessageBox.Show("插件启动失败：" + ex.Message, "插件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}