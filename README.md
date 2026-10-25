# HEAOZIE · 系统工具箱（HZ.exe）

把三个各自独立的 Python/tkinter 小工具重构成**一个**原生 Windows 程序：
**没有控制台窗口、零运行时依赖、单文件、托盘常驻**。

| 原来的形态 | 现在 |
| --- | --- |
| `gaming_booster\run_booster.bat` → cmd → Python → tkinter | HZ.exe「游戏加速」页（**已完成**） |
| `定时休眠\定时睡眠.bat` → cmd → Python → tkinter | HZ.exe「定时电源」页（**已完成**） |
| `定时关闭应用\定时关闭应用.bat` → cmd → Python → tkinter | HZ.exe「定时关闭应用」页（**已完成**） |
| 三个工具 = 三个 Python 进程 + 三份重复主题代码 | 一个进程，托盘常驻，关窗不退出 |

## 为什么重构（不是审美问题）

1. **原版在本机跑不起来两个**：`app_killer.py` 与 `gaming_booster.py` 都 `import psutil`，
   而本机 3.12 / 3.10 / 3.7 三个解释器**都没装 psutil** → 实测直接
   `ModuleNotFoundError: No module named 'psutil'`。只有 `sleep_timer.py` 能跑。
2. **每次都要开 cmd**：`.bat` 里 `start /b py -3 xxx.py` 必然先弹一个控制台窗口。
3. **资源**：Python + tkinter 空窗口实测工作集 35.8 MB，三个工具同时开就是三份解释器。

## 构建

```bat
build.cmd              :: 产物 HZ.exe（默认），也可 build.cmd HZ.new.exe
selftest.cmd           :: 无界面自检，结果 logs\selftest.txt，退出码 0 = 通过
```

编译器用 VS2022 自带的 Roslyn `csc.exe`（`tools\_csc.cmd` 自动探测），
目标 .NET Framework 4.8（Windows 自带），**不需要 .NET SDK，不需要任何安装**。

## 用法

```bat
HZ.exe                        :: 启动主界面（重复启动会唤起已运行的实例）
HZ.exe --tab=power            :: 直接打开指定页：booster|power|killer|settings（或 0-3）
HZ.exe --minimized            :: 启动后直接进托盘
HZ.exe --selftest             :: 无界面自检
HZ.exe --exit-after=5         :: 启动 5 秒后自动退出（冒烟测试）
HZ.exe --power-demo=5         :: 演示定时电源全流程（不真的执行电源操作）
HZ.exe --killer-demo=5        :: 演示定时关闭应用全流程（不真的结束进程）
HZ.exe --booster-demo         :: 演示游戏加速的预演 Turbo（不真的结束进程/清内存/清文件）
```

快捷键：`Ctrl+1..4` 切页。关窗 = 进托盘（定时任务继续跑），退出用托盘右键菜单。

## 目录结构

```
build.cmd / selftest.cmd     构建与自检入口
tools\_csc.cmd               编译器探测 + 参考集单一真源
src\                         C# 源码
  Program.cs                 入口：单实例、参数、异常兜底、提权重启
  MainForm.cs                主窗口：信息条 + 分段 Tab + 懒加载内容区 + 状态栏
  SoftUi.cs / Theme.cs       自绘设计系统（深色三层底色、胶囊、GDI+ 矢量图标）
  Json.cs / AppConfig.cs     极简 JSON 与绿色便携配置
  Log.cs / SysEnv.cs         日志与运行环境探针
  Tray.cs                    托盘 + 自绘轻量菜单（避开 ToolStrip 的约 4.6 MB 开销）
  SelfTest.cs                无界面自检
  Panels\                    四个功能页
config\hz.json               运行期生成（不入库）
logs\                        运行期生成（不入库）
legacy\                      Python 原件留档（已迁入，不再作为入口；见 legacy\README.md）
2048\                        纯前端小游戏，保持独立（不并入本应用）
```

## 仓库里还有什么

| 目录 | 内容 |
| --- | --- |
| `src/` `tools/` | 上面这套 HZ.exe 工具箱（C# / .NET Framework 4.8，单文件、零依赖、托盘常驻） |
| `2048/` | 原版 2048 网页（纯前端，未改动，独立） |
| `2048-agent/` | 让大模型玩 2048 的实验台 + **自研 bitboard 期望最大引擎**（见下） |
| `legacy/` | 被 HZ.exe 取代的 Python 原件留档 |

### 2048 AI（`2048-agent/`）

![2048 demo](2048-agent/demos/cover.png)

自研引擎 `2048-agent/nn2048/engine_bb.cpp`：棋盘压成一个 `uint64_t`（16 格 × 4 bit 存 log2）、
65536 项行移动表、转置复用、置换表、**概率截断**（累计 spawn 概率 < 1e-4 的分支静态评估）、迭代加深。
单核 **13–18 M 节点/s**，100 ms/步的实际深度 6.0。

| 指标 | 旧数组引擎 | 现在 |
| --- | ---: | ---: |
| 18 局均分 | 37.6k | **231,928** |
| 最高 | 60.8k（4096） | **387,408（16384）** |
| 16384 率 | 0 | **39%** |
| 8192 及以上 | 50% | **94%** |

- 浓缩回放（4:22，每帧 0.5 s）：[`2048-agent/demos/demo_condensed.mp4`](2048-agent/demos/demo_condensed.mp4)
- 交互回放（单页自包含）：[`2048-agent/demos/demo.html`](2048-agent/demos/demo.html)
- 数据、两个负结果与实验纪律：[`2048-agent/nn2048/RESULTS.md`](2048-agent/nn2048/RESULTS.md)、[`NIGHT_REPORT.md`](2048-agent/nn2048/NIGHT_REPORT.md)

## 设计取舍（为什么这么做）

- **WinForms + GDI+ 自绘，而不是 WPF**：本机两个同类应用（`launch-center` 的设计系统 v3、
  `pc-keeper`）都是 WinForms 自绘且已验收；WPF 那条只是没走通的实验路线。省内存、少依赖、有先例。
- **手写 csc 编译，不用 .csproj**：本机没有 .NET SDK；WinForms 属于 .NET Framework 系统组件，
  引用运行时程序集即可，产物 58.5 KB 且零安装。
- **托盘菜单自绘**：`ContextMenuStrip`/`ToolStrip` 实测约值 4.6 MB 工作集，而这只是十几行文字。
- **关窗进托盘**：定时类工具一旦进程退出，定时器就不存在了 —— 关窗必须不退出。
- **绝对时间戳倒计时**（M1）：原版递减计数在机器睡眠后会漂移，设定 30 分钟、合盖 2 小时后
  醒来还得再等 30 分钟。

## 里程碑

- [x] **M0 骨架**：构建链、单实例、托盘、四页外壳、设置页、自检
- [x] **M1 定时电源**：睡眠/关机/重启，绝对时间戳倒计时（睡眠唤醒不漂移），执行前 5 秒可取消
- [x] **M2 定时关闭应用**：进程勾选、点选目标、到点结束进程
- [x] **M3 游戏加速**：实时状态、Turbo 一键、分类与白名单、日志、提权
- [x] **M4 收尾**：图标、快捷方式脚本、toolbox 入口改指 HZ.exe、Python 原件迁入 `legacy/`

**当前版本 1.0.0**（M0–M4 完成）。已知尚未在真机上实测的三件事（需人工各点一次，开发流程不擅自触发）：
真实的睡眠/关机/重启、点选目标时的真实鼠标点选、真实的一键 Turbo。

## 安装快捷方式

```powershell
pwsh -NoProfile -File tools\install-shortcut.ps1          # 桌面 + 开始菜单
pwsh -NoProfile -File tools\install-shortcut.ps1 -Remove  # 卸载
```

## 许可

见 [LICENSE](LICENSE)（Sustainable Use License）。原 Python 工具同样出自本仓库。
