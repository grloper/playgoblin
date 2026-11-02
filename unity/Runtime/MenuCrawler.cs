using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PlayGoblin
{
    /// <summary>Reads text from uGUI Text and (through reflection, so TextMeshPro stays optional) TMP_Text.</summary>
    public static class UiText
    {
        static readonly Type Tmp = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
        static readonly PropertyInfo TmpText = Tmp?.GetProperty("text");

        public static T[] Find<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }

        static Object[] FindType(Type t)
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType(t);
#else
            return Object.FindObjectsByType(t, FindObjectsSortMode.None);
#endif
        }

        public static int IdOf(Object o)
        {
#if UNITY_6000_4_OR_NEWER
            return o.GetEntityId().GetHashCode();
#else
            return o.GetInstanceID();
#endif
        }

        public static string Of(Component c)
        {
            if (c is Text t) return t.text;
            if (Tmp != null && Tmp.IsInstanceOfType(c)) return TmpText.GetValue(c) as string;
            return null;
        }

        public static List<Component> AllActive()
        {
            var list = new List<Component>();
            foreach (var t in UiText.Find<Text>())
                if (t.isActiveAndEnabled) list.Add(t);
            if (Tmp != null)
                foreach (var o in FindType(Tmp))
                    if (o is Behaviour b && b.isActiveAndEnabled) list.Add(b);
            return list;
        }

        public static string LabelOf(Button b)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && !string.IsNullOrWhiteSpace(t.text)) return t.text.Trim();
            if (Tmp != null)
            {
                var c = b.GetComponentInChildren(Tmp);
                var s = c != null ? TmpText.GetValue(c) as string : null;
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            return b.name;
        }

        public static string PathOf(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }

    public static class PlaceholderRules
    {
        static readonly Regex Rx = new Regex(@"\b(todo|fixme|lorem ipsum|placeholder|text here|insert text|new text)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsPlaceholder(string s) => !string.IsNullOrWhiteSpace(s) && Rx.IsMatch(s);
    }

    /// <summary>Reports placeholder text that is visible on screen, once per distinct string.</summary>
    public sealed class PlaceholderScanner
    {
        readonly HashSet<string> _seen = new HashSet<string>();
        float _next;

        public void Tick(float unscaledNow)
        {
            if (unscaledNow < _next) return;
            _next = unscaledNow + 2f;
            foreach (var c in UiText.AllActive())
            {
                var s = UiText.Of(c);
                if (!PlaceholderRules.IsPlaceholder(s) || !_seen.Add(s.Trim())) continue;
                PlayGoblinEvents.Add("placeholder_text", s.Trim(), null, new Dictionary<string, string>
                {
                    { "path", UiText.PathOf(c.transform) },
                    { "scene", SceneManager.GetActiveScene().path }
                });
            }
        }
    }

    /// <summary>
    /// Clicks every active uGUI <see cref="Button"/> on screen and follows the screens they open. A button that does
    /// nothing visible on two clicks (no UI change, no scene change, no log output) is reported as dead.
    /// Buttons that look like Quit/Exit are never clicked. Works until it runs out of buttons or hits its limits.
    /// </summary>
    public sealed class MenuCrawler : IDisposable
    {
        const float ClickEvery = 0.5f;
        const float SettleSeconds = 0.6f;

        static readonly Regex Dangerous = new Regex(@"\b(quit|exit|delete|reset|erase|wipe|purchase|buy)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Back = new Regex(@"\b(back|close|cancel|return|done)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        readonly PlayGoblinRunOptions _options;
        readonly HashSet<string> _done = new HashSet<string>();
        readonly HashSet<string> _backed = new HashSet<string>();
        int _logCount;
        int _clicks;
        float _nextAction;

        Button _pending;
        string _pendingKey, _pendingLabel;
        int _pendingHash, _pendingLogs, _attempt;
        float _pendingDue;

        public int Clicks => _clicks;
        public int DeadButtons { get; private set; }

        public MenuCrawler(PlayGoblinRunOptions options)
        {
            _options = options;
            Application.logMessageReceived += OnLog;
        }

        public void Dispose() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type) => _logCount++;

        public static bool IsDangerous(string label) => !string.IsNullOrEmpty(label) && Dangerous.IsMatch(label);

        /// <summary>Call every frame. Returns true while there is still something to click or a click is being judged.</summary>
        public bool Tick(float unscaledNow)
        {
            if (_pending != null || _pendingKey != null)
            {
                if (unscaledNow < _pendingDue) return true;
                Judge(unscaledNow);
                return true;
            }
            if (unscaledNow < _nextAction) return HasWork();
            _nextAction = unscaledNow + ClickEvery;
            if (_clicks >= _options.maxMenuClicks) return false;

            var buttons = ActiveButtons();
            Button next = null;
            foreach (var b in buttons)
            {
                if (_done.Contains(KeyOf(b)) || IsDangerous(UiText.LabelOf(b)) || IsDangerous(b.name)) continue;
                next = b;
                break;
            }
            if (next == null)
            {
                // Everything on this screen is clicked: step back out once, then carry on with what is left above.
                var sig = ScreenKey(buttons);
                if (!_backed.Add(sig)) return false;
                foreach (var b in buttons)
                    if (Back.IsMatch(UiText.LabelOf(b)) && !IsDangerous(UiText.LabelOf(b))) { next = b; break; }
                if (next == null) return false;
            }
            Click(next, attempt: 1, unscaledNow);
            return true;
        }

        bool HasWork()
        {
            if (_clicks >= _options.maxMenuClicks) return false;
            foreach (var b in ActiveButtons())
                if (!_done.Contains(KeyOf(b)) && !IsDangerous(UiText.LabelOf(b)) && !IsDangerous(b.name)) return true;
            return false;
        }

        void Click(Button b, int attempt, float now)
        {
            _pending = b;
            _pendingKey = KeyOf(b);
            _pendingLabel = UiText.LabelOf(b);
            _pendingHash = Signature();
            _pendingLogs = _logCount;
            _attempt = attempt;
            _pendingDue = now + SettleSeconds;
            _clicks++;
            try { b.onClick.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        void Judge(float now)
        {
            var key = _pendingKey;
            var label = _pendingLabel;
            var changed = Signature() != _pendingHash || _logCount != _pendingLogs;
            var button = _pending;
            _pending = null;
            _pendingKey = null;
            _done.Add(key);

            var data = new Dictionary<string, string>
            {
                { "path", key }, { "label", label }, { "scene", SceneManager.GetActiveScene().path },
                { "result", changed ? "changed" : "no_change" }
            };
            if (changed || button == null)
            {
                PlayGoblinEvents.Add("ui_click", label, null, data);
                return;
            }
            if (_attempt == 1)
            {
                // One more try: the first click may only have focused or animated something.
                _done.Remove(key);
                Click(button, 2, now);
                return;
            }
            DeadButtons++;
            PlayGoblinEvents.Add("dead_button", label, null, data);
        }

        static string KeyOf(Button b) => SceneManager.GetActiveScene().name + ":" + UiText.PathOf(b.transform);

        static string ScreenKey(List<Button> buttons)
        {
            var keys = new List<string>();
            foreach (var b in buttons) keys.Add(KeyOf(b));
            keys.Sort(StringComparer.Ordinal);
            return string.Join("|", keys);
        }

        static List<Button> ActiveButtons()
        {
            var list = new List<Button>();
            foreach (var b in UiText.Find<Button>())
                if (b.isActiveAndEnabled && b.interactable) list.Add(b);
            list.Sort((a, b) => string.CompareOrdinal(UiText.PathOf(a.transform), UiText.PathOf(b.transform)));
            return list;
        }

        /// <summary>Order-independent fingerprint of what is on screen: scene, active graphics, their sprites, colors and text.</summary>
        static int Signature()
        {
            unchecked
            {
                var h = SceneManager.GetActiveScene().path.GetHashCode();
                foreach (var g in UiText.Find<Graphic>())
                {
                    if (!g.isActiveAndEnabled) continue;
                    var e = UiText.IdOf(g) * 31 + g.color.GetHashCode();
                    if (g is Image im && im.sprite != null) e = e * 31 + UiText.IdOf(im.sprite);
                    var text = UiText.Of(g);
                    if (text != null) e = e * 31 + text.GetHashCode();
                    h += e;
                }
                foreach (var s in UiText.Find<Selectable>())
                    if (s.isActiveAndEnabled) h += s.interactable ? 7 : 13;
                return h;
            }
        }
    }
}
