import io
import json

from playgoblin.config import Config
from playgoblin.mcp_server import TOOLS, handle, serve
from playgoblin.pipeline import analyze
from playgoblin.writer import RunWriter
from conftest import error_event, make_run, stuck_event
from test_pipeline import FakeUnity, fake_unity  # noqa: F401  (fixture)
from playgoblin.cli import main


# ---- --json ----

def test_report_json_is_one_parseable_document(root, capsys):
    d = make_run(root, "r1", [error_event(10, "NRE")])
    assert main(["report", str(d), "--out", str(root / "rep"), "--json"]) == 0
    data = json.loads(capsys.readouterr().out)
    assert data["findings"][0]["type"] == "exception" and data["report"]["html"].endswith("report.html")


def test_run_json_keeps_progress_off_stdout(root, fake_unity, monkeypatch, capsys):
    import playgoblin.client as c
    monkeypatch.setattr(c.time, "sleep", lambda s: None)
    assert main(["run", "--minutes", "1", "--port", str(fake_unity), "--out", str(root / "rep"), "--json"]) == 0
    captured = capsys.readouterr()
    assert json.loads(captured.out)["findings"]
    assert "Run 1/1" in captured.err


def test_file_issues_json_dry_run(root, capsys, monkeypatch):
    from test_week3 import FakeGh, make_report
    import playgoblin.issues as issues
    rep = make_report(root)
    gh = FakeGh()
    monkeypatch.setattr(issues.subprocess, "run", gh)
    monkeypatch.setattr(issues.shutil, "which", lambda n: "gh")  # gh is faked, never called for real
    assert main(["file-issues", str(rep), "--repo", "o/r", "--json"]) == 0
    out = json.loads(capsys.readouterr().out)
    assert out["posted"] is False and any("would file" in l for l in out["log"])


# ---- writer: any engine can produce a run folder ----

def test_a_run_written_by_the_writer_produces_findings(root):
    w = RunWriter(root / "runs", seed=3, engine="godot", scene="res://level1.tscn", engine_version="Godot 4.3")
    w.event("player_found", pos=(0, 0, 0))
    w.event("stuck", "no progress for 5s", pos=(12.0, 0.0, 3.4), t=61.5, blocked_by="Crate")
    w.event("stuck", "no progress for 5s", pos=(12.5, 0.0, 3.0), t=90.0, blocked_by="Crate")
    w.error("Invalid call. Nonexistent function 'foo' in base 'Node'", count=40, kind="exception", first_t=70.0)
    folder = w.end("completed", game_seconds=120, distance_m=300, coverage=0.5)
    findings, _ = analyze([folder], root / "rep", Config())
    kinds = {f.type: f for f in findings}
    assert kinds["stuck"].confirmed and kinds["exception"].severity == "critical"
    assert (folder / "run.json").is_file()


# ---- MCP server ----

def rpc(method, params=None, id=1):
    return {"jsonrpc": "2.0", "id": id, "method": method, **({"params": params} if params is not None else {})}


def test_mcp_initialize_and_list_tools():
    r = handle(rpc("initialize", {"protocolVersion": "2025-03-26"}))
    assert r["result"]["serverInfo"]["name"] == "playgoblin" and r["result"]["protocolVersion"] == "2025-03-26"
    names = {t["name"] for t in handle(rpc("tools/list"))["result"]["tools"]}
    assert names == {t["name"] for t in TOOLS} and "playgoblin_run" in names
    assert handle({"jsonrpc": "2.0", "method": "notifications/initialized"}) is None


def test_mcp_report_then_findings(root):
    d = make_run(root, "r1", [error_event(10, "NRE"), stuck_event(30, [5, 0, 5]), stuck_event(80, [5, 0, 5.5])])
    out = root / "rep"
    r = handle(rpc("tools/call", {"name": "playgoblin_report", "arguments": {"run_dirs": [str(d)], "out": str(out)}}))
    assert r["result"]["isError"] is False
    payload = json.loads(r["result"]["content"][0]["text"])
    assert payload["confirmed"] == 2 and payload["findings"][0]["id"] == "F1"
    r = handle(rpc("tools/call", {"name": "playgoblin_findings", "arguments": {"report_dir": str(out), "include_unconfirmed": False}}))
    assert len(json.loads(r["result"]["content"][0]["text"])["findings"]) == 2


def test_mcp_tool_errors_are_results_not_crashes(root):
    r = handle(rpc("tools/call", {"name": "playgoblin_findings", "arguments": {"report_dir": str(root / "none")}}))
    assert r["result"]["isError"] is True
    r = handle(rpc("tools/call", {"name": "playgoblin_status", "arguments": {"port": 1}}))
    assert r["result"]["isError"] is True and "Cannot reach" in r["result"]["content"][0]["text"]
    assert handle(rpc("nope"))["error"]["code"] == -32601


def test_mcp_file_issues_is_a_dry_run_unless_confirmed(root, monkeypatch):
    from test_week3 import FakeGh, make_report
    import playgoblin.issues as issues
    rep = make_report(root)
    gh = FakeGh()
    monkeypatch.setattr(issues.subprocess, "run", gh)
    monkeypatch.setattr(issues.shutil, "which", lambda n: "gh")  # gh is faked, never called for real
    handle(rpc("tools/call", {"name": "playgoblin_file_issues", "arguments": {"repo": "o/r", "report_dir": str(rep)}}))
    assert not any(c[1:3] == ["issue", "create"] for c in gh.calls)
    handle(rpc("tools/call", {"name": "playgoblin_file_issues", "arguments": {"repo": "o/r", "report_dir": str(rep), "confirm": True}}))
    assert any(c[1:3] == ["issue", "create"] for c in gh.calls)


def test_mcp_serves_newline_delimited_json_over_streams():
    stdin = io.StringIO(json.dumps(rpc("tools/list")) + "\nnot json\n" + json.dumps(rpc("ping", id=2)) + "\n")
    stdout = io.StringIO()
    serve(stdin, stdout)
    lines = [json.loads(l) for l in stdout.getvalue().splitlines()]
    assert lines[0]["result"]["tools"] and lines[1]["error"]["code"] == -32700 and lines[2]["result"] == {}
