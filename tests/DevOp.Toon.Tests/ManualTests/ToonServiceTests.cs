using Microsoft.Extensions.DependencyInjection;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class ToonServiceTests
{
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
}
