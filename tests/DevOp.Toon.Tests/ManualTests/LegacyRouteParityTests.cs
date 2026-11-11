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

    private static ToonDecodeOptions Options(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

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
