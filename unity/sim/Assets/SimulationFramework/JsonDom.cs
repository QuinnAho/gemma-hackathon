using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    internal enum JsonValueKind
    {
        Null,
        Object,
        Array,
        String,
        Number,
        Boolean
    }

    internal sealed class JsonValue
    {
        private readonly Dictionary<string, JsonValue> _objectValue;
        private readonly List<JsonValue> _arrayValue;
        private readonly string _stringValue;
        private readonly string _numberValue;
        private readonly bool _boolValue;

        private JsonValue(
            JsonValueKind kind,
            Dictionary<string, JsonValue> objectValue,
            List<JsonValue> arrayValue,
            string stringValue,
            string numberValue,
            bool boolValue)
        {
            Kind = kind;
            _objectValue = objectValue;
            _arrayValue = arrayValue;
            _stringValue = stringValue;
            _numberValue = numberValue;
            _boolValue = boolValue;
        }

        public JsonValueKind Kind { get; private set; }

        public IReadOnlyDictionary<string, JsonValue> ObjectValue
        {
            get { return _objectValue; }
        }

        public IReadOnlyList<JsonValue> ArrayValue
        {
            get { return _arrayValue; }
        }

        public static JsonValue CreateNull()
        {
            return new JsonValue(JsonValueKind.Null, null, null, null, null, false);
        }

        public static JsonValue CreateObject(Dictionary<string, JsonValue> value)
        {
            return new JsonValue(
                JsonValueKind.Object,
                value ?? new Dictionary<string, JsonValue>(StringComparer.Ordinal),
                null,
                null,
                null,
                false);
        }

        public static JsonValue CreateArray(List<JsonValue> value)
        {
            return new JsonValue(JsonValueKind.Array, null, value ?? new List<JsonValue>(), null, null, false);
        }

        public static JsonValue CreateString(string value)
        {
            return new JsonValue(JsonValueKind.String, null, null, value ?? string.Empty, null, false);
        }

        public static JsonValue CreateNumber(string value)
        {
            return new JsonValue(JsonValueKind.Number, null, null, null, value ?? "0", false);
        }

        public static JsonValue CreateBoolean(bool value)
        {
            return new JsonValue(JsonValueKind.Boolean, null, null, null, null, value);
        }

        public bool TryGetProperty(string name, out JsonValue value)
        {
            value = null;
            return Kind == JsonValueKind.Object &&
                   _objectValue != null &&
                   _objectValue.TryGetValue(name, out value);
        }

        public bool TryGetString(out string value)
        {
            value = string.Empty;
            if (Kind != JsonValueKind.String)
            {
                return false;
            }

            value = _stringValue ?? string.Empty;
            return true;
        }

        public bool TryGetBoolean(out bool value)
        {
            value = false;
            if (Kind != JsonValueKind.Boolean)
            {
                return false;
            }

            value = _boolValue;
            return true;
        }

        public bool TryGetDouble(out double value)
        {
            value = 0.0;
            if (Kind != JsonValueKind.Number || string.IsNullOrWhiteSpace(_numberValue))
            {
                return false;
            }

            return double.TryParse(
                _numberValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        public bool TryGetInt32(out int value)
        {
            value = 0;
            if (Kind != JsonValueKind.Number || string.IsNullOrWhiteSpace(_numberValue))
            {
                return false;
            }

            return int.TryParse(
                _numberValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        public string ToJson()
        {
            var builder = new StringBuilder(128);
            AppendJson(builder);
            return builder.ToString();
        }

        private void AppendJson(StringBuilder builder)
        {
            switch (Kind)
            {
                case JsonValueKind.Null:
                    builder.Append("null");
                    break;
                case JsonValueKind.Object:
                    builder.Append('{');
                    if (_objectValue != null)
                    {
                        var isFirst = true;
                        foreach (var pair in _objectValue)
                        {
                            if (!isFirst)
                            {
                                builder.Append(',');
                            }

                            isFirst = false;
                            builder.Append('"');
                            builder.Append(JsonText.Escape(pair.Key));
                            builder.Append("\":");
                            (pair.Value ?? CreateNull()).AppendJson(builder);
                        }
                    }

                    builder.Append('}');
                    break;
                case JsonValueKind.Array:
                    builder.Append('[');
                    if (_arrayValue != null)
                    {
                        for (var i = 0; i < _arrayValue.Count; i++)
                        {
                            if (i > 0)
                            {
                                builder.Append(',');
                            }

                            (_arrayValue[i] ?? CreateNull()).AppendJson(builder);
                        }
                    }

                    builder.Append(']');
                    break;
                case JsonValueKind.String:
                    builder.Append('"');
                    builder.Append(JsonText.Escape(_stringValue ?? string.Empty));
                    builder.Append('"');
                    break;
                case JsonValueKind.Number:
                    builder.Append(string.IsNullOrWhiteSpace(_numberValue) ? "0" : _numberValue);
                    break;
                case JsonValueKind.Boolean:
                    builder.Append(_boolValue ? "true" : "false");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported JSON value kind.");
            }
        }
    }

    internal static class JsonDom
    {
        public static JsonValue Parse(string json)
        {
            var parser = new Parser(json ?? string.Empty);
            var value = parser.ParseValue();
            parser.SkipWhitespace();

            if (!parser.IsAtEnd)
            {
                throw new FormatException("Unexpected trailing characters after JSON value.");
            }

            return value;
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _position;

            public Parser(string json)
            {
                _json = json ?? string.Empty;
            }

            public bool IsAtEnd
            {
                get { return _position >= _json.Length; }
            }

            public void SkipWhitespace()
            {
                while (!IsAtEnd && char.IsWhiteSpace(_json[_position]))
                {
                    _position++;
                }
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();

                if (IsAtEnd)
                {
                    throw new FormatException("Unexpected end of JSON.");
                }

                switch (_json[_position])
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return JsonValue.CreateString(ParseString());
                    case 't':
                        ConsumeLiteral("true");
                        return JsonValue.CreateBoolean(true);
                    case 'f':
                        ConsumeLiteral("false");
                        return JsonValue.CreateBoolean(false);
                    case 'n':
                        ConsumeLiteral("null");
                        return JsonValue.CreateNull();
                    default:
                        if (_json[_position] == '-' || char.IsDigit(_json[_position]))
                        {
                            return JsonValue.CreateNumber(ParseNumber());
                        }

                        throw new FormatException(
                            "Unexpected token at position " + _position.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }

            private JsonValue ParseObject()
            {
                var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                Expect('{');
                SkipWhitespace();

                if (TryConsume('}'))
                {
                    return JsonValue.CreateObject(result);
                }

                while (true)
                {
                    SkipWhitespace();
                    var propertyName = ParseString();
                    SkipWhitespace();
                    Expect(':');

                    result[propertyName] = ParseValue();

                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }

                    Expect(',');
                }

                return JsonValue.CreateObject(result);
            }

            private JsonValue ParseArray()
            {
                var result = new List<JsonValue>();
                Expect('[');
                SkipWhitespace();

                if (TryConsume(']'))
                {
                    return JsonValue.CreateArray(result);
                }

                while (true)
                {
                    result.Add(ParseValue());

                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        break;
                    }

                    Expect(',');
                }

                return JsonValue.CreateArray(result);
            }

            private string ParseString()
            {
                Expect('"');

                var builder = new StringBuilder();
                while (!IsAtEnd)
                {
                    var character = _json[_position++];
                    if (character == '"')
                    {
                        return builder.ToString();
                    }

                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (IsAtEnd)
                    {
                        throw new FormatException("Unexpected end of JSON string escape.");
                    }

                    var escape = _json[_position++];
                    switch (escape)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escape);
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
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw new FormatException("Unsupported JSON escape sequence.");
                    }
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private string ParseNumber()
            {
                var start = _position;

                if (_json[_position] == '-')
                {
                    _position++;
                }

                ConsumeDigits();

                if (!IsAtEnd && _json[_position] == '.')
                {
                    _position++;
                    ConsumeDigits();
                }

                if (!IsAtEnd && (_json[_position] == 'e' || _json[_position] == 'E'))
                {
                    _position++;

                    if (!IsAtEnd && (_json[_position] == '+' || _json[_position] == '-'))
                    {
                        _position++;
                    }

                    ConsumeDigits();
                }

                return _json.Substring(start, _position - start);
            }

            private void ConsumeDigits()
            {
                var start = _position;
                while (!IsAtEnd && char.IsDigit(_json[_position]))
                {
                    _position++;
                }

                if (start == _position)
                {
                    throw new FormatException("Expected digits in JSON number.");
                }
            }

            private char ParseUnicodeEscape()
            {
                if (_position + 4 > _json.Length)
                {
                    throw new FormatException("Incomplete JSON unicode escape.");
                }

                var hex = _json.Substring(_position, 4);
                _position += 4;

                int codePoint;
                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codePoint))
                {
                    throw new FormatException("Invalid JSON unicode escape.");
                }

                return (char)codePoint;
            }

            private void ConsumeLiteral(string literal)
            {
                if (_position + literal.Length > _json.Length ||
                    string.CompareOrdinal(_json, _position, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException(
                        "Expected `" + literal + "` at position " + _position.ToString(CultureInfo.InvariantCulture) + ".");
                }

                _position += literal.Length;
            }

            private void Expect(char value)
            {
                SkipWhitespace();

                if (IsAtEnd || _json[_position] != value)
                {
                    throw new FormatException(
                        "Expected `" + value + "` at position " + _position.ToString(CultureInfo.InvariantCulture) + ".");
                }

                _position++;
            }

            private bool TryConsume(char value)
            {
                SkipWhitespace();

                if (!IsAtEnd && _json[_position] == value)
                {
                    _position++;
                    return true;
                }

                return false;
            }
        }
    }
}
