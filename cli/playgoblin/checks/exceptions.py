from __future__ import annotations

from ..model import Finding


def check(run, config):
    """One finding per distinct error (the Unity side already groups by message + stack hash)."""
    for e in run.events:
        if e.type != "error_summary":
            continue
        if config.ignored(e.detail):
            continue
        kind = str(e.data.get("kind", "Error")).capitalize()  # engines write "exception" / "error"
        count = int(e.data.get("count", 1))
        first = e.data.get("first_t")
        first_line = e.detail.splitlines()[0][:120] if e.detail else ""
        yield Finding(
            type="exception",
            severity="critical" if kind == "Exception" else "high",
            title=f"{kind}: {first_line}" if first_line else kind,
            t=float(first) if first is not None else e.t,
            detail=e.detail,
            position=e.pos,
            key=e.data.get("hash", e.detail),
            occurrences=count,
            data={"kind": kind, "count": count, "last_t": e.data.get("last_t")},
        )
