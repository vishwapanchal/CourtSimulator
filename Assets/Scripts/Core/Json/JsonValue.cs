using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CourtSim.Core.Json
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// A minimal JSON document model: enough to read case files and Claude API responses and to build
    /// request bodies, without a third-party dependency. Objects keep insertion order.
    /// </summary>
    /// <remarks>Unity's JsonUtility cannot represent dictionaries or schema-shaped payloads, and Core must not
    /// reference UnityEngine, hence this small type.</remarks>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);

        private readonly List<JsonValue> _items;
        private readonly List<KeyValuePair<string, JsonValue>> _properties;
        private readonly string _string;
        private readonly double _number;
        private readonly bool _bool;

        private JsonValue(JsonKind kind)
        {
            Kind = kind;
            if (kind == JsonKind.Array)
            {
                _items = new List<JsonValue>();
            }
            else if (kind == JsonKind.Object)
            {
                _properties = new List<KeyValuePair<string, JsonValue>>();
            }
        }

        private JsonValue(string value) : this(JsonKind.String)
        {
            _string = value;
        }

        private JsonValue(double value) : this(JsonKind.Number)
        {
            _number = value;
        }

        private JsonValue(bool value) : this(JsonKind.Bool)
        {
            _bool = value;
        }

        public JsonKind Kind { get; }

        public bool IsNull => Kind == JsonKind.Null;

        /// <summary>Array items; empty for non-arrays.</summary>
        public IReadOnlyList<JsonValue> Items => _items ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();

        /// <summary>Object members in insertion order; empty for non-objects.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Properties =>
            _properties ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)Array.Empty<KeyValuePair<string, JsonValue>>();

        public static JsonValue String(string value)
        {
            return value == null ? Null : new JsonValue(value);
        }

        public static JsonValue Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "JSON cannot represent NaN or infinity.");
            }

            return new JsonValue(value);
        }

        public static JsonValue Bool(bool value)
        {
            return new JsonValue(value);
        }

        public static JsonValue NewArray()
        {
            return new JsonValue(JsonKind.Array);
        }

        public static JsonValue NewObject()
        {
            return new JsonValue(JsonKind.Object);
        }

        /// <summary>An array of strings.</summary>
        public static JsonValue StringArray(IEnumerable<string> values)
        {
            JsonValue array = NewArray();
            foreach (string value in values)
            {
                array.Add(String(value));
            }

            return array;
        }

        /// <summary>Appends <paramref name="value"/> to this array and returns the array, for chaining.</summary>
        public JsonValue Add(JsonValue value)
        {
            RequireKind(JsonKind.Array);
            _items.Add(value ?? Null);
            return this;
        }

        /// <summary>Sets member <paramref name="key"/> (replacing any existing value) and returns this object.</summary>
        public JsonValue Set(string key, JsonValue value)
        {
            RequireKind(JsonKind.Object);
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            for (int i = 0; i < _properties.Count; i++)
            {
                if (_properties[i].Key == key)
                {
                    _properties[i] = new KeyValuePair<string, JsonValue>(key, value ?? Null);
                    return this;
                }
            }

            _properties.Add(new KeyValuePair<string, JsonValue>(key, value ?? Null));
            return this;
        }

        public JsonValue Set(string key, string value)
        {
            return Set(key, String(value));
        }

        public JsonValue Set(string key, double value)
        {
            return Set(key, Number(value));
        }

        public JsonValue Set(string key, bool value)
        {
            return Set(key, Bool(value));
        }

        /// <summary>The member named <paramref name="key"/>, or null if absent or this is not an object.</summary>
        public JsonValue Get(string key)
        {
            if (_properties == null)
            {
                return null;
            }

            foreach (KeyValuePair<string, JsonValue> property in _properties)
            {
                if (property.Key == key)
                {
                    return property.Value;
                }
            }

            return null;
        }

        public bool Has(string key)
        {
            return Get(key) != null;
        }

        /// <summary>The string value, or null if this is not a string.</summary>
        public string AsString()
        {
            return Kind == JsonKind.String ? _string : null;
        }

        /// <summary>The number value, or null if this is not a number.</summary>
        public double? AsNumber()
        {
            return Kind == JsonKind.Number ? _number : (double?)null;
        }

        public bool? AsBool()
        {
            return Kind == JsonKind.Bool ? _bool : (bool?)null;
        }

        /// <summary>Member <paramref name="key"/> as a string, or <paramref name="fallback"/>.</summary>
        public string GetString(string key, string fallback = null)
        {
            return Get(key)?.AsString() ?? fallback;
        }

        public double? GetNumber(string key)
        {
            return Get(key)?.AsNumber();
        }

        /// <summary>Compact JSON text for this value.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder();
            Write(builder);
            return builder.ToString();
        }

        public override string ToString()
        {
            return ToJson();
        }

        internal static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private void Write(StringBuilder builder)
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    builder.Append("null");
                    break;
                case JsonKind.Bool:
                    builder.Append(_bool ? "true" : "false");
                    break;
                case JsonKind.Number:
                    builder.Append(_number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonKind.String:
                    WriteString(builder, _string);
                    break;
                case JsonKind.Array:
                    builder.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }

                        _items[i].Write(builder);
                    }

                    builder.Append(']');
                    break;
                case JsonKind.Object:
                    builder.Append('{');
                    for (int i = 0; i < _properties.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }

                        WriteString(builder, _properties[i].Key);
                        builder.Append(':');
                        _properties[i].Value.Write(builder);
                    }

                    builder.Append('}');
                    break;
            }
        }

        private void RequireKind(JsonKind kind)
        {
            if (Kind != kind)
            {
                throw new InvalidOperationException($"Expected a JSON {kind}, but this value is {Kind}.");
            }
        }
    }
}
