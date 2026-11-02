using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGoblin.Tests
{
    public class WalkTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        string _outRoot;

        [SetUp]
        public void SetUp()
        {
            _outRoot = Path.Combine(Path.GetTempPath(), "playgoblin-tests-" + System.Guid.NewGuid().ToString("N"));
            Time.timeScale = 4f;
            LogAssert.ignoreFailingMessages = true; // tests deliberately log errors
        }

        [TearDown]
        public void TearDown()
        {
            if (PlayGoblinRun.Current != null) PlayGoblinRun.Current.Stop();
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
            try { if (Directory.Exists(_outRoot)) Directory.Delete(_outRoot, true); } catch { /* best effort */ }
        }

        GameObject Floor(float size = 40f)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
            f.name = "Floor";
            f.transform.position = new Vector3(0, -0.5f, 0);
            f.transform.localScale = new Vector3(size, 1f, size);
            _spawned.Add(f);
            return f;
        }

        GameObject Player(Vector3 at)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            p.name = "Player";
            Object.Destroy(p.GetComponent<CapsuleCollider>());
            p.AddComponent<CharacterController>();
            p.AddComponent<PlayGoblinPlayer>().moveSpeed = 6f;
            p.transform.position = at;
            _spawned.Add(p);
            return p;
        }

        GameObject Wall(Vector3 center, Vector3 size)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = "Wall";
            w.transform.position = center;
            w.transform.localScale = size;
            _spawned.Add(w);
            return w;
        }

        PlayGoblinRunOptions Options(float gameSeconds, int seed = 1) => new PlayGoblinRunOptions
        {
            seed = seed, minutes = gameSeconds / 60f, stuckSeconds = 2f, outputRoot = _outRoot, waitForPlayerSeconds = 2f
        };

        IEnumerator WaitForEnd(float realSeconds = 60f)
        {
            var end = Time.realtimeSinceStartup + realSeconds;
            while (PlayGoblinRun.Snapshot().State != PlayGoblinRunState.Ended && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(PlayGoblinRunState.Ended, PlayGoblinRun.Snapshot().State, "run did not finish in time");
        }

        static List<EventLine> ReadEvents(string runDir)
        {
            var list = new List<EventLine>();
            foreach (var line in File.ReadAllLines(Path.Combine(runDir, "events.jsonl")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonUtility.FromJson<EventLine>(line);
                Assert.IsNotNull(e, "line is not valid JSON: " + line);
                Assert.IsNotEmpty(e.type, "line has no type: " + line);
                list.Add(e);
            }
            return list;
        }

        [System.Serializable]
        class EventLine { public long seq; public double t; public string type; public string detail; }

        static int Count(List<EventLine> events, string type) => events.FindAll(e => e.type == type).Count;

        [UnityTest]
        public IEnumerator WalksAFlatFloor_AndWritesAValidEventLog()
        {
            Floor(); Player(new Vector3(0, 1, 0));
            yield return null;

            var run = PlayGoblinRun.StartRun(Options(12f));
            yield return WaitForEnd();

            var snap = PlayGoblinRun.Snapshot();
            Assert.AreEqual("completed", snap.EndReason);
            var events = ReadEvents(snap.RunDirectory);
            Assert.AreEqual("run_start", events[0].type);
            Assert.AreEqual("run_end", events[events.Count - 1].type);
            Assert.AreEqual(1, Count(events, "player_found"));
            Assert.Greater(Count(events, "waypoint"), 1, "should have chosen several waypoints");
            Assert.Greater(Count(events, "pos"), 5, "should sample positions");
            Assert.AreEqual(0, Count(events, "stuck"), "open floor must not report stuck");
            Assert.AreEqual(0, Count(events, "out_of_world"));

            for (int i = 1; i < events.Count; i++) Assert.Greater(events[i].seq, events[i - 1].seq, "seq must increase");

            var summary = File.ReadAllText(Path.Combine(snap.RunDirectory, "run.json"));
            StringAssert.Contains("\"end_reason\":\"completed\"", summary);
            StringAssert.Contains("\"mover\":\"CharacterController\"", summary);
            Assert.IsNotNull(JsonUtility.FromJson<SummaryLine>(summary));
            Assert.Greater(JsonUtility.FromJson<SummaryLine>(summary).distance_m, 5f, "player should have actually walked");
            Assert.AreEqual(0, JsonUtility.FromJson<SummaryLine>(summary).stuck);
        }

        [System.Serializable]
        class SummaryLine { public string run_id; public float distance_m; public int stuck; public int out_of_world; public int respawns; public int walkable_cells; public int reachable_cells; }

        [UnityTest]
        public IEnumerator PlayerWalledIn_IsReportedTrapped_WithoutSpammingEvents()
        {
            Floor(60f);
            Player(new Vector3(0, 1, 0));
            // Closed 3x3 box around the player; the rest of the floor is unreachable from inside.
            foreach (var (c, s) in new[] {
                (new Vector3(2, 1.5f, 0), new Vector3(0.5f, 3, 4.5f)), (new Vector3(-2, 1.5f, 0), new Vector3(0.5f, 3, 4.5f)),
                (new Vector3(0, 1.5f, 2), new Vector3(4.5f, 3, 0.5f)), (new Vector3(0, 1.5f, -2), new Vector3(4.5f, 3, 0.5f)) })
                Wall(c, s);
            yield return null;

            PlayGoblinRun.StartRun(Options(20f));
            yield return WaitForEnd();

            var snap = PlayGoblinRun.Snapshot();
            var events = ReadEvents(snap.RunDirectory);
            Assert.AreEqual(1, Count(events, "trapped"), "a walled-in player must be reported as trapped, once");
            Assert.Less(Count(events, "waypoint"), 120, "tiny reachable area must not flood the log");
            var summary = JsonUtility.FromJson<SummaryLine>(File.ReadAllText(Path.Combine(snap.RunDirectory, "run.json")));
            Assert.Less(summary.reachable_cells, summary.walkable_cells / 5);
        }

        [UnityTest]
        public IEnumerator WallAppearingMidRun_IsReportedStuck()
        {
            Floor(40f);
            var player = Player(new Vector3(0, 1, 0));
            yield return null;

            PlayGoblinRun.StartRun(Options(40f, 3));
            yield return new WaitForSeconds(0.5f);
            // Not known to the planner (built at run start), so paths through it get blocked.
            Wall(new Vector3(0, 1.5f, 5), new Vector3(60, 3, 1));
            yield return WaitForEnd();

            var snap = PlayGoblinRun.Snapshot();
            var events = ReadEvents(snap.RunDirectory);
            Assert.GreaterOrEqual(Count(events, "stuck"), 1, "walking into an unmapped wall must be reported as stuck");
            Assert.AreEqual(snap.Stucks, Count(events, "stuck"));
        }

        [UnityTest]
        public IEnumerator FallingBelowTheWorld_IsReported_AndPlayerIsRespawned()
        {
            Floor(); var p = Player(new Vector3(0, 1, 0));
            yield return null;

            PlayGoblinRun.StartRun(Options(15f));
            yield return new WaitForSeconds(1.5f);
            var cc = p.GetComponent<CharacterController>();
            cc.enabled = false; p.transform.position = new Vector3(0, -500f, 0); cc.enabled = true;
            yield return WaitForEnd();

            var snap = PlayGoblinRun.Snapshot();
            var events = ReadEvents(snap.RunDirectory);
            Assert.AreEqual(1, Count(events, "out_of_world"));
            Assert.GreaterOrEqual(Count(events, "respawn"), 1);
            Assert.Greater(p.transform.position.y, -5f, "player must be back above the world");
        }

        [UnityTest]
        public IEnumerator RepeatedError_IsLoggedOnce_AndCountedInSummary()
        {
            Floor(); Player(new Vector3(0, 1, 0));
            yield return null;

            LogAssert.ignoreFailingMessages = true;
            PlayGoblinRun.StartRun(Options(6f));
            yield return null;
            for (int i = 0; i < 50; i++) Debug.LogError("boom: same every time");
            Debug.LogError("a different error");
            yield return WaitForEnd();

            var snap = PlayGoblinRun.Snapshot();
            var events = ReadEvents(snap.RunDirectory);
            Assert.AreEqual(2, Count(events, "error"), "one event per distinct error");
            var summary = File.ReadAllText(Path.Combine(snap.RunDirectory, "run.json"));
            StringAssert.Contains("\"count\":50", summary);
        }

        [UnityTest]
        public IEnumerator Exception_IsCaptured_WithStack()
        {
            Floor(); Player(new Vector3(0, 1, 0));
            yield return null;

            LogAssert.ignoreFailingMessages = true;
            PlayGoblinRun.StartRun(Options(5f));
            yield return null;
            Debug.LogException(new System.InvalidOperationException("kaboom"));
            yield return WaitForEnd();

            var events = ReadEvents(PlayGoblinRun.Snapshot().RunDirectory);
            Assert.AreEqual(1, Count(events, "exception"));
            var raw = File.ReadAllText(Path.Combine(PlayGoblinRun.Snapshot().RunDirectory, "events.jsonl"));
            StringAssert.Contains("kaboom", raw);
            StringAssert.Contains("\"stack\":", raw);
        }

        [UnityTest]
        public IEnumerator NoPlayer_EndsWithClearSetupError()
        {
            Floor();
            yield return null;

            PlayGoblinRun.StartRun(Options(30f));
            yield return WaitForEnd();

            var snap = PlayGoblinRun.Snapshot();
            Assert.AreEqual("no_player", snap.EndReason);
            var raw = File.ReadAllText(Path.Combine(snap.RunDirectory, "events.jsonl"));
            StringAssert.Contains("setup_error", raw);
            StringAssert.Contains("PlayGoblin Player", raw);
        }

        [UnityTest]
        public IEnumerator Stop_EndsTheRunEarly()
        {
            Floor(); Player(new Vector3(0, 1, 0));
            yield return null;

            var run = PlayGoblinRun.StartRun(Options(600f));
            yield return new WaitForSeconds(1f);
            run.Stop();
            yield return WaitForEnd(5f);

            Assert.AreEqual("stopped", PlayGoblinRun.Snapshot().EndReason);
            Assert.IsNull(PlayGoblinRun.Current);
        }

        [UnityTest]
        public IEnumerator StartingASecondRun_ReplacesTheFirst()
        {
            Floor(); Player(new Vector3(0, 1, 0));
            yield return null;

            PlayGoblinRun.StartRun(Options(600f, 1));
            var firstDir = PlayGoblinRun.Snapshot().RunDirectory;
            yield return null;
            var second = PlayGoblinRun.StartRun(Options(3f, 2));
            Assert.AreSame(second, PlayGoblinRun.Current);
            yield return WaitForEnd();

            StringAssert.Contains("\"end_reason\":\"replaced\"", File.ReadAllText(Path.Combine(firstDir, "run.json")));
        }

        [UnityTest]
        public IEnumerator SameSeed_PicksTheSameFirstWaypoint()
        {
            Floor(); var p = Player(new Vector3(0, 1, 0));
            yield return null;

            var firsts = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                var cc = p.GetComponent<CharacterController>();
                cc.enabled = false; p.transform.position = new Vector3(0, 1, 0); cc.enabled = true;
                yield return null;
                PlayGoblinRun.StartRun(Options(2f, 99));
                yield return WaitForEnd();
                var raw = File.ReadAllLines(Path.Combine(PlayGoblinRun.Snapshot().RunDirectory, "events.jsonl"));
                firsts.Add(System.Array.Find(raw, l => l.Contains("\"type\":\"waypoint\"")));
                yield return null;
            }
            Assert.IsNotNull(firsts[0]);
            var strip = new System.Text.RegularExpressions.Regex("\"seq\":\\d+,\"t\":[\\d.]+,");
            Assert.AreEqual(strip.Replace(firsts[0], ""), strip.Replace(firsts[1], ""));
        }

        [UnityTest]
        public IEnumerator Mover_IsChosenFromTheComponentsOnThePlayer()
        {
            Floor();
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(p.GetComponent<CapsuleCollider>());
            var rb = p.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            p.AddComponent<CapsuleCollider>();
            var bw = p.AddComponent<PlayGoblinPlayer>();
            p.transform.position = new Vector3(0, 1, 0);
            _spawned.Add(p);
            yield return null;
            Assert.AreEqual("Rigidbody", bw.MoverKind);

            Object.Destroy(rb);
            yield return null;
            var fresh = p.AddComponent<PlayGoblinPlayer>();
            Assert.IsNull(fresh, "DisallowMultipleComponent must reject a second PlayGoblinPlayer");
        }
    }
}
