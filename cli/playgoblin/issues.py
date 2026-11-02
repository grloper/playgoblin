"""File findings as GitHub issues through the `gh` CLI. Dry run unless told otherwise."""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
from pathlib import Path

MARKER = "playgoblin-fingerprint"
POSITIONAL = ("stuck", "out_of_world", "trapped")


def fingerprint(f: dict) -> str:
    pos = f.get("position")
    where = f"{round(pos[0] / 4)},{round(pos[2] / 4)}" if f["type"] in POSITIONAL and pos else f.get("key", "")
    return hashlib.sha1(f"{f['type']}|{where}".encode()).hexdigest()[:12]


def issue_body(f: dict, report_dir: str) -> str:
    lines = [f"**{f['title']}**", ""]
    meta = [f"Severity: {f['severity']}", f"Seen: {f['runs_seen']}/{f['runs_total']} runs, {f['occurrences']}x",
            f"First at {f['t']:.1f}s", f"Confidence: {f['confidence']}"]
    if f.get("position"):
        meta.append("Where: ({:.1f}, {:.1f}, {:.1f})".format(*f["position"]))
    lines += [" · ".join(meta), ""]
    if f.get("detail"):
        lines += ["```", f["detail"].strip(), "```", ""]
    if f.get("evidence"):
        lines += [f"Evidence is in the local report ({len(f['evidence'])} file(s) under "
                  f"`{report_dir}/evidence/{f['data'].get('id', '')}`). "
                  "The GitHub CLI cannot upload attachments, so drag the clip into this issue by hand.", ""]
    lines += ["_Filed by PlayGoblin from an automated play-through._", "", f"<!-- {MARKER}:{fingerprint(f)} -->"]
    return "\n".join(lines)


def _gh(args: list[str], runner=None) -> str:
    r = (runner or subprocess.run)(["gh", *args], capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args[:2])} failed: {(r.stderr or r.stdout).strip()}")
    return r.stdout


def plan(report_dir: Path, repo: str, include_unconfirmed: bool, runner=None):
    """Returns (to_create, skipped); skipped holds (finding, reason). Existing issues are matched by fingerprint."""
    data = json.loads((report_dir / "findings.json").read_text(encoding="utf-8"))
    existing = _gh(["issue", "list", "--repo", repo, "--state", "all", "--limit", "500",
                    "--search", MARKER, "--json", "number,body"], runner)
    create, skipped = [], []
    for f in data["findings"]:
        if not f["confirmed"] and not include_unconfirmed:
            skipped.append((f, "unconfirmed"))
        elif f"{MARKER}:{fingerprint(f)}" in existing:
            skipped.append((f, "already filed"))
        else:
            create.append(f)
    return create, skipped


def file_issues(report_dir: Path, repo: str, yes: bool, label: str | None = None, include_unconfirmed: bool = False,
                echo=print, runner=None) -> int:
    if not (report_dir / "findings.json").is_file():
        raise FileNotFoundError(f"{report_dir} has no findings.json. Run `playgoblin report` first.")
    if runner is None and shutil.which("gh") is None:
        raise RuntimeError("The GitHub CLI (gh) is not installed or not on PATH. See https://cli.github.com")
    create, skipped = plan(report_dir, repo, include_unconfirmed, runner)
    for f, why in skipped:
        echo(f"skip   {f['data']['id']} {f['title']} ({why})")
    for f in create:
        title = f"[PlayGoblin] {f['title']}"
        if not yes:
            echo(f"would file in {repo}: {title}")
            continue
        args = ["issue", "create", "--repo", repo, "--title", title, "--body", issue_body(f, str(report_dir))]
        if label:
            args += ["--label", label]
        echo(f"filed  {_gh(args, runner).strip()}  {title}")
    if not yes and create:
        echo(f"\nDry run: {len(create)} issue(s) would be filed. Add --yes to post them.")
    return 0
