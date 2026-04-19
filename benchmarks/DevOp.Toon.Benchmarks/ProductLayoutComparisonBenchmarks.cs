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
public class ProductLayoutComparisonBenchmarks
{
    private static readonly JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly ToonEncodeOptions ToonNativeOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("toon-native");

    private static readonly ToonEncodeOptions VLinkToonOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("vlink-toon");

    private static readonly ToonDecodeOptions ToonDecodeOptions = new()
    {
        ExpandPaths = ToonPathExpansion.Safe,
        Indent = 1
    };

    private static List<Product>? allProducts;

    private List<Product> products = [];
    private string toonNativeText = string.Empty;
    private string DevOpToonText = string.Empty;

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
        toonNativeText = ToonEncoder.Encode(products, ToonNativeOptions);
        DevOpToonText = ToonEncoder.Encode(products, VLinkToonOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode")]
    public string EncodeToonNative()
    {
        return ToonEncoder.Encode(products, ToonNativeOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Encode")]
    public string EncodeVLinkToon()
    {
        return ToonEncoder.Encode(products, VLinkToonOptions);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode")]
    public List<Product>? DecodeToonNative()
    {
        return ToonDecoder.Decode<List<Product>>(toonNativeText, ToonDecodeOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Decode")]
    public List<Product>? DecodeVLinkToon()
    {
        return ToonDecoder.Decode<List<Product>>(DevOpToonText, ToonDecodeOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Reference JSON")]
    public string JsonSerializeProducts()
    {
        return JsonSerializer.Serialize(products, ProductSerializerOptions);
    }
}

internal static class ProductLayoutComparisonProfiler
{
    private static readonly ToonEncodeOptions ToonNativeOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("toon-native");

    private static readonly ToonEncodeOptions VLinkToonOptions =
        ToonBenchmarkProfiles.ResolveEncodeOptions("vlink-toon");

    private static readonly ToonDecodeOptions ToonDecodeOptions = new()
    {
        ExpandPaths = ToonPathExpansion.Safe,
        Indent = 1
    };

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-products-layout-compare", StringComparison.OrdinalIgnoreCase)))
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

        var toonNativeEncodeMeasurements = MeasureEncode(iterations, products, ToonNativeOptions);
        var DevOpToonEncodeMeasurements = MeasureEncode(iterations, products, VLinkToonOptions);

        var toonNativeText = ToonEncoder.Encode(products, ToonNativeOptions);
        var DevOpToonText = ToonEncoder.Encode(products, VLinkToonOptions);

        var toonNativeDecodeMeasurements = MeasureDecode(iterations, toonNativeText);
        var DevOpToonDecodeMeasurements = MeasureDecode(iterations, DevOpToonText);

        Console.WriteLine("Product layout comparison profile");
        Console.WriteLine($"Products             : {productCount}");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine($"Toon Native chars    : {toonNativeText.Length}");
        Console.WriteLine($"DevOp Toon chars     : {DevOpToonText.Length}");
        Console.WriteLine($"Char delta           : {toonNativeText.Length - DevOpToonText.Length}");
        Console.WriteLine($"DevOp Toon smaller   : {CalculateReduction(toonNativeText.Length, DevOpToonText.Length):F2}%");
        Console.WriteLine();
        Console.WriteLine("Encode timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("Toon Native encode", toonNativeEncodeMeasurements);
        WriteMeasurement("DevOp Toon encode", DevOpToonEncodeMeasurements);
        Console.WriteLine();
        Console.WriteLine("Decode timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("Toon Native decode", toonNativeDecodeMeasurements);
        WriteMeasurement("DevOp Toon decode", DevOpToonDecodeMeasurements);
    }

    private static List<MeasurementResult> MeasureEncode(
        int iterations,
        List<Product> products,
        ToonEncodeOptions options)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = ToonEncoder.Encode(products, options);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, result.Length));
        }

        return results;
    }

    private static List<MeasurementResult> MeasureDecode(int iterations, string toonText)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            _ = ToonDecoder.Decode<List<Product>>(toonText, ToonDecodeOptions);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, toonText.Length));
        }

        return results;
    }

    private static void WriteMeasurement(string name, List<MeasurementResult> results)
    {
        var averageMs = results.Average(static result => result.Elapsed.TotalMilliseconds);
        var minMs = results.Min(static result => result.Elapsed.TotalMilliseconds);
        var maxMs = results.Max(static result => result.Elapsed.TotalMilliseconds);
        var averageAllocatedKb = results.Average(static result => result.AllocatedBytes / 1024d);
        var averageChars = results.Average(static result => result.PayloadChars);
        Console.WriteLine(
            $"{name,-20} avg {averageMs,8:F3} ms   min {minMs,8:F3} ms   max {maxMs,8:F3} ms   alloc {averageAllocatedKb,10:F2} KB   chars {averageChars,10:F0}");
    }

    private static double CalculateReduction(int originalChars, int candidateChars)
    {
        if (originalChars == 0)
        {
            return 0;
        }

        return (originalChars - candidateChars) * 100d / originalChars;
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

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes, int PayloadChars);
}
