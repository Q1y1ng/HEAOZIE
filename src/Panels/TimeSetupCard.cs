// 可复用的「设定倒计时」卡片：时/分/秒微调 + 预设 + 目标时刻提示。
//
// 「定时电源」和「定时关闭应用」两页都需要这一块，抽出来共用 —— 否则两份实现
// 迟早会漂移（原 Python 版就是两个文件里各写了一遍，改一处忘一处）。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class TimeSetupCard : SoftCard
    {
        private readonly int[] _spin = new int[3];
        private readonly Label[] _spinText = new Label[3];
        private readonly List<Control> _editable = new List<Control>();
        private readonly Label _hint;
        private string _override;
        private bool _locked;

        /// 用户改了时间（面板据此刷新自己的状态文案）。
        public event Action Changed;

        public TimeSetupCard(string title, string subtitle, List<int> presets, int presetWidth)
            : this(title, subtitle, presets, presetWidth, false)
        {
        }

        /// compact=true 用于窄列：省掉「时/分/秒」单位字，改用冒号分隔。
        public TimeSetupCard(string title, string subtitle, List<int> presets, int presetWidth, bool compact)
            : base(title, subtitle)
        {
            var row = new FlowLayoutPanel
            {
                Location = new Point(16, 64),
                Size = new Size(700, 36),
                BackColor = Theme.Card,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            string[] units = { "时", "分", "秒" };
            for (int i = 0; i < 3; i++)
            {
                if (compact && i > 0) row.Controls.Add(Colon());
                AddSpin(row, i, i == 0 ? 23 : 59, compact ? null : units[i]);
            }
            Controls.Add(row);

            var presetRow = new FlowLayoutPanel
            {
                Location = new Point(16, 110),
                Size = new Size(760, 34),
                BackColor = Theme.Card,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            if (presets != null)
            {
                foreach (int secs in presets)
                {
                    int captured = secs;
                    var b = new SoftButton(PresetLabel(captured), presetWidth, 30) { Tone = Theme.TextDim };
                    b.Click += delegate { SetSeconds(captured); };
                    _editable.Add(b);
                    presetRow.Controls.Add(b);
                }
            }
            Controls.Add(presetRow);

            _hint = Soft.Label("目标时刻：--:--:--", Theme.TextFaint, Theme.FontSmall, true);
            _hint.Location = new Point(16, 156);
            Controls.Add(_hint);
        }

        public TimeSpan Total
        {
            get { return new TimeSpan(_spin[0], _spin[1], _spin[2]); }
        }

        /// 倒计时运行中锁住输入（并给出视觉反馈：按钮变灰）。
        public void Lock(bool locked)
        {
            _locked = locked;
            foreach (Control c in _editable)
            {
                var b = c as SoftButton;
                if (b != null) b.Enabled = !locked;
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

        /// 非空时覆盖默认的「目标时刻」提示（倒计时运行中显示真实目标时刻）。
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

        private static Label Colon()
        {
            return new Label
            {
                Width = 14,
                Height = 30,
                Text = ":",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.FontMonoBig,
                ForeColor = Theme.TextFaint,
                BackColor = Theme.Card,
                Margin = new Padding(0, 0, 0, 0)
            };
        }

        private void AddSpin(FlowLayoutPanel row, int index, int max, string unit)
        {
            var minus = new SoftButton("", 30, 30) { Icon = GI.Minus, Tone = Theme.TextDim };
            minus.Click += delegate { Bump(index, -1, max); };

            var value = new Label
            {
                Width = 52,
                Height = 30,
                Text = "00",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.FontMonoBig,
                ForeColor = Theme.Text,
                BackColor = Theme.Card
            };
            _spinText[index] = value;

            var plus = new SoftButton("", 30, 30) { Icon = GI.Plus, Tone = Theme.TextDim };
            plus.Click += delegate { Bump(index, 1, max); };

            var unitLabel = new Label
            {
                Width = 26,
                Height = 30,
                Text = unit,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.FontSmall,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Card,
                Margin = new Padding(4, 0, 8, 0)
            };

            _editable.Add(minus);
            _editable.Add(plus);
            row.Controls.Add(minus);
            row.Controls.Add(value);
            row.Controls.Add(plus);
            if (unit != null) row.Controls.Add(unitLabel);
        }

        public static string PresetLabel(int seconds)
        {
            if (seconds % 3600 == 0) return (seconds / 3600) + " 小时";
            if (seconds % 60 == 0) return (seconds / 60) + " 分钟";
            return seconds + " 秒";
        }
    }
}
