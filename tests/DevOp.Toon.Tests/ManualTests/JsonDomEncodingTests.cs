using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

public sealed class JsonDomEncodingTests
{
    private const string ObjectJson =
        "{\"id\":1,\"name\":\"a b\",\"price\":1.5,\"active\":true,\"missing\":null,\"note\":\"\",\"tags\":[\"x\",\"y\"]," +
        "\"rows\":[{\"a\":1,\"b\":\"x\"},{\"a\":2,\"b\":\"y\"}],\"mixed\":[1,{\"k\":\"v\"},[2,3]],\"empty\":[],\"none\":{}," +
        "\"nested\":{\"deep\":{\"leaf\":\"z\"},\"list\":[{\"p\":1,\"q\":[1,2]},{\"p\":2,\"q\":[]}]}}";

    private static readonly string[] Shapes =
    {
        ObjectJson,
        "{}",
        "[]",
        "[1,\"two\",null,true,2.5]",
        "[{\"a\":1,\"b\":\"x\"},{\"a\":2,\"b\":\"y\"}]",
        "[{\"a\":1},{\"b\":[1,2]}]",
        "[[1,2],[3]]",
        "5",
        "-0.125",
        "12345678901234567890",
        "\"text, with: delimiters\"",
        "\"\"",
        "true",
        "null",
    };

    public static TheoryData<ToonProtocolVersion> AllVersions => new()
    {
        ToonProtocolVersion.V3_0, ToonProtocolVersion.CustomLegacy,
        ToonProtocolVersion.V4_0, ToonProtocolVersion.V4_1, ToonProtocolVersion.V4_2,
        ToonProtocolVersion.V4_3, ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4
    };

    [Theory]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    public void Encode_JsonElement_UsesJsonNormalization(ToonProtocolVersion version)
    {
        using var document = JsonDocument.Parse("{\"Id\":1,\"Items\":[]}");
        var options = new ToonEncodeOptions { ProtocolVersion = version, ExcludeEmptyArrays = false, IgnoreNullOrEmpty = false };
        var text = ToonEncoder.Encode(document.RootElement, options);
        var value = ToonDecoder.Decode(text, new ToonDecodeOptions { ProtocolVersion = version, Indent = 1 });
        Assert.Equal(1L, value!["Id"]!.GetValue<long>());
        Assert.Empty(value["Items"]!.AsArray());
    }

    [Fact]
    public void Encode_JsonDocument_UsesJsonNormalization()
    {
        using var document = JsonDocument.Parse("{\"Id\":1}");
        Assert.Equal("Id: 1", ToonEncoder.Encode(document, new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 }));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Encode_RootJsonNode_MatchesJsonText(ToonProtocolVersion version)
    {
        foreach (var options in Variants(version))
        {
            foreach (var json in Shapes)
            {
                var node = JsonNode.Parse(json);
                var expected = ToonEncoder.Json2Toon(node?.ToJsonString() ?? "null", options);
                var label = Label(version, options, json);

                Assert.True(expected == ToonEncoder.Encode(node, options), label);
                Assert.True(expected == ToonEncoder.Encode<JsonNode?>(node, options), label + " <JsonNode>");
                Assert.True(expected == Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(node, options)), label + " bytes");
                using var stream = new MemoryStream();
                ToonEncoder.EncodeToStream(node, stream, options);
                Assert.True(expected == Encoding.UTF8.GetString(stream.ToArray()), label + " stream");
                if (node is JsonObject jsonObject)
                    Assert.True(expected == ToonEncoder.Encode<JsonObject>(jsonObject, options), label + " <JsonObject>");
                if (node is JsonArray jsonArray)
                    Assert.True(expected == ToonEncoder.Encode<JsonArray>(jsonArray, options), label + " <JsonArray>");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Encode_CreatedJsonValue_MatchesJsonText(ToonProtocolVersion version)
    {
        var values = new JsonNode?[]
        {
            JsonValue.Create(42),
            JsonValue.Create(1.10m),
            JsonValue.Create(2.5d),
            JsonValue.Create("a, b"),
            JsonValue.Create(false),
            JsonValue.Create(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc)),
            JsonValue.Create(Guid.Parse("8a0e5f0e-2f39-4c35-9c3b-1d9e5f7c4a11")),
            new JsonObject { ["n"] = 1, ["s"] = "t", ["a"] = new JsonArray(1, 2, JsonValue.Create("x")) },
        };

        var options = new ToonEncodeOptions { ProtocolVersion = version };
        foreach (var value in values)
        {
            var json = value!.ToJsonString();
            Assert.True(ToonEncoder.Json2Toon(json, options) == ToonEncoder.Encode(value, options), Label(version, options, json));
            var holder = new Holder<JsonNode?> { Id = 7, Name = "n", Data = value };
            Assert.True(ToonEncoder.Json2Toon(JsonSerializer.Serialize(holder), options) == ToonEncoder.Encode(holder, options), Label(version, options, json) + " member");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Encode_NestedJsonNode_MatchesJsonText(ToonProtocolVersion version)
    {
        var options = new ToonEncodeOptions { ProtocolVersion = version };
        foreach (var json in Shapes)
        {
            if (json == "[]")
                continue;

            var label = Label(version, options, json);
            AssertMatchesJson(new Holder<JsonNode?> { Id = 7, Name = "n", Data = JsonNode.Parse(json) }, options, label + " member");
            AssertMatchesJson(new Holder<object?> { Id = 7, Name = "n", Data = JsonNode.Parse(json) }, options, label + " object member");
            AssertMatchesJson(new List<JsonNode?> { JsonNode.Parse(json), JsonNode.Parse(json) }, options, label + " list");
            AssertMatchesJson(new Dictionary<string, JsonNode?> { ["first"] = JsonNode.Parse(json), ["second"] = null }, options, label + " dictionary");
            AssertMatchesJson(new Dictionary<string, object?> { ["first"] = JsonNode.Parse(json), ["n"] = 3 }, options, label + " object dictionary");
            if (version != ToonProtocolVersion.CustomV4_4)
                AssertMatchesJson(Rows(JsonNode.Parse(json), JsonNode.Parse(json)), options, label + " rows");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Encode_NestedJsonNode_NeverDivergesFromJsonTextAndJsonElement(ToonProtocolVersion version)
    {
        // Where a CLR container already writes a JsonElement differently from the JSON text, a JsonNode may follow either.
        foreach (var options in Variants(version))
        {
            foreach (var json in Shapes)
            {
                using var document = JsonDocument.Parse(json);
                var element = document.RootElement;
                var label = Label(version, options, json);
                AssertMatchesJsonOrElement(new Holder<JsonNode?> { Id = 7, Name = "n", Data = JsonNode.Parse(json) }, new Holder<JsonElement> { Id = 7, Name = "n", Data = element }, options, label + " member");
                AssertMatchesJsonOrElement(new Holder<object?> { Id = 7, Name = "n", Data = JsonNode.Parse(json) }, new Holder<object?> { Id = 7, Name = "n", Data = element }, options, label + " object member");
                AssertMatchesJsonOrElement(new List<JsonNode?> { JsonNode.Parse(json), JsonNode.Parse(json) }, new List<JsonElement> { element, element }, options, label + " list");
                AssertMatchesJsonOrElement(new List<object?> { JsonNode.Parse(json), 3 }, new List<object?> { element, 3 }, options, label + " object list");
                AssertMatchesJsonOrElement(new Dictionary<string, JsonNode?> { ["first"] = JsonNode.Parse(json), ["second"] = null }, new Dictionary<string, object?> { ["first"] = element, ["second"] = null }, options, label + " dictionary");
                AssertMatchesJsonOrElement(Rows(JsonNode.Parse(json), JsonNode.Parse(json)), Rows(element, element), options, label + " rows");
            }
        }
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0, "[2]{Id,Name,Payload}:\n 1,a,5\n 2,b,x")]
    [InlineData(ToonProtocolVersion.CustomLegacy, "[2]{Id,Name,Payload}:\n 1,a,5\n 2,b,x")]
    [InlineData(ToonProtocolVersion.V4_4, "[2]{Id,Name,Payload}:\n 1,a,5\n 2,b,x")]
    [InlineData(ToonProtocolVersion.CustomV4_4, "[2]{Id,Name}:\n 1,a\n  Payload: 5\n 2,b\n  Payload: x")]
    public void Encode_UniformRowsWithJsonNodeMember_Encodes(ToonProtocolVersion version, string expected)
    {
        var rows = Rows<JsonNode?>(JsonValue.Create(5), JsonValue.Create("x"));
        var options = new ToonEncodeOptions { ProtocolVersion = version };

        Assert.Equal(expected, ToonEncoder.Encode(rows, options));
        Assert.Equal(expected, Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(rows, options)));
    }

    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    public void Encode_LegacyMemberOmittedByOptions_WritesNoBlankLine(ToonProtocolVersion version)
    {
        using var document = JsonDocument.Parse("[]");
        var options = new ToonEncodeOptions { ProtocolVersion = version, IgnoreNullOrEmpty = true, ExcludeEmptyArrays = true };

        Assert.Equal("Id: 7\nName: n", ToonEncoder.Encode(new Holder<JsonElement> { Id = 7, Name = "n", Data = document.RootElement }, options));
        Assert.Equal("Id: 7\nName: n\nData:\n a: 1", ToonEncoder.Encode(new Holder<JsonNode?> { Id = 7, Name = "n", Data = JsonNode.Parse("{\"e\":\"\",\"a\":1,\"z\":[]}") }, options));
    }

    private static IEnumerable<ToonEncodeOptions> Variants(ToonProtocolVersion version)
    {
        yield return new ToonEncodeOptions { ProtocolVersion = version };
        var flipped = new ToonEncodeOptions { ProtocolVersion = version };
        flipped.IgnoreNullOrEmpty = !flipped.IgnoreNullOrEmpty;
        flipped.ExcludeEmptyArrays = !flipped.ExcludeEmptyArrays;
        yield return flipped;
        yield return version is ToonProtocolVersion.V3_0 or ToonProtocolVersion.CustomLegacy
            ? new ToonEncodeOptions { ProtocolVersion = version, KeyFolding = ToonKeyFolding.Safe, Delimiter = ToonDelimiter.PIPE }
            : new ToonEncodeOptions { ProtocolVersion = version, Delimiter = ToonDelimiter.PIPE };
    }

    private static void AssertMatchesJson(object value, ToonEncodeOptions options, string label)
    {
        var expected = ToonEncoder.Json2Toon(JsonSerializer.Serialize(value), options);
        Assert.True(expected == ToonEncoder.Encode(value, options), label);
        Assert.True(expected == Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(value, options)), label + " bytes");
    }

    private static void AssertMatchesJsonOrElement(object nodeValue, object elementValue, ToonEncodeOptions options, string label)
    {
        var json = ToonEncoder.Json2Toon(JsonSerializer.Serialize(nodeValue), options);
        var element = ToonEncoder.Encode(elementValue, options);
        var node = ToonEncoder.Encode(nodeValue, options);
        Assert.True(node == json || node == element, label + "\n--- json\n" + json + "\n--- element\n" + element + "\n--- node\n" + node);
        Assert.True(node == Encoding.UTF8.GetString(ToonEncoder.EncodeToBytes(nodeValue, options)), label + " bytes");
    }

    private static List<Row<T>> Rows<T>(T first, T second)
        => new() { new() { Id = 1, Name = "a", Payload = first }, new() { Id = 2, Name = "b", Payload = second } };

    private static string Label(ToonProtocolVersion version, ToonEncodeOptions options, string json)
        => $"{version} ignore={options.IgnoreNullOrEmpty} exclude={options.ExcludeEmptyArrays} folding={options.KeyFolding} delimiter={options.Delimiter} {json}";

    private sealed class Holder<T>
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public T Data { get; set; } = default!;
    }

    private sealed class Row<T>
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public T Payload { get; set; } = default!;
    }
}
