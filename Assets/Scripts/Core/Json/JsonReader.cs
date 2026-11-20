using System;
using System.Globalization;
using System.Text;

namespace CourtSim.Core.Json
{
    /// <summary>Thrown when text is not valid JSON. <see cref="Position"/> is the zero-based character offset.</summary>
    public sealed class JsonParseException : FormatException
    {
        public JsonParseException(string message, int position)
            : base($"{message} (at character {position})")
        {
            Position = position;
        }

        public int Position { get; }
    }

    /// <summary>Strict RFC 8259 parser producing <see cref="JsonValue"/> trees.</summary>
    public static class JsonReader
    {
        /// <summary>Nesting deeper than this is rejected, so hostile input cannot overflow the stack.</summary>
        public const int MaxDepth = 64;

        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var parser = new Parser(text);
            parser.SkipWhitespace();
            JsonValue value = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw parser.Error("Unexpected content after the JSON value");
            }

            return value;
        }

        /// <summary>Parses <paramref name="text"/>, returning false (and the error) instead of throwing.</summary>
        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonParseException ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
            catch (ArgumentNullException)
            {
                value = null;
                error = "No JSON text was provided.";
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _position;

            public Parser(string text)
            {
                _text = text;
            }

            public bool AtEnd => _position >= _text.Length;

            public JsonParseException Error(string message)
            {
                return new JsonParseException(message, _position);
            }

            public void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        return;
                    }

                    _position++;
                }
            }

            public JsonValue ParseValue(int depth)
            {
                if (AtEnd)
                {
                    throw Error("Unexpected end of JSON");
                }

                char c = _text[_position];

                // depth counts the containers already open; opening one more than MaxDepth is rejected.
                if ((c == '{' || c == '[') && depth >= MaxDepth)
                {
                    throw Error($"JSON is nested more than {MaxDepth} levels deep");
                }

                switch (c)
                {
                    case '{':
                        return ParseObject(depth);
                    case '[':
                        return ParseArray(depth);
                    case '"':
                        return JsonValue.String(ParseString());
                    case 't':
                        ExpectLiteral("true");
                        return JsonValue.Bool(true);
                    case 'f':
                        ExpectLiteral("false");
                        return JsonValue.Bool(false);
                    case 'n':
                        ExpectLiteral("null");
                        return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }

                        throw Error($"Unexpected character '{c}'");
                }
            }

            private JsonValue ParseObject(int depth)
            {
                JsonValue result = JsonValue.NewObject();
                _position++; // {
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_position] != '"')
                    {
                        throw Error("Expected a property name");
                    }

                    string key = ParseString();
                    SkipWhitespace();
                    if (!TryConsume(':'))
                    {
                        throw Error("Expected ':' after a property name");
                    }

                    SkipWhitespace();
                    result.Set(key, ParseValue(depth + 1));
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    if (!TryConsume(','))
                    {
                        throw Error("Expected ',' or '}' in an object");
                    }
                }
            }

            private JsonValue ParseArray(int depth)
            {
                JsonValue result = JsonValue.NewArray();
                _position++; // [
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    if (!TryConsume(','))
                    {
                        throw Error("Expected ',' or ']' in an array");
                    }
                }
            }

            private string ParseString()
            {
                _position++; // opening quote
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("Unterminated string");
                    }

                    char c = _text[_position++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c < 0x20)
                    {
                        throw Error("Control characters must be escaped in strings");
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        throw Error("Unterminated escape sequence");
                    }

                    char escape = _text[_position++];
                    switch (escape)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseHex4());
                            break;
                        default:
                            throw Error($"Invalid escape '\\{escape}'");
                    }
                }
            }

            private char ParseHex4()
            {
                if (_position + 4 > _text.Length)
                {
                    throw Error("Incomplete \\u escape");
                }

                string hex = _text.Substring(_position, 4);
                if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                {
                    throw Error($"Invalid \\u escape '{hex}'");
                }

                _position += 4;
                return (char)code;
            }

            private JsonValue ParseNumber()
            {
                int start = _position;
                TryConsume('-');
                if (AtEnd)
                {
                    throw Error("Incomplete number");
                }

                if (_text[_position] == '0')
                {
                    _position++;
                }
                else if (!ConsumeDigits())
                {
                    throw Error("Expected a digit");
                }

                if (TryConsume('.') && !ConsumeDigits())
                {
                    throw Error("Expected a digit after the decimal point");
                }

                if (!AtEnd && (_text[_position] == 'e' || _text[_position] == 'E'))
                {
                    _position++;
                    if (!TryConsume('+'))
                    {
                        TryConsume('-');
                    }

                    if (!ConsumeDigits())
                    {
                        throw Error("Expected a digit in the exponent");
                    }
                }

                string token = _text.Substring(start, _position - start);
                // Mono (Unity) fails out-of-range values where .NET Core returns infinity; treat both alike.
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsInfinity(value))
                {
                    throw new JsonParseException("Number is out of range", start);
                }

                return JsonValue.Number(value);
            }

            private bool ConsumeDigits()
            {
                int start = _position;
                while (!AtEnd && _text[_position] >= '0' && _text[_position] <= '9')
                {
                    _position++;
                }

                return _position > start;
            }

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
                {
                    throw Error($"Expected '{literal}'");
                }

                _position += literal.Length;
            }

            private bool TryConsume(char c)
            {
                if (!AtEnd && _text[_position] == c)
                {
                    _position++;
                    return true;
                }

                return false;
            }
        }
    }
}
