using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using DevOp.Toon.API;
using DevOp.Toon.Benchmarks.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevOp.Toon.Benchmarks;

// ---------------------------------------------------------------------------
// Network simulation — profile + DelegatingHandler
// ---------------------------------------------------------------------------

/// <summary>
/// Describes a simulated network link in terms of round-trip latency and bandwidth.
/// One-way latency = RttMs / 2. Transfer time = payload / BandwidthBytesPerSec.
/// BandwidthBytesPerSec == 0 means unlimited (loopback equivalent).
/// </summary>
internal sealed record NetworkProfile(
    string Name,
    int RttMs,
    long BandwidthBytesPerSec)
{
    public static readonly NetworkProfile Loopback =
        new("Loopback (in-process)", 0, 0);

    public static readonly NetworkProfile SameRegionCloud =
        new("Same-region cloud (5 ms RTT, 1 Gbps)", 5, 125_000_000L);

    public static readonly NetworkProfile AzureCrossRegion =
        new("Azure cross-region (30 ms RTT, 100 Mbps)", 30, 12_500_000L);

    public static readonly NetworkProfile Intercontinental =
        new("Intercontinental (150 ms RTT, 100 Mbps)", 150, 12_500_000L);

    public static readonly NetworkProfile Mobile4G =
        new("Mobile 4G (50 ms RTT, 20 Mbps)", 50, 2_500_000L);

    public static readonly NetworkProfile[] All =
    [
        Loopback,
        SameRegionCloud,
        AzureCrossRegion,
        Intercontinental,
        Mobile4G,
    ];

    /// <summary>
    /// Total simulated round-trip overhead (ms) for a response of <paramref name="responseBytes"/> bytes:
    /// RTT/2 (request travel) + RTT/2 (response travel) + transfer time.
    /// </summary>
    public int TotalSimulatedDelayMs(long responseBytes)
    {
        var transferMs = BandwidthBytesPerSec > 0
            ? (int)(responseBytes * 1000L / BandwidthBytesPerSec)
            : 0;
        return RttMs + transferMs;
    }
}

/// <summary>
/// Injects simulated network latency and bandwidth throttling into the HTTP pipeline.
/// Delays the request by RttMs/2 (outbound hop), then delays the response by
/// RttMs/2 + (responseBytes / bandwidth) before returning it to the caller.
/// </summary>
internal sealed class NetworkSimulationHandler : DelegatingHandler
{
    private readonly NetworkProfile profile;

    public NetworkSimulationHandler(NetworkProfile profile, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        this.profile = profile;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Simulate request travel (one-way latency)
        if (profile.RttMs > 0)
            await Task.Delay(profile.RttMs / 2, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);

        // Buffer content so we know its byte count before computing transfer delay
        var contentType = response.Content.Headers.ContentType;
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // Simulate response travel (one-way latency) + bandwidth-limited transfer time
        var transferMs = profile.BandwidthBytesPerSec > 0
            ? (int)(content.Length * 1000L / profile.BandwidthBytesPerSec)
            : 0;
        var responseDelayMs = profile.RttMs / 2 + transferMs;
        if (responseDelayMs > 0)
            await Task.Delay(responseDelayMs, cancellationToken);

        // Replace content so the caller can still read it
        response.Content = new ByteArrayContent(content);
        if (contentType is not null)
            response.Content.Headers.ContentType = contentType;

        return response;
    }
}

// ---------------------------------------------------------------------------
// In-process test host
// ---------------------------------------------------------------------------

internal sealed class ApiResponseBenchmarkHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHost host;
    private readonly HttpClient jsonClient;
    private readonly HttpClient toonClient;

    public string DatasetName { get; }
    public int PageSize { get; }
    public int JsonPayloadBytes { get; private set; }
    public int ToonPayloadBytes { get; private set; }
    public int JsonGzipBytes { get; private set; }
    public int ToonGzipBytes { get; private set; }
    public int JsonBrotliBytes { get; private set; }
    public int ToonBrotliBytes { get; private set; }

    private ApiResponseBenchmarkHarness(string datasetName, int pageSize, IHost host, NetworkProfile network)
    {
        DatasetName = datasetName;
        PageSize = pageSize;
        this.host = host;
        var testServer = host.GetTestServer();

        jsonClient = BuildClient(testServer, network, "application/json");
        toonClient = BuildClient(testServer, network, "application/toon");
    }

    private static HttpClient BuildClient(TestServer testServer, NetworkProfile network, string accept)
    {
        HttpClient client;
        if (network.RttMs == 0 && network.BandwidthBytesPerSec == 0)
        {
            client = testServer.CreateClient();
        }
        else
        {
            var innerHandler = testServer.CreateHandler();
            var simHandler = new NetworkSimulationHandler(network, innerHandler);
            client = new HttpClient(simHandler) { BaseAddress = testServer.BaseAddress };
        }
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return client;
    }

    public static async Task<ApiResponseBenchmarkHarness> CreateAsync(
        string datasetName,
        int pageSize,
        ToonEncodeOptions encodeOptions,
        NetworkProfile? network = null)
    {
        var products = LoadProducts();

        var builder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<IReadOnlyList<Product>>(products);
                services.AddControllers().AddToon(options =>
                {
                    options.Encode.IgnoreNullOrEmpty = encodeOptions.IgnoreNullOrEmpty;
                    options.Encode.ExcludeEmptyArrays = encodeOptions.ExcludeEmptyArrays;
                    options.KeyFolding = encodeOptions.KeyFolding;
                    options.Delimiter = encodeOptions.Delimiter;
                    options.Indent = encodeOptions.Indent;
                    options.ObjectArrayLayout = encodeOptions.ObjectArrayLayout;
                }, useAsDefaultFormatter: false);
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        });

        var host = builder.Build();
        await host.StartAsync();

        var harness = new ApiResponseBenchmarkHarness(datasetName, pageSize, host, network ?? NetworkProfile.Loopback);
        await harness.MeasurePayloadSizesAsync();
        return harness;
    }

    public async Task<int> GetJsonAsync()
    {
        var response = await jsonClient.GetAsync($"/api/benchmark/products/1/{PageSize}");
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length;
    }

    public async Task<int> GetToonAsync()
    {
        var response = await toonClient.GetAsync($"/api/benchmark/products/1/{PageSize}");
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length;
    }

    private async Task MeasurePayloadSizesAsync()
    {
        var jsonBytes = await (await jsonClient.GetAsync($"/api/benchmark/products/1/{PageSize}")).Content.ReadAsByteArrayAsync();
        var toonBytes = await (await toonClient.GetAsync($"/api/benchmark/products/1/{PageSize}")).Content.ReadAsByteArrayAsync();

        JsonPayloadBytes = jsonBytes.Length;
        ToonPayloadBytes = toonBytes.Length;

        var jsonText = Encoding.UTF8.GetString(jsonBytes);
        var toonText = Encoding.UTF8.GetString(toonBytes);

        JsonGzipBytes = CompressGzip(jsonText);
        ToonGzipBytes = CompressGzip(toonText);
        JsonBrotliBytes = CompressBrotli(jsonText);
        ToonBrotliBytes = CompressBrotli(toonText);
    }

    private static int CompressGzip(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new System.IO.MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(bytes, 0, bytes.Length);
        return (int)output.Length;
    }

    private static int CompressBrotli(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new System.IO.MemoryStream();
        using (var br = new System.IO.Compression.BrotliStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            br.Write(bytes, 0, bytes.Length);
        return (int)output.Length;
    }

    private static IReadOnlyList<Product> LoadProducts()
    {
        var path = BenchmarkDataPaths.ProductsJsonPath;
        var json = System.IO.File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Product>>(json, LoadOptions)
               ?? throw new InvalidOperationException("Failed to load products.");
    }

    public async ValueTask DisposeAsync()
    {
        jsonClient.Dispose();
        toonClient.Dispose();
        await host.StopAsync();
        host.Dispose();
    }
}

// ---------------------------------------------------------------------------
// Minimal controller registered in the test host
// ---------------------------------------------------------------------------

[ApiController]
[Route("api/benchmark/products")]
public sealed class BenchmarkProductsController(IReadOnlyList<Product> products) : ControllerBase
{
    [HttpGet("{page:int}/{pageSize:int}")]
    public ActionResult<IReadOnlyList<Product>> Get(int page, int pageSize)
    {
        var skip = (page - 1) * pageSize;
        if (skip >= products.Count)
            return NotFound();
        return Ok((IReadOnlyList<Product>)products.Skip(skip).Take(pageSize).ToList());
    }
}

// ---------------------------------------------------------------------------
// BenchmarkDotNet benchmark class
// ---------------------------------------------------------------------------

[Config(typeof(InProcessShortRunConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ApiResponseBenchmarks
{
    private static readonly ToonEncodeOptions OptimalOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("optimal");

    private ApiResponseBenchmarkHarness harness100 = null!;
    private ApiResponseBenchmarkHarness harness1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        harness100 = ApiResponseBenchmarkHarness.CreateAsync("products-100", 100, OptimalOptions)
            .GetAwaiter().GetResult();
        harness1000 = ApiResponseBenchmarkHarness.CreateAsync("products-1000", 1000, OptimalOptions)
            .GetAwaiter().GetResult();

        // Warmup
        for (int i = 0; i < 3; i++)
        {
            harness100.GetJsonAsync().GetAwaiter().GetResult();
            harness100.GetToonAsync().GetAwaiter().GetResult();
            harness1000.GetJsonAsync().GetAwaiter().GetResult();
            harness1000.GetToonAsync().GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        harness100.DisposeAsync().AsTask().GetAwaiter().GetResult();
        harness1000.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("100 products")]
    public Task<int> JsonResponse100() => harness100.GetJsonAsync();

    [Benchmark]
    [BenchmarkCategory("100 products")]
    public Task<int> ToonResponse100() => harness100.GetToonAsync();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("1000 products")]
    public Task<int> JsonResponse1000() => harness1000.GetJsonAsync();

    [Benchmark]
    [BenchmarkCategory("1000 products")]
    public Task<int> ToonResponse1000() => harness1000.GetToonAsync();
}

// ---------------------------------------------------------------------------
// Profiler (called from Program.cs via --profile-api-response)
// ---------------------------------------------------------------------------

internal static class ApiResponseProfiler
{
    private static readonly ToonEncodeOptions OptimalOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("optimal");

    private const int WarmupIterations = 5;
    private const int MeasureIterations = 12;

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static a => string.Equals(a, "--profile-api-response", StringComparison.OrdinalIgnoreCase)))
            return false;

        Run();
        return true;
    }

    private static void Run()
    {
        var pageSizes = new[] { 100, 500, 1000 };

        Console.WriteLine("API response profiler");
        Console.WriteLine($"Warmup      : {WarmupIterations}");
        Console.WriteLine($"Iterations  : {MeasureIterations}");
        Console.WriteLine();

        foreach (var pageSize in pageSizes)
        {
            var harness = ApiResponseBenchmarkHarness
                .CreateAsync($"products-{pageSize}", pageSize, OptimalOptions)
                .GetAwaiter().GetResult();

            try
            {
                for (int i = 0; i < WarmupIterations; i++)
                {
                    harness.GetJsonAsync().GetAwaiter().GetResult();
                    harness.GetToonAsync().GetAwaiter().GetResult();
                }

                var jsonMs = MeasureMs(MeasureIterations, () => harness.GetJsonAsync().GetAwaiter().GetResult());
                var toonMs = MeasureMs(MeasureIterations, () => harness.GetToonAsync().GetAwaiter().GetResult());

                var rawSave = ComputeReduction(harness.JsonPayloadBytes, harness.ToonPayloadBytes);
                var gzipSave = ComputeReduction(harness.JsonGzipBytes, harness.ToonGzipBytes);
                var brotliSave = ComputeReduction(harness.JsonBrotliBytes, harness.ToonBrotliBytes);

                Console.WriteLine($"Page size   : {pageSize}");
                Console.WriteLine($"JSON bytes  : {harness.JsonPayloadBytes:N0}   gzip {harness.JsonGzipBytes:N0}   brotli {harness.JsonBrotliBytes:N0}");
                Console.WriteLine($"TOON bytes  : {harness.ToonPayloadBytes:N0}   gzip {harness.ToonGzipBytes:N0}   brotli {harness.ToonBrotliBytes:N0}");
                Console.WriteLine($"Size saved  : raw {rawSave:F1}%   gzip {gzipSave:F1}%   brotli {brotliSave:F1}%");
                Console.WriteLine($"JSON avg    : {jsonMs:F3} ms");
                Console.WriteLine($"TOON avg    : {toonMs:F3} ms");
                Console.WriteLine($"Speedup     : {jsonMs / toonMs:F2}×");
                Console.WriteLine();
            }
            finally
            {
                harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static double MeasureMs(int iterations, Action action)
    {
        var results = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            action();
            results[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        return results.Average();
    }

    private static double ComputeReduction(int baseline, int candidate)
        => baseline == 0 ? 0 : ((baseline - candidate) / (double)baseline) * 100;
}

// ---------------------------------------------------------------------------
// Network latency profiler (called from Program.cs via --profile-api-network)
// ---------------------------------------------------------------------------

internal static class NetworkLatencyProfiler
{
    private static readonly ToonEncodeOptions OptimalOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("optimal");

    private static readonly int[] PageSizes = [1, 10, 100, 1000];
    private const int WarmupIterations = 3;
    private const int MeasureIterations = 8;

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static a => string.Equals(a, "--profile-api-network", StringComparison.OrdinalIgnoreCase)))
            return false;

        Run();
        return true;
    }

    private static void Run()
    {
        Console.WriteLine("Network Latency Simulation Profiler");
        Console.WriteLine($"Warmup {WarmupIterations}  |  Iterations {MeasureIterations}  |  Page sizes: {string.Join(", ", PageSizes)}");
        Console.WriteLine();

        foreach (var pageSize in PageSizes)
        {
            RunPageSize(pageSize);
        }

        Console.WriteLine("Note: Times include in-process server overhead + injected network simulation.");
        Console.WriteLine("      'Theory' = RTT + (payload / bandwidth) with no server cost.");
        Console.WriteLine("      TOON advantage grows because its payload is smaller, reducing transfer time.");
    }

    private static void RunPageSize(int pageSize)
    {
        // Measure payload sizes once with Loopback (no delay)
        var sizeHarness = ApiResponseBenchmarkHarness
            .CreateAsync($"size-{pageSize}", pageSize, OptimalOptions, NetworkProfile.Loopback)
            .GetAwaiter().GetResult();
        var jsonBytes = sizeHarness.JsonPayloadBytes;
        var toonBytes = sizeHarness.ToonPayloadBytes;
        sizeHarness.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var rawSavePct = (1.0 - (double)toonBytes / jsonBytes) * 100;

        Console.WriteLine($"┌─ {pageSize} product{(pageSize == 1 ? "" : "s")} ─────────────────────────────────────────────────────────────────");
        Console.WriteLine($"│  JSON {jsonBytes:N0} bytes  │  TOON {toonBytes:N0} bytes  │  {rawSavePct:F1}% smaller");
        Console.WriteLine($"│");
        Console.WriteLine($"│  {"Profile",-45} {"JSON (ms)",9} {"TOON (ms)",9} {"Saved",9} {"Theory JSON",12} {"Theory TOON",12}");
        Console.WriteLine($"│  {new string('-', 100)}");

        foreach (var profile in NetworkProfile.All)
        {
            var (jsonMs, toonMs) = MeasureProfile(pageSize, profile);
            var savedMs = jsonMs - toonMs;
            var theoryJson = profile.TotalSimulatedDelayMs(jsonBytes);
            var theoryToon = profile.TotalSimulatedDelayMs(toonBytes);

            var savedStr = savedMs >= 0 ? $"-{savedMs:F1} ms" : $"+{-savedMs:F1} ms";
            Console.WriteLine(
                $"│  {profile.Name,-45} {jsonMs,8:F1}  {toonMs,8:F1}  {savedStr,9}  {theoryJson,8} ms   {theoryToon,8} ms");
        }

        Console.WriteLine($"└");
        Console.WriteLine();
    }

    private static (double jsonMs, double toonMs) MeasureProfile(int pageSize, NetworkProfile profile)
    {
        var harness = ApiResponseBenchmarkHarness
            .CreateAsync($"products-{pageSize}", pageSize, OptimalOptions, profile)
            .GetAwaiter().GetResult();

        try
        {
            for (int i = 0; i < WarmupIterations; i++)
            {
                harness.GetJsonAsync().GetAwaiter().GetResult();
                harness.GetToonAsync().GetAwaiter().GetResult();
            }

            var jsonResults = new double[MeasureIterations];
            var toonResults = new double[MeasureIterations];

            for (int i = 0; i < MeasureIterations; i++)
            {
                var t0 = Stopwatch.GetTimestamp();
                harness.GetJsonAsync().GetAwaiter().GetResult();
                jsonResults[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

                var t1 = Stopwatch.GetTimestamp();
                harness.GetToonAsync().GetAwaiter().GetResult();
                toonResults[i] = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
            }

            return (jsonResults.Average(), toonResults.Average());
        }
        finally
        {
            harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
