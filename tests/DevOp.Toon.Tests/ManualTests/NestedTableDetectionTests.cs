namespace DevOp.Toon.Tests;

public sealed class NestedTableDetectionTests
{
    [Fact]
    public void Decode_AutoAfterNestedTable_RestoresKeyedPipeParent()
    {
        const string source = "[2:|]{Name|Info{Value}}:\n  a: first|1\n    Children[1]{Name}:\n      child\n  b: \"second|quoted\"|2\n    Children[1]{Name}:\n      other";
        Assert.False(ToonDecoder.DetectVersion(source).RequiresVersionHint);
        var result = ToonDecoder.Decode<Dictionary<string, Row>>(source)!;
        Assert.Equal("second|quoted", result["b"].Name);
        Assert.Equal(2, result["b"].Info.Value);
        Assert.Equal("other", Assert.Single(result["b"].Children).Name);
    }

    [Fact]
    public void Decode_AutoAfterNestedSpillTable_RestoresOuterRowContext()
    {
        const string source = "[2]{Name,Info{Value}}:\n  first,1\n    Children[1]{Name}:\n      child\n  \"second,quoted\",2\n    Children[1]{Name}:\n      other";
        var detected = ToonDecoder.DetectVersion(source);
        Assert.False(detected.RequiresVersionHint);
        var result = ToonDecoder.Decode<List<Row>>(source)!;
        Assert.Equal("second,quoted", result[1].Name);
        Assert.Equal("other", Assert.Single(result[1].Children).Name);
    }

    [Fact]
    public void EncodeDecode_CustomNestedSpillTables_DefaultAutoMatchesExplicit()
    {
        Row[] rows =
        [
            new() { Name = "first", Info = new() { Value = 1 }, Children = [new() { Name = "child" }] },
            new() { Name = "second,quoted", Info = new() { Value = 2 }, Children = [new() { Name = "other" }] }
        ];
        var text = ToonEncoder.Encode(rows, new ToonEncodeOptions
        {
            ProtocolVersion = ToonProtocolVersion.CustomV4_4,
            IgnoreNullOrEmpty = false,
            ExcludeEmptyArrays = false,
            Indent = 2
        });
        var automatic = ToonDecoder.Decode<List<Row>>(text)!;
        var explicitResult = ToonDecoder.Decode<List<Row>>(text,
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.CustomV4_4, Indent = 2 })!;
        Assert.Equal(explicitResult[1].Name, automatic[1].Name);
        Assert.Equal(explicitResult[1].Info.Value, automatic[1].Info.Value);
        Assert.Equal(Assert.Single(explicitResult[1].Children).Name, Assert.Single(automatic[1].Children).Name);
    }

    public sealed class Row
    {
        public string Name { get; set; } = "";
        public Info Info { get; set; } = new();
        public Child[] Children { get; set; } = [];
    }

    public sealed class Info
    {
        public int Value { get; set; }
    }

    public sealed class Child
    {
        public string Name { get; set; } = "";
    }
}
