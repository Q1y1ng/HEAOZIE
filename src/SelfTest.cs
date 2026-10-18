// 自检：不开界面也能验证核心逻辑（JSON / 配置 / 主题 / 路径 / 面板构建）。
//
// 为什么必须有它：GUI 程序很难在自动化里"看一眼"，但把每个里程碑的判定逻辑
// 收进自检后，改动的对错就有了可重复的判据，而不是靠人点一遍。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Hz.Panels;
using Hz.Sys;

namespace Hz
{
    internal static class SelfTest
    {
        private static int _fail;
        private static readonly StringBuilder Report = new StringBuilder();

        public static int Run()
        {
            _fail = 0;
            Report.Clear();
            Report.AppendLine(AppInfo.DisplayName + " 自检   v" + AppInfo.Version + "   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Report.AppendLine("程序目录：" + AppConfig.RootDir);
            Report.AppendLine("运行时：  " + SysEnv.FrameworkVersion() + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
            Report.AppendLine("权限：    " + (SysEnv.IsAdmin() ? "管理员" : "普通用户"));
            Report.AppendLine(new string('-', 64));

            Check("JSON 解析与往返", TestJson);
            Check("JSON 坏输入容错", TestJsonBad);
            Check("配置默认值与部分覆盖", TestConfig);
            Check("倒计时（含睡眠漂移）", TestCountdown);
            Check("电源操作映射", TestPowerOps);
            Check("进程枚举与安全名单", TestProcList);
            Check("进程表与时间卡", TestWidgets);
            Check("加速器名单与编排", TestBooster);
            Check("清理白名单与安全闸门", TestCleanup);
            Check("系统状态采集", TestSystemInfo);
            Check("主题与运行时图标", TestTheme);
            Check("单实例消息注册", TestNative);
            Check("进程枚举", TestProcess);
            Check("目录与日志可写", TestPaths);
            Check("四个面板可构建", TestPanels);

            Report.AppendLine(new string('-', 64));
            Report.AppendLine(_fail == 0 ? "结果：全部通过" : "结果：失败 " + _fail + " 项");

            string text = Report.ToString();
            try
            {
                if (!Directory.Exists(AppConfig.LogDir)) Directory.CreateDirectory(AppConfig.LogDir);
                File.WriteAllText(Path.Combine(AppConfig.LogDir, "selftest.txt"), text, new UTF8Encoding(false));
            }
            catch { }
            Console.WriteLine(text);
            return _fail == 0 ? 0 : 1;
        }

        private static void Check(string name, Func<string> test)
        {
            string err = "";
            try { err = test() ?? ""; }
            catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
            if (string.IsNullOrEmpty(err)) Report.AppendLine("[ OK ] " + name);
            else { _fail++; Report.AppendLine("[FAIL] " + name + " -> " + err); }
        }

        // ── 各项检查 ────────────────────────────────────────────

        private static string TestJson()
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            map["num"] = 42.0;
            map["text"] = "含\"引号\"与\n换行\\反斜杠";
            map["flag"] = true;
            map["nothing"] = null;
            var list = new List<object>();
            list.Add("x");
            list.Add(3.5);
            map["arr"] = list;

            string text = Json.Write(map, true);
            object back = Json.Parse(text);
            var bm = Json.AsObj(back);
            if (Json.Int(bm, "num", 0) != 42) return "数字往返失败";
            if (Json.Str(bm, "text", "") != "含\"引号\"与\n换行\\反斜杠") return "字符串转义往返失败";
            if (!Json.Bool(bm, "flag", false)) return "布尔往返失败";
            if (bm["nothing"] != null) return "null 往返失败";
            List<object> arr = Json.List(bm, "arr");
            if (arr.Count != 2) return "数组长度往返失败（" + arr.Count + "）";
            if (Math.Abs(Json.Num(bm, "num", 0) - 42) > 0.0001) return "浮点往返失败";
            if (Json.Write(map, false).Contains("\n")) return "紧凑模式不该有换行";
            return "";
        }

        private static string TestJsonBad()
        {
            object v;
            string err;
            if (Json.TryParse("{oops", out v, out err)) return "坏 JSON 竟然解析成功";
            if (string.IsNullOrEmpty(err)) return "坏 JSON 没有给出错误原因";
            if (!Json.TryParse("{\"a\":[1,2,{\"b\":\"c\"}]}", out v, out err)) return "合法 JSON 被判为坏";
            if (Json.AsObj(v)["a"] == null) return "嵌套数组丢失";
            return "";
        }

        private static string TestConfig()
        {
            var fresh = new AppConfig();
            if (fresh.WinW != 1020 || fresh.WinH != 720) return "默认窗口尺寸不对";
            if (!fresh.CloseToTray) return "默认应当「关窗进托盘」";
            if (Json.Obj(fresh.Raw, "booster").Count == 0) return "默认配置缺少 booster 段";

            // 部分覆盖：只给 window.w，其余必须保留默认值
            AppConfig partial = AppConfig.FromText("{\"window\":{\"w\":1111}}");
            if (partial.WinW != 1111) return "部分覆盖未生效（w=" + partial.WinW + "）";
            if (partial.WinH != 720) return "部分覆盖把未提及的键清掉了";
            if (Json.Obj(partial.Raw, "killer").Count == 0) return "部分覆盖把别的段清掉了";

            // 坏输入：必须退回默认值且留下错误信息，而不是抛异常
            AppConfig bad = AppConfig.FromText("{ 这不是 JSON");
            if (string.IsNullOrEmpty(bad.LoadError)) return "坏配置没有记录错误";
            if (bad.WinW != 1020) return "坏配置没有退回默认值";
            return "";
        }

        private static string TestCountdown()
        {
            // 注入假时钟：模拟「睡眠 3 小时」，验证唤醒后立刻到点而不是继续慢慢数
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0);
            var cd = new Countdown(delegate { return now; });

            cd.Start(TimeSpan.FromMinutes(30));
            if (cd.Deadline != new DateTime(2026, 1, 1, 12, 30, 0)) return "目标时刻计算错误";

            now = now.AddMinutes(10);
            if (Math.Abs((cd.Remaining - TimeSpan.FromMinutes(20)).TotalSeconds) > 0.5) return "剩余时间计算错误";

            cd.Pause();
            now = now.AddMinutes(5);
            if (Math.Abs((cd.Remaining - TimeSpan.FromMinutes(20)).TotalSeconds) > 0.5) return "暂停期间剩余时间不该变化";

            cd.Resume();
            now = now.AddMinutes(19);
            if (cd.Poll()) return "未到点却报已到点";

            now = now.AddHours(3);   // ← 睡眠 3 小时
            if (!cd.Poll()) return "睡眠唤醒后没有立即到点（漂移缺陷没修掉）";
            if (cd.Poll()) return "到点后应当只触发一次";
            if (cd.Remaining != TimeSpan.Zero) return "到点后剩余时间应为 0";

            cd.Start(TimeSpan.FromMinutes(1));
            if (!cd.IsArmed) return "重新开始失败";
            cd.Reset();
            if (cd.IsArmed || cd.Remaining != TimeSpan.Zero) return "重置后仍在计时";

            if (Countdown.Format(new TimeSpan(1, 2, 3)) != "01:02:03") return "格式化错误";
            if (Countdown.Format(new TimeSpan(0, 0, 7)) != "00:00:07") return "补零错误";

            bool threw = false;
            try { cd.Start(TimeSpan.Zero); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            if (!threw) return "0 时长应当被拒绝";
            return "";
        }

        private static string TestPowerOps()
        {
            if (PowerOps.Parse("sleep", PowerAction.Restart) != PowerAction.Sleep) return "sleep 解析错误";
            if (PowerOps.Parse("shutdown", PowerAction.Sleep) != PowerAction.Shutdown) return "shutdown 解析错误";
            if (PowerOps.Parse("restart", PowerAction.Sleep) != PowerAction.Restart) return "restart 解析错误";
            if (PowerOps.Parse("", PowerAction.Restart) != PowerAction.Restart) return "空值应当回退";
            if (PowerOps.Parse("胡说", PowerAction.Sleep) != PowerAction.Sleep) return "非法值应当回退";

            if (PowerOps.ToConfig(PowerAction.Sleep) != "sleep") return "ToConfig(sleep) 错误";
            if (PowerOps.Parse(PowerOps.ToConfig(PowerAction.Shutdown), PowerAction.Sleep) != PowerAction.Shutdown)
                return "配置往返不一致";
            if (PowerOps.Label(PowerAction.Restart) != "重启") return "标签错误";
            if (string.IsNullOrEmpty(PowerOps.Verb(PowerAction.Sleep))) return "动词为空";
            return "";
        }

        private static string TestProcList()
        {
            List<ProcEntry> rows = ProcList.Enumerate();
            if (rows.Count < 3) return "枚举到的可关闭进程太少（" + rows.Count + "）";
            foreach (ProcEntry e in rows)
            {
                if (e.Pid <= 0) return "出现非法 PID";
                if (string.IsNullOrEmpty(e.Name)) return "出现空进程名";
                if (ProcList.IsProtected(e.Name)) return "安全名单里的进程混进了列表：" + e.Name;
            }
            if (!ProcList.IsProtected("explorer")) return "explorer 应当在安全名单里";
            if (!ProcList.IsProtected("svchost.exe")) return "svchost.exe 应当在安全名单里";
            if (!ProcList.IsProtected("EXPLORER.EXE")) return "安全名单应当忽略大小写";
            if (ProcList.IsProtected("notepad.exe")) return "notepad.exe 不该被保护";
            if (ProcList.DisplayName("chrome") != "chrome.exe") return "显示名补 .exe 失败";
            if (ProcList.DisplayName("System") != "System") return "System 不该补 .exe";
            if (ProcList.NameOf(999999) != "") return "不存在的 PID 应当返回空名字";

            string err;
            if (ProcList.Kill(999999, out err)) return "结束不存在的 PID 竟然成功";
            if (string.IsNullOrEmpty(err)) return "结束失败没有给出原因";

            int self;
            using (Process me = Process.GetCurrentProcess()) self = me.Id;
            if (!ProcList.IsSelf(self)) return "IsSelf 判断失败";
            return "";
        }

        private static string TestWidgets()
        {
            var table = new ProcTable();
            var setup = new TimeSetupCard("设定倒计时", "自检", new List<int> { 300, 900 }, 86);
            try
            {
                var rows = new List<ProcEntry>();
                rows.Add(new ProcEntry { Pid = 111, Name = "aaa.exe", MemoryMb = 10 });
                rows.Add(new ProcEntry { Pid = 222, Name = "bbb.exe", MemoryMb = 20 });
                table.SetRows(rows);
                if (table.RowCount != 2) return "SetRows 后行数不对";
                if (table.CheckedPids.Count != 0) return "初始不该有勾选";
                table.CheckAll();
                if (table.CheckedPids.Count != 2) return "全选失败";
                if (!table.IsChecked(111)) return "全选后仍报未勾选";
                table.UncheckAll();
                if (table.CheckedPids.Count != 0) return "取消全选失败";
                table.SetChecked(222, true);
                if (table.CheckedPids.Count != 1 || table.CheckedPids[0] != 222) return "单勾失败";
                if (!table.HighlightPid(222)) return "高亮已存在的 PID 失败";
                if (table.HighlightPid(999)) return "高亮不存在的 PID 竟然成功";
                // 进程退出后过期勾选必须被清掉（否则会拿着旧 PID 去结束）
                table.SetRows(rows.GetRange(0, 1));
                if (table.CheckedPids.Count != 0) return "过期勾选没有被清理";

                setup.SetSeconds(900);
                if (setup.Total != TimeSpan.FromMinutes(15)) return "SetSeconds 换算错误";
                setup.SetSeconds(3661);
                if (setup.Total != new TimeSpan(1, 1, 1)) return "SetSeconds 时分秒换算错误";
                setup.Lock(true);
                setup.SetSeconds(60);
                if (setup.Total != new TimeSpan(1, 1, 1)) return "锁定时不该还能改时间";
                setup.Lock(false);
                setup.SetSeconds(60);
                if (setup.Total != TimeSpan.FromMinutes(1)) return "解锁后改不动时间";
                if (TimeSetupCard.PresetLabel(900) != "15 分钟") return "预设标签错误";
                if (TimeSetupCard.PresetLabel(7200) != "2 小时") return "预设标签（小时）错误";
            }
            finally
            {
                table.Dispose();
                setup.Dispose();
            }
            return "";
        }

        private static string TestBooster()
        {
            foreach (string key in BoosterOps.CategoryOrder)
            {
                string label;
                if (!BoosterOps.CategoryLabels.TryGetValue(key, out label) || string.IsNullOrEmpty(label))
                    return "分类 " + key + " 缺少中文标签";
                if (BoosterOps.ProcessesOf(key).Length == 0) return "分类 " + key + " 没有进程名单";
            }
            if (BoosterOps.CategoryOrder.Length != 8) return "分类数量不是 8";

            var cats = new BoosterCategories();
            if (cats.Browsers != true) return "浏览器分类默认应当勾选";
            if (cats.Game) return "游戏启动器分类默认应当关闭（误杀风险）";
            cats.Set("dev", true);
            if (!cats.Get("dev")) return "分类 Set/Get 不一致";
            string desc = BoosterOps.DescribeCategories(cats);
            if (desc.IndexOf("开发", StringComparison.Ordinal) < 0) return "分类描述缺少已勾选分类";

            // 电源计划读取：本机必然有 powercfg，取不到也应安全返回空串而不是抛异常
            string name;
            string guid = BoosterOps.ActivePowerPlan(out name);
            if (!string.IsNullOrEmpty(guid) && guid.Length < 32) return "电源计划 GUID 解析异常：" + guid;

            // 白名单排除逻辑：把浏览器全加白名单后，不应该再有浏览器在目标集合里
            var whitelist = new List<string>();
            var whiteSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in BoosterOps.ProcessesOf("browsers"))
            {
                whitelist.Add(n + ".exe");
                whiteSet.Add(n);
            }
            KillOutcome outcome = BoosterOps.KillBackground(cats, whitelist, null, true);   // dryRun：只预演，绝不真结束进程
            foreach (string killed in outcome.KilledNames)
            {
                string bare = killed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? killed.Substring(0, killed.Length - 4) : killed;
                if (whiteSet.Contains(bare)) return "白名单里的进程被结束了：" + killed;
            }
            if (outcome.Killed > 0 && outcome.Lines.Count == 0) return "结束了进程却没有记录原因";
            return "";
        }

        private static string TestCleanup()
        {
            List<string> dirs = Cleanup.TargetDirs(false);
            if (dirs.Count < 3) return "清理目标目录太少（" + dirs.Count + "）";
            bool hasTemp = false;
            foreach (string entry in dirs)
            {
                string dir = entry.Substring(0, entry.LastIndexOf('|'));
                if (string.Equals(dir.TrimEnd('\\'),
                        (Environment.GetEnvironmentVariable("TEMP") ?? "").TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase)) hasTemp = true;
                if (!Cleanup.IsSafeTarget(dir)) return "白名单目录被判为不安全：" + dir;
            }
            if (!hasTemp) return "清理目标里没有 %TEMP%";

            // 安全闸门：盘根 / Windows 目录 / 用户主目录都必须拒绝
            if (Cleanup.IsSafeTarget("C:\\")) return "盘根竟然被判为可清理";
            if (Cleanup.IsSafeTarget(Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
                return "Windows 目录竟然被判为可清理";
            if (Cleanup.IsSafeTarget(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                return "用户主目录竟然被判为可清理";

            double mb = Cleanup.Measure();      // 只测量，不删除
            if (mb < 0) return "测量返回负数";
            return "";
        }

        private static string TestSystemInfo()
        {
            var monitor = new SystemMonitor();
            monitor.Capture(false, false);
            SystemSnapshot snap = monitor.Capture(false, false);
            if (snap.MemTotalMb <= 0) return "内存总量读取失败";
            if (snap.AvailableMb <= 0) return "可用内存读取失败";
            if (snap.MemPercent < 0 || snap.MemPercent > 100) return "内存占用百分比越界：" + snap.MemPercent;
            if (snap.CpuPercent < 0 || snap.CpuPercent > 100) return "CPU 占用百分比越界：" + snap.CpuPercent;
            if (snap.Drives.Count == 0) return "没有枚举到固定磁盘";
            foreach (DriveInfoRow d in snap.Drives)
            {
                if (d.TotalGb <= 0) return "磁盘 " + d.Letter + " 容量异常";
                if (d.UsedPercent < 0 || d.UsedPercent > 100) return "磁盘 " + d.Letter + " 占用率越界";
            }
            if (SystemMonitor.AvailableMb() <= 0) return "静态可用内存读取失败";
            // 提权助手不该抛异常（拿不到权限是正常的，这里只看不炸）
            MemTrim.EnablePrivilege("SeProfileSingleProcessPrivilege");
            return "";
        }

        private static string TestTheme()
        {
            if (Theme.FontUi == null || Theme.FontMono == null) return "字体未初始化";
            using (Icon app = Theme.AppIcon(16)) { if (app == null) return "应用图标创建失败"; }
            using (Icon dot = Theme.DotIcon(Theme.Accent, 16)) { if (dot == null) return "托盘圆点图标创建失败"; }
            using (Icon badged = Theme.AppIconWithDot(16, Theme.Warn)) { if (badged == null) return "带状态点的托盘图标创建失败"; }
            using (var path = Theme.Path(new RectangleF(0, 0, 20, 20), 6))
            {
                if (path.PointCount < 4) return "圆角路径点数异常";
            }
            if (Theme.Alpha(Color.Black, 128).A != 128) return "透明度助手不对";
            return "";
        }

        private static string TestNative()
        {
            if (Native.WM_HZ_ACTIVATE == 0) return "单实例唤醒消息注册失败";
            return "";
        }

        private static string TestProcess()
        {
            Process[] all = Process.GetProcesses();
            if (all.Length < 5) return "枚举到的进程数异常（" + all.Length + "）";
            foreach (Process p in all) p.Dispose();
            return "";
        }

        private static string TestPaths()
        {
            if (!Directory.Exists(AppConfig.RootDir)) return "程序目录不存在";
            if (!Directory.Exists(AppConfig.LogDir)) Directory.CreateDirectory(AppConfig.LogDir);
            string probe = Path.Combine(AppConfig.LogDir, "selftest-probe.tmp");
            File.WriteAllText(probe, "probe", new UTF8Encoding(false));
            string back = File.ReadAllText(probe, Encoding.UTF8);
            File.Delete(probe);
            if (back != "probe") return "写入后读回不一致";
            Log.Info("自检运行");
            if (!File.Exists(AppConfig.LogPath)) return "日志文件没有生成";
            return "";
        }

        private static string TestPanels()
        {
            var host = new StubHost();
            PanelBase[] panels =
            {
                new BoosterPanel(host),
                new PowerPanel(host),
                new KillerPanel(host),
                new SettingsPanel(host)
            };
            try
            {
                foreach (PanelBase p in panels)
                {
                    if (string.IsNullOrEmpty(p.Title)) return "有面板没有标题";
                    if (p.Controls.Count == 0) return "面板「" + p.Title + "」没有任何控件";
                }
                if (panels[0].Title == panels[3].Title) return "面板标题重复";
            }
            finally
            {
                foreach (PanelBase p in panels) p.Dispose();
            }
            return "";
        }

        private sealed class StubHost : IHzHost
        {
            private readonly AppConfig _cfg = new AppConfig();
            public AppConfig Cfg { get { return _cfg; } }
            public void SetStatus(string text) { }
            public void Log(string msg) { }
            public void ShowMain() { }
            public void SelectTab(int index) { }
            public void SetTrayStatus(string text) { }
            public void SetTrayColor(Color color) { }
            public void Notify(string title, string text) { }
        }
    }
}
