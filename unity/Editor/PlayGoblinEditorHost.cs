using UnityEditor;
using UnityEngine;

namespace PlayGoblin.Editor
{
    /// <summary>
    /// Starts the local API when the editor loads and handles starting runs from outside Play Mode:
    /// the request is saved, Play Mode is entered, and the run starts once the scene is playing.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayGoblinEditorHost
    {
        public const string EnabledPref = "PlayGoblin.HostEnabled";
        const string PendingKey = "PlayGoblin.PendingRun";
        const string StopOnEndKey = "PlayGoblin.StopPlayModeOnEnd";

        static PlayGoblinEditorHost()
        {
            PlayGoblinHost.StartHandler = StartRun;
            EditorApplication.update -= PlayGoblinHost.Pump;
            EditorApplication.update += PlayGoblinHost.Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= PlayGoblinHost.Stop;
            AssemblyReloadEvents.beforeAssemblyReload += PlayGoblinHost.Stop;
            EditorApplication.quitting -= PlayGoblinHost.Stop;
            EditorApplication.quitting += PlayGoblinHost.Stop;
            PlayGoblinRun.Ended -= OnRunEnded;
            PlayGoblinRun.Ended += OnRunEnded;

            // Batch mode (CI, test runs) doesn't need the API and shouldn't grab the port.
            if (!Application.isBatchMode && EditorPrefs.GetBool(EnabledPref, true))
            {
                PlayGoblinHost.Start(PlayGoblinHost.ResolvePort());
            }
        }

        public static void SetEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EnabledPref, enabled);
            if (enabled) PlayGoblinHost.Start(PlayGoblinHost.ResolvePort());
            else PlayGoblinHost.Stop();
        }

        public static string StartRun(PlayGoblinRunOptions options)
        {
            if (EditorApplication.isPlaying)
            {
                SessionState.SetBool(StopOnEndKey, options.stopPlayModeOnEnd);
                PlayGoblinRun.StartRun(options);
                return "running";
            }
            SessionState.SetString(PendingKey, JsonUtility.ToJson(options));
            // Enter Play Mode on the next editor tick so an HTTP caller gets its response first.
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
            return "starting";
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            var pending = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(pending)) return;
            SessionState.EraseString(PendingKey);
            var options = JsonUtility.FromJson<PlayGoblinRunOptions>(pending);
            SessionState.SetBool(StopOnEndKey, options.stopPlayModeOnEnd);
            PlayGoblinRun.StartRun(options);
        }

        static void OnRunEnded(PlayGoblinRun run)
        {
            if (!SessionState.GetBool(StopOnEndKey, false)) return;
            SessionState.EraseBool(StopOnEndKey);
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
        }
    }
}
