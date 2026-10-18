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

        private void StretchCards()
        {
            int w = Body.ClientSize.Width - Body.Padding.Horizontal;
            if (w < 240) return;
            foreach (Control c in Body.Controls)
            {
                if (c.Width != w) c.Width = w;
            }
        }

        /// 加一张卡片；body 会自动换行并随窗口宽度伸缩。
        protected SoftCard AddCard(string title, string subtitle, string body, int height)
        {
            var card = new SoftCard(title, subtitle) { Height = height, Margin = new Padding(0, 0, 0, 14) };
            card.Width = Math.Max(240, Body.ClientSize.Width - Body.Padding.Horizontal);
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
            Body.Controls.Add(card);
            return card;
        }

        /// 卡片底部的按钮行（右对齐用 FlowDirection.RightToLeft）。
        protected FlowLayoutPanel AddButtonRow(SoftCard card, bool rightAligned)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                BackColor = Theme.Card,
                FlowDirection = rightAligned ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };
            card.Controls.Add(row);
            return row;
        }

        protected SoftButton Btn(string text, int width, bool primary, EventHandler onClick)
        {
            var b = new SoftButton(text, width, 30) { Primary = primary };
            b.Click += onClick;
            return b;
        }
    }
}
