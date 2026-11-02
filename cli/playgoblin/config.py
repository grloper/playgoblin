"""playgoblin.toml: the ignore list and check settings. Every key is optional."""
from __future__ import annotations

import re
import tomllib
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Config:
    ignore_messages: list[str] = field(default_factory=list)   # regexes matched against error messages
    stuck_radius_m: float = 2.0
    stuck_min_occurrences: int = 2        # a stuck spot seen fewer times is shown as unconfirmed
    merge_radius_m: float = 2.0
    blank_frame_skip_seconds: float = 2.0  # ignore frames this close to a scene change
    pink_area_percent: float = 0.5
    fps_budget_ms: float | None = None

    def ignored(self, message: str) -> bool:
        return any(re.search(p, message) for p in self.ignore_messages)


def load_config(path: Path | None) -> Config:
    if path is None or not Path(path).exists():
        return Config()
    raw = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    cfg = Config()
    checks = raw.get("checks", {})
    cfg.ignore_messages = list(raw.get("ignore", {}).get("messages", []))
    for name in ("stuck_radius_m", "stuck_min_occurrences", "merge_radius_m", "blank_frame_skip_seconds",
                 "pink_area_percent", "fps_budget_ms"):
        if name in checks:
            setattr(cfg, name, checks[name])
    for pattern in cfg.ignore_messages:
        re.compile(pattern)  # fail early on a bad regex
    return cfg
