#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class RootDictionaryEncodingTests
{
    public static TheoryData<ToonProtocolVersion> AllProfiles => new()
    {
        ToonProtocolVersion.V3_0,
        ToonProtocolVersion.CustomLegacy,
        ToonProtocolVersion.V4_4,
        ToonProtocolVersion.CustomV4_4,
    };

    private static ToonEncodeOptions Encode(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    private static ToonDecodeOptions Decode(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootStringDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new Dictionary<string, string> { ["k"] = "v", ["other"] = "w" };

        Assert.Equal("k: v\nother: w", ToonEncoder.Encode(data, Encode(version)));
        Assert.Equal("k: v\nother: w", ToonEncoder.Encode((object)data, Encode(version)));
        Assert.Equal("k: v\nother: w", Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(data, Encode(version))));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootObjectDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "Ada",
            ["tags"] = new[] { "a", "b" },
            ["child"] = new Dictionary<string, object?> { ["x"] = 2 },
        };

        var toon = ToonEncoder.Encode(data, Encode(version));

        Assert.StartsWith("id: 1\nname: Ada\ntags[2]", toon);
        Assert.Contains("child:\n  x: 2", toon);
        Assert.DoesNotContain("{Key", toon);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootReadOnlyDictionary_RoundTripsThroughTypedDecode(ToonProtocolVersion version)
    {
        IReadOnlyDictionary<string, string> data = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string> { ["k"] = "v", ["other"] = "w" });

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<IReadOnlyDictionary<string, string>>(toon, Decode(version));

        Assert.Equal("k: v\nother: w", toon);
        Assert.NotNull(decoded);
        Assert.Equal(data, decoded!);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootIDictionary_RoundTripsThroughTypedDecode(ToonProtocolVersion version)
    {
        IDictionary<string, int> data = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<IDictionary<string, int>>(toon, Decode(version));
        var concrete = ToonDecoder.Decode<Dictionary<string, int>>(toon, Decode(version));

        Assert.Equal("a: 1\nb: 2", toon);
        Assert.Equal(data, decoded!);
        Assert.Equal(data, concrete!);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootListOfKeyValuePairs_StaysArray(ToonProtocolVersion version)
    {
        var data = new List<KeyValuePair<string, string>> { new("k", "v") };

        Assert.StartsWith("[1]", ToonEncoder.Encode(data, Encode(version)));
    }
}
