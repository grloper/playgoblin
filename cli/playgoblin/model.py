"""Data shared by the checks, dedupe and report. Plain dataclasses so findings.json is easy to produce."""
from __future__ import annotations

import json
import math
from dataclasses import asdict, dataclass, field
from pathlib import Path

SEVERITY_ORDER = {"critical": 0, "high": 1, "medium": 2, "low": 3}
SEVERITY_ICON = {"critical": "🔴", "high": "🟠", "medium": "🟡", "low": "⚪"}


@dataclass
class Finding:
    type: str
    severity: str
    title: str
    t: float
    detail: str = ""
    position: list[float] | None = None
    key: str = ""                       # identity for exact merges (e.g. a stack hash)
    run_id: str = ""
    evidence: list[str] = field(default_factory=list)   # paths relative to the report directory
    data: dict = field(default_factory=dict)
    # Filled in by dedupe:
    occurrences: int = 1
    runs_seen: int = 1
    runs_total: int = 1
    confidence: str = "medium"          # "high" | "medium" | "low"
    confirmed: bool = True              # False: shown under "Unconfirmed", never dropped silently

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class Event:
    seq: int
    t: float
    type: str
    detail: str = ""
    pos: list[float] | None = None
    data: dict = field(default_factory=dict)

    @staticmethod
    def from_json(d: dict) -> "Event":
        return Event(
            seq=int(d.get("seq", 0)),
            t=float(d.get("t", 0.0)),
            type=str(d.get("type", "")),
            detail=str(d.get("detail", "") or ""),
            pos=d.get("pos"),
            data=d.get("data") or {},
        )


@dataclass
class Run:
    """One finished PlayGoblin run: its summary (run.json), its events, and where its frames live."""
    summary: dict
    events: list[Event]
    directory: Path | None = None

    @property
    def run_id(self) -> str:
        return str(self.summary.get("run_id", ""))

    @property
    def frames_dir(self) -> Path | None:
        return self.directory / "frames" if self.directory else None


def load_run(directory: Path) -> Run:
    """Load a run directory written by the Unity package (run.json + events.jsonl [+ frames/])."""
    directory = Path(directory)
    summary_path = directory / "run.json"
    events_path = directory / "events.jsonl"
    if not summary_path.exists() or not events_path.exists():
        raise FileNotFoundError(f"{directory} is not a PlayGoblin run (needs run.json and events.jsonl)")
    summary = json.loads(summary_path.read_text(encoding="utf-8"))
    events: list[Event] = []
    for line in events_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            events.append(Event.from_json(json.loads(line)))
        except (json.JSONDecodeError, ValueError):
            continue  # a truncated last line from a crashed run must not kill the report
    return Run(summary=summary, events=events, directory=directory)


def distance_xz(a: list[float] | None, b: list[float] | None) -> float:
    if not a or not b:
        return math.inf
    return math.hypot(a[0] - b[0], a[2] - b[2])
