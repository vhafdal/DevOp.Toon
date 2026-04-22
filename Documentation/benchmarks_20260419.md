---
title: Benchmarks
description: DevOp.Toon serializes .NET data up to 77.7% smaller than JSON with 1.40× faster decode on real-world payloads.
---

# Benchmarks

DevOp.Toon is a .NET serialization library that writes data in TOON format — a structured alternative to JSON built for repeated-row and nested collection data. These benchmarks compare TOON against `System.Text.Json` across seven datasets, including a real 2,051-product file measured end-to-end.

All numbers below are medians over 15 warm iterations with `GC.Collect()` between runs, on .NET 10.0.6, Linux x64. The full methodology is at the bottom of this page.

## At a glance

| Stat | What it measures |
|---|---|
| **77.7%** smaller | Raw payload on a real 2,051-product file |
| **20.1%** smaller | Warehouses dataset after gzip |
| **2.02×** faster encode | Products dataset, 1,000 items |
| **1.40×** faster decode | Real-world 2,051-product file |

## Why TOON is smaller

TOON uses a columnar array layout for object arrays, which eliminates repeated key names across records. A thousand products encode the field names once, not a thousand times. Combined with `IgnoreNullOrEmpty` and `ExcludeEmptyArrays` in the default profile — which omit null and empty fields from the output — TOON typically produces 40–78% smaller payloads on structured business data.

## Payload size

Size reduction varies with data shape. Repeated rows and nested collections compress best; shallow payloads dominated by unique strings see modest gains. Compression narrows the gap but doesn't close it.

| Dataset | JSON bytes | TOON bytes | Raw saved | Gzip saved | Brotli saved |
|---|---:|---:|---:|---:|---:|
| Real-world products (2,051) | 11,520,246 | 2,568,353 | **77.7%** | 15.9% | 10.5% |
| Warehouses | 849,281 | 200,114 | **76.4%** | 20.1% | 20.0% |
| Flat Orders | 151,066 | 55,142 | **63.5%** | 15.7% | 7.7% |
| Products (1,000 items) | 2,878,419 | 1,253,844 | **56.4%** | 14.6% | 9.0% |
| Change Fields | 300,822 | 176,965 | **41.2%** | 7.4% | 4.0% |
| Configuration | 15,366 | 9,299 | **39.5%** | 4.4% | 5.2% |
| Catalog | 283,047 | 257,070 | **9.2%** | 6.9% | 11.7% |

On the real-world file that's 354 KB gzipped versus 422 KB for gzipped JSON — meaningful across millions of requests.

## Decode speed

Decode is where TOON most often beats JSON. Smaller payloads mean fewer tokens to parse.

| Dataset | JSON (ms) | TOON (ms) | Speedup |
|---|---:|---:|---:|
| Change Fields | 1.676 | 0.854 | **1.96×** |
| Real-world 2,051-product file | 26.1 | 18.6 | **1.40×** |
| Warehouses | 3.630 | 2.997 | **1.21×** |
| Products (1,000 items) | 35.626 | 35.049 | **1.02×** |

## Encode speed

Encode is scale-dependent. At 1,000+ rows of the same shape, TOON's columnar planning pays for itself — 2.02× faster on Products. On small, flat datasets the planning overhead exceeds the savings.

| Dataset | JSON (ms) | TOON (ms) | Speedup |
|---|---:|---:|---:|
| Products (1,000 items) | 36.550 | 18.050 | **2.02×** |
| Warehouses | 2.051 | 2.350 | 0.87× |
| Change Fields | 0.718 | 0.838 | 0.86× |
| Configuration | 0.059 | 0.068 | 0.87× |
| Flat Orders | 0.794 | 2.026 | 0.39× |
| Catalog | 1.957 | 5.202 | 0.38× |

If most of your writes are small, flat, one-off payloads, TOON encode is a wash or slower. If you ship collections, the arithmetic flips quickly.

## End-to-end API response

Same controller, same data, served as either JSON or TOON from an in-process ASP.NET Core test. Five warmup plus twelve measured iterations. Smaller payloads flush sooner, and that turns into real wall-clock time back to the client.

| Dataset | JSON (ms) | TOON (ms) | Delta | Raw saved |
|---|---:|---:|---:|---:|
| Catalog | 12.369 | 11.628 | **−0.741 ms** | 56.3% |
| Products | 11.396 | 10.827 | **−0.569 ms** | 56.3% |

## Best case: Warehouses

One dataset wins on every axis we measured. The Warehouses payload — a nested warehouse subtree with repetitive record structure — combines heavily repeated keys with deep nesting. That's exactly what TOON's columnar layout was built for.

| Metric | Result |
|---|---:|
| Raw payload | **76.4%** smaller than JSON |
| After gzip | **20.1%** smaller |
| After brotli | **20.0%** smaller |
| Encode allocations | **42%** less (0.58× JSON) |
| Decode speed | **1.21×** faster |

If your payloads look like this — nested collections of same-shape records — Warehouses is the benchmark to look at, not the averages.

## Real-world file

The strongest single data point: 2,051 actual product records, encoded once and measured end-to-end.

| | JSON | TOON | Saved |
|---|---:|---:|---:|
| Raw | 11.52 MB | 2.57 MB | **77.7%** |
| Gzipped | 422 KB | 355 KB | **15.9%** |
| Brotli | 275 KB | 246 KB | **10.5%** |
| Decode time | 26.1 ms | 18.6 ms | **1.40× faster** |

## When to use TOON

TOON shines on:

- Product catalogs, inventory lists, and other collections of same-shape records
- Audit logs and event streams
- Batch API responses
- Any payload where you're shipping lists — the more rows, the bigger the win

Stay on JSON when:

- Payloads are small, flat, and one-off — TOON encode planning adds overhead below roughly 1,000 rows of the same shape
- Your data is dominated by long unique strings with shallow nesting (see Catalog at 9.2%)
- You need maximum ecosystem compatibility with tooling that doesn't speak TOON yet

## Memory

On most datasets TOON allocates more transient heap during encode and decode than `System.Text.Json` — typically 1.1× to 2.6× — with Warehouses as the standout exception at 0.58× on encode. This is the trade-off for the columnar layout: shrinking bytes on the wire costs some CLR work to plan the column structure. If your bottleneck is payload size or network time, that trade is usually worth it. If it's GC pressure on a hot path, benchmark your specific shape.

## Methodology

- Timings are wall-clock averages over 15 warm iterations
- `GC.Collect()` is called between runs to isolate allocation measurements
- Heap allocations measured via `GC.GetAllocatedBytesForCurrentThread()`
- Gzip and Brotli use `CompressionLevel.SmallestSize`; under `Optimal` the gap narrows slightly
- All benchmarks run in-process on the same .NET runtime that would serve production traffic — no out-of-process overhead
- Source and reproducible benchmarks: [github.com/valdi-hafdal/DevOp.Toon](https://github.com/valdi-hafdal/DevOp.Toon)

### Environment

| Property | Value |
|---|---|
| Date | 2026-04-19 |
| Runtime | .NET 10.0.6 |
| OS | Zorin OS 18.1 |
| Architecture | x64 |
| Logical CPUs | 16 |
| Items per synthetic dataset | 1,000 |
| Warm iterations per case | 15 |

### Datasets

| Dataset | Description |
|---|---|
| Flat Orders | Uniform DTO rows; ideal for tabular TOON encoding |
| Catalog | Synthetic nested catalog payload with arrays and embedded objects |
| Products | Real-world mixed product payload from benchmark test data |
| Warehouses | Nested warehouse subtree extracted from products |
| Change Fields | Small repetitive row shape that benefits from compact tabular encoding |
| Configuration | Document-style nested config object with service arrays |
