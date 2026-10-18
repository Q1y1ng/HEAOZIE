// 「设置」页：运行环境、行为开关、维护动作、关于。
// 这一页在 M0 就是完整可用的 —— 它是排查问题的入口，不该等里程碑。

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
        private Label _env;
        private Label _behave;

        public override string Title { get { return "设置"; } }

        public SettingsPanel(IHzHost host) : base(host)
        {
            BuildEnvCard();
            BuildBehaviorCard();
            BuildMaintenanceCard();
            BuildAboutCard();
        }

        private Label InfoLabel(SoftCard card, int height)
        {
            var lbl = new Label
            {
                Dock = DockStyle.Top,
                Height = height,
                AutoSize = false,
                BackColor = Theme.Card,
                ForeColor = Theme.TextDim,
                Font = Theme.FontUi
            };
            card.Controls.Add(lbl);
            return lbl;
        }

        private void BuildEnvCard()
        {
            SoftCard card = AddCard("运行环境", "这些路径就是出问题时要看的地方", null, 216);
            _env = InfoLabel(card, 138);
            FlowLayoutPanel row = AddButtonRow(card, false);
            row.Controls.Add(Btn("刷新信息", 96, false, delegate { RefreshEnv(); }));
            row.Controls.Add(Btn("运行自检", 96, false, delegate { RunSelfTest(); }));
            row.Controls.Add(Btn("复制诊断信息", 116, false, delegate { CopyDiagnostics(); }));
            RefreshEnv();
        }

        private void BuildBehaviorCard()
        {
            SoftCard card = AddCard("行为", null, null, 130);
            _behave = InfoLabel(card, 52);
            FlowLayoutPanel row = AddButtonRow(card, false);
            row.Controls.Add(Btn("切换：关闭时最小化到托盘", 190, false, delegate
            {
                Host.Cfg.CloseToTray = !Host.Cfg.CloseToTray;
                Host.Cfg.Save();
                RefreshBehavior();
                Host.SetStatus("关闭窗口时最小化到托盘：" + (Host.Cfg.CloseToTray ? "开" : "关"));
            }));
            row.Controls.Add(Btn("切换：隐藏时修剪工作集", 172, false, delegate
            {
                Host.Cfg.TrimOnHide = !Host.Cfg.TrimOnHide;
                Host.Cfg.Save();
                RefreshBehavior();
                Host.SetStatus("隐藏时修剪工作集：" + (Host.Cfg.TrimOnHide ? "开" : "关"));
            }));
            RefreshBehavior();
        }

        private void BuildMaintenanceCard()
        {
            SoftCard card = AddCard("维护", null, null, 130);
            FlowLayoutPanel row = AddButtonRow(card, false);
            row.Controls.Add(Btn("打开配置目录", 116, false, delegate { OpenFolder(AppConfig.ConfigDir); }));
            row.Controls.Add(Btn("打开日志目录", 116, false, delegate { OpenFolder(AppConfig.LogDir); }));
            row.Controls.Add(Btn("查看日志", 88, false, delegate { OpenLog(); }));
            row.Controls.Add(Btn("清空日志", 88, false, delegate { ClearLog(); }));
        }

        private void BuildAboutCard()
        {
            AddCard("关于", "HEAOZIE · 系统工具箱 v" + AppInfo.Version + "（" + AppInfo.BuildTag + "）",
                "由三个 Python/tkinter 小工具重构而来：游戏性能加速器、定时休眠、定时关闭应用。\n" +
                "原版仍完整保留在仓库的 legacy/ 目录里，只是不再作为入口（它们需要 Python + psutil，\n" +
                "并且在没装 psutil 的机器上直接报 ModuleNotFoundError）。\n\n" +
                "本应用是纯原生 Windows 程序：单文件、无控制台窗口、零运行时依赖、托盘常驻，\n" +
                "关掉窗口定时任务照常执行。",
                170);
        }

        public override void OnEnter()
        {
            RefreshEnv();
            RefreshBehavior();
        }

        private void RefreshEnv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("程序目录：" + AppConfig.RootDir);
            sb.AppendLine("配置文件：" + AppConfig.ConfigPath + (File.Exists(AppConfig.ConfigPath) ? "（已存在）" : "（尚未生成）"));
            sb.AppendLine("日志文件：" + AppConfig.LogPath + (File.Exists(AppConfig.LogPath) ? "（已存在）" : "（尚未生成）"));
            sb.AppendLine("权限：" + (SysEnv.IsAdmin() ? "管理员" : "普通用户（加速页需要提权的动作会请求 UAC）"));
            sb.AppendLine("系统：" + SysEnv.OsVersion());
            sb.AppendLine("运行时：" + SysEnv.FrameworkVersion() + " / " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("工作集：" + SysEnv.WorkingSetMb().ToString("0.0") + " MB");
            if (!string.IsNullOrEmpty(Host.Cfg.LoadError)) sb.AppendLine("⚠ " + Host.Cfg.LoadError);
            _env.Text = sb.ToString();
        }

        private void RefreshBehavior()
        {
            _behave.Text = "· 关闭窗口时最小化到托盘：" + (Host.Cfg.CloseToTray ? "开" : "关") +
                           "（关掉窗口后定时任务是否继续运行取决于它）\r\n" +
                           "· 隐藏时修剪工作集：" + (Host.Cfg.TrimOnHide ? "开" : "关") +
                           "（把内存还给系统，代价是下次显示要重新加载页面）";
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
