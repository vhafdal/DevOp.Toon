using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevOp.Toon;
using DevOp.Toon.Core;

var input = args.Length > 0 ? args[0] : throw new ArgumentException("Supply the original prods.json path.");
var text = File.ReadAllText(input, Encoding.Unicode);
using var original = JsonDocument.Parse(text);
if (original.RootElement.GetArrayLength() != 2051) throw new InvalidOperationException("Expected all 2051 original rows.");
var reports = new List<object>();
var failed = false;
foreach (var version in new[] { ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4 })
{
    var encoded = ToonEncoder.Json2Toon(text, new ToonEncodeOptions
    {
        ProtocolVersion = version, Indent = 1, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false
    });
    var decodedText = ToonDecoder.Toon2Json(encoded, new ToonDecodeOptions { ProtocolVersion = version, Indent = 1 });
    using var decoded = JsonDocument.Parse(decodedText);
    var difference = Compare(original.RootElement, decoded.RootElement, "$");
    failed |= difference != null;
    reports.Add(new
    {
        Version = ToonProtocolVersions.Format(version), Correct = difference == null, Difference = difference,
        OriginalRows = original.RootElement.GetArrayLength(), DecodedRows = decoded.RootElement.GetArrayLength(),
        OriginalRowsWithPublication = Publications(original.RootElement), DecodedRowsWithPublication = Publications(decoded.RootElement),
        EncodedUtf8Bytes = Encoding.UTF8.GetByteCount(encoded), EncodedSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded)))
    });
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    Scope = "One-shot original raw JSON DOM roundtrip, no DTO projection or timing claims",
    NumericComparison = "Exact mathematical decimal coefficient/exponent; object order ignored, array order and all strings/nulls/properties preserved",
    SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
    LibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ToonEncoder).Assembly.Location))), Reports = reports
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

static int Publications(JsonElement root) => root.EnumerateArray().Count(row => row.TryGetProperty("Publication", out _));

static string? Compare(JsonElement expected, JsonElement actual, string path)
{
    if (expected.ValueKind != actual.ValueKind) return path + ": value kind differs";
    if (expected.ValueKind == JsonValueKind.Object)
    {
        if (expected.EnumerateObject().Count() != actual.EnumerateObject().Count()) return path + ": property count differs";
        foreach (var property in expected.EnumerateObject())
        {
            if (!actual.TryGetProperty(property.Name, out var value)) return path + "." + property.Name + ": missing property";
            var difference = Compare(property.Value, value, path + "." + property.Name);
            if (difference != null) return difference;
        }
    }
    else if (expected.ValueKind == JsonValueKind.Array)
    {
        if (expected.GetArrayLength() != actual.GetArrayLength()) return path + ": array length differs";
        for (var i = 0; i < expected.GetArrayLength(); i++)
        {
            var difference = Compare(expected[i], actual[i], path + "[" + i + "]");
            if (difference != null) return difference;
        }
    }
    else if (expected.ValueKind == JsonValueKind.Number)
    {
        if (Number(expected.GetRawText()) != Number(actual.GetRawText())) return path + ": mathematical numeric value differs";
    }
    else if (expected.ValueKind == JsonValueKind.String && !string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal))
        return path + ": string differs";
    return null;
}

static (bool Negative, string Digits, long Exponent) Number(string text)
{
    var negative = text[0] == '-';
    var start = negative ? 1 : 0;
    var exponentAt = text.IndexOf('e');
    if (exponentAt < 0) exponentAt = text.IndexOf('E');
    var end = exponentAt < 0 ? text.Length : exponentAt;
    var exponent = exponentAt < 0 ? 0 : long.Parse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    var mantissa = text.Substring(start, end - start);
    var point = mantissa.IndexOf('.');
    if (point >= 0) exponent -= mantissa.Length - point - 1;
    var digits = mantissa.Replace(".", "").TrimStart('0');
    if (digits.Length == 0) return (false, "0", 0);
    var length = digits.Length;
    while (length > 1 && digits[length - 1] == '0') { length--; exponent++; }
    return (negative, digits.Substring(0, length), exponent);
}
