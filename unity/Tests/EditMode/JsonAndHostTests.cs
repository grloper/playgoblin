using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace PlayGoblin.Tests
{
    public class JsonWriterTests
    {
        [Test]
        public void Object_WithMixedProps()
        {
            var s = new JsonWriter().BeginObject().Prop("a", "x").Prop("b", 1L).Prop("c", true).Prop("d", 1.5).EndObject().ToString();
            Assert.AreEqual("{\"a\":\"x\",\"b\":1,\"c\":true,\"d\":1.5}", s);
        }

        [Test]
        public void Strings_AreEscaped()
        {
            var s = new JsonWriter().BeginObject().Prop("m", "a\"b\\c\nd\te\u0001").EndObject().ToString();
            Assert.AreEqual("{\"m\":\"a\\\"b\\\\c\\nd\\te\\u0001\"}", s);
        }

        [Test]
        public void NullString_IsJsonNull()
        {
            Assert.AreEqual("{\"m\":null}", new JsonWriter().BeginObject().Prop("m", (string)null).EndObject().ToString());
        }

        [Test]
        public void NaNAndInfinity_BecomeNull()
        {
            var s = new JsonWriter().BeginObject().Prop("a", double.NaN).Prop("b", double.PositiveInfinity).EndObject().ToString();
            Assert.AreEqual("{\"a\":null,\"b\":null}", s);
        }

        [Test]
        public void Vector3_IsArray()
        {
            var s = new JsonWriter().BeginObject().Prop("p", new Vector3(1, 2.5f, -3)).EndObject().ToString();
            Assert.AreEqual("{\"p\":[1,2.5,-3]}", s);
        }

        [Test]
        public void Arrays_AndNesting_UseCommasCorrectly()
        {
            var w = new JsonWriter().BeginObject().Prop("n", 1L).BeginArray("xs").Item("a").Item("b").EndArray().Prop("z", 2L).EndObject();
            Assert.AreEqual("{\"n\":1,\"xs\":[\"a\",\"b\"],\"z\":2}", w.ToString());
        }

        [Test]
        public void ArrayOfObjects_ViaRaw()
        {
            var w = new JsonWriter().BeginObject().BeginArray("xs");
            w.Raw(null, new JsonWriter().BeginObject().Prop("i", 1L).EndObject().ToString());
            w.Raw(null, new JsonWriter().BeginObject().Prop("i", 2L).EndObject().ToString());
            Assert.AreEqual("{\"xs\":[{\"i\":1},{\"i\":2}]}", w.EndArray().EndObject().ToString());
        }

        [Test]
        public void Numbers_UseInvariantCulture()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("{\"a\":1.5}", new JsonWriter().BeginObject().Prop("a", 1.5).EndObject().ToString());
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }

        [Test]
        public void StableHash_IsDeterministic_AndSensitive()
        {
            Assert.AreEqual(StableHash.Of("abc"), StableHash.Of("abc"));
            Assert.AreNotEqual(StableHash.Of("abc"), StableHash.Of("abd"));
            Assert.AreEqual("e40c292c", StableHash.Of("a"), "FNV-1a 32-bit of \"a\"");
        }
    }

    public class EventsTests
    {
        [SetUp] public void SetUp() { PlayGoblinEvents.Begin(); }
        [TearDown] public void TearDown() { PlayGoblinEvents.End(); }

        [Test]
        public void Log_AssignsIncreasingSequenceNumbers()
        {
            PlayGoblinEvents.Log("a");
            PlayGoblinEvents.Log("b");
            var all = PlayGoblinEvents.All();
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(1, all[0].Seq);
            Assert.AreEqual(2, all[1].Seq);
        }

        [Test]
        public void Log_IsIgnored_OutsideARun()
        {
            PlayGoblinEvents.End();
            PlayGoblinEvents.Log("x");
            Assert.AreEqual(0, PlayGoblinEvents.All().Count);
        }

        [Test]
        public void Since_ReturnsOnlyNewerEvents()
        {
            for (int i = 0; i < 5; i++) PlayGoblinEvents.Log("e" + i);
            var newer = PlayGoblinEvents.Since(3);
            Assert.AreEqual(2, newer.Count);
            Assert.AreEqual("e3", newer[0].Type);
        }

        [Test]
        public void Since_RespectsMax()
        {
            for (int i = 0; i < 10; i++) PlayGoblinEvents.Log("e");
            Assert.AreEqual(3, PlayGoblinEvents.Since(0, 3).Count);
        }

        [Test]
        public void DrainPending_ReturnsEachEventOnce()
        {
            PlayGoblinEvents.Log("a");
            Assert.AreEqual(1, PlayGoblinEvents.DrainPending().Count);
            Assert.AreEqual(0, PlayGoblinEvents.DrainPending().Count);
        }

        [Test]
        public void ToJson_IncludesPositionAndData()
        {
            PlayGoblinEvents.Log("stuck", "d", new Vector3(1, 2, 3), new Dictionary<string, string> { { "k", "v" } });
            var json = PlayGoblinEvents.All()[0].ToJson();
            StringAssert.Contains("\"type\":\"stuck\"", json);
            StringAssert.Contains("\"pos\":[1,2,3]", json);
            StringAssert.Contains("\"data\":{\"k\":\"v\"}", json);
        }

        [Test]
        public void Log_FromBackgroundThread_IsSafe()
        {
            var threads = new List<Thread>();
            for (int t = 0; t < 4; t++)
            {
                var th = new Thread(() => { for (int i = 0; i < 250; i++) PlayGoblinEvents.Log("bg"); });
                threads.Add(th);
                th.Start();
            }
            foreach (var th in threads) th.Join();
            var all = PlayGoblinEvents.All();
            Assert.AreEqual(1000, all.Count);
            var seen = new HashSet<long>();
            foreach (var e in all) Assert.IsTrue(seen.Add(e.Seq), "duplicate seq " + e.Seq);
        }
    }

    public class HostRouteTests
    {
        [SetUp] public void SetUp() { PlayGoblinEvents.Begin(); }
        [TearDown] public void TearDown() { PlayGoblinEvents.End(); }

        [Test]
        public void Status_IsValidJson_WithState()
        {
            var (code, json) = PlayGoblinHost.Handle("GET", "/status", "");
            Assert.AreEqual(200, code);
            var parsed = JsonUtility.FromJson<StatusDto>(json);
            Assert.IsNotNull(parsed.state);
            Assert.AreEqual(PlayGoblinRun.Version, parsed.playgoblin);
        }

        [Test]
        public void UnknownRoute_Is404()
        {
            Assert.AreEqual(404, PlayGoblinHost.Handle("GET", "/nope", "").Item1);
        }

        [Test]
        public void WrongMethod_Is404()
        {
            Assert.AreEqual(404, PlayGoblinHost.Handle("GET", "/run/start", "").Item1);
        }

        [Test]
        public void Events_SinceCursor_AndNext()
        {
            PlayGoblinEvents.Log("a");
            PlayGoblinEvents.Log("b");
            PlayGoblinEvents.Log("c");
            var (code, json) = PlayGoblinHost.Handle("GET", "/events?since=1&max=10", "");
            Assert.AreEqual(200, code);
            StringAssert.Contains("\"type\":\"b\"", json);
            StringAssert.Contains("\"type\":\"c\"", json);
            StringAssert.DoesNotContain("\"type\":\"a\"", json);
            StringAssert.Contains("\"next\":3", json);
        }

        [Test]
        public void Events_WithBadQuery_StillAnswers()
        {
            Assert.AreEqual(200, PlayGoblinHost.Handle("GET", "/events?since=abc&max=zzz", "").Item1);
        }

        [Test]
        public void Start_WithoutHandler_Is503()
        {
            var saved = PlayGoblinHost.StartHandler;
            try
            {
                PlayGoblinHost.StartHandler = null;
                Assert.AreEqual(503, PlayGoblinHost.Handle("POST", "/run/start", "{}").Item1);
            }
            finally { PlayGoblinHost.StartHandler = saved; }
        }

        [Test]
        public void Start_RejectsNonPositiveMinutes()
        {
            var saved = PlayGoblinHost.StartHandler;
            try
            {
                PlayGoblinHost.StartHandler = _ => "running";
                Assert.AreEqual(400, PlayGoblinHost.Handle("POST", "/run/start", "{\"minutes\":0}").Item1);
            }
            finally { PlayGoblinHost.StartHandler = saved; }
        }

        [Test]
        public void LatestRun_Is404_WhenNoRunFinished()
        {
            Assert.AreEqual(404, PlayGoblinHost.Handle("GET", "/runs/latest", "").Item1);
        }

        [System.Serializable]
        class StatusDto { public string state; public string playgoblin; }
    }
}
