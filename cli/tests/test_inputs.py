from conftest import error_event, make_run
from playgoblin.checks import inputs, exceptions
from playgoblin.config import Config
from playgoblin.model import load_run


def _run(root, events, name="r"):
    return load_run(make_run(root, name, events))


def _pos(t, x=0.0, z=0.0):
    return {"t": t, "type": "pos", "pos": [x, 1, z]}


def test_dead_input(root):
    ev = [{"t": 1, "type": "dead_input", "detail": "No keyboard key moves the player forward", "data": {"input": "move"}},
          {"t": 2, "type": "dead_input", "detail": "No keyboard key moves the player forward", "data": {"input": "move"}},
          {"t": 3, "type": "dead_input", "detail": "Moving the mouse does not turn the camera", "data": {"input": "look"}}]
    fs = list(inputs.check(_run(root, ev), Config()))
    assert [(f.type, f.severity) for f in fs] == [("dead_input", "critical"), ("dead_input", "high")]
    assert fs[0].key == "dead_input:No keyboard key moves the player forward"


def test_no_movement(root):
    ev = [{"t": 0, "type": "player_found", "pos": [0, 1, 0]}] + [_pos(t, 0.1) for t in range(1, 60, 2)]
    fs = list(inputs.check(_run(root, ev), Config()))
    assert len(fs) == 1 and fs[0].type == "no_movement" and fs[0].severity == "critical"


def test_movement_ok(root):
    ev = [{"t": 0, "type": "player_found", "pos": [0, 1, 0]}] + [_pos(t, t * 0.5) for t in range(1, 60, 2)]
    assert list(inputs.check(_run(root, ev), Config())) == []


def test_engine_noise_collapsed(root):
    start = {"t": 0, "type": "run_start", "data": {"engine": "unreal"}}
    noise = [error_event(0, 'LogPython:   File "C:/Program Files/Epic Games/UE_5.8/Engine/Plugins/Experimental/Toolsets/x.py"', h=f"h{i}") for i in range(7)]
    real = error_event(30, "NullReference in Foo", h="real")
    fs = list(exceptions.check(_run(root, [start, *noise, real]), Config()))
    assert sorted(f.type for f in fs) == ["engine_noise", "exception"]
    n = next(f for f in fs if f.type == "engine_noise")
    assert n.severity == "low" and n.title.startswith("7 engine plugin") and len(n.detail.splitlines()) == 5


def test_engine_noise_not_collapsed_for_unity(root):
    start = {"t": 0, "type": "run_start", "data": {"engine": "unity"}}
    ev = [start, error_event(0, "LogPython: /Engine/Plugins/x", h="a")]
    assert [f.type for f in exceptions.check(_run(root, ev), Config())] == ["exception"]
