using System.Text.Json;

namespace DevOp.Toon.Tests;

/// <summary>
/// Legacy typed decoding of quoted array-header keys and of values whose shape cannot fill a dictionary member. A quoted key
/// binds by its unquoted text, as on 4.x and on untyped decoding; a scalar or a list of scalars for a dictionary member fails
/// with the line instead of leaving the member null.
/// </summary>
public sealed class LegacyDirectRouteBindingTests
{
    public static TheoryData<ToonProtocolVersion, bool> LegacyCases => new()
    {
        { ToonProtocolVersion.V3_0, true },
        { ToonProtocolVersion.V3_0, false },
        { ToonProtocolVersion.CustomLegacy, true },
        { ToonProtocolVersion.CustomLegacy, false }
    };

    public static TheoryData<ToonProtocolVersion> AllVersions => new()
    {
        ToonProtocolVersion.V3_0,
        ToonProtocolVersion.CustomLegacy,
        ToonProtocolVersion.V4_4,
        ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void RoundTrip_DictionaryOfArraysWithQuotedKey_KeepsKeyText(ToonProtocolVersion version)
    {
        var source = new Dictionary<string, int[]> { ["my key"] = [1, 2], ["plain"] = [3], ["a:b"] = [4] };

        var decoded = RoundTrip(source, version);

        Assert.Equal(["my key", "plain", "a:b"], decoded.Keys);
        Assert.Equal([1, 2], decoded["my key"]);
        Assert.Equal([3], decoded["plain"]);
        Assert.Equal([4], decoded["a:b"]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void RoundTrip_ArrayMembersWithQuotedNames_BindOnEveryShape(ToonProtocolVersion version)
    {
        var source = new OddNames { Numbers = [1, 2], Items = [3], Rows = [new Leaf { A = 4 }], Plain = [5] };
        var record = new OddRecord([6, 7], [8]);

        AssertOddNames(source, RoundTrip(source, version));
        AssertOddNames(source, RoundTrip(new List<OddNames> { source, source }, version)[1]);
        AssertOddNames(source, RoundTrip(new Dictionary<string, OddNames> { ["x"] = source }, version)["x"]);
        var decodedRecord = RoundTrip(record, version);
        Assert.Equal([6, 7], decodedRecord.Numbers);
        Assert.Equal([8], decodedRecord.Items);
        Assert.Equal([6, 7], RoundTrip(new List<OddRecord> { record }, version)[0].Numbers);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ListItemWhoseFirstFieldIsQuotedArray_BindsMember(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<List<OddNames>>("[1]:\n  - \"odd-name\"[2]: 1,2\n    \"my list\"[1]: 3", DecodeOptions(version, true))!;

        Assert.Equal([1, 2], decoded[0].Numbers);
        Assert.Equal([3], decoded[0].Items);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_UntypedMemberWithQuotedArrayKey_KeepsKeyText(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<Dictionary<string, object?>>("\"my key\"[2]: 1,2\nplain: 1", DecodeOptions(version, true))!;

        Assert.Equal(["my key", "plain"], decoded.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void Decode_ScalarForDictionaryMember_ThrowsWithLine(ToonProtocolVersion version, bool strict)
    {
        var options = DecodeOptions(version, strict);

        AssertShapeError(2, "5", () => ToonDecoder.Decode<MapHolder>("Id: 1\nV: 5\nName: n", options));
        AssertShapeError(2, "abc", () => ToonDecoder.Decode<MapHolder>("Id: 1\nV: abc\nName: n", options));
        AssertShapeError(2, "\"x\"", () => ToonDecoder.Decode<MapHolder>("Id: 1\nV: \"x\"\nName: n", options));
        AssertShapeError(3, "5", () => ToonDecoder.Decode<List<MapHolder>>("[1]:\n  - Id: 1\n    V: 5\n    Name: n", options));
        AssertShapeError(2, "5", () => ToonDecoder.Decode<List<MapHolder>>("[1]:\n  - V: 5\n    Name: n", options));
        AssertShapeError(3, "5", () => ToonDecoder.Decode<MapOuter>("Inner:\n  Id: 1\n  V: 5", options));
        AssertShapeError(2, "5", () => ToonDecoder.Decode<MapRecord>("Id: 1\nV: 5\nName: n", options));
        AssertShapeError(3, "5", () => ToonDecoder.Decode<List<MapRecord>>("[1]:\n  - Id: 1\n    V: 5", options));
        AssertShapeError(2, "5", () => ToonDecoder.Decode<CtorHolder>("Id: 1\nV: 5", options));
        AssertShapeError(2, "5", () => ToonDecoder.Decode<List<CtorHolder>>("[1]:\n  - V: 5\n    Id: 1", options));
        AssertShapeError(3, "5", () => ToonDecoder.Decode<Dictionary<string, Dictionary<string, int>>>("b:\n  x: 1\na: 5", options));
        AssertShapeError(2, "5", () => ToonDecoder.Decode<List<Dictionary<string, Dictionary<string, int>>>>("[1]:\n  - a: 5", options));
        AssertShapeError(3, "5", () => ToonDecoder.Decode<Dictionary<string, Dictionary<string, Dictionary<string, int>>>>("k[1]{Key}:\n  a\n    Value: 5", options));
        AssertShapeError(3, "7", () => ToonDecoder.Decode<Dictionary<string, Dictionary<string, Dictionary<string, int>>>>("k[2]{Key,Value}:\n  a,null\n  b,7", options));
        AssertShapeError(4, "7", () => ToonDecoder.Decode<MapRecord<Dictionary<string, Dictionary<string, int>>>>("Id: 1\nV[2]{Key,Value}:\n  a,null\n  b,7", options));
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void Decode_ScalarArrayForDictionaryMember_ThrowsWithLine(ToonProtocolVersion version, bool strict)
    {
        var options = DecodeOptions(version, strict);

        AssertShapeError(2, "array", () => ToonDecoder.Decode<MapHolder>("Id: 1\nV[2]: 1,2\nName: n", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<MapHolder>("Id: 1\nV[2]:\n  - 1\n  - 2\nName: n", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<MapHolder>("Id: 1\nV[1]{a}:\n  1\nName: n", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<List<MapHolder>>("[1]:\n  - V[2]: 1,2\n    Name: n", options));
        AssertShapeError(3, "array", () => ToonDecoder.Decode<List<MapHolder>>("[1]:\n  - Id: 1\n    V[2]: 1,2", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<MapRecord>("Id: 1\nV[2]: 1,2\nName: n", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<MapRecord>("Id: 1\nV[1]{a}:\n  1\nName: n", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<CtorHolder>("Id: 1\nV[2]: 1,2", options));
        AssertShapeError(2, "array", () => ToonDecoder.Decode<List<CtorHolder>>("[1]:\n  - V[2]: 1,2\n    Id: 1", options));
        AssertShapeError(1, "array", () => ToonDecoder.Decode<Dictionary<string, Dictionary<string, int>>>("a[2]: 1,2", options));
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void Decode_DictionaryMemberShapesThatBind_StillBind(ToonProtocolVersion version, bool strict)
    {
        var options = DecodeOptions(version, strict);

        foreach (var (text, expected) in new[]
                 {
                     ("Id: 1\nV: null\nName: n", "null"),
                     ("Id: 1\nV:\nName: n", "{}"),
                     ("Id: 1\nV:\n  a: 1\nName: n", "{\"a\":1}"),
                     ("Id: 1\nV[1]{Key,Value}:\n  a,1\nName: n", "{\"a\":1}"),
                     ("Id: 1\nV[0]:\nName: n", "{}"),
                     ("Id: 1\nV[0]{X}:\nName: n", "{}"),
                     ("Id: 1\nV[1]:\n  - Key: a\n    Value: 1\nName: n", "{\"a\":1}")
                 })
        {
            var holder = ToonDecoder.Decode<MapHolder>(text, options)!;
            var record = ToonDecoder.Decode<MapRecord>(text, options)!;
            var ctorHolder = ToonDecoder.Decode<CtorHolder>(text, options)!;
            var nested = ToonDecoder.Decode<Dictionary<string, Dictionary<string, int>?>>(text.Replace("Id: 1\n", "").Replace("\nName: n", ""), options)!;

            Assert.True(expected == JsonSerializer.Serialize(holder.V), $"{text}: holder bound {JsonSerializer.Serialize(holder.V)}");
            Assert.True(expected == JsonSerializer.Serialize(record.V), $"{text}: record bound {JsonSerializer.Serialize(record.V)}");
            Assert.True(expected == JsonSerializer.Serialize(ctorHolder.V), $"{text}: constructor-backed holder bound {JsonSerializer.Serialize(ctorHolder.V)}");
            Assert.True(expected == JsonSerializer.Serialize(nested["V"]), $"{text}: nested dictionary bound {JsonSerializer.Serialize(nested["V"])}");
            Assert.Equal("n", holder.Name);
            Assert.Equal("n", record.Name);
            Assert.Equal(1, ctorHolder.Id);
        }
    }

    private static void AssertShapeError(int line, string token, Func<object?> decode)
    {
        var error = Record.Exception(decode);

        var format = Assert.IsType<ToonFormatException>(error);
        Assert.Equal(ToonErrorKind.Syntax, format.Kind);
        Assert.Equal(line, format.LineNumber);
        Assert.Contains(token, format.Message);
        Assert.Contains("System.Collections.Generic.Dictionary", format.Message);
    }

    private static void AssertOddNames(OddNames expected, OddNames actual)
    {
        Assert.Equal(expected.Numbers, actual.Numbers);
        Assert.Equal(expected.Items, actual.Items);
        Assert.Equal(expected.Plain, actual.Plain);
        Assert.Equal(expected.Rows!.Select(static r => r.A), actual.Rows!.Select(static r => r.A));
    }

    private static T RoundTrip<T>(T source, ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(source, new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });
        Assert.Contains("\"", text);

        return ToonDecoder.Decode<T>(text, DecodeOptions(version, true))!;
    }

    private static ToonDecodeOptions DecodeOptions(ToonProtocolVersion version, bool strict) =>
        new() { ProtocolVersion = version, Indent = 2, Strict = strict };

    public sealed class OddNames
    {
        [ToonPropertyName("odd-name")]
        public int[]? Numbers { get; set; }

        [ToonPropertyName("my list")]
        public List<int>? Items { get; set; }

        [ToonPropertyName("row set")]
        public List<Leaf>? Rows { get; set; }

        public int[]? Plain { get; set; }
    }

    public sealed class Leaf
    {
        public int A { get; set; }
    }

    public sealed record OddRecord(
        [property: ToonPropertyName("odd-name")] int[] Numbers,
        [property: ToonPropertyName("my list")] IReadOnlyList<int> Items);

    public sealed class MapHolder
    {
        public int Id { get; set; }

        public Dictionary<string, int>? V { get; set; }

        public string? Name { get; set; }
    }

    public sealed class MapOuter
    {
        public MapHolder? Inner { get; set; }
    }

    public sealed record MapRecord(int Id, Dictionary<string, int>? V = null, string? Name = null);

    public sealed record MapRecord<TMap>(int Id, TMap? V = null)
        where TMap : class;

    public sealed record CtorHolder(int Id)
    {
        public Dictionary<string, int>? V { get; set; }
    }
}
