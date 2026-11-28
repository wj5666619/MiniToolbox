using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ShutdownTool
{
    /// <summary>
    /// v19.9：企业策略。管理员在 %ProgramData%\MiniToolbox\策略.json 或程序目录 策略.json 下发，
    /// 本地文件覆盖机器级策略；只做「收紧」，不会替用户放开任何开关。
    /// </summary>
    internal static class Policy
    {
        public static bool Loaded;
        public static string Source = "";
        public static bool NoUpdate;
        public static bool NoDiagExport;
        public static bool Verbose;
        public static string[] Disabled = new string[0];

        public static string MachinePath()
        {
            string p = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (p.Length == 0) return "";
            return Path.Combine(Path.Combine(p, "MiniToolbox"), "策略.json");
        }

        public static void Load()
        {
            Apply(MachinePath());
            if (Cfg.Dir.Length > 0) Apply(Path.Combine(Cfg.Dir, "策略.json"));
            if (Source.Length > 0) Loaded = true;
        }

        static void Apply(string path)
        {
            if (path.Length == 0 || !File.Exists(path)) return;
            try
            {
                string text = File.ReadAllText(path);
                if (text.Trim().Length == 0) return;
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> o = js.Deserialize<Dictionary<string, object>>(text);
                if (o == null) return;

                object v;
                if (o.TryGetValue("禁用在线更新", out v) && Truthy(v)) NoUpdate = true;
                if (o.TryGetValue("禁用诊断导出", out v) && Truthy(v)) NoDiagExport = true;
                if (o.TryGetValue("详细日志", out v) && Truthy(v)) Verbose = true;

                if (o.TryGetValue("禁用功能", out v))
                {
                    System.Collections.IList arr = v as System.Collections.IList;
                    if (arr != null)
                    {
                        List<string> list = new List<string>(Disabled);
                        for (int i = 0; i < arr.Count; i++)
                        {
                            string s = arr[i] == null ? "" : arr[i].ToString().Trim();
                            string k = Normalize(s);
                            if (k.Length > 0 && !list.Contains(k)) list.Add(k);
                        }
                        Disabled = list.ToArray();
                    }
                }
                if (Source.Length == 0) Source = path;
                Loaded = true;
                Diag.Info("策略已加载：" + path + "（" + Describe() + "）");
            }
            catch (Exception ex)
            {
                Diag.Warn("策略文件无法解析：" + path + " → " + ex.Message);
            }
        }

        static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is int) return (int)v != 0;
            string s = v.ToString().Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes" || s == "on";
        }

        /// <summary>允许写槽位名（home/power/clean/…）或中文页名；统一成槽位名。</summary>
        public static string Normalize(string s)
        {
            if (s == null) return "";
            string k = s.Trim().ToLowerInvariant();
            switch (k)
            {
                case "工作台": case "首页": return "home";
                case "关机": case "定时关机": return "power";
                case "清理": case "c盘清理": return "clean";
                case "去水印": case "水印": return "wm";
                case "价值": case "steam价值": return "val";
                case "配置": case "电脑配置": return "sys";
                case "视频": case "视频下载": return "video";
                case "音乐": case "音乐下载": return "music";
                case "成就": case "steam成就": return "sam";
                case "重装": case "一键重装": return "reinstall";
            }
            return k;
        }

        public static bool PageAllowed(string slot)
        {
            string k = Normalize(slot);
            for (int i = 0; i < Disabled.Length; i++)
                if (string.Equals(Disabled[i], k, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static string Describe()
        {
            if (!Loaded) return "未下发";
            List<string> parts = new List<string>();
            if (NoUpdate) parts.Add("禁用在线更新");
            if (NoDiagExport) parts.Add("禁用诊断导出");
            if (Verbose) parts.Add("强制详细日志");
            if (Disabled.Length > 0) parts.Add("禁用页 " + string.Join("/", Disabled));
            if (parts.Count == 0) parts.Add("无限制项");
            return string.Join("，", parts.ToArray()) + "（" + Source + "）";
        }

        /// <summary>v19.14 控制中心用一句短状态，不重复把来源拼两遍。</summary>
        public static string LocatedState()
        {
            if (!Loaded) return "未下发";
            List<string> parts = new List<string>();
            if (NoUpdate) parts.Add("禁用在线更新");
            if (NoDiagExport) parts.Add("禁用诊断导出");
            if (Verbose) parts.Add("强制详细日志");
            if (Disabled.Length > 0) parts.Add("禁用页 " + string.Join("/", Disabled));
            if (parts.Count == 0) return "已加载 · 无限制项";
            return "已加载 · " + string.Join("，", parts.ToArray());
        }
    }
}
