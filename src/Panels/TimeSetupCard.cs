// 「设定倒计时」卡片（Fluent 版）：Win11 数字步进器 + 预设按钮 + 目标时刻提示。
//
// 定时电源页与定时关闭应用页共用；compact=true 用于窄列（省掉"时/分/秒"单位字，改用冒号）。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class TimeSetupCard : FluentCard
    {
        private readonly int[] _spin = new int[3];
        private readonly Label[] _spinText = new Label[3];
        private readonly List<Control> _editable = new List<Control>();
        private readonly Label _hint;
        private string _override;
        private bool _locked;

        public event Action Changed;

        public TimeSetupCard(string title, string subtitle, List<int> presets, int presetWidth)
            : this(title, subtitle, presets, presetWidth, false)
        {
        }

        public TimeSetupCard(string title, string subtitle, List<int> presets, int presetWidth, bool compact)
            : base(title, subtitle)
        {
            string[] units = { "时", "分", "秒" };
            int x = 16;
            for (int i = 0; i < 3; i++)
            {
                if (compact && i > 0)
                {
                    var colon = new Label
                    {
                        Text = ":",
                        Font = Theme.Body,
                        ForeColor = Theme.TextTertiary,
                        BackColor = Color.Transparent,
                        AutoSize = false,
                        Size = new Size(14, 36),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Location = new Point(x, 58)
                    };
                    Controls.Add(colon);
                    x += 16;
                }
                x += AddSpin(i, x, i == 0 ? 23 : 59, compact ? null : units[i]);
            }

            var presetRow = new FlowLayoutPanel
            {
                Location = new Point(16, 104),
                Size = new Size(760, 34),
                BackColor = Color.Transparent,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            if (presets != null)
            {
                foreach (int secs in presets)
                {
                    int captured = secs;
                    var b = new FluentButton(PresetLabel(captured), presetWidth, 32) { Style = FluentButtonStyle.Standard };
                    b.Click += delegate { SetSeconds(captured); };
                    _editable.Add(b);
                    presetRow.Controls.Add(b);
                }
            }
            Controls.Add(presetRow);

            _hint = new Label
            {
                Text = "目标时刻：--:--:--",
                Font = Theme.Caption,
                ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(16, 148)
            };
            Controls.Add(_hint);
        }

        public TimeSpan Total { get { return new TimeSpan(_spin[0], _spin[1], _spin[2]); } }

        public void Lock(bool locked)
        {
            _locked = locked;
            foreach (Control c in _editable)
            {
                c.Enabled = !locked;
            }
        }

        public void SetSeconds(int seconds)
        {
            if (_locked) return;
            if (seconds < 0) seconds = 0;
            _spin[0] = seconds / 3600;
            _spin[1] = (seconds % 3600) / 60;
            _spin[2] = seconds % 60;
            RefreshDisplay();
        }

        public void SetHintOverride(string text)
        {
            _override = text;
            UpdateHint();
        }

        public void RefreshDisplay()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_spinText[i] != null) _spinText[i].Text = _spin[i].ToString("00");
            }
            UpdateHint();
            if (Changed != null) Changed();
        }

        private void UpdateHint()
        {
            if (!string.IsNullOrEmpty(_override))
            {
                _hint.Text = _override;
                return;
            }
            TimeSpan total = Total;
            if (total <= TimeSpan.Zero)
            {
                _hint.Text = "目标时刻：--:--:--";
                return;
            }
            _hint.Text = "目标时刻：" + DateTime.Now.Add(total).ToString("yyyy-MM-dd HH:mm:ss") +
                         "（当前 " + DateTime.Now.ToString("HH:mm:ss") + "）";
        }

        private void Bump(int index, int delta, int max)
        {
            if (_locked) return;
            _spin[index] = (_spin[index] + delta + max + 1) % (max + 1);
            RefreshDisplay();
        }

        /// 返回这一组占用的宽度。
        private int AddSpin(int index, int x, int max, string unit)
        {
            var minus = new FluentButton("", 34, 36) { Glyph = "\uE738" };   // Remove
            minus.Location = new Point(x, 58);
            minus.Click += delegate { Bump(index, -1, max); };
            _editable.Add(minus);
            Controls.Add(minus);

            var value = new Label
            {
                Text = "00",
                Font = Theme.Subtitle,
                ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(56, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x + 38, 58)
            };
            _spinText[index] = value;
            Controls.Add(value);

            var plus = new FluentButton("", 34, 36) { Glyph = "\uE710" };    // Add
            plus.Location = new Point(x + 98, 58);
            plus.Click += delegate { Bump(index, 1, max); };
            _editable.Add(plus);
            Controls.Add(plus);

            int used = 138;
            if (unit != null)
            {
                var unitLabel = new Label
                {
                    Text = unit,
                    Font = Theme.Caption,
                    ForeColor = Theme.TextTertiary,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(26, 36),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(x + 136, 58)
                };
                Controls.Add(unitLabel);
                used = 172;
            }
            return used;
        }

        public static string PresetLabel(int seconds)
        {
            if (seconds % 3600 == 0) return (seconds / 3600) + " 小时";
            if (seconds % 60 == 0) return (seconds / 60) + " 分钟";
            return seconds + " 秒";
        }
    }
}
