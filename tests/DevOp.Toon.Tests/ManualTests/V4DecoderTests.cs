using System.Text.Json.Nodes;

namespace DevOp.Toon.Tests;

public sealed class V4DecoderTests
{
    [Theory]
    [InlineData("items[2]{id,customer{name,country}}:\n  1,Ada,IS\n  2,Bob,DK", "{\"items\":[{\"id\":1,\"customer\":{\"name\":\"Ada\",\"country\":\"IS\"}},{\"id\":2,\"customer\":{\"name\":\"Bob\",\"country\":\"DK\"}}]}")]
    [InlineData("[2:]{age}:\n  alice: 30\n  bob: 25", "{\"alice\":{\"age\":30},\"bob\":{\"age\":25}}")]
    [InlineData("# root\nitems[2]{id}:\n # inside\n  1\n  # between\n  2", "{\"items\":[{\"id\":1},{\"id\":2}]}")]
    [InlineData("user.name: Ada\nempty: []", "{\"user.name\":\"Ada\",\"empty\":[]}")]
    [InlineData("items[1]:\n  - people[1:]{name}:\n      ada: Ada\n    active: true", "{\"items\":[{\"people\":{\"ada\":{\"name\":\"Ada\"}},\"active\":true}]}")]
    [InlineData("a: +1\nb: .5\nc: 1.\nd: 01", "{\"a\":\"+1\",\"b\":\".5\",\"c\":\"1.\",\"d\":\"01\"}")]
    [InlineData("x: \"😀\"", "{\"x\":\"😀\"}")]
    public void Decode_OfficialV44Forms_PreservesValues(string input, string expected)
        => AssertJson(expected, Decode(input));

    [Theory]
    [InlineData("items[1]{a}:\n  1,2")]
    [InlineData("items[1]{a,a}:")]
    [InlineData("items[0]{a}:\n  1")]
    [InlineData("x: 1\n  y: 2")]
    [InlineData("x[2:]:")]
    [InlineData("x[2:]{a}:\n  alice:")]
    [InlineData("x: \"\\ud83d\\ude00\"")]
    [InlineData("x: \"\\ud800\"")]
    [InlineData("x: \"abc\"junk")]
    [InlineData("[0]:\nother: true")]
    public void Decode_InvalidV44_Throws(string input)
        => Assert.Throws<ToonFormatException>(() => Decode(input));

    [Theory]
    [InlineData("[0]: 1,2,3", "[1,2,3]")]
    [InlineData("[999999999999999999999999999999999999]:\n  - 1\n  - 2", "[1,2]")]
    [InlineData("items[0]{a,a,b,b{x}}:\n  1,2,3,4", "{\"items\":[{\"a\":2,\"b\":{\"x\":4}}]}")]
    [InlineData("x:\n    y: 2", "{\"x\":{\"y\":2}}")]
    public void Decode_NonStrictV44Recovery_PreservesCompleteScope(string input, string expected)
        => AssertJson(expected, Decode(input, strict: false));

    [Fact]
    public void Decode_NonStrictWidthMismatch_Throws()
        => Assert.Throws<ToonFormatException>(() => Decode("items[1]{a}:\n  1,2", strict: false));

    [Theory]
    [InlineData("[1]{id,details{name}}:\n  1,Ada\n    tags[2]: x,y", "[{\"id\":1,\"details\":{\"name\":\"Ada\"},\"tags\":[\"x\",\"y\"]}]")]
    [InlineData("[2:]{id}:\n  a: 1\n    tags[1]: x\n  b: 2\n    tags[1]: y", "{\"a\":{\"id\":1,\"tags\":[\"x\"]},\"b\":{\"id\":2,\"tags\":[\"y\"]}}")]
    public void Decode_CustomV44Spills_PreservesNestedData(string input, string expected)
        => AssertJson(expected, Decode(input, ToonProtocolVersion.CustomV4_4));

    [Fact]
    public void Decode_CustomHeaderSpillCollision_Throws()
        => Assert.Throws<ToonFormatException>(() => Decode("[1]{id}:\n  1\n    id: 2", ToonProtocolVersion.CustomV4_4));

    [Fact]
    public void Decode_TypedNestedHeaderRecord_BindsWithoutNativeGraph()
    {
        var rows = ToonDecoder.Decode<List<Row>>("[2]{Id,Details{Name}}:\n  1,Ada\n  2,Bob", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 });
        Assert.Equal(new Row(1, new Details("Ada")), rows![0]);
        Assert.Equal(new Row(2, new Details("Bob")), rows[1]);
    }

    [Fact]
    public void Decode_TypedKeyedDictionary_BindsEntryValues()
    {
        var rows = ToonDecoder.Decode<IReadOnlyDictionary<string, Row>>("[2:]{Id,Details{Name}}:\n  a: 1,Ada\n  b: 2,Bob", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 });
        Assert.Equal("Ada", rows!["a"].Details.Name);
        Assert.Equal(2, rows["b"].Id);
    }

    [Fact]
    public void Decode_TypedLargeInteger_PreservesExactValue()
    {
        var value = ToonDecoder.Decode<long>("9007199254740993", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 });
        Assert.Equal(9007199254740993L, value);
    }

    [Fact]
    public void Decode_TypedUnknownField_StillValidatesItsValue()
        => Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Details>("Name: Ada\nUnknown: \"\\ud800\"", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));

    public sealed record Details(string Name);
    public sealed record Row(int Id, Details Details);

    [Theory]
    [InlineData("a: hello\t")]
    [InlineData("a: \thello")]
    [InlineData("a\t: hello")]
    [InlineData("a[2]: x\"y,z\",q")]
    public void DetectVersion_VersionSensitiveToken_RequiresHint(string source)
    {
        Assert.True(ToonDecoder.DetectVersion(source).RequiresVersionHint);
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(source));
    }

    [Theory]
    [InlineData("a: hello\tthere")]
    [InlineData("a: +word")]
    [InlineData("a[2\t]: one\ttwo")]
    [InlineData("items[1]:\n  - rows[2]{Id}:\n      1\n      2")]
    public void DetectVersion_SharedMeaning_DoesNotRequireHint(string source)
        => Assert.False(ToonDecoder.DetectVersion(source).RequiresVersionHint);

    [Fact]
    public void Decode_MutableNumericAndNullableMembers_PreservesValues()
    {
        var result = ToonDecoder.Decode<NumericRow>("Id: 2147483647\nNullable: null\nAmount: 1234.5678\nFlag: true\nState: 2\nWhen: \"2026-11-06T10:00:00+00:00\"", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 });
        Assert.Equal(int.MaxValue, result!.Id);
        Assert.Null(result.Nullable);
        Assert.Equal(1234.5678m, result.Amount);
        Assert.True(result.Flag);
        Assert.Equal(NumericState.Ready, result.State);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero), result.When);
    }

    public enum NumericState { Ready = 2 }
    public sealed class NumericRow
    {
        public int Id { get; set; }
        public int? Nullable { get; set; }
        public decimal Amount { get; set; }
        public bool Flag { get; set; }
        public NumericState State { get; set; }
        public DateTimeOffset When { get; set; }
    }

    [Fact]
    public void Decode_ThrowingPrimitiveSetter_DoesNotInvokeItTwice()
    {
        ThrowingSetter.Calls = 0;
        Assert.Throws<FormatException>(() => ToonDecoder.Decode<ThrowingSetter>("Value: 1", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
        Assert.Equal(1, ThrowingSetter.Calls);
    }

    public sealed class ThrowingSetter
    {
        public static int Calls;
        public int Value { get => 0; set { Calls++; throw new FormatException("setter failed"); } }
    }

    [Theory]
    [InlineData("items[1]{a,n{x}}:\n  1", "{\"items\":[{\"a\":1,\"n\":{}}]}")]
    [InlineData("items[1]{a}:\n  1,2", "{\"items\":[{\"a\":1}]}")]
    [InlineData("m[1:]{a}:\n    x: 1\n  y: 2", "{\"m\":{\"x\":{\"a\":1}}}")]
    [InlineData("rows[2]{a}:\n    1\n  x: 9\n    2", "{\"rows\":[{\"a\":1},{\"a\":2}]}")]
    [InlineData("a:\n    b: 1\n  c: 2\n    d: 3", "{\"a\":{\"b\":1,\"d\":3}}")]
    [InlineData("items[2]:\n  - a\n    b: 1\n  - c\n  x: 1\nz: 2", "{\"items\":[\"a\",\"c\"],\"z\":2}")]
    public void Decode_HistoricalNonStrictRecovery_UsesSelectedVersion(string source, string expected)
    {
        AssertJson(expected, Decode(source, ToonProtocolVersion.V4_3, strict: false));
        Assert.Throws<ToonFormatException>(() => Decode(source, ToonProtocolVersion.V4_4, strict: false));
    }

    [Fact]
    public void Decode_RepeatedNestedSchema_PreservesIndependentRowsAndCounts()
        => AssertJson("{\"a\":[{\"id\":1,\"n\":{\"x\":\"first\"}}],\"b\":[{\"id\":2,\"n\":{\"x\":\"second\"}},{\"id\":3,\"n\":{\"x\":\"third\"}}]}",
            Decode("a[1]{id,n{x}}:\n  1,first\nb[2]{id,n{x}}:\n  2,second\n  3,third"));

    [Theory]
    [InlineData("a[1]{x,y}:\n  1,2\nb[1|]{x,y}:\n  3|4")]
    [InlineData("a[1|]{x|y}:\n  1|2\nb[1]{x|y}:\n  3,4")]
    public void Decode_RepeatedFieldTextWithDifferentDelimiter_ValidatesEachHeader(string source)
        => Assert.Throws<ToonFormatException>(() => Decode(source));

    [Fact]
    public void Decode_V40RepeatedDuplicateSchema_StillRejectsDuplicateFieldsWithRows()
    {
        AssertJson("{\"a\":[]}", Decode("a[0]{x,x}:", ToonProtocolVersion.V4_0));
        Assert.Throws<ToonFormatException>(() => Decode("a[0]{x,x}:\nb[1]{x,x}:\n  1,2", ToonProtocolVersion.V4_0));
    }

    [Theory]
    [InlineData("x\u00a0y")]
    [InlineData("x\u2003y")]
    public void EncodeDecode_DefaultProtocol_PreservesInternalUnicodeWhitespace(string value)
    {
        var source = ToonEncoder.Encode(new { Text = value }, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 });
        Assert.Contains("Text: " + value, source);
        Assert.False(ToonDecoder.DetectVersion(source).RequiresVersionHint);
        AssertJson(System.Text.Json.JsonSerializer.Serialize(new { Text = value }), ToonDecoder.Toon2Json(source));
    }

    [Theory]
    [InlineData("x: \u00a0y")]
    [InlineData("x: y\u00a0")]
    [InlineData("x[2]: y\u00a0,z")]
    [InlineData("x\u00a0: 1")]
    public void DetectVersion_UnicodeWhitespaceAtTokenBoundary_RequiresHint(string source)
        => Assert.True(ToonDecoder.DetectVersion(source).RequiresVersionHint);

    [Fact]
    public void Decode_QuotedBclScalarCells_PreservesValuesAndOffsets()
    {
        var source = "[1]{When,Offset,Id,Duration,Maybe}:\n  \"2026-11-06T10:00:00.0000000Z\",\"2026-11-06T12:00:00.0000000+02:00\",11111111-2222-3333-4444-555555555555,\"1.02:03:04\",null";
        var rows = ToonDecoder.Decode<List<TextScalarRow>>(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 });
        var row = Assert.Single(rows!);
        Assert.Equal(new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc), row.When);
        Assert.Equal(DateTimeKind.Utc, row.When.Kind);
        Assert.Equal(TimeSpan.FromHours(2), row.Offset.Offset);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), row.Id);
        Assert.Equal(new TimeSpan(1, 2, 3, 4), row.Duration);
        Assert.Null(row.Maybe);
    }

    [Fact]
    public void Decode_EscapedDateToken_StillUnescapesBeforeBinding()
    {
        var row = ToonDecoder.Decode<TextScalarRow>("When: \"2026-\\u0031\\u0031-06T10:00:00.0000000Z\"", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 });
        Assert.Equal(new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc), row!.When);
    }

    [Fact]
    public void Decode_QuotedNullDate_IsNotConvertedToNull()
        => Assert.Throws<FormatException>(() => ToonDecoder.Decode<TextScalarRow>("Maybe: \"null\"", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));

    public sealed class TextScalarRow
    {
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
        public Guid Id { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime? Maybe { get; set; }
    }

    [Fact]
    public void Decode_TypedUnknownObject_ValidatesDuplicateWireKeysLikeDom()
    {
        const string source = "Good: 1\nIgnored:\n  x: 1\n  x: 2";
        var strict = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 };
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(source, strict));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<KnownOnly>(source, strict));
        var permissive = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2, Strict = false };
        Assert.Equal(1, ToonDecoder.Decode<KnownOnly>(source, permissive)!.Good);
        AssertJson("{\"Good\":1,\"Ignored\":{\"x\":2}}", ToonDecoder.Toon2Json(source, permissive));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_TypedUnknownCustomRows_RejectsHeaderSpillCollisionLikeDom(bool strict)
    {
        const string source = "Good: 1\nIgnored[1]{x}:\n  1\n    x: 2";
        var options = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.CustomV4_4, Indent = 2, Strict = strict };
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(source, options));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<KnownOnly>(source, options));
    }

    [Fact]
    public void Decode_TypedUnknownObject_WireKeyComparisonRemainsOrdinal()
    {
        const string source = "Good: 1\nIgnored:\n  x: 1\n  X: 2";
        Assert.Equal(1, ToonDecoder.Decode<KnownOnly>(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 })!.Good);
    }

    public sealed class KnownOnly
    {
        public int Good { get; set; }
    }

    private static string Decode(string input, ToonProtocolVersion version = ToonProtocolVersion.V4_4, bool strict = true)
        => ToonDecoder.Toon2Json(input, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2, Strict = strict });

    private static void AssertJson(string expected, string actual)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), actual);
}
