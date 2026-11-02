using System;
using NUnit.Framework;
using UnityEngine;

namespace PlayGoblin.Tests
{
    public class WalkGridTests
    {
        static readonly Bounds Area = new Bounds(Vector3.zero, new Vector3(20, 2, 20));

        static Vector3? Flat(Vector3 from) => new Vector3(from.x, 0, from.z);
        static bool AllClear(Vector3 p) => true;

        static WalkGrid Make(Func<Vector3, bool> clear, Func<Vector3, Vector3?> ground = null, float maxStep = 0.6f, Bounds? area = null)
            => WalkGrid.Build(area ?? Area, 1f, maxStep, ground ?? Flat, clear);

        [Test]
        public void OpenFloor_EveryWalkableCellIsReachable()
        {
            var g = Make(AllClear);
            Assert.AreEqual(400, g.WalkableCount);
            Assert.IsTrue(g.FloodFrom(Vector3.zero));
            Assert.AreEqual(g.WalkableCount, g.ReachableCount);
        }

        [Test]
        public void BlockedCells_AreNotWalkable()
        {
            var g = Make(p => !(p.x > -0.6f && p.x < 0.6f));
            Assert.Less(g.WalkableCount, 400);
            Assert.AreEqual(360, g.WalkableCount, "cells at x=-0.5 and x=0.5 are both blocked");
        }

        [Test]
        public void WallWithGap_PathGoesThroughTheGap()
        {
            // Wall at x=0 from z=-10 to z=6; gap above z=6.
            bool Clear(Vector3 p) => !(Mathf.Abs(p.x) < 0.6f && p.z < 6f);
            var g = Make(Clear);
            g.FloodFrom(new Vector3(-5, 0, 0));
            var path = g.FindPath(new Vector3(-5, 0, 0), new Vector3(5, 0, 0));
            Assert.IsNotNull(path);
            Assert.Greater(path.Count, 0);
            var maxZ = float.MinValue;
            foreach (var n in path)
            {
                Assert.IsTrue(Clear(n), "path node inside the wall: " + n);
                maxZ = Mathf.Max(maxZ, n.z);
            }
            Assert.GreaterOrEqual(maxZ, 6f, "path must go up to the gap to get across");
            Assert.AreEqual(5f, path[path.Count - 1].x, 0.6f);
        }

        [Test]
        public void FullWall_IsolatesTheOtherSide()
        {
            var g = Make(p => Mathf.Abs(p.x) >= 0.6f);
            g.FloodFrom(new Vector3(-5, 0, 0));
            Assert.Less(g.ReachableCount, g.WalkableCount);
            Assert.IsNull(g.FindPath(new Vector3(-5, 0, 0), new Vector3(5, 0, 0)));
            Assert.IsFalse(g.IsReachable(new Vector3(5, 0, 0)));
            Assert.IsTrue(g.IsReachable(new Vector3(-5, 0, 0)));
        }

        [Test]
        public void StepsHigherThanMaxStep_BlockMovement()
        {
            Vector3? Ledge(Vector3 v) => new Vector3(v.x, v.x < 0 ? 0f : 1f, v.z);
            var tall = Make(AllClear, Ledge, 0.6f);
            tall.FloodFrom(new Vector3(-5, 0, 0));
            Assert.IsNull(tall.FindPath(new Vector3(-5, 0, 0), new Vector3(5, 1, 0)), "1 m step is too high for 0.6 m");

            var ok = Make(AllClear, Ledge, 1.5f);
            ok.FloodFrom(new Vector3(-5, 0, 0));
            Assert.IsNotNull(ok.FindPath(new Vector3(-5, 0, 0), new Vector3(5, 1, 0)));
        }

        [Test]
        public void DiagonalMoves_CannotCutBlockedCorners()
        {
            // A checkerboard-style diagonal gap: (0,0) and (1,1) open but (1,0) and (0,1) blocked.
            bool Clear(Vector3 p)
            {
                var ix = Mathf.FloorToInt(p.x); var iz = Mathf.FloorToInt(p.z);
                return !((ix == 1 && iz == 0) || (ix == 0 && iz == 1));
            }
            var g = Make(Clear, area: new Bounds(new Vector3(1, 0, 1), new Vector3(2, 2, 2)));
            g.FloodFrom(new Vector3(0.5f, 0, 0.5f));
            Assert.AreEqual(1, g.ReachableCount, "diagonal step between two blocked orthogonals must be refused");
        }

        [Test]
        public void NoGround_MeansNothingWalkable()
        {
            var g = Make(AllClear, _ => null);
            Assert.AreEqual(0, g.WalkableCount);
            Assert.IsFalse(g.FloodFrom(Vector3.zero));
        }

        [Test]
        public void StartingOutsideTheGrid_FindsNearbyCellOnlyWithinRadius()
        {
            var g = Make(AllClear);
            Assert.IsTrue(g.FloodFrom(new Vector3(10.5f, 0, 0)), "just outside the edge snaps to the nearest cell");
            Assert.IsFalse(g.FloodFrom(new Vector3(100, 0, 100)), "far outside must not snap");
        }

        [Test]
        public void VeryLargeLevels_AreCoarsenedToTheCellCap()
        {
            var g = WalkGrid.Build(new Bounds(Vector3.zero, new Vector3(5000, 2, 5000)), 1f, 0.6f, Flat, AllClear);
            Assert.LessOrEqual(g.WalkableCount, WalkGrid.MaxCells + 2000);
            Assert.Greater(g.Cell, 1f);
        }

        [Test]
        public void StraightRuns_AreMergedIntoOneSegment()
        {
            var g = Make(AllClear);
            g.FloodFrom(Vector3.zero);
            var path = g.FindPath(new Vector3(-8, 0, 0.5f), new Vector3(8, 0, 0.5f));
            Assert.IsNotNull(path);
            Assert.LessOrEqual(path.Count, 2);
        }

        [Test]
        public void PathToTheSameCell_IsASingleNode()
        {
            var g = Make(AllClear);
            g.FloodFrom(Vector3.zero);
            var path = g.FindPath(new Vector3(0.2f, 0, 0.2f), new Vector3(0.3f, 0, 0.3f));
            Assert.AreEqual(1, path.Count);
        }

        [Test]
        public void Paths_AreNearOptimal()
        {
            var g = Make(AllClear);
            g.FloodFrom(Vector3.zero);
            var from = new Vector3(-8.5f, 0, -8.5f);
            var path = g.FindPath(from, new Vector3(8.5f, 0, 8.5f));
            var len = 0f; var prev = new Vector3(-8.5f, 0, -8.5f);
            foreach (var n in path) { len += Vector3.Distance(prev, n); prev = n; }
            Assert.AreEqual(Vector3.Distance(from, new Vector3(8.5f, 0, 8.5f)), len, 0.5f, "open floor diagonal should be a straight line");
        }

        [Test]
        public void RepeatedPathQueries_GiveTheSameAnswer()
        {
            bool Clear(Vector3 p) => !(Mathf.Abs(p.x) < 0.6f && p.z < 6f);
            var g = Make(Clear);
            g.FloodFrom(new Vector3(-5, 0, 0));
            var a = g.FindPath(new Vector3(-5, 0, 0), new Vector3(5, 0, 0));
            var b = g.FindPath(new Vector3(-5, 0, 0), new Vector3(5, 0, 0));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
        }
    }
}
