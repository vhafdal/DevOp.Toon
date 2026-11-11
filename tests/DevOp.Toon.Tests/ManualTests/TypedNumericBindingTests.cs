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
        ("int", "1.5"), ("uint", "2.5"), ("long", "0.5"), ("ulong", "1e-29"), ("IntEnum", "1.5"),
        ("byte", "1e3"), ("sbyte", "1e3"), ("float", "1e39"), ("float", "3.4028236e38"), ("float?", "-1e39"));

    [Theory]
    [MemberData(nameof(OutOfRangeCases))]
    public void Decode_NumberOutsideMember_ThrowsFormatExceptionWithTokenLineOnEveryShape(string type, string token)
        => AssertErrorOnEveryShape(type, token, Strict());

    public static TheoryData<string, string> FractionCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var type in new[] { "byte", "byte?", "sbyte", "short", "short?", "ushort", "int", "uint?", "long", "ulong", "ByteEnum", "IntEnum?" })
            foreach (var token in new[] { "1.5", "2.5", "0.5", "-0.5", "1e-29", "1e-400", "1.00000000000000000000000000001" })
                data.Add(type, token);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FractionCases))]
    public void Decode_FractionIntoIntegerOrEnumMember_ThrowsOnEveryShape(string type, string token)
        => AssertErrorOnEveryShape(type, token, Strict(), "is not an integer");

    [Theory]
    [InlineData("byte", "1.0e2", "100")]
    [InlineData("sbyte", "2.0", "2")]
    [InlineData("short", "1e3", "1000")]
    [InlineData("ushort?", "100e-2", "1")]
    [InlineData("int", "-0", "0")]
    [InlineData("uint", "-0.0", "0")]
    [InlineData("ulong", "-0.0", "0")]
    [InlineData("ulong?", "-0", "0")]
    [InlineData("long", "-9.223372036854775808e18", "-9223372036854775808")]
    [InlineData("long", "9007199254740993", "9007199254740993")]
    [InlineData("ulong", "1.8446744073709551615e19", "18446744073709551615")]
    [InlineData("ByteEnum", "4.2e1", "C")]
    [InlineData("float", "1.000000059604644775400625", "1.0000001")]
    [InlineData("float", "-0", "0")]
    [InlineData("double", "-0.0", "0")]
    [InlineData("double?", "1e308", "1E+308")]
    [InlineData("decimal", "-0.0", "0.0")]
    [InlineData("decimal", "1e-29", "0.0000000000000000000000000000")]
    [InlineData("decimal", "1.10", "1.10")]
    [InlineData("decimal?", "9007199254740993", "9007199254740993")]
    public void Decode_IntegralOrExactNumber_BindsSameValueOnEveryShape(string type, string token, string expected)
    {
        foreach (var (shape, decode, _) in ShapesFor(type, token, Strict()))
            Assert.True(Format(decode()) == expected, $"{shape}: expected {expected}, got {Format(decode())}");
    }

    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("1.50")]
    [InlineData("-0")]
    [InlineData("1e3")]
    [InlineData("0.1000000000000000055511151231257827")]
    public void Decode_NumberTokenIntoStringMember_KeepsTokenTextOnEveryShape(string token)
    {
        foreach (var (shape, decode, _) in ShapesFor("string", token, Strict()))
            Assert.True(token == (string?)decode(), $"{shape}: expected {token}, got {decode()}");
    }

    public static TheoryData<ToonProtocolVersion, string, string> LegacyEnumAcceptedCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            foreach (var (token, expected) in new[] { ("42", "C"), ("7", "7"), ("C", "C"), ("\"C\"", "C"), ("\"42\"", "C"), ("null", "A"), ("4.2e1", "C"), ("-0", "A"), ("true", "B"), ("false", "A") })
                data.Add(version, token, expected);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegacyEnumAcceptedCases))]
    public void Decode_LegacyEnumMember_BindsAsV4(ToonProtocolVersion version, string token, string expected)
    {
        Assert.Equal(expected, Format(ToonDecoder.Decode<Holder<ByteEnum>>($"V: {token}", Strict())!.V));
        Assert.Equal(expected, Format(ToonDecoder.Decode<Holder<ByteEnum>>($"V: {token}", Strict(version))!.V));
        Assert.Equal(expected, Format(ToonDecoder.Decode<List<Holder<ByteEnum>>>($"[1]{{V}}:\n  {token}", Strict(version))![0].V));
        Assert.Equal(expected, Format(ToonDecoder.Decode<List<Holder<ByteEnum>>>($"[1]:\n  - V: {token}", Strict(version))![0].V));
    }

    public static TheoryData<ToonProtocolVersion, string> LegacyEnumRejectedCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            foreach (var token in new[] { "256", "-1", "9007199254740993", "1.5", "0.5", "1e-29", "abc", "\"abc\"", "\"\"", "\"300\"" })
                data.Add(version, token);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegacyEnumRejectedCases))]
    public void Decode_LegacyEnumMemberTokenThatDoesNotFit_ThrowsFormatException(ToonProtocolVersion version, string token)
    {
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Holder<ByteEnum>>($"V: {token}", Strict()));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Holder<ByteEnum>>($"V: {token}", Strict(version)));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<Holder<ByteEnum>>>($"[1]{{V}}:\n  {token}", Strict(version)));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<Holder<ByteEnum>>>($"[1]:\n  - V: {token}", Strict(version)));
    }

    [Theory]
    [InlineData("1.5", null)]
    [InlineData("0.5", null)]
    [InlineData("1e-29", null)]
    [InlineData("1e-400", null)]
    [InlineData("256", null)]
    [InlineData("-1", null)]
    [InlineData("42", "C")]
    [InlineData("4.2e1", "C")]
    [InlineData("-0.0", "A")]
    public void Decode_EnumNumberOnPathExpansionAndJsonRoutes_BindsAsV4(string token, string? expected)
    {
        var expand = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V3_0, Indent = 2, ExpandPaths = ToonPathExpansion.Safe };
        var routes = new Func<object?>[]
        {
            () => ToonDecoder.Decode<Holder<ByteEnum>>($"V: {token}", Strict())!.V,
            () => ToonDecoder.Decode<Dotted>($"V: {token}\nz.q: 1", expand)!.V,
            () => new ToonService().Deserialize<Holder<ByteEnum>>($"{{\"V\":{token}}}", "application/json")!.V
        };

        foreach (var route in routes)
        {
            if (expected == null) Assert.Throws<ToonFormatException>(() => route());
            else Assert.Equal(expected, Format(route()));
        }
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_LegacyNegativeZeroIntoFloatingMember_BindsPositiveZeroAsV4(ToonProtocolVersion version)
    {
        foreach (var options in new[] { Strict(), Strict(version) })
        {
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ToonDecoder.Decode<Holder<double>>("V: -0.0", options)!.V));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ToonDecoder.Decode<Holder<double?>>("V: -0", options)!.V!.Value));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ToonDecoder.Decode<List<Holder<float>>>("[1]{V}:\n  -0.0", options)![0].V));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ToonDecoder.Decode<List<Holder<float?>>>("[1]:\n  - V: -0", options)![0].V!.Value));
        }
    }

    [Theory]
    [InlineData("1e400", double.PositiveInfinity)]
    [InlineData("-1e400", double.NegativeInfinity)]
    [InlineData("1e-400", 0d)]
    public void Parse_JsonNumberBeyondDoubleRange_GivesTheSameValueOnEveryRuntime(string token, double expected)
    {
        Assert.Equal(expected, ToonNode.Parse($"{{\"a\":{token}}}")!["a"]!.GetValue<double>());
        Assert.Equal(expected, ToonNode.Parse($"[{token}]")![0]!.GetValue<double>());
        Assert.Throws<ToonFormatException>(() => new ToonService().Deserialize<Holder<double>>($"{{\"V\":{token.Replace("-400", "400")}}}", "application/json"));
    }

    [Fact]
    public void Parse_MalformedJsonNumber_StillThrowsFormatException()
        => Assert.Throws<FormatException>(() => ToonNode.Parse("{\"a\":1e}"));

    [Theory]
    [InlineData(ToonProtocolVersion.V4_0)]
    [InlineData(ToonProtocolVersion.V4_1)]
    [InlineData(ToonProtocolVersion.V4_2)]
    [InlineData(ToonProtocolVersion.V4_3)]
    public void Decode_NonFiniteNumberWithoutStrict_ThrowsForNumericMembers(ToonProtocolVersion version)
    {
        var options = new ToonDecodeOptions { ProtocolVersion = version, Indent = 2, Strict = false };

        AssertErrorOnEveryShape("double", "1e400", options);
        AssertErrorOnEveryShape("int", "1e400", options);
        Assert.Equal("1e400", ToonDecoder.Decode<Holder<string>>("V: 1e400", options)!.V);
    }

    [Fact]
    public void Decode_CustomConstantColumnFraction_ThrowsWithRowLine()
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<Line>>("[3]{Id,Qty=1.5}:\n  1\n  2\n  3", Strict(ToonProtocolVersion.CustomV4_4)));

        Assert.Equal(2, error.LineNumber);
        Assert.Contains("is not an integer (System.Int16)", error.Message);
    }

    [Fact]
    public void Decode_CustomSpillFraction_ThrowsWithSpillLine()
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<Line>>("[2]{Id}:\n  1\n    Qty: 2.5\n  2", Strict(ToonProtocolVersion.CustomV4_4)));

        Assert.Equal(3, error.LineNumber);
        Assert.Contains("is not an integer (System.Int16)", error.Message);
    }

    public static TheoryData<string, string> InvalidTokenCases => Cases(
        ("int", "abc"), ("int", "\"abc\""), ("int", "\"\""), ("int?", "abc"), ("long", "\"1.5\""), ("byte", "\"300\""),
        ("double", "abc"), ("double", "\"\""), ("float", "\"x\""), ("decimal", "abc"), ("decimal", "\"\""),
        ("IntEnum", "abc"), ("IntEnum", "\"abc\""), ("IntEnum", "\"\""), ("ByteEnum", "D"), ("ByteEnum?", "\"300\""),
        ("double", "Infinity"), ("double", "\"Infinity\""), ("double", "NaN"), ("double?", "\"NaN\""), ("float", "\"-Infinity\""),
        ("float?", "NaN"), ("double", "\"1e400\""), ("bool", "abc"), ("bool?", "\"yes\""), ("Guid", "abc"), ("Guid?", "\"x\""),
        ("DateTime", "abc"), ("DateTime?", "\"null\""), ("TimeSpan", "\"x\""));

    public static TheoryData<string, string> TextScalarCases => Cases(
        ("Guid", "42"), ("Guid", "true"), ("Guid?", "1.5"), ("DateTime", "42"), ("DateTime?", "false"), ("DateTimeOffset", "1.5"),
        ("TimeSpan", "false"), ("TimeSpan?", "7"), ("Uri", "42"), ("Version", "1"), ("Version", "true"), ("char", "5"), ("char?", "true"),
        ("byte[]", "42"), ("byte[]", "false"));

    [Theory]
    [MemberData(nameof(TextScalarCases))]
    public void Decode_NumberOrBooleanIntoTextScalarMember_ThrowsFormatExceptionWithTokenLineOnEveryShape(string type, string token)
        => AssertErrorOnEveryShape(type, token, Strict());

    [Fact]
    public void Decode_NumberIntoUnsupportedMember_IsNotReportedAsFormatError()
    {
        var error = Record.Exception(() => ToonDecoder.Decode<Holder<Unsupported>>("V: 42", Strict()));

        Assert.NotNull(error);
        Assert.IsNotType<ToonFormatException>(error);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Decode_BooleanTokenIntoStringMember_KeepsTokenTextOnEveryShape(string token)
    {
        foreach (var (shape, decode, _) in ShapesFor("string", token, Strict()))
            Assert.True(token == (string?)decode(), $"{shape}: expected {token}, got {decode()}");
    }

    public static TheoryData<ToonProtocolVersion, string, string> LegacyTextScalarRejectedCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            foreach (var (type, token) in new[] { ("bool", "abc"), ("bool", "\"yes\""), ("Guid", "abc"), ("Guid", "42"), ("Guid", "true"), ("DateTime", "abc"), ("DateTime", "42"),
                         ("TimeSpan", "abc"), ("TimeSpan", "true"), ("DateTimeOffset", "abc"), ("char", "ab"), ("bool", "007"), ("bool", "+1") })
                data.Add(version, type, token);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegacyTextScalarRejectedCases))]
    public void Decode_LegacyTextScalarMemberTokenThatDoesNotParse_ThrowsFormatException(ToonProtocolVersion version, string type, string token)
    {
        Assert.Throws<ToonFormatException>(() => ShapesFor(type, token, Strict()).First().Decode());
        foreach (var (shape, decode, _) in LegacyShapesFor(type, token, Strict(version)))
        {
            var error = Record.Exception(decode);
            Assert.True(error is ToonFormatException, $"{shape}: {(error == null ? "decoded " + Format(decode()) : error.GetType().Name + ": " + error.Message)}");
        }
    }

    public static TheoryData<ToonProtocolVersion, string, string> LegacyTextScalarAcceptedCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            foreach (var (type, token) in new[] { ("bool", "true"), ("bool", "\"true\""), ("bool", "\"False\""), ("bool", "1"), ("bool", "0"), ("bool", "null"),
                         ("Guid", "null"), ("Guid", "\"11111111-2222-3333-4444-555555555555\""), ("DateTime", "null"), ("TimeSpan", "\"01:02:03\""), ("char", "x") })
                data.Add(version, type, token);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegacyTextScalarAcceptedCases))]
    public void Decode_LegacyTextScalarMember_BindsAsV4(ToonProtocolVersion version, string type, string token)
    {
        var expected = Format(ShapesFor(type, token, Strict()).First().Decode());
        foreach (var (shape, decode, _) in LegacyShapesFor(type, token, Strict(version)))
            Assert.True(expected == Format(decode()), $"{shape}: expected {expected}, got {Format(decode())}");
    }

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

    private static void AssertErrorOnEveryShape(string type, string token, ToonDecodeOptions options, string? message = null)
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
            if (message != null) Assert.True(error.Message.Contains(message), $"{shape}: message '{error.Message}' does not contain '{message}'");
        }
    }

    internal static string Format(object? value) => value switch
    {
        null => "null",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
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
        "LongEnum" => typeof(LongEnum), "FlagEnum" => typeof(FlagEnum), "bool" => typeof(bool), "bool?" => typeof(bool?),
        "Guid" => typeof(Guid), "Guid?" => typeof(Guid?), "DateTime" => typeof(DateTime), "DateTime?" => typeof(DateTime?), "TimeSpan" => typeof(TimeSpan),
        "TimeSpan?" => typeof(TimeSpan?), "DateTimeOffset" => typeof(DateTimeOffset), "Uri" => typeof(Uri), "Version" => typeof(Version),
        "char" => typeof(char), "char?" => typeof(char?), "byte[]" => typeof(byte[]),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    internal static IEnumerable<(string Shape, Func<object?> Decode, int Line)> ShapesFor(string type, string token, ToonDecodeOptions options)
    {
        var method = typeof(TypedNumericBindingTests).GetMethod(nameof(Shapes), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IEnumerable<(string, Func<object?>, int)>)method.MakeGenericMethod(ClrType(type)).Invoke(null, [token, options])!;
    }

    internal static IEnumerable<(string Shape, Func<object?> Decode, int Line)> LegacyShapesFor(string type, string token, ToonDecodeOptions options)
    {
        var method = typeof(TypedNumericBindingTests).GetMethod(nameof(LegacyShapes), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IEnumerable<(string, Func<object?>, int)>)method.MakeGenericMethod(ClrType(type)).Invoke(null, [token, options])!;
    }

    private static IEnumerable<(string Shape, Func<object?> Decode, int Line)> LegacyShapes<T>(string token, ToonDecodeOptions options)
    {
        yield return ("property", () => ToonDecoder.Decode<Holder<T>>($"V: {token}", options)!.V, 1);
        yield return ("constructor", () => ToonDecoder.Decode<Rec<T>>($"V: {token}", options)!.V, 1);
        yield return ("row", () => ToonDecoder.Decode<List<Holder<T>>>($"[1]{{V}}:\n  {token}", options)![0].V, 2);
        yield return ("constructor row", () => ToonDecoder.Decode<List<Rec<T>>>($"[1]{{V}}:\n  {token}", options)![0].V, 2);
        yield return ("list item", () => ToonDecoder.Decode<List<Holder<T>>>($"[1]:\n  - V: {token}", options)![0].V, 2);
        yield return ("nested", () => ToonDecoder.Decode<Outer<T>>($"Inner:\n  V: {token}", options)!.Inner!.V, 2);
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

    public struct Unsupported
    {
        public int Value { get; set; }
    }

    public sealed class Dotted
    {
        public ByteEnum V { get; set; }
        public Dictionary<string, object?>? Z { get; set; }
    }

    public sealed class Line
    {
        public int Id { get; set; }
        public short Qty { get; set; }
    }

    public sealed class Aliased
    {
        public string? Note { get; set; }

        [ToonPropertyName("qty")]
        public int Quantity { get; set; }
    }
}
