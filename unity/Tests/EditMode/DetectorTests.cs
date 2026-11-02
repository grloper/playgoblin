using NUnit.Framework;
using UnityEngine;

namespace PlayGoblin.Tests
{
    public class StuckDetectorTests
    {
        [Test]
        public void Fires_WhenTryingToMoveButNotMoving()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t <= 6f; t += 0.1f) fired |= d.Feed(t, Vector3.zero, true);
            Assert.IsTrue(fired);
        }

        [Test]
        public void DoesNotFire_WhenMoving()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t <= 20f; t += 0.1f) fired |= d.Feed(t, new Vector3(t * 2f, 0, 0), true);
            Assert.IsFalse(fired);
        }

        [Test]
        public void DoesNotFire_WhenNotTryingToMove()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t <= 20f; t += 0.1f) fired |= d.Feed(t, Vector3.zero, false);
            Assert.IsFalse(fired);
        }

        [Test]
        public void DoesNotFire_BeforeWindowElapses()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t < 4.5f; t += 0.1f) fired |= d.Feed(t, Vector3.zero, true);
            Assert.IsFalse(fired);
        }

        [Test]
        public void ResetsAfterFiring_SoItFiresAgainOnlyAfterAnotherWindow()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fires = 0;
            for (float t = 0; t <= 12f; t += 0.1f) if (d.Feed(t, Vector3.zero, true)) fires++;
            Assert.AreEqual(2, fires);
        }

        [Test]
        public void SlowDrift_BelowMinDistance_StillCountsAsStuck()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t <= 6f; t += 0.1f) fired |= d.Feed(t, new Vector3(t * 0.02f, 0, 0), true);
            Assert.IsTrue(fired);
        }

        [Test]
        public void WalkingOutAndBack_ToTheSameSpot_IsNotStuck()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            // Triangle wave: 14 m out and 14 m back every 5 s, so the position after one window equals the start.
            for (float t = 0; t <= 12f; t += 0.1f)
            {
                var phase = t % 5f;
                var x = phase < 2.5f ? phase * 5.6f : (5f - phase) * 5.6f;
                fired |= d.Feed(t, new Vector3(x, 0, 0), true);
            }
            Assert.IsFalse(fired, "a round trip is progress, not a stall");
        }

        [Test]
        public void JitteringInPlace_WithinHalfAMeter_IsStuck()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t <= 6f; t += 0.1f)
                fired |= d.Feed(t, new Vector3(Mathf.Sin(t * 20f) * 0.1f, 0, Mathf.Cos(t * 20f) * 0.1f), true);
            Assert.IsTrue(fired);
        }

        [Test]
        public void PausingIntent_ResetsTheWindow()
        {
            var d = new StuckDetector { Window = 5f, MinDistance = 0.5f };
            var fired = false;
            for (float t = 0; t < 4f; t += 0.1f) fired |= d.Feed(t, Vector3.zero, true);
            d.Feed(4f, Vector3.zero, false);
            for (float t = 4.1f; t < 8f; t += 0.1f) fired |= d.Feed(t, Vector3.zero, true);
            Assert.IsFalse(fired, "window must restart after intent pauses");
        }
    }

    public class OutOfWorldDetectorTests
    {
        [Test]
        public void BelowKillY_Fires()
        {
            var d = new OutOfWorldDetector { KillY = -10f };
            Assert.AreEqual("below_world", d.Feed(1f, new Vector3(0, -11f, 0), null));
        }

        [Test]
        public void AboveKillY_DoesNotFire()
        {
            var d = new OutOfWorldDetector { KillY = -10f };
            Assert.IsNull(d.Feed(1f, new Vector3(0, -9f, 0), null));
        }

        [Test]
        public void NaN_IsInvalidPosition()
        {
            var d = new OutOfWorldDetector();
            Assert.AreEqual("invalid_position", d.Feed(1f, new Vector3(float.NaN, 0, 0), null));
        }

        [Test]
        public void OffNavMesh_FiresOnlyAfterTheDelay()
        {
            var d = new OutOfWorldDetector { OffNavMeshSeconds = 3f };
            Assert.IsNull(d.Feed(0f, Vector3.zero, false));
            Assert.IsNull(d.Feed(2.9f, Vector3.zero, false));
            Assert.AreEqual("off_navmesh", d.Feed(3.1f, Vector3.zero, false));
        }

        [Test]
        public void ReturningToNavMesh_ResetsTheTimer()
        {
            var d = new OutOfWorldDetector { OffNavMeshSeconds = 3f };
            d.Feed(0f, Vector3.zero, false);
            d.Feed(2f, Vector3.zero, true);
            Assert.IsNull(d.Feed(4f, Vector3.zero, false));
            Assert.IsNull(d.Feed(6.5f, Vector3.zero, false));
            Assert.AreEqual("off_navmesh", d.Feed(7.1f, Vector3.zero, false));
        }

        [Test]
        public void UnknownNavMesh_NeverFiresOffNavMesh()
        {
            var d = new OutOfWorldDetector { OffNavMeshSeconds = 1f };
            for (float t = 0; t < 10f; t += 0.5f) Assert.IsNull(d.Feed(t, Vector3.zero, null));
        }
    }

    public class CoverageGridTests
    {
        [Test]
        public void Coverage_IsZero_WhenReachabilityUnknown()
        {
            var g = new CoverageGrid(2f);
            g.Visit(Vector3.zero);
            Assert.AreEqual(0f, g.Coverage);
        }

        [Test]
        public void Coverage_CountsVisitedReachableCells()
        {
            var g = new CoverageGrid(2f);
            g.MarkReachable(new Vector3(1, 0, 1));
            g.MarkReachable(new Vector3(3, 0, 1));
            g.MarkReachable(new Vector3(5, 0, 1));
            g.MarkReachable(new Vector3(7, 0, 1));
            g.Visit(new Vector3(1.5f, 0, 1));
            g.Visit(new Vector3(7.5f, 0, 1));
            Assert.AreEqual(0.5f, g.Coverage, 0.0001f);
        }

        [Test]
        public void VisitingUnreachableCells_DoesNotInflateCoverage()
        {
            var g = new CoverageGrid(2f);
            g.MarkReachable(new Vector3(1, 0, 1));
            g.Visit(new Vector3(100, 0, 100));
            Assert.AreEqual(0f, g.Coverage);
        }

        [Test]
        public void NegativeCoordinates_GetDistinctCells()
        {
            var g = new CoverageGrid(2f);
            Assert.AreNotEqual(g.Key(new Vector3(-1, 0, 1)), g.Key(new Vector3(1, 0, 1)));
            Assert.AreNotEqual(g.Key(new Vector3(1, 0, -1)), g.Key(new Vector3(1, 0, 1)));
        }

        [Test]
        public void Visits_CountsRepeats()
        {
            var g = new CoverageGrid(2f);
            g.Visit(new Vector3(1, 0, 1));
            g.Visit(new Vector3(1.2f, 0, 1.2f));
            Assert.AreEqual(2, g.Visits(new Vector3(0.5f, 0, 0.5f)));
        }
    }

    public class DestinationPickerTests
    {
        static Vector3? Flat(Vector3 from) => new Vector3(from.x, 0, from.z);

        [Test]
        public void SameSeed_GivesSamePoints()
        {
            var b = new Bounds(Vector3.zero, new Vector3(100, 10, 100));
            var a = DestinationPicker.FromBounds(7, b, Flat);
            var c = DestinationPicker.FromBounds(7, b, Flat);
            for (int i = 0; i < 20; i++) Assert.AreEqual(a.RandomPoint(), c.RandomPoint());
        }

        [Test]
        public void DifferentSeeds_GiveDifferentPoints()
        {
            var b = new Bounds(Vector3.zero, new Vector3(100, 10, 100));
            var a = DestinationPicker.FromBounds(1, b, Flat).RandomPoint();
            var c = DestinationPicker.FromBounds(2, b, Flat).RandomPoint();
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void Points_StayInsideBounds()
        {
            var b = new Bounds(new Vector3(10, 0, -5), new Vector3(20, 4, 8));
            var p = DestinationPicker.FromBounds(3, b, Flat);
            for (int i = 0; i < 200; i++)
            {
                var pt = p.RandomPoint().Value;
                Assert.GreaterOrEqual(pt.x, b.min.x);
                Assert.LessOrEqual(pt.x, b.max.x);
                Assert.GreaterOrEqual(pt.z, b.min.z);
                Assert.LessOrEqual(pt.z, b.max.z);
            }
        }

        [Test]
        public void NoGround_ReturnsNull()
        {
            var b = new Bounds(Vector3.zero, new Vector3(10, 10, 10));
            var p = DestinationPicker.FromBounds(3, b, _ => null);
            Assert.IsNull(p.RandomPoint());
        }

        [Test]
        public void Pick_PrefersLessVisitedCells()
        {
            var b = new Bounds(new Vector3(5, 0, 5), new Vector3(10, 4, 10));
            var grid = new CoverageGrid(2f);
            // Mark everything left of x=5 as heavily visited.
            for (float x = 0; x < 5f; x += 0.5f)
                for (float z = 0; z < 10f; z += 0.5f)
                    for (int k = 0; k < 5; k++) grid.Visit(new Vector3(x, 0, z));

            var picker = DestinationPicker.FromBounds(11, b, Flat);
            int right = 0;
            for (int i = 0; i < 100; i++) if (picker.Pick(grid, 8).Value.x >= 5f) right++;
            Assert.Greater(right, 85, "picker should strongly favour unvisited cells");
        }
    }
}
