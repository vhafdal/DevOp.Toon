using System.Reflection;
using DevOp.Toon.Core;
using Xunit;

namespace DevOp.Toon.Tests;

public sealed class V4PropertyKeyPlanTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.V4_0)]
    [InlineData(ToonProtocolVersion.V4_1)]
    [InlineData(ToonProtocolVersion.V4_2)]
    [InlineData(ToonProtocolVersion.V4_3)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_CachedQuotedAndPlainKeys_MatchesNativeEncoding(ToonProtocolVersion version)
    {
        var options = Options(version);
        var model = new AliasRow { Plain = 1, Quoted = 2, Unicode = 3, Tags = ["x", "y"] };
        var native = new Dictionary<string, object?>
        {
            ["plain.path"] = 1, ["quote\"\\\n"] = 2, ["emoji😀"] = 3, ["tag,list"] = new[] { "x", "y" }
        };
        Assert.Equal(ToonEncoder.Encode(native, options), ToonEncoder.Encode(model, options));
        Assert.Equal(ToonEncoder.Encode(new[] { native, native }, options), ToonEncoder.Encode(new[] { model, model }, options));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_MetadataNormalizedAlias_RespectsEmptyArrayOmission(ToonProtocolVersion version)
    {
        var options = Options(version);
        options.ExcludeEmptyArrays = true;
        var row = new InvalidArrayAlias();
        var alias = MetadataAlias(typeof(InvalidArrayAlias), nameof(InvalidArrayAlias.Values));
        Assert.Equal("Id: 1", ToonEncoder.Encode(row, options));
        row.Values = [1];
        Assert.Equal(ToonEncoder.Encode(new Dictionary<string, object?> { ["Id"] = 1, [alias] = new[] { 1 } }, options), ToonEncoder.Encode(row, options));
    }

    [Fact]
    public void Encode_MetadataNormalizedAlias_RespectsNullColumnSuppression()
    {
        var options = Options(ToonProtocolVersion.CustomV4_4);
        options.IgnoreNullOrEmpty = true;
        var row = new InvalidScalarAlias();
        var alias = MetadataAlias(typeof(InvalidScalarAlias), nameof(InvalidScalarAlias.Value));
        Assert.Equal("[1]{Id}:\n  1", ToonEncoder.Encode(new[] { row }, options));
        row.Value = "present";
        var native = new Dictionary<string, object?> { ["Id"] = 1, [alias] = "present" };
        Assert.Equal(ToonEncoder.Encode(new[] { native }, options), ToonEncoder.Encode(new[] { row }, options));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_RuntimeInvalidKey_ThrowsOnlyWhenEmitted(ToonProtocolVersion version)
    {
        var options = Options(version);
        options.ExcludeEmptyArrays = true;
        var row = new Dictionary<string, object?> { ["Id"] = 1, ["\ud800"] = Array.Empty<int>() };
        Assert.Equal("Id: 1", ToonEncoder.Encode(row, options));
        row["\ud800"] = new[] { 1 };
        Assert.Throws<ArgumentException>(() => ToonEncoder.Encode(row, options));
    }

    private static string MetadataAlias(Type type, string propertyName)
    {
        var alias = type.GetProperty(propertyName)!.GetCustomAttribute<ToonPropertyNameAttribute>()!.Name;
        // Custom-attribute metadata normalizes the source's unpaired surrogate before reflection.
        Assert.NotEqual("\ud800", alias);
        Assert.All(alias, character => Assert.False(char.IsSurrogate(character)));
        return alias;
    }

    private static ToonEncodeOptions Options(ToonProtocolVersion version) => new()
    {
        ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false
    };

    public sealed class AliasRow
    {
        [ToonPropertyName("plain.path")] public int Plain { get; set; }
        [ToonPropertyName("quote\"\\\n")] public int Quoted { get; set; }
        [ToonPropertyName("emoji😀")] public int Unicode { get; set; }
        [ToonPropertyName("tag,list")] public string[] Tags { get; set; } = [];
    }

    public sealed class InvalidArrayAlias
    {
        public int Id { get; set; } = 1;
        [ToonPropertyName("\ud800")] public int[] Values { get; set; } = [];
    }

    public sealed class InvalidScalarAlias
    {
        public int Id { get; set; } = 1;
        [ToonPropertyName("\ud800")] public string? Value { get; set; }
    }
}
