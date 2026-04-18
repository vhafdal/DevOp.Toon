# DevOp.Toon Benchmark Highlights

This page is the short version of the benchmark story.

Use it when you need a simple developer-facing explanation of why `DevOp.Toon` is worth evaluating instead of JSON.

For the full technical breakdown, see:
- [BenchmarkCustomerOverview.md](/home/valdi/Projects/DevOp.Toon/Documentation/BenchmarkCustomerOverview.md)
- [BenchmarkReport.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkReport.md)
- [BenchmarkWorkflow.md](/home/valdi/Projects/toon-dotnet/Documentation/BenchmarkWorkflow.md)

## Headline Claims

### 1. TOON still cuts payload size dramatically

Across the current April 2026 framework matrix:
- `Products` stayed about `47.98%` smaller than JSON
- `Warehouses` stayed about `68.11%` smaller than JSON
- `Flat orders` stayed about `63.50%` smaller than JSON

Those size wins were stable across `netstandard2.0`, `net8.0`, and `net10.0` because the emitted compact payload stayed the same.

### 2. TOON still wins after compression

Current report highlights:
- `Flat orders` is about `15.74%` smaller than JSON with gzip
- `Products` is about `12.08%` smaller with gzip
- `Warehouses` is about `13.46%` smaller with brotli

That means TOON still provides transport savings even when HTTP compression is already enabled.

### 3. net10.0 is the strongest current runtime target

In the fresh matrix:
- `net10.0` products compact encode averaged `27.210 ms`
- `net10.0` compact stream encode averaged `11.904 ms`
- `net10.0` had the best `Products` report-export encode/decode combination in this refresh

If you want the best current serializer benchmark story from this fork, `net10.0` is the best place to start.

### 4. net8.0 is the strongest practical middle ground

The fresh `net8.0` runs showed:
- the same raw and compressed size wins as the other framework rows
- products compact encode at `27.044 ms`
- products compact stream encode at `12.158 ms`
- strong nested-shape wins, especially on `Warehouses`

That makes `net8.0` a credible default runtime row when compatibility matters more than squeezing out the best absolute serializer result.

### 5. netstandard2.0 remains viable for the core library

The `netstandard2.0` row was executed on a `net8.0` host and still showed:
- `Products` compact encode at `33.882 ms`
- compact stream encode at `20.302 ms`
- the same size and compression wins as the runtime-specific targets

That makes `netstandard2.0` a credible compatibility target for the core encoder and decoder.

## Recommended Positioning

The strongest honest message right now is:

> `DevOp.Toon` delivers large and stable payload-size wins over JSON across framework targets, remains compression-friendly, and currently shows its best serializer story on `net10.0`.

## Use This With the Technical Report

Use this file for:
- README summaries
- package landing page copy
- short chat summaries
- release notes

Use the technical report for:
- the framework matrix
- dataset-by-dataset details
- encode and decode measurements
- serializer evidence and caveats
