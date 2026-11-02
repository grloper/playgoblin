using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayGoblin
{
    /// <summary>
    /// Captures errors and exceptions from any thread. Each distinct error (same message + stack) is logged
    /// once as an event; repeats are counted and reported in the run summary, so an error thrown
    /// 50 times a second doesn't flood the log.
    /// </summary>
    internal sealed class LogProbe : IDisposable
    {
        internal sealed class Group
        {
            public string Hash;
            public string Kind;      // "exception" or "error"
            public string Message;
            public int Count;
            public double FirstT;
            public double LastT;
            public Vector3? FirstPosition;
        }

        const int MaxStack = 4000;
        const int MaxMessage = 500;

        readonly object _gate = new object();
        readonly Dictionary<string, Group> _groups = new Dictionary<string, Group>();

        public LogProbe() { Application.logMessageReceivedThreaded += OnLog; }

        public void Dispose() { Application.logMessageReceivedThreaded -= OnLog; }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (!PlayGoblinEvents.Active) return;

            var kind = type == LogType.Exception ? "exception" : "error";
            var message = FirstLine(condition);
            if (message.Length > MaxMessage) message = message.Substring(0, MaxMessage);
            var hash = StableHash.Of(kind + "|" + message + "|" + stackTrace);
            var t = PlayGoblinEvents.CurrentTime;
            Vector3? pos = PlayGoblinEvents.HasPlayerPosition ? PlayGoblinEvents.LastPlayerPosition : (Vector3?)null;

            lock (_gate)
            {
                if (_groups.TryGetValue(hash, out var g))
                {
                    g.Count++;
                    g.LastT = t;
                    return;
                }
                _groups[hash] = new Group
                {
                    Hash = hash, Kind = kind, Message = message, Count = 1, FirstT = t, LastT = t, FirstPosition = pos
                };
            }

            var stack = stackTrace ?? "";
            if (stack.Length > MaxStack) stack = stack.Substring(0, MaxStack);
            PlayGoblinEvents.Add(kind, message, pos, new Dictionary<string, string>
            {
                { "hash", hash },
                { "stack", stack }
            });
        }

        public List<Group> Groups()
        {
            lock (_gate) return new List<Group>(_groups.Values);
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var i = s.IndexOf('\n');
            return (i >= 0 ? s.Substring(0, i) : s).TrimEnd('\r');
        }
    }

    /// <summary>Frame time statistics in unscaled milliseconds.</summary>
    internal sealed class FrameStats
    {
        const int Cap = 200_000;
        readonly List<float> _ms = new List<float>(4096);
        float _max;

        public void Add(float unscaledDeltaSeconds)
        {
            var ms = unscaledDeltaSeconds * 1000f;
            if (ms > _max) _max = ms;
            if (_ms.Count < Cap) _ms.Add(ms);
        }

        public int Frames => _ms.Count;

        public string ToJson()
        {
            if (_ms.Count == 0) return "{\"frames\":0}";
            var sorted = new List<float>(_ms);
            sorted.Sort();
            double sum = 0;
            foreach (var v in sorted) sum += v;
            var p95 = sorted[Mathf.Clamp((int)(sorted.Count * 0.95f), 0, sorted.Count - 1)];
            return new JsonWriter().BeginObject()
                .Prop("frames", (long)sorted.Count)
                .Prop("avg_ms", sum / sorted.Count)
                .Prop("p95_ms", p95)
                .Prop("max_ms", _max)
                .EndObject().ToString();
        }
    }
}
