# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-04

First pre-release. Accuracy on real games is not measured yet; "no findings" never means bug free.

### Added
- Open-source QA bot that plays a game unattended and reports findings with frame/clip evidence (CLI, MCP server, run-folder protocol, Python reference writer).
- Unity package and Unreal Engine plugin (verified on Unity 6000.4 and UE 5.8.3 Win64).
- Human-like input in Unreal: discovers the game's real input bindings, mouse look via simulated mouse axes, taps action keys, hops when stuck, and drives the pawn through real key input.
- Input checks: `dead_input` and `no_movement`.
- `prototype_look` check and protocol documentation.
- UMG menu crawling in the Unreal plugin.
- Frontier exploration and coverage reporting in the Unreal plugin.
- Landing-page README, launch visuals (logo, banner, demo GIF, charts, pipeline) and a dev.to article draft.
- CI: Python tests on Linux and Windows, MCP smoke test.

### Changed
- Report prints integer seeds without a decimal point and shows cell counts only when the run recorded them.
- Collapsed Unreal engine startup noise in run output.

### Fixed
- Unreal sandbox project now compiles and passes `verify_unreal.py`.
- Missing-material pink: only pure magenta counts, so pastel pink no longer causes false positives.
- Dead-input probe (`HumanLook`) is robust to intro and loading screens.
- Unreal menu crawler survives level travel (weak references, reset on world change).
- Unreal first-compile and floor-probe fixes (floor grid on `ECC_Pawn`, skip enclosing volumes, movement diagnostics).

[0.1.0]: https://github.com/grloper/playgoblin/releases/tag/v0.1.0
