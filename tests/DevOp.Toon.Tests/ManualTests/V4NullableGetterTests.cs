namespace DevOp.Toon.Tests;

public sealed class V4NullableGetterTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Encode_ChangingNullableGetter_EvaluatesOnce(ToonProtocolVersion version)
    {
        var model = new ChangingNullable();
        var text = ToonEncoder.Encode(model, new ToonEncodeOptions
        {
            ProtocolVersion = version,
            Indent = 2,
            IgnoreNullOrEmpty = false,
            ExcludeEmptyArrays = false
        });
        Assert.Equal("Value: 42", text);
        Assert.Equal(1, model.Calls);
    }

    public sealed class ChangingNullable
    {
        internal int Calls { get; private set; }
        public int? Value => Calls++ == 0 ? 42 : null;
    }
}
