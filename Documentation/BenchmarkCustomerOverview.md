# DevOp Toon Benchmark Overview

This page is the customer-facing benchmark summary for `DevOp.Toon`.

It is designed for product conversations, stakeholder reviews, landing-page copy, and customer evaluation discussions where the goal is to explain the value of `DevOp Toon` clearly without requiring readers to parse the full engineering benchmark report.

For the deeper engineering breakdown, see:
- [BenchmarkHighlights.md](/home/valdi/Projects/DevOp.Toon/Documentation/BenchmarkHighlights.md)
- [BenchmarkReport.md](/home/valdi/Projects/DevOp.Toon/Documentation/BenchmarkReport.md)
- [BenchmarkWorkflow.md](/home/valdi/Projects/DevOp.Toon/Documentation/BenchmarkWorkflow.md)

## Executive Summary

`DevOp Toon` is a compact structured data format that consistently reduces payload size compared with JSON and, on the strongest runtime targets, can also improve transport and serialization performance.

Across the benchmark datasets currently tracked in this repository, the biggest wins show up when payloads contain:
- repeated object rows
- nested arrays
- repetitive change history
- warehouse or catalog-style collections

In practical terms, that means `DevOp Toon` is strongest when you need to move or store large, structured payloads efficiently.

## Key Takeaways

### 1. Payloads are materially smaller than JSON

Current benchmark results show:

- `Products` payloads are about `47.98%` smaller than JSON
- `Warehouses` payloads are about `68.11%` smaller than JSON
- `Flat orders` payloads are about `63.50%` smaller than JSON
- `ChangeFields` payloads are about `41.17%` smaller than JSON
- `Configuration` payloads are about `39.48%` smaller than JSON

These are meaningful reductions, not marginal gains.

### 2. Size wins continue even with HTTP compression

The current report also shows savings after gzip and brotli compression:

- `Products` remains about `12.08%` smaller than JSON with gzip
- `Flat orders` remains about `15.74%` smaller with gzip
- `Warehouses` remains about `13.46%` smaller with brotli
- `Catalog` remains about `11.75%` smaller with brotli

That matters because many teams already rely on compression in transit. `DevOp Toon` still reduces the amount of data that needs to be transmitted.

### 3. The best overall runtime story is currently on .NET 10

Fresh benchmark runs collected on April 14, 2026 showed the strongest overall balance on `net10.0`:

- product compact encode around `19.582 ms`
- compact stream encode around `7.249 ms`
- product public decode around `31.531 ms`
- product payload size reduced from `2,878,419` JSON bytes to `1,253,844` compact TOON bytes

For teams already moving toward modern .NET runtimes, `net10.0` is the strongest current target for customer-visible performance messaging.

### 4. The compact DevOp Toon profile is the right default story

The library can be configured in different ways, but the strongest current customer-facing profile is:

- `ObjectArrayLayout = Columnar`
- `IgnoreNullOrEmpty = true`
- `ExcludeEmptyArrays = true`

This is the configuration referred to in this document as `DevOp Toon`.

On the real-world product dataset with `1000` products, `DevOp Toon` outperformed a more literal `Toon Native` profile on both runtime targets tested:

#### April 14, 2026 comparison: `DevOp Toon` vs `Toon Native`

| Runtime | Mode | Encode ms | Decode ms | Payload chars | Alloc encode | Alloc decode |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `net10.0` | Toon Native | 108.507 | 72.620 | 2,921,344 | 51,808.90 KB | 27,987.05 KB |
| `net10.0` | DevOp Toon | 23.706 | 30.421 | 1,252,297 | 14,651.77 KB | 6,246.10 KB |
| `net8.0` | Toon Native | 112.248 | 72.937 | 2,921,344 | 54,376.00 KB | 27,993.03 KB |
| `net8.0` | DevOp Toon | 24.555 | 16.622 | 1,252,297 | 14,651.78 KB | 6,246.10 KB |

Headline result:

- `DevOp Toon` produced a payload about `57.13%` smaller than `Toon Native`
- `DevOp Toon` used dramatically less memory
- `DevOp Toon` was multiple times faster on both encode and decode in this comparison

This is the clearest benchmark evidence in the current repository for why the `DevOp Toon` configuration should be the default position in customer discussions.

## Why These Results Matter

### Faster and lighter transport

Smaller payloads reduce:
- network transfer time
- bandwidth costs
- pressure on gateways and proxies
- compression workload
- storage footprint in logs, caches, and archived payloads

### Better fit for repeated business data

`DevOp Toon` performs especially well when objects repeat similar shapes across rows. Typical examples include:

- product catalogs
- inventory snapshots
- warehouse listings
- audit history
- configuration sets
- batched API responses

These are common enterprise payload patterns, which makes the benchmark story relevant to real workloads instead of only synthetic cases.

### Memory profile is strong in the compact mode

Fresh product benchmarks on April 14, 2026 showed:

#### Encode allocations

| Runtime | JSON | TOON readable | DevOp Toon compact |
| --- | ---: | ---: | ---: |
| `net10.0` | 13,818.88 KB | 15,195.07 KB | 13,446.13 KB |
| `net8.0` | 13,819.09 KB | 15,199.55 KB | 14,614.56 KB |

#### Decode allocations

| Runtime | JSON | DevOp Toon public decode |
| --- | ---: | ---: |
| `net10.0` | 19,800.92 KB | 6,493.56 KB |
| `net8.0` | 6,274.76 KB | 6,493.56 KB |

What that means:

- on `net10.0`, compact `DevOp Toon` is competitive with or better than JSON on encode memory and substantially better on decode memory
- on `net8.0`, encode memory is slightly above JSON, but still dramatically better than the less optimized `Toon Native` mode
- readable TOON is not the strongest memory story; compact `DevOp Toon` is

## Dataset Highlights

### Products

The `Products` dataset is the main real-world proof point in this repository.

- JSON bytes: `2,878,419`
- TOON bytes: `1,497,348` in the technical report
- compact DevOp Toon bytes from the fresh April 14 run: `1,253,844`
- payload reduction versus JSON: roughly `48%` in the report and stronger in the freshest compact profile

This is the dataset to use first in customer conversations because it combines:
- real-world complexity
- repeated row structure
- nested arrays
- measurable runtime and memory improvements

### Warehouses

`Warehouses` is the strongest nested-shape size example in the current benchmark set.

- raw size reduction: `68.11%`
- brotli reduction: `13.46%`
- fresh nested-shape runs still showed TOON encode and decode advantages over JSON on both `net8.0` and `net10.0`

### ChangeFields

`ChangeFields` is a strong example of repeated-row efficiency.

- raw size reduction: `41.17%`
- on `net10.0`, fresh nested-shape runs showed TOON encode at `0.648 ms` vs JSON `3.135 ms`
- on `net10.0`, fresh nested-shape runs showed TOON decode at `1.158 ms` vs JSON `3.170 ms`

This is a strong benchmark to show customers who deal with audit trails, revisions, or repeated field history.

## Recommended Customer Messaging

The strongest concise message today is:

> `DevOp Toon` significantly reduces structured payload size compared with JSON, keeps those wins even after compression, and delivers its best current performance profile on modern .NET runtimes such as .NET 10.

Alternative positioning for customer conversations:

- `DevOp Toon` is designed for large structured payloads where repeated rows and nested collections make JSON inefficient.
- Customers evaluating transport, storage, or synchronization efficiency should expect the biggest gains on catalog, inventory, and history-style payloads.
- The compact `DevOp Toon` profile is the recommended production configuration because it currently provides the strongest balance of size, speed, and memory usage.

## Important Framing

These benchmarks are strong evidence, but they should still be presented honestly:

- not every payload shape is faster than JSON in every runtime and every path
- the biggest wins come from repeated-row and nested collection workloads
- the compact `DevOp Toon` profile is the optimized profile to highlight, not the more literal `Toon Native` mode

That framing is still customer-friendly because the core story remains strong:

- materially smaller payloads
- continued savings after compression
- strong memory behavior in the optimized profile
- excellent fit for real structured business data

## Source and Date

This page reflects:

- the checked-in benchmark report dated `2026-04-09`
- fresh product and nested-shape benchmark runs collected on `2026-04-14`
- fresh `Toon Native` vs `DevOp Toon` comparison runs collected on `2026-04-14`

If you refresh benchmarks later, update this document so customer-facing claims stay aligned with the latest measured results.
