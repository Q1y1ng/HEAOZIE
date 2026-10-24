"""Generic N x N 2048 engine: board, moves, heuristic evaluation, expectimax.

Design goals (2026-10-24):
  - board-size agnostic: 4x4, 5x5, 6x6 ... all use the same code path;
  - "score first": expectimax search over the real game rules, heuristic eval;
  - the neural network (later) replaces/augments `evaluate()` — nothing else changes.

Board representation: tuple[int, ...] of length n*n, each cell = exponent
(0 empty, 1 = 2, 2 = 4, ...). Tuple keeps it hashable for the transposition table.
"""
from __future__ import annotations

import math
import random
import time
from functools import lru_cache

DIRS = ("up", "down", "left", "right")
NEG_INF = -1e18


# --------------------------------------------------------------------------- #
# moves
# --------------------------------------------------------------------------- #
@lru_cache(maxsize=None)
def _merge_line(line: tuple[int, ...]) -> tuple[tuple[int, ...], int]:
    vals = [c for c in line if c]
    out: list[int] = []
    gain = 0
    i = 0
    while i < len(vals):
        if i + 1 < len(vals) and vals[i] == vals[i + 1]:
            nv = vals[i] + 1
            out.append(nv)
            gain += 1 << nv
            i += 2
        else:
            out.append(vals[i])
            i += 1
    out += [0] * (len(line) - len(out))
    return tuple(out), gain


def _rows(board: tuple[int, ...], n: int) -> list[tuple[int, ...]]:
    return [board[i * n:(i + 1) * n] for i in range(n)]


def _from_rows(rows: list[tuple[int, ...]]) -> tuple[int, ...]:
    return tuple(c for row in rows for c in row)


def _cols(board: tuple[int, ...], n: int) -> list[tuple[int, ...]]:
    return [tuple(board[r * n + c] for r in range(n)) for c in range(n)]


def move(board: tuple[int, ...], direction: str, n: int) -> tuple[tuple[int, ...], int]:
    """Apply a move. Returns (new_board, score_gained); new_board == board if illegal."""
    if direction in ("left", "right"):
        rows = _rows(board, n)
        out, gain = [], 0
        for row in rows:
            line = row if direction == "left" else row[::-1]
            merged, g = _merge_line(line)
            if direction == "right":
                merged = merged[::-1]
            out.append(merged)
            gain += g
        nb = _from_rows(out)
    else:
        cols = _cols(board, n)
        out, gain = [], 0
        for col in cols:
            line = col if direction == "up" else col[::-1]
            merged, g = _merge_line(line)
            if direction == "down":
                merged = merged[::-1]
            out.append(merged)
            gain += g
        # transpose back: out[c][r]
        nb = tuple(out[c][r] for r in range(n) for c in range(n))
    if nb == board:
        return board, 0
    return nb, gain


def legal_moves(board: tuple[int, ...], n: int) -> list[str]:
    return [d for d in DIRS if move(board, d, n)[0] != board]


def empties_of(board: tuple[int, ...]) -> list[int]:
    return [i for i, v in enumerate(board) if v == 0]


def max_tile(board: tuple[int, ...]) -> int:
    return max(board) if board else 0


def spawn(board: tuple[int, ...], rng: random.Random, prob4: float = 0.1) -> tuple[int, ...]:
    empty = empties_of(board)
    if not empty:
        return board
    i = rng.choice(empty)
    b = list(board)
    b[i] = 1 if rng.random() >= prob4 else 2
    return tuple(b)


# --------------------------------------------------------------------------- #
# heuristic evaluation (higher = better)
# --------------------------------------------------------------------------- #
def _log(v: int) -> float:
    return 0.0 if v == 0 else float(v)


@lru_cache(maxsize=1 << 20)
def _line_mono(line: tuple[int, ...]) -> tuple[float, float]:
    vals = [_log(c) for c in line]
    inc = sum(max(0.0, vals[i + 1] - vals[i]) for i in range(len(vals) - 1))
    dec = sum(max(0.0, vals[i] - vals[i + 1]) for i in range(len(vals) - 1))
    return inc, dec


@lru_cache(maxsize=1 << 20)
def _pair_diff(a: int, b: int) -> float:
    return abs(_log(a) - _log(b))


def monotonicity(board: tuple[int, ...], n: int) -> float:
    """Sum of the better (increasing vs decreasing) monotonic order per line."""
    total = 0.0
    for line in _rows(board, n) + _cols(board, n):
        inc, dec = _line_mono(line)
        total += min(inc, dec)
    return total


def smoothness(board: tuple[int, ...], n: int) -> float:
    """Negative sum of |log diffs| between orthogonal neighbours (larger = smoother)."""
    total = 0.0
    for r in range(n):
        for c in range(n):
            v = board[r * n + c]
            if c + 1 < n:
                total -= _pair_diff(v, board[r * n + c + 1])
            if r + 1 < n:
                total -= _pair_diff(v, board[(r + 1) * n + c])
    return total


def corner_bonus(board: tuple[int, ...], n: int) -> float:
    m = max(board)
    if m == 0:
        return 0.0
    corners = {0, n - 1, n * (n - 1), n * n - 1}
    hits = sum(1 for i in corners if board[i] == m)
    return _log(m) * (1.0 if hits else 0.0)


def evaluate(board: tuple[int, ...], n: int) -> float:
    """Heuristic: prefer many empties, monotone rows/cols, smooth neighbours,
    and the biggest tile in a corner."""
    empties = board.count(0)
    return (
        300.0 * empties
        + 50.0 * monotonicity(board, n)
        + 12.0 * smoothness(board, n)
        + 40.0 * corner_bonus(board, n)
        + 5.0 * max_tile(board)
    )


# --------------------------------------------------------------------------- #
# expectimax
# --------------------------------------------------------------------------- #
class SearchStats:
    def __init__(self) -> None:
        self.nodes = 0
        self.depth_reached = 0
        self.cutoff = False


def expectimax(
    board: tuple[int, ...],
    n: int,
    depth: int,
    stats: SearchStats,
    deadline: float,
    prob4: float = 0.1,
) -> float:
    """Max node: pick a move. Chance node: expected value over spawns.

    `depth` counts move plies; each move ply is followed by one spawn ply.
    """
    if time.time() > deadline:
        stats.cutoff = True
        return evaluate(board, n)

    stats.nodes += 1
    moves = legal_moves(board, n)
    if not moves:
        return evaluate(board, n) - 10000.0
    if depth <= 0:
        return evaluate(board, n)

    best = NEG_INF
    for d in moves:
        nb, gain = move(board, d, n)
        v = gain + chance_value(nb, n, depth - 1, stats, deadline, prob4)
        if v > best:
            best = v
        if stats.cutoff:
            break
    return best


MAX_CHANCE_CELLS = 6


def chance_value(
    board: tuple[int, ...],
    n: int,
    depth: int,
    stats: SearchStats,
    deadline: float,
    prob4: float,
) -> float:
    empty = empties_of(board)
    if not empty:
        return expectimax(board, n, depth, stats, deadline, prob4)
    if len(empty) > MAX_CHANCE_CELLS:
        # Approximate the spawn expectation on a fixed, evenly spaced subset.
        step = len(empty) / MAX_CHANCE_CELLS
        empty = [empty[int(i * step)] for i in range(MAX_CHANCE_CELLS)]
    total = 0.0
    for i in empty:
        b = list(board)
        b[i] = 1
        total += (1.0 - prob4) * expectimax(tuple(b), n, depth, stats, deadline, prob4)
        if prob4 > 0:
            b[i] = 2
            total += prob4 * expectimax(tuple(b), n, depth, stats, deadline, prob4)
        if stats.cutoff:
            break
    return total / len(empty)


def best_move(
    board: tuple[int, ...],
    n: int,
    time_budget: float = 0.15,
    max_depth: int = 4,
    prob4: float = 0.1,
) -> tuple[str, float, SearchStats]:
    """Iterative deepening: keep the deepest complete answer within the budget."""
    moves = legal_moves(board, n)
    if not moves:
        return "", 0.0, SearchStats()
    if len(moves) == 1:
        st = SearchStats()
        st.depth_reached = 1
        return moves[0], 0.0, st

    deadline = time.time() + time_budget
    best_d, best_v = moves[0], NEG_INF
    stats = SearchStats()
    for depth in range(1, max_depth + 1):
        local = SearchStats()
        local_best, local_v = best_d, NEG_INF
        for d in moves:
            nb, gain = move(board, d, n)
            v = gain + chance_value(nb, n, depth - 1, local, deadline, prob4)
            if v > local_v:
                local_v, local_best = v, d
            if local.cutoff:
                break
        if local.cutoff:
            stats.cutoff = True
            break
        best_d, best_v = local_best, local_v
        stats.depth_reached = depth
        stats.nodes = local.nodes
        if time.time() > deadline:
            break
    return best_d, best_v, stats
