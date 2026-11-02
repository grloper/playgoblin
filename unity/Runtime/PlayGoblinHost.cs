using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PlayGoblin
{
    /// <summary>
    /// Small local HTTP+JSON API the playgoblin CLI talks to. Listens on 127.0.0.1 only.
    ///   GET  /status              current or last run
    ///   POST /run/start           body: PlayGoblinRunOptions JSON (all fields optional)
    ///   POST /run/stop
    ///   GET  /events?since=N&amp;max=M
    ///   GET  /runs/latest         run.json of the last finished run
    /// Requests that touch Unity objects are queued and executed by <see cref="Pump"/> on the main thread.
    /// </summary>
    public static class PlayGoblinHost
    {
        public const int DefaultPort = 47321;

        /// <summary>Set by the editor or player bootstrap. Runs on the main thread; returns the new state ("running"/"starting").</summary>
        public static Func<PlayGoblinRunOptions, string> StartHandler;

        static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        static TcpListener _listener;
        static Thread _thread;
        static volatile bool _running;

        public static bool IsRunning => _running;
        public static int Port { get; private set; }
        public static string LastError { get; private set; }

        public static int ResolvePort()
        {
            var env = Environment.GetEnvironmentVariable("PLAYGOBLIN_PORT");
            return int.TryParse(env, out var p) && p > 0 && p < 65536 ? p : DefaultPort;
        }

        public static bool Start(int port)
        {
            if (_running) return true;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _running = true;
                LastError = null;
                _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "PlayGoblin Host" };
                _thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _listener = null;
                _running = false;
                return false;
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* already closed */ }
            _listener = null;
            _thread = null;
        }

        /// <summary>Run queued main-thread work. Call once per frame (editor update or a MonoBehaviour).</summary>
        public static void Pump()
        {
            while (MainThread.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        static void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        static void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    var stream = client.GetStream();
                    if (!TryReadRequest(stream, out var method, out var target, out var body))
                    {
                        Write(stream, 400, Error("bad_request"));
                        return;
                    }
                    var (status, json) = Handle(method, target, body);
                    Write(stream, status, json);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
        }

        internal static (int, string) Handle(string method, string target, string body)
        {
            var q = target.IndexOf('?');
            var path = (q >= 0 ? target.Substring(0, q) : target).TrimEnd('/');
            var query = ParseQuery(q >= 0 ? target.Substring(q + 1) : "");
            if (path == "") path = "/";

            switch (method + " " + path)
            {
                case "GET /status":
                    return (200, StatusJson());

                case "POST /run/start":
                {
                    if (StartHandler == null) return (503, Error("no_start_handler"));
                    PlayGoblinRunOptions options;
                    try { options = string.IsNullOrWhiteSpace(body) ? new PlayGoblinRunOptions() : JsonUtility.FromJson<PlayGoblinRunOptions>(body); }
                    catch (Exception ex) { return (400, Error("invalid_options: " + ex.Message)); }
                    if (options.minutes <= 0) return (400, Error("minutes must be > 0"));
                    var state = OnMainThread(() => StartHandler(options));
                    return state == null
                        ? (504, Error("main_thread_timeout"))
                        : (202, "{\"state\":" + JsonWriter.Quote(state) + "}");
                }

                case "POST /run/stop":
                {
                    var result = OnMainThread(() =>
                    {
                        if (PlayGoblinRun.Current == null) return "idle";
                        PlayGoblinRun.Current.Stop();
                        return "stopped";
                    });
                    return result == null
                        ? (504, Error("main_thread_timeout"))
                        : (200, "{\"state\":" + JsonWriter.Quote(result) + "}");
                }

                case "GET /events":
                {
                    long since = 0;
                    int max = 1000;
                    if (query.TryGetValue("since", out var s)) long.TryParse(s, out since);
                    if (query.TryGetValue("max", out var m)) int.TryParse(m, out max);
                    max = Mathf.Clamp(max, 1, 5000);
                    var events = PlayGoblinEvents.Since(since, max);
                    var sb = new StringBuilder("{\"events\":[");
                    for (int i = 0; i < events.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(events[i].ToJson());
                    }
                    var next = events.Count > 0 ? events[events.Count - 1].Seq : since;
                    sb.Append("],\"next\":").Append(next.ToString(CultureInfo.InvariantCulture)).Append('}');
                    return (200, sb.ToString());
                }

                case "GET /runs/latest":
                {
                    var snap = PlayGoblinRun.Snapshot();
                    if (snap.State != PlayGoblinRunState.Ended || string.IsNullOrEmpty(snap.RunDirectory))
                        return (404, Error("no_finished_run"));
                    var file = Path.Combine(snap.RunDirectory, "run.json");
                    return File.Exists(file) ? (200, File.ReadAllText(file)) : (404, Error("run_json_missing"));
                }

                default:
                    return (404, Error("not_found"));
            }
        }

        static string StatusJson()
        {
            var s = PlayGoblinRun.Snapshot();
            var w = new JsonWriter().BeginObject()
                .Prop("playgoblin", PlayGoblinRun.Version)
                .Prop("state", s.State.ToString().ToLowerInvariant())
                .Prop("run_id", s.RunId)
                .Prop("run_dir", s.RunDirectory)
                .Prop("end_reason", s.EndReason)
                .Prop("t", s.T)
                .Prop("events", s.Events)
                .Prop("stuck", (long)s.Stucks)
                .Prop("out_of_world", (long)s.Falls)
                .Prop("errors", (long)s.Errors)
                .Prop("coverage", s.Coverage);
            return w.EndObject().ToString();
        }

        static string OnMainThread(Func<string> work, int timeoutMs = 5000)
        {
            string result = null;
            using (var done = new ManualResetEventSlim(false))
            {
                MainThread.Enqueue(() =>
                {
                    try { result = work(); }
                    catch (Exception ex) { result = "error: " + ex.Message; }
                    finally { done.Set(); }
                });
                return done.Wait(timeoutMs) ? result : null;
            }
        }

        static string Error(string message) => "{\"error\":" + JsonWriter.Quote(message) + "}";

        static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in q.Split('&'))
            {
                if (part.Length == 0) continue;
                var eq = part.IndexOf('=');
                var k = Uri.UnescapeDataString(eq >= 0 ? part.Substring(0, eq) : part);
                var v = eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1)) : "";
                d[k] = v;
            }
            return d;
        }

        static bool TryReadRequest(NetworkStream stream, out string method, out string target, out string body)
        {
            method = target = body = null;
            var header = new List<byte>(1024);
            var complete = false;
            while (!complete && header.Count < 16 * 1024)
            {
                var b = stream.ReadByte();
                if (b < 0) return false;
                header.Add((byte)b);
                var n = header.Count;
                complete = n >= 4 && header[n - 4] == '\r' && header[n - 3] == '\n' && header[n - 2] == '\r' && header[n - 1] == '\n';
            }
            if (!complete) return false;

            var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return false;
            method = first[0].ToUpperInvariant();
            target = first[1];

            var length = 0;
            foreach (var l in lines)
            {
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(l.Substring(15).Trim(), out length);
            }
            if (length < 0 || length > 1024 * 1024) return false;

            var buf = new byte[length];
            var read = 0;
            while (read < length)
            {
                var n = stream.Read(buf, read, length - read);
                if (n <= 0) return false;
                read += n;
            }
            body = Encoding.UTF8.GetString(buf);
            return true;
        }

        static void Write(NetworkStream stream, int status, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            var reason = status switch
            {
                200 => "OK", 202 => "Accepted", 400 => "Bad Request", 404 => "Not Found",
                503 => "Service Unavailable", 504 => "Gateway Timeout", _ => "Error"
            };
            var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            var h = Encoding.ASCII.GetBytes(head);
            stream.Write(h, 0, h.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }

    /// <summary>
    /// Player builds: pass -playgoblin-host to expose the local API, or -playgoblin-run to start a walk on launch
    /// (-playgoblin-seed N, -playgoblin-minutes M, -playgoblin-quit to exit when done). The editor uses its own bootstrap.
    /// </summary>
    internal static class PlayGoblinPlayerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            var host = Has(args, "-playgoblin-host");
            var run = Has(args, "-playgoblin-run");
            if (!host && !run) return;

            var pump = new GameObject("[PlayGoblin Host Pump]").AddComponent<HostPump>();
            UnityEngine.Object.DontDestroyOnLoad(pump.gameObject);
            PlayGoblinHost.StartHandler = o => { PlayGoblinRun.StartRun(o); return "running"; };
            if (host) PlayGoblinHost.Start(PlayGoblinHost.ResolvePort());

            if (run)
            {
                var o = new PlayGoblinRunOptions();
                if (int.TryParse(Value(args, "-playgoblin-seed"), out var seed)) o.seed = seed;
                if (float.TryParse(Value(args, "-playgoblin-minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && min > 0) o.minutes = min;
                if (Has(args, "-playgoblin-quit")) PlayGoblinRun.Ended += _ => Application.Quit();
                PlayGoblinRun.StartRun(o);
            }
        }

        static bool Has(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

        static string Value(string[] args, string flag)
        {
            var i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        sealed class HostPump : MonoBehaviour
        {
            void Update() => PlayGoblinHost.Pump();
            void OnApplicationQuit() => PlayGoblinHost.Stop();
        }
    }
}
