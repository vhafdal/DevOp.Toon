namespace DevOp.Toon.Tests;

public sealed class ScalarSetterBoundaryTests
{
    [Fact]
    public void Decode_ScalarProperties_PreservesNullableNullAndDateValues()
    {
        const string source = "[2]{Value,Maybe,Enabled,When}:\n  42,7,true,2026-11-06T12:00:00.0000000Z\n  1,null,false,2026-11-07T12:00:00.0000000Z";
        var result = ToonDecoder.Decode<List<ScalarRow>>(source, Options())!;
        Assert.Equal(42, result[0].Value);
        Assert.Equal(7, result[0].Maybe);
        Assert.True(result[0].Enabled);
        Assert.Equal(DateTimeKind.Utc, result[0].When.Kind);
        Assert.Equal(6, result[0].When.Day);
        Assert.Null(result[1].Maybe);
        Assert.False(result[1].Enabled);
        Assert.Equal(7, result[1].When.Day);
    }

    [Theory]
    [InlineData("bad", typeof(FormatException))]
    [InlineData("999999999999999999999999", typeof(OverflowException))]
    public void Decode_ParserFailure_DoesNotCallUserSetter(string token, Type exceptionType)
    {
        CountingSetter.Calls = 0;
        Assert.Throws(exceptionType, () => ToonDecoder.Decode<CountingSetter>("Value: " + token, Options()));
        Assert.Equal(0, CountingSetter.Calls);
    }

    [Fact]
    public void Decode_UserSetterFormatException_PropagatesExactlyOnce()
    {
        FormatSetter.Calls = 0;
        var error = Assert.Throws<FormatException>(() => ToonDecoder.Decode<FormatSetter>("Value: 1", Options()));
        Assert.Equal("user setter", error.Message);
        Assert.Equal(1, FormatSetter.Calls);
    }

    [Fact]
    public void Decode_UserSetterOverflowException_PropagatesExactlyOnce()
    {
        OverflowSetter.Calls = 0;
        var error = Assert.Throws<OverflowException>(() => ToonDecoder.Decode<OverflowSetter>("Value: 1", Options()));
        Assert.Equal("user setter", error.Message);
        Assert.Equal(1, OverflowSetter.Calls);
    }

    private static ToonDecodeOptions Options() => new() { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 };

    public sealed class ScalarRow
    {
        public int Value { get; set; }
        public int? Maybe { get; set; }
        public bool Enabled { get; set; }
        public DateTime When { get; set; }
    }

    public sealed class CountingSetter
    {
        internal static int Calls;
        public int Value { set => Calls++; }
    }

    public sealed class FormatSetter
    {
        internal static int Calls;
        public int Value { set { Calls++; throw new FormatException("user setter"); } }
    }

    public sealed class OverflowSetter
    {
        internal static int Calls;
        public int Value { set { Calls++; throw new OverflowException("user setter"); } }
    }
}
