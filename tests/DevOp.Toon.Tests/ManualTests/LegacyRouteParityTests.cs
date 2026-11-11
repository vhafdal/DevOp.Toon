namespace DevOp.Toon.Tests;

public sealed class LegacyRouteParityTests
{
    public static TheoryData<ToonProtocolVersion> AllVersions => new()
    {
        ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_StructDictionaryAndListMembers_MaterializeEveryValue(ToonProtocolVersion version)
    {
        var source = new PointHolder
        {
            Id = 1,
            Map = new Dictionary<string, Point> { ["a"] = new() { X = 1, Y = 2 }, ["b"] = new() { X = 3, Y = 4 } },
            List = [new() { X = 5, Y = 6 }, new() { X = 7, Y = 8 }]
        };
        var text = ToonEncoder.Encode(source, new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });

        var decoded = ToonDecoder.Decode<PointHolder>(text, Options(version))!;

        Assert.Equal(1, decoded.Id);
        Assert.Equal(source.Map, decoded.Map);
        Assert.Equal(source.List, decoded.List);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ConstructorBoundExtraSettableMembers_Bound(ToonProtocolVersion version)
    {
        var root = ToonDecoder.Decode<NotedRecord>("Id: 1\nNote: hello\nlbl: L\nTags[2]: a,b", Options(version))!;
        var list = ToonDecoder.Decode<List<NotedRecord>>("[2]:\n  - Id: 1\n    Note: a\n  - note: b\n    Id: 2", Options(version))!;
        var table = ToonDecoder.Decode<List<NotedRecord>>("[2]{Id,Note,lbl}:\n  1,a,x\n  2,b,y", Options(version))!;

        Assert.Equal(1, root.Id);
        Assert.Equal("hello", root.Note);
        Assert.Equal("L", root.Label);
        Assert.Equal(["a", "b"], root.Tags);
        Assert.Equal(["a", "b"], list.Select(r => r.Note));
        Assert.Equal([1, 2], list.Select(r => r.Id));
        Assert.Equal(["a", "b"], table.Select(r => r.Note));
        Assert.Equal(["x", "y"], table.Select(r => r.Label));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_LegacyConstructorBoundExtraMembers_BoundOnEveryRoute(ToonProtocolVersion version)
    {
        var expanded = ToonDecoder.Decode<NotedRecord>("Id: 1\nNote: hello\nz.q: 1", new ToonDecodeOptions { ProtocolVersion = version, Indent = 2, ExpandPaths = ToonPathExpansion.Safe })!;
        var continuation = ToonDecoder.Decode<List<NotedRecord>>("[1]{Id}:\n  1\n    Note: under\n    Tags[1]: t", Options(ToonProtocolVersion.CustomLegacy))!;
        var json = new ToonService().Deserialize<NotedRecord>("{\"Id\":1,\"Note\":\"j\"}", "application/json")!;

        Assert.Equal("hello", expanded.Note);
        Assert.Equal("under", continuation[0].Note);
        Assert.Equal(["t"], continuation[0].Tags);
        Assert.Equal("j", json.Note);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ConstructorBoundExtraMember_BoundNotCaptured(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<NotedAccountRecord>("Name: Cash\nNote: kept\nOther: 1", Options(version))!;

        Assert.Equal("kept", decoded.Note);
        Assert.Equal(["Other"], decoded.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ListItemFirstFieldArrayHeader_KeepsDecimalExact(ToonProtocolVersion version)
    {
        const string text = "[2]:\n  - Tags[2]: a,b\n    Name: Cash\n    Balance: 0.1234567890123456789012345678\n  - Name: Bank\n    Balance: 1.10";
        var options = Options(version);

        var lines = ToonDecoder.Decode<List<LedgerLine>>(text, options)!;
        var records = ToonDecoder.Decode<List<LedgerLineRecord>>(text, options)!;
        var annotated = ToonDecoder.Decode<List<AnnotatedLedgerLine>>(text, options)!;

        Assert.Equal(0.1234567890123456789012345678m, lines[0].Balance);
        Assert.Equal(1.10m, lines[1].Balance);
        Assert.Equal("1.10", lines[1].Balance.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(["a", "b"], lines[0].Tags);
        Assert.Equal("Cash", lines[0].Name);
        Assert.Equal(0.1234567890123456789012345678m, records[0].Balance);
        Assert.Equal(["a", "b"], records[0].Tags);
        Assert.Equal("Bank", records[1].Name);
        Assert.Equal(0.1234567890123456789012345678m, annotated[0].Balance);
        Assert.Equal(["a", "b"], Assert.IsType<List<object?>>(annotated[0].Annotations!["Tags"]));
        Assert.Null(annotated[1].Annotations);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_ListItemFirstFieldTable_MatchesEncoder(ToonProtocolVersion version)
    {
        var source = new List<RowsLine>
        {
            new() { Rows = [new() { K = "a", V = 1 }, new() { K = "b", V = 2 }], Name = "Cash", Balance = 0.1234567890123456789012345678m },
            new() { Rows = [new() { K = "c", V = 3 }], Name = "Bank", Balance = 2m }
        };
        var text = ToonEncoder.Encode(source.Select(r => new Dictionary<string, object?> { ["Rows"] = r.Rows, ["Name"] = r.Name, ["Balance"] = r.Balance }).ToList(),
            new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });

        var decoded = ToonDecoder.Decode<List<RowsLine>>(text, Options(version))!;

        Assert.Equal(source.Select(r => r.Name), decoded.Select(r => r.Name));
        Assert.Equal(source.Select(r => r.Balance), decoded.Select(r => r.Balance));
        Assert.Equal(["a", "b"], decoded[0].Rows!.Select(r => r.K));
        Assert.Equal(3, decoded[1].Rows![0].V);
    }

    [Theory]
    [InlineData("{\"M\":1e400}")]
    [InlineData("{\"D\":-1e400}")]
    [InlineData("{\"O\":1e400}")]
    [InlineData("{\"O\":[1,{\"x\":1e999}]}")]
    public void Deserialize_JsonTwoArgumentNumberBeyondDoubleRange_ThrowsFormatException(string json)
    {
        var error = Assert.Throws<ToonFormatException>(() => new ToonService().Deserialize<Amounts>(json, "application/json"));

        Assert.Contains("finite range", error.Message);
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<Amounts>("M: 1e400", new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
    }

    [Theory]
    [InlineData("{\"M\":1e30}")]
    [InlineData("{\"M\":79228162514264337593543950336}")]
    [InlineData("{\"Map\":{\"a\":-1e29}}")]
    public void Deserialize_JsonTwoArgumentNumberBeyondDecimalRange_ThrowsFormatException(string json)
    {
        var error = Assert.Throws<ToonFormatException>(() => new ToonService().Deserialize<Amounts>(json, "application/json"));

        Assert.Equal(ToonErrorKind.Syntax, error.Kind);
        Assert.Contains("System.Decimal", error.Message);
    }

    [Fact]
    public void Deserialize_JsonTwoArgumentNumbersInRange_Unchanged()
    {
        var decoded = new ToonService().Deserialize<Amounts>("{\"M\":12345.678,\"D\":1e308,\"O\":1e30,\"Map\":{\"a\":0.5}}", "application/json")!;

        Assert.Equal(12345.678m, decoded.M);
        Assert.Equal(1e308, decoded.D);
        Assert.Equal(1e30, decoded.O);
        Assert.Equal(0.5m, decoded.Map!["a"]);
    }

    [Fact]
    public void Deserialize_JsonTwoArgumentDecimalTargets_KeepExactValue()
    {
        const string exact = "0.1234567890123456789012345678";
        var service = new ToonService();

        var amounts = service.Deserialize<Amounts>(
            $"{{\"M\":79228162514264337593543950000,\"D\":{exact},\"O\":{exact},\"Map\":{{\"a\":{exact},\"b\":-79228162514264337593543950335}}}}",
            "application/json")!;
        var line = service.Deserialize<AnnotatedLedgerLine>($"{{\"Name\":\"Cash\",\"Balance\":{exact},\"Extra\":{exact}}}", "application/json")!;
        var record = service.Deserialize<LedgerLineRecord>($"{{\"Name\":\"Cash\",\"Balance\":79228162514264337593543950335}}", "application/json")!;
        var values = service.Deserialize<List<decimal?>>($"[{exact},null,1.10]", "application/json")!;

        Assert.Equal(79228162514264337593543950000m, amounts.M);
        Assert.Equal(0.12345678901234568, amounts.D);
        Assert.Equal(0.12345678901234568, Assert.IsType<double>(amounts.O));
        Assert.Equal(0.1234567890123456789012345678m, amounts.Map!["a"]);
        Assert.Equal(decimal.MinValue, amounts.Map["b"]);
        Assert.Equal(0.1234567890123456789012345678m, line.Balance);
        Assert.Equal(0.12345678901234568, Assert.IsType<double>(line.Annotations!["Extra"]));
        Assert.Equal(decimal.MaxValue, record.Balance);
        Assert.Equal([0.1234567890123456789012345678m, null, 1.10m], values);
        Assert.Equal("1.10", values[2]!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsType<double>(ToonNode.Parse(exact)!.AsValue().Value);
    }

    public static TheoryData<ToonProtocolVersion, string, string, string> DirectOutOfRangeCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, "Dec", "1e30", "Number 1E+30 is outside the range of System.Decimal");
                data.Add(version, "Dec", "1e400", "Number outside supported finite range");
                data.Add(version, "Dec", "-1e400", "Number outside supported finite range");
                data.Add(version, "NDec", "-1e29", "Number -1E+29 is outside the range of System.Decimal");
                data.Add(version, "Dbl", "1e400", "Number outside supported finite range");
                data.Add(version, "NDbl", "-1e400", "Number outside supported finite range");
                data.Add(version, "Flt", "1e39", "Number outside supported finite range");
                data.Add(version, "Int", "1e30", "Number 1E+30 is outside the range of System.Int32");
                data.Add(version, "Int", "99999999999", "Number 99999999999 is outside the range of System.Int32");
                data.Add(version, "NLng", "1e400", "Number outside supported finite range");
                data.Add(version, "Byt", "300", "Number 300 is outside the range of System.Byte");
                data.Add(version, "UInt", "-1", "Number -1 is outside the range of System.UInt32");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DirectOutOfRangeCases))]
    public void Decode_LegacyNumberOutsideMemberRange_ThrowsFormatException(ToonProtocolVersion version, string member, string token, string message)
    {
        var options = Options(version);

        AssertRangeError(message, () => ToonDecoder.Decode<Numbers>($"{member}: {token}", options));
        AssertRangeError(message, () => ToonDecoder.Decode<List<Numbers>>($"[1]{{{member}}}:\n  {token}", options));
        AssertRangeError(message, () => ToonDecoder.Decode<List<Numbers>>($"[1]:\n  - {member}: {token}", options));
        AssertRangeError(message, () => ToonDecoder.Decode<List<Numbers>>($"[1]{{Int}}:\n  1\n    {member}: {token}", Options(ToonProtocolVersion.CustomLegacy)));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_LegacyNumberOutsideRangeOutsideMembers_ThrowsFormatException(ToonProtocolVersion version)
    {
        var options = Options(version);

        AssertRangeError("System.Decimal", () => ToonDecoder.Decode<NumbersRecord>("Dec: 1e30\nInt: 1", options));
        AssertRangeError("finite range", () => ToonDecoder.Decode<NumbersRecord>("Dec: 1\nInt: 1e400", options));
        AssertRangeError("finite range", () => ToonDecoder.Decode<decimal>("1e400", options));
        AssertRangeError("System.Decimal", () => ToonDecoder.Decode<List<decimal>>("[2]: 1,1e30", options));
        AssertRangeError("finite range", () => ToonDecoder.Decode<Dictionary<string, decimal>>("a: 1e400", options));
        AssertRangeError("System.Int32", () => ToonDecoder.Decode<Dictionary<string, int>>("a: 1e30", options));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_LegacyNumbersInsideMemberRange_Unchanged(ToonProtocolVersion version)
    {
        const string text = "Dec: 0.1234567890123456789012345678\nNDec: -79228162514264337593543950335\nDbl: 1e308\nNDbl: -1.5\nFlt: 3.4e38\n" +
                            "Int: 1e2\nNLng: -9223372036854775808\nByt: 255\nUInt: 4294967295\nObj: 1e400";

        var decoded = ToonDecoder.Decode<Numbers>(text, Options(version))!;

        Assert.Equal(0.1234567890123456789012345678m, decoded.Dec);
        Assert.Equal(decimal.MinValue, decoded.NDec);
        Assert.Equal(1e308, decoded.Dbl);
        Assert.Equal(-1.5, decoded.NDbl);
        Assert.Equal(3.4e38f, decoded.Flt);
        Assert.Equal(100, decoded.Int);
        Assert.Equal(long.MinValue, decoded.NLng);
        Assert.Equal(byte.MaxValue, decoded.Byt);
        Assert.Equal(uint.MaxValue, decoded.UInt);
        Assert.Equal("1e400", decoded.Obj);
    }

    public static TheoryData<ToonProtocolVersion, string, string, string> DirectRejectedTokenCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, "Int", "1.5", "Number 1.5 is outside the range of System.Int32");
                data.Add(version, "NInt", "-0.5", "Number -0.5 is outside the range of System.Int32");
                data.Add(version, "NLng", "1.5", "Number 1.5 is outside the range of System.Int64");
                data.Add(version, "UInt", "0.5", "Number 0.5 is outside the range of System.UInt32");
                data.Add(version, "ULng", "2.5e0", "Number 2.5 is outside the range of System.UInt64");
                data.Add(version, "Byt", "255.6", "Number 255.6 is outside the range of System.Byte");
                data.Add(version, "Int", "abc", "Value abc is not a valid System.Int32");
                data.Add(version, "Int", "\"1.5\"", "Value 1.5 is not a valid System.Int32");
                data.Add(version, "Int", "Infinity", "Value Infinity is not a valid System.Int32");
                data.Add(version, "Dec", "abc", "Value abc is not a valid System.Decimal");
                data.Add(version, "Dec", "NaN", "Value NaN is not a valid System.Decimal");
                data.Add(version, "NDec", "\"\"", "Value  is not a valid System.Decimal");
                data.Add(version, "Dbl", "abc", "Value abc is not a valid System.Double");
                data.Add(version, "Flt", "0x10", "Value 0x10 is not a valid System.Single");
                data.Add(version, "Sht", "1_000", "Value 1_000 is not a valid System.Int16");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DirectRejectedTokenCases))]
    public void Decode_LegacyTokenNotValidForNumericMember_ThrowsFormatException(ToonProtocolVersion version, string member, string token, string message)
    {
        var options = Options(version);

        AssertRangeError(message, () => ToonDecoder.Decode<Numbers>($"{member}: {token}", options));
        AssertRangeError(message, () => ToonDecoder.Decode<List<Numbers>>($"[1]{{{member}}}:\n  {token}", options));
        AssertRangeError(message, () => ToonDecoder.Decode<List<Numbers>>($"[1]:\n  - {member}: {token}", options));
        Assert.ThrowsAny<Exception>(() => ToonDecoder.Decode<Numbers>($"{member}: {token}", Options(ToonProtocolVersion.V4_4)));
    }

    public static TheoryData<ToonProtocolVersion, string, string> DirectAcceptedTokenCases
    {
        get
        {
            var data = new TheoryData<ToonProtocolVersion, string, string>();
            foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
            {
                data.Add(version, "Int", "\"12\"");
                data.Add(version, "NInt", "\"-7\"");
                data.Add(version, "Dbl", "\"1.5\"");
                data.Add(version, "Dec", "\"0.1234567890123456789012345678\"");
                data.Add(version, "Flt", "\"Infinity\"");
                data.Add(version, "Int", "true");
                data.Add(version, "Dec", "false");
                data.Add(version, "Int", "null");
                data.Add(version, "Dbl", "null");
                data.Add(version, "Byt", "1.5");
                data.Add(version, "Byt", "-0.5");
                data.Add(version, "Sht", "2.5");
                data.Add(version, "Sht", "-1.5e0");
                data.Add(version, "ULng", "\"18446744073709551615\"");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DirectAcceptedTokenCases))]
    public void Decode_LegacyTokenAcceptedByV4_DecodesAsV4Does(ToonProtocolVersion version, string member, string token)
    {
        var property = typeof(Numbers).GetProperty(member)!;
        var expected = property.GetValue(ToonDecoder.Decode<Numbers>($"{member}: {token}", Options(ToonProtocolVersion.V4_4)));
        var options = Options(version);

        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<Numbers>($"{member}: {token}", options)));
        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<List<Numbers>>($"[1]{{{member}}}:\n  {token}", options)![0]));
        Assert.Equal(expected, property.GetValue(ToonDecoder.Decode<List<Numbers>>($"[1]:\n  - {member}: {token}", options)![0]));
    }

    private static void AssertRangeError(string message, Func<object?> decode)
    {
        var error = Assert.Throws<ToonFormatException>(decode);

        Assert.Equal(ToonErrorKind.Syntax, error.Kind);
        Assert.Contains(message, error.Message);
    }

    private static ToonDecodeOptions Options(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    public sealed class Amounts
    {
        public decimal M { get; set; }
        public double D { get; set; }
        public object? O { get; set; }
        public Dictionary<string, decimal>? Map { get; set; }
    }

    public sealed class Numbers
    {
        public decimal Dec { get; set; }
        public decimal? NDec { get; set; }
        public double Dbl { get; set; }
        public double? NDbl { get; set; }
        public float Flt { get; set; }
        public int Int { get; set; }
        public long? NLng { get; set; }
        public byte Byt { get; set; }
        public short Sht { get; set; }
        public uint UInt { get; set; }
        public ulong ULng { get; set; }
        public int? NInt { get; set; }
        public object? Obj { get; set; }
    }

    public sealed record NumbersRecord(decimal Dec, int Int);

    public sealed class LedgerLine
    {
        public List<string>? Tags { get; set; }
        public string? Name { get; set; }
        public decimal Balance { get; set; }
    }

    public sealed record LedgerLineRecord(string Name, decimal Balance, List<string>? Tags);

    public sealed class AnnotatedLedgerLine
    {
        public string? Name { get; set; }
        public decimal Balance { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class KeyValueRow
    {
        public string? K { get; set; }
        public int V { get; set; }
    }

    public sealed class RowsLine
    {
        public List<KeyValueRow>? Rows { get; set; }
        public string? Name { get; set; }
        public decimal Balance { get; set; }
    }

    public sealed record NotedRecord(int Id)
    {
        public string? Note { get; set; }

        [ToonPropertyName("lbl")]
        public string? Label { get; set; }

        public List<string>? Tags { get; set; }
    }

    public sealed record NotedAccountRecord(string Name, [property: ToonExtensionData] IDictionary<string, object?>? Annotations)
    {
        public string? Note { get; set; }
    }

    public record struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class PointHolder
    {
        public int Id { get; set; }
        public Dictionary<string, Point>? Map { get; set; }
        public List<Point>? List { get; set; }
    }
}
