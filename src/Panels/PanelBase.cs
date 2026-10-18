// 面板基类：统一底色/滚动/卡片堆叠，子类只管往里加卡片。
//
// 用 FlowLayoutPanel 而不是「多个 Dock=Top」：Dock 的层叠顺序取决于 z-order
// （谁先加谁在上），是 WinForms 里最容易悄悄排错版的地方；TopDown 流式布局
// 就是「按添加顺序从上往下」，没有歧义。

using System;
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
        protected readonly FlowLayoutPanel Body;

        public abstract string Title { get; }

        /// 首次进入该页时调用（建控件、启动刷新）。
        public virtual void OnEnter() { }

        /// 离开该页时调用（停掉刷新，别让隐藏的页面继续吃 CPU）。
        public virtual void OnLeave() { }

        /// 应用退出前的收尾（停线程、存配置）。
        public virtual void OnAppExit() { }

        protected PanelBase(IHzHost host)
        {
            Host = host;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Font = Theme.FontUi;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            AutoScroll = false;

            Body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Theme.Bg,
                Padding = new Padding(20, 16, 20, 16)
            };
            Body.ClientSizeChanged += delegate { StretchCards(); };
            Controls.Add(Body);
        }

        /// 换成左右分栏布局（左列固定宽度，右列吃满剩余）—— 需要「一页装下不滚动」时用。
        protected TableLayoutPanel BuildSplitLayout(float leftWidth)
        {
            Body.Visible = false;   // 隐藏的控件不参与停靠布局，整块客户区让给分栏
            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Bg,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(20, 16, 20, 16)
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, leftWidth));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(split);
            split.BringToFront();
            return split;
        }

        /// 分栏左列：纵向堆叠若干卡片，宽度随列宽伸缩。
        protected FlowLayoutPanel BuildColumn(params Control[] cards)
        {
            var col = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Bg,
                Margin = new Padding(0)
            };
            foreach (Control card in cards)
            {
                card.Margin = new Padding(0, 0, 0, 14);
                card.Width = Math.Max(240, col.ClientSize.Width);
                col.Controls.Add(card);
            }
            col.ClientSizeChanged += delegate
            {
                int w = col.ClientSize.Width - col.Padding.Horizontal;
                if (w < 240) return;
                foreach (Control c in col.Controls)
                {
                    if (c.Width != w) c.Width = w;
                }
            };
            return col;
        }

        private SoftCard _stretchCard;

        /// 指定一张卡片吃掉剩余高度（进程表这种「越大越好」的卡片用），避免整页出现滚动条。
        protected void MarkStretchCard(SoftCard card)
        {
            _stretchCard = card;
            StretchCards();
        }

        private void StretchCards()
        {
            int w = Body.ClientSize.Width - Body.Padding.Horizontal;
            if (w >= 240)
            {
                foreach (Control c in Body.Controls)
                {
                    if (c.Width != w) c.Width = w;
                }
            }
            if (_stretchCard == null || !Body.Controls.Contains(_stretchCard)) return;

            int others = 0;
            foreach (Control c in Body.Controls)
            {
                if (c == _stretchCard) continue;
                others += c.Height + c.Margin.Vertical;
            }
            int available = Body.ClientSize.Height - Body.Padding.Vertical - others - _stretchCard.Margin.Vertical;
            int height = Math.Max(180, available);
            if (_stretchCard.Height != height) _stretchCard.Height = height;
        }

        /// 加一张卡片；body 会自动换行并随窗口宽度伸缩。
        protected SoftCard AddCard(string title, string subtitle, string body, int height)
        {
            var card = new SoftCard(title, subtitle);
            if (!string.IsNullOrEmpty(body))
            {
                card.Controls.Add(new Label
                {
                    Text = body,
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    BackColor = Theme.Card,
                    ForeColor = Theme.TextDim,
                    Font = Theme.FontUi
                });
            }
            return AddCardControl(card, height);
        }

        /// 加一张自定义卡片（TimeSetupCard / ProcTable 这类复合控件）。
        protected T AddCardControl<T>(T card, int height) where T : SoftCard
        {
            card.Height = height;
            card.Margin = new Padding(0, 0, 0, 14);
            card.Width = Math.Max(240, Body.ClientSize.Width - Body.Padding.Horizontal);
            Body.Controls.Add(card);
            return card;
        }

        /// 卡片底部的按钮行（rightAligned 用 RightToLeft 排布；wrap 允许折成两行）。
        protected FlowLayoutPanel AddButtonRow(SoftCard card, bool rightAligned, int height, bool wrap)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = height,
                BackColor = Theme.Card,
                FlowDirection = rightAligned ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                WrapContents = wrap,
                Padding = new Padding(0, 6, 0, 0)
            };
            card.Controls.Add(row);
            return row;
        }

        protected FlowLayoutPanel AddButtonRow(SoftCard card, bool rightAligned)
        {
            return AddButtonRow(card, rightAligned, 42, false);
        }

        protected SoftButton Btn(string text, int width, bool primary, EventHandler onClick)
        {
            var b = new SoftButton(text, width, 30) { Primary = primary };
            b.Click += onClick;
            return b;
        }
    }
}
