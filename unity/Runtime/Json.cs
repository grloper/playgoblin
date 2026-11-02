using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PlayGoblin
{
    /// <summary>Minimal JSON writer. Output is always invariant-culture and valid JSON.</summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder _sb = new StringBuilder(256);
        bool _needComma;

        public JsonWriter BeginObject() { Sep(); _sb.Append('{'); _needComma = false; return this; }
        public JsonWriter EndObject() { _sb.Append('}'); _needComma = true; return this; }
        public JsonWriter BeginArray(string key) { Key(key); _sb.Append('['); _needComma = false; return this; }
        public JsonWriter EndArray() { _sb.Append(']'); _needComma = true; return this; }

        public JsonWriter Prop(string key, string value)
        {
            Key(key);
            if (value == null) _sb.Append("null"); else Escape(_sb, value);
            _needComma = true;
            return this;
        }

        public JsonWriter Prop(string key, double value) { Key(key); Number(value); _needComma = true; return this; }
        public JsonWriter Prop(string key, long value) { Key(key); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); _needComma = true; return this; }
        public JsonWriter Prop(string key, bool value) { Key(key); _sb.Append(value ? "true" : "false"); _needComma = true; return this; }

        public JsonWriter Prop(string key, Vector3 v)
        {
            Key(key);
            _sb.Append('[');
            Number(v.x); _sb.Append(','); Number(v.y); _sb.Append(','); Number(v.z);
            _sb.Append(']');
            _needComma = true;
            return this;
        }

        public JsonWriter Raw(string key, string rawJson) { Key(key); _sb.Append(rawJson); _needComma = true; return this; }

        public JsonWriter Item(string value) { Sep(); Escape(_sb, value); _needComma = true; return this; }

        public JsonWriter Props(IDictionary<string, string> data)
        {
            if (data == null) return this;
            foreach (var kv in data) Prop(kv.Key, kv.Value);
            return this;
        }

        public override string ToString() => _sb.ToString();

        void Key(string key)
        {
            Sep();
            if (key != null) { Escape(_sb, key); _sb.Append(':'); }
        }

        void Sep()
        {
            if (_needComma) _sb.Append(',');
            _needComma = false;
        }

        void Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { _sb.Append("null"); return; }
            _sb.Append(System.Math.Round(d, 3).ToString("0.###", CultureInfo.InvariantCulture));
        }

        public static void Escape(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            Escape(sb, s);
            return sb.ToString();
        }
    }

    public static class StableHash
    {
        /// <summary>FNV-1a 32-bit, hex. Stable across runs and machines (unlike string.GetHashCode).</summary>
        public static string Of(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s ?? "")
                {
                    h ^= c;
                    h *= 16777619;
                }
                return h.ToString("x8");
            }
        }
    }
}
