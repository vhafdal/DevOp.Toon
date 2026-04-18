using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json;
using DevOp.Toon;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ParsingBenchmarks
{
    private string toonText = string.Empty;
    private string jsonText = string.Empty;
    private byte[] jsonUtf8 = [];

    [Params(10, 100, 1_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var payload = BenchmarkPayloadFactory.CreateCatalogPayload(ItemCount);
        jsonText = JsonSerializer.Serialize(payload, BenchmarkPayloadFactory.SerializerOptions);
        jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(payload, BenchmarkPayloadFactory.SerializerOptions);
        toonText = ToonEncoder.Encode(payload);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DOM")]
    public ToonNode? JsonNodeParse()
    {
        return ToonNode.Parse(jsonText);
    }

    [Benchmark]
    [BenchmarkCategory("DOM")]
    public ToonNode? ToonDecode()
    {
        return ToonDecoder.Decode(toonText);
    }

    [Benchmark]
    [BenchmarkCategory("UTF8")]
    public int JsonDocumentParse()
    {
        using var document = JsonDocument.Parse(jsonUtf8);
        return document.RootElement.GetProperty("Items").GetArrayLength();
    }
}

[ShortRunJob]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProductDeserializationBenchmarks
{
    private static readonly JsonSerializerOptions ProductSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly ToonDecodeOptions ToonDeserializeOptions = new()
    {
        Indent = 2,
        ExpandPaths = ToonPathExpansion.Safe,
    };
    
    private static readonly ToonEncodeOptions ToonSerializerOptions = new()
    {
        Indent = 2,
        KeyFolding = ToonKeyFolding.Safe,
        ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
        Delimiter = ToonDelimiter.COMMA
    };
    
    private static List<Product>? allProducts;
    private static List<Product>? allProductsFromToon;

    private string jsonText = string.Empty;
    private string toonText = string.Empty;
    
    [Params(100, 1000)]
    public int ProductCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        allProducts ??= LoadProducts();

        if (ProductCount > allProducts.Count)
        {
            throw new InvalidOperationException(
                $"Requested {ProductCount} products, but '{BenchmarkDataPaths.ProductsJsonPath}' only contains {allProducts.Count}.");
        }

        allProductsFromToon ??= LoadProductsFromToon();

        var products = allProducts.Take(ProductCount).ToList();
        var toonProducts = allProductsFromToon.Take(ProductCount).ToList();
        jsonText = JsonSerializer.Serialize(products, ProductSerializerOptions);
        toonText = ToonEncoder.Encode(toonProducts,ToonSerializerOptions );
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Typed")]
    public List<Product>? JsonDeserializeProducts()
    {
        return JsonSerializer.Deserialize<List<Product>>(jsonText, ProductSerializerOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Typed")]
    public List<Product>? ToonDecodeProducts()
    {
        return ToonDecoder.Decode<List<Product>>(toonText);
    }

    private static List<Product> LoadProducts()
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

    private static List<Product> LoadProductsFromToon()
    {
        if (!File.Exists(BenchmarkDataPaths.ProductsToonPath))
        {
            throw new FileNotFoundException(
                $"The product benchmark input file was not found at '{BenchmarkDataPaths.ProductsToonPath}'.",
                BenchmarkDataPaths.ProductsToonPath);
        }

        var sourceToon = File.ReadAllText(BenchmarkDataPaths.ProductsToonPath);
        return ToonDecoder.Decode<List<Product>>(sourceToon,ToonDeserializeOptions)
            ?? throw new InvalidOperationException("Failed to deserialize products from the TOON benchmark input.");
    }


}
