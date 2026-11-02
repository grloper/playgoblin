"""One module per check. Each is a pure function: (run, config) -> iterable of Finding."""
from __future__ import annotations

from ..config import Config
from ..model import Finding, Run
from . import blank_frames, dead_buttons, exceptions, falls, fps, placeholder, setup, stuck, trapped

ALL = (setup, exceptions, stuck, falls, trapped, dead_buttons, placeholder, fps, blank_frames)


def run_checks(run: Run, config: Config) -> list[Finding]:
    findings: list[Finding] = []
    for check in ALL:
        for f in check.check(run, config):
            f.run_id = run.run_id
            findings.append(f)
    return findings
