"""Helpers that build fake run directories in the same format the Unity package writes."""
from __future__ import annotations

import json
import random
from pathlib import Path

import pytest
from PIL import Image


def make_run(root: Path, run_id: str, events: list[dict], frames: list[tuple[float, Image.Image]] | None = None,
             seed: int = 1) -> Path:
    d = root / run_id
    d.mkdir(parents=True)
    summary = {"run_id": run_id, "seed": seed, "scene": "Assets/Level.unity", "unity": "6000.4.0f1",
               "end_reason": "completed", "game_seconds": 300, "distance_m": 900, "coverage": 0.7,
               "mover": "CharacterController", "navmesh": False, "waypoints": 40}
    (d / "run.json").write_text(json.dumps(summary), encoding="utf-8")
    lines = [json.dumps({"seq": i + 1, **e}) for i, e in enumerate(events)]
    (d / "events.jsonl").write_text("\n".join(lines) + "\n", encoding="utf-8")
    if frames:
        fd = d / "frames"
        fd.mkdir()
        for i, (t, im) in enumerate(frames):
            im.save(fd / f"f{i:05d}_t{t:.2f}.png")
    return d


def textured(seed: int = 0, size=(160, 90)) -> Image.Image:
    """A game-like frame: smooth gradients plus mild noise (pure random noise would contain magenta pixels)."""
    rnd = random.Random(seed)
    w, h = size
    im = Image.new("RGB", size)
    im.putdata([(min(255, x * 255 // w + rnd.randrange(24)), min(255, y * 200 // h + rnd.randrange(24)),
                 40 + rnd.randrange(60)) for y in range(h) for x in range(w)])
    return im


def flat(color=(0, 0, 0), size=(160, 90)) -> Image.Image:
    return Image.new("RGB", size, color)


def stuck_event(t, pos, **data):
    return {"t": t, "type": "stuck", "detail": "no progress for 5s", "pos": pos,
            "data": {"blocked_by": "Wall", "controller": "flags=Sides", **data}}


def error_event(t, message, h="abc123", kind="Exception", count=3):
    return {"t": t, "type": "error_summary", "detail": message, "pos": [1, 0, 1],
            "data": {"hash": h, "kind": kind, "count": str(count), "first_t": str(t), "last_t": str(t + 5)}}


@pytest.fixture
def root(tmp_path):
    return tmp_path
