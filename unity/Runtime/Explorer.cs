using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.AI;

namespace PlayGoblin
{
    /// <summary>
    /// Walks the player to seeded random points, preferring places it hasn't visited, and records
    /// stuck spots and falls out of the world. After either one it respawns the player somewhere
    /// reachable and keeps going, so one bug doesn't end the run.
    /// </summary>
    internal sealed class Explorer
    {
        const float ArriveDistance = 1.2f;
        const float DestinationTimeout = 30f;
        const float MinDwell = 0.25f;
        const float DetectorHz = 10f;
        const float RespawnGrace = 1f;
        const int ReachabilitySamples = 3000;

        readonly PlayGoblinPlayer _player;
        readonly PlayGoblinRunOptions _options;
        readonly StuckDetector _stuck;
        readonly OutOfWorldDetector _outOfWorld = new OutOfWorldDetector();
        CoverageGrid _coverage;
        DestinationPicker _picker;
        Bounds _world;

        WalkGrid _grid;
        List<Vector3> _path;
        int _pathIdx;
        float _bodyRadius = 0.5f;
        readonly System.Random _rng;
        Vector3? _dest;
        float _levelY;
        Vector3 _lastStuck;
        float _lastStuckT = -1000f;
        float _destSince;
        Vector3 _lastGood;
        bool _hasLastGood;
        bool? _onNavMesh;
        float _nextDetect;
        float _nextSample;
        float _graceUntil;

        public int Stucks { get; private set; }
        public int Falls { get; private set; }
        public int Respawns { get; private set; }
        public int Waypoints { get; private set; }
        public float DistanceWalked { get; private set; }
        public CoverageGrid Coverage => _coverage;
        public bool UsesNavMesh => _picker != null && _picker.HasNavMesh;
        public bool UsesGrid => _grid != null;
        public int WalkableCells => _grid?.WalkableCount ?? 0;
        public int ReachableCells => _grid?.ReachableCount ?? 0;
        public Bounds World => _world;

        Vector3 _prevPos;
        bool _hasPrev;

        public Explorer(PlayGoblinPlayer player, PlayGoblinRunOptions options)
        {
            _player = player;
            _options = options;
            _rng = new System.Random(options.seed);
            _stuck = new StuckDetector { Window = options.stuckSeconds, MinDistance = options.stuckDistance };
            Rebuild();
        }

        /// <summary>Recompute level bounds and walk targets, e.g. after a scene change.</summary>
        public void Rebuild()
        {
            _world = WorldBounds.Compute(_player.transform);
            _levelY = _player.FeetY;
            _outOfWorld.KillY = _world.min.y - 10f;

            _bodyRadius = _player.BodyRadius;

            var tri = NavMesh.CalculateTriangulation();
            var hasNavMesh = tri.indices != null && tri.indices.Length >= 3;
            _picker = hasNavMesh
                ? DestinationPicker.FromNavMesh(_options.seed, _world, PickerProbe)
                : DestinationPicker.FromBounds(_options.seed, _world, PickerProbe);

            _grid = null;
            if (!hasNavMesh)
            {
                var grid = WalkGrid.Build(_world, Mathf.Max(0.5f, _bodyRadius * 2f), 0.6f, GroundProbe, HasBodyClearance);
                if (grid.FloodFrom(_player.Position)) _grid = grid;
            }

            _coverage = new CoverageGrid(2f);
            if (_grid != null)
            {
                foreach (var c in _grid.ReachableCells) _coverage.MarkReachable(c);
                if (_grid.WalkableCount >= 50 && _grid.ReachableCount < _grid.WalkableCount * 0.2f)
                {
                    PlayGoblinEvents.Add("trapped", "player can reach only a small part of the level", _player.Position,
                        new Dictionary<string, string>
                        {
                            { "reachable_cells", _grid.ReachableCount.ToString() },
                            { "walkable_cells", _grid.WalkableCount.ToString() },
                            { "cell_m", _grid.Cell.ToString("0.##", CultureInfo.InvariantCulture) }
                        });
                }
            }
            else
            {
                var reach = hasNavMesh
                    ? DestinationPicker.FromNavMesh(_options.seed + 1, _world, PickerProbe)
                    : DestinationPicker.FromBounds(_options.seed + 1, _world, PickerProbe);
                for (int i = 0; i < ReachabilitySamples; i++)
                {
                    var p = reach.RandomPoint();
                    if (!p.HasValue) continue;
                    if (hasNavMesh && !CanPathTo(_player.Position, p.Value)) continue;
                    _coverage.MarkReachable(p.Value);
                }
            }

            _dest = null;
            _path = null;
            _stuck.Reset();
            _outOfWorld.Reset();
        }

        public void Tick(float t)
        {
            var pos = _player.Position;
            PlayGoblinEvents.LastPlayerPosition = pos;
            PlayGoblinEvents.HasPlayerPosition = true;

            if (_hasPrev && t >= _graceUntil) DistanceWalked += Vector3.Distance(_prevPos, pos);
            _prevPos = pos;
            _hasPrev = true;

            var detectNow = t >= _nextDetect;
            if (detectNow)
            {
                _nextDetect = t + 1f / DetectorHz;
                if (UsesNavMesh) _onNavMesh = NavMesh.SamplePosition(pos, out _, 2.5f, NavMesh.AllAreas);
            }

            if (detectNow && t >= _graceUntil)
            {
                var reason = _outOfWorld.Feed(t, pos, UsesNavMesh ? _onNavMesh : null);
                if (reason != null)
                {
                    Falls++;
                    var where = _hasLastGood ? _lastGood : pos;
                    PlayGoblinEvents.Add("out_of_world", reason, where, new Dictionary<string, string>
                    {
                        { "reason", reason },
                        { "fell_to", Fmt(pos) }
                    });
                    Respawn(t, "out_of_world", pos);
                    return;
                }
            }

            if (pos.y >= _outOfWorld.KillY && _onNavMesh != false)
            {
                _lastGood = pos;
                _hasLastGood = true;
            }

            if ((!_dest.HasValue || HorizontalDistance(pos, _dest.Value) < ArriveDistance) && t - _destSince >= MinDwell)
            {
                NewDestination(t);
            }
            else if (t - _destSince > DestinationTimeout)
            {
                PlayGoblinEvents.Add("waypoint_timeout", $"didn't reach waypoint in {DestinationTimeout:0}s", pos,
                    new Dictionary<string, string> { { "dest", Fmt(_dest.Value) } });
                NewDestination(t);
            }

            if (_dest.HasValue)
            {
                var target = _dest.Value;
                if (_path != null)
                {
                    var reach = Mathf.Max(0.6f, _grid.Cell * 0.6f);
                    while (_pathIdx < _path.Count - 1 && HorizontalDistance(pos, _path[_pathIdx]) < reach) _pathIdx++;
                    target = _path[_pathIdx];
                }
                _player.Mover.MoveTowards(target);
            }

            if (detectNow && t >= _graceUntil && _stuck.Feed(t, pos, _dest.HasValue))
            {
                Stucks++;
                var repeat = t - _lastStuckT < 60f && HorizontalDistance(pos, _lastStuck) < 2f;
                _lastStuck = pos;
                _lastStuckT = t;
                PlayGoblinEvents.Add("stuck", $"no progress for {_options.stuckSeconds:0.#}s", pos,
                    new Dictionary<string, string>
                    {
                        { "dest", _dest.HasValue ? Fmt(_dest.Value) : "" },
                        { "next_node", _path != null ? Fmt(_path[_pathIdx]) : "" },
                        { "node", _path != null ? $"{_pathIdx + 1}/{_path.Count}" : "" },
                        { "blocked_by", DescribeBlocker(pos, _path != null ? _path[_pathIdx] : _dest) },
                        { "controller", _player.Diagnostics },
                        { "repeat", repeat ? "true" : "false" }
                    });
                if (repeat || UsesNavMesh)
                {
                    Respawn(t, "unstick", pos);
                }
                else
                {
                    // Straight-line walking can stall on walls; give it another target before teleporting.
                    _stuck.Reset();
                    NewDestination(t);
                }
                return;
            }

            if (detectNow) _coverage.Visit(pos);

            if (t >= _nextSample)
            {
                _nextSample = t + 1f / Mathf.Max(0.1f, _options.positionHz);
                PlayGoblinEvents.Add("pos", null, pos, null);
            }
        }

        void NewDestination(float t)
        {
            _path = null;
            _pathIdx = 0;
            _dest = null;
            if (_grid != null)
            {
                for (int attempt = 0; attempt < 6 && _path == null; attempt++)
                {
                    var d = PickReachable();
                    if (!d.HasValue) break;
                    var path = _grid.FindPath(_player.Position, d.Value);
                    if (path == null || path.Count == 0) continue;
                    _path = path;
                    _dest = path[path.Count - 1];
                }
            }
            if (!_dest.HasValue)
            {
                // On a NavMesh, skip targets the player can't path to (islands such as wall tops).
                for (int attempt = 0; attempt < (UsesNavMesh ? 10 : 1); attempt++)
                {
                    var d = _picker.Pick(_coverage);
                    if (!d.HasValue) break;
                    if (UsesNavMesh && !CanPathTo(_player.Position, d.Value)) continue;
                    _dest = d;
                    break;
                }
                if (!_dest.HasValue && UsesNavMesh) _dest = _picker.Pick(_coverage);
            }

            _destSince = t;
            if (_dest.HasValue)
            {
                Waypoints++;
                PlayGoblinEvents.Add("waypoint", null, _dest.Value, null);
            }
        }

        readonly NavMeshPath _navPath = new NavMeshPath();

        bool CanPathTo(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var a, 2.5f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var b, 2.5f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _navPath)
                   && _navPath.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>A reachable cell, preferring ones the player has visited least and that are not right under it.</summary>
        Vector3? PickReachable()
        {
            var cells = _grid.ReachableCells;
            if (cells.Count == 0) return null;
            Vector3? best = null;
            var bestVisits = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var c = cells[_rng.Next(cells.Count)];
                if (cells.Count > 20 && HorizontalDistance(c, _player.Position) < 3f) continue;
                var v = _coverage.Visits(c);
                if (v < bestVisits) { best = c; bestVisits = v; }
            }
            return best ?? cells[_rng.Next(cells.Count)];
        }

        void Respawn(float t, string reason, Vector3 from)
        {
            var target = (_grid != null ? PickReachable() : _picker.Pick(_coverage)) ?? (_hasLastGood ? _lastGood : from);
            _player.StopMoving();
            _player.Teleport(target);
            Respawns++;
            PlayGoblinEvents.Add("respawn", reason, target, new Dictionary<string, string> { { "from", Fmt(from) } });
            _stuck.Reset();
            _outOfWorld.Reset();
            _graceUntil = t + RespawnGrace;
            _hasPrev = false;
            NewDestination(t);
        }

        /// <summary>Name and distance of the first solid thing between the player and where it is heading ("" if clear).</summary>
        string DescribeBlocker(Vector3 pos, Vector3? heading)
        {
            if (!heading.HasValue) return "";
            var dir = heading.Value - pos;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) return "";
            var r = _bodyRadius * 0.9f;
            var origin = new Vector3(pos.x, _player.FeetY + _bodyRadius + 0.3f, pos.z);
            foreach (var h in Physics.SphereCastAll(origin, r, dir.normalized, 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(_player.transform)) continue;
                return $"{h.collider.name} @{h.distance.ToString("0.##", CultureInfo.InvariantCulture)}m";
            }
            return "";
        }

        /// <summary>First walkable-looking surface below <paramref name="from"/> on the player's level.</summary>
        Vector3? GroundProbe(Vector3 from)
        {
            var hits = Physics.RaycastAll(from, Vector3.down, _world.size.y + 20f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(_player.transform)) continue;
                if (Mathf.Abs(h.point.y - _levelY) > _options.maxClimb) continue;
                if (h.normal.y < 0.7f) continue;
                return h.point;
            }
            return null;
        }

        /// <summary>Ground probe that also rejects spots inside solid objects (used for random targets).</summary>
        Vector3? PickerProbe(Vector3 from)
        {
            var g = GroundProbe(from);
            return g.HasValue && HasBodyClearance(g.Value) ? g : null;
        }

        bool HasBodyClearance(Vector3 ground)
        {
            // Keep a margin around walls: gliding along a corner at exactly body width can stall the controller.
            var r = _bodyRadius + 0.12f;
            foreach (var c in Physics.OverlapSphere(ground + Vector3.up * (r + 0.3f), r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!c.transform.IsChildOf(_player.transform)) return false;
            }
            return true;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0; b.y = 0;
            return Vector3.Distance(a, b);
        }

        internal static string Fmt(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}", v.x, v.y, v.z);
    }

    internal static class WorldBounds
    {
        static T[] Find<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }

        /// <summary>Bounds of all solid colliders not under <paramref name="exclude"/>; falls back to renderers.</summary>
        public static Bounds Compute(Transform exclude)
        {
            // Collider bounds are stale for objects moved or scaled since the last physics step.
            Physics.SyncTransforms();
            var has = false;
            var b = new Bounds();
            foreach (var c in Find<Collider>())
            {
                if (c.isTrigger || !c.enabled || (exclude != null && c.transform.IsChildOf(exclude))) continue;
                if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
            }
            if (has) return b;
            foreach (var r in Find<Renderer>())
            {
                if (exclude != null && r.transform.IsChildOf(exclude)) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            if (has) return b;
            return new Bounds(exclude != null ? exclude.position : Vector3.zero, Vector3.one * 100f);
        }
    }
}
