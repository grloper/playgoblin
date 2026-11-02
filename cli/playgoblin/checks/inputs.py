from __future__ import annotations

from ..model import Finding


def check(run, config):
    """Dead input (the engine side found a control that does nothing) and a player that never moved."""
    seen = set()
    for e in run.events:
        if e.type != "dead_input" or e.detail in seen:
            continue
        seen.add(e.detail)
        kind = str(e.data.get("input", ""))
        yield Finding(
            type="dead_input",
            severity="critical" if kind == "move" else "high",
            title=e.detail or "An input does nothing",
            t=e.t, detail=e.detail, position=e.pos, key="dead_input:" + e.detail,
            data={"input": kind},
        )

    if not any(e.type == "player_found" for e in run.events):
        return
    length = max((e.t for e in run.events), default=0.0)
    pts = [e.pos for e in run.events if e.type == "pos" and e.pos and len(e.pos) >= 3]
    if length <= 20 or not pts:
        return
    xs = [p[0] for p in pts]
    zs = [p[2] for p in pts]
    spread = max(max(xs) - min(xs), max(zs) - min(zs))
    if spread < 1.0:
        yield Finding(
            type="no_movement", severity="critical", title="The player never moved",
            t=0.0, detail=f"{len(pts)} position samples over {length:.0f} s moved at most {spread:.2f} m",
            position=pts[0], key="no_movement", data={"samples": str(len(pts)), "spread_m": f"{spread:.2f}"},
        )
