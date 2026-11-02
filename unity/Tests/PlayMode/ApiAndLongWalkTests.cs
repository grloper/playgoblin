using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGoblin.Tests
{
    public class ApiAndLongWalkTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        string _outRoot;

        [SetUp]
        public void SetUp()
        {
            _outRoot = Path.Combine(Path.GetTempPath(), "playgoblin-tests-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            PlayGoblinHost.Stop();
            PlayGoblinHost.StartHandler = null;
            if (PlayGoblinRun.Current != null) PlayGoblinRun.Current.Stop();
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            Time.timeScale = 1f;
            try { if (Directory.Exists(_outRoot)) Directory.Delete(_outRoot, true); } catch { /* best effort */ }
        }

        GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            b.transform.position = center;
            b.transform.localScale = size;
            _spawned.Add(b);
            return b;
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

        static IEnumerator Await<T>(Task<T> task)
        {
            var end = Time.realtimeSinceStartup + 10f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end)
            {
                PlayGoblinHost.Pump();
                yield return null;
            }
            Assert.IsTrue(task.IsCompleted, "HTTP request timed out");
        }

        [UnityTest]
        public IEnumerator HttpApi_StartsARun_AndReportsItsProgress()
        {
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40));
            Player(new Vector3(0, 1, 0));
            yield return null;
            Time.timeScale = 4f;

            PlayGoblinHost.StartHandler = o => { o.outputRoot = _outRoot; PlayGoblinRun.StartRun(o); return "running"; };
            Assert.IsTrue(PlayGoblinHost.Start(0), PlayGoblinHost.LastError);
            var baseUrl = $"http://127.0.0.1:{PlayGoblinHost.Port}";

            using (var http = new HttpClient())
            {
                var status = http.GetAsync(baseUrl + "/status");
                yield return Await(status);
                Assert.AreEqual(System.Net.HttpStatusCode.OK, status.Result.StatusCode);
                StringAssert.Contains("\"state\":\"idle\"", status.Result.Content.ReadAsStringAsync().Result);

                var missing = http.GetAsync(baseUrl + "/nope");
                yield return Await(missing);
                Assert.AreEqual(System.Net.HttpStatusCode.NotFound, missing.Result.StatusCode);

                var bad = http.PostAsync(baseUrl + "/run/start", new StringContent("{\"minutes\":-1}"));
                yield return Await(bad);
                Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, bad.Result.StatusCode);

                var start = http.PostAsync(baseUrl + "/run/start", new StringContent("{\"seed\":5,\"minutes\":0.1}"));
                yield return Await(start);
                Assert.AreEqual(System.Net.HttpStatusCode.Accepted, start.Result.StatusCode);
                StringAssert.Contains("running", start.Result.Content.ReadAsStringAsync().Result);

                var deadline = Time.realtimeSinceStartup + 30f;
                string body = "";
                while (Time.realtimeSinceStartup < deadline)
                {
                    var s = http.GetStringAsync(baseUrl + "/status");
                    yield return Await(s);
                    body = s.Result;
                    if (body.Contains("\"state\":\"ended\"")) break;
                    yield return new WaitForSeconds(0.2f);
                }
                StringAssert.Contains("\"state\":\"ended\"", body);
                StringAssert.Contains("\"end_reason\":\"completed\"", body);

                var events = http.GetStringAsync(baseUrl + "/events?since=0&max=5");
                yield return Await(events);
                StringAssert.Contains("\"type\":\"run_start\"", events.Result);
                StringAssert.Contains("\"next\":5", events.Result);

                var latest = http.GetAsync(baseUrl + "/runs/latest");
                yield return Await(latest);
                Assert.AreEqual(System.Net.HttpStatusCode.OK, latest.Result.StatusCode);
                StringAssert.Contains("\"seed\":5", latest.Result.Content.ReadAsStringAsync().Result);

                var stop = http.PostAsync(baseUrl + "/run/stop", new StringContent(""));
                yield return Await(stop);
                StringAssert.Contains("idle", stop.Result.Content.ReadAsStringAsync().Result);
            }
        }

        [UnityTest]
        public IEnumerator HttpApi_Start_RejectsMalformedBody_WithoutCrashingTheHost()
        {
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40));
            Player(new Vector3(0, 1, 0));
            yield return null;
            PlayGoblinHost.StartHandler = o => "running";
            Assert.IsTrue(PlayGoblinHost.Start(0), PlayGoblinHost.LastError);
            var baseUrl = $"http://127.0.0.1:{PlayGoblinHost.Port}";

            using (var http = new HttpClient())
            {
                var bad = http.PostAsync(baseUrl + "/run/start", new StringContent("{not json"));
                yield return Await(bad);
                Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, bad.Result.StatusCode);

                var ok = http.GetAsync(baseUrl + "/status");
                yield return Await(ok);
                Assert.AreEqual(System.Net.HttpStatusCode.OK, ok.Result.StatusCode, "host must survive a bad request");
            }
        }

        [UnityTest]
        public IEnumerator TenMinuteWalk_OnAnObstacleCourse_CompletesWithGoodCoverage()
        {
            // 60x60 floor with a wall cross, pillars and a ledge-sized step; open lanes connect every quadrant.
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(60, 1, 60));
            Box("WallN", new Vector3(0, 1.5f, 8), new Vector3(2, 3, 20));
            Box("WallS", new Vector3(0, 1.5f, -8), new Vector3(2, 3, 20));
            Box("WallE", new Vector3(8, 1.5f, 0), new Vector3(20, 3, 2));
            Box("WallW", new Vector3(-8, 1.5f, 0), new Vector3(20, 3, 2));
            for (int i = 0; i < 6; i++) Box("Pillar" + i, new Vector3(-20 + i * 8, 1.5f, 18), new Vector3(1.5f, 3, 1.5f));
            Player(new Vector3(0, 1, 0));
            yield return null;
            Time.timeScale = 20f;

            var options = new PlayGoblinRunOptions { seed = 7, minutes = 10f, outputRoot = _outRoot, positionHz = 1f };
            PlayGoblinRun.StartRun(options);
            var deadline = Time.realtimeSinceStartup + 120f;
            while (PlayGoblinRun.Snapshot().State != PlayGoblinRunState.Ended && Time.realtimeSinceStartup < deadline) yield return null;

            var snap = PlayGoblinRun.Snapshot();
            Assert.AreEqual(PlayGoblinRunState.Ended, snap.State, "10-minute walk did not finish");
            Assert.AreEqual("completed", snap.EndReason);
            Assert.GreaterOrEqual(snap.T, 599f, "should cover the full 10 game minutes");

            var summaryText = File.ReadAllText(Path.Combine(snap.RunDirectory, "run.json"));
            var summary = JsonUtility.FromJson<Summary>(summaryText);
            Debug.Log("TENMIN-SUMMARY " + summaryText);
            Assert.LessOrEqual(summary.stuck, 1, "an open course should produce (almost) no false stuck reports");
            Assert.Greater(summary.coverage, 0.6f, "should visit most of the reachable floor");
            Assert.Greater(summary.distance_m, 500f, "player should actually walk a long way");
            Assert.Greater(summary.waypoints, 20);
            Assert.AreEqual(0, summary.out_of_world, "open course has no falls");

            var eventsFile = Path.Combine(snap.RunDirectory, "events.jsonl");
            var size = new FileInfo(eventsFile).Length;
            Assert.Less(size, 2_000_000, "event log should stay small for a 10-minute run");
            foreach (var line in File.ReadLines(eventsFile)) Assert.IsNotNull(JsonUtility.FromJson<Line>(line), line);
        }

        [System.Serializable]
        class Summary { public float coverage; public float distance_m; public int waypoints; public int stuck; public int out_of_world; }

        [System.Serializable]
        class Line { public string type; }
    }
}
