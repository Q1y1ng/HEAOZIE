// 托盘：NotifyIcon + 自绘轻量菜单（Fluent 观感）。
//
// 菜单是独立的小窗口（不是 Mica），所以这里用普通不透明底；但绘制与文字仍走 GDI+，
// 与主界面同一套令牌，观感一致。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hz
{
    internal interface ITrayHost
    {
        void ToggleFromTray();
        void ShowFromTray();
        void RequestExit();
        void SelectTab(int index);
        bool IsWindowVisibleOnScreen { get; }
    }

    internal sealed class Tray : IDisposable
    {
        private readonly ITrayHost _host;
        private readonly NotifyIcon _icon;
        private TrayMenu _menu;
        private bool _balloonShown;

        public Tray(ITrayHost host)
        {
            _host = host;
            _icon = new NotifyIcon
            {
                Icon = Theme.AppIcon(16),
                Text = AppInfo.DisplayName,
                Visible = true
            };
            _icon.MouseClick += OnMouseClick;
            _icon.DoubleClick += delegate { _host.ShowFromTray(); };
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _host.ToggleFromTray();
                return;
            }
            if (e.Button == MouseButtons.Right) ShowMenu(Cursor.Position);
        }

        private void ShowMenu(Point at)
        {
            if (_menu == null) BuildMenu();
            _menu.PopupAt(at);
        }

        private void BuildMenu()
        {
            _menu = new TrayMenu();
            _menu.Add("显示主界面", delegate { _host.ShowFromTray(); });
            _menu.AddSeparator();
            _menu.Add("游戏加速", delegate { _host.ShowFromTray(); _host.SelectTab(0); });
            _menu.Add("定时电源", delegate { _host.ShowFromTray(); _host.SelectTab(1); });
            _menu.Add("定时关闭应用", delegate { _host.ShowFromTray(); _host.SelectTab(2); });
            _menu.Add("设置", delegate { _host.ShowFromTray(); _host.SelectTab(3); });
            _menu.AddSeparator();
            _menu.Add("退出 " + AppInfo.Name, delegate { _host.RequestExit(); }, true);
        }

        public void SetTooltip(string text)
        {
            if (string.IsNullOrEmpty(text)) text = AppInfo.DisplayName;
            if (text.Length > 62) text = text.Substring(0, 62);
            _icon.Text = text;
        }

        public void SetState(Color color)
        {
            try
            {
                Icon old = _icon.Icon;
                _icon.Icon = Theme.AppIconWithDot(16, color);
                if (old != null) old.Dispose();
            }
            catch { }
        }

        public void BalloonOnce(string title, string text)
        {
            if (_balloonShown) return;
            _balloonShown = true;
            Balloon(title, text);
        }

        public void Balloon(string title, string text)
        {
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.ShowBalloonTip(4000);
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch { }
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }
        }
    }

    /// 自绘托盘菜单：无边框、悬停高亮、失焦即关。
    internal sealed class TrayMenu : Form
    {
        private sealed class Item
        {
            public string Text;
            public Action Run;
            public bool Separator;
            public bool Danger;
        }

        private const int ItemHeight = 34;
        private const int SeparatorHeight = 9;
        private const int PadY = 6;

        private readonly List<Item> _items = new List<Item>();
        private int _hover = -1;
        private DateTime _shownAt = DateTime.MinValue;

        public TrayMenu()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Layer;
            Font = Theme.Body;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Add(string text, Action run) { Add(text, run, false); }

        public void Add(string text, Action run, bool danger)
        {
            _items.Add(new Item { Text = text, Run = run, Danger = danger });
        }

        public void AddSeparator()
        {
            if (_items.Count == 0) return;
            _items.Add(new Item { Separator = true });
        }

        public void PopupAt(Point screenPoint)
        {
            int width = 210;
            using (Graphics g = CreateGraphics())
            {
                foreach (Item it in _items)
                {
                    if (it.Separator) continue;
                    int w = (int)Fluent.Measure(g, it.Text, Theme.Body).Width + 56;
                    if (w > width) width = w;
                }
            }
            int height = PadY * 2;
            foreach (Item it in _items) height += it.Separator ? SeparatorHeight : ItemHeight;

            Size = new Size(width, height);
            Rectangle wa = Screen.FromPoint(screenPoint).WorkingArea;
            int x = screenPoint.X - width + 12;
            int y = screenPoint.Y - height - 8;
            if (x < wa.Left) x = wa.Left + 4;
            if (y < wa.Top) y = wa.Top + 4;
            if (x + width > wa.Right) x = wa.Right - width - 4;
            if (y + height > wa.Bottom) y = wa.Bottom - height - 4;
            Location = new Point(x, y);

            _hover = -1;
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            _shownAt = DateTime.UtcNow;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250) return;
            Hide();
        }

        private int IndexAt(Point p)
        {
            int y = PadY;
            for (int i = 0; i < _items.Count; i++)
            {
                int h = _items[i].Separator ? SeparatorHeight : ItemHeight;
                if (!_items[i].Separator && p.Y >= y && p.Y < y + h) return i;
                y += h;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexAt(e.Location);
            if (idx != _hover) { _hover = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int idx = IndexAt(e.Location);
            if (idx >= 0 && e.Button == MouseButtons.Left)
            {
                Item it = _items[idx];
                Hide();
                if (it.Run != null) it.Run();
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Fluent.Fill(g, box, Fluent.CardRadius, Theme.Layer);
            Fluent.Stroke(g, box, Fluent.CardRadius, Fluent.CardStroke);

            int y = PadY;
            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                if (it.Separator)
                {
                    using (var pen = new Pen(Theme.Divider))
                        g.DrawLine(pen, 12, y + SeparatorHeight / 2, Width - 12, y + SeparatorHeight / 2);
                    y += SeparatorHeight;
                    continue;
                }
                var row = new RectangleF(5f, y, Width - 10f, ItemHeight);
                if (i == _hover)
                {
                    Fluent.Fill(g, row, Fluent.ControlRadius, it.Danger ? Theme.Alpha(Theme.Danger, 40) : Theme.SubtleFillHover);
                }
                Color fg = it.Danger ? Theme.Danger : (i == _hover ? Theme.TextPrimary : Theme.TextSecondary);
                Fluent.Text(g, it.Text, Theme.Body, new RectangleF(16f, y, Width - 24f, ItemHeight), fg, ContentAlignment.MiddleLeft);
                y += ItemHeight;
            }
        }
    }
}
