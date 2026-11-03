import json

from playgoblin.config import Config
from playgoblin.pipeline import analyze
from conftest import make_run


def _report(root, name, summary_patch=None, events=None):
    d = make_run(root, name, events or [])
    if summary_patch is not None:
        p = d / "run.json"
        s = json.loads(p.read_text(encoding="utf-8"))
        s.update(summary_patch)
        for k, v in list(s.items()):
            if v is None:
                del s[k]
        p.write_text(json.dumps(s), encoding="utf-8")
    _, paths = analyze([d], root / f"rep-{name}", Config())
    return paths


def test_report_shows_coverage_and_cells(root):
    paths = _report(root, "a", {"coverage": 0.7275, "visited_cells": 290, "reachable_cells": 400})
    md = paths["md"].read_text(encoding="utf-8")
    assert "73% covered (290/400 cells)" in md and "explored part" not in md
    assert "73% covered (290/400 cells)" in paths["html"].read_text(encoding="utf-8")
    run = json.loads(paths["json"].read_text(encoding="utf-8"))["runs"][0]
    assert run["coverage"] == 0.7275 and run["visited_cells"] == 290 and run["reachable_cells"] == 400


def test_low_coverage_is_flagged(root):
    paths = _report(root, "b", {"coverage": 0.05})
    assert "5% covered" in paths["md"].read_text(encoding="utf-8")
    assert "findings only cover the explored part" in paths["md"].read_text(encoding="utf-8")


def test_coverage_falls_back_to_run_end_event(root):
    end = {"t": 60, "type": "run_end", "detail": "completed",
           "data": {"coverage": "0.4200", "visited_cells": "168", "reachable_cells": "400"}}
    paths = _report(root, "c", {"coverage": None}, [end])
    assert "42% covered" in paths["md"].read_text(encoding="utf-8")


def test_missing_coverage_prints_question_mark(root):
    paths = _report(root, "d", {"coverage": None})
    assert "? covered" in paths["md"].read_text(encoding="utf-8")
