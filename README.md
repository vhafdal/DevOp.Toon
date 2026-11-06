# DevOp.Toon

**Version scope:** this guide covers runtime 0.3.0. It includes a [runnable version-selection sample](https://github.com/vhafdal/DevOp.Toon/tree/main/samples/ProtocolVersions), covering official 4.4, custom hybrid rows, typed round trips, and explicit decode hints. Run it from the source tree.

**TOON serialization for .NET, with a columnar format for nested business data and companion packages for API servers and HTTP clients.**

Encode CLR objects, decode directly into supported DTO shapes, or work with a `ToonNode` graph. Use the runtime on its own or configure `IToonService` once through dependency injection.

## Why choose this package?

- **Keep repeated rows compact even when they contain collections.** The custom columnar layout shares scalar field names in a table header and writes complex fields beneath their row. Product catalogs, warehouse snapshots, and change histories can retain a compact scalar core without flattening away their nested data.
- **Use your .NET models.** Supported typed paths materialize DTOs, positional records, arrays, collection interfaces, and string-keyed dictionaries without converting through a JSON string. Other supported shapes use the native-node materializer.
- **Choose the integration layer you need.** Keep model projects lightweight with the contracts package, add serialization to services, register MVC formatters at the API boundary, and use the typed HTTP client in callers.
- **Evaluate CPU, memory, and payload size together.** The implementation uses pooled output buffers, cached CLR metadata, and source-backed line ranges. These techniques reduce avoidable work; they are not a claim that every payload is faster or allocates less than another serializer.

TOON benefits depend on data shape. This package is worth evaluating when repeated keys, nested collections, or large response bodies matter to your application.

## Install

```bash
dotnet add package DevOp.Toon
```

Runtime targets: `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`.

## Encode and decode typed data

```csharp
using DevOp.Toon;

var source = new[]
{
    new Product(42, "Widget", new[] { "sale", "featured" }),
    new Product(43, "Gadget", new[] { "new" })
};

string text = ToonEncoder.Encode(source);
Product[] products = ToonDecoder.Decode<Product[]>(text)!;

public sealed record Product(int Id, string Name, string[] Tags);
```

The same runtime also reads and writes `ToonNode` graphs, converts JSON using `Json2Toon` / `Toon2Json`, and exposes byte, stream, and async operations. Async methods are convenience APIs; they do not imply incremental, bounded-memory parsing.

## Configure once with dependency injection

```csharp
using DevOp.Toon;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddToon(options =>
{
    options.Indent = 1;
    options.Strict = true;
    options.Encode.IgnoreNullOrEmpty = false;
    options.Encode.ExcludeEmptyArrays = false;
});

using var provider = services.BuildServiceProvider();
var serializer = provider.GetRequiredService<IToonService>();
string text = serializer.Encode(new { Id = 42, Name = "Widget" });
string json = serializer.Toon2Json(text);
```

## The package family

| Package | Add it to | Benefit |
| --- | --- | --- |
| [DevOp.Toon.Core](https://www.nuget.org/packages/DevOp.Toon.Core) | Shared models and contracts | Wire-name attributes and shared enums without the serialization runtime |
| [DevOp.Toon](https://www.nuget.org/packages/DevOp.Toon) | Applications and services | Encoding, typed/DOM decoding, JSON interop, and DI |
| [DevOp.Toon.API](https://www.nuget.org/packages/DevOp.Toon.API) | ASP.NET Core MVC servers | TOON input/output formatters, content negotiation, and per-request response options |
| [DevOp.Toon.Client](https://www.nuget.org/packages/DevOp.Toon.Client) | .NET HTTP callers | TOON request encoding, TOON/JSON response handling, compression configuration, and structured errors |

## Interoperability and data preservation

The custom columnar layout is an extension: row spill fields are not official TOON syntax. Use an official protocol mode when exchanging documents with other TOON implementations. Both ends must agree when using a custom format.

Omission settings trade data preservation for compactness. `IgnoreNullOrEmpty` can remove all-null/all-empty-string columns; `ExcludeEmptyArrays` can remove empty array properties. Set both to `false` when those distinctions must be retained. Binary-array formatting is another explicit policy choice; choose numeric-array output when a JSON-like array shape is required.

**Version selection in 0.3.0:** encoding defaults to official 4.4. Supply `ToonEncodeOptions.ProtocolVersion` with a concrete `ToonProtocolVersion`; decoding defaults to `Auto` and requires an explicit `ToonDecodeOptions.ProtocolVersion` for ambiguous version-dependent input. Select `CustomLegacy` for the earlier custom wire profile or `CustomV4_4` for nested header groups plus row/keyed spills. Official profiles default both omission settings to false; custom profiles default them to true. Constructor and runtime DI indentation default to one space; the API integration uses two. See the [protocol version guide](https://github.com/vhafdal/DevOp.Toon/blob/main/Documentation/ProtocolVersions.md) for migration examples.

## Performance evidence

Measure your own payloads with equivalent omission settings. The repository includes product and nested-shape benchmarks recording elapsed time, allocations, raw bytes, gzip, and Brotli. Historical results compare this implementation with `System.Text.Json` for the recorded profiles and datasets; they do not validate the protocol upgrade.

DevOp.Toon combines typed decoding, JSON interop, broad .NET compatibility, custom hybrid layouts, and integration packages spanning contracts, MVC formatters, and typed HTTP clients.

- [Documentation and package selection](https://wiki.devop.is/devop-toon/home)
- [Protocol and custom layout](https://wiki.devop.is/devop-toon/features/toon-format-protocol)
- [Benchmark methodology and results](https://wiki.devop.is/devop-toon/performance-benchmarks)
- [Source and reproducible benchmark harness](https://github.com/vhafdal/DevOp.Toon)
