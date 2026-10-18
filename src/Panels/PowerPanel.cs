// 「定时电源」页：设定倒计时 → 到点执行 睡眠 / 关机 / 重启，执行前留撤销窗口。
//
// 对照原 Python 版 sleep_timer.py（443 行 tkinter）：
//   · 保留：大号倒计时、时/分/秒微调、预设、睡眠/关机/重启三选一、开始/暂停/重置、到点可取消
//   · 改进：倒计时改为绝对目标时刻（睡眠唤醒不漂移）；托盘常驻并显示剩余时间；
//           执行前 N 秒有独立撤销窗口与气泡提醒；每次状态变化都写日志。
//
// 时间输入那一块与「定时关闭应用」共用 TimeSetupCard，避免两份实现漂移。

using System;
using System.Drawing;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class PowerPanel : PanelBase
    {
        private readonly Countdown _count = new Countdown();
        private readonly Countdown _grace = new Countdown();
        private readonly Timer _tick = new Timer { Interval = 250 };

        private TimeSetupCard _setup;
        private SegmentedTabs _actionTabs;
        private Label _digit;
        private Label _status;
        private Label _actionHint;
        private SoftButton _btnStart;
        private SoftButton _btnPause;
        private SoftButton _btnReset;
        private SoftButton _btnCancel;

        private PowerAction _action;
        private bool _inGrace;

        /// 演示模式（--power-demo=N 冒烟测试用）：走完整流程但**绝不真的执行电源操作**。
        internal bool DemoMode;

        public override string Title { get { return "定时电源"; } }

        public PowerPanel(IHzHost host) : base(host)
        {
            _action = PowerOps.Parse(host.Cfg.PowerAction, PowerAction.Sleep);

            _setup = AddCardControl(new TimeSetupCard("设定倒计时", "到达目标时刻后执行下面的操作", host.Cfg.PowerPresets, 86), 190);
            _setup.SetSeconds(1800);
            _setup.Changed += delegate { UpdateHint(); };

            BuildActionCard();
            BuildCountdownCard();

            _tick.Tick += OnTick;
            UpdateActionHint();
            ResetUi("等待启动");
        }

        /// 冒烟测试入口：预填 seconds 并立刻开始倒计时。
        internal void DemoStart(int seconds)
        {
            DemoMode = true;
            _setup.SetSeconds(seconds);
            StartCountdown();
            Host.Log("演示模式：倒计时 " + seconds + " 秒，到点后只记录不执行");
        }

        // ── 界面 ────────────────────────────────────────────────

        private void BuildActionCard()
        {
            SoftCard card = AddCardControl(new SoftCard("到达时间后执行", null), 128);

            _actionTabs = new SegmentedTabs
            {
                Location = new Point(16, 56),
                Items = new[] { "睡眠", "关机", "重启" },
                Selected = (int)_action
            };
            _actionTabs.Width = _actionTabs.PreferredWidth;
            _actionTabs.OnChange = delegate(int index) { OnActionChanged(index); };
            card.Controls.Add(_actionTabs);

            _actionHint = Soft.Label("", Theme.TextFaint, Theme.FontSmall, true);
            _actionHint.Location = new Point(16, 102);
            card.Controls.Add(_actionHint);
        }

        private void BuildCountdownCard()
        {
            SoftCard card = AddCardControl(new SoftCard("倒计时", null), 240);

            _digit = new Label
            {
                Location = new Point(16, 52),
                Size = new Size(460, 80),
                Text = "00:00:00",
                Font = Theme.FontDigit,
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_digit);

            _status = Soft.Label("等待启动", Theme.TextDim, Theme.FontUi, true);
            _status.Location = new Point(18, 144);
            card.Controls.Add(_status);

            var row = new FlowLayoutPanel
            {
                Location = new Point(16, 180),
                Size = new Size(760, 40),
                BackColor = Theme.Card,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            _btnStart = new SoftButton("开始", 104, 34) { Primary = true, Icon = GI.Play };
            _btnStart.Click += delegate { StartCountdown(); };

            _btnPause = new SoftButton("暂停", 104, 34) { Icon = GI.Pause };
            _btnPause.Click += delegate { TogglePause(); };

            _btnReset = new SoftButton("重置", 94, 34) { Icon = GI.Stop };
            _btnReset.Click += delegate { ResetUi("已重置"); Host.SetStatus("倒计时已重置"); };

            _btnCancel = new SoftButton("立即取消", 126, 34) { Danger = true, Icon = GI.Close, Visible = false };
            _btnCancel.Click += delegate { CancelGrace(); };

            row.Controls.Add(_btnStart);
            row.Controls.Add(_btnPause);
            row.Controls.Add(_btnReset);
            row.Controls.Add(_btnCancel);
            card.Controls.Add(row);
        }

        private void UpdateHint()
        {
            if (_setup == null) return;
            if (_count.IsArmed)
            {
                _setup.SetHintOverride("目标时刻：" + _count.Deadline.ToString("yyyy-MM-dd HH:mm:ss") +
                                       (_count.IsPaused ? "（已暂停，剩余 " + Countdown.Format(_count.Remaining) + "）" : ""));
                return;
            }
            _setup.SetHintOverride(null);
        }

        private void UpdateActionHint()
        {
            if (_actionHint == null) return;
            _actionHint.Text = "到点将" + PowerOps.Verb(_action) + "；执行前有 " + Host.Cfg.CancelWindowSec +
                               " 秒撤销窗口。若期间电脑睡眠过、唤醒后目标时刻已过，会立即执行（不再像旧版那样漂移）。";
        }

        private void OnActionChanged(int index)
        {
            if (_count.IsArmed || _inGrace)
            {
                _actionTabs.Selected = (int)_action;
                Host.SetStatus("倒计时运行中，操作类型不可更改（先重置）");
                return;
            }
            _action = (PowerAction)index;
            Host.Cfg.PowerAction = PowerOps.ToConfig(_action);
            Host.Cfg.Save();
            UpdateActionHint();
            Host.SetStatus("到点将" + PowerOps.Verb(_action));
        }

        // ── 倒计时 ──────────────────────────────────────────────

        private void StartCountdown()
        {
            if (_inGrace) return;
            if (_count.IsArmed && _count.IsPaused)
            {
                _count.Resume();
                _btnPause.Text = "暂停";
                UpdateHint();
                Host.SetStatus("已继续倒计时");
                return;
            }
            TimeSpan total = _setup.Total;
            if (total <= TimeSpan.Zero)
            {
                Host.SetStatus("请先设定时间（时 / 分 / 秒）");
                return;
            }
            _count.Start(total);
            _tick.Start();
            _setup.Lock(true);
            Host.Log("定时电源开始：" + PowerOps.Label(_action) + "，倒计时 " + Countdown.Format(total) +
                     "，目标 " + _count.Deadline.ToString("yyyy-MM-dd HH:mm:ss"));
            _btnStart.Enabled = false;
            _btnPause.Enabled = true;
            _btnPause.Text = "暂停";
            _btnReset.Enabled = true;
            Host.SetTrayColor(Theme.Warn);
            UpdateHint();
            UpdateRunStatus();
            UpdateTray();
            Host.SetStatus("倒计时运行中，到点将" + PowerOps.Verb(_action));
        }

        /// 卡片里的状态文字（和窗口底部状态栏是两处，别只更新一处）。
        private void UpdateRunStatus()
        {
            if (_status == null || _inGrace || !_count.IsArmed) return;
            if (_count.IsPaused)
            {
                _status.Text = "已暂停 · 剩余 " + Countdown.Format(_count.Remaining) + "（点「继续」恢复）";
                _status.ForeColor = Theme.Warn;
                return;
            }
            _status.Text = "倒计时运行中 · 目标 " + _count.Deadline.ToString("HH:mm:ss") + " · 到点将" + PowerOps.Verb(_action);
            _status.ForeColor = Theme.TextDim;
        }

        private void TogglePause()
        {
            if (!_count.IsArmed) return;
            if (_count.IsPaused)
            {
                _count.Resume();
                _btnPause.Text = "暂停";
                Host.SetStatus("已继续倒计时");
            }
            else
            {
                _count.Pause();
                _btnPause.Text = "继续";
                Host.SetStatus("已暂停（剩余 " + Countdown.Format(_count.Remaining) + "）");
            }
            UpdateHint();
            UpdateRunStatus();
            UpdateTray();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_inGrace)
            {
                int secs = (int)Math.Ceiling(_grace.Remaining.TotalSeconds);
                if (Visible)
                {
                    _digit.Text = secs.ToString("00") + " 秒";
                    _digit.ForeColor = Theme.Accent;
                    _status.Text = "⚠ 即将" + PowerOps.Verb(_action) + "，点「立即取消」可撤销";
                }
                Host.SetTrayStatus(AppInfo.Name + " · " + secs + " 秒后" + PowerOps.Label(_action));
                if (_grace.Poll()) ExecuteNow();
                return;
            }

            if (!_count.IsArmed)
            {
                _tick.Stop();
                return;
            }

            if (Visible) _digit.Text = Countdown.Format(_count.Remaining);
            UpdateTray();
            if (_count.Poll()) EnterGrace();
        }

        private void UpdateTray()
        {
            if (_count.IsArmed && !_count.IsPaused)
                Host.SetTrayStatus(AppInfo.Name + " · 定时" + PowerOps.Label(_action) + " 剩 " + Countdown.Format(_count.Remaining));
            else if (_count.IsArmed)
                Host.SetTrayStatus(AppInfo.Name + " · 已暂停（剩 " + Countdown.Format(_count.Remaining) + "）");
            else
                Host.SetTrayStatus(AppInfo.DisplayName);
        }

        private void EnterGrace()
        {
            _inGrace = true;
            int window = Math.Max(1, Host.Cfg.CancelWindowSec);
            _grace.Start(TimeSpan.FromSeconds(window));
            _btnCancel.Visible = true;
            _btnStart.Enabled = false;
            _btnPause.Enabled = false;
            _btnReset.Enabled = true;
            Host.SetTrayColor(Theme.Accent);
            Host.Log("定时到点，进入 " + window + " 秒撤销窗口：" + PowerOps.Label(_action));
            Host.Notify(AppInfo.Name, PowerOps.Label(_action) + " 将在 " + window + " 秒后执行，可点「立即取消」撤销");
            Host.SetStatus("⚠ " + window + " 秒后" + PowerOps.Verb(_action) + "，可取消");
        }

        private void CancelGrace()
        {
            if (!_inGrace) return;
            _grace.Reset();
            _inGrace = false;
            _btnCancel.Visible = false;
            Host.Log("用户在撤销窗口内取消了定时" + PowerOps.Label(_action));
            Host.SetStatus("已取消本次" + PowerOps.Label(_action));
            Host.SetTrayColor(Theme.Ok);
            ResetUi("已取消（撤销窗口内）");
        }

        private void ExecuteNow()
        {
            _inGrace = false;
            _tick.Stop();
            _btnCancel.Visible = false;
            if (DemoMode)
            {
                Host.Log("演示模式：已走完「倒计时 → 撤销窗口 → 执行」全流程，跳过真正的" + PowerOps.Label(_action));
                Host.SetStatus("演示模式：流程走通，未执行" + PowerOps.Label(_action));
                ResetUi("演示模式：流程走通，未执行" + PowerOps.Label(_action));
                return;
            }
            Host.Log("执行电源操作：" + PowerOps.Label(_action));
            string error;
            bool ok = PowerOps.Execute(_action, out error);
            if (!ok)
            {
                Host.Log("电源操作失败：" + error);
                Host.Notify(AppInfo.Name, PowerOps.Label(_action) + " 执行失败：" + error);
                MessageBox.Show(this, "执行失败：" + error, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ResetUi("执行失败：" + error);
                return;
            }
            Host.Log("电源操作已执行：" + PowerOps.Label(_action));
            // 睡眠：本进程会在唤醒后继续走到这里；关机/重启：进程随系统一起结束
            ResetUi("已执行：" + PowerOps.Label(_action) + "（睡眠唤醒后可继续使用）");
        }

        private void ResetUi(string reason)
        {
            _count.Reset();
            _grace.Reset();
            _inGrace = false;
            _tick.Stop();
            if (_btnCancel != null) _btnCancel.Visible = false;
            if (_digit != null)
            {
                _digit.Text = "00:00:00";
                _digit.ForeColor = Theme.Text;
            }
            if (_btnStart != null)
            {
                _btnStart.Enabled = true;
                _btnStart.Text = "开始";
            }
            if (_btnPause != null)
            {
                _btnPause.Enabled = false;
                _btnPause.Text = "暂停";
            }
            if (_btnReset != null) _btnReset.Enabled = false;
            if (_setup != null) _setup.Lock(false);
            if (_status != null)
            {
                _status.Text = reason;
                _status.ForeColor = Theme.TextDim;
            }
            UpdateHint();
            Host.SetTrayColor(Theme.Ok);
            Host.SetTrayStatus(AppInfo.DisplayName);
        }

        // ── 生命周期 ────────────────────────────────────────────

        public override void OnEnter()
        {
            UpdateHint();
            UpdateActionHint();
        }

        public override void OnLeave()
        {
            // 故意什么都不做：切到别的页或最小化到托盘时，倒计时必须继续跑。
        }

        public override void OnAppExit()
        {
            _tick.Stop();
        }
    }
}
