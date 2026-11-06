using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon;
using DevOp.Toon.Core;
using DevOp.Toon.Benchmarks.Models;

var inputDir = Arg("--data") ?? FindData();
var repetitions = int.Parse(Arg("--repeats") ?? "5");
var warmups = int.Parse(Arg("--warmups") ?? "5");
var selected = Arg("--profiles");
var payloadDirectory = Arg("--export-payloads");
var measureAuto = args.Contains("--measure-auto");
if (payloadDirectory != null) Directory.CreateDirectory(payloadDirectory);
var serializer = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString };
using var inputReader = new StreamReader(Path.Combine(inputDir, "prods.json"), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
var source = inputReader.ReadToEnd();
var products = JsonSerializer.Deserialize<List<Product>>(source, serializer) ?? throw new InvalidOperationException("Missing products.");
if (products.Count != 2051) throw new InvalidOperationException($"Expected all 2051 rows; got {products.Count}.");
var expectedJson = JsonSerializer.Serialize(products, serializer);
if (payloadDirectory != null) File.WriteAllBytes(Path.Combine(payloadDirectory, "source-dto.json"), Encoding.UTF8.GetBytes(expectedJson));
var result = new Report
{
    Warmups = warmups,
    SamplesPerOperation = repetitions,
    MeasureAutoRequested = measureAuto,
#if BASELINE
    Build = "baseline-6a61143",
#else
    Build = "candidate",
#endif
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    LibraryDebuggingFlags = typeof(ToonEncoder).Assembly.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>()?.DebuggingFlags.ToString(),
    LibraryAssembly = typeof(ToonEncoder).Assembly.Location,
    LibrarySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ToonEncoder).Assembly.Location))),
    Rows = products.Count,
    SourceBytes = new FileInfo(Path.Combine(inputDir, "prods.json")).Length,
    SourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(inputDir, "prods.json")))),
    Notes = "Per-operation time/allocation/GC samples exclude setup; absolute process memory includes fixture/output setup; process peak working set is cumulative, not attributable to an individual method. Timed samples use no forced GC: ManagedHeapDeltaBytes is an uncollected heap delta, not retained memory. Separate untimed retained-memory snapshots force GC after all timed samples for a profile, include the returned result, and can trigger pool trimming; each next profile warms up again before timing. Warmups and correctness checks excluded from timings. No JSON fallback is used to decode TOON. Compact omission profiles intentionally permit loss of empty/null distinctions."
};
var profiles = new List<(string Name, ToonEncodeOptions Encode, ToonDecodeOptions Decode, bool Lossless)>();
#if BASELINE
profiles.Add(("official3-equivalent", Encode(ToonObjectArrayLayout.Auto, false), Decode(), true));
profiles.Add(("custom-legacy-lossless", Encode(ToonObjectArrayLayout.Columnar, false), Decode(), true));
profiles.Add(("custom-legacy-compact", Encode(ToonObjectArrayLayout.Columnar, true), Decode(), false));
#else
profiles.Add(("official3-equivalent", Encode(ToonObjectArrayLayout.Auto, false, ToonProtocolVersion.V3_0), Decode(ToonProtocolVersion.V3_0), true));
profiles.Add(("custom-legacy-lossless", Encode(ToonObjectArrayLayout.Columnar, false, ToonProtocolVersion.CustomLegacy), Decode(ToonProtocolVersion.CustomLegacy), true));
profiles.Add(("official4.4-lossless", Encode(ToonObjectArrayLayout.Auto, false, ToonProtocolVersion.V4_4), Decode(ToonProtocolVersion.V4_4), true));
profiles.Add(("custom4.4-lossless", Encode(ToonObjectArrayLayout.Columnar, false, ToonProtocolVersion.CustomV4_4), Decode(ToonProtocolVersion.CustomV4_4), true));
profiles.Add(("custom-legacy-compact", Encode(ToonObjectArrayLayout.Columnar, true, ToonProtocolVersion.CustomLegacy), Decode(ToonProtocolVersion.CustomLegacy), false));
profiles.Add(("custom4.4-compact", Encode(ToonObjectArrayLayout.Columnar, true, ToonProtocolVersion.CustomV4_4), Decode(ToonProtocolVersion.CustomV4_4), false));
#endif
foreach (var profile in profiles)
{
    if (selected != null && !selected.Split(',').Contains(profile.Name)) continue;
    Console.Error.WriteLine($"Running {profile.Name}: all {products.Count} rows");
    var measurement = new Profile { Name = profile.Name, ExactPreservationRequired = profile.Lossless };
    result.Profiles.Add(measurement);
    try
    {
        var text = ToonEncoder.Encode(products, profile.Encode);
        var decoded = ToonDecoder.Decode<List<Product>>(text, profile.Decode);
        measurement.DecodedRows = decoded?.Count;
        var decodedJson = JsonSerializer.Serialize(decoded, serializer);
        measurement.RoundTripDifference = Difference(expectedJson, decodedJson);
        measurement.EncodedSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        measurement.DecodedModelSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(decodedJson)));
        measurement.Correct = measurement.DecodedRows == 2051 && measurement.RoundTripDifference == null;
        measurement.Characters = text.Length;
        var utf8 = Encoding.UTF8.GetBytes(text);
        if (payloadDirectory != null) File.WriteAllBytes(Path.Combine(payloadDirectory, result.Build + "-" + profile.Name + ".toon"), utf8);
        measurement.Utf8Bytes = utf8.Length;
        measurement.GzipBytes = Compress(utf8, false);
        measurement.BrotliBytes = Compress(utf8, true);
        measurement.DirectEncodeProbe = DirectEncode(products, profile.Encode);
        var misses = Misses();
        for (var i = 0; i < warmups; i++)
        {
            GC.KeepAlive(ToonEncoder.Encode(products, profile.Encode));
            GC.KeepAlive(ToonDecoder.Decode<List<Product>>(text, profile.Decode));
        }
        measurement.Encode = Measure(() => ToonEncoder.Encode(products, profile.Encode));
        measurement.Decode = Measure(() => ToonDecoder.Decode<List<Product>>(text, profile.Decode));
#if !BASELINE
        if (measureAuto && profile.Decode.ProtocolVersion is ToonProtocolVersion.V4_4 or ToonProtocolVersion.CustomV4_4)
        {
            var autoOptions = profile.Decode.Clone();
            autoOptions.ProtocolVersion = ToonProtocolVersion.Auto;
            try
            {
                var autoDecoded = ToonDecoder.Decode<List<Product>>(text, autoOptions);
                var autoJson = JsonSerializer.Serialize(autoDecoded, serializer);
                measurement.AutoDecodedRows = autoDecoded?.Count;
                measurement.AutoDecodedModelSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(autoJson)));
                measurement.AutoMatchesExplicit = measurement.AutoDecodedModelSha256 == measurement.DecodedModelSha256;
                measurement.AutoRoundTripDifference = Difference(expectedJson, autoJson);
                for (var i = 0; i < warmups; i++) GC.KeepAlive(ToonDecoder.Decode<List<Product>>(text, autoOptions));
                measurement.AutoDecode = Measure(() => ToonDecoder.Decode<List<Product>>(text, autoOptions));
            }
            catch (Exception error) { measurement.AutoError = error.ToString(); }
        }
#endif
        measurement.EncodeRetainedManagedBytes = Retained(() => ToonEncoder.Encode(products, profile.Encode));
        measurement.DecodeRetainedManagedBytes = Retained(() => ToonDecoder.Decode<List<Product>>(text, profile.Decode));
        measurement.LegacyDirectMaterializerMisses = Misses() - misses;
#if BASELINE
        measurement.DecodeEngine = "legacy typed decoder; miss delta records native fallback";
#else
        measurement.DecodeEngine = profile.Decode.ProtocolVersion is ToonProtocolVersion.V4_4 or ToonProtocolVersion.CustomV4_4 ? "v4 generic direct binding (no JSON/native retry)" : "legacy typed decoder; miss delta records native fallback";
#endif
    }
    catch (Exception error)
    {
        measurement.Error = error.ToString();
    }
}
foreach (var name in new[] { "prods.toon", "prodsOLD.toon" })
{
    var saved = new SavedFixture { Name = name };
    result.SavedFixtures.Add(saved);
    try
    {
        using var reader = new StreamReader(Path.Combine(inputDir, name), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
#if BASELINE
        var fallback = Decode();
#else
        var fallback = Decode(ToonProtocolVersion.CustomLegacy);
        var detected = ToonDecoder.DetectVersion(text);
        saved.AutomaticDetection = $"detected={detected.DetectedVersion}; selected={detected.SelectedVersion}; requiresHint={detected.RequiresVersionHint}";
#endif
        fallback.ExpandPaths = ToonPathExpansion.Safe;
        var options = ToonDecoder.DetectOptions(text, fallback);
        saved.Indent = options.Indent;
        saved.Layout = options.ObjectArrayLayout.ToString();
        saved.Protocol = "legacy custom columnar/hybrid syntax; explicit legacy hint used for content interpretation";
        var decoded = ToonDecoder.Decode<List<Product>>(text, options);
        saved.DecodedRows = decoded?.Count;
        saved.DifferenceFromSourceDto = Difference(expectedJson, JsonSerializer.Serialize(decoded, serializer));
        saved.Correct = decoded?.Count == products.Count && saved.DifferenceFromSourceDto == null;
    }
    catch (Exception error) { saved.Error = error.ToString(); }
}
var output = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
var outputPath = Arg("--output");
if (outputPath != null) File.WriteAllText(outputPath, output + "\n");
else Console.WriteLine(output);
return result.Profiles.Any(p => p.Error != null || p.ExactPreservationRequired && p.Correct != true || p.AutoError != null || p.AutoMatchesExplicit == false) ? 2 : 0;

string FindData()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "benchmarks", "DevOp.Toon.Benchmarks", "TestData");
            if (File.Exists(Path.Combine(candidate, "prods.json"))) return candidate;
        }
    throw new DirectoryNotFoundException("Supply --data with the checked-in product fixture directory.");
}

string? Arg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

#if BASELINE
ToonEncodeOptions Encode(ToonObjectArrayLayout layout, bool compact) => new()
#else
ToonEncodeOptions Encode(ToonObjectArrayLayout layout, bool compact, ToonProtocolVersion version) => new()
#endif
{
#if !BASELINE
    ProtocolVersion = version,
#endif
    Indent = 1,
    KeyFolding = ToonKeyFolding.Off,
    ObjectArrayLayout = layout,
    IgnoreNullOrEmpty = compact,
    ExcludeEmptyArrays = compact,
    Delimiter = ToonDelimiter.COMMA
};
#if BASELINE
ToonDecodeOptions Decode() => new()
#else
ToonDecodeOptions Decode(ToonProtocolVersion version) => new()
#endif
{
#if !BASELINE
    ProtocolVersion = version,
#endif
    Indent = 1,
    Strict = true,
    ExpandPaths = ToonPathExpansion.Off,
#if BASELINE
    ObjectArrayLayout = ToonObjectArrayLayout.Columnar
#else
    ObjectArrayLayout = version is ToonProtocolVersion.V4_0 or ToonProtocolVersion.V4_1 or ToonProtocolVersion.V4_2 or ToonProtocolVersion.V4_3 or ToonProtocolVersion.V4_4 ? ToonObjectArrayLayout.Auto : ToonObjectArrayLayout.Columnar
#endif
};

List<Sample> Measure(Func<object?> action)
{
    var samples = new List<Sample>();
    for (var i = 0; i < repetitions; i++)
    {
        var beforeHeap = GC.GetTotalMemory(false);
        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1); var gen2 = GC.CollectionCount(2);
        var start = Stopwatch.GetTimestamp();
        var value = action();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
        var actionGen0 = GC.CollectionCount(0) - gen0;
        var actionGen1 = GC.CollectionCount(1) - gen1;
        var actionGen2 = GC.CollectionCount(2) - gen2;
        var afterHeap = GC.GetTotalMemory(false);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        samples.Add(new Sample(elapsed, allocated, afterHeap - beforeHeap, actionGen0,
            actionGen1, actionGen2, process.WorkingSet64, process.PrivateMemorySize64, process.PeakWorkingSet64));
        GC.KeepAlive(value);
    }
    return samples;
}
long Retained(Func<object?> action)
{
    var before = GC.GetTotalMemory(true);
    var value = action();
    var after = GC.GetTotalMemory(true);
    GC.KeepAlive(value);
    return after - before;
}

long Misses()
{
    var field = typeof(ToonDecoder).Assembly.GetType("DevOp.Toon.Internal.Decode.TypedDecoder")?.GetField("DirectMaterializerMisses", BindingFlags.NonPublic | BindingFlags.Static);
    if (field?.GetValue(null) is not object dictionary) return 0;
    if (dictionary.GetType().GetProperty("Values")?.GetValue(dictionary) is not IEnumerable values) return 0;
    long sum = 0;
    foreach (var value in values) sum += Convert.ToInt64(value);
    return sum;
}
bool? DirectEncode(object value, ToonEncodeOptions options)
{
    var resolved = typeof(ToonEncodeOptions).GetMethod("GetResolvedOptions", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(options, null);
#if BASELINE
    var engine = "DevOp.Toon.Internal.Encode.ClrObjectArrayFastEncoder";
#else
    var engine = options.ProtocolVersion is ToonProtocolVersion.V4_4 or ToonProtocolVersion.CustomV4_4
        ? "DevOp.Toon.Internal.Encode.V4ClrEncoder" : "DevOp.Toon.Internal.Encode.ClrObjectArrayFastEncoder";
#endif
    var method = typeof(ToonEncoder).Assembly.GetType(engine)?.GetMethod("TryEncode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    return method == null || resolved == null ? null : (bool?)method.Invoke(null, new object?[] { value, resolved, null });
}
long Compress(byte[] bytes, bool brotli)
{
    using var target = new MemoryStream();
    using (Stream compression = brotli ? new BrotliStream(target, CompressionLevel.Optimal, true) : new GZipStream(target, CompressionLevel.Optimal, true)) compression.Write(bytes);
    return target.Length;
}
string? Difference(string expected, string actual)
{
    using var left = JsonDocument.Parse(expected);
    using var right = JsonDocument.Parse(actual);
    return Compare(left.RootElement, right.RootElement, "$", out var difference) ? null : difference;
}
bool Compare(JsonElement left, JsonElement right, string path, out string? difference)
{
    difference = null;
    if (left.ValueKind != right.ValueKind) { difference = path + ": " + left.ValueKind + " != " + right.ValueKind; return false; }
    if (left.ValueKind == JsonValueKind.Object)
    {
        var expectedProperties = left.EnumerateObject().ToArray();
        var actualProperties = right.EnumerateObject().ToArray();
        if (expectedProperties.Length != actualProperties.Length) { difference = path + ": property count"; return false; }
        foreach (var property in expectedProperties)
            if (!right.TryGetProperty(property.Name, out var value)) { difference = path + "." + property.Name + ": missing"; return false; }
            else if (!Compare(property.Value, value, path + "." + property.Name, out difference)) return false;
        return true;
    }
    if (left.ValueKind == JsonValueKind.Array)
    {
        if (left.GetArrayLength() != right.GetArrayLength()) { difference = path + ": array count"; return false; }
        for (var i = 0; i < left.GetArrayLength(); i++) if (!Compare(left[i], right[i], path + "[" + i + "]", out difference)) return false;
        return true;
    }
    var equal = left.ValueKind switch
    {
        JsonValueKind.Number => left.TryGetDecimal(out var ld) && right.TryGetDecimal(out var rd) ? ld == rd : left.GetDouble().Equals(right.GetDouble()),
        JsonValueKind.String => left.GetString() == right.GetString(),
        _ => left.GetRawText() == right.GetRawText()
    };
    if (!equal) difference = path + ": scalar values differ";
    return equal;
}

sealed class Report
{
    public int Warmups { get; set; }
    public int SamplesPerOperation { get; set; }
    public bool MeasureAutoRequested { get; set; }
    public string? Build { get; set; }
    public string? Runtime { get; set; }
    public string? LibraryDebuggingFlags { get; set; }
    public string? LibraryAssembly { get; set; }
    public string? LibrarySha256 { get; set; }
    public int Rows { get; set; }
    public long SourceBytes { get; set; }
    public string? SourceSha256 { get; set; }
    public string? Notes { get; set; }
    public List<Profile> Profiles { get; } = new();
    public List<SavedFixture> SavedFixtures { get; } = new();
}
sealed class Profile
{
    public int? AutoDecodedRows { get; set; }
    public string? AutoDecodedModelSha256 { get; set; }
    public bool? AutoMatchesExplicit { get; set; }
    public string? AutoRoundTripDifference { get; set; }
    public string? AutoError { get; set; }
    public List<Sample>? AutoDecode { get; set; }
    public string Name { get; set; } = "";
    public bool ExactPreservationRequired { get; set; }
    public bool? Correct { get; set; }
    public int? DecodedRows { get; set; }
    public string? RoundTripDifference { get; set; }
    public string? EncodedSha256 { get; set; }
    public string? DecodedModelSha256 { get; set; }
    public long Characters { get; set; }
    public long Utf8Bytes { get; set; }
    public long GzipBytes { get; set; }
    public long BrotliBytes { get; set; }
    public bool? DirectEncodeProbe { get; set; }
    public long LegacyDirectMaterializerMisses { get; set; }
    public string? DecodeEngine { get; set; }
    public string? Error { get; set; }
    public long? EncodeRetainedManagedBytes { get; set; }
    public long? DecodeRetainedManagedBytes { get; set; }
    public List<Sample>? Encode { get; set; }
    public List<Sample>? Decode { get; set; }
}
sealed class SavedFixture
{
    public string Name { get; set; } = "";
    public string? Protocol { get; set; }
    public string? AutomaticDetection { get; set; }
    public int? Indent { get; set; }
    public string? Layout { get; set; }
    public int? DecodedRows { get; set; }
    public bool? Correct { get; set; }
    public string? DifferenceFromSourceDto { get; set; }
    public string? Error { get; set; }
}
sealed record Sample(double Milliseconds, long AllocatedBytes, long ManagedHeapDeltaBytes, int Gen0, int Gen1, int Gen2, long WorkingSetBytes, long PrivateBytes, long ProcessPeakWorkingSetBytes);
