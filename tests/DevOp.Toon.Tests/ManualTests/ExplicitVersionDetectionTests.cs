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

    [Theory]
    [InlineData("[3]{Id,C=1}:\n  1\n  2\n  3")]
    [InlineData("rows[3]{Id,Customer{Country=IS,Name}}:\n  1,Ada\n  2,Bob\n  3,Cy")]
    [InlineData("rows[3|]{Id|\"my key\"=5}:\n  1\n  2\n  3")]
    public void DetectVersion_UnquotedEqualsInHeader_SelectsCustomV44(string source)
    {
        var detection = ToonDecoder.DetectVersion(source);
        Assert.False(detection.RequiresVersionHint);
        Assert.Equal(ToonProtocolVersion.CustomV4_4, detection.SelectedVersion);
        Assert.Equal(ToonProtocolVersion.CustomV4_4, detection.DetectedVersion);
    }

    [Fact]
    public void DetectVersion_QuotedEqualsInHeaderKey_SelectsOfficial()
        => Assert.Equal(ToonProtocolVersion.V4_4, ToonDecoder.DetectVersion("[2]{Id,\"a=b\"}:\n  1,x\n  2,y").SelectedVersion);

    [Theory]
    [InlineData("[2]{Id,Expr}:\n  1,a=b\n  2,c=d")]
    [InlineData("x: a=b")]
    [InlineData("t[2]: a=b,c")]
    [InlineData("\"k=v\"[2]{Id}:\n  1\n  2")]
    public void DetectVersion_EqualsInCellOrValue_SelectsOfficial(string source)
        => Assert.Equal(ToonProtocolVersion.V4_4, ToonDecoder.DetectVersion(source).SelectedVersion);

    [Fact]
    public void Decode_AutoConstantHeaderWithoutSpill_RoundTrips()
    {
        const string source = "rows[3]{CompanyId=acme,AccountCode}:\n  6900\n  6910\n  6920";

        var json = System.Text.Json.Nodes.JsonNode.Parse(ToonDecoder.Toon2Json(source))!["rows"]!.AsArray();
        Assert.All(json, row => Assert.Equal("acme", (string?)row!["CompanyId"]));
        Assert.Equal(ToonProtocolVersion.CustomV4_4, ToonDecoder.DetectOptions(source).ProtocolVersion);
    }

    [Fact]
    public void Decode_AutoConstantHeaderWithLegacyConflictToken_DoesNotRequireHint()
    {
        const string cells = "rows[3]{Code,Value}:\n  x,.5\n  x,.5\n  x,.5";
        const string constants = "rows[3]{Code=x,Value}:\n  .5\n  .5\n  .5";
        Assert.True(ToonDecoder.DetectVersion(cells).RequiresVersionHint);

        Assert.False(ToonDecoder.DetectVersion(constants).RequiresVersionHint);
        var json = System.Text.Json.Nodes.JsonNode.Parse(ToonDecoder.Toon2Json(constants))!["rows"]!.AsArray();
        Assert.All(json, row => Assert.Equal(".5", (string?)row!["Value"]));
    }
}
