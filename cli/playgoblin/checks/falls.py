from __future__ import annotations

from ..model import Finding

TITLES = {
    "below_world": "Player fell out of the world",
    "invalid_position": "Player position became invalid (NaN/Infinity)",
    "off_navmesh": "Player left the NavMesh",
}


def check(run, config):
    for e in run.events:
        if e.type != "out_of_world":
            continue
        reason = e.data.get("reason", e.detail)
        yield Finding(type="out_of_world", severity="critical", title=TITLES.get(reason, "Player left the level"),
                      t=e.t, detail=f"{reason}; ended up at {e.data.get('fell_to', '?')}", position=e.pos,
                      data=dict(e.data))
