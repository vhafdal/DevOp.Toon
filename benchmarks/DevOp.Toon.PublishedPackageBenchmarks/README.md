# Benchmark published TOON packages

This entry point uses NuGet packages, not the repository's runtime or Core projects. It links the
existing protocol harness and Product DTO source, targets .NET 10 and is not packable. Defaults
are DevOp.Toon 0.3.0 plus Core 0.2.0. Selecting runtime 0.2.9 automatically selects Core 0.1.3 and
the harness's `BASELINE` branch; `CorePackageVersion` can be overridden explicitly.

Run these commands sequentially from the repository root with the .NET 10 SDK. Each version gets
a fresh forced restore and a separate output directory. No local-project override is needed.

```bash
dotnet restore benchmarks/DevOp.Toon.PublishedPackageBenchmarks/DevOp.Toon.PublishedPackageBenchmarks.csproj -p:RuntimePackageVersion=0.2.9 --force --source https://api.nuget.org/v3/index.json
dotnet build benchmarks/DevOp.Toon.PublishedPackageBenchmarks/DevOp.Toon.PublishedPackageBenchmarks.csproj -c Release --no-restore -p:RuntimePackageVersion=0.2.9 -o artifacts/published-before

dotnet restore benchmarks/DevOp.Toon.PublishedPackageBenchmarks/DevOp.Toon.PublishedPackageBenchmarks.csproj -p:RuntimePackageVersion=0.3.0 --force --source https://api.nuget.org/v3/index.json
dotnet build benchmarks/DevOp.Toon.PublishedPackageBenchmarks/DevOp.Toon.PublishedPackageBenchmarks.csproj -c Release --no-restore -p:RuntimePackageVersion=0.3.0 -o artifacts/published-after

mkdir -p artifacts/published-results
env DOTNET_TieredCompilation=0 dotnet artifacts/published-before/DevOp.Toon.PublishedPackageBenchmarks.dll --data benchmarks/DevOp.Toon.Benchmarks/TestData --profiles custom-legacy-lossless --warmups 30 --repeats 50 --output artifacts/published-results/before-custom-lossless.json
env DOTNET_TieredCompilation=0 dotnet artifacts/published-after/DevOp.Toon.PublishedPackageBenchmarks.dll --data benchmarks/DevOp.Toon.Benchmarks/TestData --profiles custom4.4-lossless --warmups 30 --repeats 50 --output artifacts/published-results/after-custom-lossless.json
```

The baseline lossless command returns exit code 2 for its known numeric precision defect. Inspect
the report's `Error`, row count and `RoundTripDifference`; do not treat every exit 2 as acceptable.
The commands are separate so that expected baseline failure does not silently skip the new run.

Other baseline profiles are `official3-equivalent` and `custom-legacy-compact`. New profiles are
`official4.4-lossless` and `custom4.4-compact`. Add `--measure-auto` to a new-profile run to collect
separate `AutoDecode` samples and verify that the decoded model hash matches explicit decoding.
Use one profile per fresh process for matched comparisons, repeat in reverse order, and save each
run to a new filename. A multi-profile descriptive run has additional shared-pool history and must
be labeled accordingly. Confirm the loaded assembly path/hash in each report and package versions
in the corresponding output `.deps.json`; the shared harness's `Build` labels are profile labels,
not independent proof of package provenance.

## Measurement scope

All 2,051 original products are loaded into the same Product DTO model. That projection omits the
original one-row `Publication` property and normalizes date representations. Typed-model hashes
do not prove preservation of every raw JSON member. Old runtime 0.2.9 also rounds row 18's quantity
from `-296.33000000000004` to `-296.33` through its G16 formatting; this is reported rather than
hidden. New lossless profiles must preserve the normalized model. Compact omission profiles are
intentionally lossy and are compared separately.

Both executables use Release/optimized harness code and the published package assemblies. The
commands explicitly disable tiered compilation and use 30 warmups plus 50 timed samples; report
the actual runtime/CPU configuration with results. No forced GC occurs between samples. Managed
heap deltas are uncollected changes, not retained memory. Separate untimed retained-memory probes
can alter shared pools. Process peak memory includes setup and is cumulative. Compression and
correctness checks are outside timing. This measures typed string encoding/decoding, not HTTP,
network or stream-write performance, and establishes no blanket claim for other workloads.

See [the shared harness](../DevOp.Toon.ProtocolBenchmarks/README.md) for report fields and
[the benchmark evidence](../../Documentation/ProductBenchmarkResults.md) for interpretation limits.
