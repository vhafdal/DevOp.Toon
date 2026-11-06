namespace DevOp.Toon.Tests;

public sealed class ExplicitVersionDetectionTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void DetectOptions_ExplicitLegacyHashRow_UsesDataIndent(ToonProtocolVersion version)
    {
        const string source = "rows[1]{v}:\n #data";
        var options = ToonDecoder.DetectOptions(source, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 });
        Assert.Equal(1, options.Indent);
        Assert.Contains("#data", ToonDecoder.Toon2Json(source, options));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_0)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void DetectOptions_ExplicitV4CommentOnly_LeavesFallbackIndent(ToonProtocolVersion version)
    {
        var options = ToonDecoder.DetectOptions("rows[0]{v}:\r\n #comment\r\n   \r\n", new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 });
        Assert.Equal(2, options.Indent);
        Assert.Equal(version, options.ProtocolVersion);
    }

    [Theory]
    [InlineData("root:\n    value: 1\n  #comment", 4)]
    [InlineData("root:\r\n    value: 1\r\n  \"#data\": 2", 2)]
    [InlineData("root:\n  \t#data", 7)]
    [InlineData("root:\n\t#data\n   ", 7)]
    public void DetectOptions_ExplicitV4LeadingWhitespace_UsesLexicalIndent(string source, int expected)
    {
        var options = ToonDecoder.DetectOptions(source, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 7 });
        Assert.Equal(expected, options.Indent);
    }
}
