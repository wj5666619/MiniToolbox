using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace ShutdownTool
{
    // v19.1 配置总管：程序目录里散着十几个 .txt，这里统一迁到 配置.json
    // 设计：第一期只做"存储层 + 一次性迁移"，读路径保持原样（各模块照旧读自己的 txt），
    //       第二期再把各模块的读/写切到 Cfg，txt 才允许删。
    // 目录：程序目录可写就用程序目录；不可写（Program Files / 只读盘）退到 %APPDATA%\MiniToolbox
    static class Cfg
    {
        public static string Dir = "";
        public static string FilePath = "";
        // v19.7：配置加载时如果发生「文件损坏 / 回退备份」，把原因记下来，启动自检统一写日志
        public static string LastLoadNote = "";
        static Dictionary<string, object> data = new Dictionary<string, object>();
        static bool dirty = false;

        // 旧 txt → json 键名（值是相对程序目录的文件名）
        static readonly string[,] LEGACY = new string[,]
        {
            { "皮肤.txt", "skin" },
            { "主题色.txt", "accent" },
            { "音效.txt", "sound" },
            { "音效音量.txt", "volume" },
            { "音效音色.txt", "tone" },
            { "音效夜间.txt", "night" },
            { "音效夜间时段.txt", "nightRange" },
            { "音效级别.txt", "soundLevel" },
            { "界面缩放.txt", "uiScale" },
            { "update_url.txt", "updateUrl" },
            { "ui.txt", "ui" }
        };

        public static void Load(string baseDir)
        {
            try
            {
                Dir = PickDir(baseDir);
                FilePath = Path.Combine(Dir, "配置.json");
                if (File.Exists(FilePath))
                {
                    Dictionary<string, object> d = null;
                    if (TryParse(System.IO.File.ReadAllText(FilePath, Encoding.UTF8), out d))
                    {
                        data = d;
                        return;
                    }
                    // v19.7：损坏不再静默吞掉——隔离原文件，尽量从 .bak 恢复，最后才回到默认值
                    string bad = Path.Combine(Dir, "配置_损坏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                    try { System.IO.File.Move(FilePath, bad); } catch (Exception dg55_1) { Diag.Swallow("Cfg.cs:55", dg55_1); }
                    LastLoadNote = "配置.json 无法解析，已隔离为 " + Path.GetFileName(bad);
                    Dictionary<string, object> bak = null;
                    string bakPath = FilePath + ".bak";
                    if (System.IO.File.Exists(bakPath) && TryParse(System.IO.File.ReadAllText(bakPath, Encoding.UTF8), out bak))
                    {
                        data = bak;
                        LastLoadNote += "，已从备份恢复";
                        dirty = true;
                        Save();
                    }
                    else
                    {
                        data = new Dictionary<string, object>();
                        LastLoadNote += "，将使用默认设置";
                    }
                    try { Diag.Warn(LastLoadNote); } catch (Exception dg71_1) { Diag.Swallow("Cfg.cs:71", dg71_1); }
                    return;
                }
                // 首次：从旧 txt 迁移（只读入，不删原文件，改名 .bak 留给用户比对）
                for (int i = 0; i < LEGACY.GetLength(0); i++)
                {
                    string old = Path.Combine(baseDir, LEGACY[i, 0]);
                    if (!System.IO.File.Exists(old)) continue;
                    try
                    {
                        data[LEGACY[i, 1]] = System.IO.File.ReadAllText(old, Encoding.UTF8).Trim();
                        dirty = true;
                    }
                    catch (Exception dg84_1) { Diag.Swallow("Cfg.cs:84", dg84_1); }
                }
                if (dirty) Save();
            }
            catch (Exception ex)
            {
                LastLoadNote = "配置加载异常：" + ex.Message;
                try { Diag.Warn(LastLoadNote); } catch (Exception dg91_1) { Diag.Swallow("Cfg.cs:91", dg91_1); }
            }
        }

        static bool TryParse(string js, out Dictionary<string, object> d)
        {
            d = null;
            try
            {
                if (string.IsNullOrEmpty(js) || js.Trim().Length < 2) return false;
                object o = new JavaScriptSerializer().DeserializeObject(js);
                Dictionary<string, object> dd = o as Dictionary<string, object>;
                if (dd == null) return false;
                d = dd;
                return true;
            }
            catch { return false; }
        }

        static string PickDir(string baseDir)
        {
            try
            {
                string probe = Path.Combine(baseDir, ".write_test");
                System.IO.File.WriteAllText(probe, "1", new UTF8Encoding(false));
                System.IO.File.Delete(probe);
                return baseDir;
            }
            catch (Exception dg119_1) { Diag.Swallow("Cfg.cs:119", dg119_1); }
            string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniToolbox");
            try { Directory.CreateDirectory(app); } catch (Exception dg121_1) { Diag.Swallow("Cfg.cs:121", dg121_1); }
            return app;
        }

        public static string Get(string key, string def)
        {
            try { object v = data.ContainsKey(key) ? data[key] : null; return v == null ? def : Convert.ToString(v); }
            catch { return def; }
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), out v) ? v : def;
        }

        public static bool GetBool(string key, bool def)
        {
            string s = Get(key, "").Trim().ToLowerInvariant();
            if (s.Length == 0) return def;
            return !(s == "0" || s == "off" || s == "false" || s == "关");
        }

        public static void Set(string key, object val)
        {
            try { data[key] = val; dirty = true; Save(); } catch (Exception dg146_1) { Diag.Swallow("Cfg.cs:146", dg146_1); }
        }

        public static void Save()
        {
            try
            {
                if (!dirty || FilePath.Length == 0) return;
                string js = new JavaScriptSerializer().Serialize(data);
                // v19.7 原子写：先落 .tmp，再 Replace 成正式文件并留 .bak。
                // 断电 / 崩溃最多丢最后一次改动，不会留下半截 JSON 让设置全灭。
                string tmp = FilePath + ".tmp";
                System.IO.File.WriteAllText(tmp, js, new UTF8Encoding(false));
                if (System.IO.File.Exists(FilePath))
                {
                    try { System.IO.File.Replace(tmp, FilePath, FilePath + ".bak"); }
                    catch
                    {
                        try { System.IO.File.Copy(FilePath, FilePath + ".bak", true); } catch (Exception dg164_1) { Diag.Swallow("Cfg.cs:164", dg164_1); }
                        System.IO.File.Delete(FilePath);
                        System.IO.File.Move(tmp, FilePath);
                    }
                }
                else System.IO.File.Move(tmp, FilePath);
                dirty = false;
            }
            catch (Exception ex)
            {
                try { Diag.Warn("配置保存失败：" + ex.Message); } catch (Exception dg174_1) { Diag.Swallow("Cfg.cs:174", dg174_1); }
            }
        }
    }
}