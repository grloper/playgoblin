using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGoblin.Tests
{
    public class FpsRunTests
    {
        sealed class Stutter : MonoBehaviour
        {
            public int sleepMs = 60;
            void Update() => Thread.Sleep(sleepMs);
        }

        string _outRoot;
        GameObject _stutter;

        [SetUp]
        public void SetUp() => _outRoot = Path.Combine(Path.GetTempPath(), "playgoblin-fps-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (PlayGoblinRun.Current != null) PlayGoblinRun.Current.Stop();
            if (_stutter != null) Object.Destroy(_stutter);
            try { if (Directory.Exists(_outRoot)) Directory.Delete(_outRoot, true); } catch { /* best effort */ }
        }

        string[] Run(float fpsBudgetMs, float seconds, out PlayGoblinRun run)
        {
            run = PlayGoblinRun.StartRun(new PlayGoblinRunOptions
            {
                seed = 1, minutes = seconds / 60f, outputRoot = _outRoot, waitForPlayerSeconds = 60f,
                recordFrames = false, crawlMenus = false, fpsBudgetMs = fpsBudgetMs
            });
            return null;
        }

        [UnityTest]
        public IEnumerator SlowFrames_AfterTheLoadGracePeriod_AreReported_WhenABudgetIsSet()
        {
            _stutter = new GameObject("Stutter", typeof(Stutter));
            Run(33f, 12f, out var run);
            while (run.State != PlayGoblinRunState.Ended) yield return null;
            var lines = File.ReadAllLines(Path.Combine(run.RunDirectory, "events.jsonl"));
            var drops = lines.Where(l => l.Contains("\"fps_drop\"")).ToArray();
            Assert.GreaterOrEqual(drops.Length, 1, string.Join("\n", lines));
            Assert.IsTrue(drops.All(l => l.Contains("\"budget_ms\":\"33\"")));
            var firstT = double.Parse(System.Text.RegularExpressions.Regex.Match(drops[0], "\"t\":([0-9.]+)").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(firstT, 5.0, "the first 5 s are ignored");
        }

        [UnityTest]
        public IEnumerator NoBudget_NoFpsEvents()
        {
            _stutter = new GameObject("Stutter", typeof(Stutter));
            Run(0f, 8f, out var run);
            while (run.State != PlayGoblinRunState.Ended) yield return null;
            var text = File.ReadAllText(Path.Combine(run.RunDirectory, "events.jsonl"));
            StringAssert.DoesNotContain("fps_drop", text);
        }
    }
}
