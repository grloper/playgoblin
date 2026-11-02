using System.Collections.Generic;
using UnityEngine;

namespace PlayGoblin
{
    public sealed class PlayGoblinEvent
    {
        public long Seq;
        public double T;              // game seconds since run start
        public string Type;
        public string Detail;
        public Vector3? Position;
        public Dictionary<string, string> Data;

        public string ToJson()
        {
            var w = new JsonWriter().BeginObject()
                .Prop("seq", Seq)
                .Prop("t", T)
                .Prop("type", Type);
            if (!string.IsNullOrEmpty(Detail)) w.Prop("detail", Detail);
            if (Position.HasValue) w.Prop("pos", Position.Value);
            if (Data != null && Data.Count > 0)
            {
                w.Raw("data", new JsonWriter().BeginObject().Props(Data).EndObject().ToString());
            }
            return w.EndObject().ToString();
        }
    }

    /// <summary>
    /// Thread-safe event log for the current run. Games can call <see cref="Log"/> to add their own events
    /// (puzzle solved, boss spawned, ...). Calls outside a run are ignored, so it's safe to leave in shipping code.
    /// </summary>
    public static class PlayGoblinEvents
    {
        const int MemoryCap = 20000;

        static readonly object Gate = new object();
        static readonly List<PlayGoblinEvent> Memory = new List<PlayGoblinEvent>();
        static readonly Queue<PlayGoblinEvent> PendingWrite = new Queue<PlayGoblinEvent>();
        static long _seq;
        static long _dropped;
        static volatile bool _active;

        // Written on the main thread each frame so other threads can stamp events.
        internal static volatile float CurrentTime;
        internal static Vector3 LastPlayerPosition;
        internal static volatile bool HasPlayerPosition;

        public static bool Active => _active;
        public static long Count { get { lock (Gate) return _seq; } }

        /// <summary>Log a game-specific event. Position defaults to none.</summary>
        public static void Log(string type, string detail = null, Vector3? where = null, Dictionary<string, string> data = null)
        {
            Add(type, detail, where, data);
        }

        internal static PlayGoblinEvent Add(string type, string detail, Vector3? where, Dictionary<string, string> data)
        {
            if (!_active) return null;
            lock (Gate)
            {
                var e = new PlayGoblinEvent
                {
                    Seq = ++_seq,
                    T = CurrentTime,
                    Type = type,
                    Detail = detail,
                    Position = where,
                    Data = data
                };
                if (Memory.Count >= MemoryCap) { Memory.RemoveAt(0); _dropped++; }
                Memory.Add(e);
                PendingWrite.Enqueue(e);
                return e;
            }
        }

        internal static void Begin()
        {
            lock (Gate)
            {
                Memory.Clear();
                PendingWrite.Clear();
                _seq = 0;
                _dropped = 0;
                CurrentTime = 0;
                HasPlayerPosition = false;
                _active = true;
            }
        }

        internal static void End() { _active = false; }

        internal static List<PlayGoblinEvent> DrainPending()
        {
            lock (Gate)
            {
                var list = new List<PlayGoblinEvent>(PendingWrite);
                PendingWrite.Clear();
                return list;
            }
        }

        /// <summary>Events with Seq greater than <paramref name="afterSeq"/>, oldest first.</summary>
        public static List<PlayGoblinEvent> Since(long afterSeq, int max = 1000)
        {
            lock (Gate)
            {
                var result = new List<PlayGoblinEvent>();
                foreach (var e in Memory)
                {
                    if (e.Seq <= afterSeq) continue;
                    result.Add(e);
                    if (result.Count >= max) break;
                }
                return result;
            }
        }

        public static List<PlayGoblinEvent> All()
        {
            lock (Gate) return new List<PlayGoblinEvent>(Memory);
        }
    }
}
