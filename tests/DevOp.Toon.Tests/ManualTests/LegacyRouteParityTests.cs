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

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ListItemFirstFieldArrayHeader_KeepsDecimalExact(ToonProtocolVersion version)
    {
        const string text = "[2]:\n  - Tags[2]: a,b\n    Name: Cash\n    Balance: 0.1234567890123456789012345678\n  - Name: Bank\n    Balance: 1.10";
        var options = Options(version);

        var lines = ToonDecoder.Decode<List<LedgerLine>>(text, options)!;
        var records = ToonDecoder.Decode<List<LedgerLineRecord>>(text, options)!;
        var annotated = ToonDecoder.Decode<List<AnnotatedLedgerLine>>(text, options)!;

        Assert.Equal(0.1234567890123456789012345678m, lines[0].Balance);
        Assert.Equal(1.10m, lines[1].Balance);
        Assert.Equal("1.10", lines[1].Balance.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(["a", "b"], lines[0].Tags);
        Assert.Equal("Cash", lines[0].Name);
        Assert.Equal(0.1234567890123456789012345678m, records[0].Balance);
        Assert.Equal(["a", "b"], records[0].Tags);
        Assert.Equal("Bank", records[1].Name);
        Assert.Equal(0.1234567890123456789012345678m, annotated[0].Balance);
        Assert.Equal(["a", "b"], Assert.IsType<List<object?>>(annotated[0].Annotations!["Tags"]));
        Assert.Null(annotated[1].Annotations);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_ListItemFirstFieldTable_MatchesEncoder(ToonProtocolVersion version)
    {
        var source = new List<RowsLine>
        {
            new() { Rows = [new() { K = "a", V = 1 }, new() { K = "b", V = 2 }], Name = "Cash", Balance = 0.1234567890123456789012345678m },
            new() { Rows = [new() { K = "c", V = 3 }], Name = "Bank", Balance = 2m }
        };
        var text = ToonEncoder.Encode(source.Select(r => new Dictionary<string, object?> { ["Rows"] = r.Rows, ["Name"] = r.Name, ["Balance"] = r.Balance }).ToList(),
            new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });

        var decoded = ToonDecoder.Decode<List<RowsLine>>(text, Options(version))!;

        Assert.Equal(source.Select(r => r.Name), decoded.Select(r => r.Name));
        Assert.Equal(source.Select(r => r.Balance), decoded.Select(r => r.Balance));
        Assert.Equal(["a", "b"], decoded[0].Rows!.Select(r => r.K));
        Assert.Equal(3, decoded[1].Rows![0].V);
    }

    private static ToonDecodeOptions Options(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    public sealed class LedgerLine
    {
        public List<string>? Tags { get; set; }
        public string? Name { get; set; }
        public decimal Balance { get; set; }
    }

    public sealed record LedgerLineRecord(string Name, decimal Balance, List<string>? Tags);

    public sealed class AnnotatedLedgerLine
    {
        public string? Name { get; set; }
        public decimal Balance { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class KeyValueRow
    {
        public string? K { get; set; }
        public int V { get; set; }
    }

    public sealed class RowsLine
    {
        public List<KeyValueRow>? Rows { get; set; }
        public string? Name { get; set; }
        public decimal Balance { get; set; }
    }

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
