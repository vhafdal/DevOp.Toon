using Microsoft.Extensions.DependencyInjection;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class ToonServiceTests
{
    [Fact]
    public void AddToon_UsesOfficialPreservingEncodeDefaults()
    {
        var services = new ServiceCollection();
        services.AddToon();

        var provider = services.BuildServiceProvider();
        var toon = provider.GetRequiredService<IToonService>();

        var encoded = toon.Encode(new[]
        {
            new { id = 1, name = "Alice", note = "" },
            new { id = 2, name = "Bob", note = "" }
        });

        Assert.StartsWith("[2]{id,name,note}:", encoded);
        Assert.Contains("note", encoded);

        var decoded = toon.Decode(encoded);

        Assert.Equal("Alice", decoded?[0]?["name"]?.GetValue<string>());
    }

    [Fact]
    public void AddToon_RegistersSingletonServiceWithConfiguredOptions()
    {
        var services = new ServiceCollection();

        services.AddToon(options =>
        {
            options.KeyFolding = ToonKeyFolding.Off;
            options.Delimiter = ToonDelimiter.TAB;
            options.Indent = 2;
            options.Strict = false;
        });

        var provider = services.BuildServiceProvider();
        var toon = provider.GetRequiredService<IToonService>();

        var encoded = toon.Encode(new { user = new { name = "Alice" } });
        var decoded = toon.Decode<dynamic>("numbers[5]: 1, 2, 3");

        Assert.Contains("user:", encoded);
        Assert.NotNull(decoded);
    }

    [Fact]
    public void AddToon_WithInvalidIndent_ThrowsArgumentOutOfRangeException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => services.AddToon(options => options.Indent = 0));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void ToonService_UsesInjectedDecodeDefaults()
    {
        var services = new ServiceCollection();
        services.AddToon(options =>
        {
            options.Indent = 2;
            options.Decode.ProtocolVersion = ToonProtocolVersion.CustomLegacy;
            options.ExpandPaths = ToonPathExpansion.Safe;
        });

        var provider = services.BuildServiceProvider();
        var toon = provider.GetRequiredService<IToonService>();

        var decoded = toon.Decode("company.name: Acme");

        Assert.Equal("Acme", decoded?["company"]?["name"]?.GetValue<string>());
    }

    // --- Deserialize<T> tests ---

    [Theory]
    [InlineData("application/toon")]
    [InlineData("text/toon")]
    [InlineData("application/vnd.myapp+toon")]
    public void Deserialize_DecodesFromToonContentType(string contentType)
    {
        var toon = new ToonService();
        var payload = toon.Encode(new SimpleDto { Id = 7, Name = "Widget" });

        var result = toon.Deserialize<SimpleDto>(payload, contentType);

        Assert.NotNull(result);
        Assert.Equal(7, result!.Id);
        Assert.Equal("Widget", result.Name);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/json")]
    [InlineData("application/vnd.myapp+json")]
    public void Deserialize_DecodesFromJsonContentType(string contentType)
    {
        var toon = new ToonService();
        const string json = "{\"Id\":42,\"Name\":\"Gadget\"}";

        var result = toon.Deserialize<SimpleDto>(json, contentType);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("Gadget", result.Name);
    }

    [Fact]
    public void Deserialize_ThrowsNotSupportedException_ForUnsupportedContentType()
    {
        var toon = new ToonService();

        Assert.Throws<NotSupportedException>(() =>
            toon.Deserialize<SimpleDto>("hello", "text/plain"));
    }

    [Fact]
    public void Deserialize_WithDecodeOptions_DecodesFromToonContentType()
    {
        var toon = new ToonService();
        var payload = toon.Encode(new SimpleDto { Id = 3, Name = "Sprocket" });
        var decodeOptions = new ToonDecodeOptions { Strict = false };

        var result = toon.Deserialize<SimpleDto>(payload, "application/toon", decodeOptions);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Id);
        Assert.Equal("Sprocket", result.Name);
    }

    [Fact]
    public void Deserialize_WithDecodeOptions_DecodesFromJsonContentType()
    {
        var toon = new ToonService();
        const string json = "{\"Id\":99,\"Name\":\"Cog\"}";
        var decodeOptions = new ToonDecodeOptions { Strict = false };

        var result = toon.Deserialize<SimpleDto>(json, "application/json", decodeOptions);

        Assert.NotNull(result);
        Assert.Equal(99, result!.Id);
        Assert.Equal("Cog", result.Name);
    }

    [Fact]
    public void Deserialize_WithDecodeOptions_ThrowsNotSupportedException_ForUnsupportedContentType()
    {
        var toon = new ToonService();

        Assert.Throws<NotSupportedException>(() =>
            toon.Deserialize<SimpleDto>("hello", "text/plain", new ToonDecodeOptions()));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Decode_PinnedDecodeVersionWithDefaultIndents_DecodesOwnOutput(ToonProtocolVersion version)
    {
        var options = new ToonServiceOptions { ProtocolVersion = version };
        options.Decode.ProtocolVersion = version;
        var service = new ToonService(options);
        var source = new NestedDto { Id = 7, Child = new SimpleDto { Id = 8, Name = "Gear" }, Tags = ["a", "b"] };

        var text = service.Encode(source);
        var decoded = service.Decode<NestedDto>(text)!;

        Assert.Contains("\n Id: 8", text, StringComparison.Ordinal);
        Assert.Equal(1, options.Encode.Indent);
        Assert.Equal(2, options.Decode.Indent);
        Assert.Equal(8, decoded.Child!.Id);
        Assert.Equal("Gear", decoded.Child.Name);
        Assert.Equal(["a", "b"], decoded.Tags);
        Assert.Equal(8, service.Decode(text)!["Child"]!["Id"]!.GetValue<int>());
        Assert.Equal("Gear", service.Deserialize<NestedDto>(text, "application/toon")!.Child!.Name);
        Assert.Contains("\"Name\":\"Gear\"", service.Toon2Json(text), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public async Task Decode_PinnedDecodeVersionWithDefaultIndents_StillDecodesDecodeIndentInput(ToonProtocolVersion version)
    {
        var options = new ToonServiceOptions { ProtocolVersion = version };
        options.Decode.ProtocolVersion = version;
        var service = new ToonService(options);
        var text = ToonEncoder.Encode(
            new NestedDto { Id = 7, Child = new SimpleDto { Id = 8, Name = "Gear" } },
            new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });

        Assert.Equal("Gear", service.Decode<NestedDto>(text)!.Child!.Name);
        Assert.Equal("Gear", (await service.DecodeAsync<NestedDto>(text))!.Child!.Name);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void Decode_ExplicitDecodeIndent_IsHonoured(ToonProtocolVersion version)
    {
        var options = new ToonServiceOptions { ProtocolVersion = version };
        options.Decode.ProtocolVersion = version;
        options.Decode.Indent = 2;
        var service = new ToonService(options);
        var text = service.Encode(new NestedDto { Id = 7, Child = new SimpleDto { Id = 8, Name = "Gear" } });

        Assert.Throws<ToonFormatException>(() => service.Decode<NestedDto>(text));
    }

    private sealed class NestedDto
    {
        public int Id { get; set; }
        public SimpleDto? Child { get; set; }
        public List<string>? Tags { get; set; }
    }

    private sealed class SimpleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
