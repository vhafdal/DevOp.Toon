using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class V4ScalarNormalizationTests
{
    [Fact]
    public void Encode_DecimalRoot_PreservesScalarValue()
    {
        var text = ToonEncoder.Encode(12.50m, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 });
        Assert.Equal("12.5", text);
        Assert.Equal(12.5m, ToonDecoder.Decode<decimal>(text, new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
    }

    [Fact]
    public void Encode_HashCharacter_QuotesAgainstCommentSyntax()
    {
        Assert.Equal("\"#\"", ToonEncoder.Encode('#', new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
    }

    [Fact]
    public void Encode_HashUri_QuotesAgainstCommentSyntax()
    {
        Assert.Equal("\"#anchor\"", ToonEncoder.Encode(new Uri("#anchor", UriKind.Relative), new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
    }
}
