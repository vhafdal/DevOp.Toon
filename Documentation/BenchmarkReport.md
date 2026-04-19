# DevOp.Toon — Performance Benchmarks

> **DevOp.Toon** is a token-efficient alternative to JSON for .NET applications.
> These benchmarks compare serialization speed, heap allocations, and payload size
> across real-world and synthetic datasets using the same CLR that serves your API.

---

## Environment

| Property | Value |
| --- | --- |
| Date | `2026-04-18` |
| Runtime | .NET 10.0.6 (RyuJIT x86-64-v4) |
| SDK | .NET SDK 10.0.106 |
| OS | Zorin OS 18.1 (Linux) |
| CPU | 11th Gen Intel Core i7-11850H 2.50GHz, 1 CPU, 16 logical / 8 physical cores |
| Architecture | X64 |
| BenchmarkDotNet | v0.15.8 |
| BDN job | InProcessShortRun — IterationCount=3, WarmupCount=3, LaunchCount=1 |
| Profiler iterations | 12 warm iterations, explicit `GC.Collect()` between each |

**Encode options used (optimal profile):**
`KeyFolding=Off · Columnar layout · Indent=1 · ExcludeEmptyArrays=true · IgnoreNullOrEmpty=true`

---

## Key Takeaways

| | Result |
| --- | --- |
| Best raw size reduction | **Warehouses** — **76.4% smaller** than JSON |
| Best gzip reduction | **Warehouses** — **20.1% smaller** after gzip |
| Best brotli reduction | **Warehouses** — **20.0% smaller** after brotli |
| Real-world file (2,051 products) | **prods.toon is 77.7% smaller** than prods.json |
| Typed decode speed (100 products) | TOON **31% faster** than `System.Text.Json` |
| Typed decode speed (1,000 products) | TOON **17% faster** than `System.Text.Json` |
| Encode vs TOON-native format | DevOp.Toon encodes **up to 9× faster** with **88% less memory** |
| Real-world file decode | TOON **1.33× faster** than JSON, **3.1× less allocated** |
| HTTP API payload (1,000 products) | TOON response **56% smaller** raw, **14% smaller** gzip |
| HTTP API response time | JSON ~4–8% faster than TOON (encoding cost); TOON smaller on wire |

---

## Payload Size

Sizes measured as UTF-8 bytes. Compression uses `CompressionLevel.SmallestSize`.

| Dataset | JSON bytes | TOON bytes | Raw saved | Gzip saved | Brotli saved |
| --- | ---: | ---: | ---: | ---: | ---: |
| Flat Orders | 151,066 | 55,142 | **63.5%** | 15.7% | 7.7% |
| Catalog | 283,047 | 257,070 | **9.2%** | 6.9% | 11.7% |
| Products (1,000) | 2,878,419 | 1,253,844 | **56.4%** | 14.6% | 9.0% |
| Warehouses | 849,281 | 200,114 | **76.4%** | 20.1% | 20.0% |
| Change Fields | 300,822 | 176,965 | **41.2%** | 7.4% | 4.0% |
| Configuration | 15,366 | 9,299 | **39.5%** | 4.4% | 5.2% |

---

## Real-world File Benchmark — `prods.json` vs `prods.toon`

The actual on-disk files — no synthetic re-encoding.
`prods.toon` was generated from the same source data using the optimal DevOp.Toon profile.

### File Sizes (2,051 products)

| | JSON (`prods.json`) | TOON (`prods.toon`) | Saved |
| --- | ---: | ---: | ---: |
| Raw bytes | 11,520,246 | 2,568,353 | **77.7%** |
| Gzip bytes | 421,934 | 354,875 | **15.9%** |
| Brotli bytes | 274,797 | 246,025 | **10.5%** |

### Decode Speed (full file → `List<Product>`)

| | JSON | TOON | Speedup |
| --- | ---: | ---: | ---: |
| Avg decode (ms) | 23.8 | 17.9 | **1.33×** |
| Avg alloc (KB) | 39,785 | 12,730 | **3.13× less** |

### Re-encode Speed (`List<Product>` → string)

| | JSON | TOON | |
| --- | ---: | ---: | --- |
| Avg encode (ms) | 21.4 | 23.1 | ~parity |
| Avg alloc (KB) | 27,919 | 29,177 | ~parity |

---

## CPU — Typed Serialization (BenchmarkDotNet)

Measured with `[MemoryDiagnoser]`. Baseline = `System.Text.Json`.
`ToonEncodeProducts` uses readable profile (Indent=2, KeyFolding=Safe).
`ToonEncodeProductsCompact` uses optimal profile (Indent=1, KeyFolding=Off).

### Encode — 100 Products

| Method | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: |
| JsonSerializeProducts | 696.2 μs | 1.00 | 555,446 B | 1.00 |
| ToonEncodeProducts | 730.1 μs | 1.05 | 572,956 B | 1.03 |
| ToonEncodeProductsCompact | 715.8 μs | 1.03 | 562,013 B | 1.01 |

### Encode — 1,000 Products

| Method | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: |
| JsonSerializeProducts | 9,074 μs | 1.00 | 6,973,601 B | 1.00 |
| ToonEncodeProducts | 8,698 μs | **0.96** | 7,381,286 B | 1.06 |
| ToonEncodeProductsCompact | 8,404 μs | **0.93** | 7,376,935 B | 1.06 |

### Stream Encode

| Method | Count | Mean | Allocated |
| --- | ---: | ---: | ---: |
| JsonSerializeProductsToStream | 100 | 703.1 μs | 936 B |
| ToonEncodeProductsCompactToStream | 100 | **609.1 μs** | 358,802 B |
| JsonSerializeProductsToStream | 1,000 | 8,083.7 μs | 1,581 B |
| ToonEncodeProductsCompactToStream | 1,000 | **7,559.1 μs** | 3,945,836 B |

> JSON stream serialize is near-zero alloc because it writes directly to the stream.
> TOON stream path still buffers internally — this is a known area for future optimization.

---

## CPU — Typed Deserialization (BenchmarkDotNet)

Baseline = `System.Text.Json`. TOON uses the optimal encode/decode profile.

| Method | Count | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: |
| JsonDeserializeProducts | 100 | 1,128.4 μs | 1.00 | 307.23 KB | 1.00 |
| ToonDecodeProducts | 100 | **772.5 μs** | **0.69** | 555.89 KB | 1.81 |
| JsonDeserializeProducts | 1,000 | 12,759.8 μs | 1.00 | 3,671 KB | 1.00 |
| ToonDecodeProducts | 1,000 | **10,572.8 μs** | **0.83** | 6,240 KB | 1.70 |

**TOON decodes 17–31% faster than `System.Text.Json`.** Allocations are ~1.7–1.8× higher because the columnar decoder builds an intermediate token structure before materializing objects. On real-world payloads with many null/empty fields stripped by the encoder, the allocation gap narrows considerably (see real-world file section above).

---

## CPU — Encode/Decode Speed by Dataset (Profiler, 12 iterations)

### Encode

| Dataset | JSON (ms) | TOON (ms) | Delta | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Flat Orders | 0.920 | 2.193 | +1.273 | 0.42× |
| Catalog | 2.383 | 4.614 | +2.231 | 0.52× |
| Products | 23.218 | 17.546 | −5.672 | **1.32×** |
| Warehouses | 5.493 | 2.013 | −3.480 | **2.73×** |
| Change Fields | 0.659 | 1.142 | +0.483 | 0.58× |
| Configuration | 0.088 | 0.098 | +0.010 | 0.90× |

### Decode

| Dataset | JSON (ms) | TOON (ms) | Delta | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Products | 23.8 | 31.7 | +7.9 | 0.75× |
| Warehouses | 3.6 | 2.4 | −1.2 | **1.50×** |
| Change Fields | 1.4 | 0.9 | −0.5 | **1.56×** |

---

## Memory — Heap Allocations by Dataset (Profiler, 12 iterations)

### Encode Allocations

| Dataset | JSON (KB) | TOON (KB) | Delta | Ratio |
| --- | ---: | ---: | ---: | ---: |
| Flat Orders | 791.5 | 799.7 | +8.2 | 1.01× |
| Catalog | 1,561.3 | 2,212.2 | +650.9 | 1.42× |
| Products | 13,818.7 | 14,358.9 | +540.2 | 1.04× |
| Warehouses | 3,691.2 | 1,988.3 | −1,702.9 | **0.54×** |
| Change Fields | 1,596.5 | 2,554.0 | +957.5 | 1.60× |
| Configuration | 46.4 | 104.8 | +58.4 | 2.26× |

### Decode Allocations

| Dataset | JSON (KB) | TOON (KB) | Delta | Ratio |
| --- | ---: | ---: | ---: | ---: |
| Products | 19,800.9 | 6,240.3 | −13,560.6 | **0.32×** |
| Warehouses | 4,860.9 | 856.4 | −4,004.5 | **0.18×** |
| Change Fields | 1,972.7 | 1,097.8 | −874.9 | **0.56×** |

---

## DOM Parsing (BenchmarkDotNet)

Parsing JSON text or TOON text into a node tree (`ToonNode`).
Baseline = `JsonNode.Parse(jsonText)`.

| Method | Items | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: |
| JsonNodeParse | 10 | 15.7 μs | 1.00 | 27,280 B | 1.00 |
| ToonDecode | 10 | 40.3 μs | 2.59 | 93,112 B | 3.41 |
| JsonNodeParse | 100 | 157.7 μs | 1.00 | 265,241 B | 1.00 |
| ToonDecode | 100 | 421.6 μs | 2.67 | 890,818 B | 3.36 |
| JsonNodeParse | 1,000 | 1,893 μs | 1.00 | 2,654,016 B | 1.00 |
| ToonDecode | 1,000 | 7,966 μs | 4.22 | 8,880,199 B | 3.35 |
| JsonDocumentParse (UTF-8) | 10 | 8.0 μs | — | 72 B | — |
| JsonDocumentParse (UTF-8) | 100 | 77.0 μs | — | 85 B | — |
| JsonDocumentParse (UTF-8) | 1,000 | 751.4 μs | — | 588 B | — |

> `JsonDocument` (UTF-8 pooled) is the gold standard for low-alloc DOM parsing — near-zero allocations because it rents from `ArrayPool`. TOON's DOM mode is not designed to compete with this; the TOON value proposition is typed encode/decode, not raw DOM.

---

## Encode Format Comparison — DevOp.Toon vs TOON-Native (BenchmarkDotNet)

"TOON-native" = unoptimised baseline layout (Auto, no null/empty filtering).
"DevOp.Toon" = optimal columnar profile with `IgnoreNullOrEmpty` + `ExcludeEmptyArrays`.
Reference JSON = `System.Text.Json` with default options.

### Encode

| Method | Count | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: |
| EncodeToonNative | 100 | 3,749 μs | 1.00 | 4,723 KB | 1.00 |
| **EncodeDevOpToon** | 100 | **685 μs** | **0.18** | **551 KB** | **0.12** |
| JsonSerializeProducts | 100 | 688 μs | — | 550 KB | — |
| EncodeToonNative | 1,000 | 72,014 μs | 1.00 | 50,567 KB | 1.00 |
| **EncodeDevOpToon** | 1,000 | **8,268 μs** | **0.11** | **7,236 KB** | **0.14** |
| JsonSerializeProducts | 1,000 | 8,579 μs | — | 7,195 KB | — |

### Decode

| Method | Count | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: |
| DecodeToonNative | 100 | 1,800 μs | 1.00 | 2,623 KB | 1.00 |
| **DecodeDevOpToon** | 100 | **779 μs** | **0.43** | **557 KB** | **0.21** |
| DecodeToonNative | 1,000 | 25,741 μs | 1.00 | 27,815 KB | 1.00 |
| **DecodeDevOpToon** | 1,000 | **10,329 μs** | **0.40** | **6,242 KB** | **0.22** |

DevOp.Toon's columnar + null-filtering profile encodes **up to 9× faster** and with **88% less memory** than naive TOON encoding, while matching `System.Text.Json` speed and producing dramatically smaller output.

---

## HTTP API Response (in-process TestServer)

Benchmarks an ASP.NET Core controller returning product pages over an in-process `TestServer`.
Two `HttpClient` instances use content negotiation: `Accept: application/json` and `Accept: application/toon`.
Encode options: optimal profile (`KeyFolding=Off · Columnar · Indent=1 · ExcludeEmptyArrays · IgnoreNullOrEmpty`).

### Payload Sizes

Sizes measured as UTF-8 bytes of the HTTP response body.

| Page size | JSON bytes | TOON bytes | Raw saved | Gzip saved | Brotli saved |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 268,406 | 110,537 | **58.8%** | 13.5% | 8.9% |
| 500 | 1,401,532 | 602,863 | **57.0%** | 14.3% | 8.7% |
| 1,000 | 2,870,581 | 1,253,844 | **56.3%** | 14.5% | 8.6% |

### Response Time (BenchmarkDotNet, InProcessShortRun)

Baseline = JSON (`Accept: application/json`).

| Method | Mean | Ratio | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: |
| JsonResponse100 | 859.5 μs | 1.00 | 576.6 KB | 1.00 |
| ToonResponse100 | 930.9 μs | **1.08** | 695.5 KB | 1.21 |
| JsonResponse1000 | 9,922 μs | 1.00 | 8,258 KB | 1.00 |
| ToonResponse1000 | 10,326 μs | **1.04** | 10,436 KB | 1.26 |

### Response Time (Profiler, 12 iterations)

| Page size | JSON avg (ms) | TOON avg (ms) | Speedup |
| ---: | ---: | ---: | ---: |
| 100 | 3.077 | 4.654 | 0.66× |
| 500 | 13.489 | 19.363 | 0.70× |
| 1,000 | 9.207 | 11.531 | 0.80× |

> TOON responses are **4–8% slower** than JSON in BDN (encoding overhead), but deliver **56–59% smaller** raw payloads.
> The profiler shows a larger gap for small page sizes — the in-process test-server overhead dominates there.
> On a real network, the smaller TOON payload will fully offset the encoding cost at typical bandwidths.

---

## Dataset Descriptions

| Dataset | Shape | Description |
| --- | --- | --- |
| Flat Orders | Synthetic | Uniform DTO rows — every object has the same fields. Ideal for columnar TOON encoding. |
| Catalog | Synthetic | Nested catalog objects with embedded arrays (Dimensions, Inventory, Supplier, Tags). |
| Products | Real-world | Full product records from `prods.json` — mixed types, many nullable fields, nested lists. |
| Warehouses | Real-world | Warehouse sub-records extracted from products. Highly repetitive, benefits most from columnar layout. |
| Change Fields | Real-world | Small repetitive change-log rows — consistent shape drives strong tabular wins. |
| Configuration | Synthetic | Document-style config object with a service array. Shows TOON on non-tabular shapes. |

---

## Notes

- **BDN benchmarks** use `InProcessShortRun` (3 iterations, 3 warm-up). Short runs have higher variance — treat ratios as directional.
- **Profiler measurements** use 12 warm iterations with `GC.Collect()` between each — lower variance, better for absolute timing.
- The **columnar array layout** eliminates repeated property names from every object row. This is the core mechanism behind TOON's size wins on repeated-shape arrays.
- **`IgnoreNullOrEmpty` + `ExcludeEmptyArrays`** strip null/empty fields from the output. Real-world product data has many such fields, explaining why `prods.toon` is 77.7% smaller than `prods.json`.
- **TOON decode allocates more than `System.Text.Json`** on synthetic benchmarks where no fields are stripped. On real-world data where many fields are absent, TOON allocates significantly less because there is simply less to decode.
- **DOM parsing** (`ToonNode`) is ~2.6–4× slower and ~3.4× more alloc than `JsonNode`. This is expected — TOON DOM is provided for interop and inspection, not high-throughput parsing. Use typed decode for performance-critical paths.
- Gzip and Brotli use `CompressionLevel.SmallestSize`. Under `Optimal` the gap is slightly smaller.
- Source: [DevOp.Toon](https://github.com/valdi-hafdal/DevOp.Toon)
