using System.Collections;
using System.Reflection;
using System.Text;

namespace DevOp.Toon.Tests;

/// <summary>
/// The legacy encoders write CLR objects through a fast path and everything else through node normalization; for the same
/// data both must produce the same text. The slow reference here is the same value as dictionaries and lists, or a lazy
/// sequence, which the fast path does not take.
/// </summary>
public sealed class LegacyEncodeParityTests
{
    public static TheoryData<ToonProtocolVersion, ToonByteArrayFormat> ByteCases => new()
    {
        { ToonProtocolVersion.V3_0, ToonByteArrayFormat.Base64String },
        { ToonProtocolVersion.V3_0, ToonByteArrayFormat.NumericArray },
        { ToonProtocolVersion.CustomLegacy, ToonByteArrayFormat.Base64String },
        { ToonProtocolVersion.CustomLegacy, ToonByteArrayFormat.NumericArray }
    };

    public static TheoryData<ToonProtocolVersion> LegacyVersions => new() { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy };

    [Theory]
    [MemberData(nameof(ByteCases))]
    public void Encode_ByteArrays_FastPathMatchesSlowPath(ToonProtocolVersion version, ToonByteArrayFormat format)
    {
        foreach (var indent in new[] { 1, 2 })
        {
            var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = indent, ByteArrayFormat = format };
            var holder = SampleBytes();

            AssertParity(holder, options);
            AssertParity(holder.Items!, options);
            AssertParity(new List<BytesHolder> { holder, SampleBytes() }, options);
            AssertParity(new Dictionary<string, BytesItem> { ["a"] = holder.Items![0] }, options);
        }
    }

    [Fact]
    public void Encode_ByteArraysInRowsBase64_WrittenAsColumns()
    {
        var holder = SampleBytes();

        var v3 = ToonEncoder.Encode(holder, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V3_0, Indent = 2 });
        var custom = ToonEncoder.Encode(holder, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.CustomLegacy, Indent = 2 });

        Assert.Contains("Items[2]{Name,Data,Bag}:\n  a,AQID,CQ==\n  b,BA==,Cg==", v3);
        Assert.Contains("Items[2]{Name,Data,Bag}:\n  a,AQID,CQ==\n  b,BA==,Cg==", custom);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_KeyFoldingBelowRoot_FastPathMatchesSlowPath(ToonProtocolVersion version)
    {
        foreach (var indent in new[] { 1, 2 })
        {
            var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = indent, KeyFolding = ToonKeyFolding.Safe };
            var deep = SampleDeep();

            AssertParity(deep, options);
            AssertParity(new List<DeepShape> { deep, SampleDeep() }, options);
            AssertParity(new List<RowWithDeep> { new() { Id = 1, Deep = SampleDeep() }, new() { Id = 2, Deep = SampleDeep() } }, options);
            AssertParity(new MapShape { Id = 1, Map = new Dictionary<string, Leaf> { ["only"] = new() { Name = "n" } } }, options);
        }
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_KeyFoldingAtDepthTwo_FoldsChain(ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(SampleDeep(), new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, KeyFolding = ToonKeyFolding.Safe });

        Assert.Equal("Id: 1\nA:\n  X: 2\n  B.Name: n", text);
    }

    private static void AssertParity(object value, ToonEncodeOptions options)
    {
        var expected = ToonEncoder.Encode(ToSlowEquivalent(value), options);
        using var stream = new MemoryStream();
        ToonEncoder.EncodeToStream(value, stream, options);

        Assert.Equal(expected, ToonEncoder.Encode(value, options));
        Assert.Equal(expected, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(value, options)));
        Assert.Equal(expected, Encoding.UTF8.GetString(stream.ToArray()));
        if (value is IList list)
        {
            Assert.Equal(expected, ToonEncoder.Encode(Lazy(list), options));
        }
    }

    private static IEnumerable<object?> Lazy(IList items)
    {
        foreach (var item in items) yield return item;
    }

    private static object? ToSlowEquivalent(object? value)
    {
        switch (value)
        {
            case null or string or int or decimal or bool or byte[] or List<byte>:
                return value;
            case IDictionary dictionary:
                var map = new Dictionary<string, object?>();
                foreach (DictionaryEntry entry in dictionary) map[(string)entry.Key] = ToSlowEquivalent(entry.Value);
                return map;
            case IEnumerable sequence:
                var list = new List<object?>();
                foreach (var item in sequence) list.Add(ToSlowEquivalent(item));
                return list;
            default:
                var result = new Dictionary<string, object?>();
                foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    result[property.Name] = ToSlowEquivalent(property.GetValue(value));
                return result;
        }
    }

    private static BytesHolder SampleBytes() => new()
    {
        Id = 1,
        Data = [1, 2, 3],
        Items = [new() { Name = "a", Data = [1, 2, 3], Bag = [9] }, new() { Name = "b", Data = [4], Bag = [10] }]
    };

    private static DeepShape SampleDeep() => new() { Id = 1, A = new DeepLevel { X = 2, B = new Leaf { Name = "n" } } };

    public sealed class BytesItem
    {
        public string? Name { get; set; }
        public byte[]? Data { get; set; }
        public List<byte>? Bag { get; set; }
    }

    public sealed class BytesHolder
    {
        public int Id { get; set; }
        public byte[]? Data { get; set; }
        public List<BytesItem>? Items { get; set; }
    }

    public sealed class Leaf
    {
        public string? Name { get; set; }
    }

    public sealed class DeepLevel
    {
        public int X { get; set; }
        public Leaf? B { get; set; }
    }

    public sealed class DeepShape
    {
        public int Id { get; set; }
        public DeepLevel? A { get; set; }
    }

    public sealed class RowWithDeep
    {
        public int Id { get; set; }
        public DeepShape? Deep { get; set; }
    }

    public sealed class MapShape
    {
        public int Id { get; set; }
        public Dictionary<string, Leaf>? Map { get; set; }
    }
}
