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
    class LanXferServer
    {
        TcpListener lis;
        Thread acc;
        volatile bool running;
        int port = 0;
        string inbox, outbox;
        public int Port { get { return port; } }

        // v6.8 局域网剪贴板回调（由窗口层注入，保证在 UI 线程访问剪贴板）
        public Func<string> ClipGet;
        public Action<string> ClipSet;

        public LanXferServer(string inboxDir, string outboxDir)
        {
            inbox = inboxDir; outbox = outboxDir;
            try { if (!Directory.Exists(inbox)) Directory.CreateDirectory(inbox); } catch (Exception dg39_1) { Diag.Swallow("LanXferServer.cs:39", dg39_1); }
            try { if (!Directory.Exists(outbox)) Directory.CreateDirectory(outbox); } catch (Exception dg40_1) { Diag.Swallow("LanXferServer.cs:40", dg40_1); }
        }

        public string Start()
        {
            if (running) return null;
            Exception last = null;
            for (int p = 8765; p <= 8775; p++)
            {
                try
                {
                    TcpListener l = new TcpListener(IPAddress.Any, p);
                    l.Start();
                    lis = l; port = p; running = true;
                    acc = new Thread(AcceptLoop);
                    acc.IsBackground = true;
                    acc.Start();
                    return null;
                }
                catch (Exception ex) { last = ex; }
            }
            return last != null ? last.Message : "端口都被占用";
        }

        public void Stop()
        {
            running = false;
            try { if (lis != null) lis.Stop(); } catch (Exception dg67_1) { Diag.Swallow("LanXferServer.cs:67", dg67_1); }
            lis = null;
        }

        public string[] Urls()
        {
            List<string> u = new List<string>();
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ip.Address.ToString();
                        if (s.StartsWith("169.254")) continue;
                        u.Add("http://" + s + ":" + port + "/");
                    }
                }
            }
            catch (Exception dg89_1) { Diag.Swallow("LanXferServer.cs:89", dg89_1); }
            if (u.Count == 0) u.Add("http://127.0.0.1:" + port + "/");
            return u.ToArray();
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient c = null;
                try { c = lis.AcceptTcpClient(); }
                catch { if (!running) break; else continue; }
                try
                {
                    TcpClient cc = c;
                    Thread th = new Thread(delegate () { Handle(cc); });
                    th.IsBackground = true;
                    th.Start();
                }
                catch { try { c.Close(); } catch (Exception dg108_1) { Diag.Swallow("LanXferServer.cs:108", dg108_1); } }
            }
        }

        static int FindSeq(byte[] data, byte[] needle, int from)
        {
            for (int i = from; i <= data.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++) if (data[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    client.ReceiveTimeout = 30000;
                    byte[] nl = new byte[] { 13, 10, 13, 10 };
                    MemoryStream headBuf = new MemoryStream();
                    byte[] buf = new byte[2048];
                    int headEnd = -1;
                    while (headBuf.Length < 65536)
                    {
                        int n = ns.Read(buf, 0, buf.Length);
                        if (n <= 0) return;
                        headBuf.Write(buf, 0, n);
                        byte[] all = headBuf.ToArray();
                        headEnd = FindSeq(all, nl, 0);
                        if (headEnd >= 0) break;
                    }
                    byte[] raw = headBuf.ToArray();
                    if (headEnd < 0) return;
                    string hs = Encoding.UTF8.GetString(raw, 0, headEnd);
                    string[] lines = hs.Replace("\r\n", "\n").Split('\n');
                    string first = lines.Length > 0 ? lines[0] : "";
                    string[] fp = first.Split(' ');
                    if (fp.Length < 2) return;
                    string method = fp[0].ToUpperInvariant();
                    string path = fp[1];
                    int cl = 0;
                    string ctype = "";
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int c = lines[i].IndexOf(':');
                        if (c <= 0) continue;
                        string k = lines[i].Substring(0, c).Trim().ToLowerInvariant();
                        string v = lines[i].Substring(c + 1).Trim();
                        if (k == "content-length")
                        {
                            int t; if (int.TryParse(v, out t)) cl = t;
                        }
                        if (k == "content-type") ctype = v;
                    }

                    if (method == "GET" && path.StartsWith("/clip"))
                    {
                        if (path.IndexOf("fmt=json") >= 0)
                        {
                            string txt = "";
                            try { if (ClipGet != null) txt = ClipGet(); } catch (Exception dg173_1) { Diag.Swallow("LanXferServer.cs:173", dg173_1); }
                            Send(ns, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"text\":\"" + JsonEsc(txt) + "\"}"));
                        }
                        else
                        {
                            string cur = "";
                            try { if (ClipGet != null) cur = ClipGet(); } catch (Exception dg179_1) { Diag.Swallow("LanXferServer.cs:179", dg179_1); }
                            Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(ClipPage("", cur)));
                        }
                        return;
                    }
                    if (method == "POST" && path.StartsWith("/clip"))
                    {
                        if (cl > 512 * 1024) { Send(ns, 413, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("内容太长（上限 512 KB）")); return; }
                        MemoryStream cbody = new MemoryStream();
                        int chave = raw.Length - (headEnd + 4);
                        if (chave > 0) cbody.Write(raw, headEnd + 4, chave);
                        while (cbody.Length < cl)
                        {
                            int n = ns.Read(buf, 0, (int)Math.Min(buf.Length, cl - cbody.Length));
                            if (n <= 0) break;
                            cbody.Write(buf, 0, n);
                        }
                        string bodyText = Encoding.UTF8.GetString(cbody.ToArray());
                        string val = FormField(bodyText, "text");
                        try { if (ClipSet != null) ClipSet(val); } catch (Exception dg198_1) { Diag.Swallow("LanXferServer.cs:198", dg198_1); }
                        Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(ClipPage("已写入电脑剪贴板 ✓", val)));
                        return;
                    }
                    if (method == "POST" && path.StartsWith("/up"))
                    {
                        if (cl <= 0) { Send(ns, 411, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("没有文件内容")); return; }
                        if (cl > 400 * 1024 * 1024) { Send(ns, 413, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("文件超过 400 MB，请分批发送")); return; }
                        MemoryStream body = new MemoryStream();
                        int have = raw.Length - (headEnd + 4);
                        if (have > 0) body.Write(raw, headEnd + 4, have);
                        while (body.Length < cl)
                        {
                            int n = ns.Read(buf, 0, (int)Math.Min(buf.Length, cl - body.Length));
                            if (n <= 0) break;
                            body.Write(buf, 0, n);
                        }
                        string msg = SaveUpload(ctype, body.ToArray());
                        Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page(msg)));
                        return;
                    }
                    if (method == "GET" && path.StartsWith("/d/"))
                    {
                        string name = Uri.UnescapeDataString(path.Substring(3));
                        name = Path.GetFileName(name);
                        string full = Path.Combine(outbox, name);
                        if (!File.Exists(full)) { Send(ns, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("文件不存在")); return; }
                        byte[] data = File.ReadAllBytes(full);
                        SendFile(ns, name, data);
                        return;
                    }
                    if (method == "GET" || method == "HEAD")
                    {
                        Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page("")));
                        return;
                    }
                    Send(ns, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"));
                }
            }
            catch (Exception dg237_1) { Diag.Swallow("LanXferServer.cs:237", dg237_1); }
        }

        string SaveUpload(string contentType, byte[] body)
        {
            int bi = contentType.IndexOf("boundary=");
            if (bi < 0) return "上传失败：缺少 boundary";
            string boundary = contentType.Substring(bi + 9).Trim();
            if (boundary.StartsWith("\"")) boundary = boundary.Trim('"');
            boundary = boundary.Split(';')[0].Trim();
            byte[] delim = Encoding.ASCII.GetBytes("--" + boundary);
            int saved = 0;
            string last = "";
            int pos = FindSeq(body, delim, 0);
            while (pos >= 0)
            {
                int start = pos + delim.Length;
                // 结尾标记 "--"
                if (start + 1 < body.Length && body[start] == 45 && body[start + 1] == 45) break;
                if (start + 1 < body.Length && body[start] == 13 && body[start + 1] == 10) start += 2;
                int hdrEnd = FindSeq(body, new byte[] { 13, 10, 13, 10 }, start);
                if (hdrEnd < 0) break;
                string partHdr = Encoding.UTF8.GetString(body, start, hdrEnd - start);
                string fname = null;
                int fi = partHdr.IndexOf("filename=\"");
                if (fi >= 0)
                {
                    int fe = partHdr.IndexOf('"', fi + 10);
                    if (fe > fi) fname = partHdr.Substring(fi + 10, fe - fi - 10);
                }
                int dataStart = hdrEnd + 4;
                int next = FindSeq(body, delim, dataStart);
                int dataEnd = next >= 0 ? next : body.Length;
                if (dataEnd - 2 > dataStart && body[dataEnd - 1] == 10 && body[dataEnd - 2] == 13) dataEnd -= 2;
                if (fname != null && fname.Length > 0 && dataEnd > dataStart)
                {
                    string safe = Path.GetFileName(fname);
                    if (safe.Length == 0) safe = "file_" + DateTime.Now.ToString("HHmmss");
                    string dst = Path.Combine(inbox, safe);
                    if (File.Exists(dst))
                        dst = Path.Combine(inbox, DateTime.Now.ToString("HHmmss") + "_" + safe);
                    try
                    {
                        File.WriteAllBytes(dst, SubArray(body, dataStart, dataEnd - dataStart));
                        saved++;
                        last = Path.GetFileName(dst);
                    }
                    catch (Exception dg284_1) { Diag.Swallow("LanXferServer.cs:284", dg284_1); }
                }
                pos = next;
            }
            if (saved == 0) return "没有收到文件";
            return "已收到 " + saved + " 个文件：" + last;
        }

        static byte[] SubArray(byte[] src, int start, int len)
        {
            byte[] r = new byte[len];
            Buffer.BlockCopy(src, start, r, 0, len);
            return r;
        }

        static string JsonEsc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
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

        static string HtmlEsc(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // 从 application/x-www-form-urlencoded 里取一个字段
        static string FormField(string body, string name)
        {
            if (body == null) return "";
            foreach (string kv in body.Split('&'))
            {
                int eq = kv.IndexOf('=');
                string k = eq >= 0 ? kv.Substring(0, eq) : kv;
                if (k != name) continue;
                string v = eq >= 0 ? kv.Substring(eq + 1) : "";
                try { v = Uri.UnescapeDataString(v.Replace("+", " ")); } catch (Exception dg337_1) { Diag.Swallow("LanXferServer.cs:337", dg337_1); }
                return v;
            }
            return "";
        }

        string ClipPage(string msg, string text)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>小工具箱 · 局域网剪贴板</title><style>");
            sb.Append("body{background:#14181f;color:#e6ecf5;font-family:-apple-system,'PingFang SC','Microsoft YaHei',sans-serif;margin:0;padding:18px}");
            sb.Append("h1{font-size:20px;margin:0 0 6px}small{color:#8b98ab}");
            sb.Append(".card{background:#1d232c;border-radius:14px;padding:16px;margin:14px 0}");
            sb.Append("textarea{width:100%;height:180px;background:#0f1319;color:#e6ecf5;border:1px solid #2b323d;border-radius:10px;padding:10px;font-size:15px;box-sizing:border-box}");
            sb.Append("button{background:#0ea5e9;color:#fff;border:0;border-radius:10px;padding:12px 18px;font-size:16px;width:100%;margin-top:10px}");
            sb.Append("a{color:#7dd3fc;text-decoration:none}");
            sb.Append("</style></head><body>");
            sb.Append("<h1>小工具箱 · 局域网剪贴板</h1><small>电脑与手机之间直接传一段文字，不用再发微信</small>");
            if (msg != null && msg.Length > 0) sb.Append("<div class=\"card\" style=\"border-left:4px solid #22c55e\"><b>").Append(HtmlEsc(msg)).Append("</b></div>");
            sb.Append("<div class=\"card\"><b>电脑剪贴板当前内容</b>");
            sb.Append("<form method=\"post\" action=\"/clip\"><textarea name=\"text\">").Append(HtmlEsc(text)).Append("</textarea>");
            sb.Append("<button type=\"submit\">写回电脑剪贴板</button></form></div>");
            sb.Append("<div class=\"card\"><small>刷新本页可看到电脑最新的剪贴板内容；点上面的按钮会把框里的内容写进电脑剪贴板。</small><br>");
            sb.Append("<a href=\"/clip\">重新读取</a> · <a href=\"/\">回到文件互传</a></div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        string Page(string msg)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>小工具箱 · 手机互传</title><style>");
            sb.Append("body{background:#14181f;color:#e6ecf5;font-family:-apple-system,'PingFang SC','Microsoft YaHei',sans-serif;margin:0;padding:18px}");
            sb.Append("h1{font-size:20px;margin:0 0 6px}small{color:#8b98ab}");
            sb.Append(".card{background:#1d232c;border-radius:14px;padding:16px;margin:14px 0}");
            sb.Append("input[type=file]{width:100%;color:#cbd5e1}");
            sb.Append("button{background:#0ea5e9;color:#fff;border:0;border-radius:10px;padding:12px 18px;font-size:16px;width:100%;margin-top:10px}");
            sb.Append("a{color:#7dd3fc;text-decoration:none}li{padding:8px 0;border-bottom:1px solid #2b323d;list-style:none}");
            sb.Append("ul{padding:0;margin:0}");
            sb.Append("</style></head><body>");
            sb.Append("<h1>小工具箱 · 手机互传</h1><small>同一 WiFi 下用手机浏览器打开本页面即可收发文件</small>");
            if (msg != null && msg.Length > 0) sb.Append("<div class=\"card\" style=\"border-left:4px solid #22c55e\"><b>").Append(msg).Append("</b></div>");
            sb.Append("<div class=\"card\"><b>传文件到电脑</b>");
            sb.Append("<form method=\"post\" action=\"/up\" enctype=\"multipart/form-data\"><input type=\"file\" name=\"f\" multiple><button type=\"submit\">上传</button></form></div>");
            sb.Append("<div class=\"card\"><b>局域网剪贴板</b><br><small>在手机上查看/修改电脑剪贴板里的文字</small><br>");
            sb.Append("<a href=\"/clip\">打开剪贴板页 →</a></div>");
            sb.Append("<div class=\"card\"><b>从电脑下载</b><ul>");
            int cnt = 0;
            try
            {
                if (Directory.Exists(outbox))
                {
                    FileInfo[] fs = new DirectoryInfo(outbox).GetFiles("*", SearchOption.TopDirectoryOnly);
                    Array.Sort(fs, delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                    foreach (FileInfo fi in fs)
                    {
                        cnt++;
                        sb.Append("<li><a href=\"/d/").Append(Uri.EscapeDataString(fi.Name)).Append("\">")
                          .Append(fi.Name).Append("</a> <small>").Append((fi.Length / 1048576.0).ToString("0.0")).Append(" MB</small></li>");
                    }
                }
            }
            catch (Exception dg403_1) { Diag.Swallow("LanXferServer.cs:403", dg403_1); }
            if (cnt == 0) sb.Append("<li><small>电脑还没有放入文件（把文件放进「发件-电脑传给手机」目录后刷新本页）</small></li>");
            sb.Append("</ul></div><div class=\"card\"><small>注意：单个文件建议不超过 400 MB；接收到的文件在电脑「互传 / 收件-手机传到电脑」目录里。</small></div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        void SendFile(NetworkStream ns, string name, byte[] data)
        {
            string hdr = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
                + "Content-Disposition: attachment; filename=\"" + Uri.EscapeDataString(name) + "\"\r\n"
                + "Content-Length: " + data.Length + "\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.UTF8.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            ns.Write(data, 0, data.Length);
        }

        void Send(NetworkStream ns, int code, string contentType, byte[] body)
        {
            string reason = code == 200 ? "OK" : (code == 404 ? "Not Found" : (code == 411 ? "Length Required" : "Error"));
            string hdr = "HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: " + contentType
                + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.UTF8.GetBytes(hdr);
            ns.Write(hb, 0, hb.Length);
            ns.Write(body, 0, body.Length);
        }
    }
}