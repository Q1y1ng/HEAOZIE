# 变更记录

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格。
每个改动在同一轮内补写，并与代码进同一个 commit。

## [未发布]

### 新增

- **HZ.exe：原生 Windows 单体应用（M0 骨架）** —— 把三个 Python/tkinter 小工具
  （游戏性能加速器、定时休眠、定时关闭应用）重构为**一个** C# / .NET Framework 4.8 程序：
  - `build.cmd` 一键构建，产物 **58.5 KB 单文件**，`-target:winexe` → **没有控制台窗口**；
  - WPF/WinForms 均为系统组件，**零运行时依赖、零安装**；Python 与 psutil 不再是前置条件
    （原版在未装 psutil 的机器上 `app_killer.py` / `gaming_booster.py` 直接 `ModuleNotFoundError`）；
  - 单实例：重复启动会**唤起已运行的实例**并立刻退出（注册窗口消息广播，不用命名管道）；
  - **托盘常驻**：关窗不退出，定时任务继续运行；托盘左键显示/隐藏、右键自绘菜单（退出等）；
  - 四页外壳（游戏加速｜定时电源｜定时关闭应用｜设置），页面**懒加载**，未进入的页面不建控件；
  - 自绘设计系统 `SoftUi.cs`：深色三层底色、胶囊状态、GDI+ 矢量图标、`SoftButton`/`SegmentedTabs`/`SoftCard`；
  - 运行时绘制图标（应用图标与托盘圆点都是 GDI+ 画的，不需要 `.ico` 资源文件）；
  - 配置固定在 exe 同级 `config\hz.json`（绿色便携，不写注册表、不写 AppData），坏配置自动改名留证并退回默认值；
  - 日志 `logs\hz.log`（512 KB 自动轮转）+ 全局异常兜底弹窗；
  - `--selftest` 无界面自检（JSON 往返、坏输入容错、配置默认值与部分覆盖、主题与图标、
    单实例消息、进程枚举、目录可写、四个面板可构建），结果写 `logs\selftest.txt`；
  - `--tab=booster|power|killer|settings`、`--minimized`、`--exit-after=N` 命令行参数，
    便于托盘/工具箱/冒烟测试调用；
  - 设置页已完整可用：运行环境、行为开关（关窗进托盘、隐藏时修剪工作集）、
    维护动作（打开配置/日志目录、查看/清空日志、复制诊断信息、一键自检）。
- `tools\_csc.cmd`：Roslyn 编译器探测 + 参考集单一真源（沿用 launch-center / pc-keeper 的成熟做法，
  本机无 .NET SDK 也能构建）；`selftest.cmd` 自检入口；`src\app.manifest`（PerMonitorV2 高 DPI、
  asInvoker 不提权、longPathAware）。

### 实测数据（2026-10-18，本机）

| 状态 | 工作集 |
| --- | --- |
| HZ.exe 托盘常驻（隐藏并修剪工作集） | **9.2 MB** |
| HZ.exe 主界面打开 | 45 MB |
| 对照：Python 3.12 + tkinter 空窗口 | 35.8 MB |
| 对照：launch-center 实测 Active | 43.5 MB（其自身报告） |

### 变更

- 仓库新增 `.gitignore`（构建产物、`config/`、`logs/`、`__pycache__/` 不入库）。

### 待办

- M1 定时电源（绝对时间戳倒计时，修掉原版递减计数在睡眠后漂移的缺陷）
- M2 定时关闭应用（进程勾选、点选目标、到点结束进程）
- M3 游戏加速（实时状态、Turbo 一键、分类与白名单、日志面板、提权）
- M4 收尾（图标、快捷方式脚本、toolbox 入口改指 HZ.exe、Python 原件移入 `legacy/`）
