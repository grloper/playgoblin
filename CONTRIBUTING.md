# Contributing to PlayGoblin

The most useful thing you can do right now: **run it on a game and tell us what it got wrong.** A missed bug or a false report, with the run folder (`events.jsonl`, `run.json`) attached, is worth more than a new feature.

## Set up

```bash
git clone https://github.com/grloper/playgoblin && cd playgoblin/cli
pip install -e ".[dev]"
python -m pytest -q
```

The Python side needs no game engine. Engine-side changes (Unity, Unreal) are described in [AGENTS.md](AGENTS.md), including how to run their tests.

## Ground rules

- Checks are plain deterministic code, one module per check in `cli/playgoblin/checks/`. A check takes a run and returns findings, so a test is a fake run folder in, findings out (`cli/tests/conftest.py`).
- Every finding needs evidence: an event, frame or log line. A signal seen once should be shown as unconfirmed, not dropped and not asserted.
- Test with the format a real engine writes (see `docs/PROTOCOL.md`), not only hand-made fixtures.
- New engine support = a writer for the run-folder format. Open an issue first so we agree on positions, time and event names.
- Keep the CLI free of hard dependencies. Keep Unity builds at zero warnings. Do not hand-edit Unity scenes; regenerate them with the sample builder.

## Pull requests

Describe what you changed and how you verified it (test output, a report, a screenshot). Small PRs get reviewed faster.
