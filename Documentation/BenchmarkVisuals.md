# DevOp.Toon — Benchmark Data for Visual Design

I need help creating charts and visual representations of the following benchmark data
for use on a product/marketing website. The product is **DevOp.Toon**, a .NET library
that serializes data to TOON format instead of JSON — producing significantly smaller
payloads with faster decode times on real-world structured data.

---

## Context

- **What it is:** A .NET serialization library. TOON is a structured alternative to JSON.
- **Target audience:** .NET developers and engineering managers evaluating API efficiency tools.
- **Use case for visuals:** Product landing page, documentation site, evaluation conversations.
- **Tone:** Professional, data-honest, developer-credible. Not hype — the numbers speak.

---

## Environment

| Property | Value |
|---|---|
| Date | April 2026 |
| Runtime | .NET 10.0.6 |
| OS | Zorin OS 18.1 (Linux), X64 |
| Logical CPUs | 16 |
| Items per dataset | 1,000 |
| Warm iterations | 15 |

---

## Dataset 1 — Payload Size (Raw Bytes)

TOON vs JSON raw byte count. Lower is better for TOON.

| Dataset | JSON bytes | TOON bytes | % Smaller |
|---|---:|---:|---:|
| Real-world products (2,051 items, on disk) | 11,520,246 | 2,568,353 | **77.7%** |
| Warehouses | 849,281 | 200,114 | **76.4%** |
| Flat Orders | 151,066 | 55,142 | **63.5%** |
| Products (1,000 items) | 2,878,419 | 1,253,844 | **56.4%** |
| Change Fields | 300,822 | 176,965 | **41.2%** |
| Configuration | 15,366 | 9,299 | **39.5%** |
| Catalog | 283,047 | 257,070 | **9.2%** |

---

## Dataset 2 — Payload Size After Compression

Even after gzip/brotli, TOON payloads remain smaller.

| Dataset | Gzip saved | Brotli saved |
|---|---:|---:|
| Warehouses | **20.1%** | **20.0%** |
| Real-world products (on disk) | **15.9%** | **10.5%** |
| Flat Orders | **15.7%** | **7.7%** |
| Products (1,000 items) | **14.6%** | **9.0%** |
| Change Fields | **7.4%** | **4.0%** |
| Catalog | **6.9%** | **11.7%** |
| Configuration | **4.4%** | **5.2%** |

---

## Dataset 3 — Encode Speed (ms, lower is better)

1,000 items per dataset. Average of 15 warm iterations with GC.Collect() between runs.

| Dataset | JSON (ms) | TOON (ms) | Speedup |
|---|---:|---:|---:|
| Products | 36.550 | 18.050 | **2.02× faster** |
| Warehouses | 2.051 | 2.350 | 0.87× |
| Change Fields | 0.718 | 0.838 | 0.86× |
| Configuration | 0.059 | 0.068 | 0.87× |
| Flat Orders | 0.794 | 2.026 | 0.39× |
| Catalog | 1.957 | 5.202 | 0.38× |

**Note:** TOON encode is faster at scale (Products: 2.02×). On smaller/flatter datasets
the columnar layout planning has overhead that outweighs savings at small item counts.
This is the expected trade-off.

---

## Dataset 4 — Decode Speed (ms, lower is better)

| Dataset | JSON (ms) | TOON (ms) | Speedup |
|---|---:|---:|---:|
| Change Fields | 1.676 | 0.854 | **1.96× faster** |
| Warehouses | 3.630 | 2.997 | **1.21× faster** |
| Products | 35.626 | 35.049 | **1.02× faster** |

---

## Dataset 5 — Real-World File Benchmark

Full-file encode and decode of 2,051 actual products.

### File sizes

| | JSON (`prods.json`) | TOON (`prods.toon`) | Saved |
|---|---:|---:|---:|
| Raw bytes | 11,520,246 | 2,568,353 | **77.7%** |
| Gzip bytes | 421,934 | 354,875 | **15.9%** |
| Brotli bytes | 274,797 | 246,025 | **10.5%** |

### Speed

| Operation | JSON (ms) | TOON (ms) | Speedup |
|---|---:|---:|---:|
| Decode full file → `List<Product>` | 26.1 | 18.6 | **1.40× faster** |
| Re-encode `List<Product>` → string | 16.2 | 18.0 | 0.90× |

---

## Dataset 6 — API Response Speed

In-process ASP.NET Core test. 5 warmup + 12 measured iterations.
Both formats served from the same controller, same data.

| Dataset | JSON response (ms) | TOON response (ms) | Delta | Raw saved | Gzip saved |
|---|---:|---:|---:|---:|---:|
| Catalog | 12.369 | 11.628 | **−0.741 ms** | 56.3% | 14.5% |
| Products | 11.396 | 10.827 | **−0.569 ms** | 56.3% | 14.5% |

TOON HTTP responses are faster end-to-end because smaller payloads flush faster,
even though the encoder has to do slightly more work for some shapes.

---

## Key Headline Stats (for callout cards / hero section)

These are the strongest single numbers for marketing use:

| Stat | Value | Source |
|---|---|---|
| Max raw size reduction | **77.7% smaller** | Real-world 2,051 product file vs JSON |
| Max gzip reduction | **20.1% smaller** | Warehouses dataset |
| Max encode speedup | **2.02× faster** | Products dataset (1,000 items) |
| Max decode speedup | **1.96× faster** | Change Fields dataset |
| Real-world decode speedup | **1.40× faster** | 2,051 product file |
| API response time saving | **−0.74 ms** | Catalog dataset, in-process ASP.NET Core |

---

## Suggested Chart Types

Here are the charts that would be most impactful:

1. **Horizontal bar chart — Raw size reduction by dataset**
   Compare JSON vs TOON byte counts side-by-side. One bar per dataset.
   Highlight the "77.7%" real-world bar prominently.

2. **Grouped bar chart — Size reduction: raw vs gzip vs brotli**
   For each dataset, show three bars: raw %, gzip %, brotli %.
   Demonstrates that savings survive compression.

3. **Side-by-side bar — Encode speed (JSON vs TOON)**
   ms per dataset. Split datasets: ones where TOON wins vs ones where JSON is faster.
   Include a note explaining the scale trade-off.

4. **Side-by-side bar — Decode speed (JSON vs TOON)**
   Only for datasets where both were measured (Products, Warehouses, Change Fields).

5. **Hero number callout cards**
   Large-text cards for: 77.7%, 20.1%, 2.02×, 1.40×.
   Clean, minimal — suitable for a landing page hero section.

6. **Waterfall / before-after — Real-world file sizes**
   JSON 11.5 MB → TOON 2.6 MB → TOON+gzip 354 KB
   Shows the compounding effect of format + compression.

7. **Timeline / speedup comparison bar — Decode speed**
   JSON bar = baseline (1.0×), TOON bars show the speedup multiple.
   Visually clean for a "faster decoding" claim.

---

## Honest Framing (include in any copy alongside charts)

- TOON wins are largest on **repeated-row and nested collection data** — product catalogs,
  warehouse records, audit logs, batch API responses.
- Encode is faster at scale (Products 2.02×) but slower on small flat datasets (Flat Orders 0.39×).
  This is a known and expected trade-off.
- The Catalog dataset (9.2% raw saving) shows TOON is not a universal replacement —
  data shape matters.
- All benchmarks ran in-process on .NET 10.0.6 on Linux X64.
  No out-of-process overhead included.
