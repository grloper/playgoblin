using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PlayGoblin.Tests
{
    public class MenuCrawlerTests
    {
        string _outRoot;
        GameObject _canvas;

        [SetUp]
        public void SetUp()
        {
            _outRoot = Path.Combine(Path.GetTempPath(), "playgoblin-menu-" + System.Guid.NewGuid().ToString("N"));
            LogAssert.ignoreFailingMessages = true;
            _canvas = new GameObject("Canvas", typeof(Canvas));
        }

        [TearDown]
        public void TearDown()
        {
            if (PlayGoblinRun.Current != null) PlayGoblinRun.Current.Stop();
            Object.Destroy(_canvas);
            LogAssert.ignoreFailingMessages = false;
            try { if (Directory.Exists(_outRoot)) Directory.Delete(_outRoot, true); } catch { /* best effort */ }
        }

        Button AddButton(string label, UnityAction onClick = null, Transform parent = null)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent != null ? parent : _canvas.transform, false);
            var text = new GameObject("Label", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(go.transform, false);
            text.GetComponent<Text>().text = label;
            var b = go.GetComponent<Button>();
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        Text AddText(string content)
        {
            var go = new GameObject("Title", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_canvas.transform, false);
            var t = go.GetComponent<Text>();
            t.text = content;
            return t;
        }

        IEnumerator Run(System.Action<string[]> done)
        {
            var run = PlayGoblinRun.StartRun(new PlayGoblinRunOptions
            {
                seed = 1, minutes = 1f, outputRoot = _outRoot, waitForPlayerSeconds = 3f, recordFrames = false
            });
            var start = Time.realtimeSinceStartup;
            while (run.State != PlayGoblinRunState.Ended && Time.realtimeSinceStartup - start < 60f) yield return null;
            Assert.AreEqual(PlayGoblinRunState.Ended, run.State, "run should end by itself (no player -> setup_error)");
            done(File.ReadAllLines(Path.Combine(run.RunDirectory, "events.jsonl")));
        }

        [UnityTest]
        public IEnumerator Crawler_ClicksButtons_ReportsOnlyTheDeadOne_AndNeverQuits()
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_canvas.transform, false);
            panel.SetActive(false);
            var quitClicked = false;
            var playClicks = 0;
            AddButton("Play", () => { playClicks++; panel.SetActive(true); });
            AddButton("Dead");
            AddButton("Quit", () => quitClicked = true);

            string[] lines = null;
            yield return Run(l => lines = l);

            Assert.IsFalse(quitClicked, "Quit must never be clicked");
            Assert.AreEqual(1, playClicks, "a button that changes the screen is clicked once");
            var dead = lines.Where(l => l.Contains("\"dead_button\"")).ToArray();
            Assert.AreEqual(1, dead.Length, string.Join("\n", lines));
            StringAssert.Contains("Dead", dead[0]);
            Assert.IsTrue(lines.Any(l => l.Contains("\"ui_click\"") && l.Contains("Play") && l.Contains("\"changed\"")));
        }

        [UnityTest]
        public IEnumerator Crawler_FollowsAButtonThatOpensAScreen_AndFindsThePlaceholderTextThere()
        {
            var sub = new GameObject("Options", typeof(RectTransform));
            sub.transform.SetParent(_canvas.transform, false);
            sub.SetActive(false);
            var main = new GameObject("Main", typeof(RectTransform));
            main.transform.SetParent(_canvas.transform, false);
            AddButton("Options", () => { main.SetActive(false); sub.SetActive(true); }, main.transform);
            AddButton("Apply", null, sub.transform);
            var t = new GameObject("Heading", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(sub.transform, false);
            t.GetComponent<Text>().text = "TODO: write options";

            string[] lines = null;
            yield return Run(l => lines = l);

            Assert.IsTrue(lines.Any(l => l.Contains("\"placeholder_text\"") && l.Contains("TODO")), string.Join("\n", lines));
            Assert.IsTrue(lines.Any(l => l.Contains("\"dead_button\"") && l.Contains("Apply")), "second screen was crawled");
        }

        [UnityTest]
        public IEnumerator Crawler_CanBeTurnedOff()
        {
            var clicked = false;
            AddButton("Play", () => clicked = true);
            var run = PlayGoblinRun.StartRun(new PlayGoblinRunOptions
            {
                seed = 1, minutes = 1f, outputRoot = _outRoot, waitForPlayerSeconds = 2f, crawlMenus = false, recordFrames = false
            });
            var start = Time.realtimeSinceStartup;
            while (run.State != PlayGoblinRunState.Ended && Time.realtimeSinceStartup - start < 30f) yield return null;
            Assert.IsFalse(clicked);
        }
    }
}
