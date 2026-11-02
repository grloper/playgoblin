from conftest import make_run
from playgoblin.checks import prototype_look, run_checks
from playgoblin.config import Config
from playgoblin.model import load_run


def _ev(name, reason="default_material", total="2", **kw):
    return {"t": 1, "type": "prototype_look", "detail": name, "pos": [1, 0, 2],
            "data": {"reason": reason, "mesh": "/Engine/BasicShapes/Cube.Cube", "material": "/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial",
                     "component": "StaticMeshComponent0", "total": total, **kw}}


def test_one_finding_per_actor(root):
    run = load_run(make_run(root, "r", [_ev("Floor"), _ev("Wall", reason="basic_shape")]))
    fs = list(prototype_look.check(run, Config()))
    assert [(f.type, f.severity, f.key) for f in fs] == [("prototype_look", "medium", "prototype:Floor"), ("prototype_look", "medium", "prototype:Wall")]
    assert fs[0].title == "Prototype look: Floor uses the engine default or grid material"
    assert "basic shape" in fs[1].title and fs[1].position == [1, 0, 2]
    assert "WorldGridMaterial" in fs[0].detail


def test_unknown_reason_not_dropped(root):
    fs = list(prototype_look.check(load_run(make_run(root, "r", [_ev("X", reason="weird")])), Config()))
    assert len(fs) == 1 and "weird" in fs[0].title


def test_cap_is_reported(root):
    fs = list(prototype_look.check(load_run(make_run(root, "r", [_ev("A", total="120")])), Config()))
    assert "120 actors" in fs[0].detail


def test_registered_and_clean_run(root):
    run = load_run(make_run(root, "r", [_ev("A")]))
    assert [f.type for f in run_checks(run, Config())] == ["prototype_look"]
    assert list(prototype_look.check(load_run(make_run(root, "c", [{"t": 0, "type": "player_found", "pos": [0, 0, 0]}])), Config())) == []
