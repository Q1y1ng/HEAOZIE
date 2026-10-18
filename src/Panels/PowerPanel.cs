// 「定时电源」页（Fluent 版）：设定倒计时 → 到点执行 睡眠 / 关机 / 重启，执行前留撤销窗口。
// 逻辑与 v1 相同（绝对目标时刻、5 秒撤销窗口、托盘状态），只换外观层。

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
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer { Interval = 250 };

        private TimeSetupCard _setup;
        private FluentButton _btnSleep;
        private FluentButton _btnShutdown;
        private FluentButton _btnRestart;
        private FluentText _digit;
        private FluentText _status;
        private FluentText _actionHint;
        private FluentButton _btnStart;
        private FluentButton _btnPause;
        private FluentButton _btnReset;
        private FluentButton _btnCancel;

        private PowerAction _action;
        private bool _inGrace;

        internal bool DemoMode;

        public override string Title { get { return "定时电源"; } }

        public PowerPanel(IHzHost host) : base(host)
        {
            _action = PowerOps.Parse(host.Cfg.PowerAction, PowerAction.Sleep);

            _setup = AddCard(new TimeSetupCard("设定倒计时", "到达目标时刻后执行下面的操作", host.Cfg.PowerPresets, 86), 184);
            _setup.SetSeconds(1800);
            _setup.Changed += delegate { UpdateHint(); };

            AddCard(BuildActionCard(), 124);
            AddCard(BuildCountdownCard(), 240);

            _tick.Tick += OnTick;
            UpdateActionHint();
            ResetUi("等待启动");
        }

        internal void DemoStart(int seconds)
        {
            DemoMode = true;
            _setup.SetSeconds(seconds);
            StartCountdown();
            Host.Log("演示模式：倒计时 " + seconds + " 秒，到点后只记录不执行");
        }

        // ── 界面 ────────────────────────────────────────────────

        private FluentCard BuildActionCard()
        {
            var card = new FluentCard("到达时间后执行", null) { Height = 124 };
            _btnSleep = MakeAction("睡眠", 110, 16, PowerAction.Sleep);
            _btnShutdown = MakeAction("关机", 110, 134, PowerAction.Shutdown);
            _btnRestart = MakeAction("重启", 110, 252, PowerAction.Restart);
            card.Controls.Add(_btnSleep);
            card.Controls.Add(_btnShutdown);
            card.Controls.Add(_btnRestart);

            _actionHint = new FluentText("", Theme.Caption, Theme.TextTertiary)
            {
                Location = new Point(16, 96),
                Size = new Size(640, 18),
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_actionHint);
            UpdateActionButtons();
            return card;
        }

        private FluentButton MakeAction(string text, int width, int x, PowerAction action)
        {
            var b = new FluentButton(text, width, 36) { Location = new Point(x, 56) };
            b.Click += delegate { SelectAction(action); };
            return b;
        }

        private FluentCard BuildCountdownCard()
        {
            var card = new FluentCard("倒计时", null) { Height = 240 };

            _digit = new FluentText("00:00:00", Theme.Display, Theme.TextPrimary)
            {
                Location = new Point(16, 54),
                Size = new Size(460, 64),
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_digit);

            _status = new FluentText("等待启动", Theme.Body, Theme.TextSecondary)
            {
                Location = new Point(18, 126),
                Size = new Size(560, 22),
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_status);

            _btnStart = new FluentButton("开始", 112, 38) { Style = FluentButtonStyle.Accent, Glyph = Theme.Ico.Play, Location = new Point(16, 164) };
            _btnStart.Click += delegate { StartCountdown(); };
            _btnPause = new FluentButton("暂停", 112, 38) { Glyph = Theme.Ico.Pause, Location = new Point(136, 164) };
            _btnPause.Click += delegate { TogglePause(); };
            _btnReset = new FluentButton("重置", 100, 38) { Glyph = Theme.Ico.Stop, Location = new Point(256, 164) };
            _btnReset.Click += delegate { ResetUi("已重置"); Host.SetStatus("倒计时已重置"); };
            _btnCancel = new FluentButton("立即取消", 132, 38) { Style = FluentButtonStyle.Danger, Glyph = Theme.Ico.Close, Location = new Point(364, 164), Visible = false };
            _btnCancel.Click += delegate { CancelGrace(); };

            card.Controls.Add(_btnStart);
            card.Controls.Add(_btnPause);
            card.Controls.Add(_btnReset);
            card.Controls.Add(_btnCancel);
            return card;
        }

        private void UpdateActionButtons()
        {
            if (_btnSleep == null) return;
            _btnSleep.Selected = _action == PowerAction.Sleep;
            _btnShutdown.Selected = _action == PowerAction.Shutdown;
            _btnRestart.Selected = _action == PowerAction.Restart;
            _btnSleep.Invalidate();
            _btnShutdown.Invalidate();
            _btnRestart.Invalidate();
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
            _actionHint.SetText("到点将" + PowerOps.Verb(_action) + "；执行前有 " + Host.Cfg.CancelWindowSec +
                                " 秒撤销窗口。若期间电脑睡眠过、唤醒后目标时刻已过，会立即执行（不再像旧版那样漂移）。");
        }

        private void SelectAction(PowerAction action)
        {
            if (_count.IsArmed || _inGrace)
            {
                Host.SetStatus("倒计时运行中，操作类型不可更改（先重置）");
                return;
            }
            _action = action;
            Host.Cfg.PowerAction = PowerOps.ToConfig(_action);
            Host.Cfg.Save();
            UpdateActionButtons();
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
                UpdateRunStatus();
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
            Host.SetTrayColor(Theme.Warning);
            UpdateHint();
            UpdateRunStatus();
            UpdateTray();
            Host.SetStatus("倒计时运行中，到点将" + PowerOps.Verb(_action));
        }

        private void UpdateRunStatus()
        {
            if (_status == null || _inGrace || !_count.IsArmed) return;
            if (_count.IsPaused)
            {
                _status.SetText("已暂停 · 剩余 " + Countdown.Format(_count.Remaining) + "（点「继续」恢复）");
                _status.SetColor(Theme.Warning);
                return;
            }
            _status.SetText("倒计时运行中 · 目标 " + _count.Deadline.ToString("HH:mm:ss") + " · 到点将" + PowerOps.Verb(_action));
            _status.SetColor(Theme.TextSecondary);
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
                _digit.SetText(secs.ToString("00") + " 秒");
                _digit.SetColor(Theme.Danger);
                _status.SetText("⚠ 即将" + PowerOps.Verb(_action) + "，点「立即取消」可撤销");
                _status.SetColor(Theme.Danger);
                Host.SetTrayStatus(AppInfo.Name + " · " + secs + " 秒后" + PowerOps.Label(_action));
                if (_grace.Poll()) ExecuteNow();
                return;
            }

            if (!_count.IsArmed)
            {
                _tick.Stop();
                return;
            }

            _digit.SetText(Countdown.Format(_count.Remaining));
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
            Host.SetTrayColor(Theme.Danger);
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
            Host.SetTrayColor(Theme.Success);
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
                _digit.SetText("00:00:00");
                _digit.SetColor(Theme.TextPrimary);
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
                _status.SetText(reason);
                _status.SetColor(Theme.TextSecondary);
            }
            UpdateHint();
            Host.SetTrayColor(Theme.Success);
            Host.SetTrayStatus(AppInfo.DisplayName);
        }

        // ── 生命周期 ────────────────────────────────────────────

        public override void OnEnter()
        {
            UpdateHint();
            UpdateActionHint();
        }

        public override void OnAppExit()
        {
            _tick.Stop();
        }
    }
}
