using System.Reflection;

namespace DevOp.Toon.Tests;

public class Kan1TypedDecodeTests
{
    [Fact]
    public void Decode_RootArrayTarget_WithPositionalRecords_ReturnsArray()
    {
        IToonService service = new ToonService();
        var source = new[]
        {
            CreateCustomer("C-001", "Alpha"),
            CreateCustomer("C-002", "Beta")
        };

        var toon = service.Encode(source);
        var beforeMisses = GetDirectMaterializerMisses(typeof(CustomerDto[]));

        var decoded = service.Decode<CustomerDto[]>(toon);

        Assert.NotNull(decoded);
        Assert.Equal(beforeMisses, GetDirectMaterializerMisses(typeof(CustomerDto[])));
        Assert.Equal(2, decoded!.Length);
        Assert.Equal("C-001", decoded[0].Id);
        Assert.Equal("Beta", decoded[1].Name);
        Assert.Equal("Reykjavik", decoded[0].Addresses[0].City);
    }

    [Fact]
    public void Decode_ListTarget_WithCollectionAndDictionaryProperties_UsesDirectPath()
    {
        IToonService service = new ToonService();
        var source = new List<CustomerDto>
        {
            CreateCustomer("C-001", "Alpha")
        };

        var toon = service.Encode(source);
        var targetType = typeof(List<CustomerDto>);
        var beforeMisses = GetDirectMaterializerMisses(targetType);

        var decoded = service.Decode<List<CustomerDto>>(toon);

        Assert.NotNull(decoded);
        Assert.Equal(beforeMisses, GetDirectMaterializerMisses(targetType));
        var customer = Assert.Single(decoded!);
        Assert.Equal("Alpha", customer.Name);
        Assert.Equal(["Primary", "VIP"], customer.Aliases);
        Assert.Equal("medium", customer.DefaultDimensions["size"]);
        Assert.Equal("Iceland", customer.Addresses[0].Country);
    }

    [Fact]
    public void Decode_InterfaceCollectionTargets_MaterializeAsList()
    {
        IToonService service = new ToonService();
        var source = new List<CustomerDto>
        {
            CreateCustomer("C-001", "Alpha"),
            CreateCustomer("C-002", "Beta")
        };
        var toon = service.Encode(source);

        var list = service.Decode<IList<CustomerDto>>(toon);
        var readOnlyList = service.Decode<IReadOnlyList<CustomerDto>>(toon);
        var enumerable = service.Decode<IEnumerable<CustomerDto>>(toon);

        Assert.NotNull(list);
        Assert.NotNull(readOnlyList);
        Assert.NotNull(enumerable);
        Assert.IsType<List<CustomerDto>>(list);
        Assert.IsType<List<CustomerDto>>(readOnlyList);
        Assert.Equal(["Alpha", "Beta"], enumerable!.Select(static customer => customer.Name));
    }

    [Fact]
    public void Decode_StringKeyDictionaryInterfaces_ReturnExpectedValues()
    {
        var source = new DictionaryEnvelope(
            new Dictionary<string, string>
            {
                ["color"] = "blue"
            },
            new Dictionary<string, int>
            {
                ["height"] = 12
            },
            new Dictionary<string, string>
            {
                ["region"] = "north"
            });

        var toon = ToonEncoder.Encode(source);

        var decoded = ToonDecoder.Decode<DictionaryEnvelope>(toon);

        Assert.NotNull(decoded);
        Assert.Equal("blue", decoded!.Concrete["color"]);
        Assert.Equal(12, decoded.Mutable["height"]);
        Assert.Equal("north", decoded.ReadOnly["region"]);
    }

    [Fact]
    public void Decode_ReadOnlyDictionaryWithObjectValues_UsesDirectPath()
    {
        var source = new ObjectDictionaryEnvelope(
            new Dictionary<string, AddressDto>
            {
                ["billing"] = new("Reykjavik", "Iceland")
            });

        var toon = ToonEncoder.Encode(source);
        var targetType = typeof(ObjectDictionaryEnvelope);
        var beforeMisses = GetDirectMaterializerMisses(targetType);

        var decoded = ToonDecoder.Decode<ObjectDictionaryEnvelope>(toon);

        Assert.NotNull(decoded);
        Assert.Equal(beforeMisses, GetDirectMaterializerMisses(targetType));
        Assert.Equal("Reykjavik", decoded!.AddressesByType["billing"].City);
        Assert.Equal("Iceland", decoded.AddressesByType["billing"].Country);
    }

    [Fact]
    public void Decode_RootDictionaryTargets_ReturnExpectedValues()
    {
        var numeric = ToonDecoder.Decode<Dictionary<string, int>>("a: 1\nb: 2");
        var readOnly = ToonDecoder.Decode<IReadOnlyDictionary<string, string>>("first: alpha\nsecond: beta");

        Assert.NotNull(numeric);
        Assert.Equal(1, numeric!["a"]);
        Assert.Equal(2, numeric["b"]);
        Assert.NotNull(readOnly);
        Assert.Equal("alpha", readOnly!["first"]);
        Assert.IsType<Dictionary<string, string>>(readOnly);
    }

    [Fact]
    public void Decode_PositionalRecord_MatchesConstructorParametersCaseInsensitivelyAndUsesToonPropertyName()
    {
        var decoded = ToonDecoder.Decode<AliasedThing>("display_NAME: Alpha\ncount: 7");

        Assert.NotNull(decoded);
        Assert.Equal("Alpha", decoded!.DisplayName);
        Assert.Equal(7, decoded.Count);
    }

    [Fact]
    public void Decode_PositionalRecord_WithPathExpansion_UsesNativeFallbackConstructorBinding()
    {
        var decoded = ToonDecoder.Decode<PathEnvelope>(
            "user.name: Alice",
            new ToonDecodeOptions
            {
                ExpandPaths = ToonPathExpansion.Safe
            });

        Assert.NotNull(decoded);
        Assert.NotNull(decoded!.User);
        Assert.Equal("Alice", decoded.User.Name);
    }

    private static CustomerDto CreateCustomer(string id, string name)
    {
        return new CustomerDto(
            id,
            name,
            new List<AddressDto>
            {
                new("Reykjavik", "Iceland")
            },
            new List<string> { "Primary", "VIP" },
            new Dictionary<string, string>
            {
                ["size"] = "medium",
                ["currency"] = "ISK"
            });
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

    private sealed record CustomerDto(
        string Id,
        string Name,
        IReadOnlyList<AddressDto> Addresses,
        IReadOnlyList<string> Aliases,
        Dictionary<string, string> DefaultDimensions);

    private sealed record AddressDto(string City, string Country);

    private sealed record DictionaryEnvelope(
        Dictionary<string, string> Concrete,
        IDictionary<string, int> Mutable,
        IReadOnlyDictionary<string, string> ReadOnly);

    private sealed record ObjectDictionaryEnvelope(
        IReadOnlyDictionary<string, AddressDto> AddressesByType);

    private sealed record AliasedThing(
        [property: ToonPropertyName("display_name")] string DisplayName,
        int Count);

    private sealed record PathEnvelope(PathUser User);

    private sealed record PathUser(string Name);
}
