namespace DevOp.Toon.Tests;

public sealed class TypedNumericBindingTests
{
    public static TheoryData<string, string> OutOfRangeCases => Cases(
        ("byte", "256"), ("byte", "-1"), ("byte?", "256"), ("sbyte", "128"), ("sbyte", "-129"), ("sbyte?", "-129"),
        ("short", "32768"), ("short", "-32769"), ("short?", "32768"), ("ushort", "65536"), ("ushort", "-1"), ("ushort?", "-1"),
        ("int", "2147483648"), ("int", "-2147483649"), ("int?", "2147483648"), ("uint", "4294967296"), ("uint", "-1"), ("uint?", "-1"),
        ("long", "9223372036854775808"), ("long", "-9223372036854775809"), ("long?", "9223372036854775808"),
        ("ulong", "18446744073709551616"), ("ulong", "-1"), ("ulong?", "-1"),
        ("decimal", "79228162514264337593543950336"), ("decimal", "1e29"), ("decimal?", "-1e29"),
        ("ByteEnum", "256"), ("ByteEnum", "-1"), ("ByteEnum?", "256"), ("IntEnum", "2147483648"), ("LongEnum", "9223372036854775808"),
        ("int", "1.5"), ("uint", "2.5"), ("long", "0.5"), ("ulong", "1e-29"), ("IntEnum", "1.5"));

    [Theory]
    [MemberData(nameof(OutOfRangeCases))]
    public void Decode_NumberOutsideMember_ThrowsFormatExceptionWithTokenLineOnEveryShape(string type, string token)
        => AssertErrorOnEveryShape(type, token, Strict());

    public static TheoryData<string, string> InvalidTokenCases => Cases(
        ("int", "abc"), ("int", "\"abc\""), ("int", "\"\""), ("int?", "abc"), ("long", "\"1.5\""), ("byte", "\"300\""),
        ("double", "abc"), ("double", "\"\""), ("float", "\"x\""), ("decimal", "abc"), ("decimal", "\"\""),
        ("IntEnum", "abc"), ("IntEnum", "\"abc\""), ("IntEnum", "\"\""), ("ByteEnum", "D"), ("ByteEnum?", "\"300\""));

    [Theory]
    [MemberData(nameof(InvalidTokenCases))]
    public void Decode_TokenNotValidForNumericMember_ThrowsFormatExceptionWithTokenLineOnEveryShape(string type, string token)
        => AssertErrorOnEveryShape(type, token, Strict());

    [Theory]
    [InlineData("A: 1\nV: 300\nB: 2", 2)]
    [InlineData("A: 1\nB: 2\nV: abc", 3)]
    [InlineData("V: 1.5e400x\nA: 1", 1)]
    public void Decode_BadNumberOnLaterLine_ReportsThatLine(string source, int line)
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Middle>(source, Strict()));

        Assert.Equal(ToonErrorKind.Syntax, error.Kind);
        Assert.Equal(line, error.LineNumber);
    }

    [Fact]
    public void Decode_BadNumberInMiddleRow_ReportsRowLine()
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<Middle>>("[3]{A,V,B}:\n  1,1,1\n  1,300,1\n  1,1,1", Strict()));

        Assert.Equal(3, error.LineNumber);
        Assert.Contains("300", error.Message);
        Assert.Contains("System.Byte", error.Message);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_0)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public void Decode_BindingError_DoesNotDependOnStrict(ToonProtocolVersion version)
    {
        foreach (var strict in new[] { true, false })
        {
            var options = new ToonDecodeOptions { ProtocolVersion = version, Indent = 2, Strict = strict };
            var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Middle>("A: 1\nB: 99999999999", options));
            Assert.Equal(2, error.LineNumber);
        }
    }

    [Fact]
    public void Decode_StrictDuplicateKey_ReportedBeforeBadNumber()
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Middle>("A: 1\nA: 99999999999", Strict()));

        Assert.Contains("Duplicate object key", error.Message);
    }

    [Fact]
    public void Decode_AliasedMember_ReportsBindingErrorAsPropertyNameDoes()
    {
        Assert.Equal(5, ToonDecoder.Decode<Aliased>("qty: 5", Strict())!.Quantity);
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Aliased>("Note: x\nqty: 99999999999", Strict()));
        Assert.Equal(2, error.LineNumber);
    }

    [Fact]
    public void Decode_ManyThreadsOnUnusedType_ValidAndInvalidPayloadsStayIsolated()
    {
        var failures = 0;
        var values = 0;
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            for (var n = 0; n < 50; n++)
            {
                if ((i + n) % 2 == 0)
                {
                    if (ToonDecoder.Decode<ThreadedHolder<short>>($"V: {n}", Strict())!.V == n) Interlocked.Increment(ref values);
                }
                else
                {
                    try { ToonDecoder.Decode<ThreadedHolder<short>>("V: 40000", Strict()); }
                    catch (ToonFormatException) { Interlocked.Increment(ref failures); }
                }
            }
        });

        Assert.Equal(400, values);
        Assert.Equal(400, failures);
    }

    [Fact]
    public void Decode_UntypedNumbers_StayDouble()
    {
        var untyped = Assert.IsType<ToonObject>(ToonDecoder.Decode("a: 1.5", Strict()));
        Assert.Equal(1.5, untyped["a"]!.GetValue<double>());
        Assert.Equal(1.5, Assert.IsType<double>(ToonDecoder.Decode<Dictionary<string, object?>>("a: 1.5", Strict())!["a"]));
        Assert.Equal(300d, Assert.IsType<double>(ToonDecoder.Decode<Dictionary<string, object?>>("a: 300", Strict())!["a"]));
    }

    [Theory]
    [InlineData("int", "\"42\"", "42")]
    [InlineData("int", "007", "7")]
    [InlineData("int", "+42", "42")]
    [InlineData("int", "\" 42 \"", "42")]
    [InlineData("int", "true", "1")]
    [InlineData("int", "null", "0")]
    [InlineData("int?", "null", "null")]
    [InlineData("double", "\"1,000\"", "1000")]
    [InlineData("decimal", "\"42\"", "42")]
    [InlineData("IntEnum", "7", "7")]
    [InlineData("IntEnum", "C", "C")]
    [InlineData("IntEnum", "\"C\"", "C")]
    [InlineData("IntEnum", "\"42\"", "C")]
    [InlineData("IntEnum", "null", "A")]
    [InlineData("IntEnum?", "null", "null")]
    [InlineData("FlagEnum", "\"X, Y\"", "X, Y")]
    [InlineData("FlagEnum", "3", "X, Y")]
    public void Decode_AcceptedNonCanonicalTokens_StillBindOnEveryShape(string type, string token, string expected)
    {
        foreach (var (shape, decode, _) in ShapesFor(type, token, Strict()))
            Assert.True(Format(decode()) == expected, $"{shape}: expected {expected}, got {Format(decode())}");
    }

    private static void AssertErrorOnEveryShape(string type, string token, ToonDecodeOptions options)
    {
        var typeName = TypeName(type);
        foreach (var (shape, decode, line) in ShapesFor(type, token, options))
        {
            ToonFormatException? error = null;
            try
            {
                var value = decode();
                Assert.Fail($"{shape}: decoded {Format(value)} instead of failing");
            }
            catch (ToonFormatException caught)
            {
                error = caught;
            }
            catch (Exception other) when (other is not Xunit.Sdk.XunitException)
            {
                Assert.Fail($"{shape}: {other.GetType().Name}: {other.Message}");
            }

            Assert.Equal(ToonErrorKind.Syntax, error!.Kind);
            Assert.True(line == error.LineNumber, $"{shape}: line {error.LineNumber?.ToString() ?? "none"} instead of {line}: {error.Message}");
            Assert.True(error.Message.Contains(typeName), $"{shape}: message '{error.Message}' does not name {typeName}");
        }
    }

    internal static string Format(object? value) => value switch
    {
        null => "null",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    internal static ToonDecodeOptions Strict(ToonProtocolVersion version = ToonProtocolVersion.V4_4) => new() { ProtocolVersion = version, Indent = 2 };

    internal static TheoryData<string, string> Cases(params (string Type, string Token)[] cases)
    {
        var data = new TheoryData<string, string>();
        foreach (var (type, token) in cases) data.Add(type, token);
        return data;
    }

    private static string TypeName(string type)
    {
        var clr = ClrType(type);
        return (Nullable.GetUnderlyingType(clr) ?? clr).FullName!;
    }

    internal static Type ClrType(string type) => type switch
    {
        "byte" => typeof(byte), "byte?" => typeof(byte?), "sbyte" => typeof(sbyte), "sbyte?" => typeof(sbyte?),
        "short" => typeof(short), "short?" => typeof(short?), "ushort" => typeof(ushort), "ushort?" => typeof(ushort?),
        "int" => typeof(int), "int?" => typeof(int?), "uint" => typeof(uint), "uint?" => typeof(uint?),
        "long" => typeof(long), "long?" => typeof(long?), "ulong" => typeof(ulong), "ulong?" => typeof(ulong?),
        "float" => typeof(float), "float?" => typeof(float?), "double" => typeof(double), "double?" => typeof(double?),
        "decimal" => typeof(decimal), "decimal?" => typeof(decimal?), "string" => typeof(string),
        "ByteEnum" => typeof(ByteEnum), "ByteEnum?" => typeof(ByteEnum?), "IntEnum" => typeof(IntEnum), "IntEnum?" => typeof(IntEnum?),
        "LongEnum" => typeof(LongEnum), "FlagEnum" => typeof(FlagEnum),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    internal static IEnumerable<(string Shape, Func<object?> Decode, int Line)> ShapesFor(string type, string token, ToonDecodeOptions options)
    {
        var method = typeof(TypedNumericBindingTests).GetMethod(nameof(Shapes), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IEnumerable<(string, Func<object?>, int)>)method.MakeGenericMethod(ClrType(type)).Invoke(null, [token, options])!;
    }

    private static IEnumerable<(string Shape, Func<object?> Decode, int Line)> Shapes<T>(string token, ToonDecodeOptions options)
    {
        yield return ("property", () => ToonDecoder.Decode<Holder<T>>($"V: {token}", options)!.V, 1);
        yield return ("constructor", () => ToonDecoder.Decode<Rec<T>>($"V: {token}", options)!.V, 1);
        yield return ("row", () => ToonDecoder.Decode<List<Holder<T>>>($"[1]{{V}}:\n  {token}", options)![0].V, 2);
        yield return ("constructor row", () => ToonDecoder.Decode<List<Rec<T>>>($"[1]{{V}}:\n  {token}", options)![0].V, 2);
        yield return ("inline array", () => ToonDecoder.Decode<T[]>($"[1]: {token}", options)![0], 1);
        yield return ("list item", () => ToonDecoder.Decode<List<T>>($"[1]:\n  - {token}", options)![0], 2);
        yield return ("dictionary value", () => ToonDecoder.Decode<Dictionary<string, T>>($"V: {token}", options)!["V"], 1);
        yield return ("root", () => ToonDecoder.Decode<T>(token, options), 1);
        yield return ("nested", () => ToonDecoder.Decode<Outer<T>>($"Inner:\n  V: {token}", options)!.Inner!.V, 2);
    }

    public enum ByteEnum : byte { A, B, C = 42 }

    public enum IntEnum { A, B, C = 42 }

    public enum LongEnum : long { A, B }

    [Flags]
    public enum FlagEnum { None = 0, X = 1, Y = 2 }

    public sealed class Holder<T>
    {
        public T V { get; set; } = default!;
    }

    public sealed class ThreadedHolder<T>
    {
        public T V { get; set; } = default!;
    }

    public sealed record Rec<T>(T V);

    public sealed class Outer<T>
    {
        public Holder<T>? Inner { get; set; }
    }

    public sealed class Middle
    {
        public int A { get; set; }
        public byte V { get; set; }
        public int B { get; set; }
    }

    public sealed class Aliased
    {
        public string? Note { get; set; }

        [ToonPropertyName("qty")]
        public int Quantity { get; set; }
    }
}
