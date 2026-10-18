// 「设置」页（Fluent 版）：外观（跟随系统/深色/浅色）、运行环境、行为开关、维护、关于。

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class SettingsPanel : PanelBase
    {
        private FluentText _env;
        private FluentText _behave;
        private FluentButton _btnSystem;
        private FluentButton _btnDark;
        private FluentButton _btnLight;

        public override string Title { get { return "设置"; } }

        public SettingsPanel(IHzHost host) : base(host)
        {
            AddCard(BuildAppearanceCard(), 132);
            AddCard(BuildEnvCard(), 272);
            AddCard(BuildBehaviorCard(), 168);
            AddCard(BuildMaintenanceCard(), 108);
            AddCard(BuildAboutCard(), 196);
        }

        // ── 外观 ────────────────────────────────────────────────

        private FluentCard BuildAppearanceCard()
        {
            var card = new FluentCard("外观", "默认跟随系统；也可固定深色或浅色") { Height = 132 };

            _btnSystem = new FluentButton("跟随系统", 124, 36) { Location = new Point(16, 56) };
            _btnDark = new FluentButton("深色", 96, 36) { Location = new Point(148, 56) };
            _btnLight = new FluentButton("浅色", 96, 36) { Location = new Point(252, 56) };
            _btnSystem.Click += delegate { SetTheme("system"); };
            _btnDark.Click += delegate { SetTheme("dark"); };
            _btnLight.Click += delegate { SetTheme("light"); };
            card.Controls.Add(_btnSystem);
            card.Controls.Add(_btnDark);
            card.Controls.Add(_btnLight);

            card.Controls.Add(new FluentText("窗口背景使用 Windows 11 Mica；切换主题会立刻重绘整个界面。", Theme.Caption, Theme.TextTertiary)
            {
                Location = new Point(16, 100),
                Size = new Size(640, 18),
                Align = ContentAlignment.MiddleLeft
            });
            UpdateThemeButtons();
            return card;
        }

        private void SetTheme(string mode)
        {
            Host.Cfg.ThemeOverride = mode;
            Host.Cfg.Save();
            FluentTheme target = mode == "dark" ? FluentTheme.Dark
                : mode == "light" ? FluentTheme.Light
                : Theme.DetectSystemTheme();
            MainForm form = FindForm() as MainForm;
            if (form != null) form.ApplyTheme(target);
            UpdateThemeButtons();
            Host.SetStatus("外观：" + (mode == "system" ? "跟随系统" : mode == "dark" ? "深色" : "浅色"));
        }

        private void UpdateThemeButtons()
        {
            if (_btnSystem == null) return;
            string mode = Host.Cfg.ThemeOverride;
            _btnSystem.Selected = mode == "system" || string.IsNullOrEmpty(mode);
            _btnDark.Selected = mode == "dark";
            _btnLight.Selected = mode == "light";
            _btnSystem.Invalidate();
            _btnDark.Invalidate();
            _btnLight.Invalidate();
        }

        // ── 运行环境 ────────────────────────────────────────────

        private FluentCard BuildEnvCard()
        {
            var card = new FluentCard("运行环境", "这些路径就是出问题时要看的地方") { Height = 272 };

            _env = new FluentText("", Theme.Body, Theme.TextSecondary)
            {
                Location = new Point(16, 56),
                Size = new Size(760, 150),
                Wrap = true,
                Align = ContentAlignment.TopLeft
            };
            card.Controls.Add(_env);

            FlowLayoutPanel row = AddButtonRow(card, 44, false);
            var refresh = new FluentButton("刷新信息", 108, 32) { Glyph = Theme.Ico.Refresh };
            refresh.Click += delegate { RefreshEnv(); };
            var selftest = new FluentButton("运行自检", 108, 32) { Glyph = Theme.Ico.Check };
            selftest.Click += delegate { RunSelfTest(); };
            var copy = new FluentButton("复制诊断信息", 132, 32) { Glyph = Theme.Ico.Copy };
            copy.Click += delegate { CopyDiagnostics(); };
            row.Controls.Add(refresh);
            row.Controls.Add(selftest);
            row.Controls.Add(copy);
            RefreshEnv();
            return card;
        }

        // ── 行为 ────────────────────────────────────────────────

        private FluentCard BuildBehaviorCard()
        {
            var card = new FluentCard("行为", null) { Height = 168 };

            var tray = new FluentToggleSwitch("关闭窗口时最小化到托盘（关掉窗口后定时任务是否继续运行取决于它）", 740)
            {
                Location = new Point(16, 56),
                Checked = Host.Cfg.CloseToTray
            };
            tray.CheckedChanged += delegate
            {
                Host.Cfg.CloseToTray = tray.Checked;
                Host.Cfg.Save();
                Host.SetStatus("关闭窗口时最小化到托盘：" + (tray.Checked ? "开" : "关"));
            };
            card.Controls.Add(tray);

            var trim = new FluentToggleSwitch("隐藏时修剪工作集（把内存还给系统，代价是下次显示要重新加载页面）", 740)
            {
                Location = new Point(16, 96),
                Checked = Host.Cfg.TrimOnHide
            };
            trim.CheckedChanged += delegate
            {
                Host.Cfg.TrimOnHide = trim.Checked;
                Host.Cfg.Save();
                Host.SetStatus("隐藏时修剪工作集：" + (trim.Checked ? "开" : "关"));
            };
            card.Controls.Add(trim);

            _behave = new FluentText("", Theme.Caption, Theme.TextTertiary)
            {
                Location = new Point(16, 134),
                Size = new Size(740, 18),
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_behave);
            return card;
        }

        // ── 维护 ────────────────────────────────────────────────

        private FluentCard BuildMaintenanceCard()
        {
            var card = new FluentCard("维护", null) { Height = 108 };
            FlowLayoutPanel row = AddButtonRow(card, 44, false);
            var cfg = new FluentButton("打开配置目录", 132, 32) { Glyph = Theme.Ico.Folder };
            cfg.Click += delegate { OpenFolder(AppConfig.ConfigDir); };
            var log = new FluentButton("打开日志目录", 132, 32) { Glyph = Theme.Ico.Folder };
            log.Click += delegate { OpenFolder(AppConfig.LogDir); };
            var view = new FluentButton("查看日志", 104, 32) { Glyph = Theme.Ico.History };
            view.Click += delegate { OpenLog(); };
            var clear = new FluentButton("清空日志", 104, 32) { Glyph = Theme.Ico.Delete };
            clear.Click += delegate { ClearLog(); };
            row.Controls.Add(cfg);
            row.Controls.Add(log);
            row.Controls.Add(view);
            row.Controls.Add(clear);
            return card;
        }

        private FluentCard BuildAboutCard()
        {
            var card = new FluentCard("关于", "HEAOZIE · 系统工具箱 v" + AppInfo.Version + "（" + AppInfo.BuildTag + "）")
            { Height = 196 };
            card.Controls.Add(new FluentText(
                "由三个 Python/tkinter 小工具重构而来：游戏性能加速器、定时休眠、定时关闭应用。\n" +
                "原版完整保留在仓库的 legacy/ 目录里，只是不再作为入口（它们需要 Python + psutil，\n" +
                "并且在没装 psutil 的机器上直接报 ModuleNotFoundError）。\n\n" +
                "本应用是纯原生 Windows 程序：单文件、无控制台窗口、零运行时依赖、托盘常驻，\n" +
                "关掉窗口定时任务照常执行。界面为 Windows 11 Fluent（Mica 背景 + Segoe UI Variable + Segoe Fluent Icons）。",
                Theme.Body, Theme.TextSecondary)
            {
                Location = new Point(16, 56),
                Size = new Size(780, 130),
                Wrap = true,
                Align = ContentAlignment.TopLeft
            });
            return card;
        }

        // ── 数据刷新 ────────────────────────────────────────────

        public override void OnEnter()
        {
            RefreshEnv();
            UpdateThemeButtons();
        }

        private void RefreshEnv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("程序目录：" + AppConfig.RootDir);
            sb.AppendLine("配置文件：" + AppConfig.ConfigPath + (File.Exists(AppConfig.ConfigPath) ? "（已存在）" : "（尚未生成）"));
            sb.AppendLine("日志文件：" + AppConfig.LogPath + (File.Exists(AppConfig.LogPath) ? "（已存在）" : "（尚未生成）"));
            sb.AppendLine("权限：" + (SysEnv.IsAdmin() ? "管理员" : "普通用户（加速页需要提权的动作会请求 UAC）"));
            sb.AppendLine("系统：" + SysEnv.OsVersion() + "（Win11 特性：" + (Native.IsWindows11 ? "可用" : "不可用") + "，Mica " +
                          ((FindForm() as MainForm) != null && ((MainForm)FindForm()).MicaActive ? "已启用" : "未启用") + "）");
            sb.AppendLine("运行时：" + SysEnv.FrameworkVersion() + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("工作集：" + SysEnv.WorkingSetMb().ToString("0.0") + " MB");
            if (!string.IsNullOrEmpty(Host.Cfg.LoadError)) sb.AppendLine("⚠ " + Host.Cfg.LoadError);
            _env.SetText(sb.ToString().TrimEnd());

            if (_behave != null)
            {
                _behave.SetText("托盘常驻：关窗不退出；退出用托盘右键菜单。修剪工作集：隐藏时把内存还给系统（实测 45 MB → 9 MB）。");
            }
        }

        private void RunSelfTest()
        {
            int code = SelfTest.Run();
            string report = Path.Combine(AppConfig.LogDir, "selftest.txt");
            Host.SetStatus(code == 0 ? "自检通过" : "自检失败，见报告");
            MessageBox.Show(this,
                code == 0 ? "自检全部通过。\r\n\r\n报告：" + report : "自检有失败项，报告：" + report,
                AppInfo.DisplayName, MessageBoxButtons.OK,
                code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(Host.Cfg.DumpDiagnostics());
                Host.SetStatus("诊断信息已复制到剪贴板");
            }
            catch (Exception ex)
            {
                Host.SetStatus("复制失败：" + ex.Message);
            }
        }

        private static void OpenFolder(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + dir + "\"", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("打开目录失败: " + dir, ex);
            }
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(AppConfig.LogPath))
                {
                    Host.SetStatus("日志文件还不存在（本进程尚未写入）");
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = AppConfig.LogPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Host.SetStatus("打开日志失败：" + ex.Message);
            }
        }

        private void ClearLog()
        {
            try
            {
                if (File.Exists(AppConfig.LogPath)) File.Delete(AppConfig.LogPath);
                Host.SetStatus("日志已清空");
                RefreshEnv();
            }
            catch (Exception ex)
            {
                Host.SetStatus("清空日志失败：" + ex.Message);
            }
        }
    }
}