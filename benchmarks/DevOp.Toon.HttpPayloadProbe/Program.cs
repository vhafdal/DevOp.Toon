using System.Diagnostics;
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevOp.Toon;
using DevOp.Toon.Core;
using DevOp.Toon.Client;
using DevOp.Toon.Benchmarks.Models;

var mode = args.Length == 0 ? "client" : args[0];
var payload = File.ReadAllBytes(args.Length > 1 ? args[1] : throw new ArgumentException("Supply MODE and an exported UTF-8 payload path."));
using var http = new HttpClient(new PayloadHandler(payload)) { BaseAddress = new Uri("https://benchmark.invalid/") };
var decode = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.CustomV4_4, Indent = 1 };
var client = new ToonClient(http, new ToonService(), mode == "clientmeta" ? null : new ToonClientOptions { DecodeOptions = decode });
var utf8 = new UTF8Encoding(false, true);
bool? bufferVisible = null;
async Task<List<Product>> Run()
{
    if (mode is "client" or "clientmeta") return (await client.GetAsync<List<Product>>("products"))!;
    using var response = await http.GetAsync("products", mode == "pool" ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
    string text;
    if (mode == "buffer")
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        if (stream is MemoryStream memory && memory.TryGetBuffer(out var segment))
        {
            bufferVisible = true;
            text = utf8.GetString(segment.Array!, segment.Offset, segment.Count);
        }
        else
        {
            bufferVisible = false;
            text = utf8.GetString(await response.Content.ReadAsByteArrayAsync());
        }
    }
    else if (mode == "pool")
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var limit = (int)Math.Min(http.MaxResponseContentBufferSize, int.MaxValue);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(8192, limit));
        var length = 0;
        try
        {
            while (true)
            {
                if (length == limit)
                {
                    if (await stream.ReadAsync(buffer, 0, 1, CancellationToken.None) != 0)
                        throw new HttpRequestException("Response exceeds MaxResponseContentBufferSize.");
                    break;
                }
                if (length == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent((int)Math.Min(limit, (long)buffer.Length * 2));
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }
                var received = await stream.ReadAsync(buffer, length, Math.Min(buffer.Length - length, limit - length), CancellationToken.None);
                if (received == 0) break;
                length += received;
            }
            text = utf8.GetString(buffer, 0, length);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    else if (mode == "reader")
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false);
        text = await reader.ReadToEndAsync();
    }
    else
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        text = utf8.GetString(bytes);
    }
    return ToonDecoder.Decode<List<Product>>(text, decode)!;
}
for (var i = 0; i < 20; i++) if ((await Run()).Count != 2051) throw new InvalidOperationException();
var modelSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(await Run())));
var samples = new List<object>();
for (var i = 0; i < 50; i++)
{
    var before = GC.GetTotalAllocatedBytes(precise: true);
    var start = Stopwatch.GetTimestamp();
    var value = await Run();
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
    if (value.Count != 2051 || value[0].ItemCode == null) throw new InvalidOperationException();
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    samples.Add(new { Milliseconds = elapsed, AllocatedBytes = allocated, Rows = value.Count, ManagedHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64, ProcessPeakWorkingSetBytes = process.PeakWorkingSet64 });
    GC.KeepAlive(value);
}
var librarySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ToonDecoder).Assembly.Location)));
Console.WriteLine(JsonSerializer.Serialize(new { Mode = mode, BufferVisible = bufferVisible, LibrarySha256 = librarySha256, Utf8Bytes = payload.Length, Warmups = 20, CorrectnessProbes = 1, DecodedModelSha256 = modelSha256, Repetitions = 50, Scope = mode == "pool" ? "ResponseHeadersRead HttpClient pooled prototype with in-memory handler; no network" : "Buffered HttpClient with in-memory handler; no network", MemoryNotes = "Heap bytes are uncollected, not retained. Process peak includes setup. Prototype does not establish timeout/cancellation compatibility.", Samples = samples }));

sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/toon");
        response.Headers.TryAddWithoutValidation("X-Toon-Version", "custom-4.4");
        return Task.FromResult(response);
    }
}
