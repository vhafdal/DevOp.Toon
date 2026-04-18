# DevOp.Toon Benchmark Report

Generated: `2026-04-09 01:08:32 UTC`

Input count: `1000`

Iterations per dataset: `8`

API profile iterations: `12`

API warmup iterations: `5`

## Why DevOp.Toon

- TOON is designed to compress repeated object rows and nested arrays more efficiently than JSON.
- The strongest wins still show up on repeated-row and nested transport payloads such as products, warehouses, and change-field histories.
- The April 8, 2026 refresh added a framework matrix for `netstandard2.0`, `net8.0`, and `net10.0`.
- The `netstandard2.0` row was executed on a `net8.0` host because ASP.NET Core formatter benchmarks require a concrete runtime.
- The `netstandard2.0` row was re-collected on April 9, 2026 after the compatibility-layer changes that enabled the current target.

## Summary

- Payload-size wins were stable across all framework targets because the encoded payloads were identical:
  `Products` `47.98%` smaller raw than JSON, `Warehouses` `68.11%` smaller raw, `Flat orders` `63.50%` smaller raw.
- The strongest current encode row is `net10.0`, where the products compact encoder averaged `14.112 ms` and compact stream encoding averaged `6.791 ms`.
- `netstandard2.0` remained competitive on products compact encoding at `25.024 ms`, and compact stream encoding measured `12.080 ms` on the refreshed compatibility row.
- `net8.0` stayed smaller than JSON but showed the weakest transport profile in this run set, including an unstable API path and a poor compact-stream outlier (`31.588 ms`) on the products encode profile.
- Dedicated warmed `products` API runs favored TOON on `net10.0` by `6.175-8.922 ms/request`.
- Dedicated warmed `products` API runs on `net8.0` also favored TOON by `5.466-12.016 ms/request`, but the full report-export snapshot regressed by `3.857 ms`, so that runtime should be treated as mixed rather than a publishable hard win.

## Framework Matrix

| Library target | Host runtime | Products compact enc ms | Products compact stream ms | Products JSON dec ms | Products TOON dec ms | Products API result | Notes |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- |
| `netstandard2.0` | `net8.0` | 25.024 | 12.080 | 38.389 | 34.047 | n/a | Refreshed on April 9, 2026 after the compatibility-layer changes. API formatter benchmarks are not applicable on `netstandard2.0`. |
| `net8.0` | `net8.0` | 23.474 | 31.588 | 34.464 | 33.332 | Mixed: dedicated warmed runs saved `5.466-12.016 ms`, export snapshot added `3.857 ms` | Size wins held, but transport behavior was not stable enough for a stronger claim. |
| `net10.0` | `net10.0` | 14.112 | 6.791 | 36.931 | 35.172 | Saved `2.243-8.922 ms` across fresh runs | Best overall transport and encode story in this refresh. |

## Nested Hotspot Matrix

| Library target | Warehouses enc ms | Warehouses dec ms | ChangeFields enc ms | ChangeFields dec ms |
| --- | ---: | ---: | ---: | ---: |
| `netstandard2.0` | 2.685 | 7.405 | 1.185 | 2.403 |
| `net8.0` | 2.637 | 7.054 | 1.263 | 2.543 |
| `net10.0` | 4.984 | 5.862 | 0.493 | 1.951 |

## Detailed Results

The detailed dataset table below uses the April 8 `net10.0` report as the primary modern-runtime baseline.

| Dataset | Shape | JSON bytes | TOON bytes | Raw save | Gzip save | Brotli save | JSON enc ms | TOON enc ms | JSON dec ms | TOON dec ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Flat orders | Uniform DTO rows; good for showing tabular TOON wins. | 151066 | 55142 | 63.50% | 15.74% | 7.70% | 0.818 | 2.079 | n/a | n/a |
| Catalog | Synthetic nested catalog payload with arrays and embedded objects. | 283047 | 257070 | 9.18% | 6.91% | 11.75% | 2.395 | 4.874 | n/a | n/a |
| Products | Real-world mixed product payload from benchmark test data. | 2878419 | 1497348 | 47.98% | 12.08% | 7.08% | 26.197 | 23.345 | 36.931 | 35.172 |
| Warehouses | Nested warehouse subtree extracted from products; current encoder hotspot. | 849281 | 270795 | 68.11% | 10.57% | 13.46% | 2.653 | 1.966 | 2.746 | 4.085 |
| ChangeFields | Small repetitive row shape that benefits from compact tabular encoding. | 300822 | 176965 | 41.17% | 7.36% | 4.01% | 0.666 | 0.791 | 1.200 | 0.748 |
| Configuration | Document-style nested configuration object with service arrays. | 15366 | 9299 | 39.48% | 4.35% | 5.22% | 0.085 | 0.075 | n/a | n/a |

## API Response Snapshot

The table below is the `net10.0` report-export snapshot. Use the framework matrix above for the repeated API range, because dedicated `--profile-api` reruns are a better stability check than a single export pass.

| Dataset | JSON response ms | TOON response ms | Delta | Raw size save | Gzip save | Brotli save |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| catalog | 2.181 | 1.627 | -0.554 ms | 9.18% | 6.90% | 11.97% |
| products | 12.343 | 10.100 | -2.243 ms | 47.84% | 11.99% | 6.73% |

## Focused Product Encode Profile

| Library target | JSON serialize ms | TOON readable ms | TOON compact ms | JSON stream ms | TOON compact stream ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| `netstandard2.0` | 26.712 | 45.053 | 25.024 | 19.325 | 12.080 |
| `net8.0` | 24.048 | 34.368 | 23.474 | 30.153 | 31.588 |
| `net10.0` | 21.346 | 32.687 | 14.112 | 22.179 | 6.791 |

## Notes

- Absolute API timings varied materially between collection modes in this refresh. The dedicated `--profile-api` reruns are the most reliable transport comparison for the matrix.
- `net10.0` is the strongest current runtime target for public benchmark messaging.
- `net8.0` still shows the same size and compression wins, but its API timing story should be described as variable until it is re-profiled in a cleaner environment.
- `netstandard2.0` remains a viable library target for size reduction and competitive encode/decode behavior, but it does not have its own ASP.NET Core formatter benchmark row.
