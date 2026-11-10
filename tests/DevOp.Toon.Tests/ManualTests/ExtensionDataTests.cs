using System.Text;
using DevOp.Toon.Core;
using Xunit;

namespace DevOp.Toon.Tests;

public sealed class ExtensionDataTests
{
    private const string CustomAccount = "Name: Other Expenses\n@id: accounts/6900\n@metadata:\n  @collection: Accounts";
    private const string OfficialAccount = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts";

    public static TheoryData<ToonProtocolVersion> V4Versions => new()
    {
        ToonProtocolVersion.V4_0, ToonProtocolVersion.V4_1, ToonProtocolVersion.V4_2,
        ToonProtocolVersion.V4_3, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [InlineData(ToonProtocolVersion.V4_4, OfficialAccount)]
    [InlineData(ToonProtocolVersion.CustomV4_4, CustomAccount)]
    [InlineData(ToonProtocolVersion.Auto, CustomAccount)]
    [InlineData(ToonProtocolVersion.Auto, OfficialAccount)]
    public void Decode_UnmatchedKeys_Captured(ToonProtocolVersion version, string text)
    {
        var decoded = ToonDecoder.Decode<Account>(text, Decode(version))!;

        Assert.Equal("Other Expenses", decoded.Name);
        Assert.NotNull(decoded.Annotations);
        Assert.Equal(["@id", "@metadata"], decoded.Annotations!.Keys);
        Assert.Equal("accounts/6900", decoded.Annotations["@id"]);
        var metadata = Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["@metadata"]);
        Assert.Equal("Accounts", metadata["@collection"]);
    }

    [Fact]
    public void Decode_NestedObjectAndArray_CapturedAsDictionaryAndList()
    {
        const string text = "Name: Cash\nExtra:\n  A: 1\n  B: x\nTags[2]: a,b\nRows[2]{K,V}:\n  a,1\n  b,2\nEmpty: []\nFlag: true\nNothing: null";

        var annotations = ToonDecoder.Decode<Account>(text, Decode(ToonProtocolVersion.CustomV4_4))!.Annotations!;

        var extra = Assert.IsType<Dictionary<string, object?>>(annotations["Extra"]);
        Assert.Equal(1d, extra["A"]);
        Assert.Equal("x", extra["B"]);
        var tags = Assert.IsType<List<object?>>(annotations["Tags"]);
        Assert.Equal(["a", "b"], tags);
        var rows = Assert.IsType<List<object?>>(annotations["Rows"]);
        var second = Assert.IsType<Dictionary<string, object?>>(rows[1]);
        Assert.Equal("b", second["K"]);
        Assert.Equal(2d, second["V"]);
        Assert.Empty(Assert.IsType<List<object?>>(annotations["Empty"]));
        Assert.True(Assert.IsType<bool>(annotations["Flag"]));
        Assert.True(annotations.ContainsKey("Nothing"));
        Assert.Null(annotations["Nothing"]);
    }

    [Fact]
    public void Decode_MatchedAndAliasedKeys_NotCaptured()
    {
        var byAlias = ToonDecoder.Decode<AliasedAccount>("Name: Cash\nacct: A1\nOther: 3", Decode(ToonProtocolVersion.CustomV4_4))!;
        var byClrName = ToonDecoder.Decode<AliasedAccount>("Name: Cash\nCode: A2\nOther: 3", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("Cash", byAlias.Name);
        Assert.Equal("A1", byAlias.Code);
        Assert.Equal(["Other"], byAlias.Annotations!.Keys);
        Assert.Equal("A2", byClrName.Code);
        Assert.Equal(["Other"], byClrName.Annotations!.Keys);
    }

    [Fact]
    public void Decode_NoUnmatchedKeys_PropertyStaysNull()
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Null(decoded.Annotations);
    }

    [Fact]
    public void Decode_InitialisedDictionary_EntriesAdded()
    {
        var decoded = ToonDecoder.Decode<SeededAccount>("Name: Cash\n@id: a/1", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("kept", decoded.Annotations["seed"]);
        Assert.Equal("a/1", decoded.Annotations["@id"]);
    }

    [Fact]
    public void Decode_NumericEntry_IsDouble()
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash\nCount: 3\nRate: 1.5", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.IsType<double>(decoded.Annotations!["Count"]);
        Assert.Equal(3d, decoded.Annotations["Count"]);
        Assert.Equal(1.5d, decoded.Annotations["Rate"]);
    }

    [Fact]
    public void Decode_StrictDuplicateUnmatchedKey_Throws()
    {
        Assert.Throws<ToonFormatException>(() =>
            ToonDecoder.Decode<Account>("Name: Cash\nExtra: 1\nExtra: 2", Decode(ToonProtocolVersion.CustomV4_4)));
    }

    [Fact]
    public void Decode_NonStrictDuplicateUnmatchedKey_LastWins()
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash\nExtra: 1\nExtra: 2", Decode(ToonProtocolVersion.CustomV4_4, strict: false))!;

        Assert.Equal(2d, decoded.Annotations!["Extra"]);
    }

    [Fact]
    public void Decode_MalformedTableUnderUnmatchedKey_Throws()
    {
        Assert.Throws<ToonFormatException>(() =>
            ToonDecoder.Decode<Account>("Name: Cash\nRows[2]{K,V}:\n  a,1", Decode(ToonProtocolVersion.CustomV4_4)));
    }

    [Fact]
    public void Decode_KeyNamedLikeExtensionProperty_CapturedAsEntry()
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash\nAnnotations: hello", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("hello", decoded.Annotations!["Annotations"]);
    }

    [Fact]
    public void Decode_PositionalRecord_Captures()
    {
        var decoded = ToonDecoder.Decode<AccountRecord>("Name: Cash\n@id: a/1", Decode(ToonProtocolVersion.CustomV4_4))!;
        var empty = ToonDecoder.Decode<AccountRecord>("Name: Cash", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal("a/1", decoded.Annotations!["@id"]);
        Assert.Null(empty.Annotations);
    }

    [Fact]
    public void Decode_RecordWithInitProperty_Captures()
    {
        var decoded = ToonDecoder.Decode<InitAccount>("Name: Cash\n@id: a/1", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal("a/1", decoded.Annotations!["@id"]);
    }

    [Fact]
    public void Decode_GetterOnlyInitialisedDictionary_PopulatedInPlace()
    {
        var decoded = ToonDecoder.Decode<GetterOnlyAccount>("Name: Cash\n@id: a/1", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("a/1", decoded.Annotations["@id"]);
    }

    [Fact]
    public void Decode_GetterOnlyNullDictionary_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            ToonDecoder.Decode<NullGetterOnlyAccount>("Name: Cash\n@id: a/1", Decode(ToonProtocolVersion.CustomV4_4)));
    }

    [Fact]
    public void Decode_StructDto_Captures()
    {
        var decoded = ToonDecoder.Decode<AccountStruct>("Name: Cash\n@id: a/1", Decode(ToonProtocolVersion.CustomV4_4));

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal("a/1", decoded.Annotations!["@id"]);
    }

    [Fact]
    public void Decode_NestedMemberWithExtensionData_Captures()
    {
        var decoded = ToonDecoder.Decode<Ledger>("Title: Main\nPrimary:\n  Name: Cash\n  @id: a/1", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal("Main", decoded.Title);
        Assert.Equal("Cash", decoded.Primary!.Name);
        Assert.Equal("a/1", decoded.Primary.Annotations!["@id"]);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4, "[2]{Name,@id}:\n  Cash,a/1\n  Bank,a/2")]
    [InlineData(ToonProtocolVersion.V4_4, "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2")]
    public void Decode_TableRows_UnmatchedColumnsCaptured(ToonProtocolVersion version, string text)
    {
        var decoded = ToonDecoder.Decode<List<Account>>(text, Decode(version))!;

        Assert.Equal(2, decoded.Count);
        Assert.Equal("Bank", decoded[1].Name);
        Assert.Equal("a/1", decoded[0].Annotations!["@id"]);
        Assert.Equal("a/2", decoded[1].Annotations!["@id"]);
    }

    [Fact]
    public void Decode_CustomRowSpill_CapturedPerRow()
    {
        const string text = "[2]{Name,@id}:\n  Cash,a/1\n    @flags: x\n  Bank,a/2\n    @metadata:\n      @collection: Accounts";

        var decoded = ToonDecoder.Decode<List<Account>>(text, Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal(["@id", "@flags"], decoded[0].Annotations!.Keys);
        Assert.Equal("x", decoded[0].Annotations!["@flags"]);
        Assert.Equal(["@id", "@metadata"], decoded[1].Annotations!.Keys);
        Assert.Equal("Accounts", Assert.IsType<Dictionary<string, object?>>(decoded[1].Annotations!["@metadata"])["@collection"]);
    }

    [Fact]
    public void Decode_OfficialListItems_CapturedPerItem()
    {
        const string text = "[2]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - \"@id\": a/2\n    Name: Bank";

        var decoded = ToonDecoder.Decode<List<Account>>(text, Decode(ToonProtocolVersion.V4_4))!;

        Assert.Equal("Cash", decoded[0].Name);
        Assert.Equal(["@id", "@flags"], decoded[0].Annotations!.Keys);
        Assert.Equal("Bank", decoded[1].Name);
        Assert.Equal("a/2", decoded[1].Annotations!["@id"]);
    }

    [Fact]
    public void Encode_CustomV44_EntriesAfterMembersUnquoted()
    {
        var text = ToonEncoder.Encode(SampleAccount(), Encode(ToonProtocolVersion.CustomV4_4));

        Assert.Equal(CustomAccount, text);
    }

    [Fact]
    public void Encode_OfficialV44_EntriesAfterMembersQuoted()
    {
        var text = ToonEncoder.Encode(SampleAccount(), Encode(ToonProtocolVersion.V4_4));

        Assert.Equal(OfficialAccount, text);
    }

    [Fact]
    public void Encode_EntryCollidingWithMemberWireName_MemberWins()
    {
        var source = new Account { Name = "Cash", Annotations = new Dictionary<string, object?> { ["Name"] = "Other", ["@id"] = "a/1" } };

        var text = ToonEncoder.Encode(source, Encode(ToonProtocolVersion.CustomV4_4));

        Assert.Equal("Name: Cash\n@id: a/1", text);
    }

    [Fact]
    public void Encode_EntryCollidingWithAliasedMemberClrName_Skipped()
    {
        var source = new AliasedAccount
        {
            Name = "Cash",
            Code = "A1",
            Annotations = new Dictionary<string, object?> { ["Code"] = "X", ["acct"] = "Y", ["@id"] = "a/1" }
        };

        var text = ToonEncoder.Encode(source, Encode(ToonProtocolVersion.CustomV4_4));

        Assert.Equal("Name: Cash\nacct: A1\n@id: a/1", text);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4, "[2]{Name,@id}:\n  Cash,a/1\n  Bank,a/2")]
    [InlineData(ToonProtocolVersion.V4_4, "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2")]
    public void Encode_UniformEntries_FormTable(ToonProtocolVersion version, string expected)
    {
        var text = ToonEncoder.Encode(UniformAccounts(), Encode(version));

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Encode_CustomIrregularEntries_SpillUnderRow()
    {
        var text = ToonEncoder.Encode(IrregularAccounts(), Encode(ToonProtocolVersion.CustomV4_4));

        Assert.Equal("[2]{Name,@id}:\n  Cash,a/1\n    @flags: x\n  Bank,a/2", text);
        Assert.Equal(ToonEncoder.Encode(AsDictionaries(IrregularAccounts()), Encode(ToonProtocolVersion.CustomV4_4)), text);
    }

    [Fact]
    public void Encode_OfficialIrregularEntries_ListForm()
    {
        var text = ToonEncoder.Encode(IrregularAccounts(), Encode(ToonProtocolVersion.V4_4));

        Assert.StartsWith("[2]:\n  - Name: Cash\n", text);
        Assert.Contains("\"@flags\": x", text);
        Assert.Equal(ToonEncoder.Encode(AsDictionaries(IrregularAccounts()), Encode(ToonProtocolVersion.V4_4)), text);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void Encode_ExcludeEmptyArrays_OmitsEmptyEntry(ToonProtocolVersion version)
    {
        var source = new Account { Name = "Cash", Annotations = new Dictionary<string, object?> { ["Tags"] = new List<object?>(), ["Kind"] = "asset" } };

        var excluded = ToonEncoder.Encode(source, Encode(version, excludeEmptyArrays: true));
        var kept = ToonEncoder.Encode(source, Encode(version, excludeEmptyArrays: false));

        Assert.Equal("Name: Cash\nKind: asset", excluded);
        Assert.Equal("Name: Cash\nTags: []\nKind: asset", kept);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4, "[2]{Name,@id}:\n  Cash,a/1\n  Bank,a/2")]
    [InlineData(ToonProtocolVersion.V4_4, "[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2")]
    public void Encode_IgnoreNullOrEmpty_SuppressesAllNullEntryColumn(ToonProtocolVersion version, string expected)
    {
        var source = UniformAccounts();
        foreach (var account in source) account.Annotations!["@note"] = null;

        var text = ToonEncoder.Encode(source, Encode(version, ignoreNullOrEmpty: true));

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Encode_NullEntryOnSingleObject_WritesNull()
    {
        var source = new Account { Name = "Cash", Annotations = new Dictionary<string, object?> { ["@note"] = null } };

        var text = ToonEncoder.Encode(source, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.CustomV4_4, Indent = 2 });

        Assert.Equal("Name: Cash\n@note: null", text);
    }

    [Theory]
    [MemberData(nameof(V4Versions))]
    public void Encode_NullOrEmptyExtensionData_MatchesTypeWithoutAttribute(ToonProtocolVersion version)
    {
        foreach (var annotations in new[] { null, new Dictionary<string, object?>() })
        {
            var options = Encode(version);
            var (rich, plain) = TwinLedgers(annotations);

            Assert.Equal(ToonEncoder.Encode(plain.Primary, options), ToonEncoder.Encode(rich.Primary, options));
            Assert.Equal(ToonEncoder.Encode(plain.Accounts, options), ToonEncoder.Encode(rich.Accounts, options));
            Assert.Equal(ToonEncoder.Encode(plain, options), ToonEncoder.Encode(rich, options));
            var defaults = new ToonEncodeOptions { ProtocolVersion = version };
            Assert.Equal(ToonEncoder.Encode(plain, defaults), ToonEncoder.Encode(rich, defaults));
            Assert.Equal(ToonEncoder.Encode(plain.Accounts, defaults), ToonEncoder.Encode(rich.Accounts, defaults));
        }
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void Encode_NestedPropertyAndList_Flattened(ToonProtocolVersion version)
    {
        var source = new Ledger { Title = "Main", Primary = SampleAccount(), Accounts = UniformAccounts() };

        var text = ToonEncoder.Encode(source, Encode(version));

        var expected = version == ToonProtocolVersion.CustomV4_4
            ? "Title: Main\nPrimary:\n  Name: Other Expenses\n  @id: accounts/6900\n  @metadata:\n    @collection: Accounts\nAccounts[2]{Name,@id}:\n  Cash,a/1\n  Bank,a/2"
            : "Title: Main\nPrimary:\n  Name: Other Expenses\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts\nAccounts[2]{Name,\"@id\"}:\n  Cash,a/1\n  Bank,a/2";
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void Encode_BytesAndStream_MatchString(ToonProtocolVersion version)
    {
        var options = Encode(version);
        foreach (var value in new object[] { SampleAccount(), UniformAccounts(), new Ledger { Title = "Main", Primary = SampleAccount(), Accounts = UniformAccounts() } })
        {
            var text = ToonEncoder.Encode(value, options);
            using var stream = new MemoryStream();
            ToonEncoder.EncodeToStream(value, stream, options);

            Assert.Equal(text, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(value, options)));
            Assert.Equal(text, Encoding.UTF8.GetString(stream.ToArray()));
        }
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Encode_LegacyProfiles_IgnoreAttribute(ToonProtocolVersion version)
    {
        var text = ToonEncoder.Encode(SampleAccount(), Encode(version));

        Assert.StartsWith("Name: Other Expenses\nAnnotations", text);
        Assert.DoesNotContain("\n@id", text);
        Assert.DoesNotContain("\n\"@id\"", text);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Decode_LegacyProfiles_DoNotCapture(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash\nExtra: 1", Decode(version))!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Null(decoded.Annotations);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void RoundTrip_DocumentWithMetadata_PreservesValues(ToonProtocolVersion version)
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

        var text = ToonEncoder.Encode(source, Encode(version));
        var decoded = ToonDecoder.Decode<BillingAccount>(text, Decode(version))!;

        Assert.Equal(source.Name, decoded.Name);
        Assert.Equal(source.Balance, decoded.Balance);
        Assert.Equal(["@id", "@metadata"], decoded.Annotations!.Keys);
        Assert.Equal("accounts/6900", decoded.Annotations["@id"]);
        var expectedMetadata = (Dictionary<string, object?>)source.Annotations["@metadata"]!;
        var metadata = Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["@metadata"]);
        Assert.Equal(expectedMetadata.Keys, metadata.Keys);
        foreach (var entry in expectedMetadata) Assert.Equal(entry.Value, metadata[entry.Key]);
    }

    [Theory]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void RoundTrip_AllObjectEntries_KeyedTable(ToonProtocolVersion version)
    {
        var source = new AccountBook
        {
            Name = "Main",
            Accounts = new AccountMap
            {
                Entries = new Dictionary<string, object?>
                {
                    ["cash"] = new Dictionary<string, object?> { ["Number"] = 1000, ["Title"] = "Cash" },
                    ["bank"] = new Dictionary<string, object?> { ["Number"] = 1010, ["Title"] = "Bank" }
                }
            }
        };

        var text = ToonEncoder.Encode(source, Encode(version));
        var decoded = ToonDecoder.Decode<AccountBook>(text, Decode(version))!;

        Assert.Contains("Accounts[2:]{", text);
        Assert.Equal("Main", decoded.Name);
        var entries = decoded.Accounts!.Entries!;
        Assert.Equal(["cash", "bank"], entries.Keys);
        var bank = Assert.IsType<Dictionary<string, object?>>(entries["bank"]);
        Assert.Equal(1010d, bank["Number"]);
        Assert.Equal("Bank", bank["Title"]);
    }

    [Fact]
    public void Decode_EntryOrder_Preserved()
    {
        var decoded = ToonDecoder.Decode<Account>("z: 1\nName: Cash\na: 2\nm: 3", Decode(ToonProtocolVersion.CustomV4_4))!;

        Assert.Equal(["z", "a", "m"], decoded.Annotations!.Keys);
    }

    [Theory]
    [InlineData("two")]
    [InlineData("inherited")]
    [InlineData("string-dictionary")]
    [InlineData("read-only-dictionary")]
    [InlineData("node")]
    [InlineData("object")]
    [InlineData("indexer")]
    public void Decode_InvalidExtensionProperty_Throws(string kind)
    {
        var options = Decode(ToonProtocolVersion.CustomV4_4);
        const string text = "Name: Cash\n@id: a/1";
        Action decode = kind switch
        {
            "two" => () => ToonDecoder.Decode<TwoExtensions>(text, options),
            "inherited" => () => ToonDecoder.Decode<DerivedWithExtension>(text, options),
            "string-dictionary" => () => ToonDecoder.Decode<StringDictionaryExtension>(text, options),
            "read-only-dictionary" => () => ToonDecoder.Decode<ReadOnlyDictionaryExtension>(text, options),
            "node" => () => ToonDecoder.Decode<NodeExtension>(text, options),
            "object" => () => ToonDecoder.Decode<ObjectExtension>(text, options),
            _ => () => ToonDecoder.Decode<IndexerExtension>(text, options)
        };

        Assert.Throws<NotSupportedException>(decode);
    }

    [Theory]
    [InlineData("two", ToonProtocolVersion.CustomV4_4)]
    [InlineData("inherited", ToonProtocolVersion.CustomV4_4)]
    [InlineData("string-dictionary", ToonProtocolVersion.CustomV4_4)]
    [InlineData("read-only-dictionary", ToonProtocolVersion.V4_4)]
    [InlineData("node", ToonProtocolVersion.V4_4)]
    [InlineData("object", ToonProtocolVersion.V4_4)]
    [InlineData("indexer", ToonProtocolVersion.CustomV4_4)]
    public void Encode_InvalidExtensionProperty_Throws(string kind, ToonProtocolVersion version)
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

        Assert.Throws<NotSupportedException>(() => ToonEncoder.Encode(value, Encode(version)));
        Assert.Throws<NotSupportedException>(() => ToonEncoder.Encode(new[] { value, value }, Encode(version)));
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

    private static List<Dictionary<string, object?>> AsDictionaries(List<Account> accounts)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var account in accounts)
        {
            var row = new Dictionary<string, object?> { ["Name"] = account.Name };
            foreach (var entry in account.Annotations!) row[entry.Key] = entry.Value;
            result.Add(row);
        }
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

    private static ToonEncodeOptions Encode(ToonProtocolVersion version, bool ignoreNullOrEmpty = false, bool excludeEmptyArrays = false) => new()
    {
        ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = ignoreNullOrEmpty, ExcludeEmptyArrays = excludeEmptyArrays
    };

    private static ToonDecodeOptions Decode(ToonProtocolVersion version, bool strict = true) => new()
    {
        ProtocolVersion = version, Indent = 2, Strict = strict
    };

    public sealed class Account
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class AliasedAccount
    {
        public string? Name { get; set; }

        [ToonPropertyName("acct")]
        public string? Code { get; set; }

        [ToonExtensionData]
        public Dictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class SeededAccount
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?> Annotations { get; set; } = new Dictionary<string, object?> { ["seed"] = "kept" };
    }

    public sealed class GetterOnlyAccount
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public Dictionary<string, object?> Annotations { get; } = new();
    }

    public sealed class NullGetterOnlyAccount
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public Dictionary<string, object?>? Annotations { get; }
    }

    public sealed record AccountRecord(string Name, [property: ToonExtensionData] IDictionary<string, object?>? Annotations);

    public sealed record InitAccount
    {
        public string? Name { get; init; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; init; }
    }

    public struct AccountStruct
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class Ledger
    {
        public string? Title { get; set; }
        public Account? Primary { get; set; }
        public List<Account>? Accounts { get; set; }
    }

    public sealed class BillingAccount
    {
        public string? Name { get; set; }
        public decimal Balance { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class AccountBook
    {
        public string? Name { get; set; }
        public AccountMap? Accounts { get; set; }
    }

    public sealed class AccountMap
    {
        [ToonExtensionData]
        public IDictionary<string, object?>? Entries { get; set; }
    }

    public sealed class Address
    {
        public string? City { get; set; }
        public string? Zip { get; set; }
    }

    public sealed class RichAccount
    {
        public string? Name { get; set; }
        public int Number { get; set; }
        public decimal Balance { get; set; }
        public bool Active { get; set; }
        public string? Note { get; set; }
        public Address? Address { get; set; }
        public List<string>? Tags { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class PlainAccount
    {
        public string? Name { get; set; }
        public int Number { get; set; }
        public decimal Balance { get; set; }
        public bool Active { get; set; }
        public string? Note { get; set; }
        public Address? Address { get; set; }
        public List<string>? Tags { get; set; }
    }

    public sealed class RichLedger
    {
        public string? Title { get; set; }
        public RichAccount? Primary { get; set; }
        public List<RichAccount>? Accounts { get; set; }
    }

    public sealed class PlainLedger
    {
        public string? Title { get; set; }
        public PlainAccount? Primary { get; set; }
        public List<PlainAccount>? Accounts { get; set; }
    }

    public sealed class TwoExtensions
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? First { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Second { get; set; }
    }

    public class BaseWithExtension
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Inherited { get; set; }
    }

    public sealed class DerivedWithExtension : BaseWithExtension
    {
        [ToonExtensionData]
        public IDictionary<string, object?>? Declared { get; set; }
    }

    public sealed class StringDictionaryExtension
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public Dictionary<string, string>? Annotations { get; set; }
    }

    public sealed class ReadOnlyDictionaryExtension
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IReadOnlyDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class NodeExtension
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public ToonNode? Annotations { get; set; }
    }

    public sealed class ObjectExtension
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public object? Annotations { get; set; }
    }

    public sealed class IndexerExtension
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public object? this[string key]
        {
            get => null;
            set { }
        }
    }
}
