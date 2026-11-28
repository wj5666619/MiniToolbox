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
    class TingForm : Form
    {
        const int PORT = 55124;
        const char FS = '\u0001';

        TcpListener listener;
        TcpClient me;
        readonly List<TcpClient> peers = new List<TcpClient>();
        readonly Dictionary<TcpClient, string> nickOf = new Dictionary<TcpClient, string>();
        readonly object wlock = new object();
        bool netRunning, host;
        string myNick = "";
        string roomCode = "";

        readonly List<string> queue = new List<string>();
        readonly MciTrack player = new MciTrack();
        int cur = -1;
        bool playing;
        long pendPos;
        bool pendPlay;
        MemoryStream recv;
        int recvIdx = -1;
        string cacheDir = "";
        System.Windows.Forms.Timer tick;
        long lastBcast = 0;

        TextBox txtNick, txtCode, txtChat;
        Label lblRoom, lblTrack, lblTime, lblStat, lblMembers;
        ListView lvQueue, lvMembers;
        ListBox lbChat;
        Panel bar;
        Button btnPlay, btnPrev, btnNext, btnAdd, btnAddDir, btnClear, btnSend, btnHost, btnJoin, btnLeave;
        TrackBar tbVol;

        public TingForm()
        {
            Text = "一起听 · 多人同步听歌";
            ClientSize = new Size(920, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = UiKit.A(31, 35, 41);
            ForeColor = UiKit.Txt(Color.White);
            Font = new Font("Microsoft YaHei UI", 9F);
            myNick = Environment.UserName;
            cacheDir = Path.Combine(Path.GetTempPath(), "tingcache");
            try { if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir); } catch (Exception dg70_1) { Diag.Swallow("TingForm.cs:70", dg70_1); }
            BuildUi();

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 400;
            tick.Tick += delegate { OnTickUi(); };
            tick.Start();
            FormClosing += delegate { ExitRoom(); };
        }

        // ---------- UI ----------
        void BuildUi()
        {
            Label t = new Label();
            t.Text = "一起听";
            t.ForeColor = UiKit.Txt(Color.White);
            t.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            t.Location = new Point(18, 14);
            t.AutoSize = true;
            Controls.Add(t);

            Label s = new Label();
            s.Text = "房主开房间放歌，其他人输入房间码加入，全员同步听同一首、可聊天。（需要在同一局域网／能直连）";
            s.ForeColor = UiKit.A(148, 160, 178);
            s.Font = new Font("Microsoft YaHei UI", 8.5F);
            s.Location = new Point(20, 46);
            s.AutoSize = true;
            Controls.Add(s);

            // 左栏：房间
            Panel pnlL = new Panel();
            pnlL.BackColor = UiKit.A(38, 45, 58);
            pnlL.Location = new Point(18, 76);
            pnlL.Size = new Size(300, 546);
            Controls.Add(pnlL);

            pnlL.Controls.Add(MkLabel("昵称", 14, 12));

            txtNick = MkInput(14, 34, 272);
            txtNick.Text = myNick;
            pnlL.Controls.Add(txtNick);

            btnHost = MkBtn("创建房间（我放歌）", 14, 70, 272, 32, UiKit.A(208, 58, 68));
            btnHost.Click += (a, b) => StartHost();
            pnlL.Controls.Add(btnHost);

            lblRoom = MkLabel("房间码：—", 14, 112);
            lblRoom.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblRoom.ForeColor = UiKit.A(255, 196, 120);
            pnlL.Controls.Add(lblRoom);

            txtCode = MkInput(14, 140, 176);
            txtCode.Text = "输入房间码加入";
            pnlL.Controls.Add(txtCode);

            btnJoin = MkBtn("加入", 196, 140, 90, 28, UiKit.A(52, 74, 108));
            btnJoin.Click += (a, b) => JoinRoom(txtCode.Text);
            pnlL.Controls.Add(btnJoin);

            btnLeave = MkBtn("退出房间", 14, 176, 272, 28, UiKit.A(56, 62, 74));
            btnLeave.Click += (a, b) => ExitRoom();
            pnlL.Controls.Add(btnLeave);

            lblMembers = MkLabel("在线成员", 14, 214);
            pnlL.Controls.Add(lblMembers);

            lvMembers = new ListView();
            lvMembers.View = View.Details;
            lvMembers.FullRowSelect = true;
            lvMembers.HeaderStyle = ColumnHeaderStyle.None;
            lvMembers.BorderStyle = BorderStyle.None;
            lvMembers.BackColor = UiKit.A(30, 36, 46);
            lvMembers.ForeColor = UiKit.Txt(Color.White);
            lvMembers.Location = new Point(14, 236);
            lvMembers.Size = new Size(272, 120);
            lvMembers.Columns.Add("成员", 200);
            lvMembers.Columns.Add("角色", 60);
            pnlL.Controls.Add(lvMembers);

            Label c = MkLabel("聊天", 14, 364);
            pnlL.Controls.Add(c);

            lbChat = new ListBox();
            lbChat.BorderStyle = BorderStyle.None;
            lbChat.BackColor = UiKit.A(30, 36, 46);
            lbChat.ForeColor = UiKit.A(206, 216, 230);
            lbChat.Font = new Font("Microsoft YaHei UI", 8.5F);
            lbChat.Location = new Point(14, 386);
            lbChat.Size = new Size(272, 96);
            pnlL.Controls.Add(lbChat);

            txtChat = MkInput(14, 488, 190);
            pnlL.Controls.Add(txtChat);
            btnSend = MkBtn("发送", 210, 488, 76, 26, UiKit.A(52, 74, 108));
            btnSend.Click += (a, b) => SendChat();
            pnlL.Controls.Add(btnSend);
            txtChat.KeyDown += (a, b) => { if (b.KeyCode == Keys.Enter) { SendChat(); b.SuppressKeyPress = true; } };

            // 右栏：播放器 + 曲库
            Panel pnlR = new Panel();
            pnlR.BackColor = UiKit.A(38, 45, 58);
            pnlR.Location = new Point(326, 76);
            pnlR.Size = new Size(576, 546);
            Controls.Add(pnlR);

            lblTrack = MkLabel("未在播放", 16, 14);
            lblTrack.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTrack.AutoSize = false;
            lblTrack.Size = new Size(540, 26);
            pnlR.Controls.Add(lblTrack);

            bar = new Panel();
            bar.BackColor = UiKit.A(26, 31, 40);
            bar.Location = new Point(16, 50);
            bar.Size = new Size(542, 8);
            bar.Paint += BarPaint;
            pnlR.Controls.Add(bar);

            lblTime = MkLabel("00:00 / 00:00", 16, 62);
            lblTime.ForeColor = UiKit.A(150, 162, 180);
            pnlR.Controls.Add(lblTime);

            btnPrev = MkBtn("上一首", 16, 88, 92, 32, UiKit.A(56, 62, 74));
            btnPrev.Click += (a, b) => Skip(-1);
            pnlR.Controls.Add(btnPrev);

            btnPlay = MkBtn("播放", 116, 88, 110, 32, UiKit.A(208, 58, 68));
            btnPlay.Click += (a, b) => TogglePlay();
            pnlR.Controls.Add(btnPlay);

            btnNext = MkBtn("下一首", 234, 88, 92, 32, UiKit.A(56, 62, 74));
            btnNext.Click += (a, b) => Skip(1);
            pnlR.Controls.Add(btnNext);

            tbVol = new TrackBar();
            tbVol.Minimum = 0;
            tbVol.Maximum = 1000;
            tbVol.Value = 800;
            tbVol.TickStyle = TickStyle.None;
            tbVol.Location = new Point(340, 90);
            tbVol.Size = new Size(140, 26);
            tbVol.ValueChanged += (a, b) => player.Volume(tbVol.Value);
            pnlR.Controls.Add(tbVol);

            btnAdd = MkBtn("添加歌曲", 16, 134, 100, 30, UiKit.A(52, 74, 108));
            btnAdd.Click += (a, b) => AddFiles();
            pnlR.Controls.Add(btnAdd);

            btnAddDir = MkBtn("添加文件夹", 124, 134, 112, 30, UiKit.A(56, 62, 74));
            btnAddDir.Click += (a, b) => AddFolder();
            pnlR.Controls.Add(btnAddDir);

            btnClear = MkBtn("清空曲库", 244, 134, 96, 30, UiKit.A(56, 62, 74));
            btnClear.Click += (a, b) => { queue.Clear(); cur = -1; player.Close(); RefreshQueue(); UpdateTrack(); if (host) BroadcastQueue(); };
            pnlR.Controls.Add(btnClear);

            lvQueue = new ListView();
            lvQueue.View = View.Details;
            lvQueue.FullRowSelect = true;
            lvQueue.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lvQueue.BorderStyle = BorderStyle.None;
            lvQueue.BackColor = UiKit.A(30, 36, 46);
            lvQueue.ForeColor = UiKit.Txt(Color.White);
            lvQueue.Location = new Point(16, 174);
            lvQueue.Size = new Size(542, 300);
            lvQueue.Columns.Add("#", 44);
            lvQueue.Columns.Add("歌曲", 330);
            lvQueue.Columns.Add("来源", 164);
            lvQueue.DoubleClick += (a, b) => PlaySelected();
            pnlR.Controls.Add(lvQueue);

            lblStat = MkLabel("", 16, 484);
            lblStat.ForeColor = UiKit.A(150, 162, 180);
            pnlR.Controls.Add(lblStat);

            Label tip = new Label();
            tip.Text = "提示：房主负责放歌与切歌，其他人自动跟播；房主切歌时访客会自动从房主那里拉取这首音频。支持 mp3 / wav / flac(MCI 能解的格式)。";
            tip.ForeColor = UiKit.A(150, 162, 182);
            tip.Font = new Font("Microsoft YaHei UI", 8F);
            tip.Location = new Point(16, 506);
            tip.Size = new Size(542, 34);
            pnlR.Controls.Add(tip);
        }

        Label MkLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = UiKit.A(150, 162, 180);
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        TextBox MkInput(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, 24);
            t.BackColor = UiKit.A(26, 31, 40);
            t.ForeColor = UiKit.Txt(Color.White);
            t.BorderStyle = BorderStyle.FixedSingle;
            return t;
        }

        Button MkBtn(string text, int x, int y, int w, int h, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Standard;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = UiKit.Txt(Color.White);
            b.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
        }

        void BarPaint(object s, PaintEventArgs e)
        {
            e.Graphics.Clear(UiKit.A(26, 31, 40));
            long len = player.Length();
            if (len <= 0) return;
            long pos = player.Position();
            int w = (int)((double)pos / len * bar.Width);
            if (w > 0)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush gb = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, w), bar.Height), UiKit.A(248, 84, 94), UiKit.A(255, 176, 92), 0f))
                    e.Graphics.FillRectangle(gb, 0, 0, w, bar.Height);
            }
        }

        // ---------- 房间 ----------
        void StartHost()
        {
            ExitRoom();
            myNick = txtNick.Text.Trim().Length == 0 ? Environment.UserName : txtNick.Text.Trim();
            try
            {
                listener = new TcpListener(IPAddress.Any, PORT);
                listener.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "房间开不起来（端口 " + PORT + " 可能被占用）：" + ex.Message, "一起听", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            host = true;
            netRunning = true;
            string ip = LocalIp();
            roomCode = MakeCode(ip, PORT);
            lblRoom.Text = "房间码：" + roomCode + "（" + ip + "）";
            Thread acc = new Thread(AcceptLoop);
            acc.IsBackground = true;
            acc.Start();
            SetStat("房间已创建，等别人加入：" + ip + ":" + PORT);
            btnHost.Enabled = false;
            btnJoin.Enabled = false;
        }

        void AcceptLoop()
        {
            while (netRunning && listener != null)
            {
                TcpClient c = null;
                try { c = listener.AcceptTcpClient(); } catch { break; }
                if (c == null) continue;
                lock (wlock) { peers.Add(c); }
                Thread th = new Thread(delegate () { ReaderHost(c); });
                th.IsBackground = true;
                th.Start();
            }
        }

        void JoinRoom(string code)
        {
            string ip; int port;
            if (!ParseCode(code.Trim(), out ip, out port))
            {
                MessageBox.Show(this, "房间码看不懂，请让房主重新复制一次（或直接输入 IP:端口）。", "一起听", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ExitRoom();
            myNick = txtNick.Text.Trim().Length == 0 ? Environment.UserName : txtNick.Text.Trim();
            try
            {
                me = new TcpClient();
                me.Connect(ip, port);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "连不上房主 " + ip + ":" + port + "：" + ex.Message + "\n（同一局域网？防火墙放行了吗？）", "一起听", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            host = false;
            netRunning = true;
            roomCode = code.Trim();
            lblRoom.Text = "已加入：" + ip + ":" + port;
            Thread th = new Thread(ReaderGuest);
            th.IsBackground = true;
            th.Start();
            Send("H" + FS + B64(myNick));
            SetStat("已连上房主，等待房主开始放歌…");
            btnHost.Enabled = false;
            btnJoin.Enabled = false;
        }

        void ExitRoom()
        {
            netRunning = false;
            try { if (listener != null) listener.Stop(); } catch (Exception dg384_1) { Diag.Swallow("TingForm.cs:384", dg384_1); }
            listener = null;
            try { if (me != null) me.Close(); } catch (Exception dg386_1) { Diag.Swallow("TingForm.cs:386", dg386_1); }
            me = null;
            lock (wlock)
            {
                for (int i = 0; i < peers.Count; i++) { try { peers[i].Close(); } catch (Exception dg390_1) { Diag.Swallow("TingForm.cs:390", dg390_1); } }
                peers.Clear();
                nickOf.Clear();
            }
            host = false;
            roomCode = "";
            lblRoom.Text = "房间码：—";
            btnHost.Enabled = true;
            btnJoin.Enabled = true;
            while (lvMembers.Items.Count > 1) lvMembers.Items.RemoveAt(lvMembers.Items.Count - 1);
            SetStat("已退出房间。");
        }

        static string LocalIp()
        {
            try
            {
                IPHostEntry he = Dns.GetHostEntry(Dns.GetHostName());
                for (int i = 0; i < he.AddressList.Length; i++)
                {
                    IPAddress a = he.AddressList[i];
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
                }
            }
            catch (Exception dg414_1) { Diag.Swallow("TingForm.cs:414", dg414_1); }
            return "127.0.0.1";
        }

        static string MakeCode(string ip, int p)
        {
            string s = ip + ":" + p;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        static bool ParseCode(string code, out string ip, out int p)
        {
            ip = ""; p = PORT;
            string v = code.Replace('-', '+').Replace('_', '/');
            while (v.Length % 4 != 0) v += "=";
            try
            {
                string s = Encoding.UTF8.GetString(Convert.FromBase64String(v));
                int i = s.LastIndexOf(':');
                if (i <= 0) return false;
                ip = s.Substring(0, i);
                int.TryParse(s.Substring(i + 1), out p);
                return ip.Length > 0 && p > 0;
            }
            catch (Exception dg438_1) { Diag.Swallow("TingForm.cs:438", dg438_1); }
            // 也允许直接输 IP:端口
            int j = code.LastIndexOf(':');
            if (j > 0)
            {
                ip = code.Substring(0, j);
                int.TryParse(code.Substring(j + 1), out p);
                return ip.Length > 0 && p > 0;
            }
            return false;
        }

        // ---------- 协议 ----------
        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        static string B64D(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }

        void WriteLine(TcpClient c, string line)
        {
            try
            {
                NetworkStream s = c.GetStream();
                byte[] b = Encoding.UTF8.GetBytes(line + "\n");
                lock (wlock) { s.Write(b, 0, b.Length); }
            }
            catch (Exception dg462_1) { Diag.Swallow("TingForm.cs:462", dg462_1); }
        }

        void Send(string line) { if (me != null) WriteLine(me, line); }

        void Broadcast(string line)
        {
            List<TcpClient> list;
            lock (wlock) { list = new List<TcpClient>(peers); }
            for (int i = 0; i < list.Count; i++) WriteLine(list[i], line);
        }

        void ReaderHost(TcpClient c)
        {
            StreamReader sr = null;
            try { sr = new StreamReader(c.GetStream(), Encoding.UTF8); } catch { return; }
            while (netRunning)
            {
                string line = null;
                try { line = sr.ReadLine(); } catch { break; }
                if (line == null) break;
                try { HostHandle(c, line); } catch (Exception dg483_1) { Diag.Swallow("TingForm.cs:483", dg483_1); }
            }
            lock (wlock) { peers.Remove(c); nickOf.Remove(c); }
            BroadcastMembers();
        }

        void ReaderGuest()
        {
            TcpClient c = me;
            StreamReader sr = null;
            try { sr = new StreamReader(c.GetStream(), Encoding.UTF8); } catch { return; }
            while (netRunning && c == me)
            {
                string line = null;
                try { line = sr.ReadLine(); } catch { break; }
                if (line == null) break;
                try { GuestHandle(line); } catch (Exception dg499_1) { Diag.Swallow("TingForm.cs:499", dg499_1); }
            }
            if (c == me) BeginInvoke((Action)delegate { SetStat("和房主的连接断了。"); });
        }

        string CurNick(TcpClient c)
        {
            string n;
            lock (wlock) { n = nickOf.ContainsKey(c) ? nickOf[c] : "某人"; }
            return n;
        }

        void HostHandle(TcpClient c, string line)
        {
            string[] f = line.Split(FS);
            if (f[0] == "H" && f.Length > 1)
            {
                lock (wlock) { nickOf[c] = B64D(f[1]); }
                BroadcastMembers();
                WriteLine(c, QueueLine());
                SendState(c);
            }
            else if (f[0] == "G" && f.Length > 1)
            {
                int idx = -1;
                int.TryParse(f[1], out idx);
                SendFile(c, idx);
            }
            else if (f[0] == "C" && f.Length > 1)
            {
                string nick = CurNick(c);
                Broadcast("C" + FS + B64(nick) + FS + f[1]);
                Ui(delegate { AddChat(nick, B64D(f[1])); });
            }
        }

        void GuestHandle(string line)
        {
            string[] f = line.Split(FS);
            if (f[0] == "M" && f.Length > 1) { Ui(delegate { ShowMembers(f[1]); }); }
            else if (f[0] == "Q" && f.Length > 1)
            {
                string raw = f[1];
                Ui(delegate { ApplyQueue(raw); });
            }
            else if (f[0] == "S" && f.Length > 3)
            {
                int idx = -1; bool pl = false; long pos = 0;
                int.TryParse(f[1], out idx);
                pl = f[2] == "1";
                long.TryParse(f[3], out pos);
                GuestState(idx, pl, pos);
            }
            else if (f[0] == "C" && f.Length > 2)
            {
                string nick = B64D(f[1]);
                string text = B64D(f[2]);
                Ui(delegate { AddChat(nick, text); });
            }
            else if (f[0] == "F" && f.Length > 2)
            {
                int idx = -1;
                int.TryParse(f[1], out idx);
                recvIdx = idx;
                recv = new MemoryStream();
            }
            else if (f[0] == "D" && f.Length > 1)
            {
                if (recv != null)
                {
                    byte[] b = Convert.FromBase64String(f[1]);
                    recv.Write(b, 0, b.Length);
                }
            }
            else if (f[0] == "E")
            {
                FinishReceive();
            }
        }

        void GuestState(int idx, bool pl, long pos)
        {
            pendPos = pos;
            pendPlay = pl;
            if (idx != cur)
            {
                cur = idx;
                player.Close();
                UpdateTrack();
                RefreshQueue();
                if (idx >= 0 && idx < queue.Count)
                {
                    // 向房主要这首歌
                    Send("G" + FS + idx);
                    Ui(delegate { SetStat("正在从房主拉取音频…"); });
                }
                else if (idx < 0) Ui(delegate { SetStat("房主停下了。"); });
            }
            else if (cur >= 0 && player.Path.Length > 0)
            {
                long local = player.Position();
                if (Math.Abs(local - pos) > 500) player.PlayFrom(pos);
                string mode = player.Mode();
                if (pendPlay && mode != "playing") player.Resume();
                else if (!pendPlay && mode == "playing") player.Pause();
                playing = pendPlay;
                UpdateTrack();
            }
        }

        void FinishReceive()
        {
            if (recv == null || recvIdx < 0) return;
            MemoryStream ms = recv;
            recv = null;
            int idx = recvIdx;
            string name = idx < queue.Count ? Path.GetFileName(queue[idx]) : ("track" + idx + ".mp3");
            string file = Path.Combine(cacheDir, "ting_" + idx + "_" + name);
            try
            {
                File.WriteAllBytes(file, ms.ToArray());
                ms.Dispose();
                // 访客用自己的缓存路径换掉显示名
                while (queue.Count <= idx) queue.Add(file);
                queue[idx] = file;
                player.Open(file);
                player.Volume(tbVol.Value);
                player.PlayFrom(pendPos);
                if (!pendPlay) player.Pause();
                playing = pendPlay;
                Ui(delegate { SetStat("已同步：" + name); UpdateTrack(); RefreshQueue(); });
            }
            catch (Exception ex) { Ui(delegate { SetStat("缓存音频失败：" + ex.Message); }); }
        }

        void SendFile(TcpClient c, int idx)
        {
            if (idx < 0 || idx >= queue.Count) return;
            string path = queue[idx];
            // MCI 正在播放这个文件时，源文件可能被独占，改成从「分发副本」读，避免读失败
            string serve = ServeCopy(path);
            if (serve.Length > 0) path = serve;
            if (!File.Exists(path)) return;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Length > 300L * 1024 * 1024) return;
                byte[] data = ReadShared(path);
                if (data == null || data.Length == 0) return;
                WriteLine(c, "F" + FS + idx + FS + data.Length);
                const int CH = 48 * 1024;
                for (int o = 0; o < data.Length; o += CH)
                {
                    int n = Math.Min(CH, data.Length - o);
                    byte[] seg = new byte[n];
                    Array.Copy(data, o, seg, 0, n);
                    WriteLine(c, "D" + FS + Convert.ToBase64String(seg));
                }
                WriteLine(c, "E" + FS + idx);
            }
            catch (Exception dg659_1) { Diag.Swallow("TingForm.cs:659", dg659_1); }
        }

        // 允许共享读：源文件被播放器占着也能读出来
        static byte[] ReadShared(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buf = new byte[fs.Length];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < buf.Length) Array.Resize(ref buf, read);
                    return buf;
                }
            }
            catch
            {
                try { return File.ReadAllBytes(path); } catch { return null; }
            }
        }

        // 分发副本：曲目开始播放前先落一份到缓存目录，之后访客要文件就发这份，彻底绕开播放锁
        string ServeCopy(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                FileInfo fi = new FileInfo(path);
                string key = Math.Abs((path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks).GetHashCode()).ToString();
                string dst = Path.Combine(cacheDir, "serve_" + key + Path.GetExtension(path));
                if (!File.Exists(dst) || new FileInfo(dst).Length != fi.Length)
                {
                    File.Copy(path, dst, true);
                }
                return dst;
            }
            catch { return ""; }
        }

        // ---------- 曲库 / 播放 ----------
        string QueueLine()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Q");
            List<string> q;
            lock (queue) { q = new List<string>(queue); }
            for (int i = 0; i < q.Count; i++)
            {
                sb.Append(FS).Append(B64(Path.GetFileNameWithoutExtension(q[i])));
            }
            return sb.ToString();
        }

        void BroadcastQueue() { if (host) Broadcast(QueueLine()); }
        void BroadcastMembers()
        {
            if (!host) return;
            StringBuilder sb = new StringBuilder("M" + FS);
            sb.Append(B64(myNick) + "~房主");
            List<TcpClient> ls; Dictionary<TcpClient, string> nk;
            lock (wlock) { ls = new List<TcpClient>(peers); nk = new Dictionary<TcpClient, string>(nickOf); }
            for (int i = 0; i < ls.Count; i++)
            {
                string n = nk.ContainsKey(ls[i]) ? nk[ls[i]] : "某人";
                sb.Append(";" + B64(n) + "~听众");
            }
            string line = sb.ToString();
            Broadcast(line);
            Ui(delegate { ShowMembers(line); });
        }

        void SendState(TcpClient c)
        {
            long pos = player.Path.Length > 0 ? player.Position() : 0;
            WriteLine(c, "S" + FS + cur + FS + (playing ? "1" : "0") + FS + pos);
        }

        void TogglePlay()
        {
            if (queue.Count == 0) { SetStat("曲库是空的，先添加歌曲。"); return; }
            if (cur < 0) { cur = 0; LoadCur(); return; }
            playing = !playing;
            if (playing) player.Resume(); else player.Pause();
            UpdateTrack();
            if (host) SendState(null);
        }

        void Skip(int d)
        {
            if (queue.Count == 0) return;
            cur = cur < 0 ? 0 : (cur + d + queue.Count) % queue.Count;
            LoadCur();
        }

        void PlaySelected()
        {
            if (lvQueue.SelectedIndices.Count == 0) return;
            if (!host && cur >= 0) { SetStat("房主在控场，切歌请让房主操作。"); return; }
            cur = lvQueue.SelectedIndices[0];
            LoadCur();
        }

        void LoadCur()
        {
            if (cur < 0 || cur >= queue.Count) return;
            ServeCopy(queue[cur]);          // 起播前先落一份分发副本，访客来取时不会被播放锁挡住
            player.Open(queue[cur]);
            player.Volume(tbVol.Value);
            player.PlayFrom(0);
            playing = true;
            UpdateTrack();
            RefreshQueue();
            if (host) { BroadcastQueue(); Broadcast("S" + FS + cur + FS + "1" + FS + "0"); }
        }

        void AddFiles()
        {
            OpenFileDialog of = new OpenFileDialog();
            of.Multiselect = true;
            of.Filter = "音频文件|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.ogg|所有文件|*.*";
            if (of.ShowDialog(this) != DialogResult.OK) return;
            for (int i = 0; i < of.FileNames.Length; i++)
            {
                if (!queue.Contains(of.FileNames[i])) queue.Add(of.FileNames[i]);
            }
            RefreshQueue();
            BroadcastQueue();
        }

        void AddFolder()
        {
            FolderBrowserDialog fb = new FolderBrowserDialog();
            if (fb.ShowDialog(this) != DialogResult.OK) return;
            string root = fb.SelectedPath;
            SetStat("正在扫描文件夹…");
            // 大目录递归扫描放后台，界面不冻结；扫完再一次性入队
            Thread t = new Thread(delegate()
            {
                List<string> found = new List<string>();
                try
                {
                    string[] exts = new string[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".ogg" };
                    foreach (string f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (Array.IndexOf(exts, ext) >= 0) found.Add(f);
                    }
                }
                catch (Exception dg814_1) { Diag.Swallow("TingForm.cs:814", dg814_1); }
                try
                {
                    this.BeginInvoke((Action)delegate
                    {
                        int n = 0;
                        for (int i = 0; i < found.Count; i++)
                        {
                            if (queue.Contains(found[i])) continue;
                            queue.Add(found[i]);
                            n++;
                        }
                        RefreshQueue();
                        BroadcastQueue();
                        SetStat("已加入 " + n + " 首。");
                    });
                }
                catch (Exception dg831_1) { Diag.Swallow("TingForm.cs:831", dg831_1); }
            });
            t.IsBackground = true;
            t.Start();
        }

        void RefreshQueue()
        {
            lvQueue.Items.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                ListViewItem li = new ListViewItem((i + 1).ToString());
                li.SubItems.Add(Path.GetFileNameWithoutExtension(queue[i]));
                li.SubItems.Add(i == cur ? "正在播放" : "本地");
                if (i == cur) li.ForeColor = UiKit.A(255, 176, 92);
                lvQueue.Items.Add(li);
            }
        }

        void ApplyQueue(string raw)
        {
            string[] parts = raw.Split(FS);
            List<string> names = new List<string>();
            for (int i = 1; i < parts.Length; i++) names.Add(B64D(parts[i]));
            if (queue.Count != names.Count)
            {
                // 访客侧曲库只保留占位名，音频在需要时拉取
                queue.Clear();
                for (int i = 0; i < names.Count; i++) queue.Add(names[i]);
                RefreshQueue();
            }
        }

        void ShowMembers(string raw)
        {
            string[] parts = raw.Split(FS);
            string list = parts.Length > 1 ? parts[1] : "";
            lvMembers.Items.Clear();
            string[] who = list.Split(';');
            for (int i = 0; i < who.Length; i++)
            {
                if (who[i].Length == 0) continue;
                string[] kv = who[i].Split('~');
                ListViewItem li = new ListViewItem(B64D(kv[0]));
                li.SubItems.Add(kv.Length > 1 ? kv[1] : "");
                if (kv.Length > 1 && kv[1] == "房主") li.ForeColor = UiKit.A(255, 176, 92);
                lvMembers.Items.Add(li);
            }
        }

        void SendChat()
        {
            string text = txtChat.Text.Trim();
            if (text.Length == 0) return;
            txtChat.Text = "";
            AddChat(myNick, text);
            if (netRunning) Send("C" + FS + B64(text));
        }

        void AddChat(string nick, string text)
        {
            lbChat.Items.Add("[" + DateTime.Now.ToString("HH:mm") + "] " + nick + "：" + text);
            lbChat.TopIndex = lbChat.Items.Count - 1;
        }

        void UpdateTrack()
        {
            if (cur >= 0 && cur < queue.Count)
            {
                lblTrack.Text = "♪ " + Path.GetFileNameWithoutExtension(queue[cur]);
                btnPlay.Text = playing ? "暂停" : "播放";
            }
            else
            {
                lblTrack.Text = "未在播放";
                btnPlay.Text = "播放";
            }
        }

        void OnTickUi()
        {
            long len = player.Length();
            long pos = player.Position();
            lblTime.Text = Fmt(pos) + " / " + Fmt(len);
            bar.Invalidate();
            // 播完了自动下一首（房主负责推进）
            if (host && playing && len > 0 && pos >= len - 120)
            {
                string m = player.Mode();
                if (m == "stopped" || m.Length == 0) Skip(1);
            }
            if (host && netRunning && Environment.TickCount - lastBcast > 900)
            {
                lastBcast = Environment.TickCount;
                Broadcast("S" + FS + cur + FS + (playing ? "1" : "0") + FS + pos);
            }
        }

        static string Fmt(long ms)
        {
            if (ms < 0) ms = 0;
            long s = ms / 1000;
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        void SetStat(string s) { lblStat.Text = s; }

        void Ui(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch (Exception dg946_1) { Diag.Swallow("TingForm.cs:946", dg946_1); }
        }

        // ---------- 自检（/tingtest）：开房 + 生成测试音频 + 起播，供外部脚本验证协议 ----------
        public void TestHost()
        {
            try
            {
                string f1 = MakeTestWav("ting_test_a.wav", 440);
                string f2 = MakeTestWav("ting_test_b.wav", 660);
                if (f1 != null) queue.Add(f1);
                if (f2 != null) queue.Add(f2);
                StartHost();
                RefreshQueue();
                if (queue.Count > 0) { cur = 0; LoadCur(); }
                SetStat("自检：房间已开，曲库 " + queue.Count + " 首。");
            }
            catch (Exception ex) { SetStat("自检失败：" + ex.Message); }
        }

        static string MakeTestWav(string name, int hz)
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), name);
                int rate = 22050, secs = 3, ch = 1, bits = 16;
                int n = rate * secs;
                using (FileStream fs = new FileStream(p, FileMode.Create, FileAccess.Write))
                {
                    BinaryWriter bw = new BinaryWriter(fs);
                    bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                    bw.Write(36 + n * 2);
                    bw.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                    bw.Write(16); bw.Write((short)1); bw.Write((short)ch);
                    bw.Write(rate); bw.Write(rate * ch * bits / 8);
                    bw.Write((short)(ch * bits / 8)); bw.Write((short)bits);
                    bw.Write(new char[] { 'd', 'a', 't', 'a' });
                    bw.Write(n * 2);
                    for (int i = 0; i < n; i++)
                    {
                        short v = (short)(Math.Sin(2 * Math.PI * hz * i / rate) * 8000);
                        bw.Write(v);
                    }
                    bw.Flush();
                }
                return p;
            }
            catch { return null; }
        }
    }
}