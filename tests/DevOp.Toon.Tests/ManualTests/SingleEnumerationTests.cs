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
