using System.Collections.Generic;
using DevOp.Toon;
using Xunit;

namespace DevOp.Toon.Tests.Encode;

[Trait("Category", "root-array")]
public class RootLevelArrayManual
{
    [Fact]
    [Trait("Description", "string[] encodes to root-level inline array")]
    public void StringArray_Encodes_ToRootInlineArray()
    {
        var input = new string[] { "Item1", "Item2" };

        var result = ToonEncoder.Encode(input);

        Assert.Equal("[2]: Item1,Item2", result);
    }

    [Fact]
    [Trait("Description", "root-level string[] toon string decodes back to string[]")]
    public void StringArray_Decodes_FromRootInlineArray()
    {
        var toon = "[2]: Item1,Item2";

        var result = ToonDecoder.Decode<string[]>(toon);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Item1", "Item2" }, result);
    }

    [Fact]
    [Trait("Description", "string[] round-trips through encode then decode")]
    public void StringArray_RoundTrip_PreservesData()
    {
        var input = new string[] { "Item1", "Item2" };

        var toon = ToonEncoder.Encode(input);
        var result = ToonDecoder.Decode<string[]>(toon);

        Assert.NotNull(result);
        Assert.Equal(input, result);
    }

    [Fact]
    [Trait("Description", "root-level string[] toon decodes to List<string>")]
    public void StringArray_Decodes_ToListOfString()
    {
        var toon = "[2]: Item1,Item2";

        var result = ToonDecoder.Decode<List<string>>(toon);

        Assert.NotNull(result);
        Assert.Equal(new List<string> { "Item1", "Item2" }, result);
    }

    [Fact]
    [Trait("Description", "int[] round-trips through encode then decode")]
    public void IntArray_RoundTrip_PreservesData()
    {
        var input = new int[] { 1, 2, 3 };

        var toon = ToonEncoder.Encode(input);
        var result = ToonDecoder.Decode<int[]>(toon);

        Assert.NotNull(result);
        Assert.Equal(input, result);
    }

    [Fact]
    [Trait("Description", "empty string[] encodes and decodes correctly")]
    public void EmptyStringArray_RoundTrip_PreservesData()
    {
        var input = new string[0];

        var toon = ToonEncoder.Encode(input);
        var result = ToonDecoder.Decode<string[]>(toon);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Description", "service.Encode / service.Decode<string[]> round-trip via DI wrapper")]
    public void StringArray_RoundTrip_ViaService()
    {
        var service = new ToonService();
        var input = new string[] { "Item1", "Item2" };

        var toon = service.Encode(input);
        var result = service.Decode<string[]>(toon);

        Assert.NotNull(result);
        Assert.Equal(input, result);
    }
}
