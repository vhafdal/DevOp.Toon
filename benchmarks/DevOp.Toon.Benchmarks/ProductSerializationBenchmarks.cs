using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

[Config(typeof(InProcessShortRunConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProductSerializationBenchmarks
{
    private static readonly JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly ToonEncodeOptions DefaultToonSerializerOptions = new()
    {
        Indent = 2,
        KeyFolding = ToonKeyFolding.Safe,
        ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
        Delimiter = ToonDelimiter.COMMA
    };

    private static readonly ToonEncodeOptions CompactToonSerializerOptions = new()
    {
        Indent = 1,
        KeyFolding = ToonKeyFolding.Safe,
        ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
        Delimiter = ToonDelimiter.COMMA
    };

    private static List<Product>? allProducts;
    private List<Product> products = [];
    private MemoryStream jsonStream = null!;
    private MemoryStream toonStream = null!;

    [Params(100, 1000)]
    public int ProductCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        allProducts ??= ProductBenchmarkData.LoadProducts();

        if (ProductCount > allProducts.Count)
        {
            throw new InvalidOperationException(
                $"Requested {ProductCount} products, but '{BenchmarkDataPaths.ProductsJsonPath}' only contains {allProducts.Count}.");
        }

        products = allProducts.Take(ProductCount).ToList();
        jsonStream = new MemoryStream();
        toonStream = new MemoryStream();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        jsonStream.Dispose();
        toonStream.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Typed Serialize")]
    public string JsonSerializeProducts()
    {
        return JsonSerializer.Serialize(products, ProductSerializerOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Typed Serialize")]
    public string ToonEncodeProducts()
    {
        return ToonEncoder.Encode(products, DefaultToonSerializerOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Typed Serialize")]
    public string ToonEncodeProductsCompact()
    {
        return ToonEncoder.Encode(products, CompactToonSerializerOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Typed Stream Serialize")]
    public int JsonSerializeProductsToStream()
    {
        jsonStream.SetLength(0);
        jsonStream.Position = 0;
        JsonSerializer.Serialize(jsonStream, products, ProductSerializerOptions);
        return (int)jsonStream.Length;
    }

    [Benchmark]
    [BenchmarkCategory("Typed Stream Serialize")]
    public int ToonEncodeProductsCompactToStream()
    {
        toonStream.SetLength(0);
        toonStream.Position = 0;
        ToonEncoder.EncodeToStream(products, toonStream, CompactToonSerializerOptions);
        return (int)toonStream.Length;
    }
}

internal static class ProductEncodeProfiler
{
    private static readonly JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-products-encode", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var productCount = TryReadInt(args, "--count") ?? 1_000;
        var iterations = TryReadInt(args, "--iterations") ?? 8;
        Run(productCount, iterations);
        return true;
    }

    private static void Run(int productCount, int iterations)
    {
        var allProducts = ProductBenchmarkData.LoadProducts();
        if (productCount > allProducts.Count)
        {
            throw new InvalidOperationException(
                $"Requested {productCount} products, but '{BenchmarkDataPaths.ProductsJsonPath}' only contains {allProducts.Count}.");
        }

        var products = allProducts.Take(productCount).ToList();
        var jsonMeasurements = Measure(iterations, () => JsonSerializer.Serialize(products, ProductSerializerOptions));
        var toonMeasurements = Measure(iterations, () => ToonEncoder.Encode(products, ToonBenchmarkProfiles.ResolveEncodeOptions("readable")));
        var compactToonMeasurements = Measure(iterations, () => ToonEncoder.Encode(products, ToonBenchmarkProfiles.ResolveEncodeOptions("compact")));
        var jsonStreamMeasurements = MeasureStream(iterations, products, static (stream, payload) => JsonSerializer.Serialize(stream, payload, ProductSerializerOptions));
        var compactToonStreamMeasurements = MeasureStream(iterations, products, static (stream, payload) => ToonEncoder.EncodeToStream(payload, stream, ToonBenchmarkProfiles.ResolveEncodeOptions("compact")));

        var jsonPayload = JsonSerializer.Serialize(products, ProductSerializerOptions);
        var toonPayload = ToonEncoder.Encode(products, ToonBenchmarkProfiles.ResolveEncodeOptions("readable"));
        var compactToonPayload = ToonEncoder.Encode(products, ToonBenchmarkProfiles.ResolveEncodeOptions("compact"));

        Console.WriteLine("Product encode profile");
        Console.WriteLine($"Products             : {productCount}");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine($"JSON bytes           : {System.Text.Encoding.UTF8.GetByteCount(jsonPayload)}");
        Console.WriteLine($"TOON bytes           : {System.Text.Encoding.UTF8.GetByteCount(toonPayload)}");
        Console.WriteLine($"TOON compact bytes   : {System.Text.Encoding.UTF8.GetByteCount(compactToonPayload)}");
        Console.WriteLine();
        Console.WriteLine("Encode timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("JSON serialize", jsonMeasurements);
        WriteMeasurement("TOON encode readable", toonMeasurements);
        WriteMeasurement("TOON encode compact", compactToonMeasurements);
        WriteMeasurement("JSON stream serialize", jsonStreamMeasurements);
        WriteMeasurement("TOON compact stream", compactToonStreamMeasurements);
    }

    private static List<MeasurementResult> Measure(int iterations, Func<string> action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = action();
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, System.Text.Encoding.UTF8.GetByteCount(result)));
        }

        return results;
    }

    private static List<MeasurementResult> MeasureStream<T>(
        int iterations,
        T payload,
        Action<MemoryStream, T> action)
    {
        var results = new List<MeasurementResult>(iterations);
        using var stream = new MemoryStream();
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            stream.SetLength(0);
            stream.Position = 0;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            action(stream, payload);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, (int)stream.Length));
        }

        return results;
    }

    private static void WriteMeasurement(string name, List<MeasurementResult> results)
    {
        var averageMs = results.Average(static result => result.Elapsed.TotalMilliseconds);
        var minMs = results.Min(static result => result.Elapsed.TotalMilliseconds);
        var maxMs = results.Max(static result => result.Elapsed.TotalMilliseconds);
        var averageAllocatedKb = results.Average(static result => result.AllocatedBytes / 1024d);
        var averageBytes = results.Average(static result => result.OutputBytes);
        Console.WriteLine(
            $"{name,-20} avg {averageMs,8:F3} ms   min {minMs,8:F3} ms   max {maxMs,8:F3} ms   alloc {averageAllocatedKb,10:F2} KB   bytes {averageBytes,10:F0}");
    }

    private static int? TryReadInt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return null;
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes, int OutputBytes);
}

internal static class ProductBenchmarkData
{
    private static readonly JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static List<Product> LoadProducts()
    {
        if (!File.Exists(BenchmarkDataPaths.ProductsJsonPath))
        {
            throw new FileNotFoundException(
                $"The product benchmark input file was not found at '{BenchmarkDataPaths.ProductsJsonPath}'.",
                BenchmarkDataPaths.ProductsJsonPath);
        }

        var sourceJson = File.ReadAllText(BenchmarkDataPaths.ProductsJsonPath);
        return JsonSerializer.Deserialize<List<Product>>(sourceJson, ProductSerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize products from the JSON benchmark input.");
    }
}
