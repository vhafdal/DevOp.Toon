# Quickstart

This page shows the fastest path to using `DevOp.Toon` in a real .NET application.

## Encode an object

```csharp
using DevOp.Toon;

var payload = new
{
    Id = 42,
    Name = "Widget",
    Tags = new[] { "sale", "featured" }
};

string toon = ToonEncoder.Encode(payload);
```

## Decode into a typed model

```csharp
public sealed class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string[]? Tags { get; set; }
}

Product? product = ToonDecoder.Decode<Product>(toon);
```

## Work with the TOON node graph

```csharp
ToonNode? node = ToonDecoder.Decode(toon);

string? name = node?["Name"]?.GetValue<string>();
```

## Convert JSON to TOON

```csharp
string toonPayload = ToonEncoder.Json2Toon("""
{
  "id": 42,
  "name": "Widget"
}
""");
```

## Convert TOON back to JSON

```csharp
string jsonPayload = ToonDecoder.Toon2Json(toonPayload);
```

## Use custom encode options

```csharp
using DevOp.Toon.Core;
using DevOp.Toon;

var options = new ToonEncodeOptions
{
    Indent = 2,
    Delimiter = ToonDelimiter.COMMA,
    KeyFolding = ToonKeyFolding.Safe,
    ObjectArrayLayout = ToonObjectArrayLayout.Columnar,
    IgnoreNullOrEmpty = true,
    ExcludeEmptyArrays = true
};

string compact = ToonEncoder.Encode(payload, options);
```

## Recommended next step

If TOON should be a reusable application service instead of a one-off helper call, continue with `Using ToonService and DI`.
