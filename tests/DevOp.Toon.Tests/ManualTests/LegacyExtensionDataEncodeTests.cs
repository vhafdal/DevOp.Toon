using System.Text;
using static DevOp.Toon.Tests.ExtensionDataTests;

namespace DevOp.Toon.Tests;

public sealed class LegacyExtensionDataEncodeTests
{
    private const string FlatAccount = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts";

    public static TheoryData<ToonProtocolVersion> LegacyVersions => new() { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy };

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_Legacy_EntriesAfterMembersQuoted(ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(SampleAccount(), Encode(version));

        Assert.Equal(FlatAccount, text);
        Assert.Equal(ToonEncoder.Encode(AsDictionary(SampleAccount()), Encode(version)), text);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyUniformEntries_FormTable(ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(UniformAccounts(), Encode(version));
        var array = ToonEncoder.Encode(UniformAccounts().ToArray(), Encode(version));

        Assert.Equal("[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2", text);
        Assert.Equal(text, array);
    }

    [Fact]
    public void Encode_V3IrregularEntries_ListForm()
    {
        var text = ToonEncoder.Encode(IrregularAccounts(), Encode(ToonProtocolVersion.V3_0));

        Assert.Equal("[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - Name: Bank\n    \"@id\": a/2", text);
        Assert.Equal(ToonEncoder.Encode(IrregularAccounts().Select(AsDictionary).ToList(), Encode(ToonProtocolVersion.V3_0)), text);
    }

    [Fact]
    public void Encode_CustomLegacyIrregularEntries_ContinuationUnderRow()
    {
        var text = ToonEncoder.Encode(IrregularAccounts(), Encode(ToonProtocolVersion.CustomLegacy));

        Assert.Equal("[2]{Name,\"@id\"}:\n  Cash,a/1\n    \"@flags\": x\n  Bank,a/2", text);
        Assert.Equal(ToonEncoder.Encode(IrregularAccounts().Select(AsDictionary).ToList(), Encode(ToonProtocolVersion.CustomLegacy)), text);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyEntryCollidingWithMemberWireName_MemberWins(ToonProtocolVersion version)
    {
        var source = new Account { Name = "Cash", Annotations = new Dictionary<string, object?> { ["Name"] = "Other", ["@id"] = "a/1" } };

        var text = ToonEncoder.Encode(source, Encode(version));

        Assert.Equal("Name: Cash\n\"@id\": a/1", text);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyEntryCollidingWithAliasedMemberClrName_Skipped(ToonProtocolVersion version)
    {
        var source = new AliasedAccount
        {
            Name = "Cash", Code = "A1", Annotations = new Dictionary<string, object?> { ["Code"] = "X", ["acct"] = "Y", ["@id"] = "a/1" }
        };

        var text = ToonEncoder.Encode(source, Encode(version));

        Assert.Equal("Name: Cash\nacct: A1\n\"@id\": a/1", text);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyEntryNamingConstructorParameter_Skipped(ToonProtocolVersion version)
    {
        var source = new ParameterAccount("Cash") { Annotations = new Dictionary<string, object?> { ["code"] = "dropped", ["@id"] = "a/1" } };

        var text = ToonEncoder.Encode(source, Encode(version));
        var decoded = ToonDecoder.Decode<ParameterAccount>(text, Decode(version))!;

        Assert.Equal("Name: Cash\n\"@id\": a/1", text);
        Assert.Equal(["@id"], decoded.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void RoundTrip_LegacyConstructorBoundCaseVariant_MemberWins(ToonProtocolVersion version)
    {
        var record = new AccountRecord("declared", new Dictionary<string, object?> { ["name"] = "dropped1", ["NAME"] = "dropped2", ["@id"] = "a/1" });
        var aliased = new AliasedAccountRecord("A1", "Cash", new Dictionary<string, object?>
        {
            ["ACCT"] = "dropped1", ["Acct"] = "dropped2", ["code"] = "dropped3", ["@id"] = "a/1"
        });

        var recordText = ToonEncoder.Encode(record, Encode(version));
        var aliasedText = ToonEncoder.Encode(aliased, Encode(version));
        var decodedRecord = ToonDecoder.Decode<AccountRecord>(recordText, Decode(version))!;
        var decodedAliased = ToonDecoder.Decode<AliasedAccountRecord>(aliasedText, Decode(version))!;

        Assert.DoesNotContain("dropped", recordText);
        Assert.DoesNotContain("dropped", aliasedText);
        Assert.Equal("declared", decodedRecord.Name);
        Assert.Equal(["@id"], decodedRecord.Annotations!.Keys);
        Assert.Equal("A1", decodedAliased.Code);
        Assert.Equal(["@id"], decodedAliased.Annotations!.Keys);
    }

    [Fact]
    public void Encode_CustomLegacyDefaults_OmitsNullEmptyStringAndEmptyArrayEntries()
    {
        var text = ToonEncoder.Encode(SparseAccount(), new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.CustomLegacy });

        Assert.Equal("Name: Cash\n\"@meta\":\nKind: asset", text);
    }

    [Fact]
    public void Encode_V3Defaults_KeepsNullAndEmptyEntries()
    {
        var text = ToonEncoder.Encode(SparseAccount(), new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V3_0 });

        Assert.Equal("Name: Cash\n\"@note\": null\n\"@empty\": \"\"\nTags[0]:\n\"@meta\":\nKind: asset", text);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyNullOrEmptyExtensionData_MatchesTypeWithoutAttribute(ToonProtocolVersion version)
    {
        foreach (var annotations in new[] { null, new Dictionary<string, object?>() })
        {
            var (rich, plain) = TwinLedgers(annotations);
            foreach (var options in new[] { Encode(version), new ToonEncodeOptions { ProtocolVersion = version } })
            {
                Assert.Equal(ToonEncoder.Encode(plain.Primary, options), ToonEncoder.Encode(rich.Primary, options));
                Assert.Equal(ToonEncoder.Encode(plain.Accounts, options), ToonEncoder.Encode(rich.Accounts, options));
                Assert.Equal(ToonEncoder.Encode(plain, options), ToonEncoder.Encode(rich, options));
            }
        }
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyNestedUnderUnattributedRoot_Flattened(ToonProtocolVersion version)
    {
        var options = Encode(version);
        var ledger = new Ledger { Title = "Main", Primary = SampleAccount(), Accounts = UniformAccounts() };
        var holder = new Holder { Id = 1, Value = SampleAccount(), Map = new Dictionary<string, Account> { ["main"] = SampleAccount() } };
        var rows = new List<AccountRow> { new() { Id = 1, Account = SampleAccount() }, new() { Id = 2, Account = UniformAccounts()[0] } };

        var ledgerText = ToonEncoder.Encode(ledger, options);
        var holderText = ToonEncoder.Encode(holder, options);
        var rowsText = ToonEncoder.Encode(rows, options);

        var expectedLedger = "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2";
        Assert.Equal(expectedLedger, ledgerText);
        var flatAccount = AsDictionary(SampleAccount());
        Assert.Equal(ToonEncoder.Encode(new Dictionary<string, object?> { ["Id"] = 1, ["Value"] = flatAccount, ["Map"] = new Dictionary<string, object?> { ["main"] = flatAccount } }, options), holderText);
        Assert.Equal(ToonEncoder.Encode(new List<Dictionary<string, object?>>
        {
            new() { ["Id"] = 1, ["Account"] = flatAccount },
            new() { ["Id"] = 2, ["Account"] = AsDictionary(UniformAccounts()[0]) }
        }, options), rowsText);
        Assert.Contains("\"@id\": accounts/6900", holderText);
        Assert.DoesNotContain("Annotations", ledgerText + holderText + rowsText);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyNestedAttributedWithByteArray_UsesByteArrayFormat(ToonProtocolVersion version)
    {
        var holder = new BytesHolder { Id = 1, Item = new BytesAccount { Name = "a", Data = [1, 2, 3], Annotations = new Dictionary<string, object?> { ["@id"] = "a/1" } } };

        var base64 = ToonEncoder.Encode(holder, Encode(version));
        var numeric = ToonEncoder.Encode(holder, new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, ByteArrayFormat = ToonByteArrayFormat.NumericArray });

        Assert.Equal("Id: 1\nItem:\n  Name: a\n  Data: AQID\n  \"@id\": a/1", base64);
        Assert.Equal("Id: 1\nItem:\n  Name: a\n  Data[3]: 1,2,3\n  \"@id\": a/1", numeric);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyBytesAndStream_MatchString(ToonProtocolVersion version)
    {
        var values = new object[]
        {
            SampleAccount(), UniformAccounts(), IrregularAccounts(), new Ledger { Title = "Main", Primary = SampleAccount(), Accounts = UniformAccounts() },
            new Holder { Id = 1, Value = SampleAccount(), Map = new Dictionary<string, Account> { ["main"] = SampleAccount() } }
        };
        foreach (var options in new[] { Encode(version), new ToonEncodeOptions { ProtocolVersion = version } })
        {
            foreach (var value in values)
            {
                var text = ToonEncoder.Encode(value, options);
                using var stream = new MemoryStream();
                ToonEncoder.EncodeToStream(value, stream, options);

                Assert.Equal(text, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(value, options)));
                Assert.Equal(text, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Encode_LegacyKeyFoldingSafe_FoldsEntryChains(ToonProtocolVersion version)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, KeyFolding = ToonKeyFolding.Safe };
        var account = new Account
        {
            Name = "Cash",
            Annotations = new Dictionary<string, object?>
            {
                ["meta"] = new Dictionary<string, object?> { ["collection"] = "Accounts" },
                ["a"] = new Dictionary<string, object?> { ["b"] = 1 }
            }
        };
        var holder = new Holder { Id = 1, Value = account };

        var text = ToonEncoder.Encode(account, options);
        var nested = ToonEncoder.Encode(new Ledger { Title = "Main", Primary = account }, options);

        Assert.Equal("Name: Cash\nmeta.collection: Accounts\na.b: 1", text);
        Assert.Equal(ToonEncoder.Encode(new Dictionary<string, object?> { ["Title"] = "Main", ["Primary"] = AsDictionary(account), ["Accounts"] = null }, options), nested);
        Assert.Equal(ToonEncoder.Encode(new Dictionary<string, object?> { ["Id"] = 1, ["Value"] = AsDictionary(account), ["Map"] = null }, options), ToonEncoder.Encode(holder, options));
    }

    [Theory]
    [InlineData("two")]
    [InlineData("inherited")]
    [InlineData("string-dictionary")]
    [InlineData("read-only-dictionary")]
    [InlineData("node")]
    [InlineData("object")]
    [InlineData("indexer")]
    public void Encode_LegacyInvalidExtensionProperty_Throws(string kind)
    {
        object value = kind switch
        {
            "two" => new TwoExtensions { Name = "Cash" },
            "inherited" => new DerivedWithExtension { Name = "Cash" },
            "string-dictionary" => new StringDictionaryExtension { Name = "Cash" },
            "read-only-dictionary" => new ReadOnlyDictionaryExtension { Name = "Cash" },
            "node" => new NodeExtension { Name = "Cash" },
            "object" => new ObjectExtension { Name = "Cash" },
            _ => new IndexerExtension { Name = "Cash" }
        };

        foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy })
        {
            Assert.Throws<NotSupportedException>(() => ToonEncoder.Encode(value, Encode(version)));
            Assert.Throws<NotSupportedException>(() => ToonEncoder.Encode(new[] { value, value }, Encode(version)));
            Assert.Throws<NotSupportedException>(() => ToonEncoder.Encode(new Holder { Id = 1, Value = value }, Encode(version)));
        }
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void RoundTrip_LegacyQuotingSensitiveEntries_Preserved(ToonProtocolVersion version)
    {
        var entries = new Dictionary<string, object?>
        {
            ["$type"] = "Acc", ["a.b"] = 1d, ["x-y"] = "true", ["n"] = "42", ["s"] = "a,b", ["k:v"] = "null",
            ["e"] = "x y", ["-dash"] = "lead", ["#"] = 1.5d, ["quote\"d"] = "say \"hi\"", ["dec"] = 1.1d, ["flag"] = false,
            ["list"] = new List<object?> { "a", 2d, null }
        };
        var source = new Account { Name = "Cash", Annotations = entries };

        var text = ToonEncoder.Encode(source, Encode(version));
        var decoded = ToonDecoder.Decode<Account>(text, Decode(version))!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal(entries.Keys, decoded.Annotations!.Keys);
        foreach (var entry in entries) Assert.Equal(entry.Value, decoded.Annotations[entry.Key]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void RoundTrip_LegacyDocumentWithMetadata_PreservesValues(ToonProtocolVersion version)
    {
        var source = new BillingAccount
        {
            Name = "Other Expenses",
            Balance = 1250.5m,
            Annotations = new Dictionary<string, object?>
            {
                ["@id"] = "accounts/6900",
                ["@metadata"] = new Dictionary<string, object?>
                {
                    ["@collection"] = "Accounts",
                    ["@change-vector"] = "A:12-Qx1bT0",
                    ["Raven-Clr-Type"] = "Contoso.Billing.Account, Contoso.Billing",
                    ["@last-modified"] = "2026-11-10T08:00:00.0000000Z"
                }
            }
        };

        foreach (var options in new[] { Encode(version), new ToonEncodeOptions { ProtocolVersion = version } })
        {
            var text = ToonEncoder.Encode(source, options);
            var decoded = ToonDecoder.Decode<BillingAccount>(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = options.Indent })!;
            var list = ToonDecoder.Decode<List<BillingAccount>>(ToonEncoder.Encode(new[] { source, source }, options), new ToonDecodeOptions { ProtocolVersion = version, Indent = options.Indent })!;

            Assert.Equal(source.Name, decoded.Name);
            Assert.Equal(source.Balance, decoded.Balance);
            Assert.Equal(["@id", "@metadata"], decoded.Annotations!.Keys);
            var expectedMetadata = (Dictionary<string, object?>)source.Annotations["@metadata"]!;
            var metadata = Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["@metadata"]);
            Assert.Equal(expectedMetadata, metadata);
            Assert.Equal(2, list.Count);
            Assert.Equal(expectedMetadata, Assert.IsType<Dictionary<string, object?>>(list[1].Annotations!["@metadata"]));
        }
    }

    private static Account SampleAccount() => new()
    {
        Name = "Other Expenses",
        Annotations = new Dictionary<string, object?>
        {
            ["@id"] = "accounts/6900",
            ["@metadata"] = new Dictionary<string, object?> { ["@collection"] = "Accounts" }
        }
    };

    private static Account SparseAccount() => new()
    {
        Name = "Cash",
        Annotations = new Dictionary<string, object?>
        {
            ["@note"] = null, ["@empty"] = "", ["Tags"] = new List<object?>(), ["@meta"] = new Dictionary<string, object?>(), ["Kind"] = "asset"
        }
    };

    private static List<Account> UniformAccounts() =>
    [
        new() { Name = "Cash", Annotations = new Dictionary<string, object?> { ["@id"] = "a/1" } },
        new() { Name = "Bank", Annotations = new Dictionary<string, object?> { ["@id"] = "a/2" } }
    ];

    private static List<Account> IrregularAccounts() =>
    [
        new() { Name = "Cash", Annotations = new Dictionary<string, object?> { ["@id"] = "a/1", ["@flags"] = "x" } },
        new() { Name = "Bank", Annotations = new Dictionary<string, object?> { ["@id"] = "a/2" } }
    ];

    private static Dictionary<string, object?> AsDictionary(Account account)
    {
        var result = new Dictionary<string, object?> { ["Name"] = account.Name };
        foreach (var entry in account.Annotations ?? new Dictionary<string, object?>()) result[entry.Key] = entry.Value;
        return result;
    }

    private static (RichLedger Rich, PlainLedger Plain) TwinLedgers(Dictionary<string, object?>? annotations)
    {
        RichAccount Rich(string name, int number) => new()
        {
            Name = name, Number = number, Balance = number * 1.25m, Active = number % 2 == 0, Note = null,
            Address = new Address { City = "Reykjavik", Zip = "101" }, Tags = ["a", "b"], Annotations = annotations
        };
        PlainAccount Plain(string name, int number) => new()
        {
            Name = name, Number = number, Balance = number * 1.25m, Active = number % 2 == 0, Note = null,
            Address = new Address { City = "Reykjavik", Zip = "101" }, Tags = ["a", "b"]
        };
        var rich = new RichLedger { Title = "Main", Primary = Rich("Cash", 1000), Accounts = [Rich("Cash", 1000), Rich("Bank", 1011)] };
        var plain = new PlainLedger { Title = "Main", Primary = Plain("Cash", 1000), Accounts = [Plain("Cash", 1000), Plain("Bank", 1011)] };
        return (rich, plain);
    }

    private static ToonEncodeOptions Encode(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    private static ToonDecodeOptions Decode(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    public sealed class Holder
    {
        public int Id { get; set; }
        public object? Value { get; set; }
        public Dictionary<string, Account>? Map { get; set; }
    }

    public sealed class AccountRow
    {
        public int Id { get; set; }
        public Account? Account { get; set; }
    }

    public sealed class BytesAccount
    {
        public string? Name { get; set; }
        public byte[]? Data { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class BytesHolder
    {
        public int Id { get; set; }
        public BytesAccount? Item { get; set; }
    }

    public sealed class ParameterAccount
    {
        public ParameterAccount(string name, string? code = null)
        {
            Name = name;
            Code = code;
        }

        public string Name { get; }

        internal string? Code { get; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }
}
