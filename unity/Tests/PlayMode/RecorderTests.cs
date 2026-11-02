using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PlayGoblin.Tests
{
    public class RecorderTests
    {
        string _outRoot;
        GameObject _floor, _player, _cam;

        [SetUp]
        public void SetUp()
        {
            _outRoot = Path.Combine(Path.GetTempPath(), "playgoblin-rec-" + System.Guid.NewGuid().ToString("N"));
            Time.timeScale = 1f;
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.transform.position = new Vector3(0, -0.5f, 0);
            _floor.transform.localScale = new Vector3(30, 1, 30);
            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(_player.GetComponent<CapsuleCollider>());
            _player.AddComponent<CharacterController>();
            _player.AddComponent<PlayGoblinPlayer>().moveSpeed = 5f;
            _player.transform.position = new Vector3(0, 1, 0);
            _cam = new GameObject("Cam", typeof(Camera));
            _cam.transform.position = new Vector3(0, 12, -14);
            _cam.transform.LookAt(Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (PlayGoblinRun.Current != null) PlayGoblinRun.Current.Stop();
            Object.Destroy(_floor); Object.Destroy(_player); Object.Destroy(_cam);
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PLAYGOBLIN_KEEP_RUNS"))) return;
            try { if (Directory.Exists(_outRoot)) Directory.Delete(_outRoot, true); } catch { /* best effort */ }
        }

        [UnityTest]
        public IEnumerator Recorder_WritesNamedJpegFrames_AndReportsTheCount()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("needs a rendering device (run without -nographics)");

            var keep = System.Environment.GetEnvironmentVariable("PLAYGOBLIN_KEEP_RUNS");
            var root = string.IsNullOrEmpty(keep) ? _outRoot : keep;
            var run = PlayGoblinRun.StartRun(new PlayGoblinRunOptions { seed = 3, minutes = 6f / 60f, outputRoot = root, frameHz = 4f });
            while (run.State != PlayGoblinRunState.Ended) yield return null;

            var frames = Directory.GetFiles(Path.Combine(run.RunDirectory, "frames"), "*.jpg").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.GreaterOrEqual(frames.Length, 12, "6 s at 4 fps");
            StringAssert.IsMatch(@"^f00000_t\d+\.\d\d\.jpg$", frames[0]);
            var tex = new Texture2D(2, 2);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(Path.Combine(run.RunDirectory, "frames", frames[0]))));
            Assert.LessOrEqual(tex.width, 640);
            Assert.Greater(tex.width, 100);
            var summary = File.ReadAllText(Path.Combine(run.RunDirectory, "run.json"));
            StringAssert.Contains("\"recorded_frames\":" + frames.Length, summary.Replace(" ", ""));
        }

        [UnityTest]
        public IEnumerator Recorder_CanBeTurnedOff()
        {
            var run = PlayGoblinRun.StartRun(new PlayGoblinRunOptions { seed = 3, minutes = 2f / 60f, outputRoot = _outRoot, recordFrames = false });
            while (run.State != PlayGoblinRunState.Ended) yield return null;
            Assert.IsFalse(Directory.Exists(Path.Combine(run.RunDirectory, "frames")));
            StringAssert.Contains("disabled in options", File.ReadAllText(Path.Combine(run.RunDirectory, "run.json")));
        }
    }
}
