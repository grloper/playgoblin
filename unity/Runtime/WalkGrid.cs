using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayGoblin
{
    /// <summary>
    /// A walkability grid over the level, used when the scene has no NavMesh. Each cell is walkable when a probe
    /// finds ground there and the body fits. Gives A* paths around obstacles and the set of cells the player
    /// can really reach, so coverage and "trapped in a pocket" checks are accurate. Pure logic: ground and
    /// clearance come in as delegates, so it's unit-testable without a scene.
    /// </summary>
    public sealed class WalkGrid
    {
        public const int MaxCells = 250_000;

        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        readonly int _nx, _nz;
        readonly float _minX, _minZ, _maxStep;
        readonly float[] _y;
        readonly bool[] _walk;
        bool[] _reach;
        List<Vector3> _reachList = new List<Vector3>();

        // A* scratch space, reused between calls.
        float[] _g;
        int[] _parent;
        int[] _stamp;
        int _stampNow;

        public float Cell { get; }
        public int WalkableCount { get; private set; }
        public int ReachableCount => _reachList.Count;
        public IReadOnlyList<Vector3> ReachableCells => _reachList;

        WalkGrid(Bounds b, float cell, float maxStep, int nx, int nz)
        {
            Cell = cell; _maxStep = maxStep; _nx = nx; _nz = nz;
            _minX = b.min.x; _minZ = b.min.z;
            _y = new float[nx * nz];
            _walk = new bool[nx * nz];
            _reach = new bool[nx * nz];
        }

        public static WalkGrid Build(Bounds bounds, float cell, float maxStep,
            Func<Vector3, Vector3?> ground, Func<Vector3, bool> clear)
        {
            cell = Mathf.Max(0.25f, cell);
            var sx = Mathf.Max(bounds.size.x, cell);
            var sz = Mathf.Max(bounds.size.z, cell);
            if (sx * sz / (cell * cell) > MaxCells) cell = Mathf.Sqrt(sx * sz / MaxCells);
            var nx = Mathf.Max(1, Mathf.CeilToInt(sx / cell));
            var nz = Mathf.Max(1, Mathf.CeilToInt(sz / cell));

            var g = new WalkGrid(bounds, cell, maxStep, nx, nz);
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    var from = new Vector3(g._minX + (ix + 0.5f) * cell, bounds.max.y + 1f, g._minZ + (iz + 0.5f) * cell);
                    var hit = ground(from);
                    if (!hit.HasValue || !clear(hit.Value)) continue;
                    var i = iz * nx + ix;
                    g._walk[i] = true;
                    g._y[i] = hit.Value.y;
                    g.WalkableCount++;
                }
            }
            return g;
        }

        /// <summary>Marks every cell connected to the cell nearest <paramref name="start"/>. Returns false if none is near.</summary>
        public bool FloodFrom(Vector3 start)
        {
            Array.Clear(_reach, 0, _reach.Length);
            _reachList = new List<Vector3>();
            if (!NearestWalkable(start, 4, false, out var s)) return false;

            var queue = new Queue<int>();
            _reach[s] = true;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                var ix = i % _nx; var iz = i / _nx;
                for (int d = 0; d < 8; d++)
                {
                    if (!CanStep(ix, iz, d, out var j) || _reach[j]) continue;
                    _reach[j] = true;
                    queue.Enqueue(j);
                }
            }
            for (int i = 0; i < _reach.Length; i++) if (_reach[i]) _reachList.Add(CellCenter(i));
            return true;
        }

        public bool IsReachable(Vector3 p) => NearestWalkable(p, 0, true, out _);

        /// <summary>Shortest walkable path (A*), straight runs merged. Null when either end is unreachable.</summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            if (!NearestWalkable(from, 4, true, out var start)) return null;
            if (!NearestWalkable(to, 4, true, out var goal)) return null;
            if (start == goal) return new List<Vector3> { CellCenter(goal) };

            var n = _nx * _nz;
            if (_g == null) { _g = new float[n]; _parent = new int[n]; _stamp = new int[n]; }
            _stampNow++;
            var gx = goal % _nx; var gz = goal / _nx;

            var heap = new MinHeap();
            _g[start] = 0; _parent[start] = -1; _stamp[start] = _stampNow;
            heap.Push(Heuristic(start % _nx, start / _nx, gx, gz), start);
            var closed = new HashSet<int>();

            while (heap.Count > 0)
            {
                var cur = heap.Pop();
                if (cur == goal) return Reconstruct(cur);
                if (!closed.Add(cur)) continue;
                var ix = cur % _nx; var iz = cur / _nx;
                for (int d = 0; d < 8; d++)
                {
                    if (!CanStep(ix, iz, d, out var nb) || closed.Contains(nb)) continue;
                    var cost = _g[cur] + (d < 4 ? 1f : 1.4142135f);
                    if (_stamp[nb] == _stampNow && cost >= _g[nb]) continue;
                    _stamp[nb] = _stampNow; _g[nb] = cost; _parent[nb] = cur;
                    heap.Push(cost + Heuristic(nb % _nx, nb / _nx, gx, gz), nb);
                }
            }
            return null;
        }

        List<Vector3> Reconstruct(int goal)
        {
            var cells = new List<int>();
            for (var i = goal; i >= 0; i = _parent[i]) cells.Add(i);
            cells.Reverse();

            // Drop the start cell and every node that continues in the same direction.
            var path = new List<Vector3>();
            for (int k = 1; k < cells.Count; k++)
            {
                var last = k == cells.Count - 1;
                if (!last)
                {
                    var dPrev = Dir(cells[k - 1], cells[k]);
                    var dNext = Dir(cells[k], cells[k + 1]);
                    if (dPrev == dNext) continue;
                }
                path.Add(CellCenter(cells[k]));
            }
            return path;
        }

        (int, int) Dir(int a, int b) => (b % _nx - a % _nx, b / _nx - a / _nx);

        static float Heuristic(int ax, int az, int bx, int bz)
        {
            var dx = Mathf.Abs(ax - bx); var dz = Mathf.Abs(az - bz);
            return Mathf.Max(dx, dz) + 0.4142135f * Mathf.Min(dx, dz);
        }

        bool CanStep(int ix, int iz, int d, out int j)
        {
            j = -1;
            var nx = ix + Dx[d]; var nz = iz + Dz[d];
            if (!Ok(ix, iz, nx, nz)) return false;
            if (d >= 4 && (!Ok(ix, iz, nx, iz) || !Ok(ix, iz, ix, nz))) return false;
            j = nz * _nx + nx;
            return true;
        }

        bool Ok(int ax, int az, int bx, int bz)
        {
            if (bx < 0 || bz < 0 || bx >= _nx || bz >= _nz) return false;
            var b = bz * _nx + bx;
            return _walk[b] && Mathf.Abs(_y[b] - _y[az * _nx + ax]) <= _maxStep;
        }

        bool NearestWalkable(Vector3 p, int radius, bool requireReachable, out int index)
        {
            index = -1;
            var cx = Mathf.FloorToInt((p.x - _minX) / Cell);
            var cz = Mathf.FloorToInt((p.z - _minZ) / Cell);
            var best = float.MaxValue;
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var ix = cx + dx; var iz = cz + dz;
                    if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) continue;
                    var i = iz * _nx + ix;
                    if (!_walk[i] || (requireReachable && !_reach[i])) continue;
                    var c = CellCenter(i);
                    var dist = (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z);
                    if (dist < best) { best = dist; index = i; }
                }
            }
            return index >= 0;
        }

        Vector3 CellCenter(int i) =>
            new Vector3(_minX + (i % _nx + 0.5f) * Cell, _y[i], _minZ + (i / _nx + 0.5f) * Cell);

        sealed class MinHeap
        {
            readonly List<(float key, int value)> _a = new List<(float, int)>();
            public int Count => _a.Count;

            public void Push(float key, int value)
            {
                _a.Add((key, value));
                var i = _a.Count - 1;
                while (i > 0)
                {
                    var p = (i - 1) / 2;
                    if (_a[p].key <= _a[i].key) break;
                    (_a[p], _a[i]) = (_a[i], _a[p]);
                    i = p;
                }
            }

            public int Pop()
            {
                var top = _a[0].value;
                var last = _a[_a.Count - 1];
                _a.RemoveAt(_a.Count - 1);
                if (_a.Count > 0)
                {
                    _a[0] = last;
                    var i = 0;
                    while (true)
                    {
                        var l = 2 * i + 1; var r = l + 1; var m = i;
                        if (l < _a.Count && _a[l].key < _a[m].key) m = l;
                        if (r < _a.Count && _a[r].key < _a[m].key) m = r;
                        if (m == i) break;
                        (_a[m], _a[i]) = (_a[i], _a[m]);
                        i = m;
                    }
                }
                return top;
            }
        }
    }
}
