// 面板基类（v2）：统一"卡片纵向流 + 自绘细滚动条"与"左右分栏"两种版式。
//
// 版式说明：
//   · 流式（默认）：卡片自上而下堆叠，超出视口时用自绘滚动条滚动（设置页 / 定时电源页）
//   · 分栏：左列固定宽度、右列吃满剩余高度（游戏加速页 / 定时关闭应用页），一页装下不滚动
// 为什么不用 Dock=Top 堆卡片：Dock 的层叠顺序取决于 z-order，是 WinForms 里最容易悄悄排错版的地方。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hz.Panels
{
    internal interface IHzHost
    {
        AppConfig Cfg { get; }
        void SetStatus(string text);
        void Log(string msg);
        void ShowMain();
        void SelectTab(int index);

        /// 托盘提示文字（定时类页面用它显示剩余时间）。
        void SetTrayStatus(string text);

        /// 托盘图标状态色（正常/计时中/即将执行）。
        void SetTrayColor(Color color);

        /// 气泡通知（到点前提醒、动作完成）。
        void Notify(string title, string text);
    }

    internal abstract class PanelBase : UserControl
    {
        protected readonly IHzHost Host;

        private readonly FluentScrollPanel _scroll;
        private readonly FlowLayoutPanel _flow;
        private readonly List<Control> _cards = new List<Control>();
        private readonly Dictionary<Control, int> _heights = new Dictionary<Control, int>();
        private Control _stretch;
        private int _stretchMin = 200;

        public abstract string Title { get; }

        protected PanelBase(IHzHost host)
        {
            Host = host;
            Dock = DockStyle.Fill;
            // UserControl 默认不支持透明底：必须先 SetStyle，再赋 BackColor（顺序反了会抛异常）
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;

            _scroll = new FluentScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };
            _flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Location = new Point(0, 0),
                Margin = new Padding(0)
            };
            _flow.SizeChanged += delegate { SyncContentHeight(); };
            _scroll.Content.Controls.Add(_flow);
            _scroll.Resize += delegate { Relayout(); };
            Controls.Add(_scroll);
        }

        private void SyncContentHeight()
        {
            _scroll.Content.Height = Math.Max(_flow.Height, _scroll.ClientSize.Height - _scroll.Padding.Vertical);
        }

        /// 加一张卡片（流式版式）。
        protected T AddCard<T>(T card, int height) where T : Control
        {
            card.Height = height;
            card.Margin = new Padding(0, 0, 0, 16);
            card.Width = Math.Max(320, _flow.ClientSize.Width);
            _heights[card] = height;
            _cards.Add(card);
            _flow.Controls.Add(card);
            Relayout();
            return card;
        }

        /// 指定某张卡片吃掉剩余高度（进程表这类"越大越好"的卡片）。
        protected void SetStretch(Control card, int minHeight)
        {
            _stretch = card;
            _stretchMin = minHeight;
            Relayout();
        }

        private void Relayout()
        {
            int width = Math.Max(320, _scroll.ClientSize.Width - _scroll.Padding.Horizontal);
            foreach (Control c in _cards)
            {
                if (c.Width != width) c.Width = width;
            }

            if (_stretch != null && _cards.Contains(_stretch))
            {
                int others = 0;
                foreach (Control c in _cards)
                {
                    if (c == _stretch) continue;
                    others += _heights.ContainsKey(c) ? _heights[c] : c.Height;
                    others += c.Margin.Vertical;
                }
                int available = _scroll.ClientSize.Height - _scroll.Padding.Vertical - others - _stretch.Margin.Vertical;
                int height = Math.Max(_stretchMin, available);
                if (_stretch.Height != height) _stretch.Height = height;
            }
            SyncContentHeight();
        }

        /// 左右分栏版式：左列固定宽度，右列吃满剩余（返回的容器请自行填两列）。
        protected TableLayoutPanel BuildSplitLayout(float leftWidth)
        {
            _scroll.Visible = false;
            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(24, 20, 24, 20)
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, leftWidth));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(split);
            split.BringToFront();
            return split;
        }

        /// 分栏里的一列：纵向堆叠卡片，宽度随列宽伸缩。
        protected FlowLayoutPanel BuildColumn(params Control[] cards)
        {
            var col = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            foreach (Control card in cards)
            {
                card.Margin = new Padding(0, 0, 0, 16);
                card.Width = Math.Max(280, col.ClientSize.Width);
                col.Controls.Add(card);
            }
            col.ClientSizeChanged += delegate
            {
                int w = col.ClientSize.Width - col.Padding.Horizontal;
                if (w < 280) return;
                foreach (Control c in col.Controls)
                {
                    if (c.Width != w) c.Width = w;
                }
            };
            return col;
        }

        /// 卡片底部的按钮行（wrap=true 允许折行）。
        protected FlowLayoutPanel AddButtonRow(FluentCard card, int height, bool wrap)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = height,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = wrap,
                Padding = new Padding(0, 6, 0, 0)
            };
            card.Controls.Add(row);
            return row;
        }

        protected FluentButton Btn(string text, int width, FluentButtonStyle style, EventHandler onClick)
        {
            var b = new FluentButton(text, width, Fluent.ControlHeight) { Style = style };
            b.Click += onClick;
            return b;
        }

        /// 布局诊断（排查"整页空白"这类问题用）。
        internal string DebugLayout()
        {
            return "scroll=" + _scroll.Width + "x" + _scroll.Height +
                   " pad=" + _scroll.Padding.Horizontal + "," + _scroll.Padding.Vertical +
                   " content=" + _scroll.Content.Width + "x" + _scroll.Content.Height + "@" + _scroll.Content.Left + "," + _scroll.Content.Top +
                   " flow=" + _flow.Width + "x" + _flow.Height + " visible=" + _flow.Visible +
                   " cards=" + _cards.Count;
        }

        public virtual void OnEnter() { }
        public virtual void OnLeave() { }
        public virtual void OnAppExit() { }
    }
}
