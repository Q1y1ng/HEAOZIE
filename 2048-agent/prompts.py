"""Prompt templates for the 2048 agent (assisted mode).

The engine simulates every legal move and shows the resulting board. The model
only has to choose; this removes mental-simulation errors from small models and
keeps the decision auditable (the viewer shows the same candidate boards).
"""
from __future__ import annotations

SYSTEM = "You are a precise 2048 player. Follow the output format exactly."
DECISION_SYSTEM = "You are a precise 2048 player. Output only the requested line."


def board_text(grid: list[list[int]]) -> str:
    maxv = max((max(row) for row in grid), default=0)
    width = max(2, len(str(maxv)))
    lines = []
    for row in grid:
        cells = []
        for v in row:
            cells.append(("." if v == 0 else str(v)).rjust(width))
        lines.append("  " + " ".join(cells))
    return "\n".join(lines)


def _history_line(history: list[dict]) -> str:
    if not history:
        return ""
    tail = history[-4:]
    parts = [f"{h['move']}(+{h.get('gain', 0)})" for h in tail]
    return "Recent moves: " + ", ".join(parts) + "\n"


def build_prompt(
    state: dict,
    candidates: dict,
    history: list[dict] | None = None,
    assisted: bool = True,
    think_limit: bool = True,
    brief_chars: int | None = None,
) -> str:
    grid = state["grid"]
    score = state.get("score", 0)
    max_tile = state.get("maxTile", max(max(r) for r in grid))
    empties = sum(1 for row in grid for v in row if v == 0)

    legal = [d for d in ("up", "down", "left", "right") if d in candidates]
    head = [
        f"2048 on a {state['size']}x{state['size']} grid. 0 / . = empty.",
        "Rules: tiles slide as far as possible; two equal adjacent tiles merge once and add to the score;",
        "after each move a new 2 or 4 appears in a random empty cell.",
    ]
    if assisted:
        head.append("The engine simulated every legal move for you.")
        head.append("Each candidate is the board immediately after that move (the new random tile has not spawned yet).")
    head += [
        "",
        "Current board (top row first):",
        board_text(grid),
        "",
        f"Score: {score}",
        f"Largest tile: {max_tile}",
        f"Empty cells: {empties}",
        f"Legal moves: {', '.join(legal) if legal else '(none)'}",
    ]
    hist = _history_line(history or [])
    if hist:
        head += ["", hist.rstrip()]

    if assisted and candidates:
        head += ["", "Candidates:"]
        for direction in ("up", "down", "left", "right"):
            cand = candidates.get(direction)
            if not cand:
                continue
            alive = "yes" if cand.get("canMove", True) else "no (this move ends the game)"
            head.append(
                f"- {direction}: gain {cand.get('gain', 0)}, empties {cand.get('empties', 0)}, alive {alive}"
            )
            head.append(board_text(cand["grid"]))
            head.append("")

    head.append("Choose one legal move that you believe is best for the long-term score.")
    if brief_chars:
        head.append(
            f"Keep the reasoning brief — at most {brief_chars} characters — name the key risk and plan,"
            " then output exactly one final line, and nothing after it:"
        )
    elif think_limit:
        head.append("Think in at most 120 words. Then output exactly one final line, and nothing after it:")
    else:
        head.append("Think through the position as carefully as you need. Then output exactly one final line, and nothing after it:")
    head.append("MOVE: <up|down|left|right>")
    return "\n".join(head)


def build_decision_prompt(state: dict, legal: list[str]) -> str:
    """Used when the main reply had no parseable move: force a one-line answer."""
    return "\n".join(
        [
            "Current 2048 board (0 / . = empty):",
            board_text(state["grid"]),
            "",
            f"Score: {state.get('score', 0)}",
            f"Legal moves: {', '.join(legal)}",
            "",
            "Choose one legal move. Output exactly one line and nothing else:",
            "MOVE: <up|down|left|right>",
        ]
    )


def build_retry_prompt(prompt: str, bad_reply: str, legal: list[str]) -> str:
    return "\n".join(
        [
            prompt,
            "",
            "Your previous reply did not contain a valid legal move.",
            f"Legal moves are: {', '.join(legal)}",
            "Answer again. Output exactly one final line, and nothing after it:",
            "MOVE: <up|down|left|right>",
        ]
    )
