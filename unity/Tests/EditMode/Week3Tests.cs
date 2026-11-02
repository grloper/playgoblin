using NUnit.Framework;

namespace PlayGoblin.Tests
{
    public class FpsWatchTests
    {
        [Test]
        public void IsOff_WithoutABudget()
        {
            var w = new FpsWatch { BudgetMs = 0f };
            for (float t = 0; t < 5f; t += 0.2f) Assert.IsFalse(w.Feed(0.2f, t, false, out _));
        }

        [Test]
        public void Fires_AfterASecondOfSlowFrames_WithTheAverage()
        {
            var w = new FpsWatch { BudgetMs = 33f };
            var fired = false;
            FpsWatch.Drop drop = default;
            for (float t = 0; t < 2f && !fired; t += 0.1f) fired = w.Feed(0.1f, t, false, out drop);
            Assert.IsTrue(fired);
            Assert.AreEqual(100f, drop.AverageMs, 0.5f);
            Assert.GreaterOrEqual(drop.Seconds, 1f);
        }

        [Test]
        public void SingleSpike_DoesNotFire_AndAGoodFrameResetsTheWindow()
        {
            var w = new FpsWatch { BudgetMs = 33f };
            Assert.IsFalse(w.Feed(0.5f, 1f, false, out _));
            Assert.IsFalse(w.Feed(0.01f, 1.5f, false, out _));
            Assert.IsFalse(w.Feed(0.6f, 2f, false, out _));
        }

        [Test]
        public void IgnoredFrames_NeverCount()
        {
            var w = new FpsWatch { BudgetMs = 33f };
            for (float t = 0; t < 5f; t += 0.1f) Assert.IsFalse(w.Feed(0.1f, t, true, out _));
        }

        [Test]
        public void IgnoredFramesInTheMiddle_DoNotBreakARunOfSlowFrames()
        {
            var w = new FpsWatch { BudgetMs = 33f };
            var fired = false;
            var i = 0;
            for (float t = 0; t < 3f && !fired; t += 0.1f, i++) fired = w.Feed(0.1f, t, ignore: i % 3 == 0, out _);
            Assert.IsTrue(fired);
        }

        [Test]
        public void OneLongStutter_IsReportedOnce()
        {
            var w = new FpsWatch { BudgetMs = 33f };
            var count = 0;
            for (float t = 0; t < 4f; t += 0.1f) if (w.Feed(0.1f, t, false, out _)) count++;
            Assert.AreEqual(1, count);
        }
    }

    public class UiRulesTests
    {
        [TestCase("TODO: real title", true)]
        [TestCase("Lorem ipsum dolor", true)]
        [TestCase("New Text", true)]
        [TestCase("Press START", false)]
        [TestCase("Octodon", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Placeholder(string text, bool expected) => Assert.AreEqual(expected, PlaceholderRules.IsPlaceholder(text));

        [TestCase("Quit", true)]
        [TestCase("Exit to desktop", true)]
        [TestCase("Delete save", true)]
        [TestCase("Play", false)]
        [TestCase("Options", false)]
        public void DangerousButtons(string label, bool expected) => Assert.AreEqual(expected, MenuCrawler.IsDangerous(label));
    }
}
