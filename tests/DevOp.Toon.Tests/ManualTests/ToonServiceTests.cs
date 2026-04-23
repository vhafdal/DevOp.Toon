using Microsoft.Extensions.DependencyInjection;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class ToonServiceTests
{
    [Fact]
    public void AddToon_UsesCompactColumnarEncodeDefaults()
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

        Assert.StartsWith("[2]{id,name}:", encoded);
        Assert.DoesNotContain("note", encoded);

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

    private sealed class SimpleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
