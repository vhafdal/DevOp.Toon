using System.Text.Json;
using DevOp.Toon;
using DevOp.Toon.API;
using DevOp.Toon.Client;
using DevOp.Toon.Core;
using Microsoft.Extensions.DependencyInjection;

Product[] products =
[
    new() { Id = 1, Customer = new() { Name = "Ada", Country = "IS" }, Tags = ["a", "b"] },
    new() { Id = 2, Customer = new() { Name = "Bob", Country = "GB" }, Tags = ["c"] }
];

foreach (var version in new[] { ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4 })
{
    var text = ToonEncoder.Encode(products, EncodeOptions(version));
    VerifyRoundTrip(products, text, version);
    Console.WriteLine($"--- {ToonProtocolVersions.Format(version)} rows ---");
    Console.WriteLine(text);
}

var uniform = products.Select(product => new { product.Id, product.Customer }).ToArray();
var officialUniform = ToonEncoder.Encode(uniform, EncodeOptions(ToonProtocolVersion.V4_4));
var customUniform = ToonEncoder.Encode(uniform, EncodeOptions(ToonProtocolVersion.CustomV4_4));
if (officialUniform != customUniform || !officialUniform.Contains("Customer{Name,Country}"))
    throw new InvalidOperationException("Both profiles must retain uniform nested header groups.");
Console.WriteLine("--- shared uniform nested groups ---");
Console.WriteLine(officialUniform);

var keyed = new Dictionary<string, Product> { ["sku-1"] = products[0], ["sku-2"] = products[1] };
var keyedText = ToonEncoder.Encode(keyed, EncodeOptions(ToonProtocolVersion.CustomV4_4));
VerifyRoundTrip(keyed, keyedText, ToonProtocolVersion.CustomV4_4);
Console.WriteLine("--- custom-4.4 keyed rows ---");
Console.WriteLine(keyedText);

var emptyValues = new EmptyValues { Name = "", Note = null, Tags = [] };
var emptyText = ToonEncoder.Encode(emptyValues, EncodeOptions(ToonProtocolVersion.CustomV4_4));
VerifyRoundTrip(emptyValues, emptyText, ToonProtocolVersion.CustomV4_4);

var services = new ServiceCollection();
services.AddControllers().AddToon(options =>
{
    options.Indent = 2;
    options.Encode.ProtocolVersion = ToonProtocolVersion.CustomV4_4;
    options.Encode.IgnoreNullOrEmpty = false;
    options.Encode.ExcludeEmptyArrays = false;
});
services.AddToonClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com/");
    options.EncodeOptions = EncodeOptions(ToonProtocolVersion.V4_4);
    options.ResponseEncodeOverrides = new ToonResponseEncodeOverrideOptions
    {
        ProtocolVersion = ToonProtocolVersion.CustomV4_4,
        Indent = 2,
        IgnoreNullOrEmpty = false,
        ExcludeEmptyArrays = false
    };
});

using var provider = services.BuildServiceProvider();
var service = provider.GetRequiredService<IToonService>();
var serviceText = service.Encode(products);
VerifyRoundTrip(products, serviceText, ToonProtocolVersion.CustomV4_4);
Console.WriteLine("PASS: all DTO values, nested arrays, dictionary keys, empty values and MVC service options preserved.");
Console.WriteLine("Client registration compiled; no HTTP request was sent.");

static ToonEncodeOptions EncodeOptions(ToonProtocolVersion version) => new()
{
    ProtocolVersion = version,
    Indent = 2,
    IgnoreNullOrEmpty = false,
    ExcludeEmptyArrays = false
};

static void VerifyRoundTrip<T>(T source, string text, ToonProtocolVersion version)
{
    var decoded = ToonDecoder.Decode<T>(text, new ToonDecodeOptions
    {
        ProtocolVersion = version,
        Indent = 2
    });
    if (JsonSerializer.Serialize(source) != JsonSerializer.Serialize(decoded))
        throw new InvalidOperationException($"Round trip changed the {typeof(T).Name} model.");
}

public sealed class Product
{
    public int Id { get; set; }
    public Customer Customer { get; set; } = new();
    public string[] Tags { get; set; } = [];
}

public sealed class Customer
{
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
}

public sealed class EmptyValues
{
    public string Name { get; set; } = "unset";
    public string? Note { get; set; } = "unset";
    public string[]? Tags { get; set; }
}
