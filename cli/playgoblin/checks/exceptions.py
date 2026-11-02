from __future__ import annotations

from ..model import Finding


def check(run, config):
    """One finding per distinct error (the Unity side already groups by message + stack hash)."""
    unreal = any(e.type == "run_start" and e.data.get("engine") == "unreal" for e in run.events) \
        or run.summary.get("engine") == "unreal"
    errors = [e for e in run.events if e.type == "error_summary" and not config.ignored(e.detail)]

    def startup(e):
        first = e.data.get("first_t")
        return e.detail.startswith("LogPython:") and float(first if first is not None else e.t) <= 1.0

    noise = []
    if unreal:
        cand = [e for e in errors if startup(e)]
        if any("/Engine/Plugins/" in e.detail for e in cand):
            noise = cand
    if noise:
        yield Finding(
            type="engine_noise", severity="low",
            title=f"{len(noise)} engine plugin startup errors (Epic Python toolsets), not game bugs",
            t=min(e.t for e in noise), detail="\n".join(e.detail for e in noise[:5]),
            key="engine_noise", occurrences=len(noise), data={"count": str(len(noise))},
        )
    skip = {id(e) for e in noise}
    for e in errors:
        if id(e) in skip:
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
