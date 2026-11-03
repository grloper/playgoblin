import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

import pytest

from playgoblin import evidence, frames
from playgoblin.checks import run_checks
from playgoblin.cli import main
from playgoblin.client import PlayGoblinError, Client
from playgoblin.config import Config, load_config
from playgoblin.model import load_run
from playgoblin.pipeline import analyze
from conftest import error_event, flat, make_run, stuck_event, textured


def findings_of(root, *dirs, config=None):
    f, _ = analyze(list(dirs), root / "out", config or Config())
    return f


def test_exception_becomes_a_critical_finding(root):
    d = make_run(root, "r1", [error_event(12, "NullReferenceException: boom\n at Foo.Bar()")])
    (f,) = findings_of(root, d)
    assert f.type == "exception" and f.severity == "critical" and f.occurrences == 3
    assert f.title.startswith("Exception: NullReferenceException")


def test_ignore_list_hides_matching_errors(root, tmp_path):
    cfg = tmp_path / "playgoblin.toml"
    cfg.write_text('[ignore]\nmessages = ["boom"]\n', encoding="utf-8")
    d = make_run(root, "r1", [error_event(12, "NullReferenceException: boom")])
    assert findings_of(root, d, config=load_config(cfg)) == []


def test_bad_ignore_regex_fails_early(tmp_path):
    cfg = tmp_path / "playgoblin.toml"
    cfg.write_text('[ignore]\nmessages = ["(unclosed"]\n', encoding="utf-8")
    with pytest.raises(Exception):
        load_config(cfg)


def test_same_error_in_two_runs_is_merged_and_counted(root):
    a = make_run(root, "r1", [error_event(10, "NRE", h="h1", count=2)])
    b = make_run(root, "r2", [error_event(40, "NRE", h="h1", count=5)])
    (f,) = findings_of(root, a, b)
    assert f.runs_seen == 2 and f.runs_total == 2 and f.occurrences == 7


def test_lone_stuck_is_unconfirmed_but_kept(root):
    d = make_run(root, "r1", [stuck_event(30, [5, 0, 5])])
    (f,) = findings_of(root, d)
    assert f.type == "stuck" and not f.confirmed and f.confidence == "low"


def test_repeated_stuck_in_same_place_is_confirmed(root):
    d = make_run(root, "r1", [stuck_event(30, [5, 0, 5]), stuck_event(80, [5.8, 0, 5.5])])
    (f,) = findings_of(root, d)
    assert f.confirmed and f.occurrences == 2


def test_stuck_in_two_runs_is_confirmed_and_high_confidence(root):
    a = make_run(root, "r1", [stuck_event(30, [5, 0, 5])])
    b = make_run(root, "r2", [stuck_event(55, [5.5, 0, 4.5])])
    (f,) = findings_of(root, a, b)
    assert f.confirmed and f.confidence == "high" and f.runs_seen == 2


def test_stuck_spots_far_apart_stay_separate(root):
    d = make_run(root, "r1", [stuck_event(30, [5, 0, 5]), stuck_event(80, [40, 0, 40])])
    assert len(findings_of(root, d)) == 2


def test_fall_and_trapped_findings(root):
    d = make_run(root, "r1", [
        {"t": 0, "type": "trapped", "pos": [0, 1, 0],
         "data": {"reachable_cells": "4", "walkable_cells": "900", "cell_m": "1"}},
        {"t": 50, "type": "out_of_world", "detail": "below_world", "pos": [9, 1, 9],
         "data": {"reason": "below_world", "fell_to": "9,-40,9"}},
    ])
    kinds = {f.type: f for f in findings_of(root, d)}
    assert kinds["out_of_world"].severity == "critical"
    assert kinds["trapped"].severity == "high"


def test_setup_error_is_reported(root):
    d = make_run(root, "r1", [{"t": 0, "type": "setup_error", "detail": "No PlayGoblinPlayer found."}])
    (f,) = findings_of(root, d)
    assert f.type == "setup_error" and f.severity == "critical"


def test_clean_run_has_no_findings_and_says_so(root):
    d = make_run(root, "r1", [{"t": 1, "type": "pos", "pos": [0, 0, 0]}], frames=[(1.0, textured(1))])
    findings, paths = analyze([d], root / "out", Config())
    assert findings == []
    assert "No findings" in paths["md"].read_text(encoding="utf-8")


def test_truncated_last_event_line_does_not_break_loading(root):
    d = make_run(root, "r1", [error_event(5, "x")])
    with open(d / "events.jsonl", "a", encoding="utf-8") as fh:
        fh.write('{"seq": 9, "t": 3, "ty')
    assert len(load_run(d).events) == 1


def test_not_a_run_directory_is_a_clear_error(root):
    with pytest.raises(FileNotFoundError):
        load_run(root)


# ---- frames ----

@pytest.mark.skipif(not frames.available(), reason="needs numpy and Pillow")
class TestFrames:
    def test_black_screen_is_blank_but_dark_textured_scene_is_not(self, root):
        dark = textured(3)
        dark = dark.point(lambda v: v // 12)  # very dark but with grain
        d = make_run(root, "r1", [], frames=[(10.0, flat((0, 0, 0))), (11.0, flat((0, 0, 0))), (30.0, dark)])
        (f,) = findings_of(root, d)
        assert f.type == "blank_frame" and f.data["frames"] == 2

    def test_flat_gray_sky_screen_is_blank(self, root):
        d = make_run(root, "r1", [], frames=[(5.0, flat((128, 128, 128)))])
        assert findings_of(root, d)[0].type == "blank_frame"

    def test_blank_frames_next_to_a_scene_change_are_skipped(self, root):
        d = make_run(root, "r1", [{"t": 10, "type": "scene_change", "detail": "Level2"}],
                     frames=[(10.5, flat((0, 0, 0)))])
        assert findings_of(root, d) == []

    def test_magenta_area_is_a_debug_leftover(self, root):
        im = textured(7)
        for x in range(60):
            for y in range(40):
                im.putpixel((x, y), (255, 0, 255))
        d = make_run(root, "r1", [], frames=[(8.0, im)])
        (f,) = findings_of(root, d)
        assert f.type == "debug_leftover"

    def test_brand_pink_walls_are_not_missing_material(self, root):
        im = textured(9)
        for x in range(60):
            for y in range(40):
                im.putpixel((x, y), (255, 77, 184))  # #FF4DB8, lit
        shaded = flat((150, 46, 110), size=(160, 90))  # shaded #FF4DB8
        d = make_run(root, "r1", [], frames=[(8.0, im), (9.0, shaded)])
        assert [f for f in findings_of(root, d) if f.type == "debug_leftover"] == []

    def test_blank_frames_far_apart_merge_into_one_counted_finding(self, root):
        d = make_run(root, "r1", [], frames=[(5.0, flat()), (60.0, flat())])
        (f,) = findings_of(root, d)
        assert f.occurrences == 2 and len(f.evidence) >= 2


# ---- evidence + report ----

@pytest.mark.skipif(not frames.available(), reason="needs numpy and Pillow")
def test_findings_get_nearby_frames_and_a_clip(root):
    fr = [(float(t), textured(t)) for t in range(0, 20)]
    d = make_run(root, "r1", [error_event(10, "NRE")], frames=fr)
    out = root / "out"
    found, paths = analyze([d], out, Config())
    (f,) = found
    pics = [e for e in f.evidence if e.endswith(".png")]
    assert 3 <= len(pics) <= 12
    assert all((out / e).is_file() for e in f.evidence)
    if evidence.ffmpeg_path():
        clip = out / f.evidence[0]
        assert clip.suffix == ".mp4" and clip.stat().st_size > 1000
    html = paths["html"].read_text(encoding="utf-8")
    assert "F1" in html and "evidence/F1" in html


def test_report_files_are_self_consistent(root):
    d = make_run(root, "r1", [error_event(10, "NRE <b>x</b>"), stuck_event(30, [5, 0, 5])])
    findings, paths = analyze([d], root / "out", Config())
    data = json.loads(paths["json"].read_text(encoding="utf-8"))
    assert [f["data"]["id"] for f in data["findings"]] == [f.data["id"] for f in findings]
    md = paths["md"].read_text(encoding="utf-8")
    assert "Unconfirmed" in md and "F1" in md
    html = paths["html"].read_text(encoding="utf-8")
    assert "<b>x</b>" not in html and "&lt;b&gt;x&lt;/b&gt;" in html  # game text must be escaped


def test_findings_are_ordered_by_severity_then_confirmation(root):
    d = make_run(root, "r1", [stuck_event(30, [5, 0, 5]), stuck_event(60, [5, 0, 5]), error_event(10, "NRE")])
    findings = findings_of(root, d)
    assert [f.type for f in findings] == ["exception", "stuck"]


# ---- CLI + client ----

def test_cli_report_end_to_end(root, capsys):
    d = make_run(root, "r1", [error_event(10, "NRE")])
    out = root / "rep"
    assert main(["report", str(d), "--out", str(out)]) == 0
    assert (out / "report.html").is_file() and (out / "findings.json").is_file()
    assert "1 finding" in capsys.readouterr().out


def test_cli_report_on_a_bad_path_exits_nonzero(root, capsys):
    assert main(["report", str(root / "nope"), "--out", str(root / "o")]) == 2
    assert "not a PlayGoblin run" in capsys.readouterr().err


class FakeUnity(BaseHTTPRequestHandler):
    run_dir = None
    polls = 0

    def log_message(self, *a):
        pass

    def _send(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/status":
            FakeUnity.polls += 1
            ended = FakeUnity.polls >= 3
            self._send(200, {"state": "ended" if ended else "running", "run_id": "r1", "t": 5, "events": 3,
                             "end_reason": "completed", "run_dir": str(FakeUnity.run_dir)})
        else:
            self._send(404, {"error": "not_found"})

    def do_POST(self):
        n = int(self.headers.get("Content-Length", 0))
        body = json.loads(self.rfile.read(n) or b"{}")
        if self.path == "/run/start":
            self._send(202 if body.get("minutes", 0) > 0 else 400, {"state": "running"})
        else:
            self._send(404, {"error": "not_found"})


@pytest.fixture
def fake_unity(root):
    FakeUnity.run_dir = make_run(root / "unity", "r1", [error_event(10, "NRE")])
    FakeUnity.polls = 0
    srv = HTTPServer(("127.0.0.1", 0), FakeUnity)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    yield srv.server_address[1]
    srv.shutdown()


def test_cli_run_talks_to_the_api_and_writes_a_report(root, fake_unity, monkeypatch):
    import playgoblin.client as c
    monkeypatch.setattr(c.time, "sleep", lambda s: None)
    out = root / "rep"
    assert main(["run", "--minutes", "1", "--port", str(fake_unity), "--out", str(out)]) == 0
    assert (out / "runs" / "r1" / "events.jsonl").is_file()
    assert json.loads((out / "findings.json").read_text(encoding="utf-8"))["findings"]


def test_client_explains_when_unity_is_not_running():
    with pytest.raises(PlayGoblinError, match="Cannot reach PlayGoblin"):
        Client(port=1, timeout=0.5).status()


def test_cli_run_without_unity_exits_nonzero(root, capsys):
    assert main(["run", "--port", "1", "--out", str(root / "o")]) == 2
    assert "Cannot reach PlayGoblin" in capsys.readouterr().err


def test_summary_prints_on_a_legacy_codepage_console(root, monkeypatch):
    import io
    import sys
    raw = io.BytesIO()
    monkeypatch.setattr(sys, "stdout", io.TextIOWrapper(raw, encoding="cp1252", errors="strict"))
    d = make_run(root, "r1", [error_event(10, "NRE")])
    assert main(["report", str(d), "--out", str(root / "rep")]) == 0
    sys.stdout.flush()
    assert "F1" in raw.getvalue().decode("utf-8")
