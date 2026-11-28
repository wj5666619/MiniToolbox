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
    static class AiVoice
    {
        public static bool Probed = false;
        public static bool Available = false;
        public static string Culture = "";
        public static string Engine = "";
        public static string LastError = "";
        public static string LastText = "";

        public static void Probe()
        {
            Probed = true;
            try
            {
                System.Collections.ObjectModel.ReadOnlyCollection<System.Speech.Recognition.RecognizerInfo> rs =
                    System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers();
                if (rs == null || rs.Count == 0) { Available = false; Culture = ""; Engine = ""; return; }
                int pick = -1;
                for (int i = 0; i < rs.Count; i++)
                {
                    if (rs[i].Culture != null && rs[i].Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    { pick = i; break; }
                }
                if (pick < 0) pick = 0;
                Culture = rs[pick].Culture == null ? "" : rs[pick].Culture.Name;
                Engine = rs[pick].Name;
                Available = true;
                LastError = "";
            }
            catch (Exception ex) { Available = false; LastError = ex.Message; }
        }

        public static string Status()
        {
            if (!Probed) Probe();
            if (Available)
                return "语音：" + (Culture.Length > 0 ? Culture : "默认") + " 离线引擎就绪（" + Engine + "）";
            return "语音：本机没装语音识别引擎" + (LastError.Length > 0 ? "（" + LastError + "）" : "")
                + "；在 Windows 设置 → 时间和语言 → 语音 里装上中文语音即可";
        }

        public static string Listen(int seconds)
        {
            LastText = ""; LastError = "";
            if (!Probed) Probe();
            if (!Available) { LastError = "本机没有语音识别引擎"; return ""; }
            int secs = seconds > 0 ? seconds : 6;
            try
            {
                System.Speech.Recognition.SpeechRecognitionEngine eng;
                if (Culture.Length > 0)
                    eng = new System.Speech.Recognition.SpeechRecognitionEngine(new System.Globalization.CultureInfo(Culture));
                else
                    eng = new System.Speech.Recognition.SpeechRecognitionEngine();
                try
                {
                    eng.LoadGrammar(new System.Speech.Recognition.DictationGrammar());
                    eng.SetInputToDefaultAudioDevice();
                    System.Speech.Recognition.RecognitionResult r = eng.Recognize(TimeSpan.FromSeconds(secs));
                    if (r != null && r.Text != null) LastText = r.Text.Trim();
                    return LastText;
                }
                finally { try { eng.Dispose(); } catch (Exception dg85_1) { Diag.Swallow("AiVoice.cs:85", dg85_1); } }
            }
            catch (Exception ex) { LastError = ex.Message; return ""; }
        }
    }
}