namespace DevOp.Toon.Tests;

public sealed class HyphenHeaderDetectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DetectVersion_NamedHyphenTableFollowedByNestedFields_SelectsOfficial(int indent)
    {
        var source = "Items[1]:\n" + Spaces(indent) + "- Lookup[2:]{Value}:\n"
            + Spaces(indent * 3) + "a: 1\n" + Spaces(indent * 3) + "b: 2\n"
            + Spaces(indent * 2) + "Details:\n" + Spaces(indent * 3) + "Child:\n"
            + Spaces(indent * 4) + "Code: x\n" + Spaces(indent * 2) + "Tags[1]: z";
        var detection = ToonDecoder.DetectVersion(source);
        Assert.False(detection.RequiresVersionHint);
        Assert.Equal(ToonProtocolVersion.V4_4, detection.SelectedVersion);
        Assert.Contains("\"Code\":\"x\"", ToonDecoder.Toon2Json(source));
    }

    [Fact]
    public void DetectVersion_QuotedEmptyNamedHyphenTable_UsesLogicalDepth()
    {
        const string source = "Items[1]:\n  - \"\"[1:]{Value}:\n      a: 1\n    Details:\n      Child:\n        Code: x\n    Tags[1]: z";
        Assert.Equal(ToonProtocolVersion.V4_4, ToonDecoder.DetectVersion(source).SelectedVersion);
        Assert.Contains("\"Code\":\"x\"", ToonDecoder.Toon2Json(source));
    }

    [Fact]
    public void Decode_AutoNonStrictHyphenDepthJump_UsesFinalIndentUnit()
    {
        const string source = "Items[1]:\n    - Lookup[1:]{Name,Other}:\n        a: first,\"second,quoted\"\n      Details:\n        Code: x\n      Tags[1]: z";
        Assert.False(ToonDecoder.DetectVersion(source).RequiresVersionHint);
        var automatic = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { Strict = false });
        var explicitResult = ToonDecoder.Toon2Json(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2, Strict = false });
        Assert.Equal(explicitResult, automatic);
        Assert.Contains("second,quoted", automatic);
    }

    [Fact]
    public void DetectVersion_ActualCustomSpillAfterNestedTable_SelectsCustom()
    {
        const string source = "[1]{Info{Value}}:\n  1\n    Lookup[1:]{Name}:\n      a: child\n    Tags[1]: z";
        Assert.Equal(ToonProtocolVersion.CustomV4_4, ToonDecoder.DetectVersion(source).SelectedVersion);
        Assert.Contains("\"Tags\":[\"z\"]", ToonDecoder.Toon2Json(source));
    }

    private static string Spaces(int count) => new(' ', count);
}
