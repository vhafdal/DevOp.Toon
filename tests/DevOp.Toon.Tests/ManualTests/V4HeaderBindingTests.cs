namespace DevOp.Toon.Tests;

public sealed class V4HeaderBindingTests
{
    [Fact]
    public void Decode_RepeatedFullHeaderAcrossTypedAndIgnoredScopes_RebindsTarget()
    {
        const string source = "A:\n  Rows[1]{Value}:\n    1\nB:\n  Rows[1]{Value}:\n    2147483648\nIgnored:\n  Rows[1]{Value}:\n    9\nC:\n  Rows[1]{Value}:\n    3";
        var result = ToonDecoder.Decode<HeaderScopes>(source,
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 })!;
        Assert.Equal(1, Assert.Single(result.A.Rows).Value);
        Assert.Equal(2147483648L, Assert.Single(result.B.Rows).Value);
        Assert.Equal(3, Assert.Single(result.C.Rows).Value);
    }

    [Theory]
    [InlineData("\"before:quoted\",after", "before:quoted", "after")]
    [InlineData("before,after:colon", "before", "after:colon")]
    [InlineData("\"before,quoted\",after:colon", "before,quoted", "after:colon")]
    public void Decode_UnkeyedRowColons_PreservesCellMeaning(string cells, string first, string second)
    {
        var result = ToonDecoder.Decode<List<Pair>>("[1]{First,Second}:\n  " + cells,
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 })!;
        var row = Assert.Single(result);
        Assert.Equal(first, row.First);
        Assert.Equal(second, row.Second);
    }

    [Fact]
    public void Decode_ColonBeforeDelimiter_EndsUnkeyedRowsAtFollowingProperty()
    {
        const string source = "Rows[1]{First,Second}:\n  before,after\nTail: value,with,commas";
        var result = ToonDecoder.Decode<RowsWithTail>(source,
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 })!;
        Assert.Single(result.Rows);
        Assert.Equal("value,with,commas", result.Tail);
    }

    [Fact]
    public void Decode_CustomRowSpillColon_PreservesText()
    {
        var result = ToonDecoder.Decode<List<Pair>>("[1]{First}:\n  before\n    Second: after:colon",
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.CustomV4_4, Indent = 2 })!;
        Assert.Equal("after:colon", Assert.Single(result).Second);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("\"123\"", "123")]
    [InlineData("\"true\"", "true")]
    [InlineData("\"null\"", "null")]
    [InlineData("\"\"", "")]
    [InlineData("\"😀\"", "😀")]
    [InlineData("\"a😀b\"", "a😀b")]
    [InlineData("\"\ud7ff\"", "\ud7ff")]
    [InlineData("\"\ue000\"", "\ue000")]
    [InlineData("\"\uffff\"", "\uffff")]
    [InlineData("\"escaped\\nline\"", "escaped\nline")]
    public void Decode_MutableStringHeader_PreservesText(string token, string expected)
    {
        var result = ToonDecoder.Decode<List<TextValue>>("[1]{Value}:\n  " + token,
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 })!;
        Assert.Equal(expected, Assert.Single(result).Value);
    }

    [Fact]
    public void Decode_MutableStringDuplicate_RejectsDuplicateKey()
        => Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<TextValue>("Value: first\nValue: second",
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 }));

    [Fact]
    public void Decode_MutableStringInvalidUnicode_RejectsToken()
        => Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<TextValue>("Value: \"\ud800\"",
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 }));

    [Fact]
    public void Decode_ReusedHeaderAcrossDifferentTargetTypes_BindsEachTarget()
    {
        const string source = "First[1]{Value}:\n  1\nSecond[1]{Value}:\n  2147483648\nThird[1]{Value}:\n  3";
        var result = ToonDecoder.Decode<Root>(source, new ToonDecodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.V4_4,
            Indent = 2
        })!;
        Assert.Equal(1, Assert.Single(result.First).Value);
        Assert.Equal(2147483648L, Assert.Single(result.Second).Value);
        Assert.Equal(3, Assert.Single(result.Third).Value);
    }

    public sealed class Root
    {
        public List<Small> First { get; set; } = [];
        public List<Large> Second { get; set; } = [];
        public List<Small> Third { get; set; } = [];
    }

    public sealed class Small
    {
        public int Value { get; set; }
    }

    public sealed class Large
    {
        public long Value { get; set; }
    }

    public sealed class TextValue
    {
        public string? Value { get; set; }
    }

    public sealed class Pair
    {
        public string? First { get; set; }
        public string? Second { get; set; }
    }

    public sealed class RowsWithTail
    {
        public List<Pair> Rows { get; set; } = [];
        public string? Tail { get; set; }
    }

    public sealed class HeaderScopes
    {
        public SmallRows A { get; set; } = new();
        public LargeRows B { get; set; } = new();
        public SmallRows C { get; set; } = new();
    }

    public sealed class SmallRows
    {
        public List<Small> Rows { get; set; } = [];
    }

    public sealed class LargeRows
    {
        public List<Large> Rows { get; set; } = [];
    }
}
