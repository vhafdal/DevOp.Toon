# DevOp.Toon

**Version scope:** this guide covers runtime 0.3.0. It includes a [runnable version-selection sample](https://github.com/vhafdal/DevOp.Toon/tree/main/samples/ProtocolVersions), covering official 4.4, custom hybrid rows, typed round trips, and explicit decode hints. Run it from the source tree.

**TOON serialization for .NET, with a columnar format for nested business data and companion packages for API servers and HTTP clients.**

Encode CLR objects, decode directly into supported DTO shapes, or work with a `ToonNode` graph. Use the runtime on its own or configure `IToonService` once through dependency injection.

## Supported TOON protocol versions

Package versions and protocol versions are separate: **DevOp.Toon 0.3.0** supports the following
wire formats through `ToonProtocolVersion` from **DevOp.Toon.Core 0.2.0**. API **0.2.0** and
Client **0.2.0** expose the same runtime choices over HTTP.

| TOON protocol/profile | Enum | Encode | Decode | HTTP version value |
| --- | --- | --- | --- | --- |
| Official 3.0 | `V3_0` | Yes | Yes | `3.0` |
| Official 4.0 | `V4_0` | Yes | Yes | `4.0` |
| Official 4.1 | `V4_1` | Yes | Yes | `4.1` |
| Official 4.2 | `V4_2` | Yes | Yes | `4.2` |
| Official 4.3 | `V4_3` | Yes | Yes | `4.3` |
| Official 4.4 | `V4_4` | **Default** | Yes | `4.4` |
| Legacy custom columnar | `CustomLegacy` | Yes | Yes | `custom-legacy` |
| Custom 4.4 grouped rows and spills | `CustomV4_4` | Yes | Yes | `custom-4.4` |
| Automatic syntax detection | `Auto` | No | **Default** | Decode option only |

This specifies official **3.0**, not every 3.x revision. Custom profiles require compatible readers.
For ambiguous input, supply a concrete decode version. When supplying decode options, match the
producer's indentation or use `ToonDecoder.DetectOptions(text, versionHint)`.

```csharp
using DevOp.Toon;
using DevOp.Toon.Core;

var text = ToonEncoder.Encode(new { Id = 42 }, new ToonEncodeOptions
{
    ProtocolVersion = ToonProtocolVersion.V4_4,
    Indent = 2
});
var value = ToonDecoder.Decode(text, new ToonDecodeOptions
{
    ProtocolVersion = ToonProtocolVersion.V4_4,
    Indent = 2
});
```

## Supported .NET targets

| Package | Target frameworks |
| --- | --- |
| Runtime, Core, Client | `netstandard2.0`, `net8.0`, `net9.0`, `net10.0` |
| API | `net8.0`, `net10.0` |

## Why choose this package?

See [official vs custom 4.4: benefits and usage](https://wiki.devop.is/devop-toon/features/custom-4-4)
for the same nested data in both formats, measured payload/memory tradeoffs, and complete configuration
examples. The [runnable sample](https://github.com/vhafdal/DevOp.Toon/tree/main/samples/CustomV44)
uses the published runtime, API and Client packages.

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

**Version selection in 0.3.0:** encoding defaults to official 4.4. Supply `ToonEncodeOptions.ProtocolVersion` with a concrete `ToonProtocolVersion`; decoding defaults to `Auto` and requires an explicit `ToonDecodeOptions.ProtocolVersion` for ambiguous version-dependent input. Select `CustomLegacy` for the earlier custom wire profile or `CustomV4_4` for nested header groups plus row/keyed spills. Official profiles default both omission settings to false; custom profiles default them to true. Encoder options and runtime DI encoding default to one space; decode options default to two. The API integration aligns both at two spaces. See the [protocol version guide](https://github.com/vhafdal/DevOp.Toon/blob/main/Documentation/ProtocolVersions.md) for migration examples.

## Performance evidence

The **6 November 2026 published-package benchmark** uses all **2,051 product DTOs** on .NET
10.0.13, with 30 warmups and 50 samples. Compared with legacy custom output from 0.2.9,
custom 4.4 in 0.3.0 allocates **42.1% less memory during decoding** and produces **3.9% fewer UTF-8 bytes**;
it decodes **17.0% slower in that isolated pair**. The old encoder's existing numeric
precision defect and intentional compact omissions are reported separately.

See [current benchmark results and methodology](https://wiki.devop.is/devop-toon/performance-benchmarks)
and [published-package measurements](https://github.com/vhafdal/DevOp.Toon/blob/main/Documentation/PublishedPackageBenchmarks.md)
for profile-specific CPU, allocation, Auto-detection and payload-size tables. Historical April
2026 JSON comparisons remain dated separately. Measure your own data with equivalent omission settings.

DevOp.Toon combines typed decoding, JSON interop, broad .NET compatibility, custom hybrid layouts, and integration packages spanning contracts, MVC formatters, and typed HTTP clients.

- [Documentation and package selection](https://wiki.devop.is/devop-toon/home)
- [Protocol and custom layout](https://wiki.devop.is/devop-toon/features/toon-format-protocol)
- [Benchmark methodology and results](https://wiki.devop.is/devop-toon/performance-benchmarks)
- [Source and reproducible benchmark harness](https://github.com/vhafdal/DevOp.Toon)
