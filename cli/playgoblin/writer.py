"""Write a PlayGoblin run folder from any engine or tool (see docs/PROTOCOL.md).

    w = RunWriter("PlayGoblinRuns", seed=1, engine="godot", scene="res://level1.tscn")
    w.event("player_found", pos=(0, 0, 0))
    w.event("stuck", "no progress for 5s", pos=(12.0, 0.0, 3.4), t=61.5, blocked_by="Crate")
    w.end("completed", game_seconds=120, distance_m=410)

Positions are metres, Y up: [x, height, z]. Engines with Z up (Unreal, Blender) must map (x, y, z) to [x, z, y].
`playgoblin report <folder>` then turns the folder into findings, with no engine involved.
"""
from __future__ import annotations

import json
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Sequence


class RunWriter:
    def __init__(self, out_root: str | Path, seed: int = 1, engine: str = "custom", scene: str = "",
                 engine_version: str = "", run_id: str | None = None):
        started = datetime.now(timezone.utc)
        self.run_id = run_id or f"{started:%Y%m%d-%H%M%S}-s{seed}"
        self.directory = Path(out_root) / self.run_id
        self.directory.mkdir(parents=True, exist_ok=True)
        self._seed, self._engine, self._scene, self._version = seed, engine, scene, engine_version
        self._started = started
        self._t0 = time.monotonic()
        self._seq = 0
        self._counts: dict[str, int] = {}
        self._fh = open(self.directory / "events.jsonl", "w", encoding="utf-8")
        self.event("run_start", run_id=self.run_id, seed=seed, engine=engine, scene=scene)

    def event(self, type: str, detail: str | None = None, pos: Sequence[float] | None = None,
              t: float | None = None, **data) -> None:
        """Append one event. `t` is seconds since the run started (defaults to wall time since the writer was created)."""
        self._seq += 1
        e: dict = {"seq": self._seq, "t": round(time.monotonic() - self._t0 if t is None else t, 3), "type": type}
        if detail:
            e["detail"] = detail
        if pos is not None:
            e["pos"] = [round(float(x), 3) for x in pos]
        if data:
            e["data"] = {k: str(v) for k, v in data.items()}
        self._counts[type] = self._counts.get(type, 0) + 1
        self._fh.write(json.dumps(e, ensure_ascii=False) + "\n")
        self._fh.flush()

    def error(self, message: str, count: int = 1, kind: str = "error", first_t: float = 0.0, last_t: float | None = None,
              pos: Sequence[float] | None = None, stack_hash: str | None = None) -> None:
        """Report a grouped error or exception (the exception check reads these `error_summary` events)."""
        import hashlib
        h = stack_hash or hashlib.sha1(message.encode()).hexdigest()[:8]
        self.event("error_summary", message, pos=pos, hash=h, kind=kind, count=count, first_t=first_t,
                   last_t=first_t if last_t is None else last_t)

    def end(self, reason: str = "completed", **summary) -> Path:
        """Write run_end and run.json, close the file and return the run folder."""
        self.event("run_end", reason)
        self._fh.close()
        s = {"run_id": self.run_id, "seed": self._seed, "engine": self._engine, "engine_version": self._version,
             "scene": self._scene, "started_utc": self._started.isoformat(), "end_reason": reason,
             "game_seconds": round(time.monotonic() - self._t0, 3), "event_counts": self._counts}
        s.update(summary)
        (self.directory / "run.json").write_text(json.dumps(s, indent=2), encoding="utf-8")
        return self.directory
