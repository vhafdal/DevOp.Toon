using System.Text.Json;

namespace DevOp.Toon.Benchmarks;

internal static class BenchmarkPayloadFactory
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static CatalogPayload CreateCatalogPayload(int itemCount)
    {
        var items = new CatalogItem[itemCount];

        for (int i = 0; i < itemCount; i++)
        {
            items[i] = new CatalogItem(
                Id: i + 1,
                Sku: $"SKU-{i + 1:D5}",
                Name: $"Widget {i + 1}",
                Price: Math.Round(9.99m + (i * 0.37m), 2),
                Tags:
                [
                    $"segment-{i % 5}",
                    $"warehouse-{i % 3}",
                    i % 2 == 0 ? "featured" : "standard"
                ],
                Dimensions: new Dimensions(
                    Width: 10 + (i % 7),
                    Height: 5 + (i % 11),
                    Depth: 2 + (i % 13)),
                Inventory: new Inventory(
                    InStock: 25 + (i % 50),
                    Reserved: i % 4,
                    ReorderThreshold: 10 + (i % 8)),
                Supplier: new Supplier(
                    Id: $"SUP-{(i % 17) + 1:D3}",
                    Name: $"Supplier {(i % 17) + 1}",
                    Country: Countries[i % Countries.Length]));
        }

        return new CatalogPayload(
            Metadata: new CatalogMetadata(
                GeneratedAt: new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
                Version: "benchmark-v1",
                ItemCount: itemCount),
            Items: items);
    }

    private static readonly string[] Countries =
    [
        "IS",
        "DE",
        "US",
        "JP",
        "BR"
    ];

    public static FlatOrder[] CreateFlatOrders(int itemCount)
    {
        var items = new FlatOrder[itemCount];

        for (int i = 0; i < itemCount; i++)
        {
            items[i] = new FlatOrder(
                Id: i + 1,
                Sku: $"ORD-{i + 1:D6}",
                Customer: $"Customer {(i % 250) + 1}",
                Status: OrderStatuses[i % OrderStatuses.Length],
                Quantity: 1 + (i % 8),
                UnitPrice: Math.Round(19.95m + (i * 0.41m), 2),
                Currency: Currencies[i % Currencies.Length],
                FulfillmentCenter: $"FC-{(i % 7) + 1:D2}");
        }

        return items;
    }

    public static ConfigurationPayload CreateConfigurationPayload(int serviceCount)
    {
        var services = new ServiceDefinition[serviceCount];

        for (int i = 0; i < serviceCount; i++)
        {
            services[i] = new ServiceDefinition(
                Name: $"service-{i + 1:D3}",
                BaseUrl: $"https://api-{(i % 12) + 1}.example.internal",
                TimeoutSeconds: 10 + (i % 8),
                Retries: 1 + (i % 3),
                Enabled: i % 5 != 0,
                Tags:
                [
                    $"region-{i % 4}",
                    i % 2 == 0 ? "read" : "write",
                    i % 3 == 0 ? "critical" : "standard"
                ]);
        }

        return new ConfigurationPayload(
            Environment: "production",
            Version: "2026.04.01",
            Region: "eu-west-1",
            Features: new FeatureFlags(
                HybridEncoding: true,
                CompactTransport: true,
                ExpandedDiagnostics: false),
            Services: services);
    }

    private static readonly string[] OrderStatuses =
    [
        "new",
        "allocated",
        "packed",
        "shipped",
        "backorder"
    ];

    private static readonly string[] Currencies =
    [
        "ISK",
        "EUR",
        "USD"
    ];

    internal sealed record CatalogPayload(CatalogMetadata Metadata, CatalogItem[] Items);

    internal sealed record CatalogMetadata(DateTimeOffset GeneratedAt, string Version, int ItemCount);

    internal sealed record CatalogItem(
        int Id,
        string Sku,
        string Name,
        decimal Price,
        string[] Tags,
        Dimensions Dimensions,
        Inventory Inventory,
        Supplier Supplier);

    internal sealed record Dimensions(int Width, int Height, int Depth);

    internal sealed record Inventory(int InStock, int Reserved, int ReorderThreshold);

    internal sealed record Supplier(string Id, string Name, string Country);

    internal sealed record FlatOrder(
        int Id,
        string Sku,
        string Customer,
        string Status,
        int Quantity,
        decimal UnitPrice,
        string Currency,
        string FulfillmentCenter);

    internal sealed record ConfigurationPayload(
        string Environment,
        string Version,
        string Region,
        FeatureFlags Features,
        ServiceDefinition[] Services);

    internal sealed record FeatureFlags(
        bool HybridEncoding,
        bool CompactTransport,
        bool ExpandedDiagnostics);

    internal sealed record ServiceDefinition(
        string Name,
        string BaseUrl,
        int TimeoutSeconds,
        int Retries,
        bool Enabled,
        string[] Tags);
}
