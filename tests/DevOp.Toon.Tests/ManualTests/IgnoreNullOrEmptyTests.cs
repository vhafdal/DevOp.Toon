#nullable enable
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class IgnoreNullOrEmptyTests
{
    private static ToonEncodeOptions Opts() => new() { IgnoreNullOrEmpty = true };

    // --- CLR fast-path tests ---

    [Fact]
    public void AllNullColumn_IsOmittedFromHeaderAndRows()
    {
        var customers = new[]
        {
            new { Number = 1, Name = "Alice", Address1 = "1 Main St", Address2 = (string?)null, Country = (string?)null },
            new { Number = 2, Name = "Bob",   Address1 = "2 Oak Ave", Address2 = (string?)null, Country = (string?)null },
        };

        var result = ToonEncoder.Encode(customers, Opts());

        Assert.Contains("Number", result);
        Assert.Contains("Name", result);
        Assert.Contains("Address1", result);
        Assert.DoesNotContain("Address2", result);
        Assert.DoesNotContain("Country", result);
        // Each row should have 3 values (Number, Name, Address1), not 5
        Assert.Contains("1,Alice,1 Main St", result);
        Assert.Contains("2,Bob,2 Oak Ave", result);
    }

    [Fact]
    public void AllEmptyStringColumn_IsOmittedFromHeaderAndRows()
    {
        var customers = new[]
        {
            new { Number = 1, Name = "Alice", Address2 = "" },
            new { Number = 2, Name = "Bob",   Address2 = "" },
        };

        var result = ToonEncoder.Encode(customers, Opts());

        Assert.Contains("Number", result);
        Assert.Contains("Name", result);
        Assert.DoesNotContain("Address2", result);
    }

    [Fact]
    public void MixedSparseColumn_IsKeptWhenAtLeastOneRowHasValue()
    {
        var customers = new[]
        {
            new { Number = 1, Name = "Alice", Country = (string?)null },
            new { Number = 2, Name = "Bob",   Country = "IS" },
        };

        var result = ToonEncoder.Encode(customers, Opts());

        Assert.Contains("Country", result);
    }

    [Fact]
    public void NoSuppression_OutputIdenticalWithoutOption()
    {
        var customers = new[]
        {
            new { Number = 1, Name = "Alice", Country = "US" },
            new { Number = 2, Name = "Bob",   Country = "IS" },
        };

        var withOption    = ToonEncoder.Encode(customers, Opts());
        var withoutOption = ToonEncoder.Encode(customers);

        Assert.Equal(withoutOption, withOption);
    }

    [Fact]
    public void NullableValueTypeColumn_AllNull_IsOmitted()
    {
        var rows = new[]
        {
            new { Id = 1, Score = (int?)null },
            new { Id = 2, Score = (int?)null },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Id", result);
        Assert.DoesNotContain("Score", result);
    }

    [Fact]
    public void NullableValueTypeColumn_SomeNull_IsKept()
    {
        var rows = new[]
        {
            new { Id = 1, Score = (int?)null },
            new { Id = 2, Score = (int?)42 },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Score", result);
    }

    [Fact]
    public void AllColumnsNullOrEmpty_DoesNotCrash()
    {
        var rows = new[]
        {
            new { A = (string?)null, B = "" },
            new { A = (string?)null, B = "" },
        };

        // Should not throw; output may fall back to non-tabular or produce empty header block
        var ex = Record.Exception(() => ToonEncoder.Encode(rows, Opts()));
        Assert.Null(ex);
    }

    // --- NativeNode path (via Json2Toon) ---

    [Fact]
    public void NativeNode_AllNullColumn_IsOmitted()
    {
        var json = """[{"Number":1,"Name":"Alice","Country":null},{"Number":2,"Name":"Bob","Country":null}]""";

        var result = ToonEncoder.Json2Toon(json, Opts());

        Assert.Contains("Number", result);
        Assert.Contains("Name", result);
        Assert.DoesNotContain("Country", result);
    }

    [Fact]
    public void NativeNode_AllEmptyStringColumn_IsOmitted()
    {
        var json = """[{"Number":1,"Name":"Alice","Note":""},{"Number":2,"Name":"Bob","Note":""}]""";

        var result = ToonEncoder.Json2Toon(json, Opts());

        Assert.DoesNotContain("Note", result);
    }

    // --- Spill-row / non-header property null suppression ---

    [Fact]
    public void NullCollectionSpillProperty_IsOmittedFromTabularRows()
    {
        var rows = new[]
        {
            new { Id = 1, Name = "Alice", Tags = (string[]?)null },
            new { Id = 2, Name = "Bob",   Tags = (string[]?)null },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Id", result);
        Assert.DoesNotContain("Tags", result);
    }

    [Fact]
    public void NullScalarSpillProperty_IsOmittedFromTabularRows()
    {
        // ExtraDesc1 is a string? spill property — matches the real-world Categories/Variations case
        var rows = new[]
        {
            new { Id = 1, Name = "Alice", Extra = (string?)null },
            new { Id = 2, Name = "Bob",   Extra = (string?)null },
        };

        var result = ToonEncoder.Encode(rows, Opts());

        Assert.Contains("Id", result);
        Assert.DoesNotContain("Extra", result);
    }

    [Fact]
    public void NativeNode_NullSpillProperty_IsOmitted()
    {
        // Variations: null in a tabular spill row (matches the reported bug)
        var json = """[{"Id":1,"Name":"Alice","Variations":null},{"Id":2,"Name":"Bob","Variations":null}]""";

        var result = ToonEncoder.Json2Toon(json, Opts());

        Assert.Contains("Id", result);
        Assert.DoesNotContain("Variations", result);
    }
}
