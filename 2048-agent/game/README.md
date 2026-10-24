# 2048 · 自定义棋盘

单文件 2048 小游戏，支持 **4×4 – 36×36** 自定义棋盘，并可设置**每次生成「2」的个数**（1–12）。

## 运行

直接用浏览器打开 `index.html`，无需构建或联网。

```powershell
start index.html
```

## 操作

- 方向键 / WASD：移动
- 触屏或鼠标拖拽：滑动
- U：撤销一步
- 「应用并新开一局」：按所选尺寸开新局
- 「每次生成 2 的个数」：成功移动后落入几个「2」（默认 1，下一回合生效）

## 测试

```powershell
node tests/logic.test.mjs
node tests/structure.test.mjs
```
