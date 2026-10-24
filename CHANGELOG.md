# 变更记录

## [未发布]

### 变更（2048 nn2048 · 夜间实测结论与演示产物换新，2026-10-25 05:40）

- **100 ms/步 18 局（batch A+G）**：均分 **231,928**、16384 **7/18（39%）**、8192 及以上 **17/18（94%）**、
  最高 **387,408**；分布双峰（8192 段 167k–179k，16384 段 288k–387k）；
- **5×5 基线**（通用数组引擎，30 ms/步，深度仅 3.2）：**4/5 上 16384、均分 341,148**——
  棋盘越大越宽容，5×5 的上限远高于 4×4，值得补一个 `__uint128_t` bitboard；
- 两个负结果：①搜索里忽略 4 号块（`--search-prob4 0`）深度 +0.7 层但均分腰斩（126k→64k）；
  ②"空格权重 400 让均分 +1.9 倍"是假阳性——换种子复验后差异消失（235k vs 223k），
  由此确定实验纪律：**≥20 局/臂 + 换种子复验**；
- 时间预算结论：100 ms × 8 并发最划算；200/400 ms 的收益被并发吃掉（8 并发 15–18 M 节点/s，12 并发 11–12 M）；
- 演示产物换成**最高存盘局**（seed 85 · 342,816 分 · 16384 · 13,014 步）：
  `runs/batchG/demo_condensed.mp4`（4:22，25 步/帧）+ `demo.html`，旧 339k 版已删（只保留一个视频）；
- 新增 `nn2048/NIGHT_REPORT.md`：夜间工作报告（时间线、引擎结构、全部实测数据、三个教训、下一步）。

### 修复（2048 nn2048 · demo 页白屏，2026-10-25）

- `build_engine_demo.py` 的副标题误用了 `steps[i].score`（紧凑格式里存的是 `s`），
  页面加载即抛 `TypeError` 白屏——不放浏览器的冒烟测试抓不到，补上才发现；
- 新增 `nn2048/demo_smoke.js`：用 Node 的 `vm` + 极简 DOM 桩跑一遍页面脚本，
  在首帧/1/3/1/2/末帧四处置位后校验「格子文字 → 最大块」「分数文字」「步数文字」与数据一致；
  现在 `OK: 12812 steps, final 339108, max tile 16384`；
- 该测试已成为 demo 产物的验收步骤（改模板后先跑它，再出片）。

### 新增（2048 nn2048 · 引擎对局的 demo 与浓缩视频，2026-10-25）

- 新增 `nn2048/render_video.py`：把 `engine_bb --save` 的逐步 JSON 直接渲染成 mp4
  （Pillow 直出帧 → ffmpeg 管道，无浏览器/无 Playwright）。`--fps 2` = 每帧 0.5 s；
  `--stride N` = 每 N 步一帧；`--target-seconds T` 自动算步长把片长压到 T 秒；
  画面含棋盘、分数、最大块、步数、本步得分、搜索深度、方向、全程分数曲线与进度条；
- 新增 `nn2048/build_engine_demo.py`：把同一份 JSON 生成为**自包含单页回放**
  （拖时间轴、0.1/0.25/0.5/1/2 s 定速、1024→32768 里程碑跳转、快捷键、分数曲线跟随）；
- 产物（`runs/`，未入库）：`runs/engine-bb-s4/demo_condensed.mp4`（4:22，25 步/帧）
  + `demo.html`（12,812 步）+ `engine.json`；
- 最优存盘局：**seed 4 · 339,108 分 · 16384 块 · 12,812 步 · 100 ms/步**；
- 说明：同一 seed 重跑分数会大幅波动（seed 1 首跑 387,408 → 重跑 60,788），
  因为搜索是**时间预算**制、节点数随机器负载浮动，同分局面会被打破；
  所以"最高分"必须当场落盘。

### 新增（2048 nn2048 · bitboard 期望最大引擎，2026-10-25）

- 新增 `nn2048/engine_bb.cpp`：4×4 专用 **bitboard** 引擎——棋盘压成一个 `uint64_t`（16 格 × 4 bit 存 log2），
  65536 项行移动表（左/右）、转置复用（一次决策 3 次转置）、行查表评估（nneonneo 口径：
  行内 `sum^3.5` / 单调 `^4` / 合并段 / 空格，权重 270-47-11-700）、2^22 槽直映射置换表（带深度）、
  **概率截断**（累计 spawn 概率 < 1e-4 的分支静态评估，实现自适应深度）+ 迭代加深与时间预算；
- 实测（8 局 × 100 ms/步，全核 i9-12900H）：**均分 238,692**、最高 **387,408（16384）**、
  16384 4/8、8192 7/8、4096 8/8；速度 13–18 M 节点/s，实际深度 5.8–6.2。
  对照旧数组引擎（同预算）：均分 37.6k、最高 60.8k、深度 4.2–4.6——**均分 6.3 倍**；
- 新增 `--selftest`：用独立的逐格实现（转置/四方向移动/得分/评估）对拍 20 万随机局面，
  表构建或搜索语义一改就报错，防止"测速很快但算错"；
- 新增 `--bench-moves N`：只测速度与深度（不走完整局）；`--trace-every N` 打印逐步进度；
- `bench.py` 支持 `--exe`（默认仍是 `engine4.exe`，可指 `engine_bb.exe`）；
- 修正：Windows 会节流后台进程（隐藏控制台），引擎启动时调 `SetProcessInformation(ProcessPowerThrottling)`
  退出 EcoQoS，避免夜间批次白跑一半算力；
- 结论更替：`RESULTS.md` 重写——"手工特征天花板 60k、必须上学习型评估"的旧结论作废（那是**口径**问题，
  不是特征问题）；学习型评估降级为下一阶段选项。

### 新增（2048 Agent · 本地/云端大模型逐步对局，2026-10-24）

- 新增独立副本 `2048-agent/`：复制 `2048/index.html`，只加 `?seed=`（mulberry32）固定随机流，**原版零改动**；
- `play.py`：Playwright 驱动副本，逐步记录盘面 / 候选 / 思考原文 / `MOVE:` / token / 耗时到 `runs/<id>/run.jsonl`；
- `llm.py`：OpenAI 兼容流式客户端，同时接本地 llama.cpp（8096）与 OpenCode Go 免费档 `beam-free`；
- `viewer.html` + `index.html` + `serve.py`：逐步直播/回放（含模型思考、候选盘面、分数）；
  `build_replay.py` 生成可双击打开的自包含 replay；
- 结果（同 seed 1）：4B 纯棋盘 108 步 / 780 分 / 最大 64；4B 候选辅助 214 步 / 2148 分 / 最大 128（2.75×）；
  beam-free 有效 81 步 / 840 分 / 最大 128（TUN 断流截断）；
- 已知：`beam-free` 地区限制需新加坡 TUN；`longcat-2.0` 非免费；Cloudflare 1010 需浏览器 UA，
  OpenCode Go 需 `x-opencode-session`；
- 测试：副本 Node 逻辑 53 项 + 结构检查全过；viewer 108 步回放零 JS 错误。

### 新增（2048 Agent · CS2 demo 式回放与报告，2026-10-24）

- `build_demo.py`：一个 run 目录产出四件套——`demo.html`（播放/暂停/倍速/拖时间轴/关键时刻书签，
  按播放进度逐字回放该步完整思考，「决策」页签对比四个方向的得分/空格/存活与贪心建议）、
  `demo.json`（demo 文件本体，可下载离线重放）、`report.html` + `REPORT.md`（结果卡片、配置、
  过程数据、得分曲线、每步耗时图、与其他策略对比、关键节点、阶段走势、思考样本、完整走子表）；
- `llm.py` 接入 `reasoning_effort`（网关参数）；`play.py` 新增 `--reasoning-effort`、`--think-brief N`；
  `viewer.html` 增加 DeepSeek 模型标签；目录页每张卡加「Demo 回放 / 完整报告」入口；
- 播放器新增**定速播放**（0.5s/步、1s/步、2s/步，快捷键 `C`）：按固定节奏自动逐步播放，
  不必手动点“下一步”；再点一次回到按真实耗时/思考长度的自然节奏；
- `play.py` 新增 `--resume <run-dir>`：重放已记录走子（逐格校验）后追加续写，用于进程被中断后的收尾；
- DeepSeek V4.1 Flash (high) 纯棋盘首局：100 步 / **980 分 / 最大 128**（进程被外部中断），
  平均每步思考 2.3 万字符（最长 9.7 万）、与贪心一致率 31%；不设限局 `ds-flash-unlimited-s1` 在跑；
- 新增 `NN_PLAN.md`：2048 专用神经网络方案（三条路线对比、推荐"价值网 + 前瞻"、工期与验收），
  附 `nn2048/bench_env.py` 位棋盘模拟器——实测 39 万 moves/s、随机策略 420 局/s、均分 1148（与基线一致）；
- 新增 `nn2048/engine.cpp`（通用 N×N 的 expectimax 引擎，启发式权重可命令行扫参）：
  4×4 三局全部达到 2048（32.6k / 33.0k / 36.4k），比 Python 版 13.6k 高 2.5×；
- 新增 `nn2048/cpu_cap.ps1`：用 Job Object 把进程树限在指定 CPU 百分比（默认 40%），
  引擎单线程约占 1 核（本机 ≈5%）；本机 Windows 拒绝 HARD_CAP，退化为 rate-control 平均值上限；
- 新增 `nn2048/RESULTS.md` + `sweep.py`：4×4 权重扫描（7 组 × 4 seeds）——`--w-empty 200 --w-merges 900`
  均分 31.4k、最高 **51,420（4096）**；同批 base 20.0k。结论：只调参天花板 60k–80k，突破需学习型评估；
- 已知坑：网关 `base_url` 不能带 `/v1`（客户端会再拼一层，404）；提示词压不住 `reasoning_content` 长度。

### 变更（M5 · 界面重做为 Windows 11 Fluent）

> 起因：v1 的观感是"深色 Web 仪表盘"，与 Win11 原生应用不是一回事。本轮把整个外观层重做，
> **逻辑层一行未动**（倒计时、进程表、Turbo 编排、安全名单、白名单、配置格式全部照旧）。

- **左侧 NavigationView** 取代顶部胶囊 Tab（图标 + 文字，选中项带强调色竖条）；
- **自绘标题栏**：应用图标 + 名称 + 最小化/最大化/关闭，拖动/双击最大化/八向缩放
  全部交给系统（`WM_NCHITTEST` 返回 `HTCAPTION` / `HT*` / `HTMINBUTTON` / `HTMAXBUTTON` / `HTCLOSE`），
  **最大化按钮悬停会出 Win11 Snap 布局**；
- **Mica 背景**（`DWMWA_SYSTEMBACKDROP_TYPE` + 客户区 alpha 透出）+ 圆角 + 暗色标题栏；
  拿不到 Mica 时自动退回 Win11 实色底 `#202020`（设置页会如实显示"Mica 已启用/未启用"）；
- **跟随系统深浅色**（读 `AppsUseLightTheme`），设置页可固定深色/浅色，切换即时重绘；
- **系统强调色**（`AccentColor` → `DwmGetColorizationColor` → 默认蓝）；深色下强调色按钮用黑字（Win11 行为）；
- **字体换成 Win11 系统字体**：`Segoe UI Variable Text/Display` + `Segoe Fluent Icons`（图标不再手画 GDI+）；
- **新控件库 `src\Fluent.cs`**：FluentCard（8px 圆角、半透明底 + 1px 描边）、FluentButton（标准/强调/次要/危险）、
  **FluentToggleSwitch**（Win11 开关）、FluentNavView、FluentScrollPanel（自绘细滚动条）、FluentText、FluentStatusBar；
- **所有文字改用 GDI+ 绘制**（`Fluent.Text`）：WinForms 的 `Label`/`TextRenderer` 走 GDI，会把文字像素的 alpha
  写成 0 → 在 Mica 上**文字直接消失**。这是本轮最关键的一处技术决定，`src\Fluent.cs` 顶部有注释说明；
- 删除 `src\SoftUi.cs`（v1 的自绘控件库），由 `Fluent.cs` 取代。

### 修复（M5 期间发现）

- 自绘控件在 `SetStyle(SupportsTransparentBackColor)` **之前**赋 `BackColor = Color.Transparent` 会抛
  "控件不支持透明的背景色"（Form 与 UserControl 同理）——已统一为先 SetStyle 再赋色；
- `Theme` 里的图标字体字段不能叫 `Icon`：会遮蔽 `System.Drawing.Icon`，导致 `Icon.FromHandle` 编译不过（改名 `IconFont`）；
- 冒烟测试（`--exit-after`）期间不再弹异常对话框：弹窗会挂住进程、阻塞自动化验证（`_quiet`）。

### 已知未完成（诚实记录）

- 「定时关闭应用」页的截图已抓取但**尚未逐帧核对**；「游戏加速」页仍有细节待打磨
  （左侧列在 720 高度下略挤、状态卡只显示 5 行时磁盘会合并成一行汇总）；
- 真机未实测项同 1.0.0（真实睡眠/关机/重启、真实点选、真实 Turbo）。
本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格。
每个改动在同一轮内补写，并与代码进同一个 commit。

## [1.0.0] - 2026-10-18

三个 Python/tkinter 小工具 → **一个原生单文件应用**的首次完整发布（M0–M4 全部完成）。

### 新增（M4 收尾）

- **应用图标** `assets\hz.ico`：由 `tools\make-icon.ps1` 生成（16/32/48/256 四档 PNG 压缩 ICO，
  与 `Theme.AppIcon` 同一套观感），构建时内嵌进 exe → 任务栏 / 快捷方式 / Alt-Tab 都有身份。
- `tools\install-shortcut.ps1`：一键装桌面 + 开始菜单快捷方式（`-Remove` 卸载）。
  顺带替掉了原来那条 **指向不存在路径**（`E:\AI\gaming_booster\`）的 `HEAOZIE加速器.lnk`。
- **Python 原件迁入 `legacy/`**（含原 `.bat` 与一份 `legacy/README.md` 说明为什么不再用、原来有哪些缺陷），
  同时从 git 跟踪中移除 4 个 `__pycache__/*.pyc` 与那条失效快捷方式。
- `E:\AI\toolbox` 的三条 HEAOZIE 入口改指 `HZ.exe` 并带 `--tab=` 参数，另加一条「HEAOZIE 系统工具箱」总入口。
- `E:\AI\tools.md` 新增 §4.8 条目（位置 / 形态 / 参数 / 实测 / 原版缺陷）。

### 变更

- 版本号 `0.4.0 → 1.0.0`：M0 骨架 → M1 定时电源 → M2 定时关闭应用 → M3 游戏加速 → M4 收尾。

### 新增（M3 游戏加速）

- **游戏加速页完整实现** —— 对照原 `gaming_booster.py`（860 行 GUI + 786 行核心）：
  - **实时状态**（2 秒刷新）：CPU 占用、内存、GPU（`nvidia-smi`：占用/显存/温度）、
    磁盘（逐块显示，行数不够时合成一行汇总）、CPU 温度（WMI）；
  - **一键 Turbo** 在**后台线程**执行，界面不卡死，每一步结果实时进日志：
    结束后台进程 → 整理内存 → 清理临时文件 → 高性能电源计划 → 刷新 DNS；
  - 八个分类开关（浏览器/聊天/下载/音乐/办公/云同步/开发/游戏启动器），
    名单取自原版 `config.json`；开发与游戏启动器默认关闭（误杀风险最高）；
  - 分类进程预览表：勾选框 = 本次会被结束，**取消勾选即把该进程名加入白名单**（以后也不结束）；
  - 白名单编辑器（每行一个进程名）、恢复电源计划、以管理员重启入口；
  - 日志面板（自绘、按级别着色、可复制/清空/打开日志文件）。
- **内存整理用 memclean 的系统级做法**（`src\Sys\MemTrim.cs`）：启用
  `SeProfileSingleProcessPrivilege` 后调 `NtSetSystemInformation(SystemMemoryListInformation,
  MemoryEmptyWorkingSets)`（等同 RAMMap 的 Empty Working Sets），非管理员则回退逐进程空工作集。
  **原版那段"清理 Standby Memory"是两条无效的 PowerShell 咒语（ClearLog / ReleaseComObject），已丢弃。**
- **临时文件清理**（`src\Sys\Cleanup.cs`）：白名单目录（%TEMP%、Windows Temp、Prefetch、
  DirectX 着色器缓存、Edge/Chrome 缓存），**显式检查 `FileAttributes.ReparsePoint`**：
  遇到联接/符号链接只摘链接本身，一步都不进目标（本机曾因递归删除跟随联接被清空整盘）。
- **电源计划恢复改为恢复"切换前"的计划**（原版写死 Balanced GUID）。
- `--booster-demo`：冒烟开关，走完整编排与日志，但后台进程只**预演**、内存/清理/电源/DNS 全部跳过。
- 自检新增四项：`加速器名单与编排`、`清理白名单与安全闸门`、`系统状态采集`（共 15 项）。
  其中 `KillBackground` 增加 `dryRun` 参数，自检只走预演路径 —— **自检绝不能真的结束进程**。

### 修复

- **卡片高度不够导致底部按钮被裁掉**（设置页「运行环境 / 行为」、加速页状态卡与分类条）：
  高度现在按「标题留白 58 + 内容 + 按钮行 42 + 下留白 16」算，不再凭感觉给数字；
  这类问题只有截图能发现（编译与自检都发现不了），已全部逐页核对。
- 卡片内的容器**重复叠加了标题留白**（卡片 Padding 已留 58px，容器又加了一次），
  导致状态区只画出两行、分类按钮被挤出可视区；同时修正了 Fill/Top 子控件的添加顺序
  （Fill 必须最先加，否则会被后加的 Top/Bottom 压在下面）。
- 加速页的 GPU 行与温度行会随查询轮次在"有值 / 不可用"之间跳动；现在未查询的那轮沿用上次结果。
- 面板在主窗口构造期创建时，工作线程的 `BeginInvoke` 会因句柄未建好而抛异常（演示路径踩到）；
  演示改为延迟 400ms 启动，且 `IHzHost` 的状态/托盘/气泡回调对 BeginInvoke 失败做了容错。

### 新增（M2 定时关闭应用）

- **定时关闭应用页完整实现** —— 对照原 `app_killer.py`（724 行 tkinter）：
  - 左右分栏布局：左列时间与倒计时，右列进程表吃满剩余高度（一页装下，不出现整页滚动）；
  - **自绘进程表**（`ProcTable.cs`）：勾选框 / 进程名 / PID / 内存，悬停与选中高亮、
    自绘 4px 滚动指示条（原生 `VScrollBar` 在深色主题下是浅灰一条，很扎眼）、
    鼠标滚轮与 ↑↓/空格 键盘操作；
  - 点选目标：点「点击选择」后在屏幕上点任意窗口即选中其进程（照原版
    `GetAsyncKeyState` + `WindowFromPoint` + `GetWindowThreadProcessId` 的做法），
    支持「点选时自动最小化」、ESC 取消；先等按钮松开再抓下一次按下，避免抓到自己这次点击；
  - 安全边界：系统关键进程（explorer / dwm / svchost / lsass / csrss / winlogon …）
    **不进列表也不能通过点选加入**，点选命中时给出明确提示；
  - 开始前**二次确认**目标清单（到点执行时无人值守，不再弹窗阻塞）；
  - 到点前 5 秒撤销窗口；结束后逐条记录失败原因（原版只报一个「N 个失败」）；
  - 勾选目标按进程名持久化，下次启动自动恢复。
- `--killer-demo=N`：冒烟测试开关，走完「倒计时 → 撤销窗口 → 执行」全流程但不真的结束进程。
- 自检新增两项：`进程枚举与安全名单`、`进程表与时间卡`（共 12 项）。
- 面板布局助手：`PanelBase.BuildSplitLayout` / `BuildColumn`（左右分栏）、可折行按钮行。

### 修复

- **窗口每启动一次就长大一圈**：`SavePlacement` 存的是 `Width/Height`（含边框与标题栏），
  恢复时却赋给 `ClientSize`，边框被重复计入。现在两端都用 `ClientSize`，并把尺寸钳到当前虚拟桌面内
  （换显示器后配置里的旧尺寸不会把窗口开到屏幕外）。
- **勾选一个进程，到点关了七个**：按进程名恢复勾选时会把同名进程（如 `steamwebhelper.exe`
  的多个实例）全部勾上；现在每个名字只恢复一个实例。
- 进程表在卡片被压缩时高度会算成负数、整张表不可见；改为分栏布局后由列高决定。
- 切回本页会重新枚举并覆盖用户已勾选的目标；现在只在首次进入时枚举（要新数据点「刷新」）。
- `TimeSetupCard.Refresh()` 与 `Control.Refresh()` 重名（会隐藏基类方法），改名 `RefreshDisplay()`。

### 新增（M1 定时电源）

- **定时电源页完整实现** —— 对照原 `sleep_timer.py`（443 行 tkinter）：
  - 时/分/秒微调（自绘 ± 按钮，不是系统 `NumericUpDown`）、预设 15 分钟 / 30 分钟 / 1 小时 / 2 小时；
  - 睡眠 / 关机 / 重启三选一（分段切换条），选择持久化到配置；
  - 大号倒计时（Consolas 40pt）、开始 / 暂停 / 继续 / 重置、目标时刻提示；
  - **执行前 N 秒撤销窗口**（默认 5 秒，可配置）：大字变红、出现「立即取消」、托盘气泡提醒；
  - 托盘常驻显示剩余时间，托盘图标带状态点（绿=空闲 / 黄=计时中 / 红=即将执行）；
  - 每次状态变化写日志（开始、暂停、到点、取消、执行、失败）。
- **倒计时引擎改为「绝对目标时刻」**（`src\Sys\Countdown.cs`），修掉原版递减计数在
  系统睡眠/休眠后漂移的缺陷：设定 30 分钟、合盖睡 2 小时，唤醒后目标时刻已过 → 立即执行，
  而不是再等 30 分钟。自检用可注入时钟模拟「睡眠 3 小时」验证了这条。
- `src\Sys\PowerOps.cs`：睡眠（`powrprof.SetSuspendState`，参数与原版一致）/ 关机 / 重启
  （`shutdown.exe`，`CreateNoWindow` 不闪控制台），以及 `/a` 兜底撤销。
- `--power-demo=N`：冒烟测试开关，走完全流程但不真的执行电源操作。
- `tools\shot.ps1`：窗口截图工具（`-Print` 用 PrintWindow 抓窗口自身像素，被遮挡也能拍）。

### 新增（M0 骨架）

- **HZ.exe：原生 Windows 单体应用** —— 把三个 Python/tkinter 小工具
  （游戏性能加速器、定时休眠、定时关闭应用）重构为**一个** C# / .NET Framework 4.8 程序：
  - `build.cmd` 一键构建，产物 **约 60 KB 单文件**，`-target:winexe` → **没有控制台窗口**；
  - WinForms 是系统组件，**零运行时依赖、零安装**；Python 与 psutil 不再是前置条件
    （原版在未装 psutil 的机器上 `app_killer.py` / `gaming_booster.py` 直接 `ModuleNotFoundError`）；
  - 单实例：重复启动会**唤起已运行的实例**并立刻退出（注册窗口消息广播，不用命名管道）；
  - **托盘常驻**：关窗不退出，定时任务继续运行；托盘左键显示/隐藏、右键自绘菜单（退出等）；
  - 四页外壳（游戏加速｜定时电源｜定时关闭应用｜设置），页面**懒加载**，未进入的页面不建控件；
  - 自绘设计系统 `SoftUi.cs`：深色三层底色、胶囊状态、GDI+ 矢量图标、`SoftButton`/`SegmentedTabs`/`SoftCard`；
  - 运行时绘制图标（应用图标与托盘状态点都是 GDI+ 画的，不需要 `.ico` 资源文件）；
  - 配置固定在 exe 同级 `config\hz.json`（绿色便携，不写注册表、不写 AppData），坏配置自动改名留证并退回默认值；
  - 日志 `logs\hz.log`（512 KB 自动轮转）+ 全局异常兜底弹窗；
  - `--selftest` 无界面自检，结果写 `logs\selftest.txt`；
  - `--tab=booster|power|killer|settings`、`--minimized`、`--exit-after=N` 命令行参数；
  - 设置页完整可用：运行环境、行为开关（关窗进托盘、隐藏时修剪工作集）、
    维护动作（打开配置/日志目录、查看/清空日志、复制诊断信息、一键自检）。
- `tools\_csc.cmd`：Roslyn 编译器探测 + 参考集单一真源（沿用 launch-center / pc-keeper 的成熟做法，
  本机无 .NET SDK 也能构建）；`selftest.cmd` 自检入口；`src\app.manifest`（PerMonitorV2 高 DPI、
  asInvoker 不提权、longPathAware）。

### 变更

- 版本号 `0.3.0 → 0.4.0`，构建标记 `M2 定时关闭应用 → M3 游戏加速`
  （历史：`0.1.0` = M0 骨架，`0.2.0` = M1 定时电源，`0.3.0` = M2 定时关闭应用）。
- 面板契约 `IHzHost` 增加 `SetTrayStatus` / `SetTrayColor` / `Notify`（托盘提示、状态色、气泡）。
- `SoftCard` 不再 sealed（复合卡片 `TimeSetupCard` 需要继承）。
- 仓库新增 `.gitignore`（构建产物、`config/`、`logs/`、`__pycache__/` 不入库）。

### 实测数据（2026-10-18，本机）

| 状态 | 工作集 |
| --- | --- |
| HZ.exe 托盘常驻（隐藏并修剪工作集） | **9.2 MB** |
| HZ.exe 主界面打开（进程表 198 项） | 41–53 MB |
| 对照：Python 3.12 + tkinter 空窗口 | 35.8 MB |
| 对照：launch-center 实测 Active | 43.5 MB（其自身报告） |

- M0：`--selftest` 全绿；重复启动被单实例拦下并唤起了已有窗口。
- M1：`--power-demo=6` 冒烟实测（日志原文）—— 起倒计时 6 秒 → 到点进入 5 秒撤销窗口 →
  演示模式跳过执行 → 全流程无异常，退出码 0。
- M2：`--killer-demo=5` 冒烟实测 —— 自动勾选 3 个目标 → 倒计时 5 秒 → 5 秒撤销窗口 →
  演示模式跳过结束进程 → 退出码 0。
- M3：`--booster-demo` 冒烟实测 —— 状态区（CPU 1% / 内存 9196-16176 MB / GPU RTX 3060 11% ·
  显存 630/6144 MB · 49 ℃ / 磁盘 5 个合计 1657/3780 GB / CPU 温度如实报"需要管理员"）、
  8 个分类开关、预演 Turbo 全流程日志、退出码 0；自检 15 项全绿。
- ⚠ **尚未在真机上实测的三件事**（不该由开发流程擅自触发，需人工各点一次）：
  真实的睡眠/关机/重启；点选目标时的真实鼠标点选；**真实的一键 Turbo**（会结束进程/清内存/清文件）。

### 待办

- M4 收尾（图标、快捷方式脚本、toolbox 入口改指 HZ.exe、Python 原件移入 `legacy/`、推送到远端）
