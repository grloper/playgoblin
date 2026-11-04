using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlayGoblin
{
    [Serializable]
    public sealed class PlayGoblinRunOptions
    {
        public int seed = 42;
        public float minutes = 10f;
        public float stuckSeconds = 5f;
        public float stuckDistance = 0.5f;
        public float positionHz = 2f;
        /// <summary>Without a NavMesh, only walk to points at most this far above or below the level the player starts on.</summary>
        public float maxClimb = 1.5f;
        public float waitForPlayerSeconds = 10f;
        public bool stopPlayModeOnEnd;
        /// <summary>Save screenshots to the run folder so reports can show what happened. Off automatically under -nographics.</summary>
        public bool recordFrames = true;
        public float frameHz = 2f;
        /// <summary>Capture rate for <see cref="burstSeconds"/> after a stuck spot or fall.</summary>
        public float burstHz = 10f;
        public float burstSeconds = 3f;
        public int frameWidth = 640;
        public int jpgQuality = 75;
        public int maxFrames = 3000;
        /// <summary>Click every uGUI button on the menu screens before a player exists, and report buttons that do nothing.</summary>
        public bool crawlMenus = true;
        public int maxMenuClicks = 200;
        public bool scanPlaceholderText = true;
        /// <summary>Report frame times above this for a full second. 0 = off. The first seconds after a load are ignored.</summary>
        public float fpsBudgetMs = 0f;
        /// <summary>Where run folders are written. Empty = &lt;project&gt;/PlayGoblinRuns in the editor, persistentDataPath in builds.</summary>
        public string outputRoot = "";
    }

    public enum PlayGoblinRunState { Idle, Starting, Running, Ended }

    /// <summary>Thread-safe view of the current or last run, for the local API and the editor window.</summary>
    public sealed class PlayGoblinRunSnapshot
    {
        public PlayGoblinRunState State;
        public string RunId;
        public string RunDirectory;
        public string EndReason;
        public double T;
        public long Events;
        public int Stucks;
        public int Falls;
        public int Errors;
        public float Coverage;
    }

    /// <summary>
    /// One PlayGoblin run: finds the <see cref="PlayGoblinPlayer"/>, drives the <see cref="Explorer"/>, captures errors
    /// and writes &lt;runDir&gt;/events.jsonl plus a run.json summary when it ends.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class PlayGoblinRun : MonoBehaviour
    {
        public const string Version = "0.1.0";

        static readonly object SnapGate = new object();
        static PlayGoblinRunSnapshot _snapshot = new PlayGoblinRunSnapshot { State = PlayGoblinRunState.Idle };

        public static PlayGoblinRun Current { get; private set; }
        public static event Action<PlayGoblinRun> Ended;

        public PlayGoblinRunOptions Options { get; private set; }
        public string RunId { get; private set; }
        public string RunDirectory { get; private set; }
        public string EndReason { get; private set; }
        public PlayGoblinRunState State { get; private set; }
        public double ElapsedGameSeconds =>
            State == PlayGoblinRunState.Idle ? 0 : State == PlayGoblinRunState.Ended ? _endT : Time.time - _startTime;

        PlayGoblinPlayer _player;
        Explorer _explorer;
        LogProbe _logs;
        Recorder _recorder;
        MenuCrawler _crawler;
        PlaceholderScanner _placeholders;
        readonly FpsWatch _fps = new FpsWatch();
        float _fpsIgnoreUntil;
        readonly FrameStats _frames = new FrameStats();
        StreamWriter _writer;
        float _startTime;
        float _startRealtime;
        float _endT;
        float _nextSnapshot;
        DateTime _startedUtc;
        string _scene;
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public static PlayGoblinRunSnapshot Snapshot()
        {
            lock (SnapGate)
            {
                return new PlayGoblinRunSnapshot
                {
                    State = _snapshot.State, RunId = _snapshot.RunId, RunDirectory = _snapshot.RunDirectory,
                    EndReason = _snapshot.EndReason, T = _snapshot.T, Events = _snapshot.Events,
                    Stucks = _snapshot.Stucks, Falls = _snapshot.Falls, Errors = _snapshot.Errors, Coverage = _snapshot.Coverage
                };
            }
        }

        /// <summary>Start a run in the current play session. Stops any run already in progress.</summary>
        public static PlayGoblinRun StartRun(PlayGoblinRunOptions options)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("PlayGoblin runs need Play Mode.");
            if (Current != null) Current.End("replaced");

            var go = new GameObject("[PlayGoblin Run]");
            DontDestroyOnLoad(go);
            var run = go.AddComponent<PlayGoblinRun>();
            run.Begin(options ?? new PlayGoblinRunOptions());
            return run;
        }

        public void Stop() => End("stopped");

        void Begin(PlayGoblinRunOptions options)
        {
            Options = options;
            Current = this;
            _startTime = Time.time;
            _startRealtime = Time.realtimeSinceStartup;
            _startedUtc = DateTime.UtcNow;
            _scene = SceneManager.GetActiveScene().path;
            RunId = $"{_startedUtc:yyyyMMdd-HHmmss}-s{options.seed}";

            var root = string.IsNullOrEmpty(options.outputRoot) ? DefaultOutputRoot() : options.outputRoot;
            RunDirectory = Path.Combine(root, RunId);
            Directory.CreateDirectory(RunDirectory);
            _writer = new StreamWriter(Path.Combine(RunDirectory, "events.jsonl"), false, new System.Text.UTF8Encoding(false));

            PlayGoblinEvents.Begin();
            _logs = new LogProbe();
            _recorder = new Recorder(options, RunDirectory);
            _crawler = options.crawlMenus ? new MenuCrawler(options) : null;
            _placeholders = options.scanPlaceholderText ? new PlaceholderScanner() : null;
            _fps.BudgetMs = options.fpsBudgetMs;
            _fpsIgnoreUntil = 5f;
            State = PlayGoblinRunState.Starting;
            SceneManager.activeSceneChanged += OnSceneChanged;

            PlayGoblinEvents.Add("run_start", null, null, new Dictionary<string, string>
            {
                { "run_id", RunId },
                { "seed", options.seed.ToString() },
                { "minutes", options.minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "scene", _scene },
                { "unity", Application.unityVersion },
                { "playgoblin", Version }
            });
            UpdateSnapshot();
        }

        void Update()
        {
            if (State != PlayGoblinRunState.Starting && State != PlayGoblinRunState.Running) return;

            var t = Time.time - _startTime;
            PlayGoblinEvents.CurrentTime = t;
            _frames.Add(Time.unscaledDeltaTime);
            var captured = _recorder != null && Time.frameCount - _recorder.LastCaptureFrame <= 1;
            if (_fps.Feed(Time.unscaledDeltaTime, t, t < _fpsIgnoreUntil || captured, out var drop))
            {
                PlayGoblinEvents.Add("fps_drop", null, _player != null ? _player.Position : (Vector3?)null, new Dictionary<string, string>
                {
                    { "ms", drop.AverageMs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) },
                    { "seconds", drop.Seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) },
                    { "budget_ms", Options.fpsBudgetMs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) }
                });
            }
            _placeholders?.Tick(Time.unscaledTime);

            if (State == PlayGoblinRunState.Starting)
            {
                _player = FindAnyObjectByType<PlayGoblinPlayer>();
                if (_player != null)
                {
                    _explorer = new Explorer(_player, Options);
                    State = PlayGoblinRunState.Running;
                    PlayGoblinEvents.Add("player_found", _player.name, _player.Position, new Dictionary<string, string>
                    {
                        { "mover", _player.MoverKind },
                        { "navmesh", _explorer.UsesNavMesh ? "true" : "false" },
                        { "reachable_cells", _explorer.Coverage.ReachableCells.ToString() }
                    });
                }
                else if (_crawler != null && _crawler.Tick(Time.unscaledTime))
                {
                    _startRealtime = Time.realtimeSinceStartup; // still working through menus: keep waiting for a player
                }
                else if (Time.realtimeSinceStartup - _startRealtime > Options.waitForPlayerSeconds)
                {
                    PlayGoblinEvents.Add("setup_error", "No PlayGoblinPlayer found. Add the PlayGoblin Player component to your player object.", null, null);
                    Flush();
                    End("no_player");
                    return;
                }
            }

            if (State == PlayGoblinRunState.Running)
            {
                if (_player == null)
                {
                    // Player destroyed (scene change, death that despawns): look for a new one next frame.
                    State = PlayGoblinRunState.Starting;
                    _startRealtime = Time.realtimeSinceStartup;
                    PlayGoblinEvents.Add("player_lost", null, null, null);
                }
                else
                {
                    _explorer.Tick(t);
                }
            }

            Flush();
            _recorder?.Tick(this, t);
            if (Time.realtimeSinceStartup >= _nextSnapshot)
            {
                _nextSnapshot = Time.realtimeSinceStartup + 0.25f;
                UpdateSnapshot();
            }

            if (t >= Options.minutes * 60f) End("completed");
        }

        void OnSceneChanged(Scene from, Scene to)
        {
            PlayGoblinEvents.Add("scene_change", to.path, null, null);
            _scene = to.path;
            _fpsIgnoreUntil = (float)(Time.time - _startTime) + 5f;
            if (_explorer != null && _player != null) _explorer.Rebuild();
        }

        void Flush()
        {
            if (_writer == null) return;
            foreach (var e in PlayGoblinEvents.DrainPending())
            {
                _writer.WriteLine(e.ToJson());
                if (e.Type == "stuck" || e.Type == "out_of_world") _recorder?.Burst();
                _counts.TryGetValue(e.Type, out var n);
                _counts[e.Type] = n + 1;
            }
            _writer.Flush();
        }

        void End(string reason)
        {
            if (State == PlayGoblinRunState.Ended || State == PlayGoblinRunState.Idle) return;
            EndReason = reason;
            _endT = Time.time - _startTime;
            SceneManager.activeSceneChanged -= OnSceneChanged;

            var groups = _logs?.Groups() ?? new List<LogProbe.Group>();
            foreach (var g in groups)
            {
                PlayGoblinEvents.Add("error_summary", g.Message, g.FirstPosition, new Dictionary<string, string>
                {
                    { "hash", g.Hash },
                    { "kind", g.Kind },
                    { "count", g.Count.ToString() },
                    { "first_t", g.FirstT.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) },
                    { "last_t", g.LastT.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) }
                });
            }
            PlayGoblinEvents.Add("run_end", reason, null, null);
            Flush();
            _logs?.Dispose();
            _logs = null;
            _crawler?.Dispose();
            PlayGoblinEvents.End();

            WriteSummary(groups);
            _writer?.Dispose();
            _writer = null;

            State = PlayGoblinRunState.Ended;
            UpdateSnapshot();
            if (Current == this) Current = null;
            try { Ended?.Invoke(this); }
            catch (Exception ex) { Debug.LogException(ex); }

            if (Application.isPlaying) Destroy(gameObject);
        }

        void WriteSummary(List<LogProbe.Group> groups)
        {
            var w = new JsonWriter().BeginObject()
                .Prop("run_id", RunId)
                .Prop("playgoblin", Version)
                .Prop("unity", Application.unityVersion)
                .Prop("platform", Application.platform.ToString())
                .Prop("scene", _scene)
                .Prop("seed", (long)Options.seed)
                .Prop("planned_minutes", Options.minutes)
                .Prop("started_utc", _startedUtc.ToString("o"))
                .Prop("end_reason", EndReason)
                .Prop("game_seconds", (double)(Time.time - _startTime))
                .Prop("real_seconds", (double)(Time.realtimeSinceStartup - _startRealtime))
                .Prop("time_scale", Time.timeScale)
                .Prop("menu_clicks", (long)(_crawler?.Clicks ?? 0))
                .Prop("dead_buttons", (long)(_crawler?.DeadButtons ?? 0))
                .Prop("recorded_frames", (long)(_recorder?.FramesWritten ?? 0))
                .Prop("recorder_attempts", (long)(_recorder?.Attempts ?? 0))
                .Prop("recorder_problem", _recorder?.LastProblem)
                .Prop("recorder_off", _recorder != null && !_recorder.Enabled ? _recorder.DisabledReason : null)
                .Prop("mover", _player != null ? _player.MoverKind : null)
                .Prop("navmesh", _explorer != null && _explorer.UsesNavMesh)
                .Prop("grid", _explorer != null && _explorer.UsesGrid)
                .Prop("walkable_cells", (long)(_explorer?.WalkableCells ?? 0))
                .Prop("reachable_cells", (long)(_explorer?.ReachableCells ?? 0))
                .Prop("coverage", _explorer?.Coverage.Coverage ?? 0f)
                .Prop("distance_m", _explorer?.DistanceWalked ?? 0f)
                .Prop("stuck", (long)(_explorer?.Stucks ?? 0))
                .Prop("out_of_world", (long)(_explorer?.Falls ?? 0))
                .Prop("respawns", (long)(_explorer?.Respawns ?? 0))
                .Prop("waypoints", (long)(_explorer?.Waypoints ?? 0))
                .Raw("frames", _frames.ToJson());

            var counts = new JsonWriter().BeginObject();
            foreach (var kv in _counts) counts.Prop(kv.Key, (long)kv.Value);
            w.Raw("event_counts", counts.EndObject().ToString());

            w.BeginArray("errors");
            foreach (var g in groups)
            {
                var gw = new JsonWriter().BeginObject()
                    .Prop("hash", g.Hash).Prop("kind", g.Kind).Prop("message", g.Message)
                    .Prop("count", (long)g.Count).Prop("first_t", g.FirstT).Prop("last_t", g.LastT);
                if (g.FirstPosition.HasValue) gw.Prop("pos", g.FirstPosition.Value);
                w.Raw(null, gw.EndObject().ToString());
            }
            w.EndArray();

            File.WriteAllText(Path.Combine(RunDirectory, "run.json"), w.EndObject().ToString());
        }

        void UpdateSnapshot()
        {
            lock (SnapGate)
            {
                _snapshot = new PlayGoblinRunSnapshot
                {
                    State = State,
                    RunId = RunId,
                    RunDirectory = RunDirectory,
                    EndReason = EndReason,
                    T = ElapsedGameSeconds,
                    Events = PlayGoblinEvents.Count,
                    Stucks = _explorer?.Stucks ?? 0,
                    Falls = _explorer?.Falls ?? 0,
                    Errors = _logs?.Groups().Count ?? _snapshot.Errors,
                    Coverage = _explorer?.Coverage.Coverage ?? 0f
                };
            }
        }

        void OnApplicationQuit() => End("aborted");
        void OnDestroy() => End("aborted");

        static string DefaultOutputRoot()
        {
            return Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PlayGoblinRuns")
                : Path.Combine(Application.persistentDataPath, "PlayGoblinRuns");
        }
    }
}
