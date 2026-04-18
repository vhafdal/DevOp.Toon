# Benchmark Collection and Sharing Workflow

This document explains how `DevOp.Toon` benchmark results should be collected, validated, recorded, and shared.

Related docs:
- [BenchmarkReport.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkReport.md)
- [BenchmarkHighlights.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkHighlights.md)

Use this workflow whenever:
- encoder performance changes
- decoder performance changes
- payload size or compression characteristics change
- benchmark datasets or benchmark commands change
- framework-target benchmark claims need to be refreshed

## Goals

The benchmark workflow is intended to answer five things clearly:

1. Is TOON smaller than JSON for the target payloads?
2. Is TOON competitive on encode and decode performance?
3. How strong are the current serializer and payload-shape results?
4. Which payload shapes benefit most from TOON?
5. Are the reported gains stable enough to share?

## Benchmark Types

The repository currently uses several benchmark modes.

### 1. Product Encode Profile

Use this to compare serializer cost and output size for the real-world product dataset.

```bash
dotnet run -c Release --framework net8.0 --project benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj -- --profile-products-encode --count 1000 --iterations 8
```

Track:
- JSON serialize time
- TOON readable encode time
- TOON compact encode time
- JSON stream serialize time
- TOON compact stream time
- output bytes

### 2. Nested Shape Profile

Use this to find current hotspots in nested arrays and nested objects.

```bash
dotnet run -c Release --framework net8.0 --project benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj -- --profile-products-nested-shapes --count 1000 --iterations 8
```

Track:
- `Warehouses`
- `Changes`
- `ChangeFields`
- other nested subtrees that are reported

This is a diagnosis tool, not the main public benchmark.

### 3. Product Layout Comparison Profile

Use this to compare the two main TOON encoder policy bundles on the real product dataset:

- `Toon Native`: `ObjectArrayLayout = Auto`, `IgnoreNullOrEmpty = false`, `ExcludeEmptyArrays = false`
- `DevOp Toon`: `ObjectArrayLayout = Columnar`, `IgnoreNullOrEmpty = true`, `ExcludeEmptyArrays = true`

```bash
dotnet run -c Release --framework net10.0 --project benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj -- --profile-products-layout-compare --count 1000 --iterations 8
```

Track:
- encode time
- decode time
- output size / char count
- whether columnar + omission policy is still paying for itself

### 4. Benchmark Report Export

Use this to generate a publishable markdown report across multiple payload shapes.

```bash
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --export-benchmark-report --count 1000 --iterations 8 --output Documentation/BenchmarkReport.md
```

This generates:
- [BenchmarkReport.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkReport.md)

Use that file as the source for developer-facing benchmark communication.

### 5. Framework Matrix Refresh

Use this when benchmark claims need to compare `netstandard2.0`, `net8.0`, and `net10.0`.

The benchmark host now supports selecting the core library target separately from the host runtime:

- `netstandard2.0` library target is executed on a `net8.0` benchmark host
- `net8.0` library target is executed on a `net8.0` benchmark host
- `net10.0` library target is executed on a `net10.0` benchmark host

The current benchmark host exports serializer and payload-shape results only. Do not invent an API row for any framework target unless an API benchmark harness is reintroduced to the repo.

## When to Collect Results

Collect fresh benchmark results when:
- a serializer hot path changes
- nested array or object handling changes
- decoder materialization changes
- benchmark payload definitions change
- README or chat-ready benchmark claims need to be refreshed

Do not update published benchmark claims from stale runs.

## How to Collect Results

### Standard collection sequence

Run the following in order:

```bash
dotnet build -c Release benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj
dotnet run -c Release --framework net8.0 --project benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj -- --profile-products-encode --count 1000 --iterations 8
dotnet run -c Release --framework net8.0 --project benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj -- --profile-products-nested-shapes --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --export-benchmark-report --count 1000 --iterations 8 --output Documentation/BenchmarkReport.md
```

### Framework matrix sequence

Use this sequence when updating framework-target claims:

```bash
dotnet build -c Release -f net8.0 -p:BenchmarkLibraryTargetFramework=netstandard2.0 benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --profile-products-encode --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --profile-products-nested-shapes --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --export-benchmark-report --count 1000 --iterations 8 --output /tmp/netstandard2.0-report.md

dotnet build -c Release -f net8.0 -p:BenchmarkLibraryTargetFramework=net8.0 benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --profile-products-encode --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --profile-products-nested-shapes --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net8.0/DevOp.Toon.Benchmarks.dll --export-benchmark-report --count 1000 --iterations 8 --output /tmp/net8.0-report.md

dotnet build -c Release -f net10.0 -p:BenchmarkLibraryTargetFramework=net10.0 benchmarks/DevOp.Toon.Benchmarks/DevOp.Toon.Benchmarks.csproj
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net10.0/DevOp.Toon.Benchmarks.dll --profile-products-encode --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net10.0/DevOp.Toon.Benchmarks.dll --profile-products-nested-shapes --count 1000 --iterations 8
dotnet benchmarks/DevOp.Toon.Benchmarks/bin/Release/net10.0/DevOp.Toon.Benchmarks.dll --export-benchmark-report --count 1000 --iterations 8 --output /tmp/net10.0-report.md
```

### Stability rule

Do not publish a new benchmark claim from a single lucky run.

If results vary heavily:
- report the approximate band
- avoid claiming a hard win that is not repeatable

## What to Publish

For public-facing benchmark messaging, prefer:
- raw size reduction
- gzip reduction
- brotli reduction
- compact encode comparison
- notable wins on real-world nested payloads

Avoid overclaiming from:
- a single benchmark run
- only synthetic payloads
- only one direct serializer path measurement

## How to Judge Whether a Result Is Good Enough to Publish

Publish when at least one of these is true:
- raw size reduction improved materially
- compression results improved materially
- compact encode improved materially
- a benchmark hotspot was removed and the public benchmark story is clearer

Do not publish when:
- the result depends on unstable run-to-run variance
- only the benchmark harness changed and the real serializer story did not

## Files to Update

When benchmark claims change, update:

1. Repository markdown:
   - [README.md](/home/valdi/Projects/toon-dotnet/README.md)
   - [BenchmarkReport.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkReport.md)
   - [BenchmarkHighlights.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkHighlights.md)

2. Chat-ready markdown output:
   - a concise benchmark summary
   - framework winners and caveats
   - any manual follow-up notes for Wiki.js or other channels

## Recommended Public Messaging

Good benchmark messaging looks like:
- "TOON reduces raw payload size by X% on this dataset."
- "TOON remains smaller than JSON even after gzip and brotli."
- "TOON compact encode is competitive with or faster than JSON for these payload shapes."

Bad benchmark messaging looks like:
- "TOON is always faster than JSON."
- "TOON won once, therefore it is better."
- "The direct benchmark path improved, therefore real API latency improved."

## Notes for This Repository

The most important current payloads to watch are:
- `Products`
- `Warehouses`
- `Changes`
- `ChangeFields`

The most important framework rows to watch are:
- `net10.0` for the best current serializer story
- `net8.0` for current deployment compatibility
- `netstandard2.0` for the broadest library compatibility

In practice:
- `Products` is the main public proof point
- `Warehouses` is the main nested hotspot diagnostic
- `ChangeFields` is a good example of compact repeated-row wins

## Summary

Use the benchmark workflow to produce repeatable evidence, not just interesting numbers.

The source of truth should be:
- a fresh benchmark run
- a generated markdown report
- updated README claims
- a chat-ready markdown summary

If any of those are stale, the benchmark story is incomplete.
