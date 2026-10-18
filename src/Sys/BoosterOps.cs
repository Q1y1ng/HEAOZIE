// 游戏加速的编排层：分类进程名单、结束后台进程、电源计划、DNS 刷新、一键 Turbo。
//
// 名单取自原 Python 版 config.json（同一份知识，不重新发明）。与原版的差别：
//   · 原版把 steam.exe 等游戏启动器同时写进 game_processes（可杀）与 never_kill（不杀），自相矛盾；
//     这里统一为：系统关键进程一律不杀（复用 ProcList 的安全名单），游戏启动器只受「游戏」分类控制（默认关）；
//   · 原版「恢复平衡电源计划」是写死 Balanced GUID；这里改成**记住切换前的计划**再原样恢复；
//   · 每个动作都返回可读结果，失败原因逐条写日志，不静默。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Hz.Sys
{
    internal sealed class BoosterCategories
    {
        public bool Browsers = true;
        public bool Chat = true;
        public bool Download = true;
        public bool Music = true;
        public bool Office = true;
        public bool Cloud = true;
        public bool Dev;
        public bool Game;

        public bool Get(string key)
        {
            switch (key)
            {
                case "browsers": return Browsers;
                case "chat": return Chat;
                case "download": return Download;
                case "music": return Music;
                case "office": return Office;
                case "cloud": return Cloud;
                case "dev": return Dev;
                case "game": return Game;
                default: return false;
            }
        }

        public void Set(string key, bool value)
        {
            switch (key)
            {
                case "browsers": Browsers = value; break;
                case "chat": Chat = value; break;
                case "download": Download = value; break;
                case "music": Music = value; break;
                case "office": Office = value; break;
                case "cloud": Cloud = value; break;
                case "dev": Dev = value; break;
                case "game": Game = value; break;
            }
        }
    }

    internal sealed class BoosterOptions
    {
        public bool KillProcesses = true;
        public bool TrimMemory = true;
        public bool CleanTemp = true;
        public bool HighPerformance = true;
        public bool FlushDns;
        public bool StrongTrim;
        public bool CleanPrefetch;
        public readonly BoosterCategories Categories = new BoosterCategories();
    }

    internal sealed class KillOutcome
    {
        public int Killed;
        public int Failed;
        public int SkippedProtected;
        public readonly List<string> KilledNames = new List<string>();
        public readonly List<string> Lines = new List<string>();
    }

    internal sealed class TurboReport
    {
        public readonly List<string> Steps = new List<string>();
        public KillOutcome Kill;
        public MemTrimResult Trim;
        public CleanResult Clean;
        public string PreviousPowerPlan = "";
        public string PreviousPowerPlanName = "";
        public bool PowerChanged;
        public bool DnsFlushed;
    }

    internal static class BoosterOps
    {
        private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        public static readonly string[] CategoryOrder =
        {
            "browsers", "chat", "download", "music", "office", "cloud", "dev", "game"
        };

        public static readonly Dictionary<string, string> CategoryLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "browsers", "浏览器" },
            { "chat", "聊天" },
            { "download", "下载" },
            { "music", "音乐" },
            { "office", "办公" },
            { "cloud", "云同步" },
            { "dev", "开发" },
            { "game", "游戏启动器" }
        };

        private static readonly Dictionary<string, string[]> CategoryProcesses =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "browsers", new[] { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "360chrome", "qqbrowser", "sogouexplorer", "liebao", "centbrowser" } },
            { "chat", new[] { "wechat", "wx", "qq", "tim", "dingtalk", "lark", "feishu", "slack", "discord", "telegram", "whatsapp" } },
            { "download", new[] { "xunlei", "thunder", "qbittorrent", "utorrent", "aria2c", "idman", "internetdownloadmanager", "motrix", "eagleget" } },
            { "music", new[] { "cloudmusic", "qqmusic", "kugou", "kuwo", "spotify", "foobar2000", "neteasemusic" } },
            { "office", new[] { "winword", "excel", "powerpnt", "outlook", "wps", "wpsoffice", "et", "wpp" } },
            { "cloud", new[] { "onedrive", "baidunetdisk", "baidupan", "115pan", "123pan", "yundetectservice", "dropbox" } },
            { "dev", new[] { "code", "cursor", "idea64", "pycharm64", "clion64", "webstorm64", "goland64", "sublime_text", "notepad++", "winscp", "putty", "docker desktop" } },
            { "game", new[] { "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "galaxyclient", "ubisoftconnect", "eadesktop", "ea", "origin", "epicclientlauncher", "xboxapp", "gamingservices", "gamingservicesnet", "r5apex" } }
        };

        public static string[] ProcessesOf(string category)
        {
            string[] names;
            return CategoryProcesses.TryGetValue(category, out names) ? names : new string[0];
        }

        /// 本进程自己不能被加速器杀掉（否则界面直接消失）。
        private static string SelfName()
        {
            try
            {
                using (Process me = Process.GetCurrentProcess()) return me.ProcessName.ToLowerInvariant();
            }
            catch { return "hz"; }
        }

        private static string Normalize(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return "";
            string n = processName.Trim().ToLowerInvariant();
            if (n.EndsWith(".exe", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 4);
            return n;
        }

        // ── 结束后台进程 ────────────────────────────────────────

        /// dryRun=true 时只记录"将结束哪些进程"，**一个都不杀**（自检与预览用）。
        public static KillOutcome KillBackground(BoosterCategories categories, List<string> userWhitelist, Action<string> log)
        {
            return KillBackground(categories, userWhitelist, log, false);
        }

        public static KillOutcome KillBackground(BoosterCategories categories, List<string> userWhitelist, Action<string> log, bool dryRun)
        {
            var outcome = new KillOutcome();
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in CategoryOrder)
            {
                if (!categories.Get(key)) continue;
                foreach (string name in ProcessesOf(key)) wanted.Add(Normalize(name));
            }
            if (userWhitelist != null)
            {
                foreach (string name in userWhitelist) wanted.Remove(Normalize(name));
            }

            if (wanted.Count == 0)
            {
                outcome.Lines.Add("没有勾选任何分类，跳过后台进程清理");
                if (log != null) log(outcome.Lines[0]);
                return outcome;
            }

            string self = SelfName();
            int selfPid;
            using (Process me = Process.GetCurrentProcess()) selfPid = me.Id;

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { all = new Process[0]; }

            foreach (Process p in all)
            {
                try
                {
                    if (p.Id <= 4 || p.Id == selfPid) continue;
                    string name = Normalize(p.ProcessName);
                    if (name == self) continue;
                    if (!wanted.Contains(name)) continue;

                    if (ProcList.IsProtected(name))
                    {
                        outcome.SkippedProtected++;
                        outcome.Lines.Add("跳过系统关键进程：" + name);
                        continue;
                    }

                    if (dryRun)
                    {
                        outcome.Killed++;
                        outcome.KilledNames.Add(name);
                        outcome.Lines.Add("[预演] 将结束：" + name + "（PID " + p.Id + "）");
                        continue;
                    }

                    int pid = p.Id;
                    try
                    {
                        p.Kill();
                        if (!p.WaitForExit(2000))
                        {
                            outcome.Failed++;
                            outcome.Lines.Add("结束超时：" + name + "（PID " + pid + "）");
                            continue;
                        }
                        outcome.Killed++;
                        outcome.KilledNames.Add(name);
                        outcome.Lines.Add("已结束：" + name + "（PID " + pid + "）");
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        outcome.Lines.Add("结束失败：" + name + "（PID " + pid + "）：" + ex.Message);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }

            if (outcome.Killed == 0 && outcome.Failed == 0)
            {
                outcome.Lines.Add("没有找到可结束的进程（勾选的分类里当前都没在跑）");
            }
            if (log != null)
            {
                foreach (string line in outcome.Lines) log(line);
            }
            return outcome;
        }

        // ── 电源计划 ────────────────────────────────────────────

        private static string Run(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return "";
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    return output;
                }
            }
            catch
            {
                return "";
            }
        }

        /// 当前电源计划：返回 GUID，名称通过 out 带回。
        public static string ActivePowerPlan(out string name)
        {
            name = "";
            string output = Run("powercfg.exe", "/getactivescheme", 5000);
            if (string.IsNullOrEmpty(output)) return "";
            // 形如：Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)
            int idx = output.IndexOf(':');
            if (idx < 0) return "";
            string rest = output.Substring(idx + 1).Trim();
            int space = rest.IndexOf(' ');
            string guid = space > 0 ? rest.Substring(0, space).Trim() : rest.Trim();
            int paren = rest.IndexOf('(');
            if (paren >= 0)
            {
                int close = rest.IndexOf(')', paren);
                name = close > paren ? rest.Substring(paren + 1, close - paren - 1).Trim() : rest.Substring(paren + 1).Trim();
            }
            return guid;
        }

        public static bool SetPowerPlan(string guid, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(guid)) { error = "计划 GUID 为空"; return false; }
            string output = Run("powercfg.exe", "/setactive " + guid, 8000);
            // powercfg 成功时无输出，失败信息在 stderr（这里读不到）→ 复查当前计划来判断
            string ignored;
            string now = ActivePowerPlan(out ignored);
            if (!string.Equals(now, guid, StringComparison.OrdinalIgnoreCase))
            {
                error = "切换后复查发现当前计划仍是 " + now + (string.IsNullOrEmpty(output) ? "" : "（" + output.Trim() + "）");
                return false;
            }
            return true;
        }

        /// 切到高性能计划；成功时通过 out 带回切换前的计划 GUID 与名称。
        public static bool ActivateHighPerformance(out string previousGuid, out string previousName, out string error)
        {
            error = "";
            previousGuid = ActivePowerPlan(out previousName);
            string guid = HighPerformanceGuid;
            if (!PlanExists(guid))
            {
                guid = FindPlanByName("高性能", "High performance");
                if (string.IsNullOrEmpty(guid))
                {
                    error = "本机没有「高性能」电源计划（powercfg /list 里找不到）";
                    return false;
                }
            }
            return SetPowerPlan(guid, out error);
        }

        private static bool PlanExists(string guid)
        {
            string list = Run("powercfg.exe", "/list", 5000);
            return !string.IsNullOrEmpty(list) && list.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FindPlanByName(params string[] needles)
        {
            string list = Run("powercfg.exe", "/list", 5000);
            if (string.IsNullOrEmpty(list)) return "";
            foreach (string raw in list.Replace("\r", "").Split('\n'))
            {
                string line = raw;
                foreach (string needle in needles)
                {
                    if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int idx = line.IndexOf(':');
                    if (idx < 0) continue;
                    string rest = line.Substring(idx + 1).Trim();
                    int space = rest.IndexOf(' ');
                    string guid = space > 0 ? rest.Substring(0, space).Trim() : rest.Trim();
                    if (guid.Length >= 32) return guid;
                }
            }
            return "";
        }

        public static bool RestorePowerPlan(string guid, out string error)
        {
            if (string.IsNullOrEmpty(guid))
            {
                error = "没有记录到切换前的电源计划，无法恢复";
                return false;
            }
            return SetPowerPlan(guid, out error);
        }

        // ── DNS ────────────────────────────────────────────────

        public static bool FlushDns(out string error)
        {
            error = "";
            string output = Run("ipconfig.exe", "/flushdns", 8000);
            if (string.IsNullOrEmpty(output))
            {
                error = "ipconfig 没有返回内容（可能被安全软件拦截）";
                return false;
            }
            return true;
        }

        // ── 一键 Turbo ─────────────────────────────────────────

        public static TurboReport RunTurbo(BoosterOptions opt, List<string> userWhitelist, Action<string> log)
        {
            return RunTurbo(opt, userWhitelist, log, false);
        }

        /// dryRun=true：走完整编排与日志，但后台进程只预演、内存/清理/电源/DNS 一律不执行。
        public static TurboReport RunTurbo(BoosterOptions opt, List<string> userWhitelist, Action<string> log, bool dryRun)
        {
            var report = new TurboReport();
            Action<string> emit = delegate(string line)
            {
                report.Steps.Add(line);
                if (log != null) log(line);
            };

            emit("=== 一键 Turbo 开始（" + DateTime.Now.ToString("HH:mm:ss") + "）===");

            if (opt.KillProcesses)
            {
                emit("── 结束后台进程 ──" + (dryRun ? "（预演：不会真的结束）" : ""));
                report.Kill = KillBackground(opt.Categories, userWhitelist, log, dryRun);
                emit("后台进程：" + (dryRun ? "预演命中 " : "已结束 ") + report.Kill.Killed + " 个，失败 " + report.Kill.Failed +
                     " 个，跳过系统进程 " + report.Kill.SkippedProtected + " 个");
            }

            if (opt.TrimMemory && !dryRun)
            {
                emit("── 内存整理 ──");
                report.Trim = MemTrim.Run(opt.StrongTrim);
                emit("模式：" + report.Trim.Mode);
                foreach (string line in report.Trim.Lines) emit(line);
            }
            else if (opt.TrimMemory)
            {
                emit("── 内存整理 ──（预演：跳过）");
            }

            if (opt.CleanTemp && !dryRun)
            {
                emit("── 临时文件清理 ──");
                report.Clean = Cleanup.Clean(opt.CleanPrefetch && SysEnv.IsAdmin(), log);
                emit("临时文件：释放约 " + report.Clean.FreedMb.ToString("0.0") + " MB，失败 " +
                     report.Clean.Failed + " 项（占用中的文件属正常）");
            }
            else if (opt.CleanTemp)
            {
                emit("── 临时文件清理 ──（预演：跳过）");
            }

            if (opt.HighPerformance && !dryRun)
            {
                emit("── 电源计划 ──");
                string previous, previousName, error;
                if (ActivateHighPerformance(out previous, out previousName, out error))
                {
                    report.PowerChanged = true;
                    report.PreviousPowerPlan = previous;
                    report.PreviousPowerPlanName = previousName;
                    emit("已切到「高性能」电源计划（原计划：" + (string.IsNullOrEmpty(previousName) ? previous : previousName) + "）");
                }
                else
                {
                    emit("切换电源计划失败：" + error);
                }
            }

            if (opt.FlushDns && !dryRun)
            {
                emit("── DNS 刷新 ──");
                string error;
                report.DnsFlushed = FlushDns(out error);
                emit(report.DnsFlushed ? "已刷新 DNS 缓存" : "刷新 DNS 失败：" + error);
            }
            else if (opt.FlushDns)
            {
                emit("── DNS 刷新 ──（预演：跳过）");
            }

            emit("=== 一键 Turbo 完成 ===");
            return report;
        }

        public static string DescribeCategories(BoosterCategories categories)
        {
            var sb = new StringBuilder();
            foreach (string key in CategoryOrder)
            {
                if (!categories.Get(key)) continue;
                if (sb.Length > 0) sb.Append("、");
                sb.Append(CategoryLabels[key]);
            }
            return sb.Length == 0 ? "（未勾选）" : sb.ToString();
        }
    }
}
