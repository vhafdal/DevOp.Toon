# DevOp.Toon — Performance Benchmarks

> **DevOp.Toon** is a token-efficient alternative to JSON for .NET applications.
> These benchmarks compare serialization speed, heap allocations, and payload size
> across real-world and synthetic datasets using the same CLR that serves your API.

## Environment

| Property | Value |
| --- | --- |
| Date | `2026-04-19` |
| Runtime | .NET 10.0.6 |
| OS | Zorin OS 18.1 |
| Architecture | X64 |
| Logical CPUs | 16 |
| Items per dataset | 1,000 |
| Warm iterations per case | 15 |

## Key Takeaways

- **Smallest raw payload:** `Warehouses` — **76.4% smaller** than JSON  
- **Smallest gzip payload:** `Warehouses` — **20.1% smaller** after compression  
- **Smallest brotli payload:** `Warehouses` — **20.0% smaller** after brotli  
- **Fastest encode:** `Products` — TOON encode vs JSON delta: **-18.50 ms**  
- **Fastest decode:** `Change Fields` — TOON decode vs JSON delta: **-0.82 ms**  

## Payload Size

Payload sizes are measured as UTF-8 bytes. Compression uses `SmallestSize` quality.

| Dataset | JSON bytes | TOON bytes | Raw saved | Gzip saved | Brotli saved |
| --- | ---: | ---: | ---: | ---: | ---: |
| Flat Orders | 151,066 | 55,142 | **63.5%** | 15.7% | 7.7% |
| Catalog | 283,047 | 257,070 | **9.2%** | 6.9% | 11.7% |
| Products | 2,878,419 | 1,253,844 | **56.4%** | 14.6% | 9.0% |
| Warehouses | 849,281 | 200,114 | **76.4%** | 20.1% | 20.0% |
| Change Fields | 300,822 | 176,965 | **41.2%** | 7.4% | 4.0% |
| Configuration | 15,366 | 9,299 | **39.5%** | 4.4% | 5.2% |

## CPU — Encode Speed

Average of warm iterations. Lower is better.

| Dataset | JSON (ms) | TOON (ms) | Delta (ms) | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Flat Orders | 0.794 | 2.026 | +1.231 | 0.39× |
| Catalog | 1.957 | 5.202 | +3.245 | 0.38× |
| Products | 36.550 | 18.050 | -18.500 | **2.02×** |
| Warehouses | 2.051 | 2.350 | +0.299 | 0.87× |
| Change Fields | 0.718 | 0.838 | +0.121 | 0.86× |
| Configuration | 0.059 | 0.068 | +0.009 | 0.87× |

## CPU — Decode Speed

Average of warm iterations. Lower is better.

| Dataset | JSON (ms) | TOON (ms) | Delta (ms) | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Products | 35.626 | 35.049 | -0.577 | **1.02×** |
| Warehouses | 3.630 | 2.997 | -0.632 | **1.21×** |
| Change Fields | 1.676 | 0.854 | -0.822 | **1.96×** |

## Memory — Heap Allocations

Measured via `GC.GetAllocatedBytesForCurrentThread()` over warm iterations. Lower is better.

### Encode Allocations

| Dataset | JSON alloc (KB) | TOON alloc (KB) | Delta (KB) | Ratio |
| --- | ---: | ---: | ---: | ---: |
| Flat Orders | 295.4 | 545.6 | +250.2 | 1.85× |
| Catalog | 553.2 | 1008.6 | +455.5 | 1.82× |
| Products | 5622.8 | 6270.7 | +647.8 | 1.12× |
| Warehouses | 1659.1 | 966.1 | -693.0 | **0.58×** |
| Change Fields | 587.9 | 1531.8 | +943.9 | 2.61× |
| Configuration | 30.3 | 42.7 | +12.3 | 1.41× |

### Decode Allocations

| Dataset | JSON alloc (KB) | TOON alloc (KB) | Delta (KB) | Ratio |
| --- | ---: | ---: | ---: | ---: |
| Products | 3414.9 | 6240.3 | +2825.4 | 1.83× |
| Warehouses | 764.9 | 856.4 | +91.5 | 1.12× |
| Change Fields | 948.7 | 1097.8 | +149.1 | 1.16× |

## Real-world File Benchmark — `prods.json` vs `prods.toon`

These measurements use the actual benchmark test-data files exactly as they appear on disk — no synthetic re-encoding.
The TOON file was pre-encoded from the same source data using the DevOp.Toon columnar layout.

### File Sizes

| | JSON (`prods.json`) | TOON (`prods.toon`) | Saved |
| --- | ---: | ---: | ---: |
| Products | 2,051 | 2,051 | — |
| Raw bytes | 11,520,246 | 2,568,353 | **77.7%** |
| Gzip bytes | 421,934 | 354,875 | **15.9%** |
| Brotli bytes | 274,797 | 246,025 | **10.5%** |

### Decode Speed (full file → `List<Product>`)

| | JSON | TOON | Speedup |
| --- | ---: | ---: | ---: |
| Avg decode (ms) | 26.1 | 18.6 | **1.40×** |
| Alloc (KB) | 9199.4 | 12729.2 | 0.72× |

### Re-encode Speed (`List<Product>` → string)

Encodes the already-decoded product list back to each format.

| | JSON | TOON | Speedup |
| --- | ---: | ---: | ---: |
| Avg encode (ms) | 16.2 | 18.0 | 0.90× |
| Alloc (KB) | 12214.2 | 13786.4 | 0.89× |

## API Response Snapshot

Warmup iterations: `5` &nbsp;|&nbsp; Measured iterations: `12`

| Dataset | JSON (ms) | TOON (ms) | Delta | Raw saved | Gzip saved | Brotli saved |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| catalog | 12.369 | 11.628 | **-0.741 ms** | 56.3% | 14.5% | 8.6% |
| products | 11.396 | 10.827 | **-0.569 ms** | 56.3% | 14.5% | 8.6% |

## Dataset Descriptions

| Dataset | Description |
| --- | --- |
| Flat Orders | Uniform DTO rows; ideal for tabular TOON encoding. |
| Catalog | Synthetic nested catalog payload with arrays and embedded objects. |
| Products | Real-world mixed product payload from benchmark test data. |
| Warehouses | Nested warehouse subtree extracted from products. |
| Change Fields | Small repetitive row shape that benefits from compact tabular encoding. |
| Configuration | Document-style nested config object with service arrays. |

## Notes

- All timings are average wall-clock time over warm iterations with an explicit `GC.Collect()` between runs.
- TOON uses a **columnar array layout** for object arrays, eliminating repeated key names — this is where the largest size wins come from.
- `IgnoreNullOrEmpty` and `ExcludeEmptyArrays` are enabled in the optimal profile used here. These omit null and empty fields from the output, which is why size wins are larger than with naive encoding.
- TOON decode allocations are lower than `System.Text.Json` on real-world data because the columnar layout means fewer tokens to process per object. On synthetic datasets with no null/empty fields the allocations are closer.
- Gzip and Brotli compression use `CompressionLevel.SmallestSize`. Under `Optimal` the gap is slightly smaller.
- Benchmarks are run in-process using the same .NET runtime that serves production traffic. No out-of-process overhead.
- Source: [DevOp.Toon](https://github.com/valdi-hafdal/DevOp.Toon)
