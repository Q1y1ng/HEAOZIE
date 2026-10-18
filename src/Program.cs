// 入口：单实例、参数解析、全局异常兜底、提权重启。
//
// 无控制台：编译目标是 -target:winexe，进程本身就是 GUI 进程 —— 这正是
// 「不想每次用都先开 cmd」这条需求的落点。控制台输出只在被重定向时才有意义
// （自检就是这样跑起来的）。

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Hz
{
    internal static class Program
    {
        private sealed class Options
        {
            public int Tab = -1;
            public bool SelfTest;
            public bool StartMinimized;
            public int ExitAfterSeconds;
            public int DemoPowerSeconds;
            public int DemoKillerSeconds;
            public bool DemoBooster;
            public bool Help;
        }

        private const string Usage =
            "HEAOZIE · 系统工具箱\r\n" +
            "  HZ.exe                  启动主界面（重复启动会唤起已运行的实例）\r\n" +
            "  HZ.exe --tab=power      直接打开指定页：booster|power|killer|settings（或 0-3）\r\n" +
            "  HZ.exe --minimized      启动后直接进托盘\r\n" +
            "  HZ.exe --selftest       无界面自检，结果写入 logs\\selftest.txt，退出码 0/1\r\n" +
            "  HZ.exe --exit-after=5   启动 5 秒后自动退出（冒烟测试用）\r\n" +
            "  HZ.exe --power-demo=5   演示定时电源全流程（5 秒倒计时 + 撤销窗口），不真的执行\r\n" +
            "  HZ.exe --killer-demo=5  演示定时关闭应用全流程（不真的结束任何进程）\r\n" +
            "  HZ.exe --booster-demo   演示游戏加速的预演 Turbo（不真的结束进程/清内存/清文件）\r\n";

        [STAThread]
        private static int Main(string[] args)
        {
            Options opt = ParseArgs(args);

            if (opt.Help)
            {
                Console.WriteLine(Usage);
                return 0;
            }
            if (opt.SelfTest)
            {
                return SelfTest.Run();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\Hz_HeaoZie_SingleInstance_v1", out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例：广播唤醒它，然后本进程立刻退出（不再多占一份内存）。
                    Native.PostMessage(Native.HWND_BROADCAST, Native.WM_HZ_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Fatal("界面线程异常", e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Fatal("未处理异常", e.ExceptionObject as Exception); };

                Log.Info("启动 v" + AppInfo.Version + "（" + AppInfo.BuildTag + "）args=[" + string.Join(" ", args) + "]");

                AppConfig cfg = AppConfig.Load();
                if (!string.IsNullOrEmpty(cfg.LoadError)) Log.Warn(cfg.LoadError);

                var form = new MainForm(cfg, opt.Tab, opt.DemoPowerSeconds, opt.DemoKillerSeconds, opt.DemoBooster)
                {
                    StartMinimized = opt.StartMinimized,
                    AutoExitSeconds = opt.ExitAfterSeconds
                };
                Application.Run(form);

                if (MainForm.ElevatedRestartRequested)
                {
                    RelaunchElevated(MainForm.RestartTab);
                }

                Log.Info("退出（工作集 " + SysEnv.WorkingSetMb().ToString("0.0") + " MB）");
                return 0;
            }
        }

        private static void RelaunchElevated(int tab)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--tab=" + tab,
                    Verb = "runas",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // 用户在 UAC 上点了「否」也走这里，不该当成崩溃
                Log.Error("提权重启未完成（可能被 UAC 拒绝）", ex);
            }
        }

        private static void Fatal(string what, Exception ex)
        {
            Log.Error(what, ex);
            try
            {
                MessageBox.Show(what + "：\r\n" + (ex == null ? "(无异常对象)" : ex.Message) +
                                "\r\n\r\n详情见：" + AppConfig.LogPath,
                    AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        private static Options ParseArgs(string[] args)
        {
            var o = new Options();
            if (args == null) return o;
            foreach (string raw in args)
            {
                string a = (raw ?? "").Trim();
                if (a.Length == 0) continue;
                string lower = a.ToLowerInvariant();

                if (lower == "--selftest" || lower == "-selftest" || lower == "/selftest") { o.SelfTest = true; continue; }
                if (lower == "--minimized" || lower == "-minimized") { o.StartMinimized = true; continue; }
                if (lower == "--help" || lower == "-h" || lower == "/?" || lower == "-?") { o.Help = true; continue; }

                if (lower.StartsWith("--tab="))
                {
                    string v = lower.Substring(6).Trim();
                    int n;
                    if (int.TryParse(v, out n)) o.Tab = n;
                    else if (v == "booster" || v == "boost" || v == "加速") o.Tab = 0;
                    else if (v == "power" || v == "sleep" || v == "定时电源") o.Tab = 1;
                    else if (v == "killer" || v == "kill" || v == "定时关闭") o.Tab = 2;
                    else if (v == "settings" || v == "设置") o.Tab = 3;
                    continue;
                }

                if (lower.StartsWith("--exit-after="))
                {
                    int n;
                    if (int.TryParse(lower.Substring(13).Trim(), out n) && n > 0) o.ExitAfterSeconds = n;
                    continue;
                }

                if (lower.StartsWith("--power-demo="))
                {
                    int n;
                    if (int.TryParse(lower.Substring(13).Trim(), out n) && n > 0) { o.DemoPowerSeconds = n; o.Tab = 1; }
                    continue;
                }

                if (lower.StartsWith("--killer-demo="))
                {
                    int n;
                    if (int.TryParse(lower.Substring(14).Trim(), out n) && n > 0) { o.DemoKillerSeconds = n; o.Tab = 2; }
                    continue;
                }

                if (lower == "--booster-demo")
                {
                    o.DemoBooster = true;
                    o.Tab = 0;
                    continue;
                }
            }
            return o;
        }
    }
}
