using System.Text.Json;

namespace DevOp.Toon.Tests;

public sealed class RecursiveMetadataTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_FiniteRecursiveTypeChain_PreservesActualValues(ToonProtocolVersion version)
    {
        var model = new RecursiveNode { Name = "root", Child = new RecursiveNode { Name = "leaf" } };
        var text = ToonEncoder.Encode(model, Options(version));
        using var json = JsonDocument.Parse(ToonDecoder.Toon2Json(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 }));
        Assert.Equal("root", json.RootElement.GetProperty("Name").GetString());
        Assert.Equal("leaf", json.RootElement.GetProperty("Child").GetProperty("Name").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Child").GetProperty("Child").ValueKind);
        if (version is ToonProtocolVersion.V3_0 or ToonProtocolVersion.CustomLegacy) return;
        var decoded = ToonDecoder.Decode<RecursiveNode>(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 })!;
        Assert.Equal("root", decoded.Name);
        Assert.Equal("leaf", decoded.Child!.Name);
        Assert.Null(decoded.Child.Child);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_FiniteMutuallyRecursiveTypes_PreservesActualValues(ToonProtocolVersion version)
    {
        var model = new MutualA { Name = "root", Child = new MutualB { Value = 42, Child = new MutualA { Name = "leaf" } } };
        var text = ToonEncoder.Encode(model, Options(version));
        using var json = JsonDocument.Parse(ToonDecoder.Toon2Json(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 }));
        Assert.Equal(42, json.RootElement.GetProperty("Child").GetProperty("Value").GetInt32());
        Assert.Equal("leaf", json.RootElement.GetProperty("Child").GetProperty("Child").GetProperty("Name").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Child").GetProperty("Child").GetProperty("Child").ValueKind);
        if (version is ToonProtocolVersion.V3_0 or ToonProtocolVersion.CustomLegacy) return;
        var decoded = ToonDecoder.Decode<MutualA>(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 })!;
        Assert.Equal(42, decoded.Child!.Value);
        Assert.Equal("leaf", decoded.Child.Child!.Name);
        Assert.Null(decoded.Child.Child.Child);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_IndexerAndPrivateGetter_EmitsOnlyPublicReadableProperties(ToonProtocolVersion version)
        => Assert.Equal("Id: 7", ToonEncoder.Encode(new AccessorModel(), Options(version)));

    [Fact]
    public void Decode_PublicSetterWithPrivateGetter_RemainsWritable()
    {
        var decoded = ToonDecoder.Decode<AccessorModel>("Id: 8\nHidden: writable",
            new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 })!;
        Assert.Equal(8, decoded.Id);
        Assert.Equal("writable", decoded.ReadHidden());
    }

    private static ToonEncodeOptions Options(ToonProtocolVersion version) => new()
    {
        ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false
    };

    public sealed class RecursiveNode
    {
        public string Name { get; set; } = "";
        public RecursiveNode? Child { get; set; }
    }

    public sealed class MutualA
    {
        public string Name { get; set; } = "";
        public MutualB? Child { get; set; }
    }

    public sealed class MutualB
    {
        public int Value { get; set; }
        public MutualA? Child { get; set; }
    }

    public sealed class AccessorModel
    {
        public int Id { get; set; } = 7;
        public string Hidden { private get; set; } = "private";
        public string this[int index] => throw new InvalidOperationException("Indexers must not be evaluated.");
        internal string ReadHidden() => Hidden;
    }
}
