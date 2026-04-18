using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

internal static class BenchmarkReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly ToonEncodeOptions CompactOptions = ToonBenchmarkProfiles.ResolveEncodeOptions("compact");
    private static readonly ToonService ToonService = new();
    private const int ApiWarmupIterations = 5;
    private const int ApiIterations = 12;

    public static bool TryRun(string[] args)
    {
        if (!args.Any(static arg => string.Equals(arg, "--export-benchmark-report", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var count = TryReadInt(args, "--count") ?? 1_000;
        var iterations = TryReadInt(args, "--iterations") ?? 8;
        var outputPath = TryReadString(args, "--output")
            ?? Path.Combine(Environment.CurrentDirectory, "Documentation", "BenchmarkReport.md");

        Run(count, iterations, outputPath);
        return true;
    }

    private static void Run(int count, int iterations, string outputPath)
    {
        var reportCases = BuildCases(count, iterations);
        var results = reportCases.Select(static c => c.Run()).ToList();
#if HAS_API_BENCHMARKS
        var apiResults = MeasureApiProfiles(count);
#else
        List<ApiBenchmarkReportResult>? apiResults = null;
#endif
        var markdown = BuildMarkdown(count, iterations, results, apiResults);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, markdown, Encoding.UTF8);

        Console.WriteLine("Benchmark report exported");
        Console.WriteLine($"Count                : {count}");
        Console.WriteLine($"Iterations           : {iterations}");
        Console.WriteLine($"Output               : {outputPath}");
        Console.WriteLine();
        Console.WriteLine("Dataset summary");
        Console.WriteLine("-------------------------------");

        foreach (var result in results)
        {
            Console.WriteLine(
                $"{result.Name,-18} " +
                $"JSON enc {result.JsonEncodeMs,7:F3} ms   " +
                $"TOON enc {result.ToonEncodeMs,7:F3} ms   " +
                $"Raw save {result.RawReductionPercent,6:F2}%   " +
                $"Gzip save {result.GzipReductionPercent,6:F2}%");
        }

#if HAS_API_BENCHMARKS
        Console.WriteLine();
        Console.WriteLine("API summary");
        Console.WriteLine("-------------------------------");

        foreach (var result in apiResults)
        {
            Console.WriteLine(
                $"{result.Dataset,-18} " +
                $"JSON {result.JsonResponseMs,7:F3} ms   " +
                $"TOON {result.ToonResponseMs,7:F3} ms   " +
                $"{FormatApiDelta(result.JsonResponseMs, result.ToonResponseMs)}");
        }
#endif
    }

    private static List<IBenchmarkReportCase> BuildCases(int count, int iterations)
    {
        var products = ProductBenchmarkData.LoadProducts();
        if (count > products.Count)
        {
            throw new InvalidOperationException(
                $"Requested {count} products, but '{BenchmarkDataPaths.ProductsJsonPath}' only contains {products.Count}.");
        }

        var selectedProducts = products.Take(count).ToList();
        var warehouses = selectedProducts.SelectMany(static p => p.Warehouses ?? []).ToList();
        var changes = selectedProducts.SelectMany(static p => p.Changes ?? []).ToList();
        var changeFields = changes.SelectMany(static c => c.Fields ?? []).ToList();

        return
        [
            new BenchmarkReportCase<BenchmarkPayloadFactory.FlatOrder[]>(
                "Flat orders",
                "Uniform DTO rows; good for showing tabular TOON wins.",
                BenchmarkPayloadFactory.CreateFlatOrders(count),
                iterations),
            new BenchmarkReportCase<BenchmarkPayloadFactory.CatalogPayload>(
                "Catalog",
                "Synthetic nested catalog payload with arrays and embedded objects.",
                BenchmarkPayloadFactory.CreateCatalogPayload(count),
                iterations),
            new BenchmarkReportCase<List<Product>>(
                "Products",
                "Real-world mixed product payload from benchmark test data.",
                selectedProducts,
                iterations),
            new BenchmarkReportCase<List<Warehouse>>(
                "Warehouses",
                "Nested warehouse subtree extracted from products; current encoder hotspot.",
                warehouses,
                iterations),
            new BenchmarkReportCase<List<ChangeField>>(
                "ChangeFields",
                "Small repetitive row shape that benefits from compact tabular encoding.",
                changeFields,
                iterations),
            new BenchmarkReportCase<BenchmarkPayloadFactory.ConfigurationPayload>(
                "Configuration",
                "Document-style nested configuration object with service arrays.",
                BenchmarkPayloadFactory.CreateConfigurationPayload(Math.Max(10, count / 10)),
                iterations)
        ];
    }

    private static string BuildMarkdown(int count, int iterations, List<BenchmarkReportResult> results, List<ApiBenchmarkReportResult>? apiResults)
    {
        var bestRaw = results.MaxBy(static r => r.RawReductionPercent);
        var bestGzip = results.MaxBy(static r => r.GzipReductionPercent);
        var fastestEncode = results.MinBy(static r => r.ToonEncodeMs - r.JsonEncodeMs);
        var bestApi = apiResults?.MinBy(static r => r.ToonResponseMs - r.JsonResponseMs);

        var builder = new StringBuilder();
        builder.AppendLine("# DevOp.Toon Benchmark Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: `{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`");
        builder.AppendLine();
        builder.AppendLine($"Input count: `{count}`");
        builder.AppendLine();
        builder.AppendLine($"Iterations per dataset: `{iterations}`");
        builder.AppendLine();
        builder.AppendLine("## Why DevOp.Toon");
        builder.AppendLine();
        builder.AppendLine("- TOON is designed to compress repeated object rows and nested arrays more efficiently than JSON.");
        builder.AppendLine("- The strongest wins show up on real transport payloads with repeated fields, product catalogs, warehouse inventory, and change-log style rows.");
        builder.AppendLine("- The goal is not just raw bytes. The benchmark suite also tracks encode, decode, compression, and warmed local API response times.");
        builder.AppendLine("- This report is intended to help developers judge when a TOON-based NuGet package is a practical choice for transport and serialization scenarios.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Best raw size win: **{bestRaw!.Name}** at **{bestRaw.RawReductionPercent:F2}%** smaller than JSON.");
        builder.AppendLine($"- Best gzip win: **{bestGzip!.Name}** at **{bestGzip.GzipReductionPercent:F2}%** smaller than JSON.");
        builder.AppendLine($"- Most encode-competitive dataset: **{fastestEncode!.Name}** with a TOON-vs-JSON delta of **{(fastestEncode.ToonEncodeMs - fastestEncode.JsonEncodeMs):F3} ms**.");
        if (bestApi is not null)
        {
            builder.AppendLine($"- Best warmed local API result in this report: **{bestApi.Dataset}** with a TOON-vs-JSON delta of **{(bestApi.ToonResponseMs - bestApi.JsonResponseMs):F3} ms**.");
            builder.AppendLine("- These results are local serializer benchmarks. API timings should still be checked separately for transport scenarios.");
        }
        else
        {
            builder.AppendLine("- API response benchmarks are not included in this report because the current benchmark host only exports serializer and payload-shape results.");
        }
        builder.AppendLine();
        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("| Dataset | Shape | JSON bytes | TOON bytes | Raw save | Gzip save | Brotli save | JSON enc ms | TOON enc ms | JSON dec ms | TOON dec ms |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var result in results)
        {
            builder.AppendLine(
                $"| {result.Name} | {EscapePipe(result.Description)} | " +
                $"{result.JsonBytes} | {result.ToonBytes} | " +
                $"{result.RawReductionPercent:F2}% | {result.GzipReductionPercent:F2}% | {result.BrotliReductionPercent:F2}% | " +
                $"{result.JsonEncodeMs:F3} | {result.ToonEncodeMs:F3} | " +
                $"{FormatMs(result.JsonDecodeMs)} | {FormatMs(result.ToonDecodeMs)} |");
        }

        if (apiResults is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## API Response Snapshot");
            builder.AppendLine();
            builder.AppendLine($"Warmup iterations per dataset: `{ApiWarmupIterations}`");
            builder.AppendLine();
            builder.AppendLine($"Measured iterations per dataset: `{ApiIterations}`");
            builder.AppendLine();
            builder.AppendLine("| Dataset | JSON response ms | TOON response ms | Delta | Raw size save | Gzip save | Brotli save |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (var api in apiResults)
            {
                var delta = api.ToonResponseMs - api.JsonResponseMs;
                builder.AppendLine(
                    $"| {api.Dataset} | {api.JsonResponseMs:F3} | {api.ToonResponseMs:F3} | {delta:F3} ms | " +
                    $"{api.RawReductionPercent:F2}% | {api.GzipReductionPercent:F2}% | {api.BrotliReductionPercent:F2}% |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        builder.AppendLine("- `Flat orders` and `ChangeFields` are useful marketing shapes because TOON can express repeated row data very compactly.");
        builder.AppendLine("- `Products` and `Warehouses` show the more difficult mixed and nested object-array cases.");
        builder.AppendLine("- `Configuration` is included to show how TOON behaves on document-style payloads, not just tabular arrays.");

        return builder.ToString();
    }

    private static string EscapePipe(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatMs(double? value)
        => value.HasValue ? value.Value.ToString("F3") : "n/a";

    private static string FormatApiDelta(double jsonMs, double toonMs)
    {
        var delta = toonMs - jsonMs;
        return delta <= 0
            ? $"save {Math.Abs(delta):F3} ms"
            : $"extra {delta:F3} ms";
    }

    private static int GzipLength(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return checked((int)output.Length);
    }

    private static int BrotliLength(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(bytes, 0, bytes.Length);
        }

        return checked((int)output.Length);
    }

    private static double ComputeReductionPercent(double baseline, double candidate)
    {
        if (baseline <= 0)
        {
            return 0;
        }

        return ((baseline - candidate) / baseline) * 100d;
    }

    private static List<MeasurementResult> Measure(int iterations, Action action)
    {
        var results = new List<MeasurementResult>(iterations);

        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var start = Stopwatch.GetTimestamp();
            action();
            results.Add(new MeasurementResult(Stopwatch.GetElapsedTime(start)));
        }

        return results;
    }

    private static double AverageMs(List<MeasurementResult> results)
        => results.Average(static result => result.Elapsed.TotalMilliseconds);

#if HAS_API_BENCHMARKS
    private static List<ApiBenchmarkReportResult> MeasureApiProfiles(int count)
    {
        var datasets = new[] { "catalog", "products" };
        var results = new List<ApiBenchmarkReportResult>(datasets.Length);

        foreach (var dataset in datasets)
        {
            results.Add(MeasureApiProfile(dataset, count));
        }

        return results;
    }

    private static ApiBenchmarkReportResult MeasureApiProfile(string dataset, int count)
    {
        var harness = ApiResponseBenchmarkHarness.CreateAsync(dataset, count, CompactOptions).GetAwaiter().GetResult();
        try
        {
            WarmupApi(harness);
            var jsonMeasurements = MeasureApi(ApiIterations, harness.GetJsonAsync);
            var toonMeasurements = MeasureApi(ApiIterations, harness.GetToonAsync);

            return new ApiBenchmarkReportResult(
                harness.DatasetName,
                AverageMs(jsonMeasurements),
                AverageMs(toonMeasurements),
                ComputeReductionPercent(harness.JsonPayloadBytes, harness.ToonPayloadBytes),
                ComputeReductionPercent(harness.JsonGzipBytes, harness.ToonGzipBytes),
                ComputeReductionPercent(harness.JsonBrotliBytes, harness.ToonBrotliBytes));
        }
        finally
        {
            harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void WarmupApi(ApiResponseBenchmarkHarness harness)
    {
        for (int i = 0; i < ApiWarmupIterations; i++)
        {
            harness.GetJsonAsync().GetAwaiter().GetResult();
            harness.GetToonAsync().GetAwaiter().GetResult();
        }
    }

    private static List<MeasurementResult> MeasureApi(int iterations, Func<Task<int>> action)
    {
        var results = new List<MeasurementResult>(iterations);

        for (int i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            action().GetAwaiter().GetResult();
            results.Add(new MeasurementResult(Stopwatch.GetElapsedTime(start)));
        }

        return results;
    }
#endif

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

    private static string? TryReadString(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private interface IBenchmarkReportCase
    {
        BenchmarkReportResult Run();
    }

    private sealed class BenchmarkReportCase<T> : IBenchmarkReportCase
    {
        private readonly string name;
        private readonly string description;
        private readonly T payload;
        private readonly int iterations;

        public BenchmarkReportCase(string name, string description, T payload, int iterations)
        {
            this.name = name;
            this.description = description;
            this.payload = payload;
            this.iterations = iterations;
        }

        public BenchmarkReportResult Run()
        {
            var jsonText = JsonSerializer.Serialize(payload, JsonOptions);
            var toonText = ToonEncoder.Encode(payload, CompactOptions);
            double? jsonDecodeMs = null;
            double? toonDecodeMs = null;

            var jsonEncode = Measure(iterations, () => JsonSerializer.Serialize(payload, JsonOptions));
            var toonEncode = Measure(iterations, () => ToonEncoder.Encode(payload, CompactOptions));

            try
            {
                var decodeOptions = ToonService.DetectDecodeOptions(toonText);
                JsonSerializer.Deserialize<T>(jsonText, JsonOptions);
                ToonDecoder.Decode<T>(toonText, decodeOptions);

                var jsonDecode = Measure(iterations, () => JsonSerializer.Deserialize<T>(jsonText, JsonOptions));
                var toonDecode = Measure(iterations, () => ToonDecoder.Decode<T>(toonText, decodeOptions));
                jsonDecodeMs = AverageMs(jsonDecode);
                toonDecodeMs = AverageMs(toonDecode);
            }
            catch (NotSupportedException)
            {
                jsonDecodeMs = null;
                toonDecodeMs = null;
            }

            var jsonBytes = Encoding.UTF8.GetByteCount(jsonText);
            var toonBytes = Encoding.UTF8.GetByteCount(toonText);
            var jsonGzipBytes = GzipLength(jsonText);
            var toonGzipBytes = GzipLength(toonText);
            var jsonBrotliBytes = BrotliLength(jsonText);
            var toonBrotliBytes = BrotliLength(toonText);

            return new BenchmarkReportResult(
                name,
                description,
                jsonBytes,
                toonBytes,
                ComputeReductionPercent(jsonBytes, toonBytes),
                ComputeReductionPercent(jsonGzipBytes, toonGzipBytes),
                ComputeReductionPercent(jsonBrotliBytes, toonBrotliBytes),
                AverageMs(jsonEncode),
                AverageMs(toonEncode),
                jsonDecodeMs,
                toonDecodeMs);
        }
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed);

    private sealed record BenchmarkReportResult(
        string Name,
        string Description,
        int JsonBytes,
        int ToonBytes,
        double RawReductionPercent,
        double GzipReductionPercent,
        double BrotliReductionPercent,
        double JsonEncodeMs,
        double ToonEncodeMs,
        double? JsonDecodeMs,
        double? ToonDecodeMs);

    private sealed record ApiBenchmarkReportResult(
        string Dataset,
        double JsonResponseMs,
        double ToonResponseMs,
        double RawReductionPercent,
        double GzipReductionPercent,
        double BrotliReductionPercent);
}
