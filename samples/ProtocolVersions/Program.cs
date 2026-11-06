using DevOp.Toon;
using DevOp.Toon.Core;

Product[] products =
[
    new() { Id = 1, Customer = new() { Name = "Ada", Country = "IS" }, Tags = ["a", "b"] },
    new() { Id = 2, Customer = new() { Name = "Bob", Country = "GB" }, Tags = [] }
];

foreach (var version in new[] { ToonProtocolVersion.V4_4, ToonProtocolVersion.CustomV4_4 })
{
    var text = ToonEncoder.Encode(products, new ToonEncodeOptions
    {
        ProtocolVersion = version,
        Indent = 2,
        IgnoreNullOrEmpty = false,
        ExcludeEmptyArrays = false
    });
    var decoded = ToonDecoder.Decode<Product[]>(text, new ToonDecodeOptions
    {
        ProtocolVersion = version,
        Indent = 2
    })!;
    if (decoded.Length != 2 || decoded[0].Customer.Name != "Ada" ||
        !decoded[0].Tags.SequenceEqual(["a", "b"]) || decoded[1].Tags.Length != 0)
        throw new InvalidOperationException("The complete nested model was not preserved.");
    Console.WriteLine($"{ToonProtocolVersions.Format(version)}: nested DTOs and arrays preserved");
    Console.WriteLine(text);
}

const string ambiguousBody = "# producer comment\nId: 3";
if (!ToonDecoder.DetectVersion(ambiguousBody).RequiresVersionHint)
    throw new InvalidOperationException("Version-sensitive input must require a version hint.");
const string versionHeader = "4.4";
if (!ToonProtocolVersions.TryParse(versionHeader, out var bodyVersion))
    throw new InvalidOperationException("Invalid message version header.");
var explicitResult = ToonDecoder.Decode<Product>(ambiguousBody, new ToonDecodeOptions
{
    ProtocolVersion = bodyVersion,
    Indent = 2
})!;
if (explicitResult.Id != 3) throw new InvalidOperationException("Explicit body version was not applied.");
Console.WriteLine("Ambiguous comment syntax decoded using the supplied 4.4 body version.");

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
