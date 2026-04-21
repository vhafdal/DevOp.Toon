# DevOp.Toon

`DevOp.Toon` is the .NET runtime package for working with TOON, a token-efficient alternative to JSON designed for compact structured payloads.

It provides encoding, decoding, JSON interop, and dependency injection support for applications that want a smaller wire format without giving up a familiar object model in .NET.

## Why TOON Instead of JSON

TOON is worth evaluating when JSON payload size matters: API transport, storage, synchronization, or LLM-oriented data exchange.

Current benchmark highlights from this repository show:

- `Products` payloads about `47.98%` smaller than JSON
- `Warehouses` payloads about `68.11%` smaller than JSON
- `Flat orders` payloads about `63.50%` smaller than JSON
- Still smaller after compression, including `Flat orders` about `15.74%` smaller with gzip
- Best current end-to-end runtime story on `net10.0`, including warmed API runs saving about `6.175-8.922 ms/request`

The practical value is straightforward: less payload to send, less payload to store, and a format that keeps delivering size wins even when HTTP compression is already enabled.

For the benchmark source material, see:

- `Documentation/BenchmarkCustomerOverview.md`
- `Documentation/BenchmarkHighlights.md`
- `Documentation/BenchmarkReport.md`

## Features

- Encode CLR objects and collections to TOON
- Decode TOON payloads back into `ToonNode` or strongly typed .NET models
- Convert JSON to TOON and TOON back to JSON
- Register a reusable `IToonService` through Microsoft dependency injection
- Configure encoding and decoding behavior with `ToonEncodeOptions`, `ToonDecodeOptions`, and `ToonServiceOptions`
- Reuse shared TOON contracts from `DevOp.Toon.Core`

## Installation

```bash
dotnet add package DevOp.Toon
```

## Basic Usage

```csharp
using DevOp.Toon;

var payload = new
{
    Id = 42,
    Name = "Widget",
    Tags = new[] { "sale", "featured" }
};

var toon = ToonEncoder.Encode(payload);
var decoded = ToonDecoder.Decode<dynamic>(toon);
```

## Dependency Injection

`DevOp.Toon` includes `AddToon(...)` for registering `IToonService` as a singleton with shared encode/decode defaults.

```csharp
using Microsoft.Extensions.DependencyInjection;
using DevOp.Toon.Core;
using DevOp.Toon;

var services = new ServiceCollection();

services.AddToon(options =>
{
    options.Indent = 1;
    options.Delimiter = ToonDelimiter.COMMA;
    options.KeyFolding = ToonKeyFolding.Off;
    options.Strict = true;
    options.ExpandPaths = ToonPathExpansion.Safe;
});

var provider = services.BuildServiceProvider();
var toon = provider.GetRequiredService<IToonService>();

var payload = new
{
    Id = 42,
    Name = "Widget"
};

string encoded = toon.Encode(payload);
string json = toon.Toon2Json(encoded);
```

This is the recommended integration point when TOON should be a reusable application service instead of a static helper call.

## JSON Interop

If you are incrementally adopting TOON, the package can bridge both formats directly:

```csharp
using DevOp.Toon;

var service = new ToonService();

string toon = service.Json2Toon("""{"id":42,"name":"Widget"}""");
string json = service.Toon2Json(toon);
```

## Package Notes

`DevOp.Toon` depends on `DevOp.Toon.Core` and includes the runtime surface:

- `ToonEncoder`
- `ToonDecoder`
- `IToonService`
- DI registration with `AddToon(...)`
- node graph types and runtime exceptions

If you only need shared TOON contracts in DTO or model libraries, use `DevOp.Toon.Core` directly.
