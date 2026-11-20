using System;
using System.Text;
using CourtSim.Core.Json;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class JsonTests
    {
        [Test]
        public void Parse_Literals_ReturnsMatchingKinds()
        {
            Assert.That(JsonReader.Parse("null").IsNull, Is.True);
            Assert.That(JsonReader.Parse("true").AsBool(), Is.True);
            Assert.That(JsonReader.Parse("false").AsBool(), Is.False);
            Assert.That(JsonReader.Parse(" \t\r\n42 \n").AsNumber(), Is.EqualTo(42));
            Assert.That(JsonReader.Parse("\"hi\"").AsString(), Is.EqualTo("hi"));
        }

        [Test]
        public void Parse_NestedDocument_ExposesObjectsAndArraysInOrder()
        {
            JsonValue root = JsonReader.Parse("{ \"b\": [1, {\"c\": \"x\"}, []], \"a\": {}, \"n\": null }");

            Assert.That(root.Kind, Is.EqualTo(JsonKind.Object));
            Assert.That(root.Properties.Count, Is.EqualTo(3));
            Assert.That(root.Properties[0].Key, Is.EqualTo("b"));
            Assert.That(root.Properties[1].Key, Is.EqualTo("a"));
            JsonValue b = root.Get("b");
            Assert.That(b.Kind, Is.EqualTo(JsonKind.Array));
            Assert.That(b.Items.Count, Is.EqualTo(3));
            Assert.That(b.Items[1].GetString("c"), Is.EqualTo("x"));
            Assert.That(b.Items[2].Items, Is.Empty);
            Assert.That(root.Get("a").Properties, Is.Empty);
            Assert.That(root.Get("n").IsNull, Is.True);
            Assert.That(root.Has("n"), Is.True);
            Assert.That(root.Has("missing"), Is.False);
        }

        [Test]
        public void Parse_DuplicateKeys_LastValueWins()
        {
            JsonValue root = JsonReader.Parse("{\"a\":1,\"a\":2}");

            Assert.That(root.Properties.Count, Is.EqualTo(1));
            Assert.That(root.GetNumber("a"), Is.EqualTo(2));
        }

        [Test]
        public void Parse_StandardEscapes_AreDecoded()
        {
            JsonValue value = JsonReader.Parse("\"q\\\" b\\\\ s\\/ \\b\\f\\n\\r\\t\"");

            Assert.That(value.AsString(), Is.EqualTo("q\" b\\ s/ \b\f\n\r\t"));
        }

        [Test]
        public void Parse_UnicodeEscapes_DecodeBmpAndSurrogatePairs()
        {
            Assert.That(JsonReader.Parse("\"caf\\u00e9\"").AsString(), Is.EqualTo("caf\u00e9"));
            Assert.That(JsonReader.Parse("\"\\u00C9\"").AsString(), Is.EqualTo("\u00c9"));
            Assert.That(JsonReader.Parse("\"\\ud83d\\ude00\"").AsString(), Is.EqualTo("\ud83d\ude00"));
        }

        [Test]
        public void Parse_RawUnicode_IsPreserved()
        {
            Assert.That(JsonReader.Parse("\"\u4e2d\u6587 \ud83d\ude00\"").AsString(), Is.EqualTo("\u4e2d\u6587 \ud83d\ude00"));
        }

        [TestCase("0", 0d)]
        [TestCase("-0", 0d)]
        [TestCase("7", 7d)]
        [TestCase("-12.5", -12.5d)]
        [TestCase("1.5e3", 1500d)]
        [TestCase("1E-2", 0.01d)]
        [TestCase("2e+2", 200d)]
        [TestCase("123456789012", 123456789012d)]
        public void Parse_ValidNumber_ReturnsValue(string text, double expected)
        {
            Assert.That(JsonReader.Parse(text).AsNumber(), Is.EqualTo(expected));
        }

        [TestCase("-", 1)]
        [TestCase("1.", 2)]
        [TestCase("1.e5", 2)]
        [TestCase("1e", 2)]
        [TestCase("1e+", 3)]
        [TestCase("-a", 1)]
        [TestCase("01", 1)]
        [TestCase("+1", 0)]
        [TestCase(".5", 0)]
        [TestCase("1e400", 0)]
        public void Parse_InvalidNumber_ThrowsAtPosition(string text, int position)
        {
            JsonParseException error = Assert.Throws<JsonParseException>(() => JsonReader.Parse(text));

            Assert.That(error.Position, Is.EqualTo(position));
            Assert.That(error.Message, Does.Contain("at character " + position));
        }

        [TestCase("", 0, "Unexpected end")]
        [TestCase("   ", 3, "Unexpected end")]
        [TestCase("{\"a\" 1}", 5, "Expected ':'")]
        [TestCase("{\"a\":1,}", 7, "Expected a property name")]
        [TestCase("{\"a\":1 \"b\":2}", 7, "Expected ',' or '}'")]
        [TestCase("{a:1}", 1, "Expected a property name")]
        [TestCase("[1,]", 3, "Unexpected character ']'")]
        [TestCase("[1 2]", 3, "Expected ',' or ']'")]
        [TestCase("[1", 2, "Expected ',' or ']'")]
        [TestCase("\"abc", 4, "Unterminated string")]
        [TestCase("\"a\\", 3, "Unterminated escape")]
        [TestCase("\"\\x\"", 3, "Invalid escape")]
        [TestCase("\"\\u12\"", 3, "Incomplete \\u escape")]
        [TestCase("\"\\uZZZZ\"", 3, "Invalid \\u escape")]
        [TestCase("\"a\u0001\"", 3, "Control characters")]
        [TestCase("tru", 0, "Expected 'true'")]
        [TestCase("nul", 0, "Expected 'null'")]
        [TestCase("fals", 0, "Expected 'false'")]
        [TestCase("1 2", 2, "Unexpected content")]
        [TestCase("{} x", 3, "Unexpected content")]
        [TestCase("'a'", 0, "Unexpected character")]
        public void Parse_InvalidText_ReportsMessageAndPosition(string text, int position, string fragment)
        {
            JsonParseException error = Assert.Throws<JsonParseException>(() => JsonReader.Parse(text));

            Assert.That(error.Position, Is.EqualTo(position));
            Assert.That(error.Message, Does.Contain(fragment));
        }

        [Test]
        public void Parse_Null_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => JsonReader.Parse(null));
        }

        [Test]
        public void Parse_NestingAtMaxDepth_Succeeds()
        {
            JsonValue value = JsonReader.Parse(Nested(JsonReader.MaxDepth, "1"));

            for (int i = 0; i < JsonReader.MaxDepth; i++)
            {
                value = value.Items[0];
            }

            Assert.That(value.AsNumber(), Is.EqualTo(1));
        }

        [Test]
        public void Parse_NestingBeyondMaxDepth_Throws()
        {
            JsonParseException error = Assert.Throws<JsonParseException>(() => JsonReader.Parse(Nested(JsonReader.MaxDepth + 1, "1")));

            Assert.That(error.Message, Does.Contain("nested more than"));
        }

        [Test]
        public void Parse_HostileDeepNesting_ThrowsInsteadOfOverflowing()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse(new string('[', 100000)));
            Assert.Throws<JsonParseException>(() => JsonReader.Parse(Nested(5000, "{}")));
        }

        [Test]
        public void Parse_EmptyContainersBeyondMaxDepth_Throws()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse(Nested(JsonReader.MaxDepth + 1, string.Empty)));
        }

        [Test]
        public void TryParse_Valid_ReturnsValueWithoutError()
        {
            bool ok = JsonReader.TryParse("[1]", out JsonValue value, out string error);

            Assert.That(ok, Is.True);
            Assert.That(value.Items.Count, Is.EqualTo(1));
            Assert.That(error, Is.Null);
        }

        [Test]
        public void TryParse_Invalid_ReturnsFalseWithPositionedError()
        {
            bool ok = JsonReader.TryParse("[1,", out JsonValue value, out string error);

            Assert.That(ok, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(error, Does.Contain("at character 3"));
        }

        [Test]
        public void TryParse_Null_ReturnsFalseWithMessage()
        {
            bool ok = JsonReader.TryParse(null, out JsonValue value, out string error);

            Assert.That(ok, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(error, Is.EqualTo("No JSON text was provided."));
        }

        [Test]
        public void ToJson_Strings_EscapesQuotesBackslashesAndControlCharacters()
        {
            string json = JsonValue.String("a\"b\\c\nd\re\tf\bg\fh\u0001i\u001fj/k\u00e9").ToJson();

            Assert.That(json, Is.EqualTo("\"a\\\"b\\\\c\\nd\\re\\tf\\bg\\fh\\u0001i\\u001fj/k\u00e9\""));
        }

        [Test]
        public void ToJson_Numbers_UseInvariantRoundTripFormat()
        {
            Assert.That(JsonValue.Number(16000).ToJson(), Is.EqualTo("16000"));
            Assert.That(JsonValue.Number(-1.25).ToJson(), Is.EqualTo("-1.25"));
            Assert.That(JsonValue.Number(0.1).ToJson(), Is.EqualTo("0.1"));
            double tiny = 1e-7;
            Assert.That(JsonReader.Parse(JsonValue.Number(tiny).ToJson()).AsNumber(), Is.EqualTo(tiny));
            Assert.That(JsonReader.Parse(JsonValue.Number(1e300).ToJson()).AsNumber(), Is.EqualTo(1e300));
        }

        [Test]
        public void ToJson_Document_IsCompactAndOrdered()
        {
            JsonValue root = JsonValue.NewObject()
                .Set("z", 1)
                .Set("a", true)
                .Set("s", "t")
                .Set("n", JsonValue.Null)
                .Set("arr", JsonValue.NewArray().Add(JsonValue.Bool(false)).Add(null).Add(JsonValue.StringArray(new[] { "x" })));

            Assert.That(root.ToJson(), Is.EqualTo("{\"z\":1,\"a\":true,\"s\":\"t\",\"n\":null,\"arr\":[false,null,[\"x\"]]}"));
            Assert.That(root.ToString(), Is.EqualTo(root.ToJson()));
        }

        [Test]
        public void RoundTrip_ParseWriteParse_IsStable()
        {
            const string text = "{\"s\":\"line\\nquote\\\" \\u00e9 \\ud83d\\ude00 \\u0002\",\"n\":[0,-1.5,1e-7,12345678901],"
                + "\"b\":[true,false,null],\"o\":{\"nested\":{\"deep\":[{}]}}}";

            string once = JsonReader.Parse(text).ToJson();
            string twice = JsonReader.Parse(once).ToJson();

            Assert.That(twice, Is.EqualTo(once));
            JsonValue reparsed = JsonReader.Parse(once);
            Assert.That(reparsed.GetString("s"), Is.EqualTo("line\nquote\" \u00e9 \ud83d\ude00 \u0002"));
            Assert.That(reparsed.Get("n").Items[3].AsNumber(), Is.EqualTo(12345678901d));
        }

        [Test]
        public void Set_ExistingKey_ReplacesValueInPlace()
        {
            JsonValue root = JsonValue.NewObject().Set("a", 1).Set("b", 2).Set("a", "x").Set("b", (JsonValue)null);

            Assert.That(root.ToJson(), Is.EqualTo("{\"a\":\"x\",\"b\":null}"));
        }

        [Test]
        public void Set_NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => JsonValue.NewObject().Set(null, 1));
        }

        [Test]
        public void Builders_OnWrongKind_Throw()
        {
            Assert.Throws<InvalidOperationException>(() => JsonValue.NewObject().Add(JsonValue.Null));
            Assert.Throws<InvalidOperationException>(() => JsonValue.NewArray().Set("a", 1));
            Assert.Throws<InvalidOperationException>(() => JsonValue.String("s").Set("a", 1));
        }

        [Test]
        public void Number_NaNOrInfinity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.Number(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.Number(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.Number(double.NegativeInfinity));
        }

        [Test]
        public void String_Null_ReturnsJsonNull()
        {
            Assert.That(JsonValue.String(null), Is.SameAs(JsonValue.Null));
        }

        [Test]
        public void Accessors_OnWrongKind_ReturnNullOrEmpty()
        {
            JsonValue number = JsonValue.Number(3);

            Assert.That(number.AsString(), Is.Null);
            Assert.That(number.AsBool(), Is.Null);
            Assert.That(JsonValue.String("3").AsNumber(), Is.Null);
            Assert.That(number.Get("a"), Is.Null);
            Assert.That(number.Has("a"), Is.False);
            Assert.That(number.Items, Is.Empty);
            Assert.That(number.Properties, Is.Empty);
            Assert.That(number.GetString("a", "fallback"), Is.EqualTo("fallback"));
            Assert.That(number.GetNumber("a"), Is.Null);
        }

        [Test]
        public void GetString_NonStringMember_ReturnsFallback()
        {
            JsonValue root = JsonReader.Parse("{\"n\":1,\"s\":\"v\"}");

            Assert.That(root.GetString("n", "fb"), Is.EqualTo("fb"));
            Assert.That(root.GetString("s", "fb"), Is.EqualTo("v"));
            Assert.That(root.GetString("missing"), Is.Null);
            Assert.That(root.GetNumber("s"), Is.Null);
        }

        private static string Nested(int depth, string inner)
        {
            var builder = new StringBuilder();
            builder.Append('[', depth).Append(inner).Append(']', depth);
            return builder.ToString();
        }
    }
}
