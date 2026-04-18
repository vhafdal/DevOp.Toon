using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

internal static class ProductNestedProfiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-products-nested", StringComparison.OrdinalIgnoreCase)))
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

        Console.WriteLine("Product nested encode profile");
        Console.WriteLine($"Products             : {productCount}");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine();
        Console.WriteLine("Subtree sizes");
        Console.WriteLine("-------------------------------");
        Console.WriteLine($"Vendors              : {vendors.Count}");
        Console.WriteLine($"Warehouses           : {warehouses.Count}");
        Console.WriteLine($"Variations           : {variations.Count}");
        Console.WriteLine($"Categories           : {categories.Count}");
        Console.WriteLine($"SubCategories        : {subCategories.Count}");
        Console.WriteLine($"Changes              : {changes.Count}");
        Console.WriteLine($"ChangeFields         : {changeFields.Count}");
        Console.WriteLine();
        Console.WriteLine("Compact encode timings");
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
        var jsonMeasurements = Measure(iterations, payload, static value => JsonSerializer.Serialize(value, JsonOptions));
        var toonMeasurements = Measure(iterations, payload, static value => ToonEncoder.Encode(value, ToonBenchmarkProfiles.ResolveEncodeOptions("compact")));

        Console.WriteLine($"{name,-20} count {payload.Count,6}   JSON {AverageMs(jsonMeasurements),8:F3} ms   TOON {AverageMs(toonMeasurements),8:F3} ms   TOON bytes {AverageBytes(toonMeasurements),10:F0}");
    }

    private static List<MeasurementResult> Measure<T>(int iterations, T payload, Func<T, string> action)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = action(payload);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, System.Text.Encoding.UTF8.GetByteCount(result)));
        }

        return results;
    }

    private static double AverageMs(List<MeasurementResult> results)
        => results.Average(static result => result.Elapsed.TotalMilliseconds);

    private static double AverageBytes(List<MeasurementResult> results)
        => results.Average(static result => result.OutputBytes);

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
