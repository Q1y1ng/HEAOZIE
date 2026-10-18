# 变更记录

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
