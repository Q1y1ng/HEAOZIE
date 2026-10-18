# 变更记录

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格。
每个改动在同一轮内补写，并与代码进同一个 commit。

## [未发布]

### 新增

- **定时电源页（M1）完整实现** —— 对照原 `sleep_timer.py`（443 行 tkinter）：
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
- `--power-demo=N`：冒烟测试开关，预填 N 秒并起倒计时，**走完「倒计时 → 撤销窗口 → 执行」全流程
  但不真的执行电源操作**。
- 自检新增两项：`倒计时（含睡眠漂移）`、`电源操作映射`（共 10 项）。
- `tools\shot.ps1`：窗口截图工具（`-Print` 用 PrintWindow 抓窗口自身像素，被遮挡也能拍），
  用于开发期验证自绘 UI。
- **HZ.exe：原生 Windows 单体应用（M0 骨架）** —— 把三个 Python/tkinter 小工具
  （游戏性能加速器、定时休眠、定时关闭应用）重构为**一个** C# / .NET Framework 4.8 程序：
  - `build.cmd` 一键构建，产物 **58.5 KB 单文件**，`-target:winexe` → **没有控制台窗口**；
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
  - `--tab=booster|power|killer|settings`、`--minimized`、`--exit-after=N` 命令行参数，
    便于托盘/工具箱/冒烟测试调用；
  - 设置页已完整可用：运行环境、行为开关（关窗进托盘、隐藏时修剪工作集）、
    维护动作（打开配置/日志目录、查看/清空日志、复制诊断信息、一键自检）。
- `tools\_csc.cmd`：Roslyn 编译器探测 + 参考集单一真源（沿用 launch-center / pc-keeper 的成熟做法，
  本机无 .NET SDK 也能构建）；`selftest.cmd` 自检入口；`src\app.manifest`（PerMonitorV2 高 DPI、
  asInvoker 不提权、longPathAware）。

### 变更

- 版本号 `0.1.0 → 0.2.0`，构建标记 `M0 骨架 → M1 定时电源`。
- 面板契约 `IHzHost` 增加 `SetTrayStatus` / `SetTrayColor` / `Notify`（托盘提示、状态色、气泡）。
- 仓库新增 `.gitignore`（构建产物、`config/`、`logs/`、`__pycache__/` 不入库）。

### 实测数据（2026-10-18，本机）

| 状态 | 工作集 |
| --- | --- |
| HZ.exe 托盘常驻（隐藏并修剪工作集） | **9.2 MB** |
| HZ.exe 主界面打开 | 45–50 MB |
| 对照：Python 3.12 + tkinter 空窗口 | 35.8 MB |
| 对照：launch-center 实测 Active | 43.5 MB（其自身报告） |

- M0：`--selftest` 8 项全绿；重复启动被单实例拦下并唤起了已有窗口。
- M1：`--power-demo=6` 冒烟实测（日志原文）—— 起倒计时 6 秒 → 到点进入 5 秒撤销窗口 →
  演示模式跳过执行 → 全流程无异常，退出码 0；自检 10 项全绿。
- ⚠ 真实睡眠/关机/重启**尚未在真机上执行过**（不该由开发流程擅自触发），需要人工点一次验证。

### 待办

- M2 定时关闭应用（进程勾选、点选目标、到点结束进程）
- M3 游戏加速（实时状态、Turbo 一键、分类与白名单、日志面板、提权）
- M4 收尾（图标、快捷方式脚本、toolbox 入口改指 HZ.exe、Python 原件移入 `legacy/`）
