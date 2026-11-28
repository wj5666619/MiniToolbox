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
    static class QrCore
    {
        // 版本 → 尺寸=17+4V / 数据码字总数 / 分块表
        static int[] V = { 6, 7, 8, 9, 10 };
        static int[] DataTotal = { 108, 124, 154, 182, 216 };
        static int[] EcPerBlock = { 16, 18, 22, 22, 26 };
        static int[][] GroupBlocks = {
            new int[] { 4 },
            new int[] { 4 },
            new int[] { 2, 2 },
            new int[] { 3, 2 },
            new int[] { 4, 1 }
        };
        static int[][] GroupData = {
            new int[] { 27 },
            new int[] { 31 },
            new int[] { 38, 39 },
            new int[] { 36, 37 },
            new int[] { 43, 44 }
        };
        static int[][] AlignPos = {
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 }
        };

        static int[] Exp = new int[512];
        static int[] Log = new int[256];
        static bool inited = false;

        static void Init()
        {
            if (inited) return;
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = x;
                Log[x] = i;
                x <<= 1;
                if (x >= 256) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
            inited = true;
        }

        static int Mul(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        static int[] RsDivisor(int deg)
        {
            Init();
            int[] res = new int[deg];
            res[deg - 1] = 1;
            int root = 1;
            for (int i = 0; i < deg; i++)
            {
                for (int j = 0; j < deg; j++)
                {
                    res[j] = Mul(res[j], root);
                    if (j + 1 < deg) res[j] ^= res[j + 1];
                }
                root = Mul(root, 2);
            }
            return res;
        }

        static int[] RsRemainder(int[] data, int[] div)
        {
            int[] res = new int[div.Length];
            foreach (int b in data)
            {
                int factor = b ^ res[0];
                Array.Copy(res, 1, res, 0, res.Length - 1);
                res[res.Length - 1] = 0;
                for (int i = 0; i < res.Length; i++) res[i] ^= Mul(div[i], factor);
            }
            return res;
        }

        public static int Capacity(int idx)
        {
            int countBits = V[idx] >= 10 ? 16 : 8;
            int bits = DataTotal[idx] * 8 - 4 - countBits;
            return bits / 8;
        }

        public static int MaxCapacity()
        {
            return Capacity(V.Length - 1);
        }

        static int PickVersion(int byteLen, out string err)
        {
            err = "";
            for (int i = 0; i < V.Length; i++)
                if (byteLen <= Capacity(i)) return i;
            err = "文本太长（" + byteLen + " 字节），最多 " + MaxCapacity() + " 字节（约 " + (MaxCapacity() / 3) + " 个汉字）";
            return -1;
        }

        static void Push(List<bool> bits, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0);
        }

        static List<bool> DataBits(string text, int idx)
        {
            List<bool> bits = new List<bool>();
            byte[] bs = Encoding.UTF8.GetBytes(text);
            Push(bits, 0x4, 4);
            Push(bits, bs.Length, V[idx] >= 10 ? 16 : 8);
            foreach (byte b in bs) Push(bits, b, 8);
            int cap = DataTotal[idx] * 8;
            int term = Math.Min(4, cap - bits.Count);
            if (term > 0) Push(bits, 0, term);
            while (bits.Count % 8 != 0) bits.Add(false);
            int pad = 0xEC;
            while (bits.Count < cap)
            {
                Push(bits, pad, 8);
                pad = pad == 0xEC ? 0x11 : 0xEC;
            }
            return bits;
        }

        static byte[] Codewords(string text, int idx)
        {
            List<bool> bits = DataBits(text, idx);
            List<byte> dataCw = new List<byte>();
            for (int i = 0; i < bits.Count; i += 8)
            {
                int v = 0;
                for (int k = 0; k < 8; k++) v = (v << 1) | (bits[i + k] ? 1 : 0);
                dataCw.Add((byte)v);
            }
            int[] div = RsDivisor(EcPerBlock[idx]);
            List<byte[]> datas = new List<byte[]>();
            List<byte[]> ecs = new List<byte[]>();
            int pos = 0;
            for (int g = 0; g < GroupBlocks[idx].Length; g++)
            {
                for (int b = 0; b < GroupBlocks[idx][g]; b++)
                {
                    int per = GroupData[idx][g];
                    byte[] d = new byte[per];
                    for (int i = 0; i < per; i++) d[i] = dataCw[pos + i];
                    pos += per;
                    int[] di = new int[per];
                    for (int i = 0; i < per; i++) di[i] = d[i];
                    int[] ec = RsRemainder(di, div);
                    byte[] ecb = new byte[EcPerBlock[idx]];
                    for (int i = 0; i < ecb.Length; i++) ecb[i] = (byte)ec[i];
                    datas.Add(d);
                    ecs.Add(ecb);
                }
            }
            List<byte> outCw = new List<byte>();
            int maxData = 0;
            foreach (byte[] d in datas) if (d.Length > maxData) maxData = d.Length;
            for (int i = 0; i < maxData; i++)
                foreach (byte[] d in datas)
                    if (i < d.Length) outCw.Add(d[i]);
            for (int i = 0; i < EcPerBlock[idx]; i++)
                foreach (byte[] e in ecs)
                    outCw.Add(e[i]);
            return outCw.ToArray();
        }

        static bool MaskBit(int id, int x, int y)
        {
            switch (id)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return (x * y) % 2 + (x * y) % 3 == 0;
                case 6: return ((x * y) % 2 + (x * y) % 3) % 2 == 0;
                default: return ((x + y) % 2 + (x * y) % 3) % 2 == 0;
            }
        }

        public static int[,] Matrix(string text, out string err, out int ver)
        {
            err = "";
            ver = 0;
            int idx = PickVersion(Encoding.UTF8.GetByteCount(text), out err);
            if (idx < 0) return null;
            int size = 17 + 4 * V[idx];
            ver = V[idx];
            byte[] cw = Codewords(text, idx);
            int[,] mb = new int[size, size];
            bool[,] fn = new bool[size, size];
            DrawFinder(mb, fn, 0, 0, size);
            DrawFinder(mb, fn, size - 7, 0, size);
            DrawFinder(mb, fn, 0, size - 7, size);
            for (int i = 0; i < size; i++)
            {
                if (!fn[6, i]) { mb[6, i] = (i % 2 == 0) ? 1 : 0; fn[6, i] = true; }
                if (!fn[i, 6]) { mb[i, 6] = (i % 2 == 0) ? 1 : 0; fn[i, 6] = true; }
            }
            int[] ap = AlignPos[idx];
            for (int a = 0; a < ap.Length; a++)
                for (int b = 0; b < ap.Length; b++)
                {
                    int cx = ap[a], cy = ap[b];
                    if (cx == 6 && cy == 6) continue;
                    if (cx == size - 7 && cy == 6) continue;
                    if (cx == 6 && cy == size - 7) continue;
                    DrawAlign(mb, fn, cx - 2, cy - 2, size);
                }
            if (V[idx] >= 7) DrawVersion(mb, fn, size, V[idx]);
            ReserveFormatArea(fn, size);

            int totalBits = cw.Length * 8;
            int bestScore = int.MaxValue;
            int[,] best = null;
            for (int mi = 0; mi < 8; mi++)
            {
                int[,] cand = (int[,])mb.Clone();
                int bitIdx = 0;
                bool up = true;
                for (int col = size - 1; col > 0; col -= 2)
                {
                    if (col == 6) col--;
                    for (int c = 0; c < size; c++)
                    {
                        int row = up ? size - 1 - c : c;
                        for (int k = 0; k < 2; k++)
                        {
                            int cc = col - k;
                            if (cc < 0 || fn[row, cc]) continue;
                            bool bit = false;
                            if (bitIdx < totalBits)
                                bit = ((cw[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                            bitIdx++;
                            cand[row, cc] = (bit ^ MaskBit(mi, cc, row)) ? 1 : 0;
                        }
                    }
                    up = !up;
                }
                DrawFormat(cand, mi, size);
                int score = Penalty(cand, size);
                if (score < bestScore) { bestScore = score; best = cand; }
            }
            if (best == null) { err = "编码失败"; return null; }
            return best;
        }

        static void DrawFinder(int[,] m, bool[,] fn, int x0, int y0, int size)
        {
            for (int dy = -1; dy <= 7; dy++)
                for (int dx = -1; dx <= 7; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int v = 0;
                    if (dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6)
                    {
                        bool ring = dx == 0 || dx == 6 || dy == 0 || dy == 6;
                        bool core = dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4;
                        v = (ring || core) ? 1 : 0;
                    }
                    m[y, x] = v;
                    fn[y, x] = true;
                }
        }

        static void DrawAlign(int[,] m, bool[,] fn, int x0, int y0, int size)
        {
            for (int dy = 0; dy < 5; dy++)
                for (int dx = 0; dx < 5; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    bool ring = dx == 0 || dx == 4 || dy == 0 || dy == 4;
                    bool core = dx == 2 && dy == 2;
                    m[y, x] = (ring || core) ? 1 : 0;
                    fn[y, x] = true;
                }
        }

        static void ReserveFormatArea(bool[,] fn, int size)
        {
            for (int i = 0; i <= 8; i++)
            {
                if (i != 6) fn[i, 8] = true;
                if (i != 6) fn[8, i] = true;
            }
            for (int i = 0; i < 8; i++) fn[size - 1 - i, 8] = true;
            for (int i = 0; i < 8; i++) fn[8, size - 1 - i] = true;
            fn[size - 8, 8] = true;
        }

        static void DrawVersion(int[,] m, bool[,] fn, int size, int ver)
        {
            int rem = ver;
            for (int i = 0; i < 12; i++)
            {
                int b = (rem >> 11) & 1;
                rem = (rem << 1) ^ (b != 0 ? 0x1F25 : 0);
                rem &= 0xFFF;
            }
            int bits = (ver << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                m[b, a] = bit ? 1 : 0;
                fn[b, a] = true;
                m[a, b] = bit ? 1 : 0;
                fn[a, b] = true;
            }
        }

        static void DrawFormat(int[,] m, int mask, int size)
        {
            int data = mask & 0x7;                  // EC=M → 高两位 00
            int rem = data;
            for (int i = 0; i < 10; i++)
            {
                int b = (rem >> 9) & 1;
                rem = (rem << 1) ^ (b != 0 ? 0x537 : 0);
                rem &= 0x3FF;
            }
            int bits = ((data << 10) | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetF(m, 8, i, GetBit(bits, i), size);
            SetF(m, 8, 7, GetBit(bits, 6), size);
            SetF(m, 8, 8, GetBit(bits, 7), size);
            SetF(m, 7, 8, GetBit(bits, 8), size);
            for (int i = 9; i < 15; i++) SetF(m, 14 - i, 8, GetBit(bits, i), size);
            for (int i = 0; i < 8; i++) SetF(m, size - 1 - i, 8, GetBit(bits, i), size);
            for (int i = 8; i < 15; i++) SetF(m, 8, size - 15 + i, GetBit(bits, i), size);
            SetF(m, 8, size - 8, true, size);
        }

        static void SetF(int[,] m, int x, int y, bool dark, int size)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            m[y, x] = dark ? 1 : 0;
        }

        static bool GetBit(int v, int i)
        {
            return ((v >> i) & 1) != 0;
        }

        static int Penalty(int[,] m, int size)
        {
            int score = 0;
            for (int y = 0; y < size; y++)
            {
                int run = 1;
                for (int x = 1; x < size; x++)
                {
                    if (m[y, x] == m[y, x - 1]) run++;
                    else
                    {
                        if (run >= 5) score += 3 + (run - 5);
                        run = 1;
                    }
                }
                if (run >= 5) score += 3 + (run - 5);
            }
            for (int x = 0; x < size; x++)
            {
                int run = 1;
                for (int y = 1; y < size; y++)
                {
                    if (m[y, x] == m[y - 1, x]) run++;
                    else
                    {
                        if (run >= 5) score += 3 + (run - 5);
                        run = 1;
                    }
                }
                if (run >= 5) score += 3 + (run - 5);
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x + 6 < size; x++)
                    if (m[y, x] == 1 && m[y, x + 1] == 0 && m[y, x + 2] == 1 && m[y, x + 3] == 1 && m[y, x + 4] == 1 && m[y, x + 5] == 0 && m[y, x + 6] == 1) score += 40;
            for (int x = 0; x < size; x++)
                for (int y = 0; y + 6 < size; y++)
                    if (m[y, x] == 1 && m[y + 1, x] == 0 && m[y + 2, x] == 1 && m[y + 3, x] == 1 && m[y + 4, x] == 1 && m[y + 5, x] == 0 && m[y + 6, x] == 1) score += 40;
            return score;
        }

        public static Bitmap Render(string text, int pixel, Color dark, Color light, out string err)
        {
            int ver = 0;
            int[,] m = Matrix(text, out err, out ver);
            if (m == null) return null;
            int n = 17 + 4 * ver;
            int quiet = 4;
            int total = (n + quiet * 2) * pixel;
            Bitmap bmp = new Bitmap(total, total);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(light);
                using (SolidBrush b = new SolidBrush(dark))
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                            if (m[y, x] == 1)
                                g.FillRectangle(b, (x + quiet) * pixel, (y + quiet) * pixel, pixel, pixel);
            }
            return bmp;
        }

    }
}
