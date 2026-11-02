using System.Collections.Generic;
using UnityEngine;

namespace PlayGoblin
{
    /// <summary>
    /// Fires when the player has been trying to move for <see cref="Window"/> seconds
    /// but everywhere it went in that window fits inside a box with a diagonal under <see cref="MinDistance"/> meters.
    /// Using the whole window (not just start vs. end) means a walk out and back is not mistaken for being stuck.
    /// Pure logic, no Unity calls, so it's unit-testable.
    /// </summary>
    public sealed class StuckDetector
    {
        public float Window = 5f;
        public float MinDistance = 0.5f;

        readonly List<(float t, Vector3 p)> _samples = new List<(float, Vector3)>();
        float _tryingSince = -1f;

        public bool Feed(float t, Vector3 p, bool tryingToMove)
        {
            if (!tryingToMove) { Reset(); return false; }
            if (_tryingSince < 0) _tryingSince = t;

            _samples.Add((t, p));
            // Keep the newest sample that is at least Window old as the window start.
            while (_samples.Count >= 2 && _samples[1].t <= t - Window) _samples.RemoveAt(0);

            if (t - _tryingSince < Window) return false;
            if (t - _samples[0].t < Window) return false;

            var min = _samples[0].p;
            var max = min;
            foreach (var s in _samples)
            {
                min = Vector3.Min(min, s.p);
                max = Vector3.Max(max, s.p);
            }

            if (Vector3.Distance(min, max) < MinDistance)
            {
                Reset();
                return true;
            }
            return false;
        }

        public void Reset()
        {
            _samples.Clear();
            _tryingSince = -1f;
        }
    }

    /// <summary>
    /// Fires when the player drops below the level (<see cref="KillY"/>), has an invalid position,
    /// or has had no NavMesh under it for <see cref="OffNavMeshSeconds"/>.
    /// Returns a reason string, or null.
    /// </summary>
    public sealed class OutOfWorldDetector
    {
        public float KillY = -100f;
        public float OffNavMeshSeconds = 3f;

        float _offSince = -1f;

        public string Feed(float t, Vector3 p, bool? onNavMesh)
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.y))
            {
                Reset();
                return "invalid_position";
            }
            if (p.y < KillY)
            {
                Reset();
                return "below_world";
            }
            if (onNavMesh == false)
            {
                if (_offSince < 0) _offSince = t;
                if (t - _offSince >= OffNavMeshSeconds)
                {
                    Reset();
                    return "off_navmesh";
                }
            }
            else
            {
                _offSince = -1f;
            }
            return null;
        }

        public void Reset() { _offSince = -1f; }
    }

    /// <summary>Counts visits per grid cell so the explorer can prefer places it hasn't been.</summary>
    public sealed class CoverageGrid
    {
        readonly float _cell;
        readonly Dictionary<long, int> _visits = new Dictionary<long, int>();
        readonly HashSet<long> _reachable = new HashSet<long>();

        public CoverageGrid(float cellSize = 2f) { _cell = Mathf.Max(0.1f, cellSize); }

        public long Key(Vector3 p)
        {
            var x = Mathf.FloorToInt(p.x / _cell);
            var z = Mathf.FloorToInt(p.z / _cell);
            return ((long)x << 32) ^ (uint)z;
        }

        public void Visit(Vector3 p)
        {
            var k = Key(p);
            _visits.TryGetValue(k, out var n);
            _visits[k] = n + 1;
        }

        public int Visits(Vector3 p) => _visits.TryGetValue(Key(p), out var n) ? n : 0;

        public void MarkReachable(Vector3 p) => _reachable.Add(Key(p));

        public int ReachableCells => _reachable.Count;

        /// <summary>Share of reachable cells visited at least once (0..1). 0 when reachability is unknown.</summary>
        public float Coverage
        {
            get
            {
                if (_reachable.Count == 0) return 0f;
                var seen = 0;
                foreach (var k in _reachable) if (_visits.ContainsKey(k)) seen++;
                return (float)seen / _reachable.Count;
            }
        }
    }
}
