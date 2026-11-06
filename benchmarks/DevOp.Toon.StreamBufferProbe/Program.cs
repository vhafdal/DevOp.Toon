using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon;
using DevOp.Toon.Core;
using DevOp.Toon.Benchmarks.Models;

var mode = args.Length > 0 ? args[0] : "library-sync";
if (mode is not ("library-sync" or "library-async" or "convert-max" or "convert-exact")) throw new ArgumentException("Unknown mode.");
var input = args.Length > 1 ? args[1] : throw new ArgumentException("Supply MODE and prods.json path.");
var warmups = args.Length > 2 ? int.Parse(args[2]) : 30;
var repetitions = args.Length > 3 ? int.Parse(args[3]) : 50;
var protocolText = args.Length > 4 ? args[4] : "custom-4.4";
if (!ToonProtocolVersions.TryParse(protocolText, out var protocol)) throw new ArgumentException("Supply a concrete supported wire protocol version.");
var data = JsonSerializer.Deserialize<List<Product>>(File.ReadAllText(input, Encoding.Unicode), new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString })!;
if (data.Count != 2051) throw new InvalidOperationException("Expected all 2051 normalized Product DTOs.");
var options = new ToonEncodeOptions { ProtocolVersion = protocol, Indent = 1, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false };
var utf8 = new UTF8Encoding(false);
var characters = ToonEncoder.Encode(data, options).ToCharArray();
var expected = utf8.GetBytes(characters);
using (var check = new MemoryStream())
{
    if (mode == "library-async") await ToonEncoder.EncodeToStreamAsync(data, check, options);
    else if (mode == "library-sync") ToonEncoder.EncodeToStream(data, check, options);
    else ConvertTo(check);
    if (!check.ToArray().AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("UTF-8 output mismatch.");
}
using var sink = new BufferSink();
async Task Run()
{
    sink.Reset();
    if (mode == "library-async") await ToonEncoder.EncodeToStreamAsync(data, sink, options);
    else if (mode == "library-sync") ToonEncoder.EncodeToStream(data, sink, options);
    else ConvertTo(sink);
    if (sink.Bytes != expected.Length) throw new InvalidOperationException("Incomplete output.");
}
void ConvertTo(Stream destination)
{
    var required = mode == "convert-max" ? utf8.GetMaxByteCount(characters.Length) : utf8.GetByteCount(characters);
    var rented = ArrayPool<byte>.Shared.Rent(required);
    try { destination.Write(rented, 0, utf8.GetBytes(characters, 0, characters.Length, rented, 0)); }
    finally { ArrayPool<byte>.Shared.Return(rented); }
}
for (var i = 0; i < warmups; i++) await Run();
var samples = new List<Sample>(repetitions);
for (var i = 0; i < repetitions; i++)
{
    var thread = Environment.CurrentManagedThreadId;
    var before = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    await Run();
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Thread changed during allocation measurement.");
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    samples.Add(new Sample(elapsed, allocated, sink.BufferCapacity, sink.Writes));
}
using var process = Process.GetCurrentProcess();
process.Refresh();
Console.WriteLine(JsonSerializer.Serialize(new
{
    Mode = mode, ProtocolVersion = ToonProtocolVersions.Format(protocol), Rows = data.Count, Warmups = warmups, Repetitions = repetitions,
    SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
    LibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ToonEncoder).Assembly.Location))),
    EncodedSha256 = Convert.ToHexString(SHA256.HashData(expected)),
    Utf16Characters = characters.Length, Utf8Bytes = expected.Length,
    RequiredMaxBytes = utf8.GetMaxByteCount(characters.Length), RequiredExactBytes = utf8.GetByteCount(characters),
    ManagedHeapBytes = GC.GetTotalMemory(false), ProcessPeakWorkingSetBytes = process.PeakWorkingSet64,
    MemoryNotes = "BufferCapacityBytes is actual destination-visible byte-array rental capacity, not a retained-heap estimate. Shared pools may trim. Heap is uncollected; process peak includes setup. No forced GC between samples.",
    Scope = mode.StartsWith("convert-") ? "Isolated UTF-8 transcode and pool rent/return, same pre-encoded characters" : "Full typed TOON encoding and stream write to a nonbuffering sink",
    Samples = samples
}, new JsonSerializerOptions { WriteIndented = true }));

readonly record struct Sample(double Milliseconds, long AllocatedBytes, int BufferCapacityBytes, int Writes);

sealed class BufferSink : Stream
{
    internal long Bytes { get; private set; }
    internal int BufferCapacity { get; private set; }
    internal int Writes { get; private set; }
    internal void Reset() { Bytes = 0; BufferCapacity = 0; Writes = 0; }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Bytes;
    public override long Position { get => Bytes; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) { Bytes += count; BufferCapacity = Math.Max(BufferCapacity, buffer.Length); Writes++; }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); Write(buffer, offset, count); return Task.CompletedTask; }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Bytes += buffer.Length;
        BufferCapacity = Math.Max(BufferCapacity, MemoryMarshal.TryGetArray(buffer, out var segment) ? segment.Array!.Length : buffer.Length);
        Writes++;
        return ValueTask.CompletedTask;
    }
}
