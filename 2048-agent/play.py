"""Drive the 2048 agent copy with a local llama.cpp model.

Usage (the Playwright venv is required):
  E:\\AI\\tools\\playwright\\.venv\\Scripts\\python.exe play.py --policy greedy --max-moves 12
  E:\\AI\\tools\\playwright\\.venv\\Scripts\\python.exe play.py --policy llm --max-moves 12
  E:\\AI\\tools\\playwright\\.venv\\Scripts\\python.exe play.py --policy llm --max-moves 800 --max-minutes 120

The game copy is only read/driven; the original 2048/ is untouched.
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import random
import re
import sys
import time
import traceback
from datetime import datetime

_BROWSERS = r"E:\AI\tools\playwright\browsers"
if os.path.isdir(_BROWSERS):
    os.environ["PLAYWRIGHT_BROWSERS_PATH"] = _BROWSERS

from playwright.sync_api import sync_playwright  # noqa: E402

from llm import LlamaClient, LLMError, estimate_tokens, parse_move  # noqa: E402
import prompts  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent
GAME_URL = (ROOT / "game" / "index.html").as_uri()
KEYS = {"up": "ArrowUp", "down": "ArrowDown", "left": "ArrowLeft", "right": "ArrowRight"}


def now_iso() -> str:
    return datetime.now().astimezone().isoformat(timespec="seconds")


STATE_JS = r"""
() => {
  const cells = Array.from(document.querySelectorAll('#board .cell'));
  const n = Math.round(Math.sqrt(cells.length));
  const grid = [];
  for (let r = 0; r < n; r++) {
    grid.push(cells.slice(r * n, (r + 1) * n).map(e => e.textContent ? Number(e.textContent) : 0));
  }
  const overlay = document.getElementById('overlay');
  return {
    size: n,
    grid: grid,
    score: Number(document.getElementById('score').textContent),
    best: Number(document.getElementById('best').textContent),
    maxTile: Math.max(...grid.flat()),
    empties: grid.flat().filter(v => v === 0).length,
    overlay: overlay.classList.contains('show'),
    overlayTitle: document.getElementById('overlay-title').textContent,
    canMove: window.Game2048.canMove(grid)
  };
}
"""

CANDIDATES_JS = r"""
(grid) => {
  const out = {};
  for (const d of ['up', 'down', 'left', 'right']) {
    const r = window.Game2048.move(grid, d);
    if (!r.moved) continue;
    out[d] = {
      grid: r.grid,
      gain: r.score,
      empties: window.Game2048.emptyCells(r.grid).length,
      canMove: window.Game2048.canMove(r.grid)
    };
  }
  return out;
}
"""


class GameDriver:
    def __init__(self, browser, seed: int, size: int = 4, spawn: int = 1):
        self.browser = browser
        self.context = browser.new_context(viewport={"width": 900, "height": 1100}, device_scale_factor=1)
        self.page = self.context.new_page()
        self.page.set_default_timeout(15000)
        url = f"{GAME_URL}?seed={seed}&size={size}&spawn={spawn}"
        self.page.goto(url, wait_until="load")
        self.page.wait_for_selector("#board .cell")
        self.page.wait_for_timeout(80)

    def state(self) -> dict:
        return self.page.evaluate(STATE_JS)

    def candidates(self, state: dict) -> dict:
        return self.page.evaluate(CANDIDATES_JS, state["grid"])

    def move(self, direction: str) -> tuple[dict, bool]:
        self.page.keyboard.press(KEYS[direction])
        self.page.wait_for_timeout(20)
        st = self.state()
        won = False
        if st["overlay"] and st["canMove"]:
            # win overlay: dismiss it so the agent can continue
            self.page.click("#btn-overlay-continue")
            self.page.wait_for_timeout(20)
            won = True
            st = self.state()
        return st, won

    def screenshot(self, path: pathlib.Path) -> None:
        self.page.screenshot(path=str(path), full_page=True)

    def close(self) -> None:
        self.context.close()


class Recorder:
    def __init__(self, run_dir: pathlib.Path, meta: dict):
        self.dir = run_dir
        self.dir.mkdir(parents=True, exist_ok=True)
        self.meta = meta
        self.meta_path = self.dir / "meta.json"
        self.steps_path = self.dir / "run.jsonl"
        self.status_path = self.dir / "status.json"
        self.steps_path.write_text("", encoding="utf-8")
        self.write_meta()
        self.write_status({"phase": "starting"})

    @staticmethod
    def _atomic_write(path: pathlib.Path, text: str, fsync: bool = True) -> None:
        tmp = path.with_suffix(path.suffix + ".tmp")
        with open(tmp, "w", encoding="utf-8") as f:
            f.write(text)
            f.flush()
            if fsync:
                os.fsync(f.fileno())
        os.replace(tmp, path)

    def write_meta(self) -> None:
        self._atomic_write(self.meta_path, json.dumps(self.meta, ensure_ascii=False, indent=2))

    def update_meta(self, **kw) -> None:
        self.meta.update(kw)
        self.write_meta()

    def append_step(self, step: dict) -> None:
        with open(self.steps_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(step, ensure_ascii=False) + "\n")
            f.flush()
            os.fsync(f.fileno())

    def write_status(self, obj: dict) -> None:
        obj = dict(obj)
        obj["updated_at"] = now_iso()
        self._atomic_write(self.status_path, json.dumps(obj, ensure_ascii=False), fsync=False)


def greedy_choice(candidates: dict, legal: list[str]) -> str:
    def key(d: str):
        c = candidates[d]
        return (1 if c.get("canMove") else 0, c.get("gain", 0), c.get("empties", 0))

    return max(legal, key=key)


def slug(text: str) -> str:
    return re.sub(r"[^A-Za-z0-9._-]+", "-", text).strip("-") or "run"


def main() -> int:
    ap = argparse.ArgumentParser(description="2048 agent driver")
    ap.add_argument("--policy", choices=["llm", "greedy", "random"], default="llm")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--size", type=int, default=4)
    ap.add_argument("--spawn", type=int, default=1)
    ap.add_argument("--max-moves", type=int, default=800)
    ap.add_argument("--max-minutes", type=float, default=120.0)
    ap.add_argument("--model", default="2048-4b")
    ap.add_argument("--base-url", default="http://127.0.0.1:8096")
    ap.add_argument("--api-key", default="local")
    ap.add_argument("--credentials", default=None, help="YAML 文件，从中读取 OPENCODE_GO_API_KEY（避免 key 进命令行）")
    ap.add_argument("--skip-health", action="store_true")
    ap.add_argument("--no-stream", action="store_true", help="use non-streaming chat (for gateways without SSE)")
    ap.add_argument("--provider", default=None)
    ap.add_argument("--temperature", type=float, default=0.3)
    ap.add_argument("--max-tokens", type=int, default=700)
    ap.add_argument("--plain", action="store_true", help="do not show candidate boards (harder mode)")
    ap.add_argument("--headed", action="store_true")
    ap.add_argument("--run-id", default=None)
    ap.add_argument("--out", default=None)
    ap.add_argument("--notes", default="")
    args = ap.parse_args()

    api_key = args.api_key
    if args.credentials:
        cred_raw = open(args.credentials, "r", encoding="utf-8", errors="replace").read()
        m = re.search(r"^\s*OPENCODE_GO_API_KEY\s*:\s*(\S+)\s*$", cred_raw, re.M)
        if not m:
            print(f"OPENCODE_GO_API_KEY not found in {args.credentials}", file=sys.stderr)
            return 2
        api_key = m.group(1).strip().strip('"').strip("'")
    provider = args.provider or ("cloud" if "opencode" in args.base_url else "local")

    run_id = args.run_id or (time.strftime("%Y%m%d-%H%M%S") + f"-{slug(args.model)}-s{args.seed}")
    if args.out:
        run_dir = pathlib.Path(args.out).resolve()
    else:
        run_dir = ROOT / "runs" / run_id
    if run_dir.exists() and any(run_dir.iterdir()):
        print(f"Refusing to overwrite non-empty run dir: {run_dir}", file=sys.stderr)
        return 2

    meta = {
        "run_id": run_id,
        "created_at": now_iso(),
        "started_at": now_iso(),
        "status": "starting",
        "model": args.model,
        "provider": provider,
        "base_url": args.base_url,
        "seed": args.seed,
        "size": args.size,
        "spawn_count": args.spawn,
        "thinking": True,
        "policy": args.policy,
        "assisted": not args.plain,
        "prompt_version": "assisted-v1" if not args.plain else "plain-v1",
        "temperature": args.temperature,
        "max_tokens": args.max_tokens,
        "max_moves": args.max_moves,
        "max_minutes": args.max_minutes,
        "game_source": str(ROOT / "game" / "index.html"),
        "notes": args.notes,
        "initial_grid": None,
        "final": None,
        "moves_done": 0,
        "score": 0,
        "max_tile": 0,
    }
    rec = Recorder(run_dir, meta)
    print(f"run dir: {run_dir}")

    client = None
    if args.policy == "llm":
        client = LlamaClient(args.base_url, args.model, api_key)
        if args.skip_health:
            print("LLM health: skipped (--skip-health)")
        else:
            try:
                health = client.health()
                rec.update_meta(server_health=health)
                print(f"LLM health: {health}")
            except LLMError as exc:
                rec.update_meta(status="error", error=str(exc), finished_at=now_iso())
                print(str(exc), file=sys.stderr)
                return 2

    rng = random.Random(args.seed * 7919 + 1)
    started = time.time()
    moves = 0
    history: list[dict] = []
    reached = False
    status = "running"
    final_state = None
    driver = None
    browser = None
    pw = None

    def finish(reason: str) -> None:
        nonlocal status
        status = reason

    try:
        pw = sync_playwright().start()
        browser = pw.chromium.launch(headless=not args.headed, args=["--disable-gpu"])
        driver = GameDriver(browser, args.seed, args.size, args.spawn)
        st0 = driver.state()
        rec.update_meta(initial_grid=st0["grid"], size=st0["size"], empties=st0["empties"])
        driver.screenshot(rec.dir / "start.png")
        print(f"initial: score={st0['score']} max={st0['maxTile']} seed={args.seed}")

        while moves < args.max_moves:
            if time.time() - started > args.max_minutes * 60:
                finish("time_limit")
                break
            st = driver.state()
            if not st["canMove"]:
                finish("lost")
                final_state = st
                break
            cand = driver.candidates(st)
            legal = list(cand.keys())
            if not legal:
                finish("lost")
                final_state = st
                break

            prompt = prompts.build_prompt(st, cand, history, assisted=not args.plain)
            reasoning = ""
            content = ""
            llm_meta: dict = {}
            retries = 0
            fallback = False
            move = None
            how = None
            won = False

            if args.policy == "random":
                move = rng.choice(legal)
                how = "random"
            elif args.policy == "greedy":
                move = greedy_choice(cand, legal)
                how = "greedy"
            else:
                rec.write_status(
                    {
                        "phase": "thinking",
                        "step": moves + 1,
                        "model": args.model,
                        "seed": args.seed,
                        "reasoning": "",
                        "content": "",
                    }
                )

                def on_delta(rt: str, ct: str) -> None:
                    rec.write_status(
                        {
                            "phase": "thinking",
                            "step": moves + 1,
                            "model": args.model,
                            "seed": args.seed,
                            "reasoning": rt[-12000:],
                            "content": ct[-2000:],
                        }
                    )

                messages_main = [
                    {"role": "system", "content": prompts.SYSTEM},
                    {"role": "user", "content": prompt},
                ]
                try:
                    if args.no_stream:
                        res = client.chat(
                            messages=messages_main,
                            max_tokens=args.max_tokens,
                            temperature=args.temperature,
                        )
                        res.setdefault("chunks", None)
                        res.setdefault("first_token_ms", None)
                        res.setdefault("truncated", False)
                    else:
                        res = client.chat_stream(
                            messages=messages_main,
                            max_tokens=args.max_tokens,
                            temperature=args.temperature,
                            seed=args.seed * 100000 + moves,
                            on_delta=on_delta,
                        )
                    reasoning = res["reasoning"]
                    content = res["content"]
                    llm_meta = {
                        "elapsed_ms": res.get("elapsed_ms"),
                        "first_token_ms": res.get("first_token_ms"),
                        "chunks": res.get("chunks"),
                        "truncated": res.get("truncated"),
                        "transport": "chat" if args.no_stream else "stream",
                    }
                    if res.get("usage"):
                        llm_meta["usage"] = res["usage"]
                    if not reasoning and content and len(content) > 40:
                        # Cloud models often have no separate CoT field; keep their reply as the trace.
                        reasoning = content
                        llm_meta["reasoning_source"] = "content"
                    move, how = parse_move(content)
                    if move not in legal:
                        # Only accept a MOVE: line from the reasoning tail, not a random word.
                        move2, how2 = parse_move(reasoning[-1500:], allow_loose=False)
                        if move2 in legal:
                            move, how = move2, (how2 or "reasoning") + "-from-reasoning"
                except LLMError as exc:
                    llm_meta["stream_error"] = str(exc)
                    print(f"LLM stream error at step {moves + 1}: {exc}; trying non-streaming", file=sys.stderr)
                    try:
                        res = client.chat(
                            messages=[
                                {"role": "system", "content": prompts.SYSTEM},
                                {"role": "user", "content": prompt},
                            ],
                            max_tokens=args.max_tokens,
                            temperature=args.temperature,
                        )
                        reasoning = res["reasoning"]
                        content = res["content"]
                        llm_meta = {
                            "elapsed_ms": res.get("elapsed_ms"),
                            "transport": "chat",
                            "usage": res.get("usage"),
                        }
                        if not reasoning and content and len(content) > 40:
                            reasoning = content
                            llm_meta["reasoning_source"] = "content"
                        move, how = parse_move(content)
                        if move not in legal:
                            move2, how2 = parse_move(reasoning[-1500:], allow_loose=False)
                            if move2 in legal:
                                move, how = move2, (how2 or "reasoning") + "-from-reasoning"
                    except LLMError as exc2:
                        llm_meta["error"] = str(exc2)
                        print(f"LLM error at step {moves + 1}: {exc2}", file=sys.stderr)

                if move not in legal:
                    retries += 1
                    rec.write_status(
                        {
                            "phase": "retry",
                            "step": moves + 1,
                            "model": args.model,
                            "seed": args.seed,
                            "reasoning": reasoning[-4000:],
                            "content": content,
                        }
                    )
                    try:
                        res2 = client.chat_stream(
                            messages=[
                                {"role": "system", "content": prompts.DECISION_SYSTEM},
                                {"role": "user", "content": prompts.build_decision_prompt(st, legal)},
                            ],
                            max_tokens=args.max_tokens,
                            temperature=0.0,
                            seed=args.seed * 100000 + moves + 1,
                        )
                        move2, how2 = parse_move(res2.get("content", ""))
                        if move2 not in legal:
                            move2, how2 = parse_move(res2.get("reasoning", "")[-1000:], allow_loose=False)
                        if move2 in legal:
                            move, how = move2, "decision-retry"
                            if res2.get("reasoning"):
                                reasoning = (reasoning + "\n\n[decision retry]\n" + res2["content"]).strip()
                            else:
                                reasoning = (reasoning + "\n\n[decision retry]").strip()
                            content = res2.get("content", "") or content
                    except LLMError as exc:
                        llm_meta["retry_error"] = str(exc)
                        print(f"LLM retry error at step {moves + 1}: {exc}", file=sys.stderr)

                if move not in legal:
                    move = greedy_choice(cand, legal)
                    how = "greedy-fallback"
                    fallback = True

            before = st
            after, won = driver.move(move)
            if won:
                reached = True
            gained = after["score"] - before["score"]
            move_only = cand[move]["grid"]
            spawned = []
            for r in range(st["size"]):
                for c in range(st["size"]):
                    if after["grid"][r][c] != move_only[r][c]:
                        spawned.append({"r": r, "c": c, "v": after["grid"][r][c]})

            step = {
                "i": moves + 1,
                "ts": now_iso(),
                "move": move,
                "how": how,
                "gained": gained,
                "score_before": before["score"],
                "score": after["score"],
                "max_tile": after["maxTile"],
                "empties_before": before["empties"],
                "grid_before": before["grid"],
                "grid_after": after["grid"],
                "grid_move_only": move_only,
                "spawned": spawned,
                "legal": legal,
                "candidates": {
                    d: {
                        "grid": c["grid"],
                        "gain": c["gain"],
                        "empties": c["empties"],
                        "canMove": c["canMove"],
                    }
                    for d, c in cand.items()
                },
                "reasoning": reasoning,
                "content": content,
                "retries": retries,
                "fallback": fallback,
                "latency_ms": llm_meta.get("elapsed_ms"),
                "first_token_ms": llm_meta.get("first_token_ms"),
                "prompt_chars": len(prompt),
                "reasoning_chars": len(reasoning),
                "content_chars": len(content),
                "prompt_tokens_est": estimate_tokens(prompt),
                "completion_tokens_est": estimate_tokens(
                    reasoning if reasoning == content else (reasoning + "\n" + content)
                ),
                "reasoning_source": llm_meta.get("reasoning_source", "reasoning_content" if reasoning else None),
                "usage": llm_meta.get("usage"),
                "won": won,
                "overlay_after": after["overlay"],
                "can_move_after": after["canMove"],
            }
            if llm_meta.get("error"):
                step["llm_error"] = llm_meta["error"]
            rec.append_step(step)
            moves += 1
            history.append({"i": moves, "move": move, "gain": gained, "score": after["score"]})
            rec.update_meta(moves_done=moves, score=after["score"], max_tile=after["maxTile"], status="running")
            print(
                f"[{moves:3d}] {move:5s} +{gained:<5d} score={after['score']:<6d} "
                f"max={after['maxTile']:<5d} {('%.1fs' % (llm_meta.get('elapsed_ms', 0) / 1000.0)):>7s}"
                + ("  FALLBACK" if fallback else "")
                + ("  WIN" if won else "")
                + ("  RETRY" if retries else ""),
                flush=True,
            )

            if after["overlay"] and not after["canMove"]:
                finish("lost")
                final_state = after
                break
        else:
            finish("max_moves")
    except KeyboardInterrupt:
        finish("aborted")
        print("Interrupted.", file=sys.stderr)
    except Exception as exc:  # noqa: BLE001
        finish("error")
        print(f"ERROR: {exc}", file=sys.stderr)
        traceback.print_exc()
    finally:
        final_state = final_state or (driver.state() if driver else None)
        if driver is not None and final_state is not None:
            try:
                path = rec.dir / "final.png"
                driver.screenshot(path)
            except Exception:  # noqa: BLE001
                pass
        if driver is not None:
            try:
                driver.close()
            except Exception:  # noqa: BLE001
                pass
        if browser is not None:
            try:
                browser.close()
            except Exception:  # noqa: BLE001
                pass
        if pw is not None:
            try:
                pw.stop()
            except Exception:  # noqa: BLE001
                pass

        final = {
            "status": status,
            "score": final_state["score"] if final_state else None,
            "max_tile": final_state["maxTile"] if final_state else None,
            "moves": moves,
            "reached_2048": reached,
            "last_grid": final_state["grid"] if final_state else None,
            "ended_at": now_iso(),
            "seconds": round(time.time() - started, 1),
        }
        rec.update_meta(status=status, finished_at=now_iso(), final=final, moves_done=moves, max_tile=final.get("max_tile") or 0)
        rec.write_status({"phase": "done", "step": moves, "status": status, "move": None})
        print(json.dumps({"run_dir": str(rec.dir), "status": status, "final": final}, ensure_ascii=False, indent=2))

    return 0 if status in ("lost", "max_moves", "time_limit") else 1


if __name__ == "__main__":
    sys.exit(main())
