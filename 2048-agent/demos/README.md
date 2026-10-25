# 2048 AI 演示（4×4 bitboard 期望最大引擎）

> 🌐 在线播放：<https://q1y1ng.github.io/HEAOZIE/2048-agent/demos/demo.html>
> 落地页：<https://q1y1ng.github.io/HEAOZIE/>

| 文件 | 说明 |
| --- | --- |
| `demo_condensed.mp4` | **4:22 浓缩回放**：1280×720，2 fps（每帧 0.5 s），25 步/帧，带分数曲线与进度条 |
| `demo.html` | **交互回放**（单页自包含，双击即开）：13,014 步可拖时间轴、0.1–2 s 定速、1024→32768 里程碑跳转、空格/←→/C 快捷键 |
| `cover.png` | 封面帧（第 12,897 步：16384 + 8192 同时在场） |

## 这一局

| 项 | 值 |
| --- | --- |
| 分数 | **342,816** |
| 最大块 | **16384** |
| 步数 | 13,014 |
| 用时 | 约 22 分钟（100 ms/步，单核） |
| 配置 | `nn2048/engine_bb.cpp`，bitboard + 行查表 + 置换表 + 概率截断，实际深度 6.15 |

引擎在同预算（100 ms/步，18 局）下的整体水平：**均分 231,928、16384 率 39%、8192 及以上 94%**。

## 生成方式

全部离线、可复现（不需要浏览器渲染，Pillow 直出帧 → ffmpeg）：

```powershell
cd 2048-agent\nn2048
clang++ -O3 -std=c++17 -D_CRT_SECURE_NO_WARNINGS -o engine_bb.exe engine_bb.cpp
.\engine_bb.exe --selftest
py -3 bench.py --exe engine_bb.exe --seeds 10 --budget 100 --parallel 10 --start-seed 81 --save-dir ..\runs\batchG
py -3 build_engine_demo.py ..\runs\batchG\s85.json --out ..\demos\demo.html
py -3 render_video.py ..\runs\batchG\s85.json --out ..\demos\demo_condensed.mp4 --target-seconds 260 --fps 2
```

数据与结论：[`../nn2048/RESULTS.md`](../nn2048/RESULTS.md)、[`../nn2048/NIGHT_REPORT.md`](../nn2048/NIGHT_REPORT.md)。
