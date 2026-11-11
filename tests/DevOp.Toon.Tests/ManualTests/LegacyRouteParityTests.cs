namespace DevOp.Toon.Tests;

public sealed class LegacyRouteParityTests
{
    public static TheoryData<ToonProtocolVersion> AllVersions => new()
    {
        ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_StructDictionaryAndListMembers_MaterializeEveryValue(ToonProtocolVersion version)
    {
        var source = new PointHolder
        {
            Id = 1,
            Map = new Dictionary<string, Point> { ["a"] = new() { X = 1, Y = 2 }, ["b"] = new() { X = 3, Y = 4 } },
            List = [new() { X = 5, Y = 6 }, new() { X = 7, Y = 8 }]
        };
        var text = ToonEncoder.Encode(source, new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });

        var decoded = ToonDecoder.Decode<PointHolder>(text, Options(version))!;

        Assert.Equal(1, decoded.Id);
        Assert.Equal(source.Map, decoded.Map);
        Assert.Equal(source.List, decoded.List);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ConstructorBoundExtraSettableMembers_Bound(ToonProtocolVersion version)
    {
        var root = ToonDecoder.Decode<NotedRecord>("Id: 1\nNote: hello\nlbl: L\nTags[2]: a,b", Options(version))!;
        var list = ToonDecoder.Decode<List<NotedRecord>>("[2]:\n  - Id: 1\n    Note: a\n  - note: b\n    Id: 2", Options(version))!;
        var table = ToonDecoder.Decode<List<NotedRecord>>("[2]{Id,Note,lbl}:\n  1,a,x\n  2,b,y", Options(version))!;

        Assert.Equal(1, root.Id);
        Assert.Equal("hello", root.Note);
        Assert.Equal("L", root.Label);
        Assert.Equal(["a", "b"], root.Tags);
        Assert.Equal(["a", "b"], list.Select(r => r.Note));
        Assert.Equal([1, 2], list.Select(r => r.Id));
        Assert.Equal(["a", "b"], table.Select(r => r.Note));
        Assert.Equal(["x", "y"], table.Select(r => r.Label));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_LegacyConstructorBoundExtraMembers_BoundOnEveryRoute(ToonProtocolVersion version)
    {
        var expanded = ToonDecoder.Decode<NotedRecord>("Id: 1\nNote: hello\nz.q: 1", new ToonDecodeOptions { ProtocolVersion = version, Indent = 2, ExpandPaths = ToonPathExpansion.Safe })!;
        var continuation = ToonDecoder.Decode<List<NotedRecord>>("[1]{Id}:\n  1\n    Note: under\n    Tags[1]: t", Options(ToonProtocolVersion.CustomLegacy))!;
        var json = new ToonService().Deserialize<NotedRecord>("{\"Id\":1,\"Note\":\"j\"}", "application/json")!;

        Assert.Equal("hello", expanded.Note);
        Assert.Equal("under", continuation[0].Note);
        Assert.Equal(["t"], continuation[0].Tags);
        Assert.Equal("j", json.Note);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ConstructorBoundExtraMember_BoundNotCaptured(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<NotedAccountRecord>("Name: Cash\nNote: kept\nOther: 1", Options(version))!;

        Assert.Equal("kept", decoded.Note);
        Assert.Equal(["Other"], decoded.Annotations!.Keys);
    }

    private static ToonDecodeOptions Options(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    public sealed record NotedRecord(int Id)
    {
        public string? Note { get; set; }

        [ToonPropertyName("lbl")]
        public string? Label { get; set; }

        public List<string>? Tags { get; set; }
    }

    public sealed record NotedAccountRecord(string Name, [property: ToonExtensionData] IDictionary<string, object?>? Annotations)
    {
        public string? Note { get; set; }
    }

    public record struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class PointHolder
    {
        public int Id { get; set; }
        public Dictionary<string, Point>? Map { get; set; }
        public List<Point>? List { get; set; }
    }
}
