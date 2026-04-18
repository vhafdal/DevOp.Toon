using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

internal static class NestedShapeProfiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly ToonService ToonService = new();

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-products-nested-shapes", StringComparison.OrdinalIgnoreCase)))
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
        var vendors = products.SelectMany(static p => p.Vendors ?? []).ToList();
        var warehouses = products.SelectMany(static p => p.Warehouses ?? []).ToList();
        var variations = warehouses.SelectMany(static w => w.Variations ?? []).ToList();
        var categories = products.SelectMany(static p => p.Categories ?? []).ToList();
        var subCategories = categories.SelectMany(static c => c.SubCategories ?? []).ToList();
        var changes = products.SelectMany(static p => p.Changes ?? []).ToList();
        var changeFields = changes.SelectMany(static c => c.Fields ?? []).ToList();

        Console.WriteLine("Product nested shape profile");
        Console.WriteLine($"Products             : {productCount}");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine();
        Console.WriteLine("Shape sizes");
        Console.WriteLine("-------------------------------");
        Console.WriteLine($"Vendors              : {vendors.Count}");
        Console.WriteLine($"Warehouses           : {warehouses.Count}");
        Console.WriteLine($"Variations           : {variations.Count}");
        Console.WriteLine($"Categories           : {categories.Count}");
        Console.WriteLine($"SubCategories        : {subCategories.Count}");
        Console.WriteLine($"Changes              : {changes.Count}");
        Console.WriteLine($"ChangeFields         : {changeFields.Count}");
        Console.WriteLine();
        Console.WriteLine("Encode and decode timings");
        Console.WriteLine("-------------------------------");

        WriteProfile("Vendors", vendors, iterations);
        WriteProfile("Warehouses", warehouses, iterations);
        WriteProfile("Variations", variations, iterations);
        WriteProfile("Categories", categories, iterations);
        WriteProfile("SubCategories", subCategories, iterations);
        WriteProfile("Changes", changes, iterations);
        WriteProfile("ChangeFields", changeFields, iterations);
    }

    private static void WriteProfile<T>(string name, List<T> payload, int iterations)
    {
        var jsonText = JsonSerializer.Serialize(payload, JsonOptions);
        var toonText = ToonEncoder.Encode(payload, ToonBenchmarkProfiles.ResolveEncodeOptions("compact"));
        var jsonBytes = Encoding.UTF8.GetByteCount(jsonText);
        var toonBytes = Encoding.UTF8.GetByteCount(toonText);
        var decodeOptions = ToonService.DetectDecodeOptions(toonText);

        // Warm JIT and caches before measuring.
        JsonSerializer.Deserialize<List<T>>(jsonText, JsonOptions);
        ToonDecoder.Decode<List<T>>(toonText, decodeOptions);

        var jsonEncodeMeasurements = Measure(iterations, () => JsonSerializer.Serialize(payload, JsonOptions));
        var toonEncodeMeasurements = Measure(iterations, () => ToonEncoder.Encode(payload, ToonBenchmarkProfiles.ResolveEncodeOptions("compact")));
        var jsonDecodeMeasurements = Measure(iterations, () => JsonSerializer.Deserialize<List<T>>(jsonText, JsonOptions));
        var toonDecodeMeasurements = Measure(iterations, () => ToonDecoder.Decode<List<T>>(toonText, decodeOptions));

        Console.WriteLine(
            $"{name,-20} count {payload.Count,6}   " +
            $"J enc {AverageMs(jsonEncodeMeasurements),7:F3} ms   " +
            $"T enc {AverageMs(toonEncodeMeasurements),7:F3} ms   " +
            $"J dec {AverageMs(jsonDecodeMeasurements),7:F3} ms   " +
            $"T dec {AverageMs(toonDecodeMeasurements),7:F3} ms   " +
            $"T bytes {toonBytes,9}");
    }

    private static List<MeasurementResult> Measure<T>(int iterations, Func<T> action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            _ = action();
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore));
        }

        return results;
    }

    private static double AverageMs(List<MeasurementResult> results)
        => results.Average(static result => result.Elapsed.TotalMilliseconds);

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

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes);
}
