# PlayGoblin for Unreal Engine (experimental)

> **Status: compiles (0 errors, 0 warnings; UE 5.8.3, Win64 Development Editor) and is verified on its own sample (2026-11-02).** `scripts/verify_unreal.py` (UnrealEditor-Cmd, seed 1, 150 s, `-nullrhi`) finds all five planted bugs in `samples/unreal-sandbox` (trapdoor fall, 70 cm step stuck, error after first fall, dead UMG "Options" button, "TODO: title" menu text) and reports 0 findings on the `-PGClean` level; the same result with `--game-arg=-PlayGoblinKeys` (real input). One seed and one machine only: not yet verified on a real game, and frame capture (rendered mode) was not re-run. Please open an issue with what happened if you try it.

A C++ runtime plugin that walks a game and writes the same run folder as the Unity package ([docs/PROTOCOL.md](../docs/PROTOCOL.md)),
so `playgoblin report`, `playgoblin file-issues` and the MCP server work unchanged.

## What it does

- Finds the possessed pawn of player 0 (best with an `ACharacter`), picks random walkable destinations and moves with `AddMovementInput`.
- Uses the NavMesh when the level has one (`UNavigationSystemV1`), otherwise a floor grid sampled with line traces.
- Detects stuck spots (no horizontal progress for 5 s), falls below the level, and respawns the player at the last safe spot.
- Prototype look: at run start it lists visible actors that use the engine default/grid material (`WorldGridMaterial`, `DefaultMaterial`, `BasicShapeMaterial`, `Checker` under `/Engine/`) or a mesh from `/Engine/BasicShapes/`, as `prototype_look` events (max 50 actors, one event each; `data.total` has the full count). The CLI turns each into a medium finding. It checks only static assigned materials and meshes, so art that is a placeholder in other ways (grey custom materials, UI) is not seen. The generated sandbox is all engine cubes, so it reports 6 on both levels; `verify_unreal.py` prints that count and does not count it as a miss or false positive.
- Captures `Error` logs (and `LogScript` warnings, as exceptions), grouped by message.
- Optional frame capture through `FScreenshotRequest` (not with `-nullrhi`), and frame-rate drops with a budget.
- Local HTTP API (same routes as Unity) on 127.0.0.1:47321 for `playgoblin run`.

- Menu crawling (UMG, `-PlayGoblinMenus=0` turns it off): about every 2 s it finds visible `UUserWidget`s in the viewport, reports visible `UTextBlock`s with TODO/TBD/lorem/placeholder/FIXME (whole word) or the default text "Text Block"/"Button" as `placeholder_text`, and clicks each enabled `UButton` once (max 30 per run, top to bottom by widget name). A button is `dead_button` if, within 1 s, the viewport widget set, any widget visibility/enabled state, the world/map, pause state and cursor/input mode all stayed the same. Buttons labelled Quit/Exit are never clicked (`menu_skip`). Limits: clicks are simulated with `UButton::OnClicked.Broadcast()`, so Slate hit testing, hover/press styles and the input stack are not tested; only UMG (not raw Slate, CommonUI-only or Enhanced-Input-driven menus); a button that only changes text, plays a sound or sets game state is reported dead; a click that closes a menu hides the buttons queued behind it that scan (they are picked up only if the menu comes back); buttons are clicked once each by widget class and name.

Not implemented yet: blank/pink frame checks on the engine side (the CLI does the frame checks), a hook for custom start flows.
Positions are written as metres with Y up (`[x, z, y]` of Unreal's cm values) so the CLI reads them like Unity runs.

## Use it

1. Copy `PlayGoblin/` into your project's `Plugins/` folder (or add `unreal/` to `AdditionalPluginDirectories` in the `.uproject`) and enable the plugin.
2. Nothing runs unless you launch with `-playgoblin`, so shipping with the plugin enabled is harmless.

```bash
# the plugin starts a run by itself and quits when it is done
UnrealEditor-Cmd.exe MyGame.uproject -game -playgoblin -PlayGoblinSeconds=300 -PlayGoblinSeed=1 -PlayGoblinOut=D:/runs -PlayGoblinTimeScale=3 -PlayGoblinExit
playgoblin report D:/runs/<run id>

# or leave the game open and drive it from the CLI / an AI agent
UnrealEditor.exe MyGame.uproject -game -playgoblin
playgoblin run --minutes 10
```

| Flag | Meaning |
|---|---|
| `-playgoblin` | Enable the plugin and open the local API (`-PlayGoblinPort=` to change the port) |
| `-PlayGoblinSeconds=N` | Start a run by itself, N game seconds long |
| `-PlayGoblinMenus=0` | Do not crawl UMG menus (on by default) |
| `-PlayGoblinSeed=`, `-PlayGoblinOut=`, `-PlayGoblinTimeScale=`, `-PlayGoblinFpsBudgetMs=`, `-PlayGoblinExit` | Seed, output folder, game speed, frame budget, quit afterwards |

**Security note:** the API is control-only (start/stop a run, read events) but Unreal's HTTP server may listen on all interfaces. Only use `-playgoblin` on a trusted network.

## Build notes (Windows)

- Close Unreal Editor first. UnrealBuildTool refuses to build while an editor with Live Coding is open.
- Keep the project in a **short path** (for example `C:\pg`). Unreal fails on paths over 260 characters.
- `samples/unreal-sandbox` is a minimal C++ project with a generated level (a trapdoor, a 70 cm step the character cannot climb, an error after the first fall) and a `-PGClean` mode with none of those:

```bash
Build.bat PlayGoblinSandboxEditor Win64 Development -Project=C:/pg/samples/unreal-sandbox/PlayGoblinSandbox.uproject -WaitMutex
python scripts/verify_unreal.py --editor "C:/Program Files/Epic Games/UE_5.8/Engine/Binaries/Win64/UnrealEditor-Cmd.exe"
```

## Known issues (first real run, 2026-11-02)
- The floor grid used to trace from the top of the level on `ECC_Visibility` and rejected every cell in a game with roof/sky meshes. It now traces on `ECC_Pawn` from head height and skips actors the trace starts inside.
- In the first game tested the pawn stayed in `MOVE_Falling` with zero velocity and no floor at spawn, so the bot never moved. `waypoint_timeout` events now carry movement diagnostics. Whether this is a game bug (player embedded in collision) or a plugin issue is not yet resolved.
- Launching with `UnrealEditor-Cmd.exe` while the project enables Epic's experimental toolset plugins logs ~47 Python `Error` lines at t=0; they are reported as findings. These are false positives and need a filter (planned).

