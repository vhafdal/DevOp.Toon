#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Text;
using DevOp.Toon;

namespace DevOp.Toon.Tests;

public class DictionaryEncodingTests
{
    public static TheoryData<ToonProtocolVersion> AllProfiles => new()
    {
        ToonProtocolVersion.V3_0,
        ToonProtocolVersion.CustomLegacy,
        ToonProtocolVersion.V4_4,
        ToonProtocolVersion.CustomV4_4,
    };

    private static ToonEncodeOptions Encode(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    private static ToonDecodeOptions Decode(ToonProtocolVersion version) => new() { ProtocolVersion = version, Indent = 2 };

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootStringDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new Dictionary<string, string> { ["k"] = "v", ["other"] = "w" };

        Assert.Equal("k: v\nother: w", ToonEncoder.Encode(data, Encode(version)));
        Assert.Equal("k: v\nother: w", ToonEncoder.Encode((object)data, Encode(version)));
        Assert.Equal("k: v\nother: w", Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(data, Encode(version))));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootObjectDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "Ada",
            ["tags"] = new[] { "a", "b" },
            ["child"] = new Dictionary<string, object?> { ["x"] = 2 },
        };

        var toon = ToonEncoder.Encode(data, Encode(version));

        Assert.StartsWith("id: 1\nname: Ada\ntags[2]", toon);
        Assert.Contains("child:\n  x: 2", toon);
        Assert.DoesNotContain("{Key", toon);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootReadOnlyDictionary_RoundTripsThroughTypedDecode(ToonProtocolVersion version)
    {
        IReadOnlyDictionary<string, string> data = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string> { ["k"] = "v", ["other"] = "w" });

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<IReadOnlyDictionary<string, string>>(toon, Decode(version));

        Assert.Equal("k: v\nother: w", toon);
        Assert.NotNull(decoded);
        Assert.Equal(data, decoded!);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootIDictionary_RoundTripsThroughTypedDecode(ToonProtocolVersion version)
    {
        IDictionary<string, int> data = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<IDictionary<string, int>>(toon, Decode(version));
        var concrete = ToonDecoder.Decode<Dictionary<string, int>>(toon, Decode(version));

        Assert.Equal("a: 1\nb: 2", toon);
        Assert.Equal(data, decoded!);
        Assert.Equal(data, concrete!);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootListOfKeyValuePairs_StaysArray(ToonProtocolVersion version)
    {
        var data = new List<KeyValuePair<string, string>> { new("k", "v") };

        Assert.StartsWith("[1]", ToonEncoder.Encode(data, Encode(version)));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootGenericOnlyReadOnlyDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new ReadOnlyOnlyMap(("k", "v"), ("other", "w"));

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<IReadOnlyDictionary<string, string>>(toon, Decode(version));

        Assert.Equal("k: v\nother: w", toon);
        Assert.Equal("k: v\nother: w", ToonEncoder.Encode((object)data, Encode(version)));
        Assert.Equal("k: v\nother: w", Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(data, Encode(version))));
        Assert.Equal(new Dictionary<string, string> { ["k"] = "v", ["other"] = "w" }, decoded!);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootGenericOnlyDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new GenericOnlyMap { ["a"] = 1, ["b"] = 2 };

        Assert.Equal("a: 1\nb: 2", ToonEncoder.Encode(data, Encode(version)));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootExpandoObject_EmitsObject(ToonProtocolVersion version)
    {
        dynamic data = new ExpandoObject();
        data.id = 7;
        data.name = "Ada";

        Assert.Equal("id: 7\nname: Ada", ToonEncoder.Encode((object)data, Encode(version)));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_DictionaryProperties_EmitObjects(ToonProtocolVersion version)
    {
        var data = new MapHolder
        {
            Id = 1,
            Custom = new ReadOnlyOnlyMap(("k", "v")),
            ReadOnly = new Dictionary<string, string> { ["r"] = "s" },
            Mutable = new Dictionary<string, int> { ["m"] = 3 },
            Untyped = new GenericOnlyMap { ["u"] = 4 },
        };

        var toon = ToonEncoder.Encode(data, Encode(version));

        Assert.Equal("Id: 1\nCustom:\n  k: v\nReadOnly:\n  r: s\nMutable:\n  m: 3\nUntyped:\n  u: 4", toon);
        Assert.DoesNotContain("Key", toon);

        var decoded = ToonDecoder.Decode<MapHolderDto>(toon, Decode(version))!;
        Assert.Equal(new Dictionary<string, string> { ["k"] = "v" }, decoded.Custom!);
        Assert.Equal(new Dictionary<string, string> { ["r"] = "s" }, decoded.ReadOnly!);
        Assert.Equal(new Dictionary<string, int> { ["m"] = 3 }, decoded.Mutable!);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_InterfaceTypedPropertiesHoldingDictionary_EmitObjects(ToonProtocolVersion version)
    {
        var data = new InterfaceHolder
        {
            ReadOnly = new Dictionary<string, string> { ["r"] = "s" },
            Mutable = new Dictionary<string, int> { ["m"] = 3 },
        };

        Assert.Equal("ReadOnly:\n  r: s\nMutable:\n  m: 3", ToonEncoder.Encode(data, Encode(version)));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_ListOfObjectsWithDictionaryProperties_NeverEmitsKeyValueTables(ToonProtocolVersion version)
    {
        var data = new List<MapRow>
        {
            new() { Id = 1, Tags = new ReadOnlyOnlyMap(("a", "x")) },
            new() { Id = 2, Tags = new ReadOnlyOnlyMap(("b", "y")) },
        };

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<List<MapRowDto>>(toon, Decode(version))!;

        Assert.DoesNotContain("Key", toon);
        Assert.Equal(2, decoded.Count);
        Assert.Equal("x", decoded[0].Tags!["a"]);
        Assert.Equal("y", decoded[1].Tags!["b"]);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_RootListOfGenericOnlyDictionaries_EmitsListOfObjects(ToonProtocolVersion version)
    {
        var data = new List<ReadOnlyOnlyMap> { new(("a", "x")), new(("b", "y")) };

        var toon = ToonEncoder.Encode(data, Encode(version));
        var decoded = ToonDecoder.Decode<List<Dictionary<string, string>>>(toon, Decode(version))!;

        Assert.DoesNotContain("Key", toon);
        Assert.Equal("x", decoded[0]["a"]);
        Assert.Equal("y", decoded[1]["b"]);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_EnumerableTypedPropertyHoldingDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var populated = new EnumerableHolder { Pairs = new ReadOnlyOnlyMap(("k", "v")) };
        var empty = new EnumerableHolder { Pairs = new ReadOnlyOnlyMap() };
        var options = Encode(version);
        options.ExcludeEmptyArrays = true;

        Assert.Equal("Pairs:\n  k: v", ToonEncoder.Encode(populated, Encode(version)));
        Assert.DoesNotContain("[0]", ToonEncoder.Encode(empty, options));
        Assert.DoesNotContain("Key", ToonEncoder.Encode(new List<EnumerableHolder> { populated, populated }, Encode(version)));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Encode_TypeThatIsListAndDictionary_EmitsObject(ToonProtocolVersion version)
    {
        var data = new ListAndMap(("k", "v"), ("other", "w"));

        Assert.Equal("k: v\nother: w", ToonEncoder.Encode(data, Encode(version)));
        Assert.Equal("Pairs:\n  k: v\n  other: w", ToonEncoder.Encode(new EnumerableHolder { Pairs = data }, Encode(version)));

        var list = new List<ListAndMap> { new(("a", "x")), new(("b", "y")) };
        var toon = ToonEncoder.Encode(list, Encode(version));
        var decoded = ToonDecoder.Decode<List<Dictionary<string, string>>>(toon, Decode(version))!;

        Assert.DoesNotContain("Key", toon);
        Assert.Equal("x", decoded[0]["a"]);
        Assert.Equal("y", decoded[1]["b"]);
    }

    public sealed class EnumerableHolder
    {
        public IEnumerable<KeyValuePair<string, string>>? Pairs { get; set; }
    }

    /// <summary>Implements both <see cref="IList"/> and <see cref="IReadOnlyDictionary{TKey,TValue}"/>.</summary>
    public sealed class ListAndMap : List<KeyValuePair<string, string>>, IReadOnlyDictionary<string, string>
    {
        public ListAndMap(params (string Key, string Value)[] pairs)
        {
            foreach (var (key, value) in pairs)
                Add(new KeyValuePair<string, string>(key, value));
        }

        public string this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
        public IEnumerable<string> Keys => ConvertAll(e => e.Key);
        public IEnumerable<string> Values => ConvertAll(e => e.Value);
        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public bool TryGetValue(string key, out string value)
        {
            foreach (var entry in this)
            {
                if (entry.Key == key)
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }
    }

    public sealed class MapHolder
    {
        public int Id { get; set; }
        public ReadOnlyOnlyMap? Custom { get; set; }
        public IReadOnlyDictionary<string, string>? ReadOnly { get; set; }
        public IDictionary<string, int>? Mutable { get; set; }
        public object? Untyped { get; set; }
    }

    public sealed class InterfaceHolder
    {
        public IReadOnlyDictionary<string, string>? ReadOnly { get; set; }
        public IDictionary<string, int>? Mutable { get; set; }
    }

    public sealed class MapHolderDto
    {
        public int Id { get; set; }
        public IReadOnlyDictionary<string, string>? Custom { get; set; }
        public IReadOnlyDictionary<string, string>? ReadOnly { get; set; }
        public IDictionary<string, int>? Mutable { get; set; }
    }

    public sealed class MapRow
    {
        public int Id { get; set; }
        public ReadOnlyOnlyMap? Tags { get; set; }
    }

    public sealed class MapRowDto
    {
        public int Id { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
    }

    /// <summary>Implements only <see cref="IReadOnlyDictionary{TKey,TValue}"/>, not the non-generic IDictionary.</summary>
    public sealed class ReadOnlyOnlyMap : IReadOnlyDictionary<string, string>
    {
        private readonly List<KeyValuePair<string, string>> entries = new();

        public ReadOnlyOnlyMap(params (string Key, string Value)[] pairs)
        {
            foreach (var (key, value) in pairs)
                entries.Add(new KeyValuePair<string, string>(key, value));
        }

        public string this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
        public IEnumerable<string> Keys => entries.ConvertAll(e => e.Key);
        public IEnumerable<string> Values => entries.ConvertAll(e => e.Value);
        public int Count => entries.Count;
        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public bool TryGetValue(string key, out string value)
        {
            foreach (var entry in entries)
            {
                if (entry.Key == key)
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Implements only <see cref="IDictionary{TKey,TValue}"/>, not the non-generic IDictionary.</summary>
    public sealed class GenericOnlyMap : IDictionary<string, int>
    {
        private readonly Dictionary<string, int> inner = new();

        public int this[string key] { get => inner[key]; set => inner[key] = value; }
        public ICollection<string> Keys => inner.Keys;
        public ICollection<int> Values => inner.Values;
        public int Count => inner.Count;
        public bool IsReadOnly => false;
        public void Add(string key, int value) => inner.Add(key, value);
        public void Add(KeyValuePair<string, int> item) => inner.Add(item.Key, item.Value);
        public void Clear() => inner.Clear();
        public bool Contains(KeyValuePair<string, int> item) => ((ICollection<KeyValuePair<string, int>>)inner).Contains(item);
        public bool ContainsKey(string key) => inner.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, int>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, int>>)inner).CopyTo(array, arrayIndex);
        public bool Remove(string key) => inner.Remove(key);
        public bool Remove(KeyValuePair<string, int> item) => ((ICollection<KeyValuePair<string, int>>)inner).Remove(item);
        public bool TryGetValue(string key, out int value) => inner.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
