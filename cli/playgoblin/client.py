"""Client for the local API the Unity package serves on 127.0.0.1 (see unity/Runtime/PlayGoblinHost.cs)."""
from __future__ import annotations

import json
import os
import time
import urllib.error
import urllib.request
from pathlib import Path

DEFAULT_PORT = 47321


class PlayGoblinError(RuntimeError):
    pass


class Client:
    def __init__(self, host: str = "127.0.0.1", port: int | None = None, timeout: float = 10.0):
        self.base = f"http://{host}:{port or int(os.environ.get('PLAYGOBLIN_PORT', DEFAULT_PORT))}"
        self.timeout = timeout

    def _request(self, method: str, path: str, body: dict | None = None) -> dict:
        data = json.dumps(body).encode() if body is not None else (b"" if method == "POST" else None)
        req = urllib.request.Request(self.base + path, data=data, method=method)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return json.loads(resp.read().decode() or "{}")
        except urllib.error.HTTPError as e:
            detail = e.read().decode(errors="replace")
            raise PlayGoblinError(f"{method} {path} failed with HTTP {e.code}: {detail}") from e
        except (urllib.error.URLError, ConnectionError, TimeoutError) as e:
            raise PlayGoblinError(
                f"Cannot reach PlayGoblin at {self.base}. Start the game with the PlayGoblin package or plugin (Unity: press Play; Unreal: launch with -playgoblin) "
                f"and make sure the package is installed.") from e

    def status(self) -> dict:
        return self._request("GET", "/status")

    def start(self, seed: int, minutes: float) -> dict:
        return self._request("POST", "/run/start", {"seed": seed, "minutes": minutes})

    def stop(self) -> dict:
        return self._request("POST", "/run/stop")

    def events(self, since: int = 0, max_events: int = 1000) -> dict:
        return self._request("GET", f"/events?since={since}&max={max_events}")

    def latest_run(self) -> dict:
        return self._request("GET", "/runs/latest")

    def wait_for_end(self, poll_seconds: float = 1.0, timeout: float | None = None, on_tick=None) -> dict:
        started = time.monotonic()
        while True:
            s = self.status()
            if on_tick:
                on_tick(s)
            if s.get("state") == "ended":
                return s
            if timeout is not None and time.monotonic() - started > timeout:
                raise PlayGoblinError("timed out waiting for the run to finish")
            time.sleep(poll_seconds)

    def download_run(self, status: dict, dest: Path) -> Path:
        """Return a local run directory for the run that just ended.

        If Unity wrote it somewhere this machine can read, use that. Otherwise rebuild run.json and events.jsonl
        from the API (frames are only available locally)."""
        run_dir = status.get("run_dir")
        if run_dir and (Path(run_dir) / "run.json").exists():
            return Path(run_dir)
        dest.mkdir(parents=True, exist_ok=True)
        (dest / "run.json").write_text(json.dumps(self.latest_run()), encoding="utf-8")
        lines, since = [], 0
        while True:
            page = self.events(since, 5000)
            if not page["events"]:
                break
            lines += [json.dumps(e) for e in page["events"]]
            since = page["next"]
        (dest / "events.jsonl").write_text("\n".join(lines) + "\n", encoding="utf-8")
        return dest
