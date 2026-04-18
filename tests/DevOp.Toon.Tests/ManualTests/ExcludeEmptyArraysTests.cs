#nullable enable
using System.Collections.Generic;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class ExcludeEmptyArraysTests
{
    private static ToonEncodeOptions Opts() => new() { ExcludeEmptyArrays = true };

    // --- CLR fast-path (tabular spill / root plain object) ---

    [Fact]
    public void EmptyArrayProperty_IsOmittedFromSpillRows()
    {
        var rows = new[]
        {
            new { Id = 1, Name = "Alice", Tags = new string[0] },
            new { Id = 2, Name = "Bob",   Tags = new string[0] },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Id", result);
        Assert.Contains("Name", result);
        Assert.DoesNotContain("Tags", result);
    }

    [Fact]
    public void NonEmptyArrayProperty_IsStillIncluded()
    {
        var rows = new[]
        {
            new { Id = 1, Tags = new[] { "a", "b" } },
            new { Id = 2, Tags = new[] { "c" } },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Tags", result);
    }

    [Fact]
    public void MixedEmptyAndNonEmpty_IsStillIncluded()
    {
        var rows = new[]
        {
            new { Id = 1, Tags = new string[0] },
            new { Id = 2, Tags = new[] { "x" } },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Tags", result);
    }

    [Fact]
    public void EmptyListProperty_IsOmitted()
    {
        var rows = new[]
        {
            new { Id = 1, Items = new List<int>() },
            new { Id = 2, Items = new List<int>() },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Id", result);
        Assert.DoesNotContain("Items", result);
    }

    [Fact]
    public void RootPlainObject_EmptyArrayProperty_IsOmitted()
    {
        var obj = new { Name = "Alice", Tags = new string[0] };

        var result = ToonEncoder.Encode(obj, Opts());

        Assert.Contains("Name", result);
        Assert.DoesNotContain("Tags", result);
    }

    [Fact]
    public void WithExcludeEmptyArraysFalse_EmptyArrayIsIncluded()
    {
        var rows = new[]
        {
            new { Id = 1, Tags = new string[0] },
        };

        var withOption    = ToonEncoder.Encode(rows, Opts());
        var withoutOption = ToonEncoder.Encode(rows, new ToonEncodeOptions { ExcludeEmptyArrays = false });

        Assert.DoesNotContain("Tags", withOption);
        Assert.Contains("Tags", withoutOption);
    }

    // --- NativeNode path (via Json2Toon) ---

    [Fact]
    public void NativeNode_EmptyArrayColumn_IsOmitted()
    {
        var json = """[{"Id":1,"Name":"Alice","Tags":[]},{"Id":2,"Name":"Bob","Tags":[]}]""";

        var result = ToonEncoder.Json2Toon(json, Opts());

        Assert.Contains("Id", result);
        Assert.Contains("Name", result);
        Assert.DoesNotContain("Tags", result);
    }

    [Fact]
    public void NativeNode_EmptyArrayProperty_InSingleObject_IsOmitted()
    {
        var json = """{"Name":"Alice","Tags":[]}""";

        var result = ToonEncoder.Json2Toon(json, Opts());

        Assert.Contains("Name", result);
        Assert.DoesNotContain("Tags", result);
    }

    [Fact]
    public void NativeNode_NonEmptyArray_IsIncluded()
    {
        var json = """[{"Id":1,"Tags":["a","b"]},{"Id":2,"Tags":["c"]}]""";

        var result = ToonEncoder.Json2Toon(json, Opts());

        Assert.Contains("Tags", result);
    }
}
