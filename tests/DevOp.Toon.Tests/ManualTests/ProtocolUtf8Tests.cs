using System.Text;
using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class ProtocolUtf8Tests
{
    [Theory]
    [InlineData(ToonProtocolVersion.V4_0, false)]
    [InlineData(ToonProtocolVersion.V4_4, true)]
    [InlineData(ToonProtocolVersion.V4_4, false)]
    public async Task DecodeAsync_InvalidUtf8WithoutRecovery_Throws(ToonProtocolVersion version, bool strict)
    {
        using var stream = Payload();
        await Assert.ThrowsAsync<DecoderFallbackException>(() => ToonDecoder.DecodeAsync(stream, new ToonDecodeOptions { ProtocolVersion = version, Strict = strict }));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_2)]
    [InlineData(ToonProtocolVersion.V4_3)]
    public async Task DecodeAsync_InvalidUtf8WithSpecifiedRecovery_ReplacesSequence(ToonProtocolVersion version)
    {
        using var stream = Payload();
        var value = await ToonDecoder.DecodeAsync(stream, new ToonDecodeOptions { ProtocolVersion = version, Strict = false });
        Assert.Equal("\ufffd", value!["value"]!.GetValue<string>());
    }

    private static MemoryStream Payload() => new([.. Encoding.UTF8.GetBytes("value: "), 0xff]);
}
