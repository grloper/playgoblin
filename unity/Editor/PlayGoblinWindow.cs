using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlayGoblin.Editor
{
    public sealed class PlayGoblinWindow : EditorWindow
    {
        int _seed = 42;
        float _minutes = 10f;
        bool _stopPlayModeOnEnd = true;

        [MenuItem("Window/PlayGoblin")]
        public static void Open() => GetWindow<PlayGoblinWindow>("PlayGoblin");

        void OnEnable() => EditorApplication.update += Tick;
        void OnDisable() => EditorApplication.update -= Tick;

        double _nextRepaint;
        void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.5;
            Repaint();
        }

        void OnGUI()
        {
            var snap = PlayGoblinRun.Snapshot();
            var running = snap.State == PlayGoblinRunState.Running || snap.State == PlayGoblinRunState.Starting;

            EditorGUILayout.LabelField("Run", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(running))
            {
                _seed = EditorGUILayout.IntField("Seed", _seed);
                _minutes = Mathf.Max(0.1f, EditorGUILayout.FloatField("Minutes", _minutes));
                _stopPlayModeOnEnd = EditorGUILayout.Toggle("Exit Play Mode when done", _stopPlayModeOnEnd);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(running))
                {
                    if (GUILayout.Button("Start walk", GUILayout.Height(28)))
                    {
                        PlayGoblinEditorHost.StartRun(new PlayGoblinRunOptions
                        {
                            seed = _seed, minutes = _minutes, stopPlayModeOnEnd = _stopPlayModeOnEnd
                        });
                    }
                }
                using (new EditorGUI.DisabledScope(!running || PlayGoblinRun.Current == null))
                {
                    if (GUILayout.Button("Stop", GUILayout.Height(28))) PlayGoblinRun.Current?.Stop();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", snap.State.ToString());
            if (!string.IsNullOrEmpty(snap.RunId))
            {
                EditorGUILayout.LabelField("Run", snap.RunId);
                EditorGUILayout.LabelField("Time", $"{snap.T:0}s");
                EditorGUILayout.LabelField("Stuck spots", snap.Stucks.ToString());
                EditorGUILayout.LabelField("Falls out of world", snap.Falls.ToString());
                EditorGUILayout.LabelField("Distinct errors", snap.Errors.ToString());
                EditorGUILayout.LabelField("Coverage", $"{snap.Coverage * 100f:0}%");
                if (!string.IsNullOrEmpty(snap.EndReason)) EditorGUILayout.LabelField("Ended", snap.EndReason);
                if (!string.IsNullOrEmpty(snap.RunDirectory) && Directory.Exists(snap.RunDirectory)
                    && GUILayout.Button("Open run folder"))
                {
                    EditorUtility.RevealInFinder(Path.Combine(snap.RunDirectory, "events.jsonl"));
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Local API", EditorStyles.boldLabel);
            var enabled = EditorPrefs.GetBool(PlayGoblinEditorHost.EnabledPref, true);
            var newEnabled = EditorGUILayout.Toggle("Enabled", enabled);
            if (newEnabled != enabled) PlayGoblinEditorHost.SetEnabled(newEnabled);
            EditorGUILayout.LabelField("Address", PlayGoblinHost.IsRunning ? $"http://127.0.0.1:{PlayGoblinHost.Port}" : "off");
            if (!string.IsNullOrEmpty(PlayGoblinHost.LastError))
                EditorGUILayout.HelpBox(PlayGoblinHost.LastError, MessageType.Warning);
        }
    }
}
