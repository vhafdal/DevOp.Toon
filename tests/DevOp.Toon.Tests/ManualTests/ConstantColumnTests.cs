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
        const string source = "[3]{CompanyId=vhafdal,IsSystem=false,AccountCode,Name}:\n  6900,Other Expenses\n  6910,Rent\n  6920,Power";

        var rows = ToonDecoder.Decode<List<Account>>(source, Custom())!;

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal("vhafdal", row.CompanyId));
        Assert.All(rows, row => Assert.False(row.IsSystem));
        Assert.Equal([6900, 6910, 6920], rows.Select(row => row.AccountCode));
        Assert.Equal(["Other Expenses", "Rent", "Power"], rows.Select(row => row.Name));
        const string expected =
            "[{\"CompanyId\":\"vhafdal\",\"IsSystem\":false,\"AccountCode\":6900,\"Name\":\"Other Expenses\"}," +
            "{\"CompanyId\":\"vhafdal\",\"IsSystem\":false,\"AccountCode\":6910,\"Name\":\"Rent\"}," +
            "{\"CompanyId\":\"vhafdal\",\"IsSystem\":false,\"AccountCode\":6920,\"Name\":\"Power\"}]";
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
