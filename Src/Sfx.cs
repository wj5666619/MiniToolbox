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
    static class Sfx
    {
        const int SR = 44100;
        static bool on = true;
        static string storePath;
        static SoundPlayer pClick, pError, pOk, pBell, pAlarm, pDrop, pStop, pSwitch, pWarn, pSave, pCopy, pTick3, pLaunch, pDone;
        static bool nightQuiet = true;      // 22:00–08:00 只保留"重要"提示音
        // v19.0 发声级别：0 全部 / 1 只重要（点击、切页、复制这类辅助音静音） / 2 完全静默
        static int level = 1;
        static string levelPath;
        // v19.0 夜间时段可配（音效夜间时段.txt，形如 22:30-07:30），默认 22:00–08:00
        static int qsMin = 22 * 60, qeMin = 8 * 60;
        static string nightPath;
        static string volPath;
        static int vol = 1;
        static double gain = 0.82;
        static readonly double[] Gains = new double[] { 0.62, 0.82, 1.00 };
        static readonly string[] VolNames = new string[] { "轻", "中", "响" };
        // v18.6 三套音色：0 清亮（音高略高、更亮）/ 1 标准 / 2 沉稳（音高略低、尾巴更长）
        static int pack = 1;
        static string packPath;
        public const int PACK_COUNT = 3;
        public static readonly string[] PackNames = new string[] { "清亮", "标准", "沉稳" };
        static readonly double[] PackPitch = new double[] { 1.09, 1.00, 0.87 };
        static readonly double[] PackCut = new double[] { 0.42, 0.30, 0.21 };
        static readonly Dictionary<string, long> lastAt = new Dictionary<string, long>();
        // v19.14 分事件开关 + 专注模式：按声音语义分别关，不再只有总开关和三档音量
        static string evPath, focusPath;
        static bool focus;
        static readonly Dictionary<string, bool> evOn = new Dictionary<string, bool>();
        public static readonly string[] EventKeys = new string[] { "click", "switch", "copy", "drop", "save", "ok", "done", "warn", "error", "alarm", "stop", "launch", "bell", "tick3" };
        public static readonly string[] EventNames = new string[] { "点击", "切页", "复制", "投递", "保存", "成功", "完成", "警告", "失败", "闹钟", "停止", "启动", "提示", "倒计时" };

        public static bool On { get { return on; } }

        public static void Init(string baseDir)
        {
            try
            {
                storePath = Path.Combine(baseDir, "音效.txt");
                if (File.Exists(storePath))
                {
                    string s = File.ReadAllText(storePath).Trim().ToLowerInvariant();
                    on = !(s == "off" || s == "0" || s == "false" || s == "关");
                }
            }
            catch (Exception dg63_1) { Diag.Swallow("Sfx.cs:63", dg63_1); }
            try
            {
                volPath = Path.Combine(baseDir, "音效音量.txt");
                if (File.Exists(volPath))
                {
                    string s2 = File.ReadAllText(volPath).Trim().ToLowerInvariant();
                    if (s2 == "0" || s2 == "轻" || s2 == "light") vol = 0;
                    else if (s2 == "2" || s2 == "响" || s2 == "loud") vol = 2;
                    else vol = 1;
                }
            }
            catch (Exception dg75_1) { Diag.Swallow("Sfx.cs:75", dg75_1); }
            gain = Gains[vol];
            try
            {
                nightPath = Path.Combine(baseDir, "音效夜间.txt");
                if (File.Exists(nightPath))
                {
                    string s3 = File.ReadAllText(nightPath).Trim().ToLowerInvariant();
                    nightQuiet = !(s3 == "off" || s3 == "0" || s3 == "false" || s3 == "关");
                }
            }
            catch (Exception dg86_1) { Diag.Swallow("Sfx.cs:86", dg86_1); }
            // v19.0：夜间时段 "22:30-07:30"（旧格式 on/off 照旧兼容）
            try
            {
                string tp = Path.Combine(baseDir, "音效夜间时段.txt");
                if (File.Exists(tp))
                {
                    string s5 = File.ReadAllText(tp).Trim();
                    string[] two = s5.Split('-');
                    if (two.Length == 2)
                    {
                        int a = ParseHm(two[0].Trim()), b = ParseHm(two[1].Trim());
                        if (a >= 0 && b >= 0) { qsMin = a; qeMin = b; }
                    }
                }
            }
            catch (Exception dg102_1) { Diag.Swallow("Sfx.cs:102", dg102_1); }
            // v19.0：发声级别（音效级别.txt：0/全部 1/只重要 2/静默）
            try
            {
                levelPath = Path.Combine(baseDir, "音效级别.txt");
                if (File.Exists(levelPath))
                {
                    string s6 = File.ReadAllText(levelPath).Trim().ToLowerInvariant();
                    if (s6 == "2" || s6 == "silent" || s6 == "静默") level = 2;
                    else if (s6 == "1" || s6 == "important" || s6 == "仅重要" || s6 == "只重要") level = 1;
                    else level = 0;
                }
            }
            catch (Exception dg115_1) { Diag.Swallow("Sfx.cs:115", dg115_1); }
            // v18.6 音色档位：清亮 / 标准 / 沉稳，落盘 音效音色.txt
            try
            {
                packPath = Path.Combine(baseDir, "音效音色.txt");
                if (File.Exists(packPath))
                {
                    string s4 = File.ReadAllText(packPath).Trim().ToLowerInvariant();
                    if (s4 == "0" || s4 == "清亮" || s4 == "bright") pack = 0;
                    else if (s4 == "2" || s4 == "沉稳" || s4 == "deep") pack = 2;
                    else pack = 1;
                }
            }
            catch (Exception dg128_1) { Diag.Swallow("Sfx.cs:128", dg128_1); }
            // v19.14 分事件开关（音效事件.txt：key=on / key=off），未知键忽略
            for (int i = 0; i < EventKeys.Length; i++) evOn[EventKeys[i]] = true;
            try
            {
                evPath = Path.Combine(baseDir, "音效事件.txt");
                if (File.Exists(evPath))
                {
                    foreach (string ln in File.ReadAllLines(evPath, Encoding.UTF8))
                    {
                        string[] a = ln.Split('=');
                        if (a.Length != 2) continue;
                        string k = a[0].Trim();
                        if (!evOn.ContainsKey(k)) continue;
                        string v = a[1].Trim().ToLowerInvariant();
                        evOn[k] = !(v == "off" || v == "0" || v == "false" || v == "关");
                    }
                }
            }
            catch (Exception dg129a_1) { Diag.Swallow("Sfx.cs:129a", dg129a_1); }
            // 专注模式：只留失败/警告/闹钟/提示，企业现场可配合策略固定
            try
            {
                focusPath = Path.Combine(baseDir, "音效专注.txt");
                if (File.Exists(focusPath))
                {
                    string sf = File.ReadAllText(focusPath).Trim().ToLowerInvariant();
                    focus = !(sf == "off" || sf == "0" || sf == "false" || sf == "关");
                }
            }
            catch (Exception dg129b_1) { Diag.Swallow("Sfx.cs:129b", dg129b_1); }
            if (on) { try { BuildPlayers(); } catch (Exception dg129_1) { Diag.Swallow("Sfx.cs:129", dg129_1); } }
        }

        public static bool EventOn(string key)
        {
            if (key == null || key.Length == 0) return true;
            lock (evOn) { return evOn.ContainsKey(key) ? evOn[key] : true; }
        }

        public static void SetEventOn(string key, bool v)
        {
            if (key == null || key.Length == 0) return;
            lock (evOn) { evOn[key] = v; }
            try
            {
                if (evPath != null && evPath.Length > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < EventKeys.Length; i++)
                        sb.AppendLine(EventKeys[i] + "=" + (EventOn(EventKeys[i]) ? "on" : "off"));
                    File.WriteAllText(evPath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception dg129c_1) { Diag.Swallow("Sfx.cs:129c", dg129c_1); }
        }

        public static bool Focus { get { return focus; } }

        public static void SetFocus(bool v)
        {
            focus = v;
            try { if (focusPath != null && focusPath.Length > 0) File.WriteAllText(focusPath, v ? "on" : "off", new UTF8Encoding(false)); }
            catch (Exception dg129d_1) { Diag.Swallow("Sfx.cs:129d", dg129d_1); }
        }

        // 音量三档：按当前档位重新合成（声音是现算的，改音量只要重建播放器）
        public static int Volume { get { return vol; } }
        public static string VolumeName { get { return VolNames[vol]; } }
        public static void SetVolume(int v)
        {
            if (v < 0) v = 0;
            if (v > 2) v = 2;
            vol = v;
            gain = Gains[v];
            try { BuildPlayers(); } catch (Exception dg141_1) { Diag.Swallow("Sfx.cs:141", dg141_1); }
            try { if (volPath != null && volPath.Length > 0) File.WriteAllText(volPath, vol.ToString(), new UTF8Encoding(false)); } catch (Exception dg142_1) { Diag.Swallow("Sfx.cs:142", dg142_1); }
            try { lastAt.Clear(); } catch (Exception dg143_1) { Diag.Swallow("Sfx.cs:143", dg143_1); }
        }

        // ---------- 音色三档 ----------
        public static int Pack { get { return pack; } }
        public static string PackName { get { return PackNames[pack]; } }
        public static void SetPack(int v)
        {
            if (v < 0) v = 0;
            if (v >= PACK_COUNT) v = PACK_COUNT - 1;
            pack = v;
            try { BuildPlayers(); } catch (Exception dg154_1) { Diag.Swallow("Sfx.cs:154", dg154_1); }
            try { if (packPath != null && packPath.Length > 0) File.WriteAllText(packPath, PackNames[pack], new UTF8Encoding(false)); } catch (Exception dg155_1) { Diag.Swallow("Sfx.cs:155", dg155_1); }
            try { lastAt.Clear(); } catch (Exception dg156_1) { Diag.Swallow("Sfx.cs:156", dg156_1); }
        }

        public static void ResetDefaults()
        {
            SetLevel(1);
            SetPack(1);
            SetVolume(1);
            SetFocus(false);
            for (int i = 0; i < EventKeys.Length; i++) SetEventOn(EventKeys[i], true);
            SetOn(true);
        }

        static void BuildPlayers()
        {
            pClick = Player(VoiceClick());
            pDrop = Player(VoiceDrop());
            pOk = Player(VoiceOk());
            pError = Player(VoiceError());
            pBell = Player(VoiceBell());
            pAlarm = Player(VoiceAlarm());
            pStop = Player(VoiceStop());
            pSwitch = Player(VoiceSwitch());
            pWarn = Player(VoiceWarn());
            pSave = Player(VoiceSave());
            pCopy = Player(VoiceCopy());
            pTick3 = Player(VoiceTick3());
            pLaunch = Player(VoiceLaunch());
            pDone = Player(VoiceDone());
        }

        public static void Toggle() { on = !on; Persist(); }
        public static void SetOn(bool v) { on = v; Persist(); }
        static void Persist()
        {
            try { if (storePath != null && storePath.Length > 0) File.WriteAllText(storePath, on ? "on" : "off", new UTF8Encoding(false)); }
            catch (Exception dg182_1) { Diag.Swallow("Sfx.cs:182", dg182_1); }
        }

        public static void Click() { Play("click", pClick, 200); }
        public static void Drop() { Play("drop", pDrop, 260); }
        public static void Ok() { Play("ok", pOk, 260); }
        public static void Error() { Play("error", pError, 320); }
        public static void Bell() { Play("bell", pBell, 320); }
        public static void Alarm() { Play("alarm", pAlarm, 320); }
        public static void Stop() { Play("stop", pStop, 220); }
        public static void Switch() { Play("switch", pSwitch, 300); }
        public static void Warn() { Play("warn", pWarn, 300); }
        public static void Save() { Play("save", pSave, 260); }
        public static void Copy() { Play("copy", pCopy, 180); }
        public static void Tick3() { Play("tick3", pTick3, 420); }
        public static void Launch() { Play("launch", pLaunch, 360); }
        public static void Done() { Play("done", pDone, 500); }

        // 面板试听专用：不管音效是开是关、是不是夜间，点了都能听到，也不受节流影响
        public static void Preview(int idx)
        {
            if (pClick == null) { try { BuildPlayers(); } catch (Exception dg203_1) { Diag.Swallow("Sfx.cs:203", dg203_1); } }
            SoundPlayer sp = null;
            switch (idx)
            {
                case 0: sp = pClick; break;
                case 1: sp = pSwitch; break;
                case 2: sp = pCopy; break;
                case 3: sp = pDrop; break;
                case 4: sp = pSave; break;
                case 5: sp = pDone; break;
                case 6: sp = pBell; break;
                case 7: sp = pWarn; break;
                case 8: sp = pError; break;
                case 9: sp = pAlarm; break;
                case 10: sp = pStop; break;
                case 11: sp = pLaunch; break;
            }
            if (sp != null) { try { sp.Play(); } catch (Exception dg220_1) { Diag.Swallow("Sfx.cs:220", dg220_1); } }
        }

        // 夜间静默：22:00–08:00 关掉"辅助型"音（点击 / 切页 / 复制 / 投递 / 倒计时滴），
        // 完成、提醒、警告、出错、到点这些照样响，半夜不会被滴答声烦到。
        public static bool Night { get { return nightQuiet; } }

        // ---------- v19.0 发声级别 ----------
        public static int Level { get { return level; } }
        public static string LevelName { get { return LevelNames[level]; } }
        public static readonly string[] LevelNames = new string[] { "全部", "只重要", "静默" };
        public static void SetLevel(int v)
        {
            if (v < 0) v = 0;
            if (v > 2) v = 2;
            level = v;
            try { if (levelPath != null && levelPath.Length > 0) File.WriteAllText(levelPath, v.ToString(), new UTF8Encoding(false)); } catch (Exception dg236_1) { Diag.Swallow("Sfx.cs:236", dg236_1); }
        }
        public static string QuietRange
        {
            get
            {
                return string.Format("{0:00}:{1:00}–{2:00}:{3:00}", qsMin / 60, qsMin % 60, qeMin / 60, qeMin % 60);
            }
        }
        public static void SetQuietRange(int startMin, int endMin)
        {
            if (startMin < 0) startMin = 0;
            if (startMin > 1439) startMin = 1439;
            if (endMin < 0) endMin = 0;
            if (endMin > 1439) endMin = 1439;
            qsMin = startMin; qeMin = endMin;
            try
            {
                string tp = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "音效夜间时段.txt");
                File.WriteAllText(tp, string.Format("{0:00}:{1:00}-{2:00}:{3:00}", qsMin / 60, qsMin % 60, qeMin / 60, qeMin % 60), new UTF8Encoding(false));
            }
            catch (Exception dg257_1) { Diag.Swallow("Sfx.cs:257", dg257_1); }
        }
        static int ParseHm(string s)
        {
            string[] hm = s.Split(':');
            int h = 0, m = 0;
            if (!int.TryParse(hm[0], out h)) return -1;
            if (hm.Length > 1 && !int.TryParse(hm[1], out m)) return -1;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }
        public static void SetNight(bool v)
        {
            nightQuiet = v;
            try { if (nightPath != null && nightPath.Length > 0) File.WriteAllText(nightPath, v ? "on" : "off", new UTF8Encoding(false)); } catch (Exception dg271_1) { Diag.Swallow("Sfx.cs:271", dg271_1); }
        }
        static bool InQuietHours()
        {
            int nowMin = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            if (qsMin == qeMin) return false;
            if (qsMin < qeMin) return nowMin >= qsMin && nowMin < qeMin;
            return nowMin >= qsMin || nowMin < qeMin;   // 跨零点
        }
        static bool IsMinor(string key)
        {
            return key == "click" || key == "switch" || key == "copy" || key == "drop" || key == "tick3";
        }

        // 同一个音在 gapMs 内不重复触发：连点按钮不会叠成一片噪声；不同音互不影响
        static void Play(string key, SoundPlayer sp, int gapMs)
        {
            if (!on || sp == null) return;
            if (level == 2) return;                                             // 静默
            if (level == 1 && key != null && IsMinor(key)) return;               // 只留重要音
            if (!EventOn(key)) return;                                           // v19.14 分事件关闭
            if (focus && key != "error" && key != "warn" && key != "alarm" && key != "bell") return;
            try
            {
                long now = (long)Environment.TickCount;
                if (key != null && key.Length > 0 && lastAt.ContainsKey(key))
                {
                    long prev = lastAt[key];
                    if (now - prev >= 0 && now - prev < gapMs) return;
                }
                if (key != null && key.Length > 0) lastAt[key] = now;
                if (nightQuiet && InQuietHours() && IsMinor(key)) return;
                sp.Play();
            }
            catch (Exception dg303_1) { Diag.Swallow("Sfx.cs:303", dg303_1); }
        }

        static void Play(SoundPlayer sp) { Play("", sp, 0); }

        static SoundPlayer Player(byte[] wav)
        {
            SoundPlayer sp = new SoundPlayer(new MemoryStream(wav));
            try { sp.Load(); } catch (Exception dg311_1) { Diag.Swallow("Sfx.cs:311", dg311_1); }
            return sp;
        }

        // ---------- 合成基座 ----------
        static byte[] Encode(double[] sIn)
        {
            // v18.6：先按音色档位重采样（清亮更高更短 / 沉稳更低更长），再做落地反射
            sIn = PackShape(sIn);
            // v18.1：先做"落地感"——14ms / 30ms 两次轻微反射，再把高频收敛一档，
            // 最后首尾淡入淡出，避免爆音和"电子味"。
            int refl = (int)(SR * 0.014), refl2 = (int)(SR * 0.030);
            double[] s = new double[sIn.Length + refl2 + 1];
            for (int i = 0; i < sIn.Length; i++) s[i] = sIn[i];
            for (int i = 0; i < sIn.Length; i++)
            {
                if (i + refl < s.Length) s[i + refl] += sIn[i] * 0.15;
                if (i + refl2 < s.Length) s[i + refl2] += sIn[i] * 0.06;
            }
            int fadeIn = Math.Max(4, (int)(SR * 0.003));
            int fadeOut = Math.Max(6, (int)(SR * 0.008));
            double lp = 0, cut = PackCut[pack];
            for (int i = 0; i < s.Length; i++)
            {
                lp += cut * (s[i] - lp);
                double v = lp;
                if (i < fadeIn) v *= i / (double)fadeIn;
                int rem = s.Length - 1 - i;
                if (rem < fadeOut) v *= rem / (double)fadeOut;
                s[i] = v;
            }
            int n = s.Length;
            int total = 44 + n * 2;
            byte[] b = new byte[total];
            b[0] = (byte)'R'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'F';
            Int2(b, 4, total - 8);
            b[8] = (byte)'W'; b[9] = (byte)'A'; b[10] = (byte)'V'; b[11] = (byte)'E';
            b[12] = (byte)'f'; b[13] = (byte)'m'; b[14] = (byte)'t'; b[15] = (byte)' ';
            Int4(b, 16, 16);
            Int2(b, 20, 1);          // PCM
            Int2(b, 22, 1);          // 单声道
            Int4(b, 24, SR);
            Int4(b, 28, SR * 2);     // 字节率
            Int2(b, 32, 2);          // 块对齐
            Int2(b, 34, 16);         // 位深
            b[36] = (byte)'d'; b[37] = (byte)'a'; b[38] = (byte)'t'; b[39] = (byte)'a';
            Int4(b, 40, n * 2);
            int o = 44;
            for (int i = 0; i < n; i++)
            {
                double v = s[i];
                if (v > 1.0) v = 1.0; else if (v < -1.0) v = -1.0;
                short x = (short)(v * 32000.0 * gain);
                b[o] = (byte)(x & 0xFF);
                b[o + 1] = (byte)((x >> 8) & 0xFF);
                o += 2;
            }
            return b;
        }
        // 音色整形：线性重采样改音高，沉稳档再补 60ms 低频尾音，避免"掐断感"
        static double[] PackShape(double[] src)
        {
            if (src == null || src.Length < 8) return src;
            double f = PackPitch[pack];
            if (Math.Abs(f - 1.0) < 0.001) return src;
            int n = (int)(src.Length / f);
            if (n < 8) n = 8;
            double[] o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double p = i * f;
                int i0 = (int)p;
                if (i0 >= src.Length) i0 = src.Length - 1;
                int i1 = Math.Min(src.Length - 1, i0 + 1);
                double t = p - i0;
                o[i] = src[i0] * (1.0 - t) + src[i1] * t;
            }
            if (pack == 2)
            {
                int tail = (int)(SR * 0.06);
                double[] o2 = new double[o.Length + tail];
                Array.Copy(o, o2, o.Length);
                for (int i = 0; i < tail; i++)
                {
                    int k = o.Length - 1 - Math.Min(o.Length - 1, i);
                    double g2 = 1.0 - i / (double)tail;
                    o2[o.Length + i] = o[k] * 0.16 * g2;
                }
                return o2;
            }
            return o;
        }

        static void Int2(byte[] b, int at, int v) { b[at] = (byte)(v & 0xFF); b[at + 1] = (byte)((v >> 8) & 0xFF); }
        static void Int4(byte[] b, int at, int v)
        {
            b[at] = (byte)(v & 0xFF);
            b[at + 1] = (byte)((v >> 8) & 0xFF);
            b[at + 2] = (byte)((v >> 16) & 0xFF);
            b[at + 3] = (byte)((v >> 24) & 0xFF);
        }
        static double[] Buf(double ms) { return new double[(int)(SR * ms / 1000.0)]; }
        static double Sine(double phase) { return Math.Sin(phase * 2.0 * Math.PI); }
        static double Saw(double phase) { double p = phase - Math.Floor(phase); return 2.0 * p - 1.0; }

        // 点击：30ms 下行正弦，短到不吵，却有明确的“按键感”
        // v9.2：任务启动音。392Hz 撑 45ms 起跳 → 620Hz 收 75ms，一小段"抬起来"的双音，
        // 听起来是"装上了、开始跑了"，与 ok（329Hz 单音 0.36s 的"叮——"）气质不同。
        static byte[] VoiceLaunch()
        {
            double[] s = Buf(120);
            int seg = (int)(SR * 0.045);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double att = Math.Min(1.0, i / (SR * 0.006));      // 6ms 起音，避免爆音
                double v;
                if (i < seg)
                {
                    double tt = i / (double)SR;
                    v = 0.15 * att * Math.Exp(-tt * 26.0) * Sine((392 + 40 * (i / (double)seg)) * tt);
                }
                else
                {
                    double tt = (i - seg) / (double)SR;
                    double env = Math.Exp(-tt * 30.0) * (1.0 - Math.Min(1.0, tt / 0.075) * 0.25);
                    v = 0.19 * att * env * (Sine((620 - 30 * Math.Min(1.0, tt / 0.075)) * tt)
                        + 0.18 * Sine((1240 - 60 * Math.Min(1.0, tt / 0.075)) * tt));
                }
                s[i] = v;
            }
            return Encode(s);
        }

        // v18.1：任务完成音。D5 / G5 / C6 三音上行琶音，比 Ok 更"走完一段路"的气质。
        static byte[] VoiceDone()
        {
            double[] s = Buf(430);
            double[] fr = new double[] { 587.33, 783.99, 1046.5 };
            for (int k = 0; k < 3; k++)
            {
                int start = (int)(SR * 0.070 * k);
                int len = (int)(SR * 0.24);
                double amp = 0.12 + 0.02 * k;
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    double t = i / (double)SR;
                    double env = Math.Min(1.0, i / (double)(SR * 0.006)) * Math.Exp(-t * 11.0);
                    s[start + i] += amp * env * (Sine(fr[k] * t) + 0.14 * Sine(2.0 * fr[k] * t));
                }
            }
            return Encode(s);
        }

        // v9.1：倒计时最后 3 秒的"轻滴"。1180Hz 起、60ms、指数衰减，音量只有常规音的四成左右，
        // 叠一点二次谐波让它清脆而不闷。跟到点的 alarm（259Hz 双嘀）在频率、时长、音量上都分得开。
        static byte[] VoiceTick3()
        {
            double[] s = Buf(60);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)s.Length;
                double env = Math.Exp(-t * 48.0) * (1.0 - prog * 0.25);
                s[i] = 0.11 * env * (Sine((1180 - 190 * prog) * t) + 0.22 * Sine((2360 - 380 * prog) * t));
            }
            return Encode(s);
        }

        static byte[] VoiceClick()
        {
            double[] s = Buf(26);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)s.Length;
                double f = 1520 - 240 * prog;
                s[i] = 0.145 * Math.Exp(-t * 130.0) * (Sine(f * t) + 0.18 * Sine(2.0 * f * t));
            }
            return Encode(s);
        }

        // 投递：90ms 噪声道扫（气动感）+ 25ms 1600Hz 咔
        static byte[] VoiceDrop()
        {
            double[] s = Buf(150);
            Random rnd = new Random(20261117);
            int sweep = (int)(SR * 0.09);
            double lp = 0;
            for (int i = 0; i < sweep; i++)
            {
                double prog = i / (double)sweep;
                double cutoff = 0.10 + 0.55 * prog;       // 简易一阶低通，越往后越亮
                lp = lp + cutoff * ((rnd.NextDouble() * 2 - 1) - lp);
                s[i] = 0.22 * lp * (0.35 + 0.65 * prog);
            }
            int off = sweep + (int)(SR * 0.01);
            for (int i = 0; i < (int)(SR * 0.025) && off + i < s.Length; i++)
            {
                double t = i / (double)SR;
                s[off + i] += 0.14 * Math.Exp(-t * 220.0) * Sine(1600 * t);
            }
            return Encode(s);
        }

        // 成功：660 / 990 / 1320 三连上行，每音 140ms 衰减，带一点二次谐波（金属铃感）
        static byte[] VoiceOk()
        {
            double[] s = Buf(520);
            double[] fr = new double[] { 523.25, 659.25, 783.99 };   // C5 E5 G5，同一个和弦上行，听着"顺"
            for (int k = 0; k < 3; k++)
            {
                int start = (int)(SR * 0.085 * k);
                int len = (int)(SR * 0.30);
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    double t = i / (double)SR;
                    double env = Math.Min(1.0, i / (double)(SR * 0.006)) * Math.Exp(-t * 9.0);
                    s[start + i] += 0.15 * env * (Sine(fr[k] * t) + 0.16 * Sine(2.0 * fr[k] * t) + 0.05 * Sine(3.0 * fr[k] * t));
                }
            }
            return Encode(s);
        }

        // 错误（独创）：两段下坠 + 26Hz 振颤的齿状音，第一段 520→330Hz 带砂砾，第二段 300→210Hz 下沉收尾
        static byte[] VoiceError()
        {
            int seg1 = (int)(SR * 0.16);
            int gap = (int)(SR * 0.05);
            int seg2 = (int)(SR * 0.18);
            double[] s = new double[seg1 + gap + seg2];
            for (int i = 0; i < seg1; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)seg1;
                double f = 466 - 96 * prog;                       // Bb4 → A4 下坠
                double env = Math.Min(1.0, prog * 8.0) * Math.Exp(-prog * 1.2);
                s[i] = 0.26 * env * (Sine(f * t) + 0.22 * Sine(2.0 * f * t) + 0.08 * Sine(3.0 * f * t));
            }
            int off = seg1 + gap;
            for (int i = 0; i < seg2; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)seg2;
                double f = 392 - 70 * prog;                       // G4 → F4
                double env = Math.Min(1.0, prog * 9.0) * Math.Min(1.0, (1.0 - prog) * 2.6);
                s[off + i] = 0.24 * env * (Sine(f * t) + 0.18 * Sine(2.0 * f * t));
            }
            return Encode(s);
        }

        // 提醒铃：880Hz 一击 + 660Hz 长尾，两个谐波叠出“叮—咚”
        static byte[] VoiceBell()
        {
            double[] s = Buf(760);
            int strike = (int)(SR * 0.18);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                if (i < strike)
                {
                    double env = Math.Exp(-t * 14.0);
                    s[i] += 0.20 * env * (Sine(1046.5 * t) + 0.26 * Sine(2093 * t) + 0.06 * Sine(3139.5 * t));
                }
                if (i >= (int)(SR * 0.15))
                {
                    double t2 = (i - (int)(SR * 0.15)) / (double)SR;
                    double env2 = Math.Min(1.0, t2 * 36.0) * Math.Exp(-t2 * 4.2);
                    s[i] += 0.18 * env2 * (Sine(784 * t2) + 0.24 * Sine(1568 * t2));
                }
            }
            return Encode(s);
        }

        // 到点/警告：双脉冲 780Hz（方波质地），中间留 70ms 空档，明确“再确认一次”
        static byte[] VoiceAlarm()
        {
            double[] s = Buf(320);
            int p = (int)(SR * 0.09);
            int gap = (int)(SR * 0.08);
            for (int k = 0; k < 2; k++)
            {
                int start = k * (p + gap);
                for (int i = 0; i < p && start + i < s.Length; i++)
                {
                    double t = i / (double)SR;
                    double env = Math.Min(1.0, i / (double)(SR * 0.005)) * Math.Min(1.0, (p - i) / (double)(SR * 0.025));
                    s[start + i] += 0.20 * env * (Sine(660 * t) + 0.20 * Sine(1320 * t));
                }
            }
            return Encode(s);
        }

        // 取消/停止：380→250Hz 一声轻下沉，比系统“叹号”柔和
        static byte[] VoiceStop()
        {
            double[] s = Buf(120);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)s.Length;
                double env = Math.Min(1.0, prog * 5.0) * Math.Min(1.0, (1.0 - prog) * 3.0);
                s[i] = 0.18 * env * Sine((380 - 130 * prog) * t);
            }
            return Encode(s);
        }

        // 切页：55ms 轻挑 900→1150Hz，0.12 幅度，比 Click 更“气”，只在页面真正换的时候响
        static byte[] VoiceSwitch()
        {
            double[] s = Buf(58);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)s.Length;
                double env = Math.Min(1.0, prog * 4.0) * Math.Exp(-prog * 2.4);
                s[i] = 0.125 * env * (Sine((880 + 220 * prog) * t) + 0.22 * Sine((1760 + 440 * prog) * t));
            }
            return Encode(s);
        }

        // 软警告（分辨“不太对但没坏”）：140ms 420Hz 单脉冲 + 二次谐波，尾音轻收，不像 Error 那么凶
        static byte[] VoiceWarn()
        {
            double[] s = Buf(175);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)s.Length;
                double env = Math.Min(1.0, prog * 8.0) * Math.Min(1.0, (1.0 - prog) * 2.8);
                s[i] = 0.17 * env * (Sine(587.3 * t) + 0.20 * Sine(1174.6 * t));
            }
            return Encode(s);
        }

        // 保存成功：两段上行 520→780Hz，短促干净，跟 Ok 的三连区分开
        static byte[] VoiceSave()
        {
            double[] s = Buf(230);
            int seg = (int)(SR * 0.095);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                double prog = i / (double)s.Length;
                double f = i < seg ? 587.3 : 880;
                double env = Math.Min(1.0, prog * 7.0) * Math.Min(1.0, (1.0 - prog) * 2.4);
                double lip = i < seg ? 1.0 : 0.84;
                s[i] = 0.15 * lip * env * (Sine(f * t) + 0.20 * Sine(f * 2.0 * t));
            }
            return Encode(s);
        }

        // 复制：两个 22ms 的 1200Hz 极短 tick，打字机手感，几乎不打扰
        static byte[] VoiceCopy()
        {
            double[] s = Buf(70);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)SR;
                s[i] = 0.145 * Math.Exp(-t * 170.0) * (Sine(1100 * t) + 0.16 * Sine(2200 * t));
            }
            return Encode(s);
        }

        // ---------- 自测：把全部音落成 wav 并给出客观指标（不发声，跑测试不会吵到人） ----------
        public static string SelfTest(string baseDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== v18.1 音效自测（14 音 / 44.1k / 三档音量 / 夜间静默 / 防连击） ==");
            sb.AppendLine("ui_sfx=" + (on ? "on" : "off"));
            sb.AppendLine("store=" + (storePath == null ? "(unset)" : storePath));
            string dir = Path.Combine(baseDir, "音效");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch (Exception dg683_1) { Diag.Swallow("Sfx.cs:683", dg683_1); }
            byte[][] wavs = new byte[][] { VoiceClick(), VoiceDrop(), VoiceOk(), VoiceDone(), VoiceError(), VoiceBell(), VoiceAlarm(), VoiceStop(), VoiceSwitch(), VoiceWarn(), VoiceSave(), VoiceCopy(), VoiceTick3(), VoiceLaunch() };
            string[] names = new string[] { "click", "drop", "ok", "done", "error", "bell", "alarm", "stop", "switch", "warn", "save", "copy", "tick3", "launch" };
            for (int i = 0; i < wavs.Length; i++)
            {
                string p = Path.Combine(dir, names[i] + ".wav");
                try { File.WriteAllBytes(p, wavs[i]); } catch (Exception dg689_1) { Diag.Swallow("Sfx.cs:689", dg689_1); }
                double sec = (wavs[i].Length - 44) / (double)(SR * 2);
                double peak = 0, rms = 0;
                for (int k = 44; k + 1 < wavs[i].Length; k += 2)
                {
                    short v = (short)(wavs[i][k] | (wavs[i][k + 1] << 8));
                    double d = Math.Abs(v / 32000.0);
                    if (d > peak) peak = d;
                    rms += d * d;
                }
                int cnt = (wavs[i].Length - 44) / 2;
                rms = cnt > 0 ? Math.Sqrt(rms / cnt) : 0;
                sb.AppendLine(names[i] + " bytes=" + wavs[i].Length + " sec=" + sec.ToString("0.000")
                    + " peak=" + peak.ToString("0.00") + " rms=" + rms.ToString("0.000")
                    + " file=" + (File.Exists(p) ? "ok" : "fail"));
            }
            // 开关落盘往返
            bool old = on;
            // 音量三档：同一音在三档下的峰值应逐档变大
            try
            {
                int oldVol = vol;
                sb.AppendLine("volumes=" + VolNames[0] + "/" + VolNames[1] + "/" + VolNames[2] + " gains=" + Gains[0] + "/" + Gains[1] + "/" + Gains[2]);
                for (int v = 0; v < 3; v++)
                {
                    gain = Gains[v];
                    byte[] okw = VoiceOk();
                    double pk = 0;
                    for (int k = 44; k + 1 < okw.Length; k += 2)
                    {
                        short vv = (short)(okw[k] | (okw[k + 1] << 8));
                        double d = Math.Abs(vv / 32000.0);
                        if (d > pk) pk = d;
                    }
                    sb.AppendLine("vol" + v + "(" + VolNames[v] + ") ok_peak=" + pk.ToString("0.000"));
                }
                gain = Gains[oldVol];
                SetVolume(oldVol);
                string vt = File.Exists(volPath) ? File.ReadAllText(volPath).Trim() : "(none)";
                sb.AppendLine("volume_store=" + volPath + " -> " + vt + "  now=" + VolumeName);
                sb.AppendLine("dedupe_gaps click=200 drop/ok=260 error/bell/alarm=320 switch/stop=300 warn=300 save=260 copy=180");
            }
            catch (Exception ex) { sb.AppendLine("volume EX " + ex.Message); }
            // 开关落盘往返
            try
            {
                SetOn(false);
                string offTxt = File.Exists(storePath) ? File.ReadAllText(storePath).Trim() : "(none)";
                SetOn(true);
                string onTxt = File.Exists(storePath) ? File.ReadAllText(storePath).Trim() : "(none)";
                sb.AppendLine("toggle=off->" + offTxt + " / on->" + onTxt + " / now=" + (on ? "on" : "off"));
            }
            catch (Exception ex) { sb.AppendLine("toggle EX " + ex.Message); on = old; }
            return sb.ToString();
        }
    }
}