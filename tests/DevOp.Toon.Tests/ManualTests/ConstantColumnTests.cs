#if NET8_0_OR_GREATER
using System.Text;
using System.Text.Json.Nodes;

namespace DevOp.Toon.Tests;

public sealed class ConstantColumnTests
{
    public static TheoryData<ToonProtocolVersion> OfficialV4Versions => new()
    {
        ToonProtocolVersion.V4_0, ToonProtocolVersion.V4_1, ToonProtocolVersion.V4_2,
        ToonProtocolVersion.V4_3, ToonProtocolVersion.V4_4
    };

    [Fact]
    public void Decode_ConstantHeaderField_AssignsValueToEveryRow()
    {
        const string source = "[3]{CompanyId=acme,IsSystem=false,AccountCode,Name}:\n  6900,Other Expenses\n  6910,Rent\n  6920,Power";

        var rows = ToonDecoder.Decode<List<Account>>(source, Custom())!;

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal("acme", row.CompanyId));
        Assert.All(rows, row => Assert.False(row.IsSystem));
        Assert.Equal([6900, 6910, 6920], rows.Select(row => row.AccountCode));
        Assert.Equal(["Other Expenses", "Rent", "Power"], rows.Select(row => row.Name));
        const string expected =
            "[{\"CompanyId\":\"acme\",\"IsSystem\":false,\"AccountCode\":6900,\"Name\":\"Other Expenses\"}," +
            "{\"CompanyId\":\"acme\",\"IsSystem\":false,\"AccountCode\":6910,\"Name\":\"Rent\"}," +
            "{\"CompanyId\":\"acme\",\"IsSystem\":false,\"AccountCode\":6920,\"Name\":\"Power\"}]";
        AssertJson(expected, ToonDecoder.Toon2Json(source, Custom()));
        Assert.True(ToonNode.DeepEquals(ToonNode.Parse(expected), ToonDecoder.Decode(source, Custom())));
    }

    [Fact]
    public void Decode_ConstantInNestedGroup_AssignsNestedLeaf()
    {
        const string source = "[3]{Id,Customer{Country=IS,Name}}:\n  1,Ada\n  2,Bob\n  3,Cy";

        AssertJson(
            "[{\"Id\":1,\"Customer\":{\"Country\":\"IS\",\"Name\":\"Ada\"}},{\"Id\":2,\"Customer\":{\"Country\":\"IS\",\"Name\":\"Bob\"}},{\"Id\":3,\"Customer\":{\"Country\":\"IS\",\"Name\":\"Cy\"}}]",
            ToonDecoder.Toon2Json(source, Custom()));
        var rows = ToonDecoder.Decode<List<Order>>(source, Custom())!;
        Assert.All(rows, row => Assert.Equal("IS", row.Customer!.Country));
        Assert.NotSame(rows[0].Customer, rows[1].Customer);
    }

    [Fact]
    public void Decode_ConstantInKeyedTableWithSpills_KeepsEntryKeysAndSpills()
    {
        const string source = "[3:]{Id,Customer{Country=IS,Name}}:\n  sku1: 1,Ada\n    Tags[2]: a,b\n  sku2: 2,Bob\n  sku3: 3,Cy";

        AssertJson(
            "{\"sku1\":{\"Id\":1,\"Customer\":{\"Country\":\"IS\",\"Name\":\"Ada\"},\"Tags\":[\"a\",\"b\"]}," +
            "\"sku2\":{\"Id\":2,\"Customer\":{\"Country\":\"IS\",\"Name\":\"Bob\"}}," +
            "\"sku3\":{\"Id\":3,\"Customer\":{\"Country\":\"IS\",\"Name\":\"Cy\"}}}",
            ToonDecoder.Toon2Json(source, Custom()));
    }

    [Fact]
    public void Decode_ConstantTokenKinds_PreservesPrimitiveType()
    {
        const string source = "[2]{Id,A=1,B=\"1\",C=false,D=\"false\",E=null,F=\"null\",G=\"\",H=-1.5}:\n  1\n  2";

        var row = "\"A\":1,\"B\":\"1\",\"C\":false,\"D\":\"false\",\"E\":null,\"F\":\"null\",\"G\":\"\",\"H\":-1.5";
        AssertJson($"[{{\"Id\":1,{row}}},{{\"Id\":2,{row}}}]", ToonDecoder.Toon2Json(source, Custom()));
    }

    [Fact]
    public void Decode_QuotedConstantWithEqualsDelimiterAndBraces_PreservesText()
    {
        const string source = "[2]{Id,Expr=\"a=b\",Pair=\"x,y\",Pipe=\"x|y\",Brace=\"{a}\",Colon=\"a:b\",Escaped=\"q\\\"t\\\\n\"}:\n  1\n  2";

        var json = JsonNode.Parse(ToonDecoder.Toon2Json(source, Custom()))!.AsArray();
        foreach (var row in json)
        {
            Assert.Equal("a=b", (string?)row!["Expr"]);
            Assert.Equal("x,y", (string?)row["Pair"]);
            Assert.Equal("x|y", (string?)row["Pipe"]);
            Assert.Equal("{a}", (string?)row["Brace"]);
            Assert.Equal("a:b", (string?)row["Colon"]);
            Assert.Equal("q\"t\\n", (string?)row["Escaped"]);
        }
    }

    [Fact]
    public void Decode_QuotedNameContainingEquals_RemainsCellColumn()
    {
        const string source = "[2]{Id,\"a=b\",\"my key\"=5}:\n  1,x\n  2,y";

        AssertJson("[{\"Id\":1,\"a=b\":\"x\",\"my key\":5},{\"Id\":2,\"a=b\":\"y\",\"my key\":5}]", ToonDecoder.Toon2Json(source, Custom()));
    }

    [Fact]
    public void Decode_ConstantIntoRecordStructNullableEnumAndDateMembers_BindsLikeCell()
    {
        const string constants = "[3]{Id,Kind=2,Qty=null,Created=\"2026-01-02T17:03:15.0000000Z\",At=\"2026-01-02T17:03:15.0000000+02:00\",Price=1.5,Pos{X=7,Y}}:\n  1,10\n  2,20\n  3,30";
        const string cells = "[3]{Id,Kind,Qty,Created,At,Price,Pos{X,Y}}:\n" +
            "  1,2,null,\"2026-01-02T17:03:15.0000000Z\",\"2026-01-02T17:03:15.0000000+02:00\",1.5,7,10\n" +
            "  2,2,null,\"2026-01-02T17:03:15.0000000Z\",\"2026-01-02T17:03:15.0000000+02:00\",1.5,7,20\n" +
            "  3,2,null,\"2026-01-02T17:03:15.0000000Z\",\"2026-01-02T17:03:15.0000000+02:00\",1.5,7,30";

        var hoisted = ToonDecoder.Decode<List<Line>>(constants, Custom())!;
        var expected = ToonDecoder.Decode<List<Line>>(cells, Custom())!;

        Assert.Equal(expected, hoisted);
        Assert.Equal(LineKind.Credit, hoisted[2].Kind);
        Assert.Null(hoisted[1].Qty);
        Assert.Equal(7, hoisted[2].Pos.X);
        Assert.Equal(30, hoisted[2].Pos.Y);
        Assert.Equal(expected[0].Created.Kind, hoisted[0].Created.Kind);
        Assert.Equal(expected[0].At.Offset, hoisted[0].At.Offset);
    }

    [Fact]
    public void Decode_ConstantForUnknownMember_IsIgnoredOrCapturedByExtensionData()
    {
        const string source = "[3]{Name,Extra=x}:\n  a\n  b\n  c";

        var plain = ToonDecoder.Decode<List<Account>>(source, Custom())!;
        Assert.Equal(["a", "b", "c"], plain.Select(row => row.Name));

        var captured = ToonDecoder.Decode<List<ExtendedAccount>>(source, Custom())!;
        Assert.All(captured, row => Assert.Equal("x", row.Annotations!["Extra"]));
        Assert.NotSame(captured[0].Annotations, captured[1].Annotations);
    }

    [Fact]
    public void Decode_ConstantAndSpillSameKey_ThrowsDuplicateObjectKey()
    {
        const string source = "[3]{Id,Tag=a}:\n  1\n    Tag: b\n  2\n  3";

        Assert.Contains("Duplicate object key", Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom())).Message);
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom(strict: false)));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Decode<List<Tagged>>(source, Custom(strict: false)));
    }

    [Theory]
    [InlineData("[2]{Id,A=1,A}:\n  1,2\n  3,4")]
    [InlineData("[2]{Id,A,A=1}:\n  1,2\n  3,4")]
    [InlineData("[2]{Id,A=1,A=2}:\n  1\n  3")]
    [InlineData("[2]{Id,G{A=1,A}}:\n  1,2\n  3,4")]
    public void Decode_ConstantDuplicatesHeaderField_ThrowsWhenNotStrict(string source)
    {
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom(strict: false)));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_RowWithCellForConstant_ThrowsRowWidthMismatch(bool strict)
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json("[2]{Id,A=1}:\n  1,1\n  2,1", Custom(strict)));
        Assert.Contains("Row width mismatch", error.Message);
    }

    [Theory]
    [InlineData("[2]{A=1,B=2}:", true)]
    [InlineData("[2]{A=1,B=2}:", false)]
    [InlineData("[2]{A=1,B{C=2}}:\n  x", false)]
    [InlineData("x[2:]{A=1}:\n  a:\n  b:", false)]
    public void Decode_HeaderWithOnlyConstants_Throws(string source, bool strict)
    {
        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom(strict)));
        Assert.Contains("at least one cell", error.Message);
    }

    [Theory]
    [InlineData("[2]{A=,B}:\n  1\n  2")]
    [InlineData("[2]{A= ,B}:\n  1\n  2")]
    [InlineData("[2]{=1,B}:\n  1\n  2")]
    [InlineData("[2]{A=x=y,B}:\n  1\n  2")]
    [InlineData("[2]{A=\"x\"y,B}:\n  1\n  2")]
    [InlineData("[2]{A=\"x\\q\",B}:\n  1\n  2")]
    [InlineData("[2]{A=1{B},C}:\n  1,2\n  3,4")]
    [InlineData("[2]{A{B}=1,C}:\n  1,2\n  3,4")]
    [InlineData("[2]{A=x,y,B}:\n  1\n  2")]
    [InlineData("[2|]{A=x,y|B}:\n  1\n  2")]
    public void Decode_MalformedConstant_Throws(string source)
    {
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom()));
        Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json(source, Custom(strict: false)));
    }

    [Theory]
    [MemberData(nameof(OfficialV4Versions))]
    public void Decode_EqualsInHeaderUnderOfficialVersions_KeepsLiteralFieldName(ToonProtocolVersion version)
    {
        var options = new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 };

        AssertJson("[{\"A=1\":\"x\",\"B\":\"y\"},{\"A=1\":\"z\",\"B\":\"w\"}]", ToonDecoder.Toon2Json("[2]{A=1,B}:\n  x,y\n  z,w", options));
    }

    [Fact]
    public void Decode_ConstantHeaderUnderOfficialV44_ThrowsRowWidthMismatch()
    {
        var options = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4, Indent = 2 };

        var error = Assert.Throws<ToonFormatException>(() => ToonDecoder.Toon2Json("[2]{A=1,B}:\n  x\n  y", options));
        Assert.Contains("Row width mismatch", error.Message);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(257)]
    public void Decode_MoreDistinctConstantHeadersThanCache_StillDecodes(int tables)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < tables; i++) builder.Append($"t{i}[3]{{Id,C={i}}}:\n  1\n  2\n  3\n");
        for (var i = 0; i < tables; i++) builder.Append($"r{i}[3]{{Id,C={i % 7}}}:\n  1\n  2\n  3\n");

        var json = JsonNode.Parse(ToonDecoder.Toon2Json(builder.ToString(), Custom()))!.AsObject();

        for (var i = 0; i < tables; i++)
        {
            Assert.All(json[$"t{i}"]!.AsArray(), row => Assert.Equal(i, (int)row!["C"]!));
            Assert.All(json[$"r{i}"]!.AsArray(), row => Assert.Equal(i % 7, (int)row!["C"]!));
        }
    }

    [Fact]
    public void Decode_ReusedConstantHeaderAcrossTargetTypes_BindsEachTarget()
    {
        const string source = "First[3]{Id,Code=7}:\n  1\n  2\n  3\nSecond[3]{Id,Code=7}:\n  4\n  5\n  6\nThird[3]{Id,Code=7}:\n  7\n  8\n  9";

        var document = ToonDecoder.Decode<CodeDocument>(source, Custom())!;

        Assert.All(document.First!, row => Assert.Equal(7, row.Code));
        Assert.All(document.Second!, row => Assert.Equal(7d, row.Code));
        Assert.All(document.Third!, row => Assert.Equal(7, row.Code));
        Assert.Equal([4L, 5L, 6L], document.Second!.Select(row => row.Id));
    }

    [Fact]
    public void Decode_ConcurrentConstantTables_ProducesIndependentRows()
    {
        const string source = "[3]{Id,Customer{Country=IS,Name}}:\n  1,Ada\n  2,Bob\n  3,Cy";
        var results = new List<Order>[32];

        Parallel.For(0, results.Length, i => results[i] = ToonDecoder.Decode<List<Order>>(source, Custom())!);

        foreach (var rows in results)
        {
            Assert.Equal([1, 2, 3], rows.Select(row => row.Id));
            Assert.All(rows, row => Assert.Equal("IS", row.Customer!.Country));
            Assert.Equal(["Ada", "Bob", "Cy"], rows.Select(row => row.Customer!.Name));
        }
        Assert.NotSame(results[0][0].Customer, results[1][0].Customer);
    }

    public static TheoryData<ToonProtocolVersion> NonCustomV44Versions => new()
    {
        ToonProtocolVersion.V3_0, ToonProtocolVersion.V4_0, ToonProtocolVersion.V4_1, ToonProtocolVersion.V4_2,
        ToonProtocolVersion.V4_3, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomLegacy
    };

    private const string AccountsOff =
        "[3]{CompanyId,IsSystem,CreatedBy,AccountCode,Name,AccountType}:\n" +
        "  acme,false,system,6900,Other Expenses,Expense\n" +
        "  acme,false,system,1000,Cash,Asset\n" +
        "  acme,false,system,2000,Payables,Liability";

    private const string AccountsOn =
        "[3]{CompanyId=acme,IsSystem=false,CreatedBy=system,AccountCode,Name,AccountType}:\n" +
        "  6900,Other Expenses,Expense\n" +
        "  1000,Cash,Asset\n" +
        "  2000,Payables,Liability";

    [Fact]
    public void Encode_ConstantColumnsDefault_MatchesPreviousCustomOutput()
    {
        var options = Encode(constants: false);

        Assert.False(new ToonEncodeOptions().ConstantColumns);
        Assert.False(new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.CustomV4_4 }.ConstantColumns);
        Assert.Equal(AccountsOff, ToonEncoder.Encode(Accounts(), options));
        Assert.Equal(AccountsOff, ToonEncoder.Json2Toon(AccountsJson, options));
    }

    [Theory]
    [MemberData(nameof(NonCustomV44Versions))]
    public void Encode_ConstantColumnsOnOtherVersions_OutputUnchanged(ToonProtocolVersion version)
    {
        var off = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 };
        var on = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, ConstantColumns = true };
        var keyed = new Dictionary<string, object> { ["a"] = new { Id = 1, K = "x" }, ["b"] = new { Id = 2, K = "x" }, ["c"] = new { Id = 3, K = "x" } };

        Assert.Equal(ToonEncoder.Encode(Accounts(), off), ToonEncoder.Encode(Accounts(), on));
        Assert.Equal(ToonEncoder.Encode(keyed, off), ToonEncoder.Encode(keyed, on));
        Assert.Equal(ToonEncoder.Json2Toon(AccountsJson, off), ToonEncoder.Json2Toon(AccountsJson, on));
        Assert.DoesNotContain("=", ToonEncoder.Encode(Accounts(), on));
    }

    [Fact]
    public void Encode_ConstantColumn_HoistsInDeclaredOrder()
    {
        Assert.Equal(AccountsOn, ToonEncoder.Encode(Accounts(), Encode()));
        Assert.Equal(AccountsOn, ToonEncoder.Json2Toon(AccountsJson, Encode()));

        var rows = new[] { new Mixed { Id = 1, Kind = "A", Name = "x" }, new Mixed { Id = 2, Kind = "A", Name = "y" }, new Mixed { Id = 3, Kind = "A", Name = "z" } };
        Assert.Equal("[3]{Id,Kind=A,Name}:\n  1,x\n  2,y\n  3,z", ToonEncoder.Encode(rows, Encode()));
        var decoded = ToonDecoder.Decode<List<Account>>(AccountsOn, Custom())!;
        Assert.Equal(Accounts().Select(Describe), decoded.Select(Describe));
    }

    [Fact]
    public void Encode_BelowRowThreshold_KeepsCells()
    {
        var rows = Accounts().Take(2).ToArray();

        Assert.Equal(
            "[2]{CompanyId,IsSystem,CreatedBy,AccountCode,Name,AccountType}:\n  acme,false,system,6900,Other Expenses,Expense\n  acme,false,system,1000,Cash,Asset",
            ToonEncoder.Encode(rows, Encode()));
    }

    [Fact]
    public void Encode_AllColumnsConstant_KeepsFirstLeafAsCell()
    {
        var flat = Enumerable.Range(0, 3).Select(_ => new { A = 1, B = "x" }).ToArray();
        var nested = Enumerable.Range(0, 3).Select(_ => new { G = new { X = 1, Y = 2 }, Z = 3 }).ToArray();

        Assert.Equal("[3]{A,B=x}:\n  1\n  1\n  1", ToonEncoder.Encode(flat, Encode()));
        Assert.Equal("[3]{G{X,Y=2},Z=3}:\n  1\n  1\n  1", ToonEncoder.Encode(nested, Encode()));
        Assert.Equal(3, ToonDecoder.Toon2Json(ToonEncoder.Encode(nested, Encode()), Custom()).Split("\"Z\":3").Length - 1);
    }

    [Fact]
    public void Encode_NestedGroupConstantLeaf_HoistsInsideGroup()
    {
        var rows = new[]
        {
            new Order { Id = 1, Customer = new Customer { Country = "IS", Name = "Ada" } },
            new Order { Id = 2, Customer = new Customer { Country = "IS", Name = "Bob" } },
            new Order { Id = 3, Customer = new Customer { Country = "IS", Name = "Cy" } }
        };

        Assert.Equal("[3]{Id,Customer{Country=IS,Name}}:\n  1,Ada\n  2,Bob\n  3,Cy", ToonEncoder.Encode(rows, Encode()));
    }

    [Fact]
    public void Encode_KeyedTableConstant_HoistsAndKeepsSpills()
    {
        var data = new Dictionary<string, object>
        {
            ["sku1"] = new Dictionary<string, object?> { ["Id"] = 1, ["Customer"] = new Dictionary<string, object?> { ["Country"] = "IS", ["Name"] = "Ada" }, ["Tags"] = new[] { "a", "b" } },
            ["sku2"] = new Dictionary<string, object?> { ["Id"] = 2, ["Customer"] = new Dictionary<string, object?> { ["Country"] = "IS", ["Name"] = "Bob" } },
            ["sku3"] = new Dictionary<string, object?> { ["Id"] = 3, ["Customer"] = new Dictionary<string, object?> { ["Country"] = "IS", ["Name"] = "Cy" } }
        };

        const string expected = "[3:]{Id,Customer{Country=IS,Name}}:\n  sku1: 1,Ada\n    Tags[2]: a,b\n  sku2: 2,Bob\n  sku3: 3,Cy";
        Assert.Equal(expected, ToonEncoder.Encode(data, Encode()));
        AssertJson(System.Text.Json.JsonSerializer.Serialize(data), ToonDecoder.Toon2Json(expected, Custom()));
    }

    [Fact]
    public void Encode_ColumnAbsentInOneRow_SpillsInsteadOfHoisting()
    {
        var rows = new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Kind"] = "A" },
            new Dictionary<string, object?> { ["Id"] = 2, ["Kind"] = "A" },
            new Dictionary<string, object?> { ["Id"] = 3 }
        };

        Assert.Equal("[3]{Id}:\n  1\n    Kind: A\n  2\n    Kind: A\n  3", ToonEncoder.Encode(rows, Encode()));
    }

    [Fact]
    public void Encode_AllNullColumn_OmittedWhenIgnoringElseHoistedAsNull()
    {
        var rows = Enumerable.Range(1, 3).Select(i => new Noted { Id = i, Note = null, Tag = "", G = new NullLeaf { X = null, Y = i } }).ToArray();

        Assert.Equal("[3]{Id,Note=null,Tag=\"\",G{X=null,Y}}:\n  1,1\n  2,2\n  3,3", ToonEncoder.Encode(rows, Encode()));
        var ignoring = Encode();
        ignoring.IgnoreNullOrEmpty = true;
        Assert.Equal("[3]{Id,G{X=null,Y}}:\n  1,1\n  2,2\n  3,3", ToonEncoder.Encode(rows, ignoring));
    }

    [Fact]
    public void Encode_MixedNullAndEmptyString_NotConstant()
    {
        var rows = new[] { new Noted { Id = 1, Tag = null }, new Noted { Id = 2, Tag = "" }, new Noted { Id = 3, Tag = null } };

        var text = ToonEncoder.Encode(rows, Encode());

        Assert.StartsWith("[3]{Id,Note=null,Tag,G=null}:", text);
        Assert.Contains("  2,\"\"", text);
    }

    [Fact]
    public void Encode_NumberAndNumericString_NotConstant()
    {
        Assert.Equal("[3]{Id,V}:\n  1,1\n  2,\"1\"\n  3,1", ToonEncoder.Json2Toon("[{\"Id\":1,\"V\":1},{\"Id\":2,\"V\":\"1\"},{\"Id\":3,\"V\":1}]", Encode()));
        Assert.Equal("[3]{Id,V}:\n  1,1\n  2,1\n  3,1", ToonEncoder.Json2Toon("[{\"Id\":1,\"V\":1},{\"Id\":2,\"V\":1.0},{\"Id\":3,\"V\":1}]", Encode()));
        Assert.Equal("[3]{Id,V=1}:\n  1\n  2\n  3", ToonEncoder.Json2Toon("[{\"Id\":1,\"V\":1},{\"Id\":2,\"V\":1},{\"Id\":3,\"V\":1}]", Encode()));
    }

    [Theory]
    [InlineData("a=b", ToonDelimiter.COMMA, "\"a=b\"")]
    [InlineData("x,y", ToonDelimiter.PIPE, "\"x,y\"")]
    [InlineData("x,y", ToonDelimiter.TAB, "\"x,y\"")]
    [InlineData("x|y", ToonDelimiter.COMMA, "\"x|y\"")]
    [InlineData("x|y", ToonDelimiter.TAB, "\"x|y\"")]
    [InlineData("x\ty", ToonDelimiter.COMMA, "\"x\\ty\"")]
    [InlineData("true", ToonDelimiter.COMMA, "\"true\"")]
    [InlineData("12", ToonDelimiter.COMMA, "\"12\"")]
    [InlineData("", ToonDelimiter.COMMA, "\"\"")]
    [InlineData("-a", ToonDelimiter.COMMA, "\"-a\"")]
    [InlineData("a:b", ToonDelimiter.COMMA, "\"a:b\"")]
    [InlineData("{a}", ToonDelimiter.COMMA, "\"{a}\"")]
    [InlineData("[a]", ToonDelimiter.PIPE, "\"[a]\"")]
    [InlineData("plain text", ToonDelimiter.COMMA, "plain text")]
    public void Encode_ConstantNeedingHeaderQuotes_IsQuoted(string value, ToonDelimiter delimiter, string expected)
    {
        var options = Encode();
        options.Delimiter = delimiter;
        var d = delimiter switch { ToonDelimiter.PIPE => "|", ToonDelimiter.TAB => "\t", _ => "," };
        var marker = delimiter == ToonDelimiter.COMMA ? "" : d;
        var rows = Enumerable.Range(1, 3).Select(i => new Mixed { Id = i, Kind = value, Name = "n" + i }).ToArray();

        var text = ToonEncoder.Encode(rows, options);

        Assert.Equal($"[3{marker}]{{Id{d}Kind={expected}{d}Name}}:\n  1{d}n1\n  2{d}n2\n  3{d}n3", text);
        var decoded = ToonDecoder.Decode<List<Mixed>>(text, Custom())!;
        Assert.All(decoded, row => Assert.Equal(value, row.Kind));
        Assert.Equal(ToonProtocolVersion.CustomV4_4, ToonDecoder.DetectVersion(text).SelectedVersion);
        Assert.All(ToonDecoder.Decode<List<Mixed>>(text)!, row => Assert.Equal(value, row.Kind));
    }

    [Fact]
    public void Encode_CellContainingEquals_StaysUnquoted()
    {
        var rows = new[] { new Mixed { Id = 1, Kind = "a=b" }, new Mixed { Id = 2, Kind = "c=d" }, new Mixed { Id = 3, Kind = "e=f" } };

        Assert.Equal("[3]{Id,Kind,Name=null}:\n  1,a=b\n  2,c=d\n  3,e=f", ToonEncoder.Encode(rows, Encode()));
    }

    [Fact]
    public void Encode_DateTimeDifferingOnlyInKind_NotConstant()
    {
        var instant = new DateTime(2026, 1, 2, 17, 3, 15);
        var utc = DateTime.SpecifyKind(instant, DateTimeKind.Utc);
        var rows = new[] { new Stamped { Id = 1, At = utc }, new Stamped { Id = 2, At = instant }, new Stamped { Id = 3, At = utc } };
        var same = new[] { new Stamped { Id = 1, At = utc }, new Stamped { Id = 2, At = utc }, new Stamped { Id = 3, At = utc } };

        AssertCellsRoundTrip(rows, "At", row => row.At, (left, right) => left.Ticks == right.Ticks && left.Kind == right.Kind);
        Assert.Contains("At=", ToonEncoder.Encode(same, Encode()));
    }

    [Fact]
    public void Encode_DateTimeOffsetSameInstantDifferentOffset_NotConstant()
    {
        var utc = new DateTimeOffset(2026, 1, 2, 17, 3, 15, TimeSpan.Zero);
        var rows = new[] { new Stamped { Id = 1, When = utc }, new Stamped { Id = 2, When = utc.ToOffset(TimeSpan.FromHours(2)) }, new Stamped { Id = 3, When = utc } };

        AssertCellsRoundTrip(rows, "When", row => row.When, (left, right) => left.EqualsExact(right));
    }

    [Fact]
    public void Encode_UriDifferingOnlyInFragment_NotConstant()
    {
        var rows = new[] { new Stamped { Id = 1, Link = new Uri("https://example.com/a#one") }, new Stamped { Id = 2, Link = new Uri("https://example.com/a#two") }, new Stamped { Id = 3, Link = new Uri("https://example.com/a#one") } };

        AssertCellsRoundTrip(rows, "Link", row => row.Link, (left, right) => left?.ToString() == right?.ToString());
    }

    [Fact]
    public void Encode_ToBytesAndStream_MatchStringOutput()
    {
        var text = ToonEncoder.Encode(Accounts(), Encode());
        using var stream = new MemoryStream();

        ToonEncoder.EncodeToStream(Accounts(), stream, Encode());

        Assert.Equal(AccountsOn, text);
        Assert.Equal(text, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(Accounts(), Encode())));
        Assert.Equal(text, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void RoundTrip_EveryScalarTypeAsConstant_ReproducesValues()
    {
        var sample = Scalars.Sample();
        var rows = Enumerable.Range(1, 3).Select(i => Scalars.Sample() with { Id = i }).ToArray();

        var text = ToonEncoder.Encode(rows, Encode());
        var decoded = ToonDecoder.Decode<List<Scalars>>(text, Custom())!;

        Assert.StartsWith("[3]{Id,Text=", text);
        Assert.Equal(1, text.Split('\n').Skip(1).First().Trim().Length);
        Assert.Equal(rows, decoded);
        Assert.All(decoded, row => Assert.Equal(sample.Utc.Kind, row.Utc.Kind));
        Assert.All(decoded, row => Assert.Equal(sample.Offset.Offset, row.Offset.Offset));
        Assert.Equal(ToonEncoder.Encode(rows, Encode(constants: false)), ToonEncoder.Encode(decoded, Encode(constants: false)));
    }

    [Fact]
    public void RoundTrip_JsonWithConstants_PreservesKeyOrderAndTypes()
    {
        const string json = "[{\"id\":1,\"kind\":\"A\",\"n\":1,\"s\":\"1\",\"b\":true,\"z\":null,\"e\":\"\",\"d\":1.5,\"g\":{\"x\":\"IS\",\"y\":1}}," +
            "{\"id\":2,\"kind\":\"A\",\"n\":1,\"s\":\"1\",\"b\":true,\"z\":null,\"e\":\"\",\"d\":1.5,\"g\":{\"x\":\"IS\",\"y\":2}}," +
            "{\"id\":3,\"kind\":\"A\",\"n\":1,\"s\":\"1\",\"b\":true,\"z\":null,\"e\":\"\",\"d\":1.5,\"g\":{\"x\":\"IS\",\"y\":3}}]";

        var text = ToonEncoder.Json2Toon(json, Encode());

        Assert.Equal("[3]{id,kind=A,n=1,s=\"1\",b=true,z=null,e=\"\",d=1.5,g{x=IS,y}}:\n  1,1\n  2,2\n  3,3", text);
        Assert.Equal(json, ToonDecoder.Toon2Json(text, Custom()));
    }

    [Fact]
    public void Encode_ClrAndNodePaths_ProduceIdenticalBytes()
    {
        AssertSamePaths(Accounts());
        AssertSamePaths(Enumerable.Range(1, 4).Select(i => Scalars.Sample() with { Id = i, Missing = i == 2 ? 5 : null }).ToArray());
        AssertSamePaths(Enumerable.Range(1, 3).Select(i => new Order { Id = i, Customer = new Customer { Country = "IS", Name = "n" + i } }).ToArray());
        AssertSamePaths(Enumerable.Range(1, 3).Select(i => new Noted { Id = i, Note = null, Tag = "", G = new NullLeaf { X = null, Y = 1 } }).ToArray());
        AssertSamePaths(Enumerable.Range(1, 3).Select(i => new { G = new { X = 1, Y = 2 }, Z = 3 }).ToArray());
        AssertSamePaths(Enumerable.Range(1, 3).Select(i => new Mixed { Id = i, Kind = "x|y,a=b", Name = "n" }).ToArray(), ToonDelimiter.PIPE);
        AssertSamePaths(Enumerable.Range(1, 3).Select(i => new Mixed { Id = i, Kind = "x\ty", Name = "n" }).ToArray(), ToonDelimiter.TAB);
        var ignoring = Encode();
        ignoring.IgnoreNullOrEmpty = true;
        var noted = Enumerable.Range(1, 3).Select(i => new Noted { Id = i, Note = null, Tag = "", G = new NullLeaf { X = null, Y = i } }).ToArray();
        Assert.Equal(ToonEncoder.Encode(noted.Select(row => row), ignoring), ToonEncoder.Encode(noted, ignoring));
    }

    [Fact]
    public void Encode_SameTokenRules_HoistOnlyWhenEveryCellTokenIsIdentical()
    {
        AssertHoistMatchesTokens(0.0, -0.0);
        AssertHoistMatchesTokens(double.NaN, double.NaN);
        AssertHoistMatchesTokens(0f, -0f);
        AssertHoistMatchesTokens(1.0m, 1.00m);
        AssertHoistMatchesTokens(1.5m, 1.50m);
        AssertHoistMatchesTokens("Name", "name");
        AssertHoistMatchesTokens(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified));
        AssertHoistMatchesTokens(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Local), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Local));
        AssertHoistMatchesTokens((DateTime?)new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), (DateTime?)new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Local));
        AssertHoistMatchesTokens(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 2, 1, 0, 0, TimeSpan.FromHours(1)));
        AssertHoistMatchesTokens((DateTimeOffset?)null, (DateTimeOffset?)new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        AssertHoistMatchesTokens(new Uri("https://example.com/a#x"), new Uri("https://example.com/a#y"));
        AssertHoistMatchesTokens(new Uri("https://example.com/a"), new Uri("https://example.com/a"));
        AssertHoistMatchesTokens(new Version(1, 2), new Version(1, 2, 0));
        AssertHoistMatchesTokens(LineKind.Credit, LineKind.Debit);
        AssertHoistMatchesTokens((LineKind?)LineKind.Credit, (LineKind?)LineKind.Credit);
        AssertHoistMatchesTokens((int?)null, (int?)0);
        AssertHoistMatchesTokens(TimeSpan.FromHours(1), TimeSpan.FromMinutes(60));
        AssertHoistMatchesTokens('a', 'A');
        AssertHoistMatchesTokens(long.MaxValue, long.MaxValue);
    }

    [Fact]
    public void Encode_PolymorphicRows_FallsBackAndRoundTrips()
    {
        var rows = new List<BaseRow> { new() { Id = 1, Kind = "A" }, new DerivedRow { Id = 2, Kind = "A", Extra = "e" }, new() { Id = 3, Kind = "A" } };

        var text = ToonEncoder.Encode(rows, Encode());

        Assert.Equal("[3]{Id,Kind=A}:\n  1\n  2\n    Extra: e\n  3", text);
        Assert.Equal(ToonDecoder.Toon2Json(ToonEncoder.Encode(rows, Encode(constants: false)), Custom()), ToonDecoder.Toon2Json(text, Custom()));
    }

    [Fact]
    public void Encode_ConcurrentFirstUseWithAndWithoutConstants_ProducesStableOutput()
    {
        var rows = Enumerable.Range(1, 3).Select(i => new FirstUseRow { Id = i, Kind = "K", Child = new FirstUseChild { Code = 7, Name = "c" + i } }).ToArray();
        var on = Encode();
        var off = Encode(constants: false);
        var results = new string[64];

        Parallel.For(0, results.Length, i => results[i] = ToonEncoder.Encode(rows, i % 2 == 0 ? on : off));

        for (var i = 0; i < results.Length; i++)
            Assert.Equal(i % 2 == 0
                ? "[3]{Id,Kind=K,Child{Code=7,Name}}:\n  1,c1\n  2,c2\n  3,c3"
                : "[3]{Id,Kind,Child{Code,Name}}:\n  1,K,7,c1\n  2,K,7,c2\n  3,K,7,c3", results[i]);
    }

    private static void AssertSamePaths<T>(T[] rows, ToonDelimiter delimiter = ToonDelimiter.COMMA)
    {
        var options = Encode();
        options.Delimiter = delimiter;
        var clr = ToonEncoder.Encode(rows, options);
        Assert.Equal(ToonEncoder.Encode(rows.Select(row => row), options), clr);
        Assert.Equal(clr, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(rows, options)));
        Assert.Contains("=", clr.Split('\n')[0]);
    }

    private static void AssertHoistMatchesTokens<T>(T first, T second)
    {
        var rows = new[] { new Holder<T> { Id = 1, V = first }, new Holder<T> { Id = 2, V = second }, new Holder<T> { Id = 3, V = first } };
        var cells = ToonEncoder.Encode(rows, Encode(constants: false)).Split('\n').Skip(1).Select(line => line.Substring(line.IndexOf(',') + 1)).ToArray();
        var identical = cells.Distinct(StringComparer.Ordinal).Count() == 1;

        foreach (var text in new[] { ToonEncoder.Encode(rows, Encode()), ToonEncoder.Encode(rows.Select(row => row), Encode()) })
        {
            Assert.True(identical == text.StartsWith("[3]{Id,V=", StringComparison.Ordinal), $"{typeof(T).Name}: {text}");
            Assert.Equal(ToonDecoder.Toon2Json(string.Join("\n", ToonEncoder.Encode(rows, Encode(constants: false))), Custom()), ToonDecoder.Toon2Json(text, Custom()));
        }
    }

    public sealed class Holder<T>
    {
        public int Id { get; set; }
        public T V { get; set; } = default!;
    }

    public class BaseRow
    {
        public int Id { get; set; }
        public string? Kind { get; set; }
    }

    public sealed class DerivedRow : BaseRow
    {
        public string? Extra { get; set; }
    }

    public sealed class FirstUseChild
    {
        public int Code { get; set; }
        public string? Name { get; set; }
    }

    public sealed class FirstUseRow
    {
        public int Id { get; set; }
        public string? Kind { get; set; }
        public FirstUseChild? Child { get; set; }
    }

    private const string AccountsJson =
        "[{\"CompanyId\":\"acme\",\"IsSystem\":false,\"CreatedBy\":\"system\",\"AccountCode\":6900,\"Name\":\"Other Expenses\",\"AccountType\":\"Expense\"}," +
        "{\"CompanyId\":\"acme\",\"IsSystem\":false,\"CreatedBy\":\"system\",\"AccountCode\":1000,\"Name\":\"Cash\",\"AccountType\":\"Asset\"}," +
        "{\"CompanyId\":\"acme\",\"IsSystem\":false,\"CreatedBy\":\"system\",\"AccountCode\":2000,\"Name\":\"Payables\",\"AccountType\":\"Liability\"}]";

    private static Account[] Accounts() =>
    [
        new() { CompanyId = "acme", IsSystem = false, CreatedBy = "system", AccountCode = 6900, Name = "Other Expenses", AccountType = "Expense" },
        new() { CompanyId = "acme", IsSystem = false, CreatedBy = "system", AccountCode = 1000, Name = "Cash", AccountType = "Asset" },
        new() { CompanyId = "acme", IsSystem = false, CreatedBy = "system", AccountCode = 2000, Name = "Payables", AccountType = "Liability" }
    ];

    private static string Describe(Account account)
        => $"{account.CompanyId}|{account.IsSystem}|{account.CreatedBy}|{account.AccountCode}|{account.Name}|{account.AccountType}";

    private static void AssertCellsRoundTrip<T>(Stamped[] rows, string column, Func<Stamped, T> select, Func<T, T, bool> same)
    {
        var text = ToonEncoder.Encode(rows, Encode());
        Assert.DoesNotContain(column + "=", text);
        var decoded = ToonDecoder.Decode<List<Stamped>>(text, Custom())!;
        for (var i = 0; i < rows.Length; i++) Assert.True(same(select(rows[i]), select(decoded[i])), text);
    }

    internal static ToonEncodeOptions Encode(bool constants = true) => new()
    {
        ProtocolVersion = ToonProtocolVersion.CustomV4_4,
        Indent = 2,
        IgnoreNullOrEmpty = false,
        ExcludeEmptyArrays = false,
        ConstantColumns = constants
    };

    public sealed class Mixed
    {
        public int Id { get; set; }
        public string? Kind { get; set; }
        public string? Name { get; set; }
    }

    public sealed class NullLeaf
    {
        public string? X { get; set; }
        public int Y { get; set; }
    }

    public sealed class Noted
    {
        public int Id { get; set; }
        public string? Note { get; set; }
        public string? Tag { get; set; }
        public NullLeaf? G { get; set; }
    }

    public sealed class Stamped
    {
        public int Id { get; set; }
        public DateTime At { get; set; }
        public DateTimeOffset When { get; set; }
        public Uri? Link { get; set; }
    }

    public sealed record Scalars(
        int Id, string Text, bool Flag, char Letter, byte U8, sbyte S8, short S16, ushort U16, int S32, uint U32, long S64, ulong U64,
        decimal Money, double Ratio, float Single, Guid Key, TimeSpan Span, Version Ver, Uri Link, LineKind Kind,
        DateTime Utc, DateTime Local, DateTimeOffset Offset, DateOnly Day, TimeOnly Time, int? Missing, string? Absent)
    {
        public static Scalars Sample() => new(
            0, "Other Expenses", true, 'x', 200, -5, -300, 60000, -70000, 4000000000, -9000000000, 18000000000000000000,
            1234.5600m, 0.1, 1.5f, new Guid("8f1e1f9e-2f7d-4a52-9f0e-0f5f3f8c2b11"), TimeSpan.FromMinutes(90), new Version(1, 2, 3), new Uri("https://example.com/a?b=c#d"), LineKind.Credit,
            new DateTime(2026, 1, 2, 17, 3, 15, DateTimeKind.Utc), new DateTime(2026, 1, 2, 17, 3, 15, DateTimeKind.Unspecified), new DateTimeOffset(2026, 1, 2, 17, 3, 15, TimeSpan.FromHours(2)),
            new DateOnly(2026, 1, 2), new TimeOnly(17, 3, 15), null, null);
    }

    internal static ToonDecodeOptions Custom(bool strict = true)
        => new() { ProtocolVersion = ToonProtocolVersion.CustomV4_4, Indent = 2, Strict = strict };

    internal static void AssertJson(string expected, string actual)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), actual);

    public sealed class Account
    {
        public string? CompanyId { get; set; }
        public bool IsSystem { get; set; }
        public string? CreatedBy { get; set; }
        public int AccountCode { get; set; }
        public string? Name { get; set; }
        public string? AccountType { get; set; }
    }

    public sealed class ExtendedAccount
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class Customer
    {
        public string? Country { get; set; }
        public string? Name { get; set; }
    }

    public sealed class Order
    {
        public int Id { get; set; }
        public Customer? Customer { get; set; }
    }

    public sealed class Tagged
    {
        public int Id { get; set; }
        public string? Tag { get; set; }
    }

    public enum LineKind
    {
        Debit = 1,
        Credit = 2
    }

    public struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed record Line(int Id, LineKind Kind, int? Qty, DateTime Created, DateTimeOffset At, decimal Price, Point Pos);

    public sealed class IntCode
    {
        public int Id { get; set; }
        public int Code { get; set; }
    }

    public sealed class DoubleCode
    {
        public long Id { get; set; }
        public double Code { get; set; }
    }

    public sealed class CodeDocument
    {
        public List<IntCode>? First { get; set; }
        public List<DoubleCode>? Second { get; set; }
        public List<IntCode>? Third { get; set; }
    }
}
#endif
