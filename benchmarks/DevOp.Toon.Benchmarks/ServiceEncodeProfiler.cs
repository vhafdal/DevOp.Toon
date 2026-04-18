using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

internal static class ServiceEncodeProfiler
{
    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--profile-service-encode", StringComparison.OrdinalIgnoreCase)))
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
        var compactOptions = ToonBenchmarkProfiles.ResolveEncodeOptions("compact");
        var service = new ToonService(new ToonServiceOptions
        {
            Indent = compactOptions.Indent,
            Delimiter = compactOptions.Delimiter,
            KeyFolding = compactOptions.KeyFolding,
            ObjectArrayLayout = compactOptions.ObjectArrayLayout
        });

        var encoderMeasurements = Measure(iterations, () => ToonEncoder.Encode(products, compactOptions));
        var serviceMeasurements = Measure(iterations, () => service.Encode(products));
        var encoderBytesMeasurements = MeasureBytes(iterations, () => ToonEncoder.EncodeToBytes(products, compactOptions));
        var serviceBytesMeasurements = MeasureBytes(iterations, () => service.Encode(products), static text => System.Text.Encoding.UTF8.GetBytes(text));

        Console.WriteLine("Service encode profile");
        Console.WriteLine($"Products             : {productCount}");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine();
        Console.WriteLine("Encode timings");
        Console.WriteLine("-------------------------------");
        WriteMeasurement("ToonEncoder.Encode", encoderMeasurements);
        WriteMeasurement("ToonService.Encode", serviceMeasurements);
        WriteMeasurement("ToonEncoder.Bytes", encoderBytesMeasurements);
        WriteMeasurement("ToonService+UTF8", serviceBytesMeasurements);
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

    private static List<MeasurementResult> MeasureBytes(int iterations, Func<byte[]> action)
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
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, result.Length));
        }

        return results;
    }

    private static List<MeasurementResult> MeasureBytes(int iterations, Func<string> action, Func<string, byte[]> byteEncoder)
    {
        var results = new List<MeasurementResult>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var text = action();
            var bytes = byteEncoder(text);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocatedAfter - allocatedBefore, bytes.Length));
        }

        return results;
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
