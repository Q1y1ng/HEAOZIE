"""Minimal llama.cpp / OpenAI-compatible client for the 2048 agent.

No third-party dependencies: urllib only.
"""
from __future__ import annotations

import json
import re
import time
import urllib.error
import urllib.request
import uuid

DIRECTIONS = ("up", "down", "left", "right")


class LLMError(RuntimeError):
    pass


def _now_ms() -> float:
    return time.perf_counter() * 1000.0


class LlamaClient:
    def __init__(self, base_url: str, model: str, api_key: str = "local", timeout: float = 300.0):
        self.base_url = base_url.rstrip("/")
        self.model = model
        self.api_key = api_key
        self.timeout = timeout
        self.session_id = uuid.uuid4().hex

    # A browser-like UA is required by the OpenCode Go gateway (Cloudflare 1010 otherwise).
    UA = (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36"
    )
    EXTRA = {"User-Agent": UA, "Origin": "https://opencode.ai", "Referer": "https://opencode.ai/"}

    def _request(self, payload: dict, timeout: float | None = None) -> urllib.request.Request:
        headers = {
            "Content-Type": "application/json",
            "Accept": "text/event-stream, application/json",
            "Authorization": "Bearer " + self.api_key,
            "x-opencode-session": self.session_id,
        }
        headers.update(self.EXTRA)
        return urllib.request.Request(
            self.base_url + "/v1/chat/completions",
            data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
            headers=headers,
            method="POST",
        )

    def health(self) -> dict:
        headers = {"Authorization": "Bearer " + self.api_key, "x-opencode-session": self.session_id}
        headers.update(self.EXTRA)
        req = urllib.request.Request(self.base_url + "/health", headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=10) as r:
                return json.loads(r.read().decode("utf-8"))
        except Exception as exc:  # noqa: BLE001 - surface a useful message
            raise LLMError(
                f"llama-server 未就绪（{self.base_url}）：{exc}\n"
                "先启动 8096（见 README 的启动命令），再跑 play.py。"
            ) from exc

    def chat(self, messages: list[dict], max_tokens: int = 256, temperature: float = 0.3) -> dict:
        payload = {
            "model": self.model,
            "messages": messages,
            "max_tokens": max_tokens,
            "temperature": temperature,
            "stream": False,
        }
        t0 = _now_ms()
        try:
            with urllib.request.urlopen(self._request(payload, self.timeout), timeout=self.timeout) as r:
                resp = json.loads(r.read().decode("utf-8"))
        except Exception as exc:  # noqa: BLE001
            raise LLMError(f"LLM 请求失败：{exc}") from exc
        msg = resp["choices"][0]["message"]
        return {
            "reasoning": msg.get("reasoning_content") or "",
            "content": msg.get("content") or "",
            "usage": resp.get("usage") or {},
            "elapsed_ms": round(_now_ms() - t0, 1),
            "timings": resp.get("timings") or {},
        }

    def chat_stream(
        self,
        messages: list[dict],
        max_tokens: int = 700,
        temperature: float = 0.3,
        seed: int = 1234,
        on_delta=None,
        timeout: float | None = None,
    ) -> dict:
        """Stream one completion; returns the full reasoning/content plus timing.

        on_delta(reasoning_text, content_text) is called at most every ~0.35s,
        and once at the end. It is used to publish the live "thinking" status.
        """
        payload = {
            "model": self.model,
            "messages": messages,
            "max_tokens": max_tokens,
            "temperature": temperature,
            "top_p": 0.9,
            "seed": seed,
            "stream": True,
        }
        t0 = _now_ms()
        reasoning_parts: list[str] = []
        content_parts: list[str] = []
        usage: dict = {}
        chunks = 0
        first_ms: float | None = None
        last_cb = 0.0

        def emit(force: bool = False) -> None:
            nonlocal last_cb
            if on_delta is None:
                return
            now = time.perf_counter()
            if not force and now - last_cb < 0.35:
                return
            last_cb = now
            try:
                on_delta("".join(reasoning_parts), "".join(content_parts))
            except Exception:  # noqa: BLE001 - a status write must never kill the game
                pass

        try:
            with urllib.request.urlopen(self._request(payload, timeout or self.timeout), timeout=timeout or self.timeout) as r:
                for raw in r:
                    line = raw.decode("utf-8", errors="replace").strip()
                    if not line or not line.startswith("data:"):
                        continue
                    data = line[5:].strip()
                    if data == "[DONE]":
                        break
                    try:
                        obj = json.loads(data)
                    except json.JSONDecodeError:
                        continue
                    if obj.get("usage"):
                        usage = obj["usage"]
                    choices = obj.get("choices") or []
                    if not choices:
                        continue
                    delta = choices[0].get("delta") or {}
                    rtxt = delta.get("reasoning_content") or ""
                    ctxt = delta.get("content") or ""
                    if rtxt or ctxt:
                        chunks += 1
                        if first_ms is None:
                            first_ms = _now_ms() - t0
                    if rtxt:
                        reasoning_parts.append(rtxt)
                    if ctxt:
                        content_parts.append(ctxt)
                    if chunks % 8 == 0:
                        emit()
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")[:500]
            raise LLMError(f"LLM HTTP {exc.code}: {body}") from exc
        except Exception as exc:  # noqa: BLE001
            if reasoning_parts or content_parts:
                # Keep whatever streamed in; the caller can decide to use it.
                emit(force=True)
                return {
                    "reasoning": "".join(reasoning_parts),
                    "content": "".join(content_parts),
                    "usage": usage,
                    "elapsed_ms": round(_now_ms() - t0, 1),
                    "first_token_ms": round(first_ms, 1) if first_ms else None,
                    "chunks": chunks,
                    "truncated": True,
                    "error": str(exc),
                }
            raise LLMError(f"LLM 流式请求失败：{exc}") from exc

        emit(force=True)
        return {
            "reasoning": "".join(reasoning_parts),
            "content": "".join(content_parts),
            "usage": usage,
            "elapsed_ms": round(_now_ms() - t0, 1),
            "first_token_ms": round(first_ms, 1) if first_ms else None,
            "chunks": chunks,
            "truncated": False,
            "error": None,
        }


_MOVE_RE = re.compile(r"MOVE\s*[:\-]\s*[^A-Za-z0-9]{0,8}\s*(up|down|left|right)\b", re.IGNORECASE)
_DIR_RE = re.compile(r"\b(up|down|left|right)\b", re.IGNORECASE)
_SHORT_RE = re.compile(r"\b([udlr])\b")
_ZH = (("上", "up"), ("下", "down"), ("左", "left"), ("右", "right"), ("↑", "up"), ("↓", "down"), ("←", "left"), ("→", "right"))


def parse_move(text: str, allow_loose: bool = True) -> tuple[str | None, str | None]:
    """Extract one direction from model output. Returns (direction, how)."""
    if not text:
        return None, None
    stripped = text.strip()
    # Pure JSON is accepted too (useful if someone switches to response_format).
    if stripped.startswith("{"):
        try:
            obj = json.loads(stripped)
            mv = str(obj.get("move", "")).lower()
            if mv in DIRECTIONS:
                return mv, "json"
        except Exception:  # noqa: BLE001
            pass
    m = _MOVE_RE.findall(text)
    if m:
        return m[-1].lower(), "move-line"
    if not allow_loose:
        return None, None
    m = _DIR_RE.findall(text)
    if m:
        return m[-1].lower(), "word"
    m = _SHORT_RE.findall(text)
    if m:
        return {"u": "up", "d": "down", "l": "left", "r": "right"}[m[-1].lower()], "letter"
    for zh, mv in _ZH:
        if zh in text:
            return mv, "zh"
    return None, None


def estimate_tokens(text: str) -> int:
    """Rough token estimate for display/report only (never used for control)."""
    if not text:
        return 0
    # English ~3.6 chars/token; CJK ~1.2 chars/token. This is a mixed heuristic.
    cjk = sum(1 for ch in text if "\u4e00" <= ch <= "\u9fff")
    other = len(text) - cjk
    return max(1, int(other / 3.6 + cjk / 1.2))
