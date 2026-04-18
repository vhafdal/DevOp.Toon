using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using DevOp.Toon.Benchmarks.Models;

namespace DevOp.Toon.API.TestHost.Services;

public sealed class ProductDataService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly Lazy<IReadOnlyList<Product>> products;

    public ProductDataService(IOptions<ProductDataOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Value.DataPath))
        {
            throw new ArgumentException("The product data path must be configured.", nameof(options));
        }

        products = new Lazy<IReadOnlyList<Product>>(() => LoadProducts(options.Value.DataPath));
    }

    public bool TryGetPage(int page, int pageSize, out IReadOnlyList<Product> pageItems)
    {
        var allProducts = products.Value;
        var skip = ((long)page - 1L) * pageSize;

        if (skip < 0 || skip >= allProducts.Count)
        {
            pageItems = [];
            return false;
        }

        pageItems = allProducts.Skip((int)skip).Take(pageSize).ToList();
        return true;
    }

    private static IReadOnlyList<Product> LoadProducts(string dataPath)
    {
        if (!File.Exists(dataPath))
        {
            throw new FileNotFoundException(
                $"The product benchmark input file was not found at '{dataPath}'.",
                dataPath);
        }

        var sourceJson = File.ReadAllText(dataPath, Encoding.Unicode);
        return JsonSerializer.Deserialize<List<Product>>(sourceJson, SerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize products from the JSON benchmark input.");
    }
}
