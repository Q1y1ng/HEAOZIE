// 主窗口（v2 / Win11 Fluent 外壳）：
//   · 自绘标题栏（图标 + 名称 + 最小化/最大化/关闭），拖动、双击最大化、缩放、Win11 Snap 布局
//     全部交给系统：WM_NCHITTEST 返回 HTCAPTION / HT*BORDER / HTMINBUTTON / HTMAXBUTTON / HTCLOSE
//   · Mica 背景（DWM backdrop + 客户区 alpha 透出）；拿不到就退回 Win11 实色底
//   · 左侧 NavigationView + 右侧内容区 + 底部状态条
//   · 关闭语义不变：关窗进托盘，定时任务继续跑；退出走托盘菜单

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hz.Panels;

namespace Hz
{
    internal sealed class MainForm : Form, IHzHost, ITrayHost
    {
        public static bool AppExiting;
        public static bool ElevatedRestartRequested;
        public static int RestartTab;

        private const int CaptionHeight = 40;
        private const int ButtonWidth = 46;
        private const int ResizeBorder = 6;

        private readonly AppConfig _cfg;
        private Tray _tray;
        private CaptionBar _caption;
        private FluentNavView _nav;
        private Panel _content;
        private FluentStatusBar _status;
        private Timer _clock;
        private int _clockTick;
        private bool _applying;
        private bool _mica;

        private readonly string[] _titles = { "游戏加速", "定时电源", "定时关闭应用", "设置" };
        private readonly string[] _glyphs = { Theme.Ico.Bolt, Theme.Ico.Power, Theme.Ico.Clock, Theme.Ico.Settings };
        private readonly PanelBase[] _panels = new PanelBase[4];
        private readonly Control[] _views = new Control[4];

        public bool StartMinimized;
        public int AutoExitSeconds;
        public int DemoPowerSeconds;
        public int DemoKillerSeconds;
        public bool DemoBooster;

        public AppConfig Cfg { get { return _cfg; } }
        public bool MicaActive { get { return _mica; } }

        public MainForm(AppConfig cfg, int initialTab, int demoPowerSeconds, int demoKillerSeconds, bool demoBooster)
        {
            _cfg = cfg;
            DemoPowerSeconds = demoPowerSeconds;
            DemoKillerSeconds = demoKillerSeconds;
            DemoBooster = demoBooster;

            // 主题覆盖（设置页可选）：默认跟随系统
            if (_cfg.ThemeOverride == "dark") Theme.Apply(FluentTheme.Dark);
            else if (_cfg.ThemeOverride == "light") Theme.Apply(FluentTheme.Light);

            Text = AppInfo.DisplayName;
            Icon = Theme.AppIcon(32);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Base;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Body;
            MinimumSize = new Size(1000, 700);
            ClientSize = ClampToScreen(new Size(Math.Max(1120, _cfg.WinW), Math.Max(720, _cfg.WinH)));
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            // Form 默认也不支持透明底：Mica 模式下要把客户区画成 alpha=0，必须先开这个样式
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            KeyPreview = true;

            BuildLayout();
            RestorePlacement();

            var screen = Screen.FromControl(this);
            MaximizedBounds = screen.WorkingArea;

            _tray = new Tray(this);
            ApplyTab(initialTab >= 0 ? initialTab : _cfg.LastTab, true);
        }

        // ── 布局 ────────────────────────────────────────────────

        private void BuildLayout()
        {
            _caption = new CaptionBar(this) { Dock = DockStyle.Top, Height = CaptionHeight };

            _nav = new FluentNavView
            {
                Dock = DockStyle.Left,
                Width = 212,
                HeaderText = AppInfo.Name,
                HeaderSubText = "系统工具箱",
                FooterText = "v" + AppInfo.Version + " · " + AppInfo.BuildTag
            };
            for (int i = 0; i < _titles.Length; i++) _nav.AddItem(_glyphs[i], _titles[i], null);
            _nav.SelectedChanged += delegate(int index) { ApplyTab(index, false); };

            _content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 0, 12, 0) };

            var status = new FluentStatusBar { Dock = DockStyle.Bottom };
            _status = status;

            // 停靠顺序 = z-order 反序：Fill 先加，Top/Bottom/Left 后加
            Controls.Add(_content);
            Controls.Add(status);
            Controls.Add(_nav);
            Controls.Add(_caption);

            _clock = new Timer { Interval = 1000 };
            _clock.Tick += delegate { OnClock(); };
            _clock.Start();
        }

        private void OnClock()
        {
            _clockTick++;
            if (_clockTick % 5 == 0)
            {
                _status.SetRight(DateTime.Now.ToString("HH:mm:ss") + "    内存 " + SysEnv.WorkingSetMb().ToString("0") + " MB");
            }
        }

        // ── Mica / 主题 ─────────────────────────────────────────

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyBackdrop();
        }

        private void ApplyBackdrop()
        {
            _mica = Native.ApplyMica(Handle, Theme.Mode == FluentTheme.Dark, true);
            // Mica 生效时客户区必须留 alpha（背景不画实色），否则透不出桌面
            BackColor = _mica ? Color.FromArgb(0, 0, 0, 0) : Theme.Base;
            Invalidate(true);
        }

        /// 主题切换（设置页调用）：重建令牌 → 重套 DWM → 全窗重绘。
        public void ApplyTheme(FluentTheme mode)
        {
            Theme.Apply(mode);
            if (IsHandleCreated) Native.RefreshBackdrop(Handle, mode == FluentTheme.Dark);
            BackColor = _mica ? Color.FromArgb(0, 0, 0, 0) : Theme.Base;
            _caption.Invalidate();
            _nav.Invalidate();
            _status.Invalidate();
            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (_mica)
            {
                // alpha=0 → 交给 DWM 画 Mica；GDI+ 才会正确写 alpha（GDI 会写成不透明的黑）
                e.Graphics.Clear(Color.FromArgb(0, 0, 0, 0));
                return;
            }
            e.Graphics.Clear(Theme.Base);
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
                    if (DemoBooster)
                    {
                        var booster = panel as BoosterPanel;
                        if (booster != null) booster.DemoStart();
                    }
                }
                for (int i = 0; i < 4; i++)
                {
                    if (i != index && _panels[i] != null) _panels[i].OnLeave();
                    if (_views[i] != null) _views[i].Visible = (i == index);
                }
                _panels[index].OnEnter();
                _nav.SelectedIndex = index;
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
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetStatus), text); } catch { }
                return;
            }
            _status.SetLeft(text);
        }

        public void Log(string msg) { Hz.Log.Info(msg); }
        public void ShowMain() { ShowFromTray(); }
        public void SelectTab(int index) { ApplyTab(index, false); }

        public void SetTrayStatus(string text)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetTrayStatus), text); } catch { }
                return;
            }
            if (_tray != null) _tray.SetTooltip(text);
        }

        public void SetTrayColor(Color color)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<Color>(SetTrayColor), color); } catch { }
                return;
            }
            if (_tray != null) _tray.SetState(color);
        }

        public void Notify(string title, string text)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string, string>(Notify), title, text); } catch { }
                return;
            }
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

        public static void RequestElevatedRestart(int tab)
        {
            ElevatedRestartRequested = true;
            RestartTab = tab;
            AppExiting = true;
            Application.Exit();
        }

        // ── 自绘标题栏的命中测试（交给系统做拖动/缩放/Snap）──────

        private Rectangle MinButtonRect { get { return new Rectangle(ClientSize.Width - ButtonWidth * 3, 0, ButtonWidth, CaptionHeight); } }
        private Rectangle MaxButtonRect { get { return new Rectangle(ClientSize.Width - ButtonWidth * 2, 0, ButtonWidth, CaptionHeight); } }
        private Rectangle CloseButtonRect { get { return new Rectangle(ClientSize.Width - ButtonWidth, 0, ButtonWidth, CaptionHeight); } }

        internal int CaptionButtonAt(Point p)
        {
            if (CloseButtonRect.Contains(p)) return 3;
            if (MaxButtonRect.Contains(p)) return 2;
            if (MinButtonRect.Contains(p)) return 1;
            return 0;
        }

        internal Rectangle MinRect { get { return MinButtonRect; } }
        internal Rectangle MaxRect { get { return MaxButtonRect; } }
        internal Rectangle CloseRect { get { return CloseButtonRect; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Native.WM_HZ_ACTIVATE)
            {
                ShowFromTray();
                return;
            }

            if (m.Msg == Native.WM_NCHITTEST && WindowState != FormWindowState.Minimized)
            {
                base.WndProc(ref m);
                int lp = m.LParam.ToInt32();
                var screen = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
                Point p = PointToClient(screen);

                if (WindowState == FormWindowState.Normal)
                {
                    bool left = p.X < ResizeBorder, right = p.X >= ClientSize.Width - ResizeBorder;
                    bool top = p.Y < ResizeBorder, bottom = p.Y >= ClientSize.Height - ResizeBorder;
                    if (top && left) { m.Result = (IntPtr)Native.HTTOPLEFT; return; }
                    if (top && right) { m.Result = (IntPtr)Native.HTTOPRIGHT; return; }
                    if (bottom && left) { m.Result = (IntPtr)Native.HTBOTTOMLEFT; return; }
                    if (bottom && right) { m.Result = (IntPtr)Native.HTBOTTOMRIGHT; return; }
                    if (left) { m.Result = (IntPtr)Native.HTLEFT; return; }
                    if (right) { m.Result = (IntPtr)Native.HTRIGHT; return; }
                    if (top) { m.Result = (IntPtr)Native.HTTOP; return; }
                    if (bottom) { m.Result = (IntPtr)Native.HTBOTTOM; return; }
                }

                int button = CaptionButtonAt(p);
                _caption.SetHover(button);
                if (button == 1) { m.Result = (IntPtr)Native.HTMINBUTTON; return; }
                if (button == 2) { m.Result = (IntPtr)Native.HTMAXBUTTON; return; }
                if (button == 3) { m.Result = (IntPtr)Native.HTCLOSE; return; }
                if (p.Y < CaptionHeight) { m.Result = (IntPtr)Native.HTCAPTION; return; }
            }

            base.WndProc(ref m);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_caption != null) _caption.Invalidate();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            try
            {
                Screen s = Screen.FromControl(this);
                if (MaximizedBounds != s.WorkingArea) MaximizedBounds = s.WorkingArea;
            }
            catch { }
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

        private static Size ClampToScreen(Size wanted)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            int w = Math.Max(1000, Math.Min(wanted.Width, vs.Width - 60));
            int h = Math.Max(700, Math.Min(wanted.Height, vs.Height - 60));
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
        }

        private void SavePlacement()
        {
            if (WindowState == FormWindowState.Normal)
            {
                _cfg.WinW = ClientSize.Width;
                _cfg.WinH = ClientSize.Height;
                _cfg.WinX = Location.X;
                _cfg.WinY = Location.Y;
            }
            _cfg.WinMaximized = WindowState == FormWindowState.Maximized;
            _cfg.Save();
        }
    }

    /// 自绘标题栏：应用图标 + 名称 + 三个窗口按钮（悬停/按下态由 WM_NCHITTEST 驱动）。
    internal sealed class CaptionBar : Control
    {
        private readonly MainForm _form;
        private int _hover;

        public CaptionBar(MainForm form)
        {
            _form = form;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void SetHover(int button)
        {
            if (_hover == button) return;
            _hover = button;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 窗口按钮（Win11 尺寸 46x32，图标 10px）
            DrawButton(g, _form.MinRect, Theme.Ico.Minimize, 1);
            DrawButton(g, _form.MaxRect,
                _form.WindowState == FormWindowState.Maximized ? Theme.Ico.Restore : Theme.Ico.Maximize, 2);
            DrawButton(g, _form.CloseRect, Theme.Ico.CaptionClose, 3);

            // 应用图标 + 名称
            using (Icon icon = Theme.AppIcon(16))
            {
                g.DrawIcon(icon, new Rectangle(14, 12, 16, 16));
            }
            Fluent.Text(g, AppInfo.Name, Theme.Caption, new RectangleF(38f, 0f, 260f, Height), Theme.TextSecondary, ContentAlignment.MiddleLeft);
        }

        private void DrawButton(Graphics g, Rectangle rect, string glyph, int index)
        {
            var box = new RectangleF(rect.X + 0.5f, rect.Y + 4.5f, rect.Width - 1f, rect.Height - 9f);
            if (_hover == index)
            {
                Color fill = index == 3 ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Theme.SubtleFillHover;
                Fluent.Fill(g, box, Fluent.ControlRadius, fill);
            }
            Color fg = _hover == index && index == 3 ? Color.White : Theme.TextSecondary;
            Fluent.Icon(g, glyph, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), fg, Theme.IconSmallFont);
        }
    }
}
