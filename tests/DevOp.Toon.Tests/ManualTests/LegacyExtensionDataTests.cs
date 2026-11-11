using static DevOp.Toon.Tests.ExtensionDataTests;

namespace DevOp.Toon.Tests;

public sealed class LegacyExtensionDataTests
{
    private const string RouteMarker = "route.marker: 1";

    public enum Route
    {
        Direct,
        ExpandPaths
    }

    public static TheoryData<ToonProtocolVersion> LegacyVersions => new() { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy };

    public static TheoryData<ToonProtocolVersion, Route> LegacyRoutes => new()
    {
        { ToonProtocolVersion.V3_0, Route.Direct },
        { ToonProtocolVersion.V3_0, Route.ExpandPaths },
        { ToonProtocolVersion.CustomLegacy, Route.Direct },
        { ToonProtocolVersion.CustomLegacy, Route.ExpandPaths }
    };

    public static TheoryData<ToonProtocolVersion> V4Versions => new()
    {
        ToonProtocolVersion.V4_0, ToonProtocolVersion.V4_1, ToonProtocolVersion.V4_2,
        ToonProtocolVersion.V4_3, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyUnmatchedKeys_Captured(ToonProtocolVersion version, Route route)
    {
        const string text = "Name: Other Expenses\n\"@id\": accounts/6900\n\"@metadata\":\n  \"@collection\": Accounts";

        var decoded = DecodeObject<Account>(text, version, route, a => a.Annotations);

        Assert.Equal("Other Expenses", decoded.Name);
        Assert.Equal(["@id", "@metadata"], decoded.Annotations!.Keys);
        Assert.Equal("accounts/6900", decoded.Annotations["@id"]);
        Assert.Equal("Accounts", Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["@metadata"])["@collection"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyNestedObjectAndArray_CapturedAsDictionaryAndList(ToonProtocolVersion version, Route route)
    {
        const string text = "Name: Cash\nExtra:\n  A: 1\n  B: x\n  Deep:\n    C: true\nTags[2]: a,b\nRows[2]{K,V}:\n  a,1\n  b,2\nEmpty[0]:\nNothing: null";

        var annotations = DecodeObject<Account>(text, version, route, a => a.Annotations).Annotations!;

        var extra = Assert.IsType<Dictionary<string, object?>>(annotations["Extra"]);
        Assert.Equal(1d, extra["A"]);
        Assert.Equal("x", extra["B"]);
        Assert.Equal(true, Assert.IsType<Dictionary<string, object?>>(extra["Deep"])["C"]);
        Assert.Equal(["a", "b"], Assert.IsType<List<object?>>(annotations["Tags"]));
        var rows = Assert.IsType<List<object?>>(annotations["Rows"]);
        Assert.Equal(2d, Assert.IsType<Dictionary<string, object?>>(rows[1])["V"]);
        Assert.Empty(Assert.IsType<List<object?>>(annotations["Empty"]));
        Assert.True(annotations.ContainsKey("Nothing"));
        Assert.Null(annotations["Nothing"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyNumericEntry_IsDoubleOnEveryRoute(ToonProtocolVersion version, Route route)
    {
        var annotations = DecodeObject<Account>("Name: Cash\nn: 2000000\nr: 1.10\nk: -3\nt: 0.0000001", version, route, a => a.Annotations).Annotations!;

        Assert.Equal(2000000d, Assert.IsType<double>(annotations["n"]));
        Assert.Equal(1.1d, Assert.IsType<double>(annotations["r"]));
        Assert.Equal(-3d, Assert.IsType<double>(annotations["k"]));
        Assert.Equal(0.0000001d, Assert.IsType<double>(annotations["t"]));
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyMatchedAndAliasedKeys_NotCaptured(ToonProtocolVersion version, Route route)
    {
        var byAlias = DecodeObject<AliasedAccount>("Name: Cash\nacct: A1\nOther: 3", version, route, a => a.Annotations);
        var byClrName = DecodeObject<AliasedAccount>("Name: Cash\nCode: A2\nOther: 3", version, route, a => a.Annotations);

        Assert.Equal("A1", byAlias.Code);
        Assert.Equal(["Other"], byAlias.Annotations!.Keys);
        Assert.Equal("A2", byClrName.Code);
        Assert.Equal(["Other"], byClrName.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyKeyNamedLikeExtensionProperty_NonObjectCapturedAsEntry(ToonProtocolVersion version, Route route)
    {
        var scalar = DecodeObject<Account>("Name: Cash\nAnnotations: hello", version, route, a => a.Annotations);
        var list = DecodeObject<Account>("Name: Cash\nAnnotations[2]: a,b", version, route, a => a.Annotations);

        Assert.Equal(["Annotations"], scalar.Annotations!.Keys);
        Assert.Equal("hello", scalar.Annotations["Annotations"]);
        Assert.Equal(["a", "b"], Assert.IsType<List<object?>>(list.Annotations!["Annotations"]));
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyDuplicateUnmatchedKey_LastWinsEvenWhenStrict(ToonProtocolVersion version, Route route)
    {
        var decoded = DecodeObject<Account>("Name: Cash\nExtra: 1\nOther: x\nExtra: 2", version, route, a => a.Annotations);

        Assert.Equal(["Extra", "Other"], decoded.Annotations!.Keys);
        Assert.Equal(2d, decoded.Annotations["Extra"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyMalformedTableUnderUnmatchedKey_Throws(ToonProtocolVersion version, Route route)
    {
        Assert.Throws<ToonFormatException>(() => DecodeObject<Account>("Name: Cash\nRows[2]{K,V}:\n  a,1", version, route, a => a.Annotations));
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyPositionalRecord_Captures(ToonProtocolVersion version, Route route)
    {
        var decoded = DecodeObject<AccountRecord>("Name: Cash\n\"@id\": a/1", version, route, a => a.Annotations);

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal(["@id"], decoded.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyRecordWithInitProperty_Captures(ToonProtocolVersion version, Route route)
    {
        var decoded = DecodeObject<InitAccount>("Name: Cash\n\"@id\": a/1", version, route, a => a.Annotations);

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal("a/1", decoded.Annotations!["@id"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyGetterOnlyInitialisedDictionary_PopulatedInPlace(ToonProtocolVersion version, Route route)
    {
        var decoded = DecodeObject<GetterOnlyAccount>("Name: Cash\n\"@id\": a/1", version, route, a => a.Annotations);
        var seeded = DecodeObject<SeededAccount>("Name: Cash\n\"@id\": a/1", version, route, a => a.Annotations);

        Assert.Equal("a/1", decoded.Annotations["@id"]);
        Assert.Equal(["seed", "@id"], seeded.Annotations.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyGetterOnlyNullDictionary_Throws(ToonProtocolVersion version, Route route)
    {
        Assert.Throws<NotSupportedException>(() => DecodeObject<NullGetterOnlyAccount>("Name: Cash\n\"@id\": a/1", version, route, a => a.Annotations));
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyConstructorBoundCaseVariantKey_BindsToMember(ToonProtocolVersion version, Route route)
    {
        var decoded = DecodeObject<AccountRecord>("name: Cash\nOther: 1", version, route, a => a.Annotations);
        var aliased = DecodeObject<AliasedAccountRecord>("Name: Cash\nACCT: A1\nOther: 1", version, route, a => a.Annotations);

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal(["Other"], decoded.Annotations!.Keys);
        Assert.Equal("A1", aliased.Code);
        Assert.Equal(["Other"], aliased.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyDeclaredReadOnlyMemberKey_NotCaptured(ToonProtocolVersion version, Route route)
    {
        var decoded = DecodeObject<ReadOnlyMemberAccount>("Name: Cash\nUpper: X\nsum: 2\nOther: 1", version, route, a => a.Annotations);
        var record = DecodeObject<RecordWithExtra>("Name: Cash\nNote: n\nOther: 1", version, route, a => a.Annotations);

        Assert.Equal(["Other"], decoded.Annotations!.Keys);
        Assert.Equal(["Other"], record.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyStructDto_Captures(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<AccountStruct>("Name: Cash\n\"@id\": a/1", Options(version, Route.Direct));

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal("a/1", decoded.Annotations!["@id"]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyStructWithLazyGetter_CapturesIntoBoxedValue(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<LazyAccountStruct>("Name: Cash\nextra: 1", Options(version, Route.Direct));

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal(1d, decoded.Annotations["extra"]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyTypeWithByteArrayMember_Captures(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<BytesAccount>("Name: Cash\nData: AQID\n\"@id\": a/1", Options(version, Route.Direct))!;

        Assert.Equal([1, 2, 3], decoded.Data);
        Assert.Equal(["@id"], decoded.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyExpandPaths_DottedKeyCapturedAsNestedEntry(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash\nmeta.collection: Accounts\nmeta.rev: 2", Options(version, Route.ExpandPaths))!;

        var meta = Assert.IsType<Dictionary<string, object?>>(decoded.Annotations!["meta"]);
        Assert.Equal("Accounts", meta["collection"]);
        Assert.Equal(2d, meta["rev"]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyExpandPaths_QuotedDottedKeyStaysLiteral(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<Account>("Name: Cash\n\"a.b\": 1\nm.n: 2", Options(version, Route.ExpandPaths))!;

        Assert.Equal(["a.b", "m"], decoded.Annotations!.Keys);
        Assert.Equal(1d, decoded.Annotations["a.b"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyInvalidExtensionProperty_Throws(ToonProtocolVersion version, Route route)
    {
        var options = Options(version, route);
        const string text = "Name: Cash\n\"@id\": a/1\na.b: 1";

        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<TwoExtensions>(text, options));
        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<StringDictionaryExtension>(text, options));
        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<ObjectExtension>(text, options));
        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<Ledger<TwoExtensions>>("Title: t\nPrimary:\n  Name: Cash\na.b: 1", options));
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_Legacy040NestedExtensionObject_MergedIntoBag(ToonProtocolVersion version, Route route)
    {
        const string text = "Name: Other Expenses\nAnnotations:\n  \"@id\": accounts/6900\n  \"@metadata\":\n    \"@collection\": Accounts";

        var decoded = DecodeObject<Account>(text, version, route, a => a.Annotations);

        Assert.Equal("Other Expenses", decoded.Name);
        Assert.Equal(["@id", "@metadata"], decoded.Annotations!.Keys);
        Assert.Equal("Accounts", Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["@metadata"])["@collection"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_Legacy040NestedAndSiblingSameKey_SiblingWins(ToonProtocolVersion version, Route route)
    {
        var siblingFirst = DecodeObject<Account>("Name: Cash\n\"@id\": sibling\nAnnotations:\n  \"@id\": nested\n  Name: member\n  Kept: 1", version, route, a => a.Annotations);
        var nestedFirst = DecodeObject<Account>("Name: Cash\nAnnotations:\n  \"@id\": nested\n  Kept: 1\n\"@id\": sibling", version, route, a => a.Annotations);

        Assert.Equal("Cash", siblingFirst.Name);
        Assert.Equal(["@id", "Kept"], siblingFirst.Annotations!.Keys);
        Assert.Equal("sibling", siblingFirst.Annotations["@id"]);
        Assert.Equal("sibling", nestedFirst.Annotations!["@id"]);
        Assert.Equal(1d, nestedFirst.Annotations["Kept"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_Legacy040NestedUnderAliasedName_Merged(ToonProtocolVersion version, Route route)
    {
        var byAlias = DecodeObject<AliasedExtensionAccount>("Name: Cash\nmeta:\n  \"@id\": a/1", version, route, a => a.Extra);
        var byClrName = DecodeObject<AliasedExtensionAccount>("Name: Cash\nExtra:\n  \"@id\": a/2", version, route, a => a.Extra);

        Assert.Equal("a/1", byAlias.Extra!["@id"]);
        Assert.Equal("a/2", byClrName.Extra!["@id"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_Legacy040NestedRecord_Merged(ToonProtocolVersion version, Route route)
    {
        var record = DecodeObject<AccountRecord>("Name: Cash\nannotations:\n  \"@id\": a/1\n  name: dropped", version, route, a => a.Annotations);

        Assert.Equal("Cash", record.Name);
        Assert.Equal(["@id"], record.Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_Legacy040NestedStruct_Merged(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<AccountStruct>("Name: Cash\nAnnotations:\n  \"@id\": a/2", Options(version, Route.Direct));

        Assert.Equal("a/2", decoded.Annotations!["@id"]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyNestedMemberWithExtensionData_Captures(ToonProtocolVersion version)
    {
        const string text = "Title: Main\nPrimary:\n  Name: Cash\n  \"@id\": a/1\n  \"@meta\":\n    rev: 2\nAccounts[1]:\n  - Name: Bank\n    \"@id\": a/2";

        var decoded = ToonDecoder.Decode<Ledger>(text, Options(version, Route.Direct))!;

        Assert.Equal("Main", decoded.Title);
        Assert.Equal(["@id", "@meta"], decoded.Primary!.Annotations!.Keys);
        Assert.Equal(2d, Assert.IsType<Dictionary<string, object?>>(decoded.Primary.Annotations["@meta"])["rev"]);
        Assert.Equal("a/2", decoded.Accounts![0].Annotations!["@id"]);
    }

    [Theory]
    [MemberData(nameof(LegacyRoutes))]
    public void Decode_LegacyDictionaryOfAttributedValues_CapturesPerValue(ToonProtocolVersion version, Route route)
    {
        var text = "Name: Book\nMap:\n  cash:\n    Name: Cash\n    \"@id\": a/1\n  bank:\n    Name: Bank\n    \"@id\": a/2" +
                   (route == Route.ExpandPaths ? "\nroute.marker: 1" : string.Empty);

        var decoded = ToonDecoder.Decode<AccountDirectory>(text, Options(version, route))!;

        Assert.Equal("a/1", decoded.Map!["cash"].Annotations!["@id"]);
        Assert.Equal("a/2", decoded.Map["bank"].Annotations!["@id"]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyTableRows_UnmatchedColumnsCaptured(ToonProtocolVersion version)
    {
        const string text = "[2]{Name,\"@id\",n}:\n  Cash,a/1,1\n  Bank,a/2,2000000";

        var decoded = ToonDecoder.Decode<List<Account>>(text, Options(version, Route.Direct))!;
        var records = ToonDecoder.Decode<List<AccountRecord>>(text, Options(version, Route.Direct))!;

        Assert.Equal("Bank", decoded[1].Name);
        Assert.Equal(["@id", "n"], decoded[0].Annotations!.Keys);
        Assert.Equal("a/2", decoded[1].Annotations!["@id"]);
        Assert.Equal(2000000d, Assert.IsType<double>(decoded[1].Annotations!["n"]));
        Assert.Equal("Bank", records[1].Name);
        Assert.Equal(["@id", "n"], records[1].Annotations!.Keys);
    }

    [Fact]
    public void Decode_CustomLegacyRowContinuation_CapturedPerRow()
    {
        const string text = "[2]{Name,\"@id\"}:\n  Cash,a/1\n    \"@flags\": x\n  Bank,a/2\n    \"@metadata\":\n      \"@collection\": Accounts\n    Tags[2]: a,b";
        var options = Options(ToonProtocolVersion.CustomLegacy, Route.Direct);

        var decoded = ToonDecoder.Decode<List<Account>>(text, options)!;
        var records = ToonDecoder.Decode<List<AccountRecord>>(text, options)!;

        Assert.Equal(["@id", "@flags"], decoded[0].Annotations!.Keys);
        Assert.Equal(["@id", "@metadata", "Tags"], decoded[1].Annotations!.Keys);
        Assert.Equal("Accounts", Assert.IsType<Dictionary<string, object?>>(decoded[1].Annotations!["@metadata"])["@collection"]);
        Assert.Equal(["@id", "@flags"], records[0].Annotations!.Keys);
        Assert.Equal(["@id", "@metadata", "Tags"], records[1].Annotations!.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyListItems_CapturedPerItem(ToonProtocolVersion version)
    {
        const string text = "[3]:\n  - Name: Cash\n    \"@id\": a/1\n    \"@flags\": x\n  - \"@id\": a/2\n    Name: Bank\n  - Tags[2]: a,b\n    Name: Third";
        var options = Options(version, Route.Direct);

        var decoded = ToonDecoder.Decode<List<Account>>(text, options)!;
        var records = ToonDecoder.Decode<List<AccountRecord>>(text, options)!;

        Assert.Equal(["@id", "@flags"], decoded[0].Annotations!.Keys);
        Assert.Equal("Bank", decoded[1].Name);
        Assert.Equal(["@id"], decoded[1].Annotations!.Keys);
        Assert.Equal("Third", decoded[2].Name);
        Assert.Equal(["a", "b"], Assert.IsType<List<object?>>(decoded[2].Annotations!["Tags"]));
        Assert.Equal(["@id"], records[1].Annotations!.Keys);
        Assert.Equal("Third", records[2].Name);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyListItemFirstFieldNestedValues_MatchUntypedDecode(ToonProtocolVersion version)
    {
        var source = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["Rows"] = new List<Dictionary<string, object?>> { new() { ["K"] = "a", ["V"] = 1 }, new() { ["K"] = "b", ["V"] = 2 } },
                ["Name"] = "Cash"
            },
            new() { ["Meta"] = new Dictionary<string, object?> { ["rev"] = 3 }, ["Name"] = "Bank" }
        };
        var text = ToonEncoder.Encode(source, new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 });
        var options = Options(version, Route.Direct);

        var decoded = ToonDecoder.Decode<List<Account>>(text, options)!;
        var untyped = ToonDecoder.Decode<List<Dictionary<string, object?>>>(text, options)!;

        Assert.Equal("Cash", decoded[0].Name);
        var rows = Assert.IsType<List<object?>>(decoded[0].Annotations!["Rows"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2d, Assert.IsType<Dictionary<string, object?>>(rows[1])["V"]);
        Assert.Equal("Bank", decoded[1].Name);
        Assert.Equal(3d, Assert.IsType<Dictionary<string, object?>>(decoded[1].Annotations!["Meta"])["rev"]);
        Assert.Equal("Cash", untyped[0]["Name"]);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyTypeWithoutAttribute_UnmatchedKeysStillIgnored(ToonProtocolVersion version)
    {
        const string text = "Name: Cash\nExtra:\n  A: 1\nTags[2]: a,b\n\"@id\": a/1\nNumber: 7";

        var decoded = ToonDecoder.Decode<PlainAccount>(text, Options(version, Route.Direct))!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal(7, decoded.Number);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyEmptyDocument_ExtensionStaysNull(ToonProtocolVersion version)
    {
        var empty = ToonDecoder.Decode<Account>(string.Empty, Options(version, Route.Direct))!;
        var matchedOnly = ToonDecoder.Decode<Account>("Name: Cash", Options(version, Route.Direct))!;
        var record = ToonDecoder.Decode<AccountRecord>("Name: Cash", Options(version, Route.Direct))!;

        Assert.Null(empty.Annotations);
        Assert.Null(matchedOnly.Annotations);
        Assert.Null(record.Annotations);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_LegacyAttributedTypeFirstUseInParallel_CapturesOnEveryThread(ToonProtocolVersion version)
    {
        var text = version == ToonProtocolVersion.V3_0
            ? "Name: Cash\n\"@id\": a/1\nItems[1]:\n  - Name: x\n    \"@k\": 1"
            : "Name: Cash\n\"@id\": a/1\nItems[1]{Name,\"@k\"}:\n  x,1";
        var results = new ParallelFirstUse[16];

        Parallel.For(0, results.Length, i =>
            results[i] = version == ToonProtocolVersion.V3_0
                ? ToonDecoder.Decode<ParallelFirstUse>(text, Options(version, Route.Direct))!
                : ToonDecoder.Decode<ParallelFirstUseCustom>(text, Options(version, Route.Direct))!);

        Assert.All(results, r =>
        {
            Assert.Equal("a/1", r.Annotations!["@id"]);
            Assert.Equal(1d, r.Items![0].Annotations!["@k"]);
        });
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_Legacy040KeyValueRows_StayListEntry(ToonProtocolVersion version)
    {
        const string text = "Name: Other Expenses\nAnnotations[2]{Key}:\n  \"@id\"\n    Value: accounts/6900\n  \"@metadata\"\n    Value:\n      \"@collection\": Accounts";

        var decoded = ToonDecoder.Decode<Account>(text, Options(version, Route.Direct))!;

        Assert.Equal(["Annotations"], decoded.Annotations!.Keys);
        var rows = Assert.IsType<List<object?>>(decoded.Annotations["Annotations"]);
        Assert.Equal("@id", Assert.IsType<Dictionary<string, object?>>(rows[0])["Key"]);
    }

    [Theory]
    [MemberData(nameof(V4Versions))]
    public void Decode_V4NestedObjectNamedLikeProperty_StaysEntry(ToonProtocolVersion version)
    {
        var key = version == ToonProtocolVersion.CustomV4_4 ? "@id" : "\"@id\"";

        var decoded = ToonDecoder.Decode<Account>($"Name: Cash\nAnnotations:\n  {key}: a/1", new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 })!;

        Assert.Equal(["Annotations"], decoded.Annotations!.Keys);
        Assert.Equal("a/1", Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["Annotations"])["@id"]);
    }

    [Theory]
    [MemberData(nameof(V4Versions))]
    public void Decode_V4DeclaredReadOnlyMemberKey_NotCaptured(ToonProtocolVersion version)
    {
        var options = new ToonDecodeOptions { ProtocolVersion = version, Indent = 2 };

        var decoded = ToonDecoder.Decode<ReadOnlyMemberAccount>("Name: Cash\nUpper: X\nsum: 2\nTotal: 3\nOther: 1", options)!;
        var table = ToonDecoder.Decode<List<ReadOnlyMemberAccount>>("[1]{Name,Upper,Other}:\n  Cash,X,1", options)!;
        var record = ToonDecoder.Decode<RecordWithComputed>("Name: Cash\nShout: X\nOther: 1", options)!;

        Assert.Equal(["Other"], decoded.Annotations!.Keys);
        Assert.Equal(0, decoded.Total);
        Assert.Equal(["Other"], table[0].Annotations!.Keys);
        Assert.Equal(["Other"], record.Annotations!.Keys);
    }

    [Fact]
    public void Deserialize_JsonThreeArgumentLegacyOptions_Captures()
    {
        var service = new ToonService();
        const string json = "{\"Name\":\"Cash\",\"@id\":\"a/1\",\"n\":3}";

        foreach (var version in new[] { ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy, ToonProtocolVersion.V4_4 })
        {
            var decoded = service.Deserialize<Account>(json, "application/json", new ToonDecodeOptions { ProtocolVersion = version })!;

            Assert.Equal("Cash", decoded.Name);
            Assert.Equal(["@id", "n"], decoded.Annotations!.Keys);
            Assert.Equal(3d, Assert.IsType<double>(decoded.Annotations["n"]));
        }
    }

    [Fact]
    public void Deserialize_JsonTwoArgument_CapturesUnmatchedKeys()
    {
        var decoded = new ToonService().Deserialize<Account>("{\"Name\":\"Cash\",\"@id\":\"a/1\",\"n\":3,\"big\":12345678901,\"r\":1.5}", "application/json")!;

        Assert.Equal("Cash", decoded.Name);
        Assert.Equal(["@id", "n", "big", "r"], decoded.Annotations!.Keys);
        Assert.Equal(3d, Assert.IsType<double>(decoded.Annotations["n"]));
        Assert.Equal(12345678901d, Assert.IsType<double>(decoded.Annotations["big"]));
        Assert.Equal(1.5d, Assert.IsType<double>(decoded.Annotations["r"]));
    }

    [Fact]
    public void Deserialize_JsonTwoArgumentNestedAndArray_Captured()
    {
        var decoded = new ToonService().Deserialize<Account>("{\"Name\":\"Cash\",\"@metadata\":{\"@collection\":\"Accounts\",\"n\":2},\"tags\":[1,\"a\",null,true]}", "application/json")!;

        var metadata = Assert.IsType<Dictionary<string, object?>>(decoded.Annotations!["@metadata"]);
        Assert.Equal("Accounts", metadata["@collection"]);
        Assert.Equal(2d, Assert.IsType<double>(metadata["n"]));
        Assert.Equal([1d, "a", null, true], Assert.IsType<List<object?>>(decoded.Annotations["tags"]));
    }

    [Fact]
    public void Deserialize_JsonTwoArgumentKeyNamedLikeProperty_CapturedAsEntry()
    {
        var decoded = new ToonService().Deserialize<Account>("{\"Name\":\"Cash\",\"Annotations\":\"hello\"}", "application/json")!;

        Assert.Equal("hello", decoded.Annotations!["Annotations"]);
    }

    [Fact]
    public void Deserialize_JsonTwoArgumentObjectNamedLikeProperty_StaysEntry()
    {
        var decoded = new ToonService().Deserialize<Account>("{\"Name\":\"Cash\",\"Annotations\":{\"@id\":\"a/1\"}}", "application/json")!;

        Assert.Equal(["Annotations"], decoded.Annotations!.Keys);
        Assert.Equal("a/1", Assert.IsType<Dictionary<string, object?>>(decoded.Annotations["Annotations"])["@id"]);
    }

    [Fact]
    public void Deserialize_JsonTwoArgumentRecordAndReadOnlyMember_CaptureOnlyUnmatchedKeys()
    {
        var service = new ToonService();
        var record = service.Deserialize<AccountRecord>("{\"name\":\"Cash\",\"@id\":\"a/1\"}", "application/json")!;
        var readOnly = service.Deserialize<ReadOnlyMemberAccount>("{\"Name\":\"Cash\",\"Upper\":\"X\",\"Other\":1}", "application/json")!;

        Assert.Equal("Cash", record.Name);
        Assert.Equal(["@id"], record.Annotations!.Keys);
        Assert.Equal(["Other"], readOnly.Annotations!.Keys);
    }

    private static T DecodeObject<T>(string text, ToonProtocolVersion version, Route route, Func<T, IDictionary<string, object?>?> bag)
    {
        if (route == Route.Direct) return ToonDecoder.Decode<T>(text, Options(version, route))!;

        var decoded = ToonDecoder.Decode<T>(text + "\n" + RouteMarker, Options(version, route))!;
        var entries = bag(decoded)!;
        Assert.Equal(1d, Assert.IsType<Dictionary<string, object?>>(entries["route"])["marker"]);
        entries.Remove("route");
        return decoded;
    }

    private static ToonDecodeOptions Options(ToonProtocolVersion version, Route route, bool strict = true) => new()
    {
        ProtocolVersion = version,
        Indent = 2,
        Strict = strict,
        ExpandPaths = route == Route.ExpandPaths ? ToonPathExpansion.Safe : ToonPathExpansion.Off
    };

    public sealed class BytesAccount
    {
        public string? Name { get; set; }
        public byte[]? Data { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class ReadOnlyMemberAccount
    {
        public string? Name { get; set; }
        public string? Upper => Name?.ToUpperInvariant();

        [ToonPropertyName("sum")]
        public int Total { get; private set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed record RecordWithExtra(string Name, [property: ToonExtensionData] IDictionary<string, object?>? Annotations)
    {
        public string? Note { get; set; }
    }

    public sealed class AccountDirectory
    {
        public string? Name { get; set; }
        public Dictionary<string, Account>? Map { get; set; }
    }

    public sealed record RecordWithComputed(string Name, [property: ToonExtensionData] IDictionary<string, object?>? Annotations)
    {
        public string Shout => Name.ToUpperInvariant();
    }

    public class ParallelFirstUse
    {
        public string? Name { get; set; }
        public List<ParallelFirstUseItem>? Items { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class ParallelFirstUseCustom : ParallelFirstUse
    {
    }

    public sealed class ParallelFirstUseItem
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        public IDictionary<string, object?>? Annotations { get; set; }
    }

    public sealed class Ledger<TAccount>
    {
        public string? Title { get; set; }
        public TAccount? Primary { get; set; }
    }

    public sealed class AliasedExtensionAccount
    {
        public string? Name { get; set; }

        [ToonExtensionData]
        [ToonPropertyName("meta")]
        public IDictionary<string, object?>? Extra { get; set; }
    }
}
