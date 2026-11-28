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
    class PhoneRemoteServer
    {
        TcpListener lis;
        Thread acc;
        volatile bool running;
        int port = 0;
        public Func<string> StatusJson;
        public Func<string, string> Cmd;
        public string ShareDir = "";        // v12.2 文件互传根目录（其下 发送 / 接收）

        public int Port { get { return port; } }

        public string Start()
        {
            if (running) return null;
            Exception last = null;
            for (int p = 8730; p <= 8745; p++)
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
            try { if (lis != null) lis.Stop(); } catch (Exception dg59_1) { Diag.Swallow("PhoneRemoteServer.cs:59", dg59_1); }
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
                        string a = ip.Address.ToString();
                        if (a.StartsWith("169.254")) continue;
                        u.Add("http://" + a + ":" + port + "/");
                    }
                }
            }
            catch (Exception dg81_1) { Diag.Swallow("PhoneRemoteServer.cs:81", dg81_1); }
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
                catch { try { c.Close(); } catch (Exception dg100_1) { Diag.Swallow("PhoneRemoteServer.cs:100", dg100_1); } }
            }
        }

        static void Send(NetworkStream ns, int code, string ctype, byte[] body)
        {
            string head = "HTTP/1.1 " + code + " OK\r\nContent-Type: " + ctype
                + "\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.UTF8.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
            ns.Write(body, 0, body.Length);
            ns.Flush();
        }

        public string SendDir()
        {
            string root = ShareDir != null && ShareDir.Length > 0 ? ShareDir : Path.GetTempPath();
            string d = Path.Combine(root, "发送");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg118_1) { Diag.Swallow("PhoneRemoteServer.cs:118", dg118_1); }
            return d;
        }

        public string RecvDir()
        {
            string root = ShareDir != null && ShareDir.Length > 0 ? ShareDir : Path.GetTempPath();
            string d = Path.Combine(root, "接收");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch (Exception dg126_1) { Diag.Swallow("PhoneRemoteServer.cs:126", dg126_1); }
            return d;
        }

        static string Esc(string t)
        {
            if (t == null) return "";
            return t.Replace("\\", "\\\\").Replace("\"", "'");
        }

        static string Html(string t)
        {
            if (t == null) return "";
            return t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    client.ReceiveTimeout = 20000;
                    MemoryStream headBuf = new MemoryStream();
                    byte[] rbuf = new byte[1024];
                    byte[] all = null;
                    int headEnd = -1;
                    byte[] nl = new byte[] { 13, 10, 13, 10 };
                    while (headBuf.Length < 16384)
                    {
                        int n = ns.Read(rbuf, 0, rbuf.Length);
                        if (n <= 0) return;
                        headBuf.Write(rbuf, 0, n);
                        all = headBuf.ToArray();
                        headEnd = -1;
                        for (int i = 0; i <= all.Length - 4; i++)
                        {
                            if (all[i] == nl[0] && all[i + 1] == nl[1] && all[i + 2] == nl[2] && all[i + 3] == nl[3]) { headEnd = i; break; }
                        }
                        if (headEnd >= 0) break;
                    }
                    if (headEnd < 0 || all == null) return;
                    string hs = Encoding.UTF8.GetString(all, 0, headEnd);
                    string[] lines = hs.Replace("\r\n", "\n").Split('\n');
                    if (lines.Length == 0) return;
                    string[] fp = lines[0].Split(' ');
                    if (fp.Length < 2) return;

                    string method = fp[0].ToUpperInvariant();
                    string path = fp[1];
                    long clen = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string h = lines[i];
                        if (h.Length > 15 && h.ToLowerInvariant().StartsWith("content-length:"))
                        {
                            try { clen = long.Parse(h.Substring(15).Trim()); } catch (Exception dg183_1) { Diag.Swallow("PhoneRemoteServer.cs:183", dg183_1); }
                        }
                    }
                    int restAt = headEnd + 4;
                    int restLen = all.Length - restAt;

                    if (method == "POST" && path.StartsWith("/up"))
                    {
                        Upload(ns, path, all, restAt, restLen, clen);
                        return;
                    }
                    if (method != "GET")
                    {
                        Send(ns, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("GET/POST only"));
                        return;
                    }

                    if (path.StartsWith("/api/status"))
                    {
                        string js = "{}";
                        try { if (StatusJson != null) js = StatusJson(); } catch (Exception dg203_1) { Diag.Swallow("PhoneRemoteServer.cs:203", dg203_1); }
                        Send(ns, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(js));
                        return;
                    }
                    if (path.StartsWith("/api/cmd"))
                    {
                        string cc2 = "";
                        int q = path.IndexOf("c=");
                        if (q >= 0)
                        {
                            cc2 = path.Substring(q + 2);
                            int amp = cc2.IndexOf('&');
                            if (amp >= 0) cc2 = cc2.Substring(0, amp);
                        }
                        if (cc2.Length > 0) cc2 = Uri.UnescapeDataString(cc2);
                        string r = "unknown";
                        try { if (Cmd != null) r = Cmd(cc2); } catch (Exception ex) { r = "err:" + ex.Message; }
                        Send(ns, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"r\":\"" + Esc(r) + "\"}"));
                        return;
                    }
                    if (path.StartsWith("/files"))
                    {
                        Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(FilesPage()));
                        return;
                    }
                    if (path.StartsWith("/dl"))
                    {
                        ServeFile(ns, path);
                        return;
                    }
                    Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page()));
                }
            }
            catch (Exception dg236_1) { Diag.Swallow("PhoneRemoteServer.cs:236", dg236_1); }
        }

        void Upload(NetworkStream ns, string path, byte[] all, int restAt, int restLen, long clen)
        {
            string nm = "upload_" + DateTime.Now.ToString("HHmmss") + ".bin";
            try
            {
                int q = path.IndexOf("name=");
                if (q >= 0)
                {
                    nm = Uri.UnescapeDataString(path.Substring(q + 5));
                    int amp = nm.IndexOf('&');
                    if (amp >= 0) nm = nm.Substring(0, amp);
                }
            }
            catch (Exception dg252_1) { Diag.Swallow("PhoneRemoteServer.cs:252", dg252_1); }
            nm = Path.GetFileName(nm);
            if (nm == null || nm.Length == 0) nm = "upload.bin";
            string dir = RecvDir();
            string dst = Path.Combine(dir, nm);
            try
            {
                int dup = 1;
                string bn = Path.GetFileNameWithoutExtension(nm);
                string ex = Path.GetExtension(nm);
                while (File.Exists(dst))
                {
                    dst = Path.Combine(dir, bn + "_" + dup.ToString() + ex);
                    dup++;
                }
                long left = clen > 0 ? clen : Math.Max(0, restLen);
                using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                {
                    if (restLen > 0)
                    {
                        int writeLen = restLen > left ? (int)left : restLen;
                        fs.Write(all, restAt, writeLen);
                        left -= writeLen;
                    }
                    byte[] b2 = new byte[8192];
                    while (left > 0)
                    {
                        int want = (int)Math.Min(b2.Length, left);
                        int n = ns.Read(b2, 0, want);
                        if (n <= 0) break;
                        fs.Write(b2, 0, n);
                        left -= n;
                    }
                }
                long got = 0;
                try { got = new FileInfo(dst).Length; } catch (Exception dg287_1) { Diag.Swallow("PhoneRemoteServer.cs:287", dg287_1); }
                Send(ns, 200, "application/json; charset=utf-8",
                    Encoding.UTF8.GetBytes("{\"ok\":\"" + Esc(Path.GetFileName(dst)) + "\",\"bytes\":" + got.ToString() + "}"));
            }
            catch (Exception ex)
            {
                try { Send(ns, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ex.Message)); } catch (Exception dg293_1) { Diag.Swallow("PhoneRemoteServer.cs:293", dg293_1); }
            }
        }

        void ServeFile(NetworkStream ns, string path)
        {
            try
            {
                string nm = "";
                int q = path.IndexOf("f=");
                if (q >= 0)
                {
                    nm = Uri.UnescapeDataString(path.Substring(q + 2));
                    int amp = nm.IndexOf('&');
                    if (amp >= 0) nm = nm.Substring(0, amp);
                }
                nm = Path.GetFileName(nm);
                if (nm == null || nm.Length == 0) { Send(ns, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("no file")); return; }
                string fp = Path.Combine(SendDir(), nm);
                if (!File.Exists(fp)) { Send(ns, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found")); return; }
                long len = new FileInfo(fp).Length;
                string head = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"" + Uri.EscapeDataString(nm)
                    + "\"\r\nContent-Length: " + len.ToString() + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                byte[] hb = Encoding.UTF8.GetBytes(head);
                ns.Write(hb, 0, hb.Length);
                using (FileStream fs = File.OpenRead(fp))
                {
                    byte[] b2 = new byte[8192];
                    long left = len;
                    while (left > 0)
                    {
                        int n = fs.Read(b2, 0, b2.Length);
                        if (n <= 0) break;
                        ns.Write(b2, 0, n);
                        left -= n;
                    }
                }
                ns.Flush();
            }
            catch (Exception dg332_1) { Diag.Swallow("PhoneRemoteServer.cs:332", dg332_1); }
        }

        string FilesPage()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>小工具箱 · 文件互传</title><style>");
            sb.Append("*{box-sizing:border-box}body{margin:0;padding:16px;background:#0b0f14;color:#e6edf6;font-family:'Microsoft YaHei',system-ui,sans-serif}");
            sb.Append(".card{max-width:560px;margin:0 auto;background:#141a22;border:1px solid #26313e;border-radius:16px;padding:16px 16px 20px}");
            sb.Append("h1{margin:0 0 4px;font-size:19px}h2{margin:18px 0 8px;font-size:14px;color:#8ea0b6;font-weight:600}");
            sb.Append(".sub{color:#8ea0b6;font-size:12px;margin-bottom:14px;line-height:1.5}");
            sb.Append("input[type=file]{width:100%;padding:12px;border:1px dashed #2f3d4d;border-radius:12px;background:#10161e;color:#cfe0f2;font-size:13px}");
            sb.Append("#msg{min-height:18px;color:#7dd3a0;font-size:13px;margin:10px 0}");
            sb.Append("ul{list-style:none;margin:0;padding:0}li{display:flex;justify-content:space-between;gap:10px;padding:10px 0;border-bottom:1px solid #1e2732;font-size:13px}");
            sb.Append("li a{color:#7cb7ff;text-decoration:none;word-break:break-all}li span{color:#8ea0b6;flex:0 0 auto}");
            sb.Append(".none{color:#6f8199}nav{margin-top:14px;font-size:13px}nav a{color:#8ea0b6}");
            sb.Append("</style></head><body><div class='card'>");
            sb.Append("<h1>文件互传</h1><div class='sub'>手机 → 电脑：选好文件会自动上传到「接收」目录。<br>电脑 → 手机：点文件名直接下载。</div>");
            sb.Append("<input id='f' type='file' multiple><div id='msg'></div>");
            sb.Append("<h2>电脑上的文件（发送目录）</h2><ul>");
            try
            {
                string[] fs = Directory.GetFiles(SendDir());
                Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
                if (fs.Length == 0)
                {
                    sb.Append("<li class='none'>发送目录是空的：把文件放进 手机工具\\发送 就会出现在这里</li>");
                }
                else
                {
                    for (int i = 0; i < fs.Length && i < 200; i++)
                    {
                        string nm = Path.GetFileName(fs[i]);
                        long sz = 0;
                        try { sz = new FileInfo(fs[i]).Length; } catch (Exception dg368_1) { Diag.Swallow("PhoneRemoteServer.cs:368", dg368_1); }
                        string human = sz < 1024 ? sz + " B" : (sz < 1024L * 1024 ? (sz / 1024.0).ToString("0.0") + " KB"
                            : (sz < 1024L * 1024 * 1024 ? (sz / 1024.0 / 1024.0).ToString("0.0") + " MB" : (sz / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB"));
                        sb.Append("<li><a href='/dl?f=").Append(Uri.EscapeDataString(nm)).Append("'>").Append(Html(nm)).Append("</a><span>").Append(human).Append("</span></li>");
                    }
                }
            }
            catch (Exception dg375_1) { Diag.Swallow("PhoneRemoteServer.cs:375", dg375_1); }
            sb.Append("</ul>");
            int recv = 0;
            try { recv = Directory.GetFiles(RecvDir()).Length; } catch (Exception dg378_1) { Diag.Swallow("PhoneRemoteServer.cs:378", dg378_1); }
            sb.Append("<h2>已接收</h2><div class='sub'>接收目录里现在有 ").Append(recv.ToString()).Append(" 个文件。</div>");
            sb.Append("<nav><a href='/'>← 回到遥控面板</a> · <a href='/files'>刷新</a></nav>");
            sb.Append("</div><script>");
            sb.Append("var inp=document.getElementById('f'),msg=document.getElementById('msg');");
            sb.Append("inp.onchange=function(){var fs=inp.files;if(!fs.length)return;var i=0;");
            sb.Append("function next(){if(i>=fs.length){msg.textContent='全部上传完成，共 '+fs.length+' 个';inp.value='';return}");
            sb.Append("var f=fs[i];msg.textContent='上传中 '+(i+1)+'/'+fs.length+'：'+f.name;");
            sb.Append("fetch('/up?name='+encodeURIComponent(f.name),{method:'POST',body:f})");
            sb.Append(".then(function(r){return r.json()}).then(function(d){i++;next()})");
            sb.Append(".catch(function(){msg.textContent='上传失败：'+f.name})}next()};");
            sb.Append("</script></body></html>");
            return sb.ToString();
        }

        string Page()
        {
            string html = @"<!doctype html>
<html><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>小工具箱 · 手机遥控</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;padding:18px;background:#0b0f14;color:#e6edf6;font-family:'Microsoft YaHei',system-ui,-apple-system,'Segoe UI',sans-serif}
.card{max-width:520px;margin:0 auto;background:#141a22;border:1px solid #26313e;border-radius:16px;padding:18px 18px 22px}
h1{margin:0 0 4px;font-size:19px;letter-spacing:.5px}
.sub{color:#8ea0b6;font-size:12px;margin-bottom:16px}
.big{font-size:44px;font-weight:700;letter-spacing:1px;margin:6px 0 2px}
.act{color:#8ea0b6;font-size:13px;margin-bottom:14px}
#bar{height:8px;background:#1d2733;border-radius:6px;overflow:hidden;margin:6px 0 18px}
#bar i{display:block;height:100%;width:0%;background:linear-gradient(90deg,#3b82f6,#22d3ee);transition:width .4s}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}
button{padding:14px 10px;border:0;border-radius:12px;font-size:15px;font-weight:700;color:#fff;background:#384353;font-family:inherit}
button:active{transform:scale(.97)}
.p{background:#2563eb}
.r{background:#b3453a}
.g{background:#1f7a4d}
#msg{min-height:18px;color:#f0a05a;font-size:13px;margin-top:14px}
</style></head>
<body><div class='card'>
<h1>小工具箱 · 遥控</h1>
<div class='sub'>电脑端开着才会响应；数据 1 秒刷新</div>
<div class='big' id='left'>--</div>
<div class='act' id='act'>未在倒计时</div>
<div id='bar'><i id='fill'></i></div>
<div class='grid'>
<button class='p' onclick=""go('add60')"">加 1 分钟</button>
<button onclick=""go('cancel')"">取消任务</button>
<button onclick=""go('lock')"">锁屏</button>
<button onclick=""go('sleep')"">睡眠</button>
<button class='r' onclick=""confirmShutdown('shutdown')"">关机</button>
<button class='r' onclick=""confirmShutdown('restart')"">重启</button>
</div>
<div id='msg'></div>
</div>
<script>
function fmt(s){var h=Math.floor(s/3600),m=Math.floor(s%3600/60),x=s%60;var t='';if(h>0)t+=h+':';t+=(m<10&&h>0?'0':'')+m+':';t+=(x<10?'0':'')+x;return t}
function go(c){var m=document.getElementById('msg');fetch('/api/cmd?c='+c).then(function(r){return r.json()}).then(function(d){m.textContent='已执行：'+c;st()}).catch(function(){m.textContent='执行失败'})}
function confirmShutdown(c){var w=c==='shutdown'?'确定要关机吗？（30 秒后）':'确定要重启吗？（30 秒后）';if(confirm(w))go(c)}
function st(){fetch('/api/status').then(function(r){return r.json()}).then(function(d){
var L=document.getElementById('left'),A=document.getElementById('act'),F=document.getElementById('fill');
if(d.active){L.textContent=fmt(d.left);A.textContent='正在执行：'+d.action+(d.total>0?'（共 '+fmt(d.total)+'）':'');
F.style.width=(d.total>0?Math.min(100,100-d.left/d.total*100):0)+'%'}else{L.textContent='--';A.textContent='未在倒计时';F.style.width='0%'}
}).catch(function(){document.getElementById('act').textContent='连不上电脑端'})}
setInterval(st,1000);st();
</script></body></html>";
            return html;
        }
    }
}