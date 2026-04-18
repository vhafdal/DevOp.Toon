# JSON Interop

You do not need to switch an application from JSON to TOON all at once. `DevOp.Toon` supports a practical migration path where both formats can coexist.

## Convert JSON to TOON

```csharp
using DevOp.Toon;

var service = new ToonService();

string toon = service.Json2Toon("""
{
  "id": 42,
  "name": "Widget",
  "tags": ["sale", "featured"]
}
""");
```

## Convert TOON to JSON

```csharp
string json = service.Toon2Json(toon);
```

## Why this matters

JSON interop is useful when:

- one side of a system still speaks JSON
- you want to benchmark or evaluate TOON without changing every consumer
- you are migrating incrementally rather than rewriting transport contracts in one step

## Practical migration patterns

### Evaluation mode

Start by generating TOON from existing JSON payloads and comparing:

- payload size
- compression results
- runtime behavior

### Boundary mode

Keep JSON internally for now, but introduce TOON at selected boundaries such as:

- storage pipelines
- batch exports
- internal service-to-service transport

### Full runtime mode

Move application services to `IToonService` and use JSON conversion only where interoperability still matters.

## Important note

The interop path makes adoption easier, but the biggest long-term gains usually come when TOON becomes a first-class transport or persistence format rather than a temporary conversion step.
