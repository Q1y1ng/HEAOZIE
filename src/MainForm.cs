// 主窗口：顶部信息条 + 分段 Tab + 懒加载内容区 + 状态栏；关窗进托盘、单实例、可提权重启。
//
// 关闭语义（用户已确认）：**关窗不退出**，定时任务继续跑；要退出用托盘菜单的「退出」。
// 这是「定时」类工具的硬要求 —— 进程死了，定时器就不存在了。

using System;
using System.Drawing;
using System.Windows.Forms;
using Hz.Panels;
using Microsoft.Win32;

namespace Hz
{
    internal sealed class MainForm : Form, IHzHost, ITrayHost
    {
        public static bool AppExiting;
        public static bool ElevatedRestartRequested;
        public static int RestartTab;

        private readonly AppConfig _cfg;
        private Tray _tray;
        private HeaderBar _header;
        private SegmentedTabs _tabs;
        private Panel _content;
        private Label _statusText;
        private Label _statusRight;
        private Timer _clock;
        private int _clockTick;
        private bool _applying;

        private readonly string[] _titles = { "游戏加速", "定时电源", "定时关闭应用", "设置" };
        private readonly PanelBase[] _panels = new PanelBase[4];
        private readonly Control[] _views = new Control[4];

        public bool StartMinimized;
        public int AutoExitSeconds;

        /// --power-demo=N：预填 N 秒并在「定时电源」页起倒计时（冒烟测试，不会真的执行电源操作）。
        public int DemoPowerSeconds;

        /// --killer-demo=N：预填 N 秒并在「定时关闭应用」页起倒计时（冒烟测试，不会真的结束进程）。
        public int DemoKillerSeconds;

        public AppConfig Cfg { get { return _cfg; } }

        public MainForm(AppConfig cfg, int initialTab, int demoPowerSeconds, int demoKillerSeconds)
        {
            _cfg = cfg;
            // 必须在构造期就拿到：ApplyTab 在构造里跑，晚了就赶不上首屏的演示倒计时
            DemoPowerSeconds = demoPowerSeconds;
            DemoKillerSeconds = demoKillerSeconds;

            Text = AppInfo.DisplayName;
            Icon = Theme.AppIcon(32);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.FontUi;
            MinimumSize = new Size(940, 660);
            ClientSize = ClampToScreen(new Size(_cfg.WinW, _cfg.WinH));
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            KeyPreview = true;

            BuildLayout();
            RestorePlacement();
            _tray = new Tray(this);

            int tab = initialTab >= 0 ? initialTab : _cfg.LastTab;
            ApplyTab(tab, true);
        }

        // ── 布局 ────────────────────────────────────────────────

        private void BuildLayout()
        {
            _header = new HeaderBar
            {
                Dock = DockStyle.Top,
                Title = AppInfo.Name,
                Subtitle = "系统工具箱 · v" + AppInfo.Version + " · " + AppInfo.BuildTag
            };

            _tabs = new SegmentedTabs { Dock = DockStyle.Left, Items = _titles };
            _tabs.Width = _tabs.PreferredWidth;
            _tabs.OnChange = delegate(int index) { ApplyTab(index, false); };

            var tabHost = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Bg, Padding = new Padding(20, 6, 20, 10) };
            tabHost.Controls.Add(_tabs);

            _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

            var status = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Surface };
            _statusRight = new Label
            {
                Dock = DockStyle.Right,
                Width = 300,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.TextFaint,
                Font = Theme.FontSmall,
                BackColor = Theme.Surface,
                Padding = new Padding(0, 0, 14, 0)
            };
            _statusText = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextDim,
                Font = Theme.FontSmall,
                BackColor = Theme.Surface,
                Padding = new Padding(16, 0, 0, 0),
                Text = "就绪"
            };
            status.Controls.Add(_statusText);
            status.Controls.Add(_statusRight);

            // Dock 的层叠顺序 = z-order 反序（后加的先占位）：Fill 先加，Top/Bottom 后加。
            Controls.Add(_content);
            Controls.Add(status);
            Controls.Add(tabHost);
            Controls.Add(_header);

            _clock = new Timer { Interval = 1000 };
            _clock.Tick += delegate { OnClock(); };
            _clock.Start();
        }

        private void OnClock()
        {
            _clockTick++;
            if (_clockTick % 5 == 0)
            {
                _statusRight.Text = DateTime.Now.ToString("HH:mm:ss") + "   内存 " + SysEnv.WorkingSetMb().ToString("0") + " MB";
            }
        }

        private static Size ClampToScreen(Size wanted)
        {
            // 配置里可能存着换显示器前的大尺寸：钳到当前虚拟桌面内，别让窗口开到屏幕外
            Rectangle vs = SystemInformation.VirtualScreen;
            int w = Math.Max(940, Math.Min(wanted.Width, vs.Width - 60));
            int h = Math.Max(660, Math.Min(wanted.Height, vs.Height - 60));
            return new Size(w, h);
        }

        private void RestorePlacement()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            bool valid = _cfg.WinX >= vs.Left - 50 && _cfg.WinY >= vs.Top - 50
                         && _cfg.WinX < vs.Right - 100 && _cfg.WinY < vs.Bottom - 100;
            if (valid) Location = new Point(_cfg.WinX, _cfg.WinY);
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Location = new Point((vs.Width - Width) / 2 + vs.Left, (vs.Height - Height) / 2 + vs.Top);
            }
            if (_cfg.WinMaximized) WindowState = FormWindowState.Maximized;
            UpdateHeaderChips();
        }

        private void UpdateHeaderChips()
        {
            _header.SetChips(
                (SysEnv.IsAdmin() ? "管理员" : "普通用户") + "|" + (SysEnv.IsAdmin() ? "#2EA043" : "#939EAB"),
                AppInfo.Version + "|#3B82F6");
        }

        // ── Tab 与懒加载 ────────────────────────────────────────

        private PanelBase CreatePanel(int index)
        {
            switch (index)
            {
                case 0: return new BoosterPanel(this);
                case 1: return new PowerPanel(this);
                case 2: return new KillerPanel(this);
                default: return new SettingsPanel(this);
            }
        }

        private void ApplyTab(int index, bool initial)
        {
            if (_applying) return;
            if (index < 0 || index > 3) index = 0;
            _applying = true;
            try
            {
                if (_panels[index] == null)
                {
                    PanelBase panel = CreatePanel(index);
                    _panels[index] = panel;
                    _views[index] = panel;
                    _content.Controls.Add(panel);
                    SetStatus("已加载「" + panel.Title + "」");
                    if (DemoPowerSeconds > 0)
                    {
                        var power = panel as PowerPanel;
                        if (power != null) power.DemoStart(DemoPowerSeconds);
                    }
                    if (DemoKillerSeconds > 0)
                    {
                        var killer = panel as KillerPanel;
                        if (killer != null) killer.DemoStart(DemoKillerSeconds);
                    }
                }
                for (int i = 0; i < 4; i++)
                {
                    if (i != index && _panels[i] != null) _panels[i].OnLeave();
                    if (_views[i] != null) _views[i].Visible = (i == index);
                }
                _panels[index].OnEnter();
                _tabs.Selected = index;
                _cfg.LastTab = index;
                if (!initial) SetStatus("「" + _panels[index].Title + "」");
            }
            finally
            {
                _applying = false;
            }
        }

        // ── IHzHost ────────────────────────────────────────────

        public void SetStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), text); return; }
            _statusText.Text = text;
        }

        public void Log(string msg)
        {
            Hz.Log.Info(msg);
        }

        public void ShowMain()
        {
            ShowFromTray();
        }

        public void SelectTab(int index)
        {
            ApplyTab(index, false);
        }

        public void SetTrayStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetTrayStatus), text); return; }
            if (_tray != null) _tray.SetTooltip(text);
        }

        public void SetTrayColor(Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action<Color>(SetTrayColor), color); return; }
            if (_tray != null) _tray.SetState(color);
        }

        public void Notify(string title, string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, string>(Notify), title, text); return; }
            if (_tray != null) _tray.Balloon(title, text);
        }

        // ── ITrayHost ──────────────────────────────────────────

        public bool IsWindowVisibleOnScreen
        {
            get { return Visible && WindowState != FormWindowState.Minimized; }
        }

        public void ToggleFromTray()
        {
            if (IsWindowVisibleOnScreen && ContainsFocus) HideToTray();
            else ShowFromTray();
        }

        public void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        public void HideToTray()
        {
            SavePlacement();
            Hide();
            if (_cfg.TrimOnHide) Native.TrimWorkingSet();
            if (_tray != null) _tray.BalloonOnce(AppInfo.Name, "已最小化到托盘，定时任务会继续运行。右键托盘图标可退出。");
            SetStatus("已最小化到托盘（关窗不退出）");
        }

        public void RequestExit()
        {
            AppExiting = true;
            Close();
        }

        /// 请求以管理员身份重启（提权动作失败或用户点「以管理员重启」时用）。
        public static void RequestElevatedRestart(int tab)
        {
            ElevatedRestartRequested = true;
            RestartTab = tab;
            AppExiting = true;
            Application.Exit();
        }

        // ── 生命周期 ────────────────────────────────────────────

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (AutoExitSeconds > 0)
            {
                var t = new Timer { Interval = Math.Max(1000, AutoExitSeconds * 1000) };
                t.Tick += delegate { t.Stop(); Hz.Log.Info("--exit-after 到点，自动退出（冒烟测试）"); RequestExit(); };
                t.Start();
            }
            if (StartMinimized) HideToTray();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Native.WM_HZ_ACTIVATE)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AppExiting && e.CloseReason == CloseReason.UserClosing && _cfg.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            SavePlacement();
            for (int i = 0; i < 4; i++)
            {
                if (_panels[i] != null) _panels[i].OnAppExit();
            }
            if (_clock != null) _clock.Stop();
            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }
            base.OnFormClosing(e);
        }

        // 系统关机/注销时 FormClosing 的 CloseReason 不是 UserClosing，
        // 下面的判断会放行退出，所以这里不需要额外挂 SystemEvents.SessionEnding。

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.Modifiers) == Keys.Control)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code >= Keys.D1 && code <= Keys.D4)
                {
                    ApplyTab(code - Keys.D1, false);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SavePlacement()
        {
            if (WindowState == FormWindowState.Normal)
            {
                // 存 ClientSize（客户区），恢复时也赋给 ClientSize —— 混用 Width/Height
                // 会把边框与标题栏重复计入，窗口每启动一次就长大一圈（实测踩过）。
                _cfg.WinW = ClientSize.Width;
                _cfg.WinH = ClientSize.Height;
                _cfg.WinX = Location.X;
                _cfg.WinY = Location.Y;
            }
            _cfg.WinMaximized = WindowState == FormWindowState.Maximized;
            _cfg.Save();
        }
    }
}
