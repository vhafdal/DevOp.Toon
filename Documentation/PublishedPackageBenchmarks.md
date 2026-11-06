# Published package performance

## Published-package benchmark — 6 November 2026

This run loads the actual NuGet binaries: **DevOp.Toon 0.3.0 / Core 0.2.0**, compared where
stated with **DevOp.Toon 0.2.9 / Core 0.1.3**. It measures all **2,051 products**, normalized
through the existing Product DTOs, on **.NET 10.0.13** (Linux x64). Each profile has **30 warmups
and 50 samples**, with `DOTNET_TieredCompilation=0` and **no forced GC inside timed samples**.
Values below are medians. This is in-process serialization, not network/API timing.

### Current 0.3.0 profiles

| Profile | Encode ms | Explicit decode ms | Auto decode ms | Decode allocated bytes | UTF-8 bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Official 4.4, omissions off | 28.23 | 62.35 | 86.52 | 27,730,160 | 5,959,545 |
| Custom 4.4, omissions off | 19.98 | 43.51 | 54.08 | 10,242,568 | 2,939,907 |
| Custom 4.4, compact omissions | 18.93 | 33.29 | 44.63 | 9,000,048 | 2,547,079 |

Both omission-off profiles preserve the complete normalized DTO model. Auto and explicit
results have identical decoded-model hashes. Compact omissions intentionally change the model.
These three profiles run sequentially within one process, so shared-pool history can affect
cross-profile timings. Use isolated repetitions when assessing a performance budget.

### Separate 0.2.9 / 0.3.0 custom comparison

Each side runs in a fresh process with the same DTO input and omission flags set to false:

| Published package/profile | Encode ms | Decode ms | Encode allocated bytes | Decode allocated bytes | UTF-8 bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0.2.9 legacy custom | 21.53 | 32.95 | 12,963,488 | 17,686,776 | 3,059,139 |
| 0.3.0 custom 4.4 | 18.25 | 38.56 | 7,213,952 | 10,242,568 | 2,939,907 |

In this pair, custom 4.4 allocates **42.1% less memory during decoding** and produces a **3.9% smaller
payload**, with **17.0% slower decoding**. These are different wire layouts; the legacy
regression budget is evaluated separately using equivalent profiles.

The old encoder rounds `-296.33000000000004` to `-296.33` on product 18. Its source-model
roundtrip therefore fails even with omissions off; that frozen defect is not attributed to 0.3.0.
The new custom profile matches the DTO model. A separate raw-DOM checker covers the original
JSON's `Publication` member, which is absent from the Product DTO and excluded from these timings.

### Provenance and reproduction

- 0.2.9 package repository commit: `6a61143339707e2b76360d56bb28b13effdf2da4`.
- 0.3.0 package repository commit: `03af9a78e30cc334010edc9146996bf117820608`.
- Old net10 runtime DLL SHA-256: `0DE811F8932BAFBD0596894CFF93EEBD5CB671CCD752321AC473D97697955A59`.
- New net10 runtime DLL SHA-256: `664FB3DF914A26C99F0943F941E4B722A4BB41DA1FEB392D668E21ED6D80553F`.
- Original JSON SHA-256: `9619D5D9D5DDAE0544F07E0D37AB8D4154B9E43B73731109CD77E85679B595DD`.

The [published-package harness](../benchmarks/DevOp.Toon.PublishedPackageBenchmarks/README.md) records
loaded library hashes, output/model hashes, sample settings and raw measurements. It restores the
selected published runtime/Core packages directly, without local runtime project references. Raw
reports and package archives remain in ignored local evidence storage.

Earlier source-build, stream-buffer, small-DTO and transport-option experiments are recorded
in [OptimizationReview.md](OptimizationReview.md). The April 2026 JSON comparisons used a
different runtime, profile and collection method; they are historical results, not this run.
