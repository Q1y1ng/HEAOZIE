"""Fast bitboard 2048 simulator — feasibility spike for the NN plan.

Board = 16 nibbles in one int; each cell stores the exponent (0 empty, 1=2, 2=4 ...).
Row moves are precomputed for all 65536 16-bit rows, so a move is 4 table lookups
(plus a transpose for up/down).

Measured on this machine (2026-10-24, Python 3.12, no numpy in the hot loop):
  - single-board move throughput : ~390k moves/s
  - random-policy playthrough    : ~420 games/s
  - random-policy average score  : 1148 (matches the recorded harness baseline 1149)

This is the environment the NN would be trained in: 10M moves of experience is
~30-60 s of generation, so TD / imitation training is not data-bound here.
"""
from __future__ import annotations

import random
import time


def rows_of(b: int) -> list[int]:
    return [(b >> (16 * i)) & 0xFFFF for i in range(4)]


def _move_row_left(r: int) -> tuple[int, int]:
    cells = [(r >> (4 * i)) & 0xF for i in range(4)]
    vals = [c for c in cells if c]
    out: list[int] = []
    score = 0
    i = 0
    while i < len(vals):
        if i + 1 < len(vals) and vals[i] == vals[i + 1]:
            nv = vals[i] + 1
            out.append(nv)
            score += 1 << nv
            i += 2
        else:
            out.append(vals[i])
            i += 1
    out += [0] * (4 - len(out))
    rr = 0
    for i, c in enumerate(out):
        rr |= c << (4 * i)
    return rr, score


LEFT = [_move_row_left(r) for r in range(65536)]


def _rev16(r: int) -> int:
    return ((r & 0xF) << 12) | ((r & 0xF0) << 4) | ((r & 0xF00) >> 4) | ((r >> 12) & 0xF)


RIGHT = [(_rev16(LEFT[_rev16(r)][0]), LEFT[_rev16(r)][1]) for r in range(65536)]


def transpose(b: int) -> int:
    rows = rows_of(b)
    t = 0
    for c in range(4):
        col = 0
        for r in range(4):
            col |= ((rows[r] >> (4 * c)) & 0xF) << (4 * r)
        t |= col << (16 * c)
    return t


def move(b: int, d: int) -> tuple[int, int]:
    """d: 0=left 1=right 2=up 3=down. Returns (new_board, score_gained)."""
    rows = rows_of(b)
    if d < 2:
        table = LEFT if d == 0 else RIGHT
        nb = 0
        sc = 0
        for i, r in enumerate(rows):
            nr, s = table[r]
            nb |= nr << (16 * i)
            sc += s
        return nb, sc
    t = transpose(b)
    table = LEFT if d == 2 else RIGHT
    nb = 0
    sc = 0
    for i, r in enumerate(rows_of(t)):
        nr, s = table[r]
        nb |= nr << (16 * i)
        sc += s
    return transpose(nb), sc


def empties(b: int) -> int:
    return sum(1 for i in range(16) if ((b >> (4 * i)) & 0xF) == 0)


def spawn(b: int, rng: random.Random) -> int:
    e = empties(b)
    if not e:
        return b
    k = rng.randrange(e)
    idx = -1
    for i in range(16):
        if ((b >> (4 * i)) & 0xF) == 0:
            idx += 1
            if idx == k:
                v = 1 if rng.random() < 0.9 else 2
                return b | (v << (4 * i))
    return b


def play_random(games: int, rng: random.Random) -> list[int]:
    scores = []
    for _ in range(games):
        b = spawn(spawn(0, rng), rng)
        score = 0
        while True:
            dirs = [d for d in range(4) if move(b, d)[0] != b]
            if not dirs:
                break
            b, s = move(b, rng.choice(dirs))
            score += s
            b = spawn(b, rng)
        scores.append(score)
    return scores


def bench(games: int = 200) -> dict:
    rng = random.Random(1)
    t0 = time.time()
    scores = play_random(games, rng)
    dt = time.time() - t0
    return {
        "games": games,
        "avg_score": sum(scores) / len(scores),
        "max_score": max(scores),
        "seconds": round(dt, 3),
        "games_per_sec": round(games / dt, 1),
    }


if __name__ == "__main__":
    print(bench())
