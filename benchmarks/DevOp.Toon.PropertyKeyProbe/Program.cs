using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevOp.Toon;
using DevOp.Toon.Core;

var output = args.Length > 0 ? args[0] : null;
const int warmups = 30, samples = 30, operations = 1000;
var profiles = new List<object>();
var plain = new PlainRow();
var escaped = new EscapedRow();
var list = Enumerable.Range(0, 8).Select(i => new ListRow { Id = i }).ToArray();
foreach (var version in new[] { ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4 })
foreach (var (name, value) in new (string, object)[] { ("plain-root", plain), ("escaped-root", escaped), ("complex-eight-rows", list) })
{
    var options = new ToonEncodeOptions { ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false };
    var expected = ToonEncoder.Encode(value, options);
    var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    for (var i = 0; i < warmups; i++) Batch();
    var timings = new List<object>();
    for (var i = 0; i < samples; i++)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var last = Batch();
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (!string.Equals(last, expected, StringComparison.Ordinal)) throw new InvalidOperationException("Output changed during sampling.");
        timings.Add(new { NanosecondsPerOperation = watch.Elapsed.TotalNanoseconds / operations, AllocatedBytesPerOperation = (double)bytes / operations });
        GC.KeepAlive(last);
    }
    profiles.Add(new { Name = version + "/" + name, OutputSha256 = expectedHash, Characters = expected.Length, Samples = timings });
    string Batch()
    {
        string last = "";
        for (var i = 0; i < operations; i++) last = ToonEncoder.Encode(value, options);
        return last;
    }
}
var library = typeof(ToonEncoder).Assembly.Location;
var report = JsonSerializer.Serialize(new
{
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    Library = library, LibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))),
    Warmups = warmups, SamplesPerProfile = samples, OperationsPerSample = operations,
    Notes = "Setup, correctness/hash checks and warmups excluded. No forced GC in timed samples; allocation includes Stopwatch per batch (identical for both builds). Run frozen and candidate in separate processes with identical harness binary/source.", Profiles = profiles
}, new JsonSerializerOptions { WriteIndented = true });
if (output != null) File.WriteAllText(output, report);
else Console.WriteLine(report);

public sealed class PlainRow
{
    public int RecordId { get; set; } = 42;
    public string Name { get; set; } = "Widget";
    public decimal Price { get; set; } = 12.5m;
    public bool Enabled { get; set; } = true;
    public int Quantity { get; set; } = 7;
    public string Category { get; set; } = "Goods";
}
public sealed class EscapedRow
{
    [ToonPropertyName("record:id")] public int RecordId { get; set; } = 42;
    [ToonPropertyName("display name")] public string Name { get; set; } = "Widget";
    [ToonPropertyName("price,amount")] public decimal Price { get; set; } = 12.5m;
    [ToonPropertyName("enabled?")] public bool Enabled { get; set; } = true;
    [ToonPropertyName("stock/count")] public int Quantity { get; set; } = 7;
    [ToonPropertyName("category😀")] public string Category { get; set; } = "Goods";
}
public sealed class ListRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "Widget";
    public decimal Price { get; set; } = 12.5m;
    public bool Enabled { get; set; } = true;
    [ToonPropertyName("stock/count")] public int Quantity { get; set; } = 7;
    [ToonPropertyName("tag,list")] public string[] Tags { get; set; } = ["blue", "small"];
}
