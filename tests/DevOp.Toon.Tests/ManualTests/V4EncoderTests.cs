using System.Collections.Generic;
using DevOp.Toon.Core;
using Xunit;

namespace DevOp.Toon.Tests;

public sealed class V4EncoderTests
{
    [Fact]
    public void Encode_UniformNestedObjects_UsesNestedHeaderGroups()
    {
        var rows = new[]
        {
            new { Id = 1, Customer = new { Name = "Ada", Country = "IS" } },
            new { Id = 2, Customer = new { Name = "Bob", Country = "GB" } }
        };
        Assert.Equal("[2]{Id,Customer{Name,Country}}:\n 1,Ada,IS\n 2,Bob,GB", ToonEncoder.Encode(rows, Official()));
    }

    [Fact]
    public void Encode_KeyedUniformObjects_UsesKeyedHeader()
    {
        var data = new Dictionary<string, object>
        {
            ["alice"] = new { Age = 30, City = "Reykjavik" },
            ["bob"] = new { Age = 25, City = "London" }
        };
        Assert.Equal("[2:]{Age,City}:\n alice: 30,Reykjavik\n bob: 25,London", ToonEncoder.Encode(data, Official()));
    }

    [Fact]
    public void Encode_ArrayColumn_OfficialFallsBackAndCustomRetainsGroups()
    {
        var rows = new[]
        {
            new { Id = 1, Details = new { Active = true }, Tags = new[] { "A" } },
            new { Id = 2, Details = new { Active = false }, Tags = new[] { "B" } }
        };
        var official = ToonEncoder.Encode(rows, Official());
        Assert.StartsWith("[2]:\n - Id: 1", official);
        var custom = ToonEncoder.Encode(rows, Custom());
        Assert.Equal("[2]{Id,Details{Active}}:\n 1,true\n  Tags[1]: A\n 2,false\n  Tags[1]: B", custom);
    }

    [Fact]
    public void Encode_KeyedHybridRows_PreservesSpills()
    {
        var data = new Dictionary<string, object>
        {
            ["a"] = new { Id = 1, Tags = new[] { "A" } },
            ["b"] = new { Id = 2, Tags = new[] { "B" } }
        };
        Assert.Equal("[2:]{Id}:\n a: 1\n  Tags[1]: A\n b: 2\n  Tags[1]: B", ToonEncoder.Encode(data, Custom()));
    }

    [Fact]
    public void Encode_EmptyArrays_UsesPositionSpecificForms()
    {
        Assert.Equal("[]", ToonEncoder.Encode(new int[0], Official()));
        Assert.Equal("Items: []", ToonEncoder.Encode(new { Items = new int[0] }, Official()));
        Assert.Equal("[1]:\n - [0]:", ToonEncoder.Encode(new[] { new int[0] }, Official()));
    }

    [Theory]
    [InlineData("#tag", "\"#tag\"")]
    [InlineData("+1", "\"+1\"")]
    [InlineData("\ufefftext", "\"\ufefftext\"")]
    [InlineData("emoji 😀", "emoji 😀")]
    [InlineData("\u00a0text", "\u00a0text")]
    public void Encode_Strings_UsesV4Quoting(string value, string expected)
    {
        Assert.Equal(expected, ToonEncoder.Encode(value, Official()));
    }

    [Fact]
    public void Encode_UnpairedSurrogate_Throws()
    {
        Assert.ThrowsAny<System.ArgumentException>(() => ToonEncoder.Encode("\ud800", Official()));
    }

    [Theory]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1000000d, "1000000")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(-0d, "0")]
    public void Encode_NumericBoundaries_UsesCanonicalRepresentation(double value, string expected)
    {
        Assert.Equal(expected, ToonEncoder.Encode(value, Official()));
    }

    [Fact]
    public void Encode_HybridMissingAndNullFields_UsesSpillsWithoutCoercion()
    {
        var rows = new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Optional"] = null },
            new Dictionary<string, object?> { ["Id"] = 2, ["Other"] = "value" }
        };
        Assert.Equal("[2]{Id}:\n 1\n  Optional: null\n 2\n  Other: value", ToonEncoder.Encode(rows, Custom()));
    }

    [Fact]
    public void Encode_UniformObjectsWithDifferentKeyOrder_UsesFirstRowOrder()
    {
        var rows = new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Ada" },
            new Dictionary<string, object?> { ["Name"] = "Bob", ["Id"] = 2 }
        };
        Assert.Equal("[2]{Id,Name}:\n 1,Ada\n 2,Bob", ToonEncoder.Encode(rows, Official()));
    }

    [Fact]
    public void Encode_NestedTableAsListFirstField_UsesCorrectContentDepth()
    {
        var rows = new object[]
        {
            new { Users = new[] { new { Id = 1, Name = "Ada" } }, Status = "active" },
            "tail"
        };
        Assert.Equal("[2]:\n - Users[1]{Id,Name}:\n   1,Ada\n  Status: active\n - tail", ToonEncoder.Encode(rows, Official()));
    }

    private static ToonEncodeOptions Official() => new() { ProtocolVersion = ToonProtocolVersion.V4_4 };
    private static ToonEncodeOptions Custom() => new() { ProtocolVersion = ToonProtocolVersion.CustomV4_4, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false };
}
