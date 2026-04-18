# DevOp.Toon

`DevOp.Toon` is the .NET runtime for working with TOON, a compact structured format designed to reduce payload size without forcing teams to abandon familiar object models.

It is built for teams that care about transport efficiency, storage footprint, and predictable application integration. You can use it as a simple serializer, a reusable DI service, or a bridge between JSON and TOON while you adopt the format incrementally.

## Why teams look at TOON

- Payloads can be materially smaller than JSON on repeated-row and nested business data.
- Savings often remain even when HTTP compression is already enabled.
- The runtime integrates naturally into modern .NET applications.

## What this package includes

- `ToonEncoder` for writing TOON
- `ToonDecoder` for reading TOON
- `ToonService` and `IToonService` for application-level use
- `AddToon(...)` for dependency injection
- JSON-to-TOON and TOON-to-JSON conversion paths

## Best fit workloads

This package is strongest when payloads contain repeated object rows, nested collections, or catalog-style business data, for example:

- product catalogs
- warehouse snapshots
- audit histories
- configuration payloads
- batched API responses

## Recommended path through this space

If you are evaluating the package for the first time, read these pages in order:

1. `Why TOON`
2. `Installation and Package Selection`
3. `Quickstart`
4. `Using ToonService and DI`
5. `Performance and Benchmarks`

**For engineering readers:** protocol notes, API reference, build configuration, and contributor guidance are included later in the tree so the space stays approachable for new users.
