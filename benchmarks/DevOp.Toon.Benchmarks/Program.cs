using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Running;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        ToonEncoder.DefaultOptions = new ToonEncodeOptions
        {
            KeyFolding = ToonKeyFolding.Off,
            Delimiter = ToonDelimiter.COMMA,
            Indent = 1,
            ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
            ExcludeEmptyArrays = true,
            IgnoreNullOrEmpty = true
        };
        
        ToonDecoder.DefaultOptions = new ToonDecodeOptions
        {
            ExpandPaths = ToonPathExpansion.Safe,
            Indent = 2,
        };
        
        if (ProductDecodeProfiler.TryRun(args))
        {
            return;
        }

        if (ProductEncodeProfiler.TryRun(args))
        {
            return;
        }

        if (ProductLayoutComparisonProfiler.TryRun(args))
        {
            return;
        }

        if (ProductNestedProfiler.TryRun(args))
        {
            return;
        }

        if (NestedShapeProfiler.TryRun(args))
        {
            return;
        }

        if (ServiceEncodeProfiler.TryRun(args))
        {
            return;
        }

        if (DefaultEncodeProfiler.TryRun(args))
        {
            return;
        }

        if (ByteSequenceProfiler.TryRun(args))
        {
            return;
        }

        if (BenchmarkReportExporter.TryRun(args))
        {
            return;
        }

#if HAS_API_BENCHMARKS
        if (ApiResponseProfiler.TryRun(args))
        {
            return;
        }

        if (NetworkLatencyProfiler.TryRun(args))
        {
            return;
        }
#endif

        /*
    string ProductsJsonPath = @"C:\temp\prods.json";
    JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    var sourceJson = File.ReadAllText(ProductsJsonPath);
    var prods = JsonSerializer.Deserialize<List<Product>>(sourceJson, ProductSerializerOptions);
    var toonText = ToonEncoder.Encode(prods);
    System.IO.File.WriteAllText(@"C:\temp\prods.toon", toonText);
    */
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
