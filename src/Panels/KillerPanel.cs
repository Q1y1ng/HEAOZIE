// 「定时关闭应用」页：勾选进程 → 设定倒计时 → 到点结束它们。
//
// 对照原 Python 版 app_killer.py（724 行 tkinter）：
//   · 保留：进程表（名称/PID/内存 + 勾选）、全选/取消、刷新、预设、开始/暂停/重置、
//           到点前 5 秒可取消、点选目标（GetAsyncKeyState + WindowFromPoint）、点选时自动最小化
//   · 改进：安全名单更完整（系统关键进程不进列表也不能点选）；开始前二次确认目标清单；
//           结束后逐条记录失败原因（原版只报一个「N 个失败」）；勾选目标持久化到配置；
//           托盘常驻显示剩余时间；倒计时用绝对目标时刻（睡眠唤醒不漂移）。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class KillerPanel : PanelBase
    {
        private readonly Countdown _count = new Countdown();
        private readonly Countdown _grace = new Countdown();
        private readonly Timer _tick = new Timer { Interval = 250 };
        private readonly Timer _pickTimer = new Timer { Interval = 100 };
        private readonly Timer _refreshAfterKill = new Timer { Interval = 1500 };

        private TimeSetupCard _setup;
        private ProcTable _table;
        private Label _countLabel;
        private Label _digit;
        private Label _status;
        private SoftButton _btnStart;
        private SoftButton _btnPause;
        private SoftButton _btnReset;
        private SoftButton _btnCancel;
        private SoftButton _btnPick;
        private SoftButton _btnMinimize;

        private bool _inGrace;
        private bool _picking;
        private bool _sawButtonRelease;
        private bool _autoMinimize;

        /// 演示模式（--killer-demo=N 冒烟测试用）：走完整流程但**绝不真的结束进程**。
        internal bool DemoMode;

        public override string Title { get { return "定时关闭应用"; } }

        public KillerPanel(IHzHost host) : base(host)
        {
            _autoMinimize = host.Cfg.KillerAutoMinimize;

            // 左右分栏：左列时间与倒计时，右列进程表吃满剩余高度 —— 一页装下，不用滚动
            TableLayoutPanel split = BuildSplitLayout(500f);

            _setup = new TimeSetupCard("设定倒计时", "到达目标时刻后结束勾选的进程（时 : 分 : 秒）", host.Cfg.KillerPresets, 86, true);
            _setup.Height = 190;
            _setup.SetSeconds(1800);
            _setup.Changed += delegate { UpdateHint(); };

            SoftCard countdownCard = BuildCountdownCard();
            split.Controls.Add(BuildColumn(_setup, countdownCard), 0, 0);
            split.Controls.Add(BuildTableCard(), 1, 0);

            _tick.Tick += OnTick;
            _pickTimer.Tick += OnPickTick;
            _refreshAfterKill.Tick += delegate { _refreshAfterKill.Stop(); RefreshProcesses(); };

            ResetUi("等待启动");
        }

        /// 冒烟测试入口：预填 seconds 并立刻开始（跳过二次确认，不真的结束进程）。
        internal void DemoStart(int seconds)
        {
            DemoMode = true;
            RefreshProcesses();
            _table.CheckTop(3);          // 自动挑 3 个目标（绝不会真的结束它们）
            _setup.SetSeconds(seconds);
            StartCountdown(true);
            Host.Log("演示模式：倒计时 " + seconds + " 秒，到点后只记录不结束进程");
        }

        // ── 界面 ────────────────────────────────────────────────

        private SoftCard BuildTableCard()
        {
            var card = new SoftCard("选择要关闭的进程",
                "勾选左侧方框；也可以点「点击选择」后在屏幕上点目标窗口") { Dock = DockStyle.Fill };

            var tableHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 0, 16, 8),
                BackColor = Theme.Card
            };
            _table = new ProcTable { Dock = DockStyle.Fill };
            _table.SelectionChanged += delegate { UpdateCountLabel(); };
            tableHost.Controls.Add(_table);
            card.Controls.Add(tableHost);

            FlowLayoutPanel row = AddButtonRow(card, false, 74, true);
            var refresh = new SoftButton("刷新", 74, 30) { Icon = GI.Refresh };
            refresh.Click += delegate { RefreshProcesses(); };
            var all = new SoftButton("全选", 68, 30) { Icon = GI.Check };
            all.Click += delegate { _table.CheckAll(); };
            var none = new SoftButton("取消全选", 90, 30) { Icon = GI.Close };
            none.Click += delegate { _table.UncheckAll(); };

            _btnPick = new SoftButton("点击选择", 100, 30) { Icon = GI.Search, Tone = Theme.Info };
            _btnPick.Click += delegate { TogglePicker(); };

            _btnMinimize = new SoftButton(MinimizeLabel(), 150, 30) { Icon = GI.Down };
            _btnMinimize.Click += delegate { ToggleAutoMinimize(); };

            _countLabel = Soft.Label("", Theme.TextFaint, Theme.FontSmall, true);
            _countLabel.Margin = new Padding(12, 8, 0, 0);

            row.Controls.Add(refresh);
            row.Controls.Add(all);
            row.Controls.Add(none);
            row.Controls.Add(_btnPick);
            row.Controls.Add(_btnMinimize);
            row.Controls.Add(_countLabel);
            return card;
        }

        private SoftCard BuildCountdownCard()
        {
            var card = new SoftCard("倒计时", null) { Height = 240 };

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
            _btnStart.Click += delegate { StartCountdown(false); };

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
            return card;
        }

        private string MinimizeLabel()
        {
            return _autoMinimize ? "点选时自动最小化：开" : "点选时自动最小化：关";
        }

        private void ToggleAutoMinimize()
        {
            _autoMinimize = !_autoMinimize;
            Host.Cfg.KillerAutoMinimize = _autoMinimize;
            Host.Cfg.Save();
            _btnMinimize.Text = MinimizeLabel();
            Host.SetStatus("点选目标时" + (_autoMinimize ? "自动最小化本窗口" : "保持本窗口"));
        }

        private void UpdateCountLabel()
        {
            if (_countLabel == null) return;
            int n = _table == null ? 0 : _table.CheckedPids.Count;
            _countLabel.Text = n == 0 ? "已勾选 0 个进程" : "已勾选 " + n + " 个进程";
        }

        private void UpdateHint()
        {
            if (_count.IsArmed)
            {
                _setup.SetHintOverride("目标时刻：" + _count.Deadline.ToString("yyyy-MM-dd HH:mm:ss") +
                                       (_count.IsPaused ? "（已暂停，剩余 " + Countdown.Format(_count.Remaining) + "）" : ""));
                return;
            }
            _setup.SetHintOverride(null);
        }

        // ── 进程列表 ────────────────────────────────────────────

        private bool _loaded;

        private void RefreshProcesses()
        {
            var rows = ProcList.Enumerate();
            _table.SetRows(rows);

            // 恢复上次勾选的目标：按进程名匹配（PID 每次都变），**每个名字只恢复一个**。
            // 否则 steamwebhelper.exe 这种一进程名的多个实例会被一次性全勾上，
            // 用户明明只勾了一个，到点却关了七个。
            if (Host.Cfg.KillerTargets.Count > 0)
            {
                var pending = new List<string>(Host.Cfg.KillerTargets);
                foreach (ProcEntry e in rows)
                {
                    int idx = IndexOfName(pending, e.Name);
                    if (idx < 0) continue;
                    _table.SetChecked(e.Pid, true);
                    pending.RemoveAt(idx);
                    if (pending.Count == 0) break;
                }
            }
            _loaded = true;
            UpdateCountLabel();
            Host.SetStatus("已刷新进程列表：" + rows.Count + " 个可关闭进程");
        }

        private static int IndexOfName(List<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        // ── 点选目标 ────────────────────────────────────────────

        private void TogglePicker()
        {
            if (_picking) { CancelPicker("已取消点选"); return; }
            if (_count.IsArmed || _inGrace)
            {
                Host.SetStatus("倒计时运行中不能点选目标");
                return;
            }
            _picking = true;
            _sawButtonRelease = false;
            _pickTimer.Start();
            _btnPick.Text = "点选进行中…";
            Host.SetStatus("请在屏幕上点击目标窗口（按 ESC 取消）");
            if (_autoMinimize)
            {
                Form f = FindForm();
                if (f != null) f.WindowState = FormWindowState.Minimized;
            }
        }

        private void OnPickTick(object sender, EventArgs e)
        {
            if (!_picking)
            {
                _pickTimer.Stop();
                return;
            }
            if (Native.KeyDown(Native.VK_ESCAPE))
            {
                CancelPicker("已取消点选");
                return;
            }
            // 先等「点击选择」这一下松开，再抓下一次按下 —— 否则会立刻抓到自己这次点击
            if (!Native.KeyDown(Native.VK_LBUTTON))
            {
                _sawButtonRelease = true;
                return;
            }
            if (!_sawButtonRelease) return;
            FinishPicker(Native.ProcessIdUnderCursor());
        }

        private void CancelPicker(string reason)
        {
            _pickTimer.Stop();
            _picking = false;
            RestoreAfterPick();
            _btnPick.Text = "点击选择";
            Host.SetStatus(reason);
        }

        private void RestoreAfterPick()
        {
            Form f = FindForm();
            if (f == null) return;
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
        }

        private void FinishPicker(int pid)
        {
            _pickTimer.Stop();
            _picking = false;
            RestoreAfterPick();
            _btnPick.Text = "点击选择";

            if (pid <= 0)
            {
                Host.SetStatus("没有取到有效窗口，请重试");
                return;
            }
            if (ProcList.IsSelf(pid))
            {
                Host.SetStatus("不能选择本程序自身");
                return;
            }
            string name = ProcList.NameOf(pid);
            if (string.IsNullOrEmpty(name))
            {
                Host.SetStatus("该窗口所属进程已退出");
                return;
            }
            if (ProcList.IsProtected(name))
            {
                Host.SetStatus("「" + name + "」是系统关键进程，已跳过（关掉会影响桌面或服务）");
                Host.Log("点选被拒：" + name + " 在安全名单内");
                return;
            }
            if (!_table.HighlightPid(pid))
            {
                Host.SetStatus("「" + name + "」不在当前列表里，点「刷新」后重试");
                return;
            }
            _table.SetChecked(pid, true);
            Host.SetStatus("已勾选：" + name + "（PID " + pid + "）");
        }

        // ── 倒计时 ──────────────────────────────────────────────

        private void StartCountdown(bool skipConfirm)
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

            List<int> pids = _table.CheckedPids;
            if (pids.Count == 0)
            {
                Host.SetStatus("请先勾选要关闭的进程");
                return;
            }
            TimeSpan total = _setup.Total;
            if (total <= TimeSpan.Zero)
            {
                Host.SetStatus("请先设定时间（时 / 分 / 秒）");
                return;
            }

            if (!skipConfirm)
            {
                string names = string.Join("、", _table.CheckedNames.ToArray());
                if (names.Length > 320) names = names.Substring(0, 320) + "…";
                DialogResult answer = MessageBox.Show(this,
                    "将在 " + Countdown.Format(total) + " 后结束以下 " + pids.Count + " 个进程：\r\n\r\n" +
                    names + "\r\n\r\n确认开始倒计时？（到点前 5 秒还可以取消）",
                    AppInfo.DisplayName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (answer != DialogResult.OK)
                {
                    Host.SetStatus("已取消");
                    return;
                }
            }

            Host.Cfg.KillerTargets.Clear();
            Host.Cfg.KillerTargets.AddRange(_table.CheckedNames);
            Host.Cfg.Save();

            _count.Start(total);
            _tick.Start();
            _setup.Lock(true);
            _btnStart.Enabled = false;
            _btnPause.Enabled = true;
            _btnPause.Text = "暂停";
            _btnReset.Enabled = true;
            _btnPick.Enabled = false;
            Host.Log("定时关闭应用开始：目标 " + pids.Count + " 个进程（" + string.Join(", ", _table.CheckedNames.ToArray()) +
                     "），倒计时 " + Countdown.Format(total) + "，目标 " + _count.Deadline.ToString("yyyy-MM-dd HH:mm:ss"));
            Host.SetTrayColor(Theme.Warn);
            UpdateHint();
            UpdateRunStatus();
            UpdateTray();
            Host.SetStatus("倒计时运行中，到点将结束 " + pids.Count + " 个进程");
        }

        private void UpdateRunStatus()
        {
            if (_status == null || _inGrace || !_count.IsArmed) return;
            if (_count.IsPaused)
            {
                _status.Text = "已暂停 · 剩余 " + Countdown.Format(_count.Remaining) + "（点「继续」恢复）";
                _status.ForeColor = Theme.Warn;
                return;
            }
            _status.Text = "倒计时运行中 · 目标 " + _count.Deadline.ToString("HH:mm:ss") +
                           " · 到点结束 " + _table.CheckedPids.Count + " 个进程";
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
                    _status.Text = "⚠ 即将结束 " + _table.CheckedPids.Count + " 个进程，点「立即取消」可撤销";
                }
                Host.SetTrayStatus(AppInfo.Name + " · " + secs + " 秒后关闭 " + _table.CheckedPids.Count + " 个进程");
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
                Host.SetTrayStatus(AppInfo.Name + " · 定时关闭应用 剩 " + Countdown.Format(_count.Remaining));
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
            Host.Log("定时到点，进入 " + window + " 秒撤销窗口，准备结束 " + _table.CheckedPids.Count + " 个进程");
            Host.Notify(AppInfo.Name, "将在 " + window + " 秒后结束 " + _table.CheckedPids.Count + " 个进程，可取消");
            Host.SetStatus("⚠ " + window + " 秒后结束 " + _table.CheckedPids.Count + " 个进程，可取消");
        }

        private void CancelGrace()
        {
            if (!_inGrace) return;
            _grace.Reset();
            _inGrace = false;
            _btnCancel.Visible = false;
            Host.Log("用户在撤销窗口内取消了本次关闭");
            Host.SetTrayColor(Theme.Ok);
            ResetUi("已取消（撤销窗口内）");
            Host.SetStatus("已取消本次关闭");
        }

        private void ExecuteNow()
        {
            _inGrace = false;
            _tick.Stop();
            _btnCancel.Visible = false;

            List<int> pids = _table.CheckedPids;
            List<string> names = _table.CheckedNames;

            if (DemoMode)
            {
                Host.Log("演示模式：本应结束 " + pids.Count + " 个进程（" + string.Join(", ", names.ToArray()) + "），已跳过");
                Host.SetStatus("演示模式：流程走通，未结束任何进程");
                ResetUi("演示模式：流程走通，未结束任何进程");
                return;
            }

            if (pids.Count == 0)
            {
                Host.Log("到点执行时目标列表已空（进程可能已退出）");
                ResetUi("目标进程已全部退出，无需处理");
                return;
            }

            int ok = 0;
            int fail = 0;
            var failures = new StringBuilder();
            for (int i = 0; i < pids.Count; i++)
            {
                string error;
                if (ProcList.Kill(pids[i], out error))
                {
                    ok++;
                    Host.Log("已结束进程：" + (i < names.Count ? names[i] : "?") + "（PID " + pids[i] + "）");
                }
                else
                {
                    fail++;
                    string label = i < names.Count ? names[i] : "?";
                    failures.AppendLine("  · " + label + "（PID " + pids[i] + "）：" + error);
                    Host.Log("结束失败：" + label + "（PID " + pids[i] + "）：" + error);
                }
            }

            string summary = "成功结束 " + ok + " 个进程" + (fail > 0 ? "，" + fail + " 个失败（见日志）" : "");
            Host.Log("定时关闭应用执行完毕：" + summary + (fail > 0 ? "\n" + failures : ""));
            Host.Notify(AppInfo.Name, summary);
            Host.SetStatus(summary);
            ResetUi(summary);
            _refreshAfterKill.Start();   // 1.5 秒后刷新列表，和原版行为一致
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
            if (_btnPick != null) _btnPick.Enabled = true;
            if (_setup != null) _setup.Lock(false);
            if (_status != null)
            {
                _status.Text = reason;
                _status.ForeColor = Theme.TextDim;
            }
            UpdateHint();
            UpdateCountLabel();
            Host.SetTrayColor(Theme.Ok);
            Host.SetTrayStatus(AppInfo.DisplayName);
        }

        // ── 生命周期 ────────────────────────────────────────────

        public override void OnEnter()
        {
            // 只在首次进入时枚举：之后切回来保留用户已勾选的目标（要新数据点「刷新」）
            if (!_loaded) RefreshProcesses();
            UpdateHint();
            UpdateCountLabel();
        }

        public override void OnLeave()
        {
            if (_picking) CancelPicker("已离开本页，点选取消");
        }

        public override void OnAppExit()
        {
            _tick.Stop();
            _pickTimer.Stop();
            _refreshAfterKill.Stop();
        }
    }
}
