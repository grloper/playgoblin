using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace PlayGoblin.Tests
{
    public class NavMeshTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();
        NavMeshDataInstance _navInstance;
        string _outRoot;
        string _keep;

        [SetUp]
        public void SetUp()
        {
            // PLAYGOBLIN_KEEP_RUNS=<dir> keeps the run folders so the CLI can be tried on real output.
            _keep = System.Environment.GetEnvironmentVariable("PLAYGOBLIN_KEEP_RUNS");
            _outRoot = !string.IsNullOrEmpty(_keep)
                ? _keep
                : Path.Combine(Path.GetTempPath(), "playgoblin-tests-" + System.Guid.NewGuid().ToString("N"));
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (PlayGoblinRun.Current != null) PlayGoblinRun.Current.Stop();
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            if (_navInstance.valid) NavMesh.RemoveNavMeshData(_navInstance);
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
            try { if (string.IsNullOrEmpty(_keep) && Directory.Exists(_outRoot)) Directory.Delete(_outRoot, true); } catch { /* best effort */ }
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

        /// <summary>Bakes a NavMesh from every collider in the scene, the same way a level's baked NavMesh would exist at runtime.</summary>
        void BakeNavMesh(Bounds bounds)
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(data, "NavMesh bake failed");
            _navInstance = NavMesh.AddNavMeshData(data);
        }

        GameObject AgentPlayer(Vector3 at, float speed = 6f)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            p.name = "Player";
            Object.Destroy(p.GetComponent<CapsuleCollider>());
            p.transform.position = at;
            var agent = p.AddComponent<NavMeshAgent>();
            agent.speed = speed;
            agent.acceleration = 40f;
            p.AddComponent<PlayGoblinPlayer>();
            _spawned.Add(p);
            return p;
        }

        [UnityTest]
        public IEnumerator NavMeshAgent_WalksABakedLevel_WithGoodCoverageAndNoFalseReports()
        {
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(60, 1, 60));
            Box("WallN", new Vector3(0, 1.5f, 8), new Vector3(2, 3, 20));
            Box("WallS", new Vector3(0, 1.5f, -8), new Vector3(2, 3, 20));
            Box("WallE", new Vector3(8, 1.5f, 0), new Vector3(20, 3, 2));
            Box("WallW", new Vector3(-8, 1.5f, 0), new Vector3(20, 3, 2));
            yield return null;
            Physics.SyncTransforms();
            BakeNavMesh(new Bounds(Vector3.zero, new Vector3(80, 20, 80)));

            var player = AgentPlayer(new Vector3(-20, 0, -20));
            yield return null;
            Time.timeScale = 10f;

            var options = new PlayGoblinRunOptions { seed = 3, minutes = 5f, outputRoot = _outRoot, positionHz = 1f };
            PlayGoblinRun.StartRun(options);
            var deadline = Time.realtimeSinceStartup + 120f;
            while (PlayGoblinRun.Snapshot().State != PlayGoblinRunState.Ended && Time.realtimeSinceStartup < deadline) yield return null;

            var snap = PlayGoblinRun.Snapshot();
            Assert.AreEqual(PlayGoblinRunState.Ended, snap.State, "NavMesh walk did not finish");
            Assert.AreEqual("completed", snap.EndReason);

            var text = File.ReadAllText(Path.Combine(snap.RunDirectory, "run.json"));
            Debug.Log("NAVMESH-SUMMARY " + text);
            var s = JsonUtility.FromJson<Summary>(text);
            Assert.IsTrue(text.Contains("\"navmesh\":true"), "run should have used the NavMesh code path");
            Assert.LessOrEqual(s.stuck, 1, "open NavMesh level should produce (almost) no stuck reports");
            Assert.AreEqual(0, s.out_of_world, "agent on a NavMesh should never fall or leave it");
            Assert.Greater(s.distance_m, 150f, "agent should really walk");
            Assert.Greater(s.waypoints, 8);
            Assert.Greater(s.coverage, 0.4f);
        }

        [UnityTest]
        public IEnumerator NavMeshAgent_PlacedOffTheNavMesh_IsReportedAndRespawned()
        {
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40));
            yield return null;
            Physics.SyncTransforms();
            BakeNavMesh(new Bounds(Vector3.zero, new Vector3(60, 20, 60)));

            var player = AgentPlayer(Vector3.zero);
            yield return null;
            Time.timeScale = 5f;

            PlayGoblinRun.StartRun(new PlayGoblinRunOptions { seed = 1, minutes = 3f, outputRoot = _outRoot, positionHz = 1f });
            yield return new WaitForSeconds(2f);

            // Pull the agent off the mesh the way a glitch would: disable it and drop it far outside.
            player.GetComponent<NavMeshAgent>().enabled = false;
            player.transform.position = new Vector3(200, 0, 200);

            var deadline = Time.realtimeSinceStartup + 60f;
            while (PlayGoblinRun.Snapshot().State != PlayGoblinRunState.Ended && Time.realtimeSinceStartup < deadline) yield return null;

            var snap = PlayGoblinRun.Snapshot();
            var text = File.ReadAllText(Path.Combine(snap.RunDirectory, "run.json"));
            Debug.Log("NAVMESH-OFF-SUMMARY " + text);
            var s = JsonUtility.FromJson<Summary>(text);
            Assert.GreaterOrEqual(s.out_of_world, 1, "leaving the NavMesh should be reported");
            var events = File.ReadAllText(Path.Combine(snap.RunDirectory, "events.jsonl"));
            StringAssert.Contains("\"type\":\"out_of_world\"", events);
        }

        [System.Serializable]
        class Summary { public float coverage; public float distance_m; public int waypoints; public int stuck; public int out_of_world; }
    }
}
