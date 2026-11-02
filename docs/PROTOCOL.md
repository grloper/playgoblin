# The PlayGoblin run folder

PlayGoblin does not care what engine a game uses. Anything that writes this folder gets the same checks, report,
deduplication across runs, GitHub issues and AI tooling. The Unity package and the Unreal plugin both write it;
`playgoblin.writer.RunWriter` (Python) is a reference writer you can port to GDScript, C#, Lua or anything else.

```
PlayGoblinRuns/<run_id>/
  events.jsonl      one JSON object per line, in order
  run.json          summary, written when the run ends
  frames/           optional screenshots: f<index>_t<seconds>.png or .jpg
```

Build a report from it with `playgoblin report PlayGoblinRuns/<run_id>`. No engine, network or API key is involved.

## Coordinates and time

- Positions are **metres, Y up**: `[x, height, z]`. Z-up engines (Unreal, Blender) write `[x, z, y]`. Unreal also converts centimetres to metres.
- `t` is **game seconds since the run started**. Frame filenames use the same clock, so `f00012_t6.50.png` belongs to events near `t = 6.5`.

## events.jsonl

Every line: `{"seq": 1, "t": 0.0, "type": "...", "detail": "optional text", "pos": [x, y, z], "data": {"key": "string value"}}`.
`seq` counts up from 1. `detail`, `pos` and `data` are optional. **All `data` values are strings.**

| type | Meaning | Fields the checks read |
|---|---|---|
| `run_start` | First event | `data`: run_id, seed, engine, scene |
| `player_found` | The bot controls a player | `pos`; `data`: mover, navmesh |
| `pos` | Position sample (about 2 per second) | `pos` |
| `waypoint` / `waypoint_timeout` | Reached or gave up on a destination | `pos` |
| `stuck` | The player tried to move and made no progress | `pos`, `detail`; `data`: blocked_by, controller |
| `out_of_world` | The player fell below the level or left the walkable area | `pos`; `data`: reason, fell_to |
| `respawn` | The bot put the player back | `pos`; `data`: from |
| `trapped` | The start area is a closed pocket | `pos`; `data`: reachable_cells, walkable_cells, cell_m |
| `error_summary` | One group of identical errors, written at the end | `detail` = message; `data`: **hash**, **kind** (`exception` or `error`), **count**, first_t, last_t; `pos` |
| `dead_button` | A UI button that did nothing when clicked twice | `detail` = label; `data`: path, label, scene |
| `placeholder_text` | TODO/lorem-style text visible in the UI | `detail` = text; `data`: path |
| `input_map` | What the engine side found bound to input (info) | `data`: forward_keys, action_keys, jump_key, mouse_look (`true`/`false`), axes, enhanced_input |
| `dead_input` | A control does nothing (e.g. no key moves the player forward) | `detail`; `data`: input (`move` or `look`) |
| `input_press` | A key the bot pressed (info only) | `detail` = key name |
| `fps_drop` | Frame time above budget for a second or more | `pos`; `data`: ms, seconds, budget_ms |
| `scene_change` | A level or scene loaded (blank frames next to it are ignored) | `detail` = scene |
| `setup_error` | PlayGoblin could not start (no player found, ...) | `detail` |
| `run_end` | Last event | `detail` = reason |

Unknown event types are kept and ignored, so you can add your own (`PlayGoblinEvents.Log`-style game events are fine).
Group identical errors yourself before writing `error_summary`: the same `hash` in several runs becomes one finding.

## run.json

```json
{ "run_id": "20261102-153000-s1", "seed": 1, "engine": "unity", "engine_version": "6000.4.0f1", "scene": "Assets/Level.unity",
  "end_reason": "completed", "game_seconds": 300, "distance_m": 900, "coverage": 0.7, "mover": "CharacterController",
  "navmesh": false, "waypoints": 40, "event_counts": {"stuck": 2} }
```

Only `run_id` is needed; the rest shows up in the report header when present.

## What becomes a finding

| Finding | Source | Notes |
|---|---|---|
| Exception / error | `error_summary` | critical when `kind` is `exception`, else high |
| Stuck spot | `stuck` | merged within 2 m; **unconfirmed** until it repeats (2+ times or in 2+ runs) |
| Fell out of the world | `out_of_world` | critical |
| Closed start area | `trapped` | high |
| Dead button | `dead_button` | medium |
| Dead input | `dead_input` | critical for `move`, high for `look` |
| Player never moved | `player_found` + `pos` | critical; run over 20 s and under 1 m of spread |
| Engine startup noise | `error_summary` | low; Unreal `LogPython:` lines at t<=1 s from `/Engine/Plugins/`, collapsed into one finding |
| Placeholder text, magenta (missing material) frames | `placeholder_text`, `frames/` | medium |
| Frame rate drop | `fps_drop` | medium; `fps_budget_ms` in `playgoblin.toml` can filter |
| Blank screen | `frames/` | high; needs numpy and Pillow |

## The local API (optional)

Engines that want `playgoblin run` to drive them serve this on `127.0.0.1:47321` (`PLAYGOBLIN_PORT`): `GET /status`,
`POST /run/start` (`{"seed": 1, "minutes": 5}`), `POST /run/stop`, `GET /events?since=N&max=M` (returns `{"events": [], "next": N}`),
`GET /runs/latest`. `/status` returns `state` (`idle|starting|running|ended`), `run_id`, `run_dir`, `end_reason`, `t`, `events`.
Without it you can still run your own walk and call `playgoblin report` afterwards.
