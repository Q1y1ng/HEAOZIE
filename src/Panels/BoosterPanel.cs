// 「游戏加速」页：实时状态 + 一键 Turbo + 分类进程预览 + 日志。
//
// 对照原 Python 版 gaming_booster（860 行 GUI + 786 行核心）：
//   · 保留：实时状态（CPU/内存/GPU/磁盘/温度，2 秒刷新）、一键 Turbo（结束后台进程 →
//     整理内存 → 清理临时文件 → 高性能电源计划 → 刷新 DNS）、分类勾选、白名单、日志面板、
//     恢复电源计划、UAC 提权入口；
//   · 改进：内存整理走 memclean 的**系统级**空工作集（原版那段"清理 Standby Memory"
//     是两条无效的 PowerShell 咒语，已丢弃）；恢复电源计划改成恢复**切换前**的计划
//     （原版写死 Balanced）；GPU/温度拿不到时如实说明原因，不再静默显示 0；
//     Turbo 在后台线程跑，界面不卡死，每一步结果实时进日志。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class BoosterPanel : PanelBase
    {
        private readonly SystemMonitor _monitor = new SystemMonitor();
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly Dictionary<string, SoftButton> _categoryButtons = new Dictionary<string, SoftButton>(StringComparer.Ordinal);
        private readonly Dictionary<string, SoftButton> _optionButtons = new Dictionary<string, SoftButton>(StringComparer.Ordinal);

        private StatusView _status;
        private Label _statusNote;
        private LogView _log;
        private ProcTable _table;
        private SoftButton _btnTurbo;
        private SoftButton _btnRestore;
        private Label _previewNote;
        private int _tick;
        private bool _busy;
        private bool _live;
        private bool _lastDryRun;

        public override string Title { get { return "游戏加速"; } }

        public BoosterPanel(IHzHost host) : base(host)
        {
            TableLayoutPanel split = BuildSplitLayout(520f);
            split.Controls.Add(BuildColumn(BuildStatusCard(), BuildTurboCard()), 0, 0);
            split.Controls.Add(BuildRightColumn(), 1, 0);

            _statusTimer.Tick += delegate { RefreshStatus(); };
            _log.Append("就绪。勾选分类后点「一键 Turbo」；每一步结果都会写在这里。");
            _log.Append("当前分类：" + BoosterOps.DescribeCategories(Host.Cfg.BoosterCats), Theme.TextFaint);
            UpdateOptionButtons();
        }

        // ── 左列 ────────────────────────────────────────────────

        private SoftCard BuildStatusCard()
        {
            var card = new SoftCard("系统状态", "2 秒刷新；GPU 走 nvidia-smi，温度走 WMI") { Height = 276 };
            _status = new StatusView { Dock = DockStyle.Fill };
            // 卡片的 Padding 已经把标题区留出来了，容器**不能再叠一层 58**（叠了会把内容挤没）
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0), BackColor = Theme.Card };
            host.Controls.Add(_status);
            card.Controls.Add(host);

            _statusNote = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                AutoSize = false,
                BackColor = Theme.Card,
                ForeColor = Theme.TextFaint,
                Font = Theme.FontSmall,
                Padding = new Padding(16, 0, 0, 0)
            };
            card.Controls.Add(_statusNote);
            return card;
        }

        private SoftCard BuildTurboCard()
        {
            var card = new SoftCard("一键 Turbo", "按顺序：结束后台进程 → 整理内存 → 清理临时文件 → 高性能电源 → 刷新 DNS")
            { Height = 280 };

            _btnTurbo = new SoftButton("一键 Turbo", 210, 44) { Primary = true, Icon = GI.Bolt };
            _btnTurbo.Location = new Point(16, 58);
            _btnTurbo.Font = Theme.FontUiBold;
            _btnTurbo.Click += delegate { RunTurbo(); };
            card.Controls.Add(_btnTurbo);

            _btnRestore = new SoftButton("恢复电源计划", 138, 34) { Icon = GI.Refresh };
            _btnRestore.Location = new Point(236, 63);
            _btnRestore.Click += delegate { RestorePowerPlan(); };
            card.Controls.Add(_btnRestore);

            var white = new SoftButton("白名单", 96, 34) { Icon = GI.Shield };
            white.Location = new Point(382, 63);
            white.Click += delegate { EditWhitelist(); };
            card.Controls.Add(white);

            var options = new FlowLayoutPanel
            {
                Location = new Point(16, 110),
                Size = new Size(480, 78),
                BackColor = Theme.Card,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };
            AddOption(options, "kill", "结束后台进程", 148);
            AddOption(options, "trim", "整理内存", 118);
            AddOption(options, "clean", "清理临时文件", 148);
            AddOption(options, "power", "高性能电源", 138);
            AddOption(options, "dns", "刷新 DNS", 118);
            AddOption(options, "strong", "强效内存整理", 148);
            AddOption(options, "prefetch", "清理 Prefetch", 138);
            card.Controls.Add(options);

            string note = SysEnv.IsAdmin()
                ? "当前是管理员权限：强效内存整理与 Prefetch 清理可用。"
                : "当前是普通用户：强效内存整理需要管理员（自动回退温和），Prefetch 清理会跳过。";
            var hint = Soft.Label(note, Theme.TextFaint, Theme.FontSmall, true);
            hint.Location = new Point(16, 194);
            card.Controls.Add(hint);

            if (!SysEnv.IsAdmin())
            {
                var elevate = new SoftButton("以管理员重启", 130, 30) { Icon = GI.Shield, Tone = Theme.Info };
                elevate.Location = new Point(16, 216);
                elevate.Click += delegate
                {
                    Host.Log("用户请求以管理员身份重启");
                    MainForm.RequestElevatedRestart(0);
                };
                card.Controls.Add(elevate);
            }
            return card;
        }

        private void AddOption(FlowLayoutPanel host, string key, string text, int width)
        {
            var b = new SoftButton(text, width, 30) { Tone = Theme.TextDim };
            b.Click += delegate { ToggleOption(key); };
            _optionButtons[key] = b;
            host.Controls.Add(b);
        }

        // ── 右列 ────────────────────────────────────────────────

        private Control BuildRightColumn()
        {
            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Bg,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(14, 0, 0, 0)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 232f));

            right.Controls.Add(BuildProcessCard(), 0, 0);
            right.Controls.Add(BuildLogCard(), 0, 1);
            return right;
        }

        private SoftCard BuildProcessCard()
        {
            var card = new SoftCard("后台进程（按分类）",
                "勾选框 = 本次会被结束；取消勾选会把这个进程名加入白名单（以后也不结束）") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };

            // 添加顺序 = 停靠处理的反序：Fill 必须最先加，否则 Top/Bottom 会压在它上面
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 4), BackColor = Theme.Card };
            _table = new ProcTable { Dock = DockStyle.Fill };
            _table.SelectionChanged += delegate { SyncWhitelistFromTable(); };
            host.Controls.Add(_table);
            card.Controls.Add(host);

            var chips = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Theme.Card,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 6)
            };
            foreach (string key in BoosterOps.CategoryOrder)
            {
                var b = new SoftButton(BoosterOps.CategoryLabels[key], 96, 28) { Tone = Theme.TextDim };
                string captured = key;
                b.Click += delegate { ToggleCategory(captured); };
                _categoryButtons[key] = b;
                chips.Controls.Add(b);
            }
            card.Controls.Add(chips);

            FlowLayoutPanel row = AddButtonRow(card, false);
            var refresh = new SoftButton("刷新", 78, 30) { Icon = GI.Refresh };
            refresh.Click += delegate { RefreshProcessPreview(); };
            var all = new SoftButton("全选", 72, 30) { Icon = GI.Check };
            all.Click += delegate { _table.CheckAll(); SyncWhitelistFromTable(); };
            var none = new SoftButton("全部排除", 96, 30) { Icon = GI.Close };
            none.Click += delegate { _table.UncheckAll(); SyncWhitelistFromTable(); };
            _previewNote = Soft.Label("", Theme.TextFaint, Theme.FontSmall, true);
            _previewNote.Margin = new Padding(10, 8, 0, 0);
            row.Controls.Add(refresh);
            row.Controls.Add(all);
            row.Controls.Add(none);
            row.Controls.Add(_previewNote);
            return card;
        }

        private SoftCard BuildLogCard()
        {
            var card = new SoftCard("日志", null) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0) };
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 4), BackColor = Theme.Card };
            _log = new LogView { Dock = DockStyle.Fill };
            host.Controls.Add(_log);
            card.Controls.Add(host);

            FlowLayoutPanel row = AddButtonRow(card, false, 40, false);
            var clear = new SoftButton("清空", 78, 28) { Icon = GI.Trash };
            clear.Click += delegate { _log.Clear(); };
            var copy = new SoftButton("复制日志", 100, 28) { Icon = GI.Info };
            copy.Click += delegate { CopyLog(); };
            var open = new SoftButton("打开日志文件", 122, 28) { Icon = GI.Folder };
            open.Click += delegate { OpenLogFile(); };
            row.Controls.Add(clear);
            row.Controls.Add(copy);
            row.Controls.Add(open);
            return card;
        }

        // ── 状态刷新 ────────────────────────────────────────────

        private void RefreshStatus()
        {
            if (!_live) return;
            _tick++;
            bool withGpu = (_tick % 2) == 1;          // nvidia-smi 是外部进程，隔一次查
            bool withTemp = (_tick % 5) == 1;         // WMI 更慢，5 次查一次（内部还有 10 秒缓存）
            SystemSnapshot snap = _monitor.Capture(withGpu, withTemp);
            _status.Update(snap, "");
            string note = "刷新于 " + snap.At.ToString("HH:mm:ss");
            if (!string.IsNullOrEmpty(Host.Cfg.BoosterLastRun)) note += " · 上次 Turbo " + Host.Cfg.BoosterLastRun;
            _statusNote.Text = note;
        }

        // ── 分类与选项 ──────────────────────────────────────────

        private void ToggleCategory(string key)
        {
            bool value = !Host.Cfg.BoosterCats.Get(key);
            Host.Cfg.BoosterCats.Set(key, value);
            Host.Cfg.Save();
            UpdateCategoryButtons();
            RefreshProcessPreview();
            Host.SetStatus((value ? "已勾选" : "已取消") + "分类「" + BoosterOps.CategoryLabels[key] + "」");
        }

        private void UpdateCategoryButtons()
        {
            foreach (KeyValuePair<string, SoftButton> kv in _categoryButtons)
            {
                bool on = Host.Cfg.BoosterCats.Get(kv.Key);
                kv.Value.Primary = on;
                kv.Value.Tone = on ? Color.White : Theme.TextDim;
                kv.Value.Invalidate();
            }
        }

        private void ToggleOption(string key)
        {
            switch (key)
            {
                case "kill": Host.Cfg.BoosterKill = !Host.Cfg.BoosterKill; break;
                case "trim": Host.Cfg.BoosterTrim = !Host.Cfg.BoosterTrim; break;
                case "clean": Host.Cfg.BoosterCleanTemp = !Host.Cfg.BoosterCleanTemp; break;
                case "power": Host.Cfg.BoosterHighPerf = !Host.Cfg.BoosterHighPerf; break;
                case "dns": Host.Cfg.BoosterFlushDns = !Host.Cfg.BoosterFlushDns; break;
                case "strong": Host.Cfg.BoosterStrongTrim = !Host.Cfg.BoosterStrongTrim; break;
                case "prefetch": Host.Cfg.BoosterCleanPrefetch = !Host.Cfg.BoosterCleanPrefetch; break;
            }
            Host.Cfg.Save();
            UpdateOptionButtons();
        }

        private void UpdateOptionButtons()
        {
            SetOption("kill", Host.Cfg.BoosterKill);
            SetOption("trim", Host.Cfg.BoosterTrim);
            SetOption("clean", Host.Cfg.BoosterCleanTemp);
            SetOption("power", Host.Cfg.BoosterHighPerf);
            SetOption("dns", Host.Cfg.BoosterFlushDns);
            SetOption("strong", Host.Cfg.BoosterStrongTrim);
            SetOption("prefetch", Host.Cfg.BoosterCleanPrefetch);
        }

        private void SetOption(string key, bool on)
        {
            SoftButton b;
            if (!_optionButtons.TryGetValue(key, out b)) return;
            b.Primary = on;
            b.Tone = on ? Color.White : Theme.TextDim;
            b.Invalidate();
        }

        // ── 进程预览与白名单 ────────────────────────────────────

        private void RefreshProcessPreview()
        {
            if (_table == null) return;
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in BoosterOps.CategoryOrder)
            {
                if (!Host.Cfg.BoosterCats.Get(key)) continue;
                foreach (string name in BoosterOps.ProcessesOf(key)) wanted.Add(name);
            }

            var rows = new List<ProcEntry>();
            foreach (ProcEntry e in ProcList.Enumerate())
            {
                string bare = e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? e.Name.Substring(0, e.Name.Length - 4)
                    : e.Name;
                if (wanted.Contains(bare)) rows.Add(e);
            }
            _table.SetRows(rows);
            _table.CheckAll();
            foreach (ProcEntry e in rows)
            {
                if (Host.Cfg.BoosterWhitelist.Contains(e.Name)) _table.SetChecked(e.Pid, false);
            }
            UpdatePreviewNote();
        }

        private void UpdatePreviewNote()
        {
            if (_previewNote == null) return;
            int total = _table.RowCount;
            int will = _table.CheckedPids.Count;
            _previewNote.Text = total == 0
                ? "当前分类下没有正在运行的进程"
                : "匹配 " + total + " 个，将结束 " + will + " 个";
        }

        /// 表格勾选 ↔ 白名单双向同步（取消勾选即加入白名单）。
        private void SyncWhitelistFromTable()
        {
            if (_table == null) return;
            var checkedNames = new HashSet<string>(_table.CheckedNames, StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            var updated = new List<string>();
            foreach (string name in Host.Cfg.BoosterWhitelist)
            {
                if (checkedNames.Contains(name)) { changed = true; continue; }   // 重新勾选 = 移出白名单
                updated.Add(name);
            }
            // 表格里被取消勾选的进程名加入白名单
            foreach (string name in _table.UncheckedNames)
            {
                if (!updated.Contains(name)) { updated.Add(name); changed = true; }
            }
            if (changed)
            {
                Host.Cfg.BoosterWhitelist.Clear();
                Host.Cfg.BoosterWhitelist.AddRange(updated);
                Host.Cfg.Save();
            }
            UpdatePreviewNote();
        }

        private void EditWhitelist()
        {
            using (var dlg = new Form
            {
                Text = "白名单 · 每行一个进程名（如 chrome.exe）",
                Size = new Size(420, 470),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                Font = Theme.FontUi,
                MinimizeBox = false,
                MaximizeBox = false,
                Icon = Theme.AppIcon(16)
            })
            {
                var boxHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Theme.Bg };
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Theme.Card,
                    ForeColor = Theme.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = Theme.FontMono,
                    Text = string.Join("\r\n", Host.Cfg.BoosterWhitelist.ToArray())
                };
                boxHost.Controls.Add(box);
                dlg.Controls.Add(boxHost);

                var buttons = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Bg, Padding = new Padding(14, 8, 14, 8) };
                var save = new SoftButton("保存", 96, 32) { Primary = true };
                save.Location = new Point(180, 10);
                var cancel = new SoftButton("取消", 96, 32);
                cancel.Location = new Point(286, 10);
                cancel.Click += delegate { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };
                save.Click += delegate
                {
                    var list = new List<string>();
                    foreach (string raw in box.Text.Replace("\r", "").Split('\n'))
                    {
                        string name = raw.Trim();
                        if (name.Length == 0) continue;
                        if (!list.Contains(name)) list.Add(name);
                    }
                    Host.Cfg.BoosterWhitelist.Clear();
                    Host.Cfg.BoosterWhitelist.AddRange(list);
                    Host.Cfg.Save();
                    Host.Log("白名单已更新，共 " + list.Count + " 条");
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };
                buttons.Controls.Add(save);
                buttons.Controls.Add(cancel);
                dlg.Controls.Add(buttons);
                dlg.AcceptButton = null;

                dlg.ShowDialog(FindForm());
                RefreshProcessPreview();
                Host.SetStatus("白名单：" + Host.Cfg.BoosterWhitelist.Count + " 条");
            }
        }

        // ── 一键 Turbo ─────────────────────────────────────────

        private BoosterOptions CollectOptions()
        {
            var opt = new BoosterOptions
            {
                KillProcesses = Host.Cfg.BoosterKill,
                TrimMemory = Host.Cfg.BoosterTrim,
                CleanTemp = Host.Cfg.BoosterCleanTemp,
                HighPerformance = Host.Cfg.BoosterHighPerf,
                FlushDns = Host.Cfg.BoosterFlushDns,
                StrongTrim = Host.Cfg.BoosterStrongTrim,
                CleanPrefetch = Host.Cfg.BoosterCleanPrefetch
            };
            opt.Categories.Browsers = Host.Cfg.BoosterCats.Browsers;
            opt.Categories.Chat = Host.Cfg.BoosterCats.Chat;
            opt.Categories.Download = Host.Cfg.BoosterCats.Download;
            opt.Categories.Music = Host.Cfg.BoosterCats.Music;
            opt.Categories.Office = Host.Cfg.BoosterCats.Office;
            opt.Categories.Cloud = Host.Cfg.BoosterCats.Cloud;
            opt.Categories.Dev = Host.Cfg.BoosterCats.Dev;
            opt.Categories.Game = Host.Cfg.BoosterCats.Game;
            return opt;
        }

        private void RunTurbo()
        {
            RunTurbo(false);
        }

        /// 冒烟测试入口：走完整编排与日志，但后台进程只预演、其余动作一律不执行。
        /// 延迟 400ms 再跑：面板是在主窗口构造期创建的，那时窗口句柄还没建好，
        /// 工作线程的 BeginInvoke 会失败（真实点击时不存在这个问题）。
        internal void DemoStart()
        {
            var delay = new System.Windows.Forms.Timer { Interval = 400 };
            delay.Tick += delegate
            {
                delay.Stop();
                delay.Dispose();
                _log.Append("演示模式：只预演「将结束哪些进程」，内存/清理/电源/DNS 全部跳过", Theme.Warn);
                var opt = new BoosterOptions
                {
                    KillProcesses = true,
                    TrimMemory = true,
                    CleanTemp = true,
                    HighPerformance = true,
                    FlushDns = true,
                    StrongTrim = false,
                    CleanPrefetch = false
                };
                opt.Categories.Browsers = true;
                opt.Categories.Chat = true;
                RunTurbo(true, opt);
            };
            delay.Start();
        }

        private void RunTurbo(bool dryRun)
        {
            RunTurbo(dryRun, null);
        }

        private void RunTurbo(bool dryRun, BoosterOptions preset)
        {
            if (_busy) return;
            _busy = true;
            _btnTurbo.Enabled = false;
            _btnTurbo.Text = dryRun ? "预演中…" : "加速中…";
            _log.Append("──── " + DateTime.Now.ToString("HH:mm:ss") + (dryRun ? "（预演）" : "") + " ────", Theme.Info);

            BoosterOptions opt = preset ?? CollectOptions();
            _lastDryRun = dryRun;
            var whitelist = new List<string>(Host.Cfg.BoosterWhitelist);

            var worker = new Thread(delegate()
            {
                TurboReport report = null;
                try
                {
                    report = BoosterOps.RunTurbo(opt, whitelist, delegate(string line) { Post(line); }, dryRun);
                }
                catch (Exception ex)
                {
                    Post("Turbo 异常：" + ex.GetType().Name + ": " + ex.Message, Theme.Danger);
                    Log.Error("Turbo 异常", ex);
                }
                TurboReport captured = report;
                try
                {
                    BeginInvoke(new Action(delegate { OnTurboDone(captured); }));
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Name = "hz-turbo";
            worker.Start();
        }

        private void Post(string line)
        {
            Post(line, Theme.TextDim);
        }

        private void Post(string line, Color color)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<string, Color>(Post), line, color); return; }
                _log.Append(line, color);
            }
            catch { }
        }

        private void OnTurboDone(TurboReport report)
        {
            _busy = false;
            _btnTurbo.Enabled = true;
            _btnTurbo.Text = "一键 Turbo";
            Host.Cfg.BoosterLastRun = DateTime.Now.ToString("HH:mm:ss");
            if (report != null && report.PowerChanged)
            {
                Host.Cfg.BoosterPreviousPowerPlan = report.PreviousPowerPlan;
            }
            Host.Cfg.Save();

            if (report == null)
            {
                Host.SetStatus("Turbo 未完成，见日志");
                return;
            }
            string summary = _lastDryRun ? "预演完成" : "Turbo 完成";
            if (report.Kill != null)
            {
                summary += _lastDryRun
                    ? " · 预演命中 " + report.Kill.Killed + " 个进程（未结束）"
                    : " · 结束 " + report.Kill.Killed + " 个进程";
            }
            if (report.Trim != null) summary += " · 内存 " + (report.Trim.FreedMb >= 0 ? "+" : "") + report.Trim.FreedMb.ToString("0.0") + " MB";
            if (report.Clean != null) summary += " · 临时文件 " + report.Clean.FreedMb.ToString("0.0") + " MB";
            if (report.PowerChanged) summary += " · 已切高性能";
            if (report.DnsFlushed) summary += " · DNS 已刷新";
            Host.SetStatus(summary);
            Host.Notify(AppInfo.Name, summary);
            RefreshStatus();
            RefreshProcessPreview();
        }

        private void RestorePowerPlan()
        {
            string error;
            if (BoosterOps.RestorePowerPlan(Host.Cfg.BoosterPreviousPowerPlan, out error))
            {
                Host.Log("已恢复电源计划：" + Host.Cfg.BoosterPreviousPowerPlan);
                Host.SetStatus("已恢复切换前的电源计划");
                _log.Append("已恢复电源计划：" + Host.Cfg.BoosterPreviousPowerPlan, Theme.Ok);
                Host.Cfg.BoosterPreviousPowerPlan = "";
                Host.Cfg.Save();
            }
            else
            {
                Host.Log("恢复电源计划失败：" + error);
                Host.SetStatus("恢复电源计划失败：" + error);
                _log.Append("恢复电源计划失败：" + error, Theme.Danger);
            }
        }

        private void CopyLog()
        {
            try
            {
                Clipboard.SetText(_log.AllText());
                Host.SetStatus("日志已复制到剪贴板");
            }
            catch (Exception ex)
            {
                Host.SetStatus("复制失败：" + ex.Message);
            }
        }

        private void OpenLogFile()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppConfig.LogPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Host.SetStatus("打开日志失败：" + ex.Message);
            }
        }

        // ── 生命周期 ────────────────────────────────────────────

        public override void OnEnter()
        {
            _live = true;
            UpdateCategoryButtons();
            UpdateOptionButtons();
            RefreshStatus();
            RefreshProcessPreview();
            _statusTimer.Start();
        }

        public override void OnLeave()
        {
            // 离开本页就停表：状态刷新是唯一会持续吃 CPU 的东西，隐藏时必须停
            _live = false;
            _statusTimer.Stop();
        }

        public override void OnAppExit()
        {
            _live = false;
            _statusTimer.Stop();
        }
    }
}
