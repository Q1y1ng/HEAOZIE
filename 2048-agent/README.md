# 2048 Agent · 本地 4B / 云端免费模型逐步对局

> 位置：`E:\AI\tools\HEAOZIE\2048-agent`（HEAOZIE 仓库内，独立目录）
> 原始游戏：`E:\AI\tools\HEAOZIE\2048` **未改动**（`index.html` SHA256 `B21402556CBDA9B50A6B0CEFB716794DE8BD1BC6EE2290500D25BF3C853334F2`）
> 2026-10-24

## 这是什么

把 `../2048` 复制成独立副本，只加了一个 **`?seed=`** 固定随机流；控制、记录、回放全部外置：

- **纯棋盘模式（`--plain`）**：只给模型当前盘面、规则、分数和合法方向；它必须自己模拟“往哪滑会变成什么样”。
- **候选辅助模式**：引擎先算出每个合法落子后的盘面（`Game2048.move`），模型只做选择。用于对照，不是首轮主结果。
- 每一步都记录：盘面（移动前/后/未生成新块）、候选、模型思考原文、最终 `MOVE:`、token、耗时、是否兜底。
- `viewer.html` 可以**逐步观看**（直播轮询 / 离线回放），`index.html` 是四局对局的目录页。

## 目录

```
2048-agent/
  game/index.html          从 ../2048 复制的副本，只加 ?seed= 支持
  game/tests/              原版 Node 逻辑/结构测试
  index.html               对局目录（自动读各 run 的 meta）
  viewer.html              逐步 viewer（直播 + 回放；/*__RUN_DATA__*/ 占位）
  serve.py                 本地静态服务（127.0.0.1:8788，no-store）
  build_replay.py          run 目录 -> 自包含 replay.html
  build_demo.py            run 目录 -> CS2 demo 式播放器 demo.html + report.html + REPORT.md
  play.py                  主控：Playwright 驱动 + LLM 循环 + JSONL 记录
  llm.py                   OpenAI 兼容客户端（流式/非流式，llama.cpp 与 OpenCode Go）
  prompts.py               纯棋盘 / 候选辅助 / 强制决策提示词
  runs/<run-id>/           meta.json + run.jsonl + status.json + final.png + replay.html（gitignore）
  logs/                    8096 服务日志（gitignore）
```

## 快速开始

### 1. 本地 4B（Qwen3.5-4B-Deckard，思考开启）

启动 8096（6 GB 卡；4B 约 3.8 GB）：

```bat
D:\AI\llama.cpp\llama-server.exe ^
  -m D:\AI\models\Qwen3.5-4B-Deckard-HERETIC\Qwen3.5-4B-Deckard-HERETIC-UNCENSORED-Thinking.i1-Q4_K_M.gguf ^
  -ngl 99 -c 8192 -ctk q4_0 -ctv q4_0 -fa on ^
  -rea on --reasoning-budget 512 ^
  -t 16 --cpu-range 0-19 -np 1 ^
  --host 127.0.0.1 --port 8096 --alias 2048-4b --api-key local
```

```powershell
cd E:\AI\tools\HEAOZIE\2048-agent
$env:PLAYWRIGHT_BROWSERS_PATH='E:/AI/tools/playwright/browsers'
& 'E:\AI\tools\playwright\.venv\Scripts\python.exe' play.py `
  --policy llm --plain --seed 1 --max-moves 1200 --max-minutes 140 --run-id plain-4b-s1
```

- 想看候选辅助：去掉 `--plain`。
- 只验证链路、不花模型时间：`--policy greedy --max-moves 12`。

### 2. 云端 beam-free（OpenCode Go 免费档）

前置：

- OpenCode Go 订阅的 key 在 `E:\DeepSeekHarness\.credentials.yaml`（`OPENCODE_GO_API_KEY`）。
- `beam-free` **按地区限制**；本机需开新加坡 TUN，否则返回 `403 RegionError`。
- 直连需要浏览器 UA（否则 Cloudflare 1010）和 `x-opencode-session`（否则 400 MissingSessionID）；`llm.py` 已带。

```powershell
cd E:\AI\tools\HEAOZIE\2048-agent
$env:PLAYWRIGHT_BROWSERS_PATH='E:/AI/tools/playwright/browsers'
& 'E:\AI\tools\playwright\.venv\Scripts\python.exe' play.py `
  --policy llm --plain --seed 1 --max-moves 800 --max-minutes 180 --run-id free-beam-s1b `
  --base-url https://opencode.ai/zen/go --model beam-free `
  --credentials E:\DeepSeekHarness\.credentials.yaml --skip-health `
  --max-tokens 8192 --temperature 0.2
```

> 云端不设“思考预算”：`--max-tokens 8192` 只是请求上限，模型想到自然收尾为止。
> beam 在中等盘面通常用 2500–3500 token，12–25 秒/步。

### 3. 云端 DeepSeek V4.1 Flash（high 推理档，不设限）

```powershell
cd E:\AI\tools\HEAOZIE\2048-agent
$env:PLAYWRIGHT_BROWSERS_PATH='E:/AI/tools/playwright/browsers'
& 'E:\AI\tools\playwright\.venv\Scripts\python.exe' play.py `
  --policy llm --plain --unlimited-thinking `
  --model deepseek-v4.1-flash --base-url https://opencode.ai/zen/go `
  --reasoning-effort high --max-tokens 384000 --llm-timeout 3600 `
  --max-moves 10000 --max-minutes 1440 `
  --credentials E:\DeepSeekHarness\.credentials.yaml --skip-health `
  --run-id ds-flash-unlimited-s1
```

- `--base-url` **不要带 `/v1`**：客户端会自己拼 `/v1/chat/completions`，多一层就是 404（本次踩过）。
- `--reasoning-effort high` 走网关参数；`--unlimited-thinking` 去掉提示词里的字数限制。
- `--max-tokens 384000` 是该模型在网关声明的上限（实测接受），只是**传输上限**，不是思考预算；
  纯棋盘上模型自然思考 1.2 万–9.7 万字符/步，中位 45 秒/步，慢步 2–5 分钟。
- 对照写法：加 `--think-brief 600`（提示词请求 ≤600 字）——模型**基本不听**，仍会写上万字。

### 4. 观看

```powershell
py -3 serve.py
# 打开 http://127.0.0.1:8788/
```

- 目录页会自动列出现有 run，并给运行中的局加 `&live=1`。
- 单局直播：`http://127.0.0.1:8788/viewer.html?run=runs/<run-id>&live=1`
- 单局静态：`http://127.0.0.1:8788/viewer.html?run=runs/<run-id>`
- 离线自包含：`py -3 build_replay.py runs/<run-id>` → `runs/<run-id>/replay.html`（可直接双击，不依赖服务）

### 5. Demo 回放 + 完整报告（CS2 demo 式）

```powershell
py -3 build_demo.py runs/<run-id>
```

产出（都在 run 目录里）：

| 文件 | 作用 |
| --- | --- |
| `demo.html` | **播放器**：空格播放/暂停、←→ 单步、↑↓ 倍速、拖时间轴跳步；右侧「思考」按播放进度逐字打出该步完整推理，另有「决策」（四个方向候选对比 + 贪心建议）和「事件」（逐步 killfeed）两个页签 |
| `demo.json` | demo 文件本体（meta + 每步全量数据），播放器里可下载，可离线重放 |
| `report.html` | 完整报告：结果卡片、配置、过程数据、得分曲线、每步耗时图、与其他策略对比、关键节点、阶段走势、思考样本、完整走子表 |
| `REPORT.md` | 同一份报告的 Markdown 版 |

播放器时间轴按每步真实耗时 + 思考字数排布（1× 约等于“读懂这段思考”的速度），8× 可快速扫完整局；
时间轴上的彩色小格是每步落子方向，高度是得分。

**定速播放**：播放栏右侧「0.5s/步 · 1s/步 · 2s/步」——点一下即按固定节奏自动逐步播放（`1s/步` 在 1× 下就是每秒一步，
适合只想看动作、不想读长思考的时候）；再点一次或按 `C` 回到“按真实节奏”。

### 6. 续局（从已有 run 接着下）

```powershell
py -3 play.py --resume runs/<run-id> --policy llm --plain ... 
```

`--resume` 会重放该目录里已记录的走子（同 seed 下逐格校验，不一致立即中止），然后从下一步接着下、**追加**写 `run.jsonl`。
用于进程被外部中断后的收尾；建议先复制一份目录再续，保留原始数据。

## 结果（4×4，spawn=1，seed=1）

| 对局 | 模式 | 模型 | 分数 | 最大块 | 步数 | 用时 | 到 2048 |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| `plain-4b-s1` | 纯棋盘 | Qwen3.5-4B | **780** | 64 | 108 | 21m22s | 否 |
| `assisted-4b-s1` | 候选辅助 | Qwen3.5-4B | **2148** | 128 | 214 | 36m51s | 否 |
| `free-beam-s1b` | 纯棋盘 | beam-free | **840** | 128 | 81* | 34m30s | 否 |
| `ds-flash-plain-s1` | 纯棋盘 | DeepSeek V4.1 Flash (high) | **980** | 128 | 100** | 1h51m | 否 |
| `ds-flash-unlimited-s1` | 纯棋盘·不设限 | DeepSeek V4.1 Flash (high) | 进行中 | — | — | — | 否 |

**一句话**：同 seed 下，4B 纯棋盘 780 → 候选辅助 **2148（2.75×）**，但仍低于一步贪心 4362；
beam 有效段 840 / 128（81 步），之后因 TUN 断流截断。三局都没有到 2048。

\* beam 局在第 82 步遇到 TUN/网络断流（`urlopen error [Errno 2]`），之后的步是贪心兜底，不是 beam 下的；
已截断在最后一个有效步 81，完整 95 步错误日志保留在 `runs/free-beam-s1b/run.with-network-errors.jsonl`。

\** DeepSeek 局在第 100 步（980 分 / 128 块）被外部中断，不是自然结束；100 步全量数据、demo 与报告在
`runs/ds-flash-plain-s1/`。不设限的同模型局 `ds-flash-unlimited-s1` 仍在跑，结果以它为准。

基线（同机 5 seeds，4×4，spawn=1）：

| 策略 | 均分 | 范围 | 最高块 |
| --- | ---: | --- | --- |
| random 合法步 | 1149 | 676–1868 | 64–128 |
| greedy 一步（最大即时合并分+空格） | 4362 | 924–7468 | 256–512 |

## 观察

1. **4B 纯棋盘打不过 random**：780 分低于 random 均分 1149；108 步就无路可走。它会写像模像样的 2048 分析，但在“两个 2 向左合并”这类基本规则上会判断错——viewer 里能看到原文。
2. **候选辅助把 4B 从 780 拉到 2148（2.75×）**：它仍低于一步贪心 4362，说明“模拟”是当前 4B 的主要瓶颈；补上后策略仍不如一个 20 行脚本。
3. **beam 的推理明显更懂 2048**：有效 81 步 840 分 / 最大 128；它会逐行算合并、讨论角落和单调性。之后 TUN 断流，已截断，不作为完整成绩。
4. **“看模型怎么想”比“看分数”更有信息量**：4B 的错误往往出在中间步骤的规则幻觉（比如“两个 2 向左不合并”），而不是最终格式；viewer 把这些原文完整保留了下来。
5. **同 seed 不是逐格配对**：两局随机流相同，但走法一分叉，落点和盘面就不同；比较看量级/分布，不做逐格显著性。
6. **DeepSeek V4.1 Flash (high) 纯棋盘 980 分 / 128 块，低于 random 均分 1149**：它 100 步里平均每步思考 2.3 万字符（最长 9.7 万），
   但和一步贪心的选择只有 **31%** 一致——想得多不等于下得好，它经常在“保持角落”和“整理单调性”之间来回摇摆。
7. **它的思考有明显复读**：大量 “We need …” 句式自我复述；测试过 system 级“只准想 120 词”也只能把 1 万字符压到 7 千，
   提示词约束不住 `reasoning_content`。要限速只能砍 `max_tokens`，代价是截断/兜底（8192 时第 5 步就 fallback）。
8. **“不设限”的真实代价**：同模型不设字数提示时，单步思考 1.2 万–9.7 万字符、中位 45 秒/步、慢步 2–5 分钟，
   一局轻松超过 2 小时；这是“看模型自然怎么想”的票价，不是 harness 的开销。

## 实现要点

- **副本只改随机流**：`?seed=N` → `mulberry32`，替换 `newGame` / `afterMove` 里两处 `Math.random`；原版一行未动。
- **驱动**：Playwright 打开 `game/index.html?seed=N`，读 DOM 得到盘面/分数；用页面里的 `Game2048.move` 判合法步和候选；方向键落子；到 2048 自动点“继续玩”；死局停。
- **记录**：每步 JSONL（`grid_before` / `grid_after` / `grid_move_only` / `spawned` / `candidates` / `reasoning` / `content` / `latency_ms` / `usage`）。`meta.json` 每步更新，`status.json` 给直播的“正在思考”用。
- **LLM**：`llm.py` 同时支持 llama.cpp 和 OpenCode Go；流式解析 `delta.reasoning_content`，断流自动降级非流式；本地 4B 走 8096，beam 走 `https://opencode.ai/zen/go`。
- **viewer**：棋盘 + 逐步卡片 + 思考原文 + 候选小盘 + 分数/耗时/合法步；直播每 1.2 s 轮询；`build_replay.py` 把 `run.jsonl` 内嵌成单文件。

## 已知坑

- **`beam-free` 地区限制**：不开新加坡 TUN 是 403 RegionError；`longcat-2.0` 不是免费模型（用户已确认），不要拿它当 free。
- **Cloudflare 1010**：默认 `Python-urllib` UA 会被拒；必须浏览器 UA。
- **400 MissingSessionID**：OpenCode Go 要 `x-opencode-session`；`llm.py` 每个客户端生成一个。
- **beam 想太久**：无思考预算时会自然收尾，但复杂盘面可能吃满 8192；若 `content` 为空，harness 会重问一次，仍失败才贪心兜底（日志有 `FALLBACK`）。
- **流断**：偶发 `Remote end closed connection`；自动转非流式，不丢步。
- **本地 4B 的 512 思考预算**：思考会被截断，`content` 仍应给出 `MOVE:`；这是为了把一局压到可接受时长。
- **胜利弹层**：到 2048 后键盘被拦，driver 自动点 `#btn-overlay-continue`。
- **localStorage 旧局**：每个 run 用全新 browser context，不继承上一局。
- **直接双击 viewer.html**：`file://` 下浏览器禁止 fetch `run.jsonl`；用 `serve.py`，或看 `replay.html`。
- **Edge headless 截图不可用**（本机已知）；这里统一用 Playwright 自带 chromium。

## 测试

```powershell
cd E:\AI\tools\HEAOZIE\2048-agent\game
node tests/logic.test.mjs        # 53 passed
node tests/structure.test.mjs    # PASS
```

viewer smoke：`runs/plain-4b-s1/replay.html` 实测 108 步、16 格、终局 780/64/108，控制台 0 错误。

## 许可

沿用 HEAOZIE 仓库的 [Sustainable Use License](../LICENSE)；`2048` 原游戏版权归原作者。
