using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class ProtocolJsonInteropTests
{
    [Fact]
    public void Deserialize_JsonWithDifferentEncodeAndDecodeVersions_PreservesInput()
    {
        var options = new ToonServiceOptions();
        options.Encode.ProtocolVersion = ToonProtocolVersion.CustomV4_4;
        options.Decode.ProtocolVersion = ToonProtocolVersion.CustomLegacy;
        var service = new ToonService(options);
        var value = service.Deserialize<JsonPayload>("{\"Items\":[],\"Name\":\"\"}", "application/json");
        Assert.NotNull(value);
        Assert.Empty(value!.Items!);
        Assert.Equal("", value.Name);
    }

    private sealed class JsonPayload
    {
        public int[]? Items { get; set; }
        public string? Name { get; set; }
    }
}
