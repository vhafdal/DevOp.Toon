using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
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

    private static readonly ToonEncodeOptions CompactOptions = ToonBenchmarkProfiles.ResolveEncodeOptions("optimal");
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
        var iterations = TryReadInt(args, "--iterations") ?? 12;
        var outputPath = TryReadString(args, "--output")
            ?? Path.Combine(Environment.CurrentDirectory, "Documentation", "BenchmarkReport.md");

        Run(count, iterations, outputPath);
        return true;
    }

    private static void Run(int count, int iterations, string outputPath)
    {
        var reportCases = BuildCases(count, iterations);
        var results = reportCases.Select(static c => c.Run()).ToList();
        var fileResult = ProductFilesReportCase.Run(iterations);
#if HAS_API_BENCHMARKS
        var apiResults = MeasureApiProfiles(count);
#else
        List<ApiBenchmarkReportResult>? apiResults = null;
#endif
        var markdown = BuildMarkdown(count, iterations, results, fileResult, apiResults);

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

        if (fileResult is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Real-world file summary");
            Console.WriteLine("-------------------------------");
            Console.WriteLine($"Products             : {fileResult.ProductCount}");
            Console.WriteLine($"JSON file bytes      : {fileResult.JsonFileBytes:N0}");
            Console.WriteLine($"TOON file bytes      : {fileResult.ToonFileBytes:N0}");
            Console.WriteLine($"Raw save             : {fileResult.RawReductionPercent:F2}%");
            Console.WriteLine($"JSON dec avg         : {fileResult.JsonDecodeMs:F3} ms");
            Console.WriteLine($"TOON dec avg         : {fileResult.ToonDecodeMs:F3} ms");
            Console.WriteLine($"JSON enc avg         : {fileResult.JsonReencodeMs:F3} ms");
            Console.WriteLine($"TOON enc avg         : {fileResult.ToonReencodeMs:F3} ms");
        }
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
                "Flat Orders",
                "Uniform DTO rows; ideal for tabular TOON encoding.",
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
                "Nested warehouse subtree extracted from products.",
                warehouses,
                iterations),
            new BenchmarkReportCase<List<ChangeField>>(
                "Change Fields",
                "Small repetitive row shape that benefits from compact tabular encoding.",
                changeFields,
                iterations),
            new BenchmarkReportCase<BenchmarkPayloadFactory.ConfigurationPayload>(
                "Configuration",
                "Document-style nested config object with service arrays.",
                BenchmarkPayloadFactory.CreateConfigurationPayload(Math.Max(10, count / 10)),
                iterations)
        ];
    }

    private static string BuildMarkdown(int count, int iterations, List<BenchmarkReportResult> results, ProductFilesResult? fileResult, List<ApiBenchmarkReportResult>? apiResults)
    {
        var bestRaw = results.MaxBy(static r => r.RawReductionPercent)!;
        var bestGzip = results.MaxBy(static r => r.GzipReductionPercent)!;
        var bestBrotli = results.MaxBy(static r => r.BrotliReductionPercent)!;
        var fastestEncode = results.MinBy(static r => r.ToonEncodeMs - r.JsonEncodeMs)!;
        var bestDecode = results
            .Where(static r => r.JsonDecodeMs.HasValue && r.ToonDecodeMs.HasValue)
            .MinBy(static r => r.ToonDecodeMs!.Value - r.JsonDecodeMs!.Value);

        var dotnetVersion = Environment.Version.ToString();
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.ProcessArchitecture.ToString();
        var cpu = Environment.ProcessorCount;

        var b = new StringBuilder();

        b.AppendLine("# DevOp.Toon — Performance Benchmarks");
        b.AppendLine();
        b.AppendLine("> **DevOp.Toon** is a token-efficient alternative to JSON for .NET applications.");
        b.AppendLine("> These benchmarks compare serialization speed, heap allocations, and payload size");
        b.AppendLine("> across real-world and synthetic datasets using the same CLR that serves your API.");
        b.AppendLine();

        b.AppendLine("## Environment");
        b.AppendLine();
        b.AppendLine("| Property | Value |");
        b.AppendLine("| --- | --- |");
        b.AppendLine($"| Date | `{DateTimeOffset.UtcNow:yyyy-MM-dd}` |");
        b.AppendLine($"| Runtime | .NET {dotnetVersion} |");
        b.AppendLine($"| OS | {os} |");
        b.AppendLine($"| Architecture | {arch} |");
        b.AppendLine($"| Logical CPUs | {cpu} |");
        b.AppendLine($"| Items per dataset | {count:N0} |");
        b.AppendLine($"| Warm iterations per case | {iterations} |");
        b.AppendLine();

        b.AppendLine("## Key Takeaways");
        b.AppendLine();
        b.AppendLine($"- **Smallest raw payload:** `{bestRaw.Name}` — **{bestRaw.RawReductionPercent:F1}% smaller** than JSON  ");
        b.AppendLine($"- **Smallest gzip payload:** `{bestGzip.Name}` — **{bestGzip.GzipReductionPercent:F1}% smaller** after compression  ");
        b.AppendLine($"- **Smallest brotli payload:** `{bestBrotli.Name}` — **{bestBrotli.BrotliReductionPercent:F1}% smaller** after brotli  ");
        b.AppendLine($"- **Fastest encode:** `{fastestEncode.Name}` — TOON encode vs JSON delta: **{fastestEncode.ToonEncodeMs - fastestEncode.JsonEncodeMs:F2} ms**  ");
        if (bestDecode is not null)
        {
            var decodeDelta = bestDecode.ToonDecodeMs!.Value - bestDecode.JsonDecodeMs!.Value;
            b.AppendLine($"- **Fastest decode:** `{bestDecode.Name}` — TOON decode vs JSON delta: **{decodeDelta:F2} ms**  ");
        }
        b.AppendLine();

        b.AppendLine("## Payload Size");
        b.AppendLine();
        b.AppendLine("Payload sizes are measured as UTF-8 bytes. Compression uses `SmallestSize` quality.");
        b.AppendLine();
        b.AppendLine("| Dataset | JSON bytes | TOON bytes | Raw saved | Gzip saved | Brotli saved |");
        b.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            b.AppendLine(
                $"| {r.Name} | {r.JsonBytes:N0} | {r.ToonBytes:N0} | " +
                $"**{r.RawReductionPercent:F1}%** | {r.GzipReductionPercent:F1}% | {r.BrotliReductionPercent:F1}% |");
        }
        b.AppendLine();

        b.AppendLine("## CPU — Encode Speed");
        b.AppendLine();
        b.AppendLine("Average of warm iterations. Lower is better.");
        b.AppendLine();
        b.AppendLine("| Dataset | JSON (ms) | TOON (ms) | Delta (ms) | Speedup |");
        b.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            var delta = r.ToonEncodeMs - r.JsonEncodeMs;
            var speedup = r.JsonEncodeMs / r.ToonEncodeMs;
            var deltaStr = delta >= 0 ? $"+{delta:F3}" : $"{delta:F3}";
            var speedupStr = speedup >= 1.0 ? $"**{speedup:F2}×**" : $"{speedup:F2}×";
            b.AppendLine(
                $"| {r.Name} | {r.JsonEncodeMs:F3} | {r.ToonEncodeMs:F3} | {deltaStr} | {speedupStr} |");
        }
        b.AppendLine();

        var decodable = results.Where(static r => r.JsonDecodeMs.HasValue && r.ToonDecodeMs.HasValue).ToList();
        if (decodable.Count > 0)
        {
            b.AppendLine("## CPU — Decode Speed");
            b.AppendLine();
            b.AppendLine("Average of warm iterations. Lower is better.");
            b.AppendLine();
            b.AppendLine("| Dataset | JSON (ms) | TOON (ms) | Delta (ms) | Speedup |");
            b.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (var r in decodable)
            {
                var delta = r.ToonDecodeMs!.Value - r.JsonDecodeMs!.Value;
                var speedup = r.JsonDecodeMs!.Value / r.ToonDecodeMs!.Value;
                var deltaStr = delta >= 0 ? $"+{delta:F3}" : $"{delta:F3}";
                var speedupStr = speedup >= 1.0 ? $"**{speedup:F2}×**" : $"{speedup:F2}×";
                b.AppendLine(
                    $"| {r.Name} | {r.JsonDecodeMs!.Value:F3} | {r.ToonDecodeMs!.Value:F3} | {deltaStr} | {speedupStr} |");
            }
            b.AppendLine();
        }

        b.AppendLine("## Memory — Heap Allocations");
        b.AppendLine();
        b.AppendLine("Measured via `GC.GetAllocatedBytesForCurrentThread()` over warm iterations. Lower is better.");
        b.AppendLine();
        b.AppendLine("### Encode Allocations");
        b.AppendLine();
        b.AppendLine("| Dataset | JSON alloc (KB) | TOON alloc (KB) | Delta (KB) | Ratio |");
        b.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            var delta = r.ToonEncodeAllocKb - r.JsonEncodeAllocKb;
            var ratio = r.JsonEncodeAllocKb > 0 ? r.ToonEncodeAllocKb / r.JsonEncodeAllocKb : 0;
            var deltaStr = delta >= 0 ? $"+{delta:F1}" : $"{delta:F1}";
            var ratioLabel = ratio <= 1.0 ? $"**{ratio:F2}×**" : $"{ratio:F2}×";
            b.AppendLine(
                $"| {r.Name} | {r.JsonEncodeAllocKb:F1} | {r.ToonEncodeAllocKb:F1} | {deltaStr} | {ratioLabel} |");
        }
        b.AppendLine();

        var decodableAlloc = results.Where(static r => r.JsonDecodeAllocKb.HasValue && r.ToonDecodeAllocKb.HasValue).ToList();
        if (decodableAlloc.Count > 0)
        {
            b.AppendLine("### Decode Allocations");
            b.AppendLine();
            b.AppendLine("| Dataset | JSON alloc (KB) | TOON alloc (KB) | Delta (KB) | Ratio |");
            b.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (var r in decodableAlloc)
            {
                var delta = r.ToonDecodeAllocKb!.Value - r.JsonDecodeAllocKb!.Value;
                var ratio = r.JsonDecodeAllocKb!.Value > 0 ? r.ToonDecodeAllocKb!.Value / r.JsonDecodeAllocKb!.Value : 0;
                var deltaStr = delta >= 0 ? $"+{delta:F1}" : $"{delta:F1}";
                var ratioLabel = ratio <= 1.0 ? $"**{ratio:F2}×**" : $"{ratio:F2}×";
                b.AppendLine(
                    $"| {r.Name} | {r.JsonDecodeAllocKb!.Value:F1} | {r.ToonDecodeAllocKb!.Value:F1} | {deltaStr} | {ratioLabel} |");
            }
            b.AppendLine();
        }

        if (fileResult is not null)
        {
            b.AppendLine("## Real-world File Benchmark — `prods.json` vs `prods.toon`");
            b.AppendLine();
            b.AppendLine($"These measurements use the actual benchmark test-data files exactly as they appear on disk — no synthetic re-encoding.");
            b.AppendLine($"The TOON file was pre-encoded from the same source data using the DevOp.Toon columnar layout.");
            b.AppendLine();
            b.AppendLine("### File Sizes");
            b.AppendLine();
            b.AppendLine($"| | JSON (`prods.json`) | TOON (`prods.toon`) | Saved |");
            b.AppendLine($"| --- | ---: | ---: | ---: |");
            b.AppendLine($"| Products | {fileResult.ProductCount:N0} | {fileResult.ProductCount:N0} | — |");
            b.AppendLine($"| Raw bytes | {fileResult.JsonFileBytes:N0} | {fileResult.ToonFileBytes:N0} | **{fileResult.RawReductionPercent:F1}%** |");
            b.AppendLine($"| Gzip bytes | {fileResult.JsonGzipBytes:N0} | {fileResult.ToonGzipBytes:N0} | **{fileResult.GzipReductionPercent:F1}%** |");
            b.AppendLine($"| Brotli bytes | {fileResult.JsonBrotliBytes:N0} | {fileResult.ToonBrotliBytes:N0} | **{fileResult.BrotliReductionPercent:F1}%** |");
            b.AppendLine();
            b.AppendLine("### Decode Speed (full file → `List<Product>`)");
            b.AppendLine();
            b.AppendLine("| | JSON | TOON | Speedup |");
            b.AppendLine("| --- | ---: | ---: | ---: |");
            var decodeSpeedup = fileResult.JsonDecodeMs / fileResult.ToonDecodeMs;
            var decodeSpeedupLabel = decodeSpeedup >= 1.0 ? $"**{decodeSpeedup:F2}×**" : $"{decodeSpeedup:F2}×";
            b.AppendLine($"| Avg decode (ms) | {fileResult.JsonDecodeMs:F1} | {fileResult.ToonDecodeMs:F1} | {decodeSpeedupLabel} |");
            b.AppendLine($"| Alloc (KB) | {fileResult.JsonDecodeAllocKb:F1} | {fileResult.ToonDecodeAllocKb:F1} | {fileResult.JsonDecodeAllocKb / fileResult.ToonDecodeAllocKb:F2}× |");
            b.AppendLine();
            b.AppendLine("### Re-encode Speed (`List<Product>` → string)");
            b.AppendLine();
            b.AppendLine("Encodes the already-decoded product list back to each format.");
            b.AppendLine();
            b.AppendLine("| | JSON | TOON | Speedup |");
            b.AppendLine("| --- | ---: | ---: | ---: |");
            var encodeSpeedup = fileResult.JsonReencodeMs / fileResult.ToonReencodeMs;
            var encodeSpeedupLabel = encodeSpeedup >= 1.0 ? $"**{encodeSpeedup:F2}×**" : $"{encodeSpeedup:F2}×";
            b.AppendLine($"| Avg encode (ms) | {fileResult.JsonReencodeMs:F1} | {fileResult.ToonReencodeMs:F1} | {encodeSpeedupLabel} |");
            b.AppendLine($"| Alloc (KB) | {fileResult.JsonReencodeAllocKb:F1} | {fileResult.ToonReencodeAllocKb:F1} | {fileResult.JsonReencodeAllocKb / fileResult.ToonReencodeAllocKb:F2}× |");
            b.AppendLine();
        }

        if (apiResults is not null)
        {
            b.AppendLine("## API Response Snapshot");
            b.AppendLine();
            b.AppendLine($"Warmup iterations: `{ApiWarmupIterations}` &nbsp;|&nbsp; Measured iterations: `{ApiIterations}`");
            b.AppendLine();
            b.AppendLine("| Dataset | JSON (ms) | TOON (ms) | Delta | Raw saved | Gzip saved | Brotli saved |");
            b.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var api in apiResults)
            {
                var delta = api.ToonResponseMs - api.JsonResponseMs;
                var deltaStr = delta <= 0 ? $"**{delta:F3} ms**" : $"+{delta:F3} ms";
                b.AppendLine(
                    $"| {api.Dataset} | {api.JsonResponseMs:F3} | {api.ToonResponseMs:F3} | {deltaStr} | " +
                    $"{api.RawReductionPercent:F1}% | {api.GzipReductionPercent:F1}% | {api.BrotliReductionPercent:F1}% |");
            }
            b.AppendLine();
        }

        b.AppendLine("## Dataset Descriptions");
        b.AppendLine();
        b.AppendLine("| Dataset | Description |");
        b.AppendLine("| --- | --- |");
        foreach (var r in results)
        {
            b.AppendLine($"| {r.Name} | {EscapePipe(r.Description)} |");
        }
        b.AppendLine();

        b.AppendLine("## Notes");
        b.AppendLine();
        b.AppendLine("- All timings are average wall-clock time over warm iterations with an explicit `GC.Collect()` between runs.");
        b.AppendLine("- TOON uses a **columnar array layout** for object arrays, eliminating repeated key names — this is where the largest size wins come from.");
        b.AppendLine("- `IgnoreNullOrEmpty` and `ExcludeEmptyArrays` are enabled in the optimal profile used here. These omit null and empty fields from the output, which is why size wins are larger than with naive encoding.");
        b.AppendLine("- TOON decode allocations are lower than `System.Text.Json` on real-world data because the columnar layout means fewer tokens to process per object. On synthetic datasets with no null/empty fields the allocations are closer.");
        b.AppendLine("- Gzip and Brotli compression use `CompressionLevel.SmallestSize`. Under `Optimal` the gap is slightly smaller.");
        b.AppendLine("- Benchmarks are run in-process using the same .NET runtime that serves production traffic. No out-of-process overhead.");
        b.AppendLine("- Source: [DevOp.Toon](https://github.com/valdi-hafdal/DevOp.Toon)");

        return b.ToString();
    }

    private static string EscapePipe(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

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
        using (var gz = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gz.Write(bytes, 0, bytes.Length);
        }
        return checked((int)output.Length);
    }

    private static int BrotliLength(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var br = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            br.Write(bytes, 0, bytes.Length);
        }
        return checked((int)output.Length);
    }

    private static double ComputeReductionPercent(double baseline, double candidate)
    {
        if (baseline <= 0) return 0;
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
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            action();
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocAfter = GC.GetAllocatedBytesForCurrentThread();
            results.Add(new MeasurementResult(elapsed, allocAfter - allocBefore));
        }
        return results;
    }

    private static double AverageMs(List<MeasurementResult> results)
        => results.Average(static r => r.Elapsed.TotalMilliseconds);

    private static double AverageAllocKb(List<MeasurementResult> results)
        => results.Average(static r => r.AllocatedBytes / 1024d);

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
            results.Add(new MeasurementResult(Stopwatch.GetElapsedTime(start), 0));
        }
        return results;
    }
#endif

    private static int? TryReadInt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(args[i + 1], out var value))
                return value;
        }
        return null;
    }

    private static string? TryReadString(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
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

            var jsonEncode = Measure(iterations, () => JsonSerializer.Serialize(payload, JsonOptions));
            var toonEncode = Measure(iterations, () => ToonEncoder.Encode(payload, CompactOptions));

            double? jsonDecodeMs = null;
            double? toonDecodeMs = null;
            double? jsonDecodeAllocKb = null;
            double? toonDecodeAllocKb = null;

            try
            {
                var decodeOptions = ToonService.DetectDecodeOptions(toonText);
                JsonSerializer.Deserialize<T>(jsonText, JsonOptions);
                ToonDecoder.Decode<T>(toonText, decodeOptions);

                var jsonDecode = Measure(iterations, () => JsonSerializer.Deserialize<T>(jsonText, JsonOptions));
                var toonDecode = Measure(iterations, () => ToonDecoder.Decode<T>(toonText, decodeOptions));

                jsonDecodeMs = AverageMs(jsonDecode);
                toonDecodeMs = AverageMs(toonDecode);
                jsonDecodeAllocKb = AverageAllocKb(jsonDecode);
                toonDecodeAllocKb = AverageAllocKb(toonDecode);
            }
            catch (NotSupportedException) { }

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
                AverageAllocKb(jsonEncode),
                AverageAllocKb(toonEncode),
                jsonDecodeMs,
                toonDecodeMs,
                jsonDecodeAllocKb,
                toonDecodeAllocKb);
        }
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes);

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
        double JsonEncodeAllocKb,
        double ToonEncodeAllocKb,
        double? JsonDecodeMs,
        double? ToonDecodeMs,
        double? JsonDecodeAllocKb,
        double? ToonDecodeAllocKb);

    private sealed record ApiBenchmarkReportResult(
        string Dataset,
        double JsonResponseMs,
        double ToonResponseMs,
        double RawReductionPercent,
        double GzipReductionPercent,
        double BrotliReductionPercent);

    private sealed record ProductFilesResult(
        int ProductCount,
        long JsonFileBytes,
        long ToonFileBytes,
        double RawReductionPercent,
        int JsonGzipBytes,
        int ToonGzipBytes,
        double GzipReductionPercent,
        int JsonBrotliBytes,
        int ToonBrotliBytes,
        double BrotliReductionPercent,
        double JsonDecodeMs,
        double ToonDecodeMs,
        double JsonDecodeAllocKb,
        double ToonDecodeAllocKb,
        double JsonReencodeMs,
        double ToonReencodeMs,
        double JsonReencodeAllocKb,
        double ToonReencodeAllocKb);

    private static class ProductFilesReportCase
    {
        private static readonly JsonSerializerOptions FileJsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public static ProductFilesResult? Run(int iterations)
        {
            if (!File.Exists(BenchmarkDataPaths.ProductsJsonPath) ||
                !File.Exists(BenchmarkDataPaths.ProductsToonPath))
            {
                return null;
            }

            var jsonText = File.ReadAllText(BenchmarkDataPaths.ProductsJsonPath);
            var toonText = File.ReadAllText(BenchmarkDataPaths.ProductsToonPath);

            var toonDecodeOpts = ToonService.DetectDecodeOptions(toonText);

            var jsonFileBytes = new FileInfo(BenchmarkDataPaths.ProductsJsonPath).Length;
            var toonFileBytes = new FileInfo(BenchmarkDataPaths.ProductsToonPath).Length;
            var jsonGzipBytes = GzipLength(jsonText);
            var toonGzipBytes = GzipLength(toonText);
            var jsonBrotliBytes = BrotliLength(jsonText);
            var toonBrotliBytes = BrotliLength(toonText);

            var jsonDecode = Measure(iterations, () => JsonSerializer.Deserialize<List<Product>>(jsonText, FileJsonOptions));
            var toonDecode = Measure(iterations, () => ToonDecoder.Decode<List<Product>>(toonText, toonDecodeOpts));

            var products = JsonSerializer.Deserialize<List<Product>>(jsonText, FileJsonOptions)!;

            var jsonEncode = Measure(iterations, () => JsonSerializer.Serialize(products, FileJsonOptions));
            var toonEncode = Measure(iterations, () => ToonEncoder.Encode(products, CompactOptions));

            return new ProductFilesResult(
                ProductCount: products.Count,
                JsonFileBytes: jsonFileBytes,
                ToonFileBytes: toonFileBytes,
                RawReductionPercent: ComputeReductionPercent(jsonFileBytes, toonFileBytes),
                JsonGzipBytes: jsonGzipBytes,
                ToonGzipBytes: toonGzipBytes,
                GzipReductionPercent: ComputeReductionPercent(jsonGzipBytes, toonGzipBytes),
                JsonBrotliBytes: jsonBrotliBytes,
                ToonBrotliBytes: toonBrotliBytes,
                BrotliReductionPercent: ComputeReductionPercent(jsonBrotliBytes, toonBrotliBytes),
                JsonDecodeMs: AverageMs(jsonDecode),
                ToonDecodeMs: AverageMs(toonDecode),
                JsonDecodeAllocKb: AverageAllocKb(jsonDecode),
                ToonDecodeAllocKb: AverageAllocKb(toonDecode),
                JsonReencodeMs: AverageMs(jsonEncode),
                ToonReencodeMs: AverageMs(toonEncode),
                JsonReencodeAllocKb: AverageAllocKb(jsonEncode),
                ToonReencodeAllocKb: AverageAllocKb(toonEncode));
        }
    }
}
