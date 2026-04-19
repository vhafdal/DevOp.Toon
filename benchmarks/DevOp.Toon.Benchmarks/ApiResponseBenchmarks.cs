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

    private ApiResponseBenchmarkHarness(string datasetName, int pageSize, IHost host)
    {
        DatasetName = datasetName;
        PageSize = pageSize;
        this.host = host;
        var testServer = host.GetTestServer();
        jsonClient = testServer.CreateClient();
        jsonClient.DefaultRequestHeaders.Accept.Clear();
        jsonClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        toonClient = testServer.CreateClient();
        toonClient.DefaultRequestHeaders.Accept.Clear();
        toonClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/toon"));
    }

    public static async Task<ApiResponseBenchmarkHarness> CreateAsync(
        string datasetName,
        int pageSize,
        ToonEncodeOptions encodeOptions)
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

        var harness = new ApiResponseBenchmarkHarness(datasetName, pageSize, host);
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
