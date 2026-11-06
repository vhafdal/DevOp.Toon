using System.Text.Json;
using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class JsonDomEncodingTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void Encode_JsonElement_UsesJsonNormalization(ToonProtocolVersion version)
    {
        using var document = JsonDocument.Parse("{\"Id\":1,\"Items\":[]}");
        var options = new ToonEncodeOptions { ProtocolVersion = version, ExcludeEmptyArrays = false, IgnoreNullOrEmpty = false };
        var text = ToonEncoder.Encode(document.RootElement, options);
        var value = ToonDecoder.Decode(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = 1 });
        Assert.Equal(1L, value!["Id"]!.GetValue<long>());
        Assert.Empty(value["Items"]!.AsArray());
    }

    [Fact]
    public void Encode_JsonDocument_UsesJsonNormalization()
    {
        using var document = JsonDocument.Parse("{\"Id\":1}");
        Assert.Equal("Id: 1", ToonEncoder.Encode(document, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
    }
}
