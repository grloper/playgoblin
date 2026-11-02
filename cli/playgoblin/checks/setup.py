from __future__ import annotations

from ..model import Finding


def check(run, config):
    for e in run.events:
        if e.type == "setup_error":
            yield Finding(type="setup_error", severity="critical", title="PlayGoblin could not start walking",
                          t=e.t, detail=e.detail, key="setup:" + e.detail)
