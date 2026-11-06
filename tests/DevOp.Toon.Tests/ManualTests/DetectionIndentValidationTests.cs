namespace DevOp.Toon.Tests;

[Collection("DefaultOptions")]
public sealed class DetectionIndentValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Decode_AutoRootWithInvalidIndent_ThrowsArgumentOutOfRange(int indent)
    {
        var options = new ToonDecodeOptions { Indent = indent };
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonDecoder.Decode("1", options));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToonDecoder.Decode<int>("1", options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DetectOptions_InvalidFallbackIndent_ThrowsArgumentOutOfRange(int indent)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ToonDecoder.DetectOptions("Root:\n  Value: 1", new ToonDecodeOptions { Indent = indent }));

    [Fact]
    public void DetectVersion_InvalidDefaultIndent_ThrowsAndRestoresDefaultOptions()
    {
        var original = ToonDecoder.DefaultOptions;
        try
        {
            ToonDecoder.DefaultOptions = new ToonDecodeOptions { Indent = 0 };
            Assert.Throws<ArgumentOutOfRangeException>(() => ToonDecoder.DetectVersion("1"));
        }
        finally { ToonDecoder.DefaultOptions = original; }
    }
}
