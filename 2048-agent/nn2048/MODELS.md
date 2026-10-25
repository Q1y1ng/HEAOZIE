# 训练好的 n-tuple 权重

权重不随仓库分发（单个 768 MB），放在 GitHub Release 里：

**<https://github.com/Q1y1ng/HEAOZIE/releases/tag/ntuple-weights-v1>**

| 文件 | 网络 | 训练局数 | 贪心 | 深度 1 | 深度 2 + 截断 | 备注 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `ntuple-6x12-10M.bin` | 12 × 6-tuple（全部 2×3 / 3×2 块，804 MB 表） | 10.0M | 83,054 | 131,543 | **168,982** | 最高 363,260、16384×3/20 |
| `ntuple-4x16-4M.bin` | 16 × 4-tuple（4 行 + 4 列 + 8 个 2×2，4 MB 表） | 4.0M | 52,131 | 85,031 | ~107k | 早期版本 |

## 用法

```bash
# 编译（clang++ / MSVC，C++17）
clang++ -O3 -std=c++17 -D_CRT_SECURE_NO_WARNINGS -o ntuple.exe ntuple.cpp

# 贪心（纯网络价值，最快的策略）
./ntuple.exe --load ntuple-6x12-10M.bin --eval 30 --eval-only

# 深度 1 期望最大（0.9 s/局）
./ntuple.exe --load ntuple-6x12-10M.bin --eval 30 --eval-only --search-depth 1

# 深度 2 + 概率截断（13–23 s/局，最强）
./ntuple.exe --load ntuple-6x12-10M.bin --eval 20 --eval-only --search-depth 2 --prob-cut 1e-3
```

权重文件格式：magic `NTUP2048` + 头部（图案数、tuple 长度、seed、α、已训局数）+ 每个图案的
格子索引与 float32 表。`--load` 会同时恢复图案定义，不需要再传 `--pat`。

## 训练

从零训练的命令与曲线见 [`NTUPLE.md`](NTUPLE.md)；训练日志在 [`training-logs/`](training-logs/)。

| 日志 | 内容 |
| --- | --- |
| `4x16_4M.log` | 4-tuple × 16，400 万局 |
| `6x8_6M.log` | 6-tuple × 8，600 万局 |
| `6x12_10M.log` | 6-tuple × 12，1000 万局（最佳） |

## 现状与上限

手工评估 + bitboard 期望最大仍是最高分方案（均分 **231.9k**、16384 率 39%、最高 387.4k，见 `RESULTS.md`）。
学习型最好到 **169.0k**（6×12 + 深度 2），约为手工方案的 73%，且在该网络规模下已饱和；
再往上需要 16–20 个 6-tuple 的 5 小时以上长训，或多阶段 TD。
