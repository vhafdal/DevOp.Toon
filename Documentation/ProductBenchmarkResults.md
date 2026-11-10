# Full product benchmark evidence — 2026-11-06

For the current results measured from the **published NuGet binaries**, see
[PublishedPackageBenchmarks.md](PublishedPackageBenchmarks.md). The measurements below record
the earlier source-build optimization and regression work.

All three existing-path regression comparisons for the protocol upgrade pass with the isolated
method below. These measurements do not authorize publication.

The benchmark uses all 2,051 products from the original checked-in UTF-16 JSON fixture, with
the existing product DTO types. Source SHA-256 is
`9619D5D9D5DDAE0544F07E0D37AB8D4154B9E43B73731109CD77E85679B595DD`.
The original `prods.json`, `prods.toon` and `prodsOLD.toon` files were preserved.
The timed round-trip comparison uses the normalized DTO model. The original JSON also has a
`Publication` member on one row which is not declared by `Product` and is dropped during DTO
loading. Typed-model hashes therefore do not establish preservation of every original JSON
member. A separate one-shot raw DOM round trip checks that distinction.

## Method

The clean baseline is runtime commit `6a61143`. Candidate and baseline use the same compiler,
.NET 10 runtime, optimized Debug settings, DTO input, indentation, delimiter and explicit
omission settings. Release builds resolve published packages, so optimized Debug with local
project references is used for this source comparison. Each raw report records the loaded
library's absolute path, SHA-256 and debugging flags. This is a serializer benchmark, not HTTP.

Existing official 3.0 and legacy custom compact/lossless profiles are regression comparisons.
Official 4.4 and grouped custom 4.4 are new wire shapes, measured separately. Compact settings
intentionally omit values and are never treated as equivalent to a lossless source model.

Timed samples use a warmed library and no forced collections between operations. Raw samples
include elapsed time, thread allocations, collection counts, process memory and managed heap
delta. The heap delta is not retained memory; retained-memory checks are separate untimed
operations. Process peak working set is cumulative and includes fixture/setup work. The final
existing-path comparison runs each profile in a separate process with 30 warmups and 30 samples
to avoid previous profiles changing shared pool state. Earlier multi-profile and optimization
experiments are retained as exploratory evidence, including unsuccessful regression verdicts.

## Observed results

The isolated legacy custom lossless comparison preserves the baseline's encoded bytes and
decoded model hashes exactly. Encode measured 13.98 ms baseline versus 13.45 ms candidate;
decode measured 33.07 ms versus 30.80 ms. Encode allocations match at 8,968,736 bytes; candidate
decode allocates 32 fewer bytes. This profile passes the 5% time/no-added-allocation budget.
All three isolated existing profiles pass the overall 5% time/no-added-allocation gate:

| Existing profile | Encode time ratio | Decode time ratio | Extra encode bytes | Extra decode bytes |
| --- | ---: | ---: | ---: | ---: |
| Official 3.0 equivalent | 1.006× | 0.994× | -8 | -32 |
| Custom legacy lossless | 0.962× | 0.931× | 0 | -32 |
| Custom legacy compact | 0.943× | 0.955× | 0 | -32 |

Ratios are candidate/baseline medians. Encoded bytes and decoded model hashes match the baseline
for each profile. This verdict applies to the isolated matched comparisons, not the earlier
multi-profile runs whose pool history produced inconsistent allocation deltas. Those unsuccessful
exploratory reports remain available rather than being presented as passing evidence.

An earlier optimized source-build runtime (`3F6054F7BB5BF68801E9E11F5F2DDA2A886C7FCED33F021EDC70EC318C041776`)
measured the new shapes as follows on all 2,051 normalized DTOs:

| New profile | Encode ms | Explicit decode ms | Auto decode ms | Decode allocated bytes | UTF-8 bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Official 4.4 lossless | 19.26 | 52.27 | 70.60 | 27,730,160 | 5,959,545 |
| Custom 4.4 lossless | 14.13 | 31.86 | 44.85 | 10,242,568 | 2,939,907 |
| Custom 4.4 compact | 14.03 | 25.19 | 34.67 | 9,000,048 | 2,547,079 |

Both lossless profiles match the complete normalized DTO model. All three Auto results have the
same decoded-model hash as explicit decoding; compact omissions remain intentional. Auto timings
include protocol detection, so they are separate from explicit-version decoding. The 31.86 ms
custom figure belongs to this all-profile run; use the separate fresh pair below to compare legacy
and new-custom latency. Host variation does not establish that an earlier CPU gap was eliminated.

New custom lossless allocation is about 42% lower than the earlier isolated legacy lossless figure
of 17.69 MB. Span-based fields and bounded exact-header reuse reduced it from the preceding new
implementation's 15.04 MB to 10.24 MB. The grouped lossless payload remains about 3.9% smaller than
legacy lossless (3,059,139 bytes). Reverted string-setter and surrogate-precheck experiments are
recorded in [OptimizationReview.md](OptimizationReview.md), alongside measured client metadata
improvements and rejected HTTP experiments.

## Fresh isolated legacy/new-custom comparison

The final separate processes use 30 warmups and 50 samples, the same complete normalized DTO
input and explicit lossless omission settings:

| Profile | Encode ms | Decode ms | Encode allocated bytes | Decode allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Frozen legacy custom | 13.25 | 31.13825 | 8,968,736 | 17,686,776 |
| Retained new custom 4.4 | 13.02 | 34.48255 | 7,213,952 | 10,241,608 |

The new custom decoder remains approximately 10.7% slower in this matched pair, with about 42%
less decode allocation and 19.6% less encode allocation. Its payload is about 3.9% smaller.
The latency gap is not claimed to be eliminated. The frozen legacy numeric precision defect
below remains present; the new lossless profile matches the complete normalized DTO model.
Raw reports are `toon-final-pair-legacy.json` and `toon-final-pair-custom.json` in the evidence
directory. The rejected fused-setter experiment and its separate before/after reports do not
replace this retained-runtime comparison.

The frozen legacy encoder already rounds product 18's `TotalQuantityInWarehouse` from
`-296.33000000000004` to `-296.33`. Both baseline and candidate legacy output retain this existing
defect. The comparator requires their exact output/model hashes to match and reports the source
round-trip failure explicitly. New lossless modes must match the normalized DTO model instead.

Both saved TOON files decode 2,051 rows under explicit legacy settings. They contain the old
custom wire family; their names are not evidence of an official protocol version. `prods.toon`
also changes omitted empty arrays to null, and `prodsOLD.toon` contains historical numeric
differences. Their raw saved-fixture results are retained separately from round-trip checks.

## Original raw JSON preservation

A separate one-shot DOM check used the frozen retained runtime SHA above and all original JSON
properties, without projecting into Product DTOs. Both official 4.4 and custom 4.4 lossless round
trips pass: all 2,051 rows, the `Publication` member on one row, every property/array position,
strings, booleans and nulls are preserved. Numbers compare by exact mathematical decimal
coefficient/exponent; object-property order may change. Raw JSON string values, including their
date representations, are compared exactly.

The raw DOM payloads are 5,604,105 bytes for official 4.4 and 2,957,992 bytes for custom 4.4.
These differ from DTO payload sizes because the inputs differ after DTO projection/date
normalization. This is correctness evidence, not a DOM timing/allocation benchmark. The
[reusable checker](../benchmarks/DevOp.Toon.RawJsonRoundTrip/README.md) records the input
and runtime hashes and the publication-member counts. Its raw reports are retained locally and
excluded from the public repository.

## Reproduction and raw reports

Use [the benchmark harness](../benchmarks/DevOp.Toon.ProtocolBenchmarks/README.md) for commands,
including `--profiles`, `--warmups 30`, `--repeats 30` and the baseline/candidate comparator.
All builds and executions ran sequentially, one at a time. Full raw reports and
exploratory optimization results are retained locally and excluded from the public repository.
Local report names distinguish isolated comparisons from multi-profile and intermediate experiments.

Compression measurements, including native Zstandard and independent CLI round-trip checks,
are reported in [CompressionComparison.md](CompressionComparison.md). They add no production
dependency and do not establish .NET HTTP compression support.
