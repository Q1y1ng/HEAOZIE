// 配置：一个 JSON 文件，位置固定在 exe 同级目录（绿色便携，不写注册表、不写 AppData）。
//
// 设计原则：**坏配置不许让应用起不来**。解析失败就把原文件改名留证、退回默认值，
// 并在设置页把错误显示出来 —— 静默用默认值比报错更糟，但崩掉更糟。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hz
{
    internal static class AppInfo
    {
        public const string Name = "HEAOZIE";
        public const string DisplayName = "HEAOZIE · 系统工具箱";
        public const string Version = "0.1.0";
        public const string BuildTag = "M0 骨架";
    }

    internal sealed class AppConfig
    {
        public static string RootDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar); }
        }

        public static string ConfigDir { get { return Path.Combine(RootDir, "config"); } }
        public static string ConfigPath { get { return Path.Combine(ConfigDir, "hz.json"); } }
        public static string LogDir { get { return Path.Combine(RootDir, "logs"); } }
        public static string LogPath { get { return Path.Combine(LogDir, "hz.log"); } }

        public Dictionary<string, object> Raw { get; private set; }
        public string LoadError { get; private set; }

        // ── 窗口 ────────────────────────────────────────────────
        public int WinW = 1020;
        public int WinH = 720;
        public int WinX = -1;
        public int WinY = -1;
        public bool WinMaximized;
        public int LastTab;
        public bool CloseToTray = true;
        public bool TrimOnHide = true;

        public AppConfig()
        {
            Raw = Defaults();
        }

        private static Dictionary<string, object> Defaults()
        {
            var win = new Dictionary<string, object>(StringComparer.Ordinal);
            win["w"] = 1020;
            win["h"] = 720;
            win["x"] = -1;
            win["y"] = -1;
            win["maximized"] = false;
            win["lastTab"] = 0;
            win["closeToTray"] = true;
            win["trimOnHide"] = true;

            var power = new Dictionary<string, object>(StringComparer.Ordinal);
            power["action"] = "sleep";
            power["presets"] = new List<object> { 900, 1800, 3600, 7200 };
            power["cancelWindowSec"] = 5;

            var killer = new Dictionary<string, object>(StringComparer.Ordinal);
            killer["presets"] = new List<object> { 300, 900, 1800, 3600 };
            killer["autoMinimize"] = false;
            killer["lastTargets"] = new List<object>();

            var booster = new Dictionary<string, object>(StringComparer.Ordinal);
            var cats = new Dictionary<string, object>(StringComparer.Ordinal);
            cats["browsers"] = true;
            cats["chat"] = true;
            cats["download"] = true;
            cats["music"] = true;
            cats["office"] = true;
            cats["cloud"] = true;
            cats["dev"] = false;
            cats["game"] = false;
            booster["categories"] = cats;
            booster["whitelist"] = new List<object>();
            booster["cleanTemp"] = true;
            booster["highPerformancePower"] = true;
            booster["flushDns"] = false;

            var root = new Dictionary<string, object>(StringComparer.Ordinal);
            root["window"] = win;
            root["power"] = power;
            root["killer"] = killer;
            root["booster"] = booster;
            root["configVersion"] = 1;
            return root;
        }

        /// 从 JSON 文本构造配置（Load 与自检共用）：坏输入退回默认值并把原因写进 LoadError。
        internal static AppConfig FromText(string text)
        {
            var cfg = new AppConfig();
            object parsed;
            string err;
            if (!Json.TryParse(text, out parsed, out err))
            {
                cfg.LoadError = "配置解析失败（" + err + "）";
                return cfg;
            }
            // 合并：只覆盖出现的键，缺失的沿用默认值（向前兼容旧配置）
            Merge(cfg.Raw, Json.AsObj(parsed));
            cfg.ReadFromRaw();
            return cfg;
        }

        public static AppConfig Load()
        {
            string path = ConfigPath;
            try
            {
                if (!File.Exists(path)) return new AppConfig();
                AppConfig cfg = FromText(File.ReadAllText(path, Encoding.UTF8));
                if (!string.IsNullOrEmpty(cfg.LoadError))
                {
                    // 坏配置留证改名，别让下一次启动继续踩同一个坑
                    string bad = path + ".bad";
                    try
                    {
                        if (File.Exists(bad)) File.Delete(bad);
                        File.Move(path, bad);
                        cfg.LoadError += "，已改名为 " + Path.GetFileName(bad);
                    }
                    catch { }
                    Log.Error(cfg.LoadError);
                }
                return cfg;
            }
            catch (Exception ex)
            {
                var cfg = new AppConfig();
                cfg.LoadError = "配置读取异常：" + ex.Message;
                Log.Error("配置读取异常", ex);
                return cfg;
            }
        }

        private static void Merge(Dictionary<string, object> into, Dictionary<string, object> from)
        {
            foreach (var kv in from)
            {
                var sub = kv.Value as Dictionary<string, object>;
                if (sub != null)
                {
                    object existing;
                    var target = into.TryGetValue(kv.Key, out existing) ? existing as Dictionary<string, object> : null;
                    if (target == null)
                    {
                        target = new Dictionary<string, object>(StringComparer.Ordinal);
                        into[kv.Key] = target;
                    }
                    Merge(target, sub);
                }
                else
                {
                    into[kv.Key] = kv.Value;
                }
            }
        }

        private void ReadFromRaw()
        {
            var win = Json.Obj(Raw, "window");
            WinW = Math.Max(900, Json.Int(win, "w", 1020));
            WinH = Math.Max(620, Json.Int(win, "h", 720));
            WinX = Json.Int(win, "x", -1);
            WinY = Json.Int(win, "y", -1);
            WinMaximized = Json.Bool(win, "maximized", false);
            LastTab = Json.Int(win, "lastTab", 0);
            CloseToTray = Json.Bool(win, "closeToTray", true);
            TrimOnHide = Json.Bool(win, "trimOnHide", true);
        }

        private void WriteToRaw()
        {
            var win = Json.Obj(Raw, "window");
            win["w"] = WinW;
            win["h"] = WinH;
            win["x"] = WinX;
            win["y"] = WinY;
            win["maximized"] = WinMaximized;
            win["lastTab"] = LastTab;
            win["closeToTray"] = CloseToTray;
            win["trimOnHide"] = TrimOnHide;
        }

        public bool Save()
        {
            try
            {
                WriteToRaw();
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, Json.Write(Raw, true), new UTF8Encoding(false));
                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                File.Move(tmp, ConfigPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("配置保存失败", ex);
                return false;
            }
        }

        /// 诊断用：把当前配置与运行环境导成一段可粘贴的文本。
        public string DumpDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine(AppInfo.DisplayName + "  v" + AppInfo.Version + "（" + AppInfo.BuildTag + "）");
            sb.AppendLine("exe:    " + RootDir);
            sb.AppendLine("配置:   " + ConfigPath);
            sb.AppendLine("日志:   " + LogPath);
            sb.AppendLine("权限:   " + (SysEnv.IsAdmin() ? "管理员" : "普通用户"));
            sb.AppendLine("系统:   " + SysEnv.OsVersion());
            sb.AppendLine("CLR:    " + Environment.Version + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("工作集: " + SysEnv.WorkingSetMb().ToString("0.0", CultureInfo.InvariantCulture) + " MB");
            if (!string.IsNullOrEmpty(LoadError)) sb.AppendLine("配置错误: " + LoadError);
            return sb.ToString();
        }
    }
}
