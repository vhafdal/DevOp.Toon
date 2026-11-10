using DevOp.Toon.Core;
using Xunit;

namespace DevOp.Toon.Tests;

public sealed class AnnotationKeyTests
{
    private const string AccountClrType = "Contoso.Billing.Account, Contoso.Billing";

    [Fact]
    public void Encode_CustomV44_AnnotationKeys_Unquoted()
    {
        var text = ToonEncoder.Encode(Account(), Encode(ToonProtocolVersion.CustomV4_4));

        Assert.Equal(
            "@id: accounts/6900\n" +
            "@type: Account\n" +
            "Name: Other Expenses\n" +
            "@metadata:\n" +
            "  @collection: Accounts\n" +
            "  Raven-Clr-Type: \"" + AccountClrType + "\"\n" +
            "  @flags: HasRevisions",
            text);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.V3_0)]
    public void Encode_OtherProfiles_AnnotationKeys_Quoted(ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(Account(), Encode(version));

        Assert.Contains("\"@id\": accounts/6900", text);
        Assert.Contains("\"@metadata\":", text);
        Assert.Contains("\"Raven-Clr-Type\":", text);
    }

    [Fact]
    public void Decode_CustomV44_AnnotationKeys_RoundTripToNode()
    {
        var text = ToonEncoder.Encode(Account(), Encode(ToonProtocolVersion.CustomV4_4));

        var node = ToonDecoder.Decode(text, Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("accounts/6900", node["@id"]!.GetValue<string>());
        Assert.Equal("Accounts", node["@metadata"]!["@collection"]!.GetValue<string>());
        Assert.Equal(AccountClrType, node["@metadata"]!["Raven-Clr-Type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    [InlineData(ToonProtocolVersion.Auto)]
    public void Decode_AnnotationKeys_RoundTripToDto(ToonProtocolVersion decodeVersion)
    {
        var source = new AccountDocument
        {
            Id = "accounts/6900",
            Name = "Other Expenses",
            Metadata = new AccountMetadata { Collection = "Accounts", ClrType = AccountClrType, Flags = "HasRevisions" }
        };
        var text = ToonEncoder.Encode(source, Encode(ToonProtocolVersion.CustomV4_4));
        Assert.StartsWith("@id: accounts/6900\n", text);

        var decoded = ToonDecoder.Decode<AccountDocument>(text, Decode(decodeVersion))!;

        Assert.Equal(source.Id, decoded.Id);
        Assert.Equal(source.Name, decoded.Name);
        Assert.Equal("Accounts", decoded.Metadata!.Collection);
        Assert.Equal(AccountClrType, decoded.Metadata.ClrType);
        Assert.Equal("HasRevisions", decoded.Metadata.Flags);
    }

    [Fact]
    public void Decode_CustomV44_AnnotationKeys_RoundTripToDictionary()
    {
        var text = ToonEncoder.Encode(Account(), Encode(ToonProtocolVersion.CustomV4_4));

        var decoded = ToonDecoder.Decode<Dictionary<string, object?>>(text, Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal(["@id", "@type", "Name", "@metadata"], decoded.Keys);
    }

    [Fact]
    public void Decode_CustomV44_ODataAnnotationKeys_StaySingleKeys()
    {
        var source = new Dictionary<string, object?>
        {
            ["@odata.context"] = "https://api.example/$metadata#Accounts",
            ["Name@odata.type"] = "Edm.String",
            ["Name"] = "Other Expenses"
        };
        var text = ToonEncoder.Encode(source, Encode(ToonProtocolVersion.CustomV4_4));
        Assert.Equal("@odata.context: \"https://api.example/$metadata#Accounts\"\nName@odata.type: Edm.String\nName: Other Expenses", text);

        var node = ToonDecoder.Decode(text, Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("https://api.example/$metadata#Accounts", node["@odata.context"]!.GetValue<string>());
        Assert.Equal("Edm.String", node["Name@odata.type"]!.GetValue<string>());
        Assert.Null(node["@odata"]);
    }

    [Fact]
    public void Encode_CustomV44_AnnotationKeysInTableHeaderAndArrayKey_RoundTrip()
    {
        var source = new Dictionary<string, object?>
        {
            ["@tags"] = new[] { "a", "b" },
            ["Rows"] = new[]
            {
                new Dictionary<string, object?> { ["@id"] = 1, ["@flags"] = "x" },
                new Dictionary<string, object?> { ["@id"] = 2, ["@flags"] = "y" }
            }
        };
        var text = ToonEncoder.Encode(source, Encode(ToonProtocolVersion.CustomV4_4));
        Assert.Equal("@tags[2]: a,b\nRows[2]{@id,@flags}:\n  1,x\n  2,y", text);

        var node = ToonDecoder.Decode(text, Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("b", node["@tags"]![1]!.GetValue<string>());
        Assert.Equal(2, node["Rows"]![1]!["@id"]!.GetValue<int>());
        Assert.Equal("y", node["Rows"]![1]!["@flags"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("-x", "\"-x\": 1")]
    [InlineData("@a b", "\"@a b\": 1")]
    [InlineData("@a:b", "\"@a:b\": 1")]
    [InlineData("a,b", "\"a,b\": 1")]
    [InlineData("@a[0]", "\"@a[0]\": 1")]
    [InlineData("", "\"\": 1")]
    [InlineData("@", "@: 1")]
    [InlineData("a-b", "a-b: 1")]
    [InlineData("$id", "$id: 1")]
    [InlineData("$ref", "$ref: 1")]
    [InlineData("$schema", "$schema: 1")]
    [InlineData("a$b", "a$b: 1")]
    [InlineData("-$x", "\"-$x\": 1")]
    public void Encode_CustomV44_KeyQuoting(string key, string expected)
    {
        var options = Encode(ToonProtocolVersion.CustomV4_4);

        var text = ToonEncoder.Encode(new Dictionary<string, object?> { [key] = 1 }, options);

        Assert.Equal(expected, text);
        var node = ToonDecoder.Decode(text, Decode(ToonProtocolVersion.CustomV4_4))!;
        Assert.Equal(1, node[key]!.GetValue<int>());
    }

    [Fact]
    public void Encode_CustomV44_ReferencePreservationKeys_RoundTrip()
    {
        var source = new Dictionary<string, object?>
        {
            ["$id"] = "1",
            ["$values"] = new[] { "a", "b" },
            ["Owner"] = new Dictionary<string, object?> { ["$ref"] = "1" }
        };
        var text = ToonEncoder.Encode(source, Encode(ToonProtocolVersion.CustomV4_4));
        Assert.Equal("$id: \"1\"\n$values[2]: a,b\nOwner:\n  $ref: \"1\"", text);

        var node = ToonDecoder.Decode(text, Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("1", node["Owner"]!["$ref"]!.GetValue<string>());
        Assert.Equal("b", node["$values"]![1]!.GetValue<string>());
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.V3_0)]
    public void Encode_OfficialProfiles_DollarKeys_Quoted(ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(new Dictionary<string, object?> { ["$id"] = 1 }, Encode(version));

        Assert.Equal("\"$id\": 1", text);
    }

    private static Dictionary<string, object?> Account() => new()
    {
        ["@id"] = "accounts/6900",
        ["@type"] = "Account",
        ["Name"] = "Other Expenses",
        ["@metadata"] = new Dictionary<string, object?>
        {
            ["@collection"] = "Accounts",
            ["Raven-Clr-Type"] = AccountClrType,
            ["@flags"] = "HasRevisions"
        }
    };

    private static ToonEncodeOptions Encode(ToonProtocolVersion version) => new()
    {
        ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false
    };

    private static ToonDecodeOptions Decode(ToonProtocolVersion version) => new()
    {
        ProtocolVersion = version, Indent = 2
    };

    public sealed class AccountDocument
    {
        [ToonPropertyName("@id")]
        public string? Id { get; set; }

        public string? Name { get; set; }

        [ToonPropertyName("@metadata")]
        public AccountMetadata? Metadata { get; set; }
    }

    public sealed class AccountMetadata
    {
        [ToonPropertyName("@collection")]
        public string? Collection { get; set; }

        [ToonPropertyName("Raven-Clr-Type")]
        public string? ClrType { get; set; }

        [ToonPropertyName("@flags")]
        public string? Flags { get; set; }
    }
}
