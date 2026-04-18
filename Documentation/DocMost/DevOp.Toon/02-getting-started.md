# Getting Started

This page is the shortest path from evaluation to a working `DevOp.Toon` integration.

## 1. Install the package

```bash
dotnet add package DevOp.Toon
```

If you only need shared TOON enums and attributes in DTO projects, use `DevOp.Toon.Core` instead. Use `DevOp.Toon` when you need actual runtime behavior such as encoding, decoding, JSON interop, or DI registration.

## 2. Encode an object

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

## 3. Decode back to a .NET type

```csharp
using DevOp.Toon;

var item = ToonDecoder.Decode<Item>(toon);
```

## 4. Use the service in applications

```csharp
using Microsoft.Extensions.DependencyInjection;
using DevOp.Toon;

var services = new ServiceCollection();
services.AddToon();

var provider = services.BuildServiceProvider();
var toon = provider.GetRequiredService<IToonService>();
```

## 5. Bridge JSON while you adopt TOON

```csharp
var service = new ToonService();

string toonPayload = service.Json2Toon("""{"id":42,"name":"Widget"}""");
string jsonPayload = service.Toon2Json(toonPayload);
```

## Recommended next pages

- Read `Why TOON` for the value story.
- Read `Quickstart` for fuller examples.
- Read `Using ToonService and DI` if you want application-wide defaults.
