using System;
using System.Collections;
using PlayGoblin;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrokenGame.Tests
{
    /// <summary>
    /// Walks a sample scene and keeps the run folder. The pass/fail judgement is made by scripts/verify_samples.py,
    /// which turns the run into a report and compares it with the planted bugs.
    /// Environment: PLAYGOBLIN_SAMPLE_SCENE (Broken|Clean), PLAYGOBLIN_SAMPLE_OUT (folder), PLAYGOBLIN_SAMPLE_SECONDS (game seconds), PLAYGOBLIN_SAMPLE_SEED.
    /// </summary>
    public class SampleRunTests
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator WalkTheSampleScene()
        {
            var scene = Environment.GetEnvironmentVariable("PLAYGOBLIN_SAMPLE_SCENE");
            var outDir = Environment.GetEnvironmentVariable("PLAYGOBLIN_SAMPLE_OUT");
            if (string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(outDir)) Assert.Ignore("set PLAYGOBLIN_SAMPLE_SCENE and PLAYGOBLIN_SAMPLE_OUT");
            var seconds = float.TryParse(Environment.GetEnvironmentVariable("PLAYGOBLIN_SAMPLE_SECONDS"), out var s) ? s : 120f;
            LogAssert.ignoreFailingMessages = true; // the planted bugs log errors on purpose

            yield return SceneManager.LoadSceneAsync(scene);
            Time.timeScale = 4f;
            var run = PlayGoblinRun.StartRun(new PlayGoblinRunOptions
            {
                seed = int.TryParse(Environment.GetEnvironmentVariable("PLAYGOBLIN_SAMPLE_SEED"), out var seed) ? seed : 7, minutes = seconds / 60f, outputRoot = outDir, fpsBudgetMs = 33f, waitForPlayerSeconds = 30f
            });
            while (run.State != PlayGoblinRunState.Ended) yield return null;
            Time.timeScale = 1f;
            Assert.AreNotEqual("no_player", run.EndReason);
        }
    }
}
