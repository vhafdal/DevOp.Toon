namespace DevOp.Toon.Tests;

public sealed class BlankIndentDetectionTests
{
    [Fact]
    public void Decode_AutoNonStrictTabBlank_DoesNotChangeHyphenHeaderIndent()
    {
        const string source = "Items[1]:\n  \t\n    - Lookup[1:]{Name,Other}:\n            a: first,\"second,quoted\"\n        Details:\n            Code: x\n        Tags[1]: z";
        Assert.False(ToonDecoder.DetectVersion(source).RequiresVersionHint);
        var automatic = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { Strict = false });
        var explicitResult = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 4, Strict = false });
        Assert.Equal(explicitResult, automatic);
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(source));
    }

    [Fact]
    public void Decode_AutoNonStrictTabBlank_IgnoresBlankFeatureClassification()
        => Assert.Equal("{\"a\":1,\"b\":2}", ToonDecoder.Toon2Json("a: 1\n  \t\nb: 2", new ToonDecodeOptions { Strict = false }));

    [Fact]
    public void Decode_TabBeforeHash_RemainsDataAfterBlankRecovery()
    {
        const string source = "items[1]{Value}:\n  \t\n \t#data";
        var options = ToonDecoder.DetectOptions(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Strict = false, Indent = 4 });
        Assert.Equal(4, options.Indent);
        Assert.Contains("#data", ToonDecoder.Toon2Json(source, options));
    }

    [Fact]
    public void Decode_AutoMixedTabData_InfersIndentOnlyFromSpaceIndentedRows()
    {
        const string source = "Rows[2]{Info{Value}}:\n    first\n \t#x";
        var automatic = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { Strict = false });
        var explicitResult = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 4, Strict = false });
        Assert.Equal(explicitResult, automatic);
        Assert.Contains("#x", automatic);
    }

    [Fact]
    public void Decode_AutoMixedTabQuotedRow_UsesTheSameDepthAsReader()
    {
        const string source = "Rows[2]{Info{Value},Name}:\n    first,A\n \tsecond,\"B,C\"";
        var automatic = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { Strict = false });
        var explicitResult = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 4, Strict = false });
        Assert.Equal(explicitResult, automatic);
        Assert.Contains("B,C", automatic);
    }
}
