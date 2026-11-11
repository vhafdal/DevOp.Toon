# Full product protocol benchmark

This serializer-only harness uses all 2,051 checked-in products and the existing product DTOs.
It does not reference the API package. Supply `--data` with the absolute path to
`benchmarks/DevOp.Toon.Benchmarks/TestData`.

The baseline build selects `BASELINE` and calls only APIs available at commit `6a61143`.
Candidate builds additionally compare official 4.4 and custom 4.4. Both use identical DTO input,
indentation, delimiter and omission policies. Compact omission profiles are reported separately.

Run both builds with identical compiler/runtime settings, sequentially, one at a time.
Set `DEVOP_TOON_CORE_CSPROJ` when using the existing Debug local-project-reference workflow.
`Optimize=true` can be used for optimized Debug comparisons; report that configuration explicitly.
Use `--no-incremental` when rebuilding the comparison after changing compiler optimization settings;
an earlier experiment reused a runtime assembly built with different settings. Confirm the loaded
assembly's recorded SHA-256 and debugging flags before comparing timings.

```bash
mkdir -p artifacts
dotnet build benchmarks/DevOp.Toon.ProtocolBenchmarks/DevOp.Toon.ProtocolBenchmarks.csproj -c Debug -p:Optimize=true -p:BuildFlavor=baseline -p:RuntimeProject=/absolute/baseline/src/DevOp.Toon/DevOp.Toon.csproj
dotnet benchmarks/DevOp.Toon.ProtocolBenchmarks/bin/baseline/Debug/net10.0/DevOp.Toon.ProtocolBenchmarks.dll --data /absolute/TestData --output artifacts/toon-baseline.json

dotnet build benchmarks/DevOp.Toon.ProtocolBenchmarks/DevOp.Toon.ProtocolBenchmarks.csproj -c Debug -p:Optimize=true -p:BuildFlavor=candidate
dotnet benchmarks/DevOp.Toon.ProtocolBenchmarks/bin/candidate/Debug/net10.0/DevOp.Toon.ProtocolBenchmarks.dll --data /absolute/TestData --output artifacts/toon-candidate.json
```

Candidate builds also run `custom4.4-constants-lossless` and `custom4.4-constants-compact`, the
custom 4.4 profiles with `ConstantColumns` enabled. After the product profiles, the harness encodes a
deterministic synthetic chart of accounts (`--synthetic-accounts <rows>`, default 1000, `0` skips it)
with each 4.4 profile and reports UTF-8, gzip and Brotli sizes plus a typed round-trip check under
`SyntheticAccounts`; these entries are not timed.

Default measurement uses five warmups and five samples per operation. Override with
`--warmups`, `--repeats`, or restrict profiles with comma-separated `--profiles`.
Add `--measure-auto` to measure default protocol detection plus decode for the new official/custom
profiles. `AutoDecode` samples are separate from explicit-version `Decode` samples and include a
full decoded-model hash comparison against the explicit path. Ambiguity/errors are reported rather
than silently retried. Reports record the configured warmups and samples per operation.
Setup, output compression, direct-path probes and JSON correctness checks are outside the
measurement window. The harness never uses a JSON fallback for TOON deserialization.

Reports contain raw samples for encode/decode milliseconds, allocated bytes, collection counts,
managed heap deltas, process working/private memory, payload sizes, gzip and Brotli sizes.
Process peak working set is cumulative process state, not an isolated per-operation peak;
absolute process memory includes fixture/output setup. Timed samples use `GetTotalMemory(false)`;
their heap delta is not retained memory. Separate untimed retained-memory measurements force
collection and include the last result. No forced collection occurs between timed samples.
Use repeated runs to assess noise before applying the 5% throughput/no-extra-allocation gate.
Use at least 30 warmups and 30 samples for the regression gate, and run each comparable profile
in its own process for both binaries. Earlier profiles and separate retained-memory collections
can change shared pool state; report such exploratory multi-profile results separately.

Lossless profiles check typed round trips against the same source DTO JSON model, including
all nested fields. Compact profiles explicitly report any changed empty/null distinction.
Unknown original JSON members absent from the DTO schema are outside this timed comparison.
In particular, the original fixture's one-row `Publication` member requires a separate raw DOM
round-trip check; a typed model hash cannot prove that member was preserved.
The exit code is nonzero for a codec exception or a lossless correctness failure. Existing
`prods.toon` and `prodsOLD.toon` are decoded separately using an explicit legacy hint; differences
from the JSON snapshot are reported and are not silently treated as proof of round-trip correctness.

Generate a median comparison and existing-path regression verdict:

```bash
python3 benchmarks/DevOp.Toon.ProtocolBenchmarks/compare.py artifacts/toon-baseline.json artifacts/toon-candidate.json --output artifacts/toon-comparison.md
```

The comparator refuses mismatched source hashes or row counts and returns a nonzero exit status
when comparable existing paths regress above the budget, add allocations, or fail correctness.
Frozen legacy correctness is checked against the baseline's exact encoded and decoded hashes.
The baseline has an existing numeric precision loss at product 18; this remains a reported source
round-trip failure. New official and custom lossless modes must preserve the full normalized DTO model.

See [the full product evidence](../../Documentation/ProductBenchmarkResults.md) for measured
results, binary provenance, saved-fixture interpretation and validation limits.

## Compression comparison

Export exact UTF-8 payloads by adding `--export-payloads artifacts/toon-protocol-payloads` to each benchmark execution. Then run:

```bash
python3 benchmarks/DevOp.Toon.ProtocolBenchmarks/compression_benchmark.py artifacts/toon-protocol-payloads --output artifacts/toon-compression-comparison.json
```

The comparison uses installed system `libzstd`, `libbrotlienc`, `libbrotlidec`, the `zstd` CLI, and Python's standard-library gzip/zlib. These are benchmark-only prerequisites; no production package dependency is added. Native timings use single-threaded one-shot calls with fresh codec state and buffer/result allocations. CLI zstd output is separately verified against every original input. This is not a .NET transport benchmark or evidence of HTTP negotiation support.

See [the measured compression report](../../Documentation/CompressionComparison.md) for versions, settings, results, and limits.

## Original JSON preservation

Use the [raw JSON round-trip checker](../DevOp.Toon.RawJsonRoundTrip/README.md) to validate all original properties without DTO projection. It is a separate correctness check and does not add serializer timing or allocation evidence. Raw machine reports are retained locally in ignored output directories rather than published with the source.
