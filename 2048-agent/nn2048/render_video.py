"""Render an engine game (engine_bb --save JSON) to an mp4 video.

Default: 2 fps = 0.5 s per move, exactly one frame per move.

Usage:
  py -3 render_video.py runs/engine-bb-s1/engine.json --out runs/engine-bb-s1/demo_0.5s.mp4
  py -3 render_video.py engine.json --out slow.mp4 --fps 2
  py -3 render_video.py engine.json --out condensed.mp4 --stride 20   # 20 moves per frame
"""
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

FFMPEG = r"D:\full\ffmpeg-7.0.1-full_build\bin\ffmpeg.exe"

BG = (14, 17, 22)
PANEL = (22, 27, 34)
FG = (232, 238, 246)
DIM = (138, 150, 166)
ACCENT = (110, 168, 254)

# classic 2048 palette, extended for exponents up to 16
TILE_BG = {
    1: (238, 228, 218), 2: (237, 224, 200), 3: (242, 177, 121), 4: (245, 149, 99),
    5: (246, 124, 95), 6: (246, 94, 59), 7: (237, 207, 114), 8: (237, 204, 97),
    9: (237, 200, 80), 10: (237, 197, 63), 11: (60, 58, 50), 12: (60, 58, 50),
    13: (52, 50, 44), 14: (44, 42, 37), 15: (36, 34, 30), 16: (28, 27, 24),
}
TILE_FG_LIGHT = (249, 246, 242)
TILE_FG_DARK = (119, 110, 101)

ARROW = {"left": "\u2190", "right": "\u2192", "up": "\u2191", "down": "\u2193"}
DIR_ZH = {"left": "\u5de6", "right": "\u53f3", "up": "\u4e0a", "down": "\u4e0b"}


def find_font(names: list[str], size: int) -> ImageFont.FreeTypeFont:
    for path in names:
        p = pathlib.Path(path)
        if p.exists():
            try:
                return ImageFont.truetype(str(p), size)
            except OSError:
                continue
    return ImageFont.load_default()


FONT_DIRS = [
    r"C:\Windows\Fonts\msyhbd.ttc",
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\arialbd.ttf",
]


class Renderer:
    def __init__(self, size: tuple[int, int], meta: dict) -> None:
        self.w, self.h = size
        self.meta = meta
        self.img = Image.new("RGB", (self.w, self.h), BG)
        self.draw = ImageDraw.Draw(self.img)
        s = self.h / 720.0
        self.f_title = find_font(FONT_DIRS, int(24 * s))
        self.f_small = find_font(FONT_DIRS, int(16 * s))
        self.f_label = find_font(FONT_DIRS, int(17 * s))
        self.f_score = find_font(FONT_DIRS, int(58 * s))
        self.f_big = find_font(FONT_DIRS, int(30 * s))
        self.f_tile = find_font([r"C:\Windows\Fonts\segoeuib.ttf",
                                 r"C:\Windows\Fonts\arialbd.ttf"], int(44 * s))
        self.f_tile_small = find_font([r"C:\Windows\Fonts\segoeuib.ttf",
                                       r"C:\Windows\Fonts\arialbd.ttf"], int(30 * s))
        # board geometry
        self.board_px = int(self.h * 0.80)
        self.board_x = int(self.w * 0.035)
        self.board_y = int((self.h - self.board_px) / 2 + self.h * 0.03)
        self.gap = int(self.board_px * 0.022)
        self.cell = (self.board_px - 5 * self.gap) // 4
        self.panel_x = self.board_x + self.board_px + int(self.w * 0.035)

    # -- helpers -------------------------------------------------------
    def _tile(self, x: int, y: int, w: int, h: int, level: int) -> None:
        if level <= 0:
            self.draw.rounded_rectangle([x, y, x + w, y + h], radius=int(w * 0.08),
                                        fill=(32, 38, 46))
            return
        bg = TILE_BG.get(min(level, 16), TILE_BG[16])
        self.draw.rounded_rectangle([x, y, x + w, y + h], radius=int(w * 0.08), fill=bg)
        value = 1 << level
        text = str(value) if value < 100000 else f"{value // 1024}K"
        fg = TILE_FG_DARK if level <= 2 else TILE_FG_LIGHT
        font = self.f_tile if len(text) <= 4 else self.f_tile_small
        self.draw.text((x + w / 2, y + h / 2), text, font=font, fill=fg, anchor="mm")

    def draw_board(self, grid: list[list[int]]) -> None:
        for r in range(4):
            for c in range(4):
                x = self.board_x + self.gap + c * (self.cell + self.gap)
                y = self.board_y + self.gap + r * (self.cell + self.gap)
                value = grid[r][c]
                level = 0 if value <= 0 else value.bit_length() - 1
                self._tile(x, y, self.cell, self.cell, level)

    def draw_score_chart(self, scores: list[int], up_to: int, x: int, y: int, w: int, h: int) -> None:
        self.draw.rounded_rectangle([x, y, x + w, y + h], radius=8, fill=PANEL)
        n = len(scores)
        if n < 2:
            return
        mx = max(scores[-1], 1)
        step = max(1, n // 500)
        pts = []
        for i in range(0, n, step):
            px = x + 6 + (w - 12) * i / (n - 1)
            py = y + h - 6 - (h - 12) * scores[i] / mx
            pts.append((px, py))
        if len(pts) >= 2:
            # whole run, dim
            self.draw.line(pts, fill=(48, 60, 76), width=2)
            # played part, bright
            cut = max(1, min(len(pts), int(up_to / max(1, n - 1) * (len(pts) - 1)) + 1))
            if cut >= 2:
                self.draw.line(pts[:cut], fill=ACCENT, width=3)
            # playhead dot
            px, py = pts[cut - 1]
            self.draw.ellipse([px - 4, py - 4, px + 4, py + 4], fill=(232, 163, 61))

    # -- frames --------------------------------------------------------
    def frame(self, step: dict, grid: list[list[int]], scores: list[int],
              total: int, last: bool = False) -> Image.Image:
        self.img = Image.new("RGB", (self.w, self.h), BG)
        self.draw = ImageDraw.Draw(self.img)
        seed = self.meta.get("seed", "?")
        self.draw.text((self.board_x, int(self.h * 0.045)),
                       f"2048 \u00b7 expectimax bitboard \u00b7 seed {seed}",
                       font=self.f_title, fill=FG)
        sub = f"{self.meta.get('budget_ms', 0):.0f} ms/move"
        if self.meta.get("label"):
            sub += f"  \u00b7  {self.meta['label']}"
        self.draw.text((self.board_x, int(self.h * 0.045) + int(self.h * 0.045)),
                       sub, font=self.f_small, fill=ACCENT)

        self.draw_board(grid)

        # right panel
        x = self.panel_x
        self.draw.text((x, int(self.h * 0.10)), "\u5206\u6570", font=self.f_label, fill=DIM)
        self.draw.text((x, int(self.h * 0.135)), f"{step.get('score', 0):,}", font=self.f_score, fill=FG)

        y = int(self.h * 0.27)
        rows = [
            ("\u6b65\u6570", f"{step.get('i', 0)} / {total}"),
            ("\u6700\u5927\u5757", f"{max(grid[r][c] for r in range(4) for c in range(4))}"),
            ("\u672c\u6b65\u5f97\u5206", f"+{step.get('gained', 0)}"),
            ("\u641c\u7d22\u6df1\u5ea6", f"{step.get('depth', 0)}"),
        ]
        for label, value in rows:
            self.draw.text((x, y), label, font=self.f_label, fill=DIM)
            self.draw.text((x + int(self.w * 0.135), y), value, font=self.f_big, fill=FG)
            y += int(self.h * 0.062)

        # the move
        mv = step.get("move", "")
        self.draw.text((x, y + int(self.h * 0.01)), "\u65b9\u5411", font=self.f_label, fill=DIM)
        self.draw.text((x + int(self.w * 0.135), y), f"{ARROW.get(mv, '?')} {DIR_ZH.get(mv, '')}",
                       font=find_font(FONT_DIRS, int(38 * self.h / 720)), fill=ACCENT)

        # chart + progress
        chart_y = int(self.h * 0.66)
        chart_h = int(self.h * 0.20)
        chart_w = self.w - x - int(self.w * 0.035)
        self.draw_score_chart(scores, int(step.get("i", 0)), x, chart_y, chart_w, chart_h)
        bar_y = chart_y + chart_h + int(self.h * 0.03)
        self.draw.rounded_rectangle([x, bar_y, x + chart_w, bar_y + 8], radius=4, fill=(38, 45, 55))
        frac = min(1.0, max(0.0, (step.get("i", 0)) / max(total, 1)))
        if frac > 0:
            self.draw.rounded_rectangle([x, bar_y, x + int(chart_w * frac), bar_y + 8], radius=4, fill=ACCENT)

        if last:
            self.draw.text((x, bar_y + int(self.h * 0.04)), "\u7ec8\u5c40", font=self.f_big, fill=ACCENT)
        return self.img

    def title_card(self, lines: list[str]) -> Image.Image:
        self.img = Image.new("RGB", (self.w, self.h), BG)
        self.draw = ImageDraw.Draw(self.img)
        y = int(self.h * 0.36)
        for text, font, color in lines:
            self.draw.text((self.w // 2, y), text, font=font, fill=color, anchor="mm")
            y += int(self.h * 0.09)
        return self.img


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("json", type=pathlib.Path)
    ap.add_argument("--out", type=pathlib.Path, required=True)
    ap.add_argument("--fps", type=float, default=2.0, help="frames per second (2 = 0.5 s/step)")
    ap.add_argument("--stride", type=int, default=1, help="moves per frame")
    ap.add_argument("--target-seconds", type=float, default=0,
                    help="abridged cut: pick the stride so the clip fits this length")
    ap.add_argument("--label", default="", help="overlay label, e.g. 'x20 condensed'")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--crf", type=int, default=30)
    ap.add_argument("--intro", type=float, default=2.0, help="title card seconds")
    ap.add_argument("--outro", type=float, default=4.0, help="final card seconds")
    ap.add_argument("--max-steps", type=int, default=0)
    args = ap.parse_args()

    data = json.loads(args.json.read_text(encoding="utf-8"))
    meta = data.get("meta", {})
    steps = data["steps"]
    if args.max_steps:
        steps = steps[: args.max_steps]
    total = steps[-1]["i"] if steps else 0
    scores = [0] + [s.get("score", 0) for s in steps]

    r = Renderer((args.width, args.height), meta)
    args.out.parent.mkdir(parents=True, exist_ok=True)

    # abridged cut: how many moves per frame fit the requested duration?
    body_seconds = max(1.0, args.target_seconds - args.intro - args.outro) if args.target_seconds else 0
    if body_seconds:
        budget_frames = max(1.0, body_seconds * args.fps)
        args.stride = max(args.stride, int(round(len(steps) / budget_frames)))
    if args.label:
        meta["label"] = args.label
    print(f"stride={args.stride}  frames~{len(steps) / max(1, args.stride):.0f}  "
          f"duration~{len(steps) / max(1, args.stride) / args.fps + args.intro + args.outro:.0f}s",
          file=sys.stderr)

    cmd = [
        FFMPEG, "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24",
        "-s", f"{args.width}x{args.height}", "-r", f"{args.fps}", "-i", "-",
        "-c:v", "libx264", "-preset", "veryfast", "-crf", str(args.crf),
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        str(args.out),
    ]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)

    def emit(img: Image.Image, times: int = 1) -> None:
        raw = img.tobytes()
        for _ in range(times):
            proc.stdin.write(raw)

    frames = 0
    if args.intro > 0:
        n = int(round(args.intro * args.fps))
        card = r.title_card([
            ("2048 \u00b7 expectimax bitboard", r.f_score, FG),
            (f"seed {meta.get('seed', '?')} \u00b7 {meta.get('budget_ms', 0):.0f} ms/move", r.f_big, DIM),
            (f"{total} \u6b65 \u00b7 {args.fps:g} fps", r.f_big, DIM),
        ])
        emit(card, max(1, n))
        frames += max(1, n)

    for idx, step in enumerate(steps):
        if idx % max(1, args.stride):
            continue
        grid = step.get("grid_after") or [[0] * 4 for _ in range(4)]
        emit(r.frame(step, grid, scores, total))
        frames += 1
        if frames % 200 == 0:
            print(f"\r{frames} frames ({idx + 1}/{len(steps)} steps)", end="", file=sys.stderr, flush=True)

    if args.outro > 0 and steps:
        n = int(round(args.outro * args.fps))
        last = steps[-1]
        tmax = max(max(row) for row in (last.get("grid_after") or [[0]]))
        card = r.title_card([
            (f"\u6700\u7ec8 {last.get('score', 0):,} \u5206", r.f_score, ACCENT),
            (f"\u6700\u5927\u5757 {tmax} \u00b7 {last.get('i', 0)} \u6b65", r.f_big, FG),
            (f"seed {meta.get('seed', '?')} \u00b7 {meta.get('budget_ms', 0):.0f} ms/move", r.f_big, DIM),
        ])
        emit(card, max(1, n))
        frames += max(1, n)

    proc.stdin.close()
    proc.wait()
    print(f"\n{args.out}  frames={frames}  size={args.out.stat().st_size / 1e6:.1f} MB",
          file=sys.stderr)
    return 0 if proc.returncode == 0 else proc.returncode


if __name__ == "__main__":
    raise SystemExit(main())
