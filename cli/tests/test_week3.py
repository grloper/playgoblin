import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from types import SimpleNamespace

import pytest

from playgoblin import llm
from playgoblin.cli import main
from playgoblin.config import Config
from playgoblin.issues import MARKER, fingerprint
from playgoblin.pipeline import analyze
from conftest import error_event, make_run, stuck_event


def fps_event(t, ms=80, seconds=2):
    return {"t": t, "type": "fps_drop", "pos": [3, 0, 3],
            "data": {"ms": str(ms), "seconds": str(seconds), "budget_ms": "33"}}


def findings_of(root, *dirs, config=None):
    return analyze(list(dirs), root / "out", config or Config())[0]


def test_fps_drops_become_one_finding_and_respect_a_stricter_budget(root):
    d = make_run(root, "r1", [fps_event(20), fps_event(40, ms=50)])
    (f,) = findings_of(root, d)
    assert f.type == "fps_drop" and f.occurrences == 2
    assert findings_of(root, d, config=Config(fps_budget_ms=100)) == []


def test_placeholder_text_is_a_debug_leftover(root):
    d = make_run(root, "r1", [{"t": 5, "type": "placeholder_text", "detail": "TODO: real title",
                               "data": {"path": "Canvas/Menu/Title"}}])
    (f,) = findings_of(root, d)
    assert f.type == "debug_leftover" and "Canvas/Menu/Title" in f.detail


def test_dead_button_finding(root):
    d = make_run(root, "r1", [{"t": 9, "type": "dead_button", "detail": "Options",
                               "data": {"path": "Canvas/Options", "label": "Options", "scene": "Menu"}}])
    (f,) = findings_of(root, d)
    assert f.type == "dead_button" and f.confirmed and "Options" in f.title


# ---- file-issues ----

class FakeGh:
    def __init__(self, existing=""):
        self.calls, self.existing = [], existing

    def __call__(self, cmd, **kw):
        self.calls.append(cmd)
        out = self.existing if cmd[1:3] == ["issue", "list"] else "https://github.com/o/r/issues/7\n"
        return SimpleNamespace(returncode=0, stdout=out, stderr="")


def make_report(root):
    d = make_run(root, "r1", [error_event(10, "NRE", h="h1"), stuck_event(30, [5, 0, 5])])
    analyze([d], root / "rep", Config())
    return root / "rep"


def test_file_issues_is_a_dry_run_by_default(root):
    from playgoblin.issues import file_issues
    gh, out = FakeGh(), []
    file_issues(make_report(root), "o/r", yes=False, echo=out.append, runner=gh)
    assert not any(c[1:3] == ["issue", "create"] for c in gh.calls)
    assert any("would file" in l for l in out) and any("Dry run" in l for l in out)
    assert any("unconfirmed" in l for l in out)  # the lone stuck spot is not filed


def test_file_issues_with_yes_creates_issues_with_a_fingerprint(root):
    from playgoblin.issues import file_issues
    gh = FakeGh()
    file_issues(make_report(root), "o/r", yes=True, label="playgoblin", echo=lambda s: None, runner=gh)
    (create,) = [c for c in gh.calls if c[1:3] == ["issue", "create"]]
    body = create[create.index("--body") + 1]
    assert MARKER in body and "--label" in create and create[create.index("--title") + 1].startswith("[PlayGoblin]")


def test_file_issues_skips_findings_that_are_already_filed(root):
    from playgoblin.issues import file_issues
    rep = make_report(root)
    f = json.loads((rep / "findings.json").read_text(encoding="utf-8"))["findings"][0]
    gh, out = FakeGh(existing=f"[{{'body': '{MARKER}:{fingerprint(f)}'}}]"), []
    file_issues(rep, "o/r", yes=True, echo=out.append, runner=gh)
    assert not any(c[1:3] == ["issue", "create"] for c in gh.calls)
    assert any("already filed" in l for l in out)


def test_file_issues_without_a_report_is_a_clear_error(root, capsys):
    assert main(["report", "file-issues", str(root / "nope"), "--repo", "o/r"]) == 2
    assert "findings.json" in capsys.readouterr().err


# ---- LLM summary ----

class FakeLlm(BaseHTTPRequestHandler):
    seen = []

    def log_message(self, *a):
        pass

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        FakeLlm.seen.append((self.path, dict(self.headers), body))
        payload = ({"content": [{"type": "text", "text": "claude says <b>fix NRE</b>"}]} if "messages" in self.path and "v1/messages" in self.path
                   else {"choices": [{"message": {"content": "openai says fix NRE"}}]})
        raw = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)


@pytest.fixture
def llm_server():
    FakeLlm.seen = []
    srv = HTTPServer(("127.0.0.1", 0), FakeLlm)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    yield f"http://127.0.0.1:{srv.server_address[1]}"
    srv.shutdown()


FINDINGS = [{"severity": "critical", "title": "Exception: NRE", "runs_seen": 1, "runs_total": 1, "detail": "boom"}]


def test_summary_openai_compatible(llm_server):
    env = {"PLAYGOBLIN_LLM_PROVIDER": "openai", "PLAYGOBLIN_LLM_MODEL": "m", "PLAYGOBLIN_LLM_BASE_URL": llm_server}
    assert llm.summarize(FINDINGS, env) == "openai says fix NRE"
    path, headers, body = FakeLlm.seen[0]
    assert path == "/chat/completions" and "Exception: NRE" in body["messages"][0]["content"]
    assert "Authorization" not in headers


def test_summary_anthropic_and_report_escapes_it(root, llm_server, monkeypatch):
    env = {"PLAYGOBLIN_LLM_PROVIDER": "anthropic", "PLAYGOBLIN_LLM_MODEL": "m", "PLAYGOBLIN_LLM_API_KEY": "k",
           "PLAYGOBLIN_LLM_BASE_URL": llm_server}
    for k, v in env.items():
        monkeypatch.setenv(k, v)
    d = make_run(root, "r1", [error_event(10, "NRE")])
    assert main(["report", str(d), "--out", str(root / "rep"), "--summary"]) == 0
    assert {k.lower(): v for k, v in FakeLlm.seen[0][1].items()}["x-api-key"] == "k"
    html = (root / "rep" / "report.html").read_text(encoding="utf-8")
    assert "&lt;b&gt;fix NRE&lt;/b&gt;" in html and "<b>fix NRE</b>" not in html
    assert "claude says" in (root / "rep" / "report.md").read_text(encoding="utf-8")


def test_summary_without_configuration_explains_itself(root, monkeypatch, capsys):
    for k in ("PLAYGOBLIN_LLM_PROVIDER", "PLAYGOBLIN_LLM_MODEL"):
        monkeypatch.delenv(k, raising=False)
    d = make_run(root, "r1", [error_event(10, "NRE")])
    assert main(["report", str(d), "--out", str(root / "rep"), "--summary"]) == 2
    assert "PLAYGOBLIN_LLM_PROVIDER" in capsys.readouterr().err


def test_no_summary_means_no_network(root):
    d = make_run(root, "r1", [error_event(10, "NRE")])
    assert main(["report", str(d), "--out", str(root / "rep")]) == 0
    assert json.loads((root / "rep" / "findings.json").read_text(encoding="utf-8"))["summary"] is None


def test_lowercase_exception_kind_from_the_engines_is_critical(root):
    d = make_run(root, "r1", [error_event(10, "NullReferenceException: boom", kind="exception")])
    (f,) = findings_of(root, d)
    assert f.severity == "critical" and f.title.startswith("Exception: ")
