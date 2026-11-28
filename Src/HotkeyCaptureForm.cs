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
    class HotkeyCaptureForm : Form
    {
        public int Mods = 0;
        public int Vk = 0;
        Label hint;

        public HotkeyCaptureForm(string name, bool en)
        {
            Text = en ? "Press a shortcut" : "按下想要的快捷键";
            BackColor = UiKit.A(20, 24, 30);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowIcon = false;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            Label t = new Label();
            t.Text = (en ? "Set shortcut for " : "为「") + name + (en ? "" : "」设置快捷键");
            t.ForeColor = UiKit.Txt(Color.White);
            t.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            t.Location = new Point(20, 18);
            t.AutoSize = true;
            Controls.Add(t);

            hint = new Label();
            hint.Text = en ? "Press Ctrl / Alt / Shift / Win plus a key. Esc cancels." : "请按 Ctrl / Alt / Shift / Win 加一个键；Esc 取消。";
            hint.ForeColor = UiKit.A(150, 160, 176);
            hint.Location = new Point(20, 56);
            hint.Size = new Size(400, 40);
            Controls.Add(hint);

            Label big = new Label();
            big.Name = "big";
            big.Text = "…";
            big.ForeColor = UiKit.A(96, 165, 250);
            big.Font = new Font("Consolas", 18F, FontStyle.Bold);
            big.Location = new Point(20, 104);
            big.Size = new Size(400, 40);
            Controls.Add(big);

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                int vk = (int)e.KeyCode;
                if (vk == 27) { DialogResult = DialogResult.Cancel; Close(); return; }
                if (vk == 17 || vk == 16 || vk == 18 || vk == 91 || vk == 92) return; // 纯修饰键
                int mods = 0;
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) mods |= 2;
                if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt) mods |= 1;
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) mods |= 4;
                if (mods == 0)
                {
                    big.ForeColor = UiKit.A(232, 82, 61);
                    big.Text = en ? "Need Ctrl/Alt/Shift/Win" : "必须带 Ctrl / Alt / Shift / Win";
                    return;
                }
                big.ForeColor = UiKit.A(120, 220, 160);
                big.Text = HotkeyManager.Display(mods, vk);
                Mods = mods; Vk = vk;
                // 留一点时间让用户看到组合键再关窗
                System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
                tm.Interval = 320;
                tm.Tick += delegate (object s2, EventArgs e2) { tm.Stop(); tm.Dispose(); DialogResult = DialogResult.OK; Close(); };
                tm.Start();
            };
        }
    }
}
