using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

internal static class ProductDecodeProfiler
{
    private static readonly JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-products", StringComparison.OrdinalIgnoreCase)))
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
        var products = LoadProducts(productCount);
        var toonProducts = LoadProductsFromToon(productCount);
        var jsonText = JsonSerializer.Serialize(products, ProductSerializerOptions);
        var toonText = ToonEncoder.Encode(toonProducts);
        var decodeOptions = CreateProfilingDecodeOptions(toonText);
        var harness = DecodeReflectionHarness<List<Product>>.Create(decodeOptions);

        // Warm up JIT and reflection plumbing before measuring.
        JsonSerializer.Deserialize<List<Product>>(jsonText, ProductSerializerOptions);
        ToonDecoder.Decode<List<Product>>(toonText, decodeOptions);
        var warmScan = harness.Scan(toonText);
        harness.DirectDecode(warmScan);
        harness.TypedDecode(toonText);

        var jsonMeasurements = Measure(iterations, () => JsonSerializer.Deserialize<List<Product>>(jsonText, ProductSerializerOptions));
        var publicMeasurements = Measure(iterations, () => ToonDecoder.Decode<List<Product>>(toonText, decodeOptions));
        var scanMeasurements = Measure(iterations, () => harness.Scan(toonText));

        bool allDirectSucceeded = true;
        var directMeasurements = Measure(iterations, () =>
        {
            var scanResult = harness.Scan(toonText);
            allDirectSucceeded &= harness.DirectDecode(scanResult);
        });

        var typedMeasurements = Measure(iterations, () => harness.TypedDecode(toonText));

        Console.WriteLine("Product decode profile");
        Console.WriteLine($"Input file : {BenchmarkDataPaths.ProductsJsonPath}");
        Console.WriteLine($"Products   : {productCount}");
        Console.WriteLine($"Iterations : {iterations}");
        Console.WriteLine($"TOON chars : {toonText.Length}");
        Console.WriteLine($"JSON chars : {jsonText.Length}");
        Console.WriteLine($"Decode indent used for profile : {decodeOptions.Indent}");
        Console.WriteLine();
        Console.WriteLine("Stage timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("JSON deserialize", jsonMeasurements);
        WriteMeasurement("TOON public decode", publicMeasurements);
        WriteMeasurement("TOON scan only", scanMeasurements);
        WriteMeasurement("TOON direct decode", directMeasurements);
        WriteMeasurement("TOON typed internal", typedMeasurements);
        Console.WriteLine();
        Console.WriteLine($"Direct fast path succeeded on all iterations: {allDirectSucceeded}");
    }

    private static List<Product> LoadProducts(int productCount)
    {
        if (!File.Exists(BenchmarkDataPaths.ProductsJsonPath))
        {
            throw new FileNotFoundException(
                $"The product benchmark input file was not found at '{BenchmarkDataPaths.ProductsJsonPath}'.",
                BenchmarkDataPaths.ProductsJsonPath);
        }

        var sourceJson = File.ReadAllText(BenchmarkDataPaths.ProductsJsonPath);
        var allProducts = JsonSerializer.Deserialize<List<Product>>(sourceJson, ProductSerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize products from the JSON benchmark input.");

        if (productCount > allProducts.Count)
        {
            throw new InvalidOperationException(
                $"Requested {productCount} products, but '{BenchmarkDataPaths.ProductsJsonPath}' only contains {allProducts.Count}.");
        }

        return allProducts.Take(productCount).ToList();
    }

    private static List<Product> LoadProductsFromToon(int productCount)
    {
        if (!File.Exists(BenchmarkDataPaths.ProductsToonPath))
        {
            throw new FileNotFoundException(
                $"The product benchmark input file was not found at '{BenchmarkDataPaths.ProductsToonPath}'.",
                BenchmarkDataPaths.ProductsToonPath);
        }

        var sourceToon = File.ReadAllText(BenchmarkDataPaths.ProductsToonPath);
        var allProducts = ToonDecoder.Decode<List<Product>>(sourceToon)
            ?? throw new InvalidOperationException("Failed to deserialize products from the TOON benchmark input.");

        if (productCount > allProducts.Count)
        {
            throw new InvalidOperationException(
                $"Requested {productCount} products, but '{BenchmarkDataPaths.ProductsToonPath}' only contains {allProducts.Count}.");
        }

        return allProducts.Take(productCount).ToList();
    }

    private static List<MeasurementResult> Measure(int iterations, Action action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            action();
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore));
        }

        return results;
    }

    private static void WriteMeasurement(string name, List<MeasurementResult> results)
    {
        var averageMs = results.Average(static result => result.Elapsed.TotalMilliseconds);
        var minMs = results.Min(static result => result.Elapsed.TotalMilliseconds);
        var maxMs = results.Max(static result => result.Elapsed.TotalMilliseconds);
        var averageAllocatedKb = results.Average(static result => result.AllocatedBytes / 1024d);
        Console.WriteLine(
            $"{name,-20} avg {averageMs,8:F3} ms   min {minMs,8:F3} ms   max {maxMs,8:F3} ms   alloc {averageAllocatedKb,10:F2} KB");
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

    private static ToonDecodeOptions CreateProfilingDecodeOptions(string toonText)
    {
        var defaults = ToonDecoder.DefaultOptions;
        return ToonDecoder.DetectOptions(toonText, new ToonDecodeOptions
        {
            ExpandPaths = defaults.ExpandPaths,
            Strict = defaults.Strict,
            Indent = defaults.Indent > 0 ? defaults.Indent : 2
        });
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes);
}
