using System.Diagnostics;
using System.Reflection;
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
        var decodeOptions = CreateProfilingDecodeOptions();
        var harness = ReflectionHarness.Create(decodeOptions);

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

    private static ToonDecodeOptions CreateProfilingDecodeOptions()
    {
        var defaults = ToonDecoder.DefaultOptions;
        return new ToonDecodeOptions
        {
            ExpandPaths = defaults.ExpandPaths,
            Strict = defaults.Strict,
            Indent = defaults.Indent > 0 ? defaults.Indent : 2
        };
    }

    private readonly record struct MeasurementResult(TimeSpan Elapsed, long AllocatedBytes);

    private sealed class ReflectionHarness
    {
        private readonly object resolvedOptions;
        private readonly int indent;
        private readonly bool strict;
        private readonly MethodInfo scanMethod;
        private readonly ConstructorInfo lineCursorConstructor;
        private readonly PropertyInfo scanLinesProperty;
        private readonly PropertyInfo scanBlankLinesProperty;
        private readonly MethodInfo directDecodeMethod;
        private readonly MethodInfo typedDecodeMethod;

        private ReflectionHarness(
            object resolvedOptions,
            int indent,
            bool strict,
            MethodInfo scanMethod,
            ConstructorInfo lineCursorConstructor,
            PropertyInfo scanLinesProperty,
            PropertyInfo scanBlankLinesProperty,
            MethodInfo directDecodeMethod,
            MethodInfo typedDecodeMethod)
        {
            this.resolvedOptions = resolvedOptions;
            this.indent = indent;
            this.strict = strict;
            this.scanMethod = scanMethod;
            this.lineCursorConstructor = lineCursorConstructor;
            this.scanLinesProperty = scanLinesProperty;
            this.scanBlankLinesProperty = scanBlankLinesProperty;
            this.directDecodeMethod = directDecodeMethod;
            this.typedDecodeMethod = typedDecodeMethod;
        }

        public static ReflectionHarness Create(ToonDecodeOptions decodeOptions)
        {
            var assembly = typeof(ToonDecoder).Assembly;
            var scannerType = assembly.GetType("DevOp.Toon.Internal.Decode.Scanner")
                ?? throw new InvalidOperationException("Scanner type was not found.");
            var scanResultType = assembly.GetType("DevOp.Toon.Internal.Decode.ScanResult")
                ?? throw new InvalidOperationException("ScanResult type was not found.");
            var lineCursorType = assembly.GetType("DevOp.Toon.Internal.Decode.LineCursor")
                ?? throw new InvalidOperationException("LineCursor type was not found.");
            var resolvedOptionsType = assembly.GetType("DevOp.Toon.Internal.Decode.ResolvedDecodeOptions")
                ?? throw new InvalidOperationException("ResolvedDecodeOptions type was not found.");
            var directMaterializerType = assembly.GetType("DevOp.Toon.Internal.Decode.DirectMaterializer")
                ?? throw new InvalidOperationException("DirectMaterializer type was not found.");
            var typedDecoderType = assembly.GetType("DevOp.Toon.Internal.Decode.TypedDecoder")
                ?? throw new InvalidOperationException("TypedDecoder type was not found.");

            var scanMethod = scannerType.GetMethod("ToParsedLines", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Scanner.ToParsedLines was not found.");
            var lineCursorConstructor = lineCursorType.GetConstructor(new[] { typeof(List<>).MakeGenericType(assembly.GetType("DevOp.Toon.Internal.Decode.ParsedLine")!), typeof(List<>).MakeGenericType(assembly.GetType("DevOp.Toon.Internal.Decode.BlankLineInfo")!) })
                ?? throw new InvalidOperationException("LineCursor constructor was not found.");
            var scanLinesProperty = scanResultType.GetProperty("Lines", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ScanResult.Lines was not found.");
            var scanBlankLinesProperty = scanResultType.GetProperty("BlankLines", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("ScanResult.BlankLines was not found.");

            var resolvedOptions = Activator.CreateInstance(resolvedOptionsType)
                ?? throw new InvalidOperationException("ResolvedDecodeOptions could not be created.");
            resolvedOptionsType.GetProperty("Indent")!.SetValue(resolvedOptions, decodeOptions.Indent);
            resolvedOptionsType.GetProperty("Strict")!.SetValue(resolvedOptions, decodeOptions.Strict);
            resolvedOptionsType.GetProperty("ExpandPaths")!.SetValue(resolvedOptions, decodeOptions.ExpandPaths);

            var directDecodeMethod = directMaterializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "TryDecode"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 3)
                .MakeGenericMethod(typeof(List<Product>));

            var typedDecodeMethod = typedDecoderType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "Decode"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 2
                    && method.GetParameters()[0].ParameterType == typeof(string))
                .MakeGenericMethod(typeof(List<Product>));

            return new ReflectionHarness(
                resolvedOptions,
                decodeOptions.Indent,
                decodeOptions.Strict,
                scanMethod,
                lineCursorConstructor,
                scanLinesProperty,
                scanBlankLinesProperty,
                directDecodeMethod,
                typedDecodeMethod);
        }

        public object Scan(string toonText)
        {
            return scanMethod.Invoke(null, [toonText, indent, strict])
                ?? throw new InvalidOperationException("Scanner.ToParsedLines returned null.");
        }

        public bool DirectDecode(object scanResult)
        {
            var cursor = CreateCursor(scanResult);
            var args = new object?[] { cursor, resolvedOptions, null };
            var success = (bool)(directDecodeMethod.Invoke(null, args) ?? false);
            return success;
        }

        public void TypedDecode(string toonText)
        {
            _ = typedDecodeMethod.Invoke(null, [toonText, resolvedOptions]);
        }

        private object CreateCursor(object scanResult)
        {
            var lines = scanLinesProperty.GetValue(scanResult)
                ?? throw new InvalidOperationException("ScanResult.Lines returned null.");
            var blankLines = scanBlankLinesProperty.GetValue(scanResult)
                ?? throw new InvalidOperationException("ScanResult.BlankLines returned null.");
            return lineCursorConstructor.Invoke([lines, blankLines]);
        }
    }
}
