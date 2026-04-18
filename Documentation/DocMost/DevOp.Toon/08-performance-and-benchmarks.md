# Performance and Benchmarks

This page is the top-level benchmark summary for `DevOp.Toon`. It is designed for evaluation conversations, architecture review, and product positioning.

## Headline results

Current benchmark material in this repository shows:

- `Products` payloads about `47.98%` smaller than JSON
- `Warehouses` payloads about `68.11%` smaller than JSON
- `Flat orders` payloads about `63.50%` smaller than JSON
- continued savings after compression, including `Products` about `12.08%` smaller with gzip

## Why these numbers matter

Smaller payloads reduce:

- network transfer time
- bandwidth cost
- compression workload
- log and cache storage footprint

The format is strongest when data has repeated shape across rows or nested collections, which is why product, warehouse, and history-style datasets are strong proof points.

## Runtime positioning

The strongest current runtime story is on `.NET 10`.

Fresh benchmark material referenced in this repository shows:

- compact product encode around `19.582 ms`
- compact stream encode around `7.249 ms`
- product public decode around `31.531 ms`

`.NET 8` remains a credible practical target, while `netstandard2.0` remains valuable for compatibility.

## Recommended production profile

The strongest current customer-facing profile is:

- `ObjectArrayLayout = Columnar`
- `IgnoreNullOrEmpty = true`
- `ExcludeEmptyArrays = true`

This is the profile that best expresses the DevOp TOON value story in the current benchmark set.

## Honest framing

These results are strong, but they should be positioned responsibly:

- not every payload is faster than JSON in every path
- the biggest wins come from repeated-row and nested collection workloads
- the compact columnar profile is the best default story, not every possible encoding mode

## Source material

Use the engineering benchmark pages in this repository when you need the detailed tables or collection workflow:

- `Documentation/BenchmarkCustomerOverview.md`
- `Documentation/BenchmarkHighlights.md`
- `Documentation/BenchmarkReport.md`
- `Documentation/BenchmarkWorkflow.md`
