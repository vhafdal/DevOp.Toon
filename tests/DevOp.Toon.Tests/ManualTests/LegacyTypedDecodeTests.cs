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

    private static ToonDecodeOptions Decode(ToonProtocolVersion version, ToonPathExpansion expandPaths = ToonPathExpansion.Off) => new()
    {
        ProtocolVersion = version, Indent = 2, ExpandPaths = expandPaths
    };
}
