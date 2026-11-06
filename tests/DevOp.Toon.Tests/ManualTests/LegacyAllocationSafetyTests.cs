using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class LegacyAllocationSafetyTests
{
    [Theory]
    [InlineData("[1000000000]:")]
    [InlineData("[1000000000]: 1")]
    public void Decode_UntrustedDeclaredLength_DoesNotReserveDeclaredCapacity(string source)
    {
        var options = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.CustomLegacy };
        ToonDecoder.Decode<List<int>>("[0]:", options);
#if NET6_0_OR_GREATER
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<int>>(source, options));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1024 * 1024);
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(source, options));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1024 * 1024);
#else
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<int>>(source, options));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode(source, options));
#endif
    }
}
