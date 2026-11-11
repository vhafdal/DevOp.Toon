namespace DevOp.Toon.Tests;

public sealed class LegacyTypedDecodeTests
{
    public static TheoryData<ToonProtocolVersion> LegacyVersions => new() { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy };

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_UntypedDictionaryWithNestedObject_ReadsEveryKey(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<Dictionary<string, object?>>("z: 0\nh:\n  x: 1\n  y: a\nt[2]: a,b", Decode(version))!;

        Assert.Equal(["z", "h", "t"], decoded.Keys);
        Assert.Equal(0d, Convert.ToDouble(decoded["z"]));
        var nested = Assert.IsType<Dictionary<string, object?>>(decoded["h"]);
        Assert.Equal(["x", "y"], nested.Keys);
        Assert.Equal("a", nested["y"]);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0, ToonPathExpansion.Off)]
    [InlineData(ToonProtocolVersion.V3_0, ToonPathExpansion.Safe)]
    [InlineData(ToonProtocolVersion.CustomLegacy, ToonPathExpansion.Off)]
    [InlineData(ToonProtocolVersion.CustomLegacy, ToonPathExpansion.Safe)]
    public void Decode_StructDto_BindsMembers(ToonProtocolVersion version, ToonPathExpansion expandPaths)
    {
        var decoded = ToonDecoder.Decode<NameStruct>("Name: x\nCount: 3", Decode(version, expandPaths));

        Assert.Equal("x", decoded.Name);
        Assert.Equal(3, decoded.Count);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_ExpandPathsIntegralNumbers_BindIntAndLongMembers(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<Counters>("Small: 5\nLarge: 2000000\nWide: 3000000000\nNear: 999999999999999\nUnit.Name: u", Decode(version, ToonPathExpansion.Safe))!;

        Assert.Equal(5, decoded.Small);
        Assert.Equal(2000000, decoded.Large);
        Assert.Equal(3000000000L, decoded.Wide);
        Assert.Equal(999999999999999L, decoded.Near);
        Assert.Equal("u", decoded.Unit!.Name);
    }

    [Theory]
    [InlineData("Small: 1.5")]
    [InlineData("Large: 3000000000")]
    [InlineData("Wide: 1000000000000001")]
    [InlineData("Wide: 9007199254740994")]
    public void Decode_ExpandPathsNumberOutsideIntegralRange_Throws(string text)
    {
        Assert.Throws<NotSupportedException>(() =>
            ToonDecoder.Decode<Counters>(text + "\nUnit.Name: u", Decode(ToonProtocolVersion.V3_0, ToonPathExpansion.Safe)));
    }

    private static ToonDecodeOptions Decode(ToonProtocolVersion version, ToonPathExpansion expandPaths = ToonPathExpansion.Off) => new()
    {
        ProtocolVersion = version, Indent = 2, ExpandPaths = expandPaths
    };

    public struct NameStruct
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    public sealed class Unit
    {
        public string? Name { get; set; }
    }

    public sealed class Counters
    {
        public int Small { get; set; }
        public int Large { get; set; }
        public long Wide { get; set; }
        public long Near { get; set; }
        public Unit? Unit { get; set; }
    }
}
