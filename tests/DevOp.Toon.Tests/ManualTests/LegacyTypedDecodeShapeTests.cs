using System.Reflection;
using System.Text.Json;

namespace DevOp.Toon.Tests;

public sealed class LegacyTypedDecodeShapeTests
{
    public static TheoryData<ToonProtocolVersion> LegacyVersions => new()
    {
        ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy
    };

    public static TheoryData<ToonProtocolVersion> AllVersions => new()
    {
        ToonProtocolVersion.V3_0,
        ToonProtocolVersion.V4_0,
        ToonProtocolVersion.V4_1,
        ToonProtocolVersion.V4_2,
        ToonProtocolVersion.V4_3,
        ToonProtocolVersion.V4_4,
        ToonProtocolVersion.CustomLegacy,
        ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RootArrayOfPositionalRecords_RoundTripsOnDirectRouteAndService(ToonProtocolVersion version)
    {
        var source = new[] { CreateCustomer("C-001", "Alpha"), CreateCustomer("C-002", "Beta") };

        var decoded = RoundTripDirect(source, version);
        var viaService = RoundTripService(source, version);

        Assert.IsType<CustomerDto[]>(decoded);
        AssertStructurallyEqual(source, decoded);
        AssertStructurallyEqual(source, viaService);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_ArrayMemberOfPositionalRecords_RoundTripsOnPathExpansionRoute(ToonProtocolVersion version)
    {
        var source = new[] { CreateCustomer("C-001", "Alpha"), CreateCustomer("C-002", "Beta") };

        var decoded = RoundTripExpanded(source, version);

        AssertStructurallyEqual(source, decoded);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RecordWithReadOnlyListAndDictionaryMembers_RoundTripsOnEveryRoute(ToonProtocolVersion version)
    {
        var source = CreateCustomer("C-001", "Alpha");

        AssertStructurallyEqual(source, RoundTripDirect(source, version));
        AssertStructurallyEqual(source, RoundTripExpanded(source, version));
        AssertStructurallyEqual(source, RoundTripService(source, version));
        AssertStructurallyEqual(new List<CustomerDto> { source }, RoundTripDirect(new List<CustomerDto> { source }, version));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RootCollectionTargets_RoundTripAsExpectedConcreteTypes(ToonProtocolVersion version)
    {
        var source = CreateAddresses();

        var array = RoundTripDirect<AddressDto[]>(source.ToArray(), version);
        var list = RoundTripDirect<List<AddressDto>>(source, version);
        var mutable = RoundTripDirect<IList<AddressDto>>(source, version);
        var readOnly = RoundTripDirect<IReadOnlyList<AddressDto>>(source, version);
        var enumerable = RoundTripDirect<IEnumerable<AddressDto>>(source, version);

        Assert.IsType<AddressDto[]>(array);
        Assert.IsType<List<AddressDto>>(list);
        Assert.IsType<List<AddressDto>>(mutable);
        Assert.IsType<List<AddressDto>>(readOnly);
        Assert.Equal(source, array);
        Assert.Equal(source, list);
        Assert.Equal(source, mutable);
        Assert.Equal(source, readOnly);
        Assert.Equal(source, enumerable);
        Assert.Equal(source, RoundTripService<IReadOnlyList<AddressDto>>(source, version));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RootDictionaryTargets_RoundTripAsDictionary(ToonProtocolVersion version)
    {
        var source = CreateAddressMap();

        var concrete = RoundTripDirect<Dictionary<string, AddressDto>>(source, version);
        var mutable = RoundTripDirect<IDictionary<string, AddressDto>>(source, version);
        var readOnly = RoundTripDirect<IReadOnlyDictionary<string, AddressDto>>(source, version);
        var viaService = RoundTripService<IDictionary<string, AddressDto>>(source, version);

        Assert.IsType<Dictionary<string, AddressDto>>(mutable);
        Assert.IsType<Dictionary<string, AddressDto>>(readOnly);
        Assert.Equal(source, concrete);
        Assert.Equal(source, mutable);
        Assert.Equal(source, readOnly);
        Assert.Equal(source, viaService);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_CollectionAndDictionaryMembers_RoundTripOnEveryRoute(ToonProtocolVersion version)
    {
        var source = CreateShapes();

        var direct = RoundTripDirect(source, version);
        var expanded = RoundTripExpanded(source, version);
        var viaService = RoundTripService(source, version);

        AssertStructurallyEqual(source, direct);
        AssertStructurallyEqual(source, expanded);
        AssertStructurallyEqual(source, viaService);
        Assert.IsType<List<AddressDto>>(direct.Mutable);
        Assert.IsType<Dictionary<string, int>>(direct.MutableMap);
        Assert.IsType<List<AddressDto>>(expanded.Mutable);
        Assert.IsType<Dictionary<string, int>>(expanded.MutableMap);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RootCollectionTargets_RoundTripOnPathExpansionRoute(ToonProtocolVersion version)
    {
        var source = CreateAddresses();
        var map = CreateAddressMap();

        AssertStructurallyEqual(source.ToArray(), RoundTripExpanded(source.ToArray(), version));
        AssertStructurallyEqual(source, RoundTripExpanded<List<AddressDto>>(source, version));
        AssertStructurallyEqual(source, RoundTripExpanded<IList<AddressDto>>(source, version));
        AssertStructurallyEqual(source, RoundTripExpanded<IReadOnlyList<AddressDto>>(source, version));
        AssertStructurallyEqual(source, RoundTripExpanded<IEnumerable<AddressDto>>(source, version));
        AssertStructurallyEqual(map, RoundTripExpanded<Dictionary<string, AddressDto>>(map, version));
        AssertStructurallyEqual(map, RoundTripExpanded<IDictionary<string, AddressDto>>(map, version));
        AssertStructurallyEqual(map, RoundTripExpanded<IReadOnlyDictionary<string, AddressDto>>(map, version));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_ConstructorOnlyTypes_BindParametersCaseInsensitivelyOnEveryRoute(ToonProtocolVersion version)
    {
        var lowerCase = new LowerCaseParameters("Alpha", 7);
        var aliased = new AliasedThing("Beta", 9);

        AssertStructurallyEqual(lowerCase, RoundTripDirect(lowerCase, version));
        AssertStructurallyEqual(lowerCase, RoundTripExpanded(lowerCase, version));
        AssertStructurallyEqual(lowerCase, RoundTripService(lowerCase, version));
        AssertStructurallyEqual(aliased, RoundTripDirect(aliased, version));
        AssertStructurallyEqual(aliased, RoundTripExpanded(aliased, version));
        AssertStructurallyEqual(aliased, RoundTripService(aliased, version));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_PositionalRecord_MatchesKeysCaseInsensitivelyOnEveryRoute(ToonProtocolVersion version)
    {
        var direct = ToonDecoder.Decode<AliasedThing>("DISPLAY_name: Alpha\nCOUNT: 7", DecodeOptions(version))!;
        var expanded = ToonDecoder.Decode<FoldedEnvelope<AliasedThing>>(
            "Box.Value:\n  DISPLAY_name: Alpha\n  COUNT: 7",
            DecodeOptions(version, ToonPathExpansion.Safe))!;

        Assert.Equal(new AliasedThing("Alpha", 7), direct);
        Assert.Equal(new AliasedThing("Alpha", 7), expanded.Box.Value);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Decode_ReportedThingList_RoundTripsWithPinnedAndDetectedDecodeVersion(ToonProtocolVersion version)
    {
        var source = new List<Thing>
        {
            new("Alpha", ["x", "y"], new() { ["k"] = "v" })
        };
        IToonService detecting = new ToonService(new ToonServiceOptions { ProtocolVersion = version });

        var pinned = RoundTripService(source, version);
        var detected = detecting.Decode<List<Thing>>(detecting.Encode(source));

        AssertStructurallyEqual(source, pinned);
        AssertStructurallyEqual(source, detected);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_EmptyAndNullCollectionMembers_KeepEmptyDistinctFromNullOnEveryRoute(ToonProtocolVersion version)
    {
        var source = new OptionalShapes([], [], new Dictionary<string, string>(), null, null);

        var direct = RoundTripDirect(source, version, keepEmpty: true);
        var expanded = RoundTripExpanded(source, version, keepEmpty: true);
        var viaService = RoundTripService(source, version, keepEmpty: true);

        foreach (var decoded in new[] { direct, expanded, viaService })
        {
            Assert.NotNull(decoded.Tags);
            Assert.Empty(decoded.Tags);
            Assert.NotNull(decoded.Numbers);
            Assert.Empty(decoded.Numbers);
            Assert.NotNull(decoded.Meta);
            Assert.Empty(decoded.Meta);
            Assert.Null(decoded.Addresses);
            Assert.Null(decoded.Lookup);
            AssertStructurallyEqual(source, decoded);
        }
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_StringKeyedConcreteDictionaries_RoundTripOnEveryRoute(ToonProtocolVersion version)
    {
        var sorted = new SortedDictionary<string, AddressDto> { ["work"] = new("Oslo", "Norway"), ["home"] = new("Reykjavik", "Iceland") };
        var book = new AddressBook { ["billing"] = new("Bergen", "Norway"), ["shipping"] = new("Turku", "Finland") };
        var source = new ConcreteMaps(
            new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 },
            sorted,
            book);

        var rootSorted = RoundTripDirect(sorted, version);
        var rootBook = RoundTripDirect(book, version);

        Assert.IsType<SortedDictionary<string, AddressDto>>(rootSorted);
        Assert.Equal(sorted, rootSorted);
        Assert.IsType<AddressBook>(rootBook);
        Assert.Equal(book, rootBook);
        Assert.Equal(sorted, RoundTripExpanded(sorted, version));
        Assert.Equal(sorted, RoundTripService(sorted, version));
        foreach (var decoded in new[] { RoundTripDirect(source, version), RoundTripExpanded(source, version), RoundTripService(source, version) })
        {
            Assert.Equal(source.Counts, decoded.Counts);
            Assert.Equal(source.Sorted, decoded.Sorted);
            Assert.IsType<AddressBook>(decoded.Book);
            Assert.Equal(source.Book, decoded.Book);
        }
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_NonStringKeyedDictionaries_ThrowNotSupportedOnEveryRoute(ToonProtocolVersion version)
    {
        AssertNotSupportedOnEveryRoute(new Dictionary<int, string> { [1] = "one", [2] = "two" }, version);
        AssertNotSupportedOnEveryRoute(new Dictionary<Guid, string> { [Guid.Parse("8f7c1f4e-0d55-4b8e-9d42-3e1c6b9a7f10")] = "x" }, version);
        AssertNotSupportedOnEveryRoute(new Dictionary<DayOfWeek, int> { [DayOfWeek.Monday] = 1 }, version);
        AssertNotSupportedOnEveryRoute(new SortedDictionary<int, string> { [1] = "one" }, version);
        AssertNotSupportedOnEveryRoute(
            new GuidKeyed("a", new Dictionary<Guid, string> { [Guid.Parse("8f7c1f4e-0d55-4b8e-9d42-3e1c6b9a7f10")] = "x" }),
            version);
        AssertNotSupportedOnEveryRoute(new IntKeyedHolder { Name = "a", Map = new Dictionary<int, string> { [1] = "one" } }, version);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_KeyChainsFoldedBelowTheRoot_ExpandOnPathExpansionRoute(ToonProtocolVersion version)
    {
        var source = new Settings(
            "main",
            new Section(3, new Dictionary<string, AddressDto> { ["home"] = new("Reykjavik", "Iceland") }),
            [new Section(1, new Dictionary<string, AddressDto> { ["work"] = new("Oslo", "Norway") })]);
        var encodeOptions = EncodeOptions(version);
        encodeOptions.KeyFolding = ToonKeyFolding.Safe;
        var toon = ToonEncoder.Encode(source, encodeOptions);
        const string rootArray = "[1]:\n  - Size: 1\n    Map.work:\n      City: Oslo\n      Country: Norway";
        Assert.Contains("\n  Map.home:", toon, StringComparison.Ordinal);

        var decoded = ToonDecoder.Decode<Settings>(toon, DecodeOptions(version, ToonPathExpansion.Safe))!;
        var decodedArray = ToonDecoder.Decode<List<Section>>(rootArray, DecodeOptions(version, ToonPathExpansion.Safe))!;

        AssertStructurallyEqual(source, decoded);
        AssertStructurallyEqual(source.History, decodedArray);
        Assert.Equal(
            JsonSerializer.Serialize(source),
            ToonDecoder.Toon2Json(toon, DecodeOptions(version, ToonPathExpansion.Safe)));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_DottedKeysBelowTheRoot_ExpandUnlessQuoted(ToonProtocolVersion version)
    {
        const string text =
            "Outer:\n" +
            "  \"a.b\": 1\n" +
            "  \"q.r\"[2]: 1,2\n" +
            "  c.d: 2\n" +
            "  rows[1]{\"x.y\",z.w}:\n" +
            "    3,4\n" +
            "  items[1]:\n" +
            "    - e.f: 5\n" +
            "      \"g.h\": 6";
        const string expected =
            "{\"Outer\":{\"a.b\":1,\"q.r\":[1,2],\"c\":{\"d\":2},\"rows\":[{\"x.y\":3,\"z\":{\"w\":4}}],\"items\":[{\"e\":{\"f\":5},\"g.h\":6}]}}";

        Assert.Equal(expected, ToonDecoder.Toon2Json(text, DecodeOptions(version, ToonPathExpansion.Safe)));
        Assert.Equal(
            "{\"q.r\":[1,2],\"c\":{\"d\":2}}",
            ToonDecoder.Toon2Json("\"q.r\"[2]: 1,2\nc.d: 2", DecodeOptions(version, ToonPathExpansion.Safe)));
        Assert.Equal(
            "{\"Outer\":{\"a.b\":1,\"c.d\":2}}",
            ToonDecoder.Toon2Json("Outer:\n  \"a.b\": 1\n  c.d: 2", DecodeOptions(version)));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_ConflictingDottedKeysBelowTheRoot_FollowStrictMode(ToonProtocolVersion version)
    {
        const string text = "Outer:\n  a.b: 1\n  a: 2";
        var strict = DecodeOptions(version, ToonPathExpansion.Safe);
        var lenient = DecodeOptions(version, ToonPathExpansion.Safe);
        lenient.Strict = false;

        Assert.Throws<ToonPathExpansionException>(() => ToonDecoder.Toon2Json(text, strict));
        Assert.Equal("{\"Outer\":{\"a\":2}}", ToonDecoder.Toon2Json(text, lenient));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_ByteArrayTargetWithPathExpansionOff_KeepsDottedKeysLiteral(ToonProtocolVersion version)
    {
        var decoded = ToonDecoder.Decode<DottedBlob>("Data: AQID\na.b: x", DecodeOptions(version))!;

        Assert.Equal([1, 2, 3], decoded.Data);
        Assert.Equal("x", decoded.Dotted);
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RecursiveTypes_RoundTripOnEveryRoute(ToonProtocolVersion version)
    {
        var source = new TreeNode
        {
            Name = "root",
            Children = [new TreeNode { Name = "a", Children = [new TreeNode { Name = "a1" }] }, new TreeNode { Name = "b" }],
            Map = new Dictionary<string, TreeNode> { ["x"] = new TreeNode { Name = "mx" }, ["y"] = new TreeNode { Name = "my" } },
            Next = new TreeNode { Name = "next" }
        };
        var record = new TreeRecord("r", [new TreeRecord("c", [])]);

        var decoded = ToonDecoder.Decode<TreeNode>(ToonEncoder.Encode(source, EncodeOptions(version)), DecodeOptions(version))!;
        var decodedList = ToonDecoder.Decode<List<TreeNode>>(ToonEncoder.Encode(new List<TreeNode> { source }, EncodeOptions(version)), DecodeOptions(version))!;
        var decodedRecord = ToonDecoder.Decode<TreeRecord>(ToonEncoder.Encode(record, EncodeOptions(version, keepEmpty: true)), DecodeOptions(version))!;

        AssertStructurallyEqual(source, decoded);
        AssertStructurallyEqual(new List<TreeNode> { source }, decodedList);
        AssertStructurallyEqual(record, decodedRecord);
        AssertStructurallyEqual(source, RoundTripExpanded(source, version));
        AssertStructurallyEqual(source, RoundTripService(source, version));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_RepeatedDottedKeyBelowTheRoot_LastOccurrenceDecidesExpansion(ToonProtocolVersion version)
    {
        var options = DecodeOptions(version, ToonPathExpansion.Safe);
        options.Strict = false;

        Assert.Equal("{\"Outer\":{\"a\":{\"b\":2}}}", ToonDecoder.Toon2Json("Outer:\n  \"a.b\": 1\n  a.b: 2", options));
        Assert.Equal("{\"Outer\":{\"a.b\":2}}", ToonDecoder.Toon2Json("Outer:\n  a.b: 1\n  \"a.b\": 2", options));
        Assert.Equal("{\"rows\":[{\"a\":{\"b\":2}}]}", ToonDecoder.Toon2Json("rows[1]{\"a.b\",a.b}:\n  1,2", options));
        Assert.Equal("{\"rows\":[{\"a.b\":2}]}", ToonDecoder.Toon2Json("rows[1]{a.b,\"a.b\"}:\n  1,2", options));
        Assert.Equal(
            "{\"rows\":[{\"a\":{\"b\":3},\"id\":2},{\"a.b\":4,\"id\":5}]}",
            ToonDecoder.Toon2Json("rows[2]{\"a.b\",id}:\n  1,2\n    a.b: 3\n  4,5", options));
        Assert.Equal(
            "{\"rows\":[{\"a.b\":3,\"id\":2},{\"a\":{\"b\":4},\"id\":5}]}",
            ToonDecoder.Toon2Json("rows[2]{a.b,id}:\n  1,2\n    \"a.b\": 3\n  4,5", options));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_UnbindableDictionaryMemberAbsentOrNull_DecodesTheOtherMembers(ToonProtocolVersion version)
    {
        AssertUnbindableMember<Dictionary<int, string>>(version, "V:\n  \"1\": a");
        AssertUnbindableMember<Dictionary<Guid, string>>(version, "V:\n  \"8f7c1f4e-0d55-4b8e-9d42-3e1c6b9a7f10\": a");
        AssertUnbindableMember<SortedDictionary<int, string>>(version, "V:\n  \"1\": a");
        AssertUnbindableMember<System.Dynamic.ExpandoObject>(version, "V:\n  k: a");
        AssertUnbindableMember<System.Collections.Hashtable>(version, "V:\n  k: a");
        AssertUnbindableMember<Dictionary<string, Dictionary<int, string>>>(version, "V:\n  k:\n    \"1\": a");
    }

    private static void AssertUnbindableMember<TMap>(ToonProtocolVersion version, string valueLines)
        where TMap : class
    {
        var expanded = DecodeOptions(version, ToonPathExpansion.Safe);
        var service = new ToonService();

        foreach (var holder in new[]
                 {
                     ToonDecoder.Decode<MapHolder<TMap>>("Id: 1\nName: n", DecodeOptions(version))!,
                     ToonDecoder.Decode<MapHolder<TMap>>("Id: 1\nV: null\nName: n", DecodeOptions(version))!,
                     ToonDecoder.Decode<MapHolder<TMap>>("Id: 1\nName: n\nz.q: 1", expanded)!,
                     ToonDecoder.Decode<List<MapHolder<TMap>>>("[2]{Id,Name}:\n  1,n\n  2,m", DecodeOptions(version))![0],
                     service.Deserialize<MapHolder<TMap>>("{\"Id\":1,\"V\":null,\"Name\":\"n\"}", "application/json")!
                 })
        {
            Assert.Equal(1, holder.Id);
            Assert.Equal("n", holder.Name);
            Assert.Null(holder.V);
        }

        var record = ToonDecoder.Decode<MapRecord<TMap>>("Id: 1\nName: n", DecodeOptions(version))!;
        Assert.Equal(1, record.Id);
        Assert.Null(record.V);

        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<MapHolder<TMap>>($"Id: 1\n{valueLines}", DecodeOptions(version)));
        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<MapHolder<TMap>>($"Id: 1\n{valueLines}\nz.q: 1", expanded));
        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<MapRecord<TMap>>($"Id: 1\n{valueLines}", DecodeOptions(version)));
    }

    [Theory]
    [MemberData(nameof(LegacyVersions))]
    public void Decode_QuotedArrayKeyWithBraces_StillExpandsDottedTableFields(ToonProtocolVersion version)
    {
        const string text = "\"x{}\"[1]{a.b}:\n  1";
        var options = DecodeOptions(version, ToonPathExpansion.Safe);

        var typed = ToonDecoder.Decode<BracedTable>(text, options)!;

        Assert.Equal("{\"x{}\":[{\"a\":{\"b\":1}}]}", ToonDecoder.Toon2Json(text, options));
        Assert.Equal(1, typed.Rows![0].A!.B);
    }

    private static void AssertNotSupportedOnEveryRoute<T>(T source, ToonProtocolVersion version)
    {
        var toon = ToonEncoder.Encode(source, EncodeOptions(version));
        var encodeOptions = EncodeOptions(version);
        encodeOptions.KeyFolding = ToonKeyFolding.Safe;
        var folded = ToonEncoder.Encode(new FoldedEnvelope<T>(new FoldedBox<T>(source)), encodeOptions);
        IToonService service = CreateService(version);

        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<T>(toon, DecodeOptions(version)));
        Assert.Throws<NotSupportedException>(() => ToonDecoder.Decode<FoldedEnvelope<T>>(folded, DecodeOptions(version, ToonPathExpansion.Safe)));
        Assert.Throws<NotSupportedException>(() => service.Decode<T>(service.Encode(source)));
    }

    private static T RoundTripDirect<T>(T source, ToonProtocolVersion version, bool keepEmpty = false)
    {
        var toon = ToonEncoder.Encode(source, EncodeOptions(version, keepEmpty));
        var decodeOptions = DecodeOptions(version);
        var beforeMisses = GetDirectMaterializerMisses(typeof(T));

        var decoded = ToonDecoder.Decode<T>(toon, decodeOptions);

        Assert.Equal(beforeMisses, GetDirectMaterializerMisses(typeof(T)));
        Assert.NotNull(decoded);
        AssertStructurallyEqual(decoded!, DecodeWithDirectMaterializer<T>(toon, decodeOptions));
        return decoded!;
    }

    private static T RoundTripExpanded<T>(T source, ToonProtocolVersion version, bool keepEmpty = false)
    {
        var encodeOptions = EncodeOptions(version, keepEmpty);
        encodeOptions.KeyFolding = ToonKeyFolding.Safe;
        var toon = ToonEncoder.Encode(new FoldedEnvelope<T>(new FoldedBox<T>(source)), encodeOptions);
        Assert.StartsWith("Box.Value", toon, StringComparison.Ordinal);
        Assert.Single(toon.Split('\n'), static line => line.Split(':')[0].Contains('.'));

        var decoded = ToonDecoder.Decode<FoldedEnvelope<T>>(toon, DecodeOptions(version, ToonPathExpansion.Safe));

        Assert.NotNull(decoded);
        Assert.NotNull(decoded!.Box.Value);
        return decoded.Box.Value;
    }

    private static T RoundTripService<T>(T source, ToonProtocolVersion version, bool keepEmpty = false)
    {
        IToonService service = CreateService(version, keepEmpty);

        var decoded = service.Decode<T>(service.Encode(source));

        Assert.NotNull(decoded);
        return decoded!;
    }

    private static ToonService CreateService(ToonProtocolVersion version, bool keepEmpty = false)
    {
        var options = new ToonServiceOptions { Indent = 2 };
        options.Encode.ProtocolVersion = version;
        if (keepEmpty)
        {
            options.Encode.IgnoreNullOrEmpty = false;
            options.Encode.ExcludeEmptyArrays = false;
        }

        options.Decode.ProtocolVersion = version;
        return new ToonService(options);
    }

    private static void AssertStructurallyEqual<T>(T expected, T actual)
    {
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static ToonEncodeOptions EncodeOptions(ToonProtocolVersion version, bool keepEmpty = false)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2 };
        if (keepEmpty)
        {
            options.IgnoreNullOrEmpty = false;
            options.ExcludeEmptyArrays = false;
        }

        return options;
    }

    private static ToonDecodeOptions DecodeOptions(ToonProtocolVersion version, ToonPathExpansion expandPaths = ToonPathExpansion.Off) =>
        new() { ProtocolVersion = version, Indent = 2, ExpandPaths = expandPaths };

    private static T DecodeWithDirectMaterializer<T>(string toon, ToonDecodeOptions options)
    {
        var assembly = typeof(ToonDecoder).Assembly;
        var resolved = typeof(ToonDecodeOptions).GetMethod("GetResolvedOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(options, null)!;
        var scanResult = assembly.GetType("DevOp.Toon.Internal.Decode.Scanner")!
            .GetMethod("ToParsedLines", BindingFlags.Static | BindingFlags.Public, null, [typeof(string), typeof(int), typeof(bool)], null)!
            .Invoke(null, [toon, options.Indent, options.Strict])!;
        var lines = scanResult.GetType().GetProperty("Lines")!.GetValue(scanResult);
        var blankLines = scanResult.GetType().GetProperty("BlankLines")!.GetValue(scanResult);
        var cursor = Activator.CreateInstance(assembly.GetType("DevOp.Toon.Internal.Decode.LineCursor")!, lines, blankLines)!;
        var tryDecode = assembly.GetType("DevOp.Toon.Internal.Decode.DirectMaterializer")!
            .GetMethod("TryDecode", BindingFlags.Static | BindingFlags.Public)!
            .MakeGenericMethod(typeof(T));

        object?[] args = [cursor, resolved, null];
        Assert.True((bool)tryDecode.Invoke(null, args)!, $"The direct materializer did not decode {typeof(T)}.");
        Assert.NotNull(args[2]);
        return (T)args[2]!;
    }

    private static int GetDirectMaterializerMisses(Type targetType)
    {
        var typedDecoder = typeof(ToonDecoder).Assembly.GetType("DevOp.Toon.Internal.Decode.TypedDecoder")
            ?? throw new InvalidOperationException("TypedDecoder was not found.");
        var field = typedDecoder.GetField("DirectMaterializerMisses", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TypedDecoder.DirectMaterializerMisses was not found.");
        var misses = field.GetValue(null)
            ?? throw new InvalidOperationException("TypedDecoder.DirectMaterializerMisses was null.");
        var tryGetValue = misses.GetType().GetMethod("TryGetValue")
            ?? throw new InvalidOperationException("DirectMaterializerMisses.TryGetValue was not found.");

        object?[] args = [targetType.FullName ?? targetType.Name, null];
        return (bool)tryGetValue.Invoke(misses, args)! ? (int)args[1]! : 0;
    }

    private static CustomerDto CreateCustomer(string id, string name) =>
        new(
            id,
            name,
            [new("Reykjavik", "Iceland"), new("Oslo", "Norway")],
            ["Primary", "VIP"],
            new Dictionary<string, string> { ["size"] = "medium", ["currency"] = "ISK" });

    private static List<AddressDto> CreateAddresses() => [new("Reykjavik", "Iceland"), new("Oslo", "Norway")];

    private static Dictionary<string, AddressDto> CreateAddressMap() => new()
    {
        ["billing"] = new("Reykjavik", "Iceland"),
        ["shipping"] = new("Oslo", "Norway")
    };

    private static CollectionShapes CreateShapes() =>
        new(
            [new("Reykjavik", "Iceland"), new("Oslo", "Norway")],
            [new("Bergen", "Norway")],
            [new("Akureyri", "Iceland")],
            [new("Turku", "Finland"), new("Espoo", "Finland")],
            [new("Lund", "Sweden")],
            [1, 2, 3],
            ["a", "b"],
            new Dictionary<string, AddressDto> { ["home"] = new("Reykjavik", "Iceland"), ["work"] = new("Oslo", "Norway") },
            new Dictionary<string, int> { ["height"] = 12, ["width"] = 4 },
            new Dictionary<string, string> { ["region"] = "north", ["zone"] = "b" });

    private sealed record CustomerDto(
        string Id,
        string Name,
        IReadOnlyList<AddressDto> Addresses,
        IReadOnlyList<string> Aliases,
        Dictionary<string, string> DefaultDimensions);

    private sealed record AddressDto(string City, string Country);

    private sealed record OptionalShapes(
        IReadOnlyList<string> Tags,
        int[] Numbers,
        Dictionary<string, string> Meta,
        IReadOnlyList<AddressDto>? Addresses,
        IReadOnlyDictionary<string, AddressDto>? Lookup);

    private sealed record CollectionShapes(
        AddressDto[] Array,
        List<AddressDto> Concrete,
        IList<AddressDto> Mutable,
        IReadOnlyList<AddressDto> ReadOnly,
        IEnumerable<AddressDto> Sequence,
        int[] Numbers,
        IList<string> Labels,
        Dictionary<string, AddressDto> Map,
        IDictionary<string, int> MutableMap,
        IReadOnlyDictionary<string, string> ReadOnlyMap);

    private sealed class LowerCaseParameters
    {
        public LowerCaseParameters(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public string Name { get; }

        public int Count { get; }
    }

    private sealed record AliasedThing(
        [property: ToonPropertyName("display_name")] string DisplayName,
        int Count);

    private sealed record Thing(string Name, IReadOnlyList<string> Tags, Dictionary<string, string> Meta);

    private sealed class AddressBook : Dictionary<string, AddressDto>
    {
    }

    private sealed record ConcreteMaps(SortedDictionary<string, int> Counts, SortedDictionary<string, AddressDto> Sorted, AddressBook Book);

    private sealed record GuidKeyed(string Name, IDictionary<Guid, string> Map);

    private sealed class IntKeyedHolder
    {
        public string Name { get; set; } = string.Empty;

        public Dictionary<int, string> Map { get; set; } = new();
    }

    private sealed class DottedBlob
    {
        public byte[] Data { get; set; } = [];

        [ToonPropertyName("a.b")]
        public string? Dotted { get; set; }
    }

    public sealed class MapHolder<TMap>
        where TMap : class
    {
        public int Id { get; set; }

        public TMap? V { get; set; }

        public string? Name { get; set; }
    }

    public sealed record MapRecord<TMap>(int Id, TMap? V = null, string? Name = null)
        where TMap : class;

    private sealed class BracedTable
    {
        [ToonPropertyName("x{}")]
        public List<BracedRow>? Rows { get; set; }
    }

    private sealed class BracedRow
    {
        [ToonPropertyName("a")]
        public BracedLeaf? A { get; set; }
    }

    private sealed class BracedLeaf
    {
        [ToonPropertyName("b")]
        public int B { get; set; }
    }

    private sealed class TreeNode
    {
        public string Name { get; set; } = string.Empty;

        public List<TreeNode>? Children { get; set; }

        public Dictionary<string, TreeNode>? Map { get; set; }

        public TreeNode? Next { get; set; }
    }

    private sealed record TreeRecord(string Name, IReadOnlyList<TreeRecord> Children);

    private sealed record Settings(string Name, Section Current, List<Section> History);

    private sealed record Section(int Size, Dictionary<string, AddressDto> Map);

    private sealed record FoldedEnvelope<T>(FoldedBox<T> Box);

    private sealed record FoldedBox<T>(T Value);
}
