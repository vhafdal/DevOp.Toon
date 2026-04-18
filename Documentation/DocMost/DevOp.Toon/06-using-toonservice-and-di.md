# Using ToonService and DI

`ToonService` is the recommended integration point when TOON should be configured once and reused throughout an application.

## Register the service

```csharp
using Microsoft.Extensions.DependencyInjection;
using DevOp.Toon.Core;
using DevOp.Toon;

var services = new ServiceCollection();

services.AddToon(options =>
{
    options.Indent = 2;
    options.Delimiter = ToonDelimiter.COMMA;
    options.KeyFolding = ToonKeyFolding.Safe;
    options.ObjectArrayLayout = ToonObjectArrayLayout.Columnar;
    options.Strict = true;
    options.ExpandPaths = ToonPathExpansion.Off;
});
```

`AddToon(...)` registers:

- `ToonServiceOptions` as a singleton
- `IToonService` backed by `ToonService`

## Use the service

```csharp
var provider = services.BuildServiceProvider();
var toon = provider.GetRequiredService<IToonService>();

string encoded = toon.Encode(new { Id = 42, Name = "Widget" });
string json = toon.Toon2Json(encoded);
```

## When to prefer `IToonService`

Use the service abstraction when you want:

- one place to define encode and decode defaults
- dependency injection across an application
- easy testing and replacement of the TOON integration boundary
- synchronous and asynchronous APIs behind one interface

## Service options overview

`ToonServiceOptions` combines encode and decode configuration:

- `Indent`
- `Delimiter`
- `KeyFolding`
- `FlattenDepth`
- `ObjectArrayLayout`
- `Strict`
- `ExpandPaths`

**Recommended default story:** for customer-facing and production-oriented examples, the strongest current profile is columnar object arrays with null and empty-array reduction enabled on the encode side.
