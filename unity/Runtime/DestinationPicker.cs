using System;
using UnityEngine;
using UnityEngine.AI;

namespace PlayGoblin
{
    /// <summary>
    /// Picks seeded random walk targets: area-weighted points on the NavMesh when there is one,
    /// otherwise points inside the level bounds that have ground under them.
    /// </summary>
    public sealed class DestinationPicker
    {
        readonly System.Random _rng;
        readonly Vector3[] _verts;
        readonly int[] _tris;
        readonly float[] _cumArea;
        readonly Bounds _bounds;
        readonly Func<Vector3, Vector3?> _groundProbe;

        public bool HasNavMesh => _cumArea != null && _cumArea.Length > 0;

        DestinationPicker(int seed, Vector3[] verts, int[] tris, Bounds bounds, Func<Vector3, Vector3?> groundProbe)
        {
            _rng = new System.Random(seed);
            _bounds = bounds;
            _groundProbe = groundProbe;
            if (verts == null || tris == null || tris.Length < 3) return;

            _verts = verts;
            _tris = tris;
            _cumArea = new float[tris.Length / 3];
            float total = 0;
            for (int i = 0; i < _cumArea.Length; i++)
            {
                var a = verts[tris[i * 3]];
                var b = verts[tris[i * 3 + 1]];
                var c = verts[tris[i * 3 + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                _cumArea[i] = total;
            }
            if (total <= 0f) _cumArea = null;
        }

        public static DestinationPicker FromNavMesh(int seed, Bounds fallbackBounds, Func<Vector3, Vector3?> groundProbe)
        {
            var tri = NavMesh.CalculateTriangulation();
            return new DestinationPicker(seed, tri.vertices, tri.indices, fallbackBounds, groundProbe);
        }

        public static DestinationPicker FromBounds(int seed, Bounds bounds, Func<Vector3, Vector3?> groundProbe)
        {
            return new DestinationPicker(seed, null, null, bounds, groundProbe);
        }

        public Vector3? RandomPoint()
        {
            if (HasNavMesh) return RandomNavMeshPoint();
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var p = new Vector3(
                    Lerp(_bounds.min.x, _bounds.max.x, Next()),
                    _bounds.max.y + 1f,
                    Lerp(_bounds.min.z, _bounds.max.z, Next()));
                var ground = _groundProbe?.Invoke(p);
                if (ground.HasValue) return ground;
            }
            return null;
        }

        /// <summary>Samples several candidates and returns the one in the least-visited cell (first wins ties).</summary>
        public Vector3? Pick(CoverageGrid coverage, int candidates = 8)
        {
            Vector3? best = null;
            var bestVisits = int.MaxValue;
            for (int i = 0; i < candidates; i++)
            {
                var p = RandomPoint();
                if (!p.HasValue) continue;
                var v = coverage?.Visits(p.Value) ?? 0;
                if (v < bestVisits) { best = p; bestVisits = v; }
            }
            return best;
        }

        Vector3 RandomNavMeshPoint()
        {
            var total = _cumArea[_cumArea.Length - 1];
            var r = Next() * total;
            var i = Array.BinarySearch(_cumArea, r);
            if (i < 0) i = ~i;
            if (i >= _cumArea.Length) i = _cumArea.Length - 1;

            var a = _verts[_tris[i * 3]];
            var b = _verts[_tris[i * 3 + 1]];
            var c = _verts[_tris[i * 3 + 2]];
            var r1 = Next();
            var r2 = Next();
            if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
            return a + r1 * (b - a) + r2 * (c - a);
        }

        float Next() => (float)_rng.NextDouble();
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
