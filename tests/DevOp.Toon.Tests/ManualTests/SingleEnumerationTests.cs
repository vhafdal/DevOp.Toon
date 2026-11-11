using System.Collections;
using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class SingleEnumerationTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_MixedSingleUseEnumerable_EnumeratesOnce(ToonProtocolVersion version)
    {
        var input = new SingleUseRows();
        var output = ToonEncoder.Encode(input, new ToonEncodeOptions { ProtocolVersion = version, ExcludeEmptyArrays = false, IgnoreNullOrEmpty = false });
        var decoded = ToonDecoder.Decode(output, new ToonDecodeOptions { ProtocolVersion = version, Indent = 1 });
        Assert.Equal(2, decoded!.AsArray().Count);
        Assert.Equal("tail", decoded[1]!.GetValue<string>());
        Assert.Equal(1, input.Enumerations);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V3_0)]
    public void Encode_NestedSingleUseEnumerableWithKeyFolding_EnumeratesOnce(ToonProtocolVersion version)
    {
        var values = new SingleUseValues();
        var root = new NestedSequenceHolder { Name = "a", Values = values };
        var rows = new List<NestedSequenceHolder> { new() { Name = "r", Values = new SingleUseValues() } };
        var options = new ToonEncodeOptions { ProtocolVersion = version, KeyFolding = ToonKeyFolding.Safe };

        var rootText = ToonEncoder.Encode(root, options);
        var rowsText = ToonEncoder.Encode(rows, options);

        Assert.Equal(1, values.Enumerations);
        Assert.Equal(1, rows[0].Values is SingleUseValues rowValues ? rowValues.Enumerations : -1);
        var decodeOptions = new ToonDecodeOptions { ProtocolVersion = version, Indent = 1 };
        Assert.Equal(new[] { 1, 2 }, ToonDecoder.Decode<NestedSequenceHolder>(rootText, decodeOptions)!.Values);
        Assert.Equal(new[] { 1, 2 }, ToonDecoder.Decode<List<NestedSequenceHolder>>(rowsText, decodeOptions)![0].Values);
    }

    public sealed class NestedSequenceHolder
    {
        public string Name { get; set; } = "";
        public IEnumerable<int> Values { get; set; } = Array.Empty<int>();
    }

    private sealed class SingleUseValues : IEnumerable<int>
    {
        public int Enumerations { get; private set; }
        public IEnumerator<int> GetEnumerator()
        {
            if (++Enumerations > 1) throw new InvalidOperationException("Source enumerated twice.");
            yield return 1;
            yield return 2;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleUseRows : IEnumerable<object>
    {
        public int Enumerations { get; private set; }
        public IEnumerator<object> GetEnumerator()
        {
            if (++Enumerations > 1) throw new InvalidOperationException("Source enumerated twice.");
            yield return new { Id = 1 };
            yield return "tail";
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
