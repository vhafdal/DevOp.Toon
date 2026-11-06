using System.Collections;
using System.Reflection;
using DevOp.Toon.Core;
using Xunit;

namespace DevOp.Toon.Tests;

public sealed class V4ClrListEncodingTests
{
    [Fact]
    public void Encode_ComplexDtoRows_UsesDirectListPathAndPreservesAllFields()
    {
        var rows = new[]
        {
            new ComplexRow { Id = 1, Details = new Details { Enabled = true, Visible = false }, Tags = new[] { "A", "B" } },
            new ComplexRow { Id = 2, Details = new Details { Enabled = false, Visible = true }, Tags = new[] { "C" } }
        };
        var options = Options();
        Assert.True(DirectlyHandled(rows, options));
        var text = ToonEncoder.Encode(rows, options);
        Assert.Equal("[2]:\n - Id: 1\n  Details:\n   Enabled: true\n   Visible: false\n  Tags[2]: A,B\n - Id: 2\n  Details:\n   Enabled: false\n   Visible: true\n  Tags[1]: C", text);
        var result = ToonDecoder.Decode<List<ComplexRow>>(text, DecodeOptions());
        Assert.Equal(2, result!.Count);
        Assert.Equal(new[] { "A", "B" }, result[0].Tags);
        Assert.False(result[0].Details!.Visible);
        Assert.Equal(new[] { "C" }, result[1].Tags);
    }

    [Fact]
    public void Encode_FirstFieldComplex_UsesHyphenLineAndCorrectChildDepth()
    {
        var rows = new[] { new FirstComplex { Details = new Details { Enabled = true, Visible = false }, Id = 1, Tags = new[] { "A" } } };
        Assert.True(DirectlyHandled(rows, Options()));
        Assert.Equal("[1]:\n - Details:\n   Enabled: true\n   Visible: false\n  Id: 1\n  Tags[1]: A", ToonEncoder.Encode(rows, Options()));
    }

    [Fact]
    public void Encode_FirstFieldArray_UsesNamedTabularHeaderOnHyphenLine()
    {
        var rows = new[] { new FirstArray { Children = new[] { new Child { Name = "Ada", Value = 42 } }, Tags = new[] { "A" } } };
        Assert.True(DirectlyHandled(rows, Options()));
        Assert.Equal("[1]:\n - Children[1]{Name,Value}:\n   Ada,42\n  Tags[1]: A", ToonEncoder.Encode(rows, Options()));
    }

    [Fact]
    public void Encode_OneShotProperty_EnumeratesOnlyOnce()
    {
        var sequence = new OneShot(new[] { new Child { Name = "Ada", Value = 42 } });
        var rows = new[] { new OneShotRow { Id = 1, Children = sequence } };
        var text = ToonEncoder.Encode(rows, Options());
        Assert.Equal(1, sequence.Enumerations);
        Assert.Equal("[1]:\n - Id: 1\n  Children[1]{Name,Value}:\n   Ada,42", text);
    }

    [Fact]
    public void Encode_AnonymousNestedObjectArray_UsesListRatherThanKeylessTable()
    {
        var rows = new object[] { new[] { new Child { Name = "Ada", Value = 42 } }, "tail" };
        Assert.True(DirectlyHandled(rows, Options()));
        Assert.Equal("[2]:\n - [1]:\n  - Name: Ada\n   Value: 42\n - tail", ToonEncoder.Encode(rows, Options()));
    }

    [Fact]
    public void Encode_NestedByteArrays_PreservesBase64Normalization()
    {
        var rows = new[] { new BinaryRow { Id = 1, Data = new byte[] { 1, 2, 3 }, Tags = new[] { "A" } } };
        Assert.Equal("[1]:\n - Id: 1\n  Data: AQID\n  Tags[1]: A", ToonEncoder.Encode(rows, Options()));
        Assert.Equal("[2]: AQID,BAUG", ToonEncoder.Encode(new[] { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 } }, Options()));
    }

    private static ToonEncodeOptions Options() => new() { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 1, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false };
    private static ToonDecodeOptions DecodeOptions() => new() { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 1 };
    private static bool DirectlyHandled(object value, ToonEncodeOptions options)
    {
        var resolved = typeof(ToonEncodeOptions).GetMethod("GetResolvedOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(options, null);
        var encoder = typeof(ToonEncoder).Assembly.GetType("DevOp.Toon.Internal.Encode.V4ClrEncoder")!;
        return (bool)encoder.GetMethod("TryEncode", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { value, resolved, null })!;
    }

    public sealed class Details { public bool? Enabled { get; set; } public bool? Visible { get; set; } }
    public sealed class Child { public string? Name { get; set; } public int Value { get; set; } }
    public sealed class ComplexRow { public int Id { get; set; } public Details? Details { get; set; } public string[]? Tags { get; set; } }
    public sealed class FirstComplex { public Details? Details { get; set; } public int Id { get; set; } public string[]? Tags { get; set; } }
    public sealed class FirstArray { public Child[]? Children { get; set; } public string[]? Tags { get; set; } }
    public sealed class BinaryRow { public int Id { get; set; } public byte[]? Data { get; set; } public string[]? Tags { get; set; } }
    public sealed class OneShotRow { public int Id { get; set; } public IEnumerable<Child>? Children { get; set; } }
    private sealed class OneShot(IEnumerable<Child> values) : IEnumerable<Child>
    {
        public int Enumerations { get; private set; }
        public IEnumerator<Child> GetEnumerator()
        {
            if (++Enumerations > 1) throw new InvalidOperationException("Sequence enumerated twice.");
            return values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
