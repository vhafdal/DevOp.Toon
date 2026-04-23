using System.Collections.Generic;
using System.Linq;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class ByteCollectionRoundTripTests
{
    [Fact]
    public void ByteArray_Encodes_AsBase64String_ByDefault()
    {
        byte[] input = [0, 1, 2, 255];

        var encoded = ToonEncoder.Encode(input);

        Assert.Equal("AAEC/w==", encoded);
    }

    [Fact]
    public void ByteArray_Decodes_FromPrimitiveNumericArray()
    {
        var decoded = ToonDecoder.Decode<byte[]>("[4]: 0,1,2,255");

        Assert.NotNull(decoded);
        Assert.Equal<byte>([0, 1, 2, 255], decoded!);
    }

    [Fact]
    public void ListOfByte_RoundTrips_AsBase64String_ByDefault()
    {
        List<byte> input = [5, 10, 250];

        var encoded = ToonEncoder.Encode(input);
        var decoded = ToonDecoder.Decode<List<byte>>(encoded);

        Assert.Equal("BQr6", encoded);
        Assert.NotNull(decoded);
        Assert.Equal(input, decoded!);
    }

    [Fact]
    public void EnumerableOfByte_RoundTrips_AsBase64String_ByDefault()
    {
        IEnumerable<byte> input = new byte[] { 3, 7, 11, 200 };

        var encoded = ToonEncoder.Encode(input);
        var decoded = ToonDecoder.Decode<IEnumerable<byte>>(encoded);

        Assert.Equal("AwcLyA==", encoded);
        Assert.NotNull(decoded);
        Assert.Equal(input, decoded!.ToArray());
    }

    [Fact]
    public void ByteArray_RoundTrips_AsBase64String_WithoutDecodeOptions()
    {
        byte[] input = [0, 1, 2, 255];
        var encodeOptions = new ToonEncodeOptions
        {
            ByteArrayFormat = ToonByteArrayFormat.Base64String
        };

        var encoded = ToonEncoder.Encode(input, encodeOptions);
        var decoded = ToonDecoder.Decode<byte[]>(encoded);

        Assert.Equal("AAEC/w==", encoded);
        Assert.NotNull(decoded);
        Assert.Equal(input, decoded!);
    }

    [Fact]
    public void ByteArrayProperty_RoundTrips_AsBase64String_WithoutDecodeOptions()
    {
        var input = new ByteContainer
        {
            Name = "payload",
            Data = [1, 2, 3, 250]
        };
        var encodeOptions = new ToonEncodeOptions
        {
            ByteArrayFormat = ToonByteArrayFormat.Base64String
        };

        var encoded = ToonEncoder.Encode(input, encodeOptions);
        var decoded = ToonDecoder.Decode<ByteContainer>(encoded);

        Assert.Contains("Data: AQID+g==", encoded);
        Assert.NotNull(decoded);
        Assert.Equal(input.Name, decoded!.Name);
        Assert.NotNull(decoded.Data);
        Assert.Equal(input.Data, decoded.Data);
    }

    [Fact]
    public void ByteArray_Decodes_FromBase64String_WithoutDecodeOptions()
    {
        var decoded = ToonDecoder.Decode<byte[]>("AQID+g==");

        Assert.NotNull(decoded);
        Assert.Equal<byte>([1, 2, 3, 250], decoded!);
    }

    [Fact]
    public void ListOfByte_Decodes_FromBase64String_WithoutDecodeOptions()
    {
        var decoded = ToonDecoder.Decode<List<byte>>("BQr6");

        Assert.NotNull(decoded);
        Assert.Equal<byte>([5, 10, 250], decoded!);
    }

    [Fact]
    public void EnumerableOfByte_Decodes_FromBase64String_WithoutDecodeOptions()
    {
        var decoded = ToonDecoder.Decode<IEnumerable<byte>>("AwcLyA==");

        Assert.NotNull(decoded);
        Assert.Equal<byte>([3, 7, 11, 200], decoded!.ToArray());
    }

    [Fact]
    public void ListOfByte_UsesNumericArray_WhenConfigured()
    {
        List<byte> input = [5, 10, 250];
        var encodeOptions = new ToonEncodeOptions
        {
            ByteArrayFormat = ToonByteArrayFormat.NumericArray
        };

        var encoded = ToonEncoder.Encode(input, encodeOptions);

        Assert.Equal("[3]: 5,10,250", encoded);
    }

    [Fact]
    public void EnumerableOfByte_UsesNumericArray_WhenConfigured()
    {
        IEnumerable<byte> input = new byte[] { 3, 7, 11, 200 };
        var encodeOptions = new ToonEncodeOptions
        {
            ByteArrayFormat = ToonByteArrayFormat.NumericArray
        };

        var encoded = ToonEncoder.Encode(input, encodeOptions);

        Assert.Equal("[4]: 3,7,11,200", encoded);
    }

    private sealed class ByteContainer
    {
        public string? Name { get; set; }
        public byte[]? Data { get; set; }
    }
}
