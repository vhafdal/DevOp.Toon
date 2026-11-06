using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DevOp.Toon.Core;
using Xunit;

namespace DevOp.Toon.Tests;

public sealed class VersionedProtocolFixtureTests
{
    public static IEnumerable<object[]> Cases()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "ProtocolFixtures");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        foreach (var snapshot in Directory.GetDirectories(root))
        {
            var tag = Path.GetFileName(snapshot);
            foreach (var category in new[] { "encode", "decode" })
            {
                foreach (var path in Directory.GetFiles(Path.Combine(snapshot, category), "*.json"))
                {
                    using var fixture = JsonDocument.Parse(File.ReadAllText(path));
                    var tests = fixture.RootElement.GetProperty("tests");
                    for (var i = 0; i < tests.GetArrayLength(); i++)
                    {
                        var test = tests[i];
                        if (test.TryGetProperty("minSpecVersion", out var minimum) &&
                            Version.Parse(tag.Substring(1)).CompareTo(Version.Parse(minimum.GetString()!)) < 0) continue;
                        yield return new object[] { tag, category, Path.GetFileName(path), i, test.GetProperty("name").GetString()! };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "ProtocolConformance")]
    public void Codec_PinnedOfficialFixture_MatchesSpecification(string tag, string category, string file, int index, string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProtocolFixtures", tag, category, file)));
        var test = fixture.RootElement.GetProperty("tests")[index];
        var version = ProtocolVersion(tag);
        var hasOptions = test.TryGetProperty("options", out var options);
        var indent = hasOptions && options.TryGetProperty("indentSize", out var indentSize) ? indentSize.GetInt32()
            : hasOptions && options.TryGetProperty("indent", out var oldIndent) ? oldIndent.GetInt32() : 2;
        if (category == "encode")
        {
            var encode = new ToonEncodeOptions
            {
                ProtocolVersion = version,
                Indent = indent,
                IgnoreNullOrEmpty = false,
                ExcludeEmptyArrays = false
            };
            if (hasOptions && options.TryGetProperty("delimiter", out var delimiter))
                encode.Delimiter = delimiter.GetString() switch { "\t" => ToonDelimiter.TAB, "|" => ToonDelimiter.PIPE, _ => ToonDelimiter.COMMA };
            if (hasOptions && options.TryGetProperty("keyFolding", out var folding))
                encode.KeyFolding = string.Equals(folding.GetString(), "safe", StringComparison.OrdinalIgnoreCase) ? ToonKeyFolding.Safe : ToonKeyFolding.Off;
            if (hasOptions && options.TryGetProperty("flattenDepth", out var flatten)) encode.FlattenDepth = flatten.GetInt32();
            var input = ToonNode.Parse(test.GetProperty("input").GetRawText());
            Assert.Equal(test.GetProperty("expected").GetString(), ToonEncoder.Encode(input, encode));
            return;
        }
        var decode = new ToonDecodeOptions { ProtocolVersion = version, Indent = indent };
        if (hasOptions && options.TryGetProperty("strict", out var strict)) decode.Strict = strict.GetBoolean();
        if (hasOptions && options.TryGetProperty("expandPaths", out var expansion))
            decode.ExpandPaths = string.Equals(expansion.GetString(), "safe", StringComparison.OrdinalIgnoreCase) ? ToonPathExpansion.Safe : ToonPathExpansion.Off;
        var source = test.GetProperty("input").GetString()!;
        if (test.TryGetProperty("shouldError", out var shouldError) && shouldError.GetBoolean())
        {
            var error = Record.Exception(() => ToonDecoder.Decode(source, decode));
            Assert.True(error is ToonFormatException or ToonPathExpansionException, $"Expected a protocol error, got {error?.GetType().Name ?? "success"}.");
            return;
        }
        var actual = ToonDecoder.Decode(source, decode);
        var expected = ToonNode.Parse(test.GetProperty("expected").GetRawText());
        Assert.True(ToonNode.DeepEquals(expected, actual), $"{tag}/{file}: {name}; actual: {actual?.ToString()}");
    }

    private static ToonProtocolVersion ProtocolVersion(string tag) => tag switch
    {
        "v3.0.0" => ToonProtocolVersion.V3_0,
        "v4.0.0" => ToonProtocolVersion.V4_0,
        "v4.1.3" => ToonProtocolVersion.V4_1,
        "v4.2.1" => ToonProtocolVersion.V4_2,
        "v4.3.0" => ToonProtocolVersion.V4_3,
        "v4.4.0" => ToonProtocolVersion.V4_4,
        _ => throw new ArgumentOutOfRangeException(nameof(tag))
    };
}
