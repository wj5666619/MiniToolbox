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
    static class AiLocal
    {
        public static string Dir = "";

        public static void Init(string baseDir)
        {
            Dir = Path.Combine(baseDir, "AI");
            try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); } catch (Exception dg30_1) { Diag.Swallow("AiLocal.cs:30", dg30_1); }
        }

        public static string Exe()
        {
            string[] cand = { "llama-cli.exe", "main.exe", "llama.exe" };
            try
            {
                for (int i = 0; i < cand.Length; i++)
                {
                    string p = Path.Combine(Dir, cand[i]);
                    if (File.Exists(p)) return p;
                }
                string[] sub = Directory.GetDirectories(Dir);
                for (int i = 0; i < sub.Length; i++)
                    for (int k = 0; k < cand.Length; k++)
                    {
                        string p2 = Path.Combine(sub[i], cand[k]);
                        if (File.Exists(p2)) return p2;
                    }
            }
            catch (Exception dg51_1) { Diag.Swallow("AiLocal.cs:51", dg51_1); }
            return "";
        }

        public static string Model()
        {
            try
            {
                if (!Directory.Exists(Dir)) return "";
                string[] top = Directory.GetFiles(Dir, "*.gguf");
                if (top.Length > 0) return top[0];
                string[] sub = Directory.GetDirectories(Dir);
                for (int i = 0; i < sub.Length; i++)
                {
                    string[] ml = Directory.GetFiles(sub[i], "*.gguf");
                    if (ml.Length > 0) return ml[0];
                }
            }
            catch (Exception dg69_1) { Diag.Swallow("AiLocal.cs:69", dg69_1); }
            return "";
        }

        public static bool Ready { get { return Exe().Length > 0 && Model().Length > 0; } }

        public static string Status()
        {
            string e = Exe(), m = Model();
            if (e.Length > 0 && m.Length > 0)
            {
                long mb = 0;
                try { mb = new FileInfo(m).Length / 1048576; } catch (Exception dg81_1) { Diag.Swallow("AiLocal.cs:81", dg81_1); }
                return "大模型已就绪：" + Path.GetFileName(m) + "（" + mb + " MB）";
            }
            if (e.Length == 0 && m.Length == 0)
                return "内置语义引擎运行中；未检测到本地模型（把 llama-cli.exe 与 .gguf 放进 AI\\ 目录即可再启用大模型问答）";
            if (e.Length == 0) return "发现模型文件，但缺推理程序（把 llama-cli.exe 放进 AI\\ 目录）";
            return "发现推理程序，但缺 *.gguf 模型文件（放进 AI\\ 目录）";
        }

        public static string Ask(string prompt, int maxTokens, int timeoutMs)
        {
            if (!Ready) return "";
            string pf = Path.Combine(Dir, "_ask.txt");
            try { File.WriteAllText(pf, (prompt == null ? "" : prompt) + "\n", new UTF8Encoding(false)); } catch { return ""; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Exe(),
                    "-m \"" + Model() + "\" -f \"" + pf + "\" -n " + maxTokens + " --temp 0.2 -t 4 -c 2048 -ngl 99 --no-display-prompt");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Dir;
                Process p = Process.Start(psi);
                StringBuilder sb = new StringBuilder();
                System.Threading.Thread th = new System.Threading.Thread(delegate ()
                {
                    try { sb.Append(p.StandardOutput.ReadToEnd()); } catch (Exception dg108_1) { Diag.Swallow("AiLocal.cs:108", dg108_1); }
                });
                th.IsBackground = true;
                th.Start();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception dg112_1) { Diag.Swallow("AiLocal.cs:112", dg112_1); } return "（模型超时）"; }
                th.Join(2000);
                return sb.ToString().Trim();
            }
            catch (Exception ex) { return "（模型调用失败：" + ex.Message + "）"; }
        }
    }
}