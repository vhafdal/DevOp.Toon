# Protocol release validation — 2026-11-06

This record covers prepublication validation for Core 0.2.0, runtime 0.3.0, API 0.2.0 and
Client 0.2.0. Source and Release-package checks use isolated worktrees and local package storage.

| Check | Observed result |
| --- | --- |
| Runtime tests, .NET 10 | 3,799 passed, 4 skipped; 0 failed, including stream Unicode/cancellation and property-key/metadata regressions |
| API tests, .NET 10 | 51 passed; 0 failed, including MVC/TestServer version/status/body-boundary and changed-header snapshot cases |
| Client tests, net8 target | 36 passed; 0 failed; executed with major roll-forward on installed .NET 10 |
| Core build | netstandard2.0, net8.0, net9.0 and net10.0 passed |
| Runtime build | netstandard2.0, net8.0, net9.0 and net10.0 passed |
| API build | net8.0 and net10.0 passed |
| Client build | netstandard2.0, net8.0, net9.0 and net10.0 passed |
| Spec generator build | Passed |
| Existing-path performance | All three isolated 30-warmup/30-sample comparisons passed |
| Runnable protocol sample | Build passed with zero warnings; official/custom nested round trips and explicit version hint passed |
| Original raw JSON DOM check | Both lossless modes pass all 2,051 original rows/properties, including one-row Publication member |
| Repository benchmark helpers | HTTP, stream, property-key, transport-options and raw-JSON helpers build; Product-based helpers retain existing DTO nullable warnings |

All builds and tests ran sequentially, one at a time. Build success for net8/net9 does not establish execution
on native .NET 8/9 runtimes; those runtimes were unavailable. No coverage percentage or live
production interoperability is claimed. TestServer fixtures emit the .NET 10 obsolete-WebHost
warning; production API paths do not introduce that hosting API.

Compiler/test logs and detailed validation reports are retained locally and excluded from the
public repository. This page preserves the reviewed summary.

The official protocol suite uses 3,093 vendored cases across pinned 3.0 and 4.0–4.4 snapshots;
commit references and fixture-only upstream attribution are in
[ProtocolFixtures](../tests/DevOp.Toon.Tests/ProtocolFixtures/). Manual regressions cover
typed/DOM paths, skipped-field validation, UTF-8 policies, one-shot enumeration, explicit option
cloning, untrusted declared counts, JSON interoperability and HTTP version metadata.
Further full-dataset and review regressions cover nested table-context restoration, logical
hyphen-header depths, mixed spaces/tabs and non-strict recovery, invalid indentation options,
single evaluation of nullable getters, finite recursive metadata and public getter/indexer rules.

An independent static review found no new correctness/security issue in the retained decoder
optimizations. It checked quote-aware search, unkeyed colon-prefix classification, target-type
rebinding and exact wire-key duplicate tracking. The review also confirmed that timed benchmark
samples contain no forced collection and warned against extrapolating a single profile's result
to the entire gate. The final separate reports cover all three existing profiles. Earlier
unknown-field validation, allocation-boundary, invalid-client-UTF8 and default-stream-policy
findings were fixed with regressions before the final test run.

See [ProductBenchmarkResults.md](ProductBenchmarkResults.md) for raw binary/source provenance,
the preserved legacy numeric defect, successful existing-path gates and the slower-decode versus
lower-allocation tradeoff of the new custom grammar. See
[CompressionComparison.md](CompressionComparison.md) for benchmark-only Zstandard measurements.

The [runnable sample](../samples/ProtocolVersions/README.md) built with zero warnings and executed
successfully. Both official/custom typed nested DTOs and arrays were preserved with lossless
options, and ambiguous comment syntax decoded using the supplied 4.4 body version. Its build/run
logs and the final TRX counter summaries are retained locally and excluded from the public
repository.

The follow-up pass implements request-local option reuse with changed-header invalidation,
once-per-response client metadata resolution, cached CLR property keys and exact v4 UTF-8
rental sizing. Both multi-value header backing-array mutation and metadata-normalized aliases
have behavioral regressions. Legacy modes retain their original byte-rental strategy after the
exact-count experiment did not consistently meet the existing-path CPU budget. Raw validation
and benchmark artifacts stay local in ignored directories; reusable probes remain under
`benchmarks/`.

Release-package builds and tests also pass using an isolated NuGet package cache and local feed,
without Debug project references. Nuspec dependency inspection confirms Core 0.2.0 and runtime
0.3.0 throughout the family. The release gate compiles and verifies actual TRX output and rejects
main-branch publication, wrong versions, dirty source, absent tests, wrong package commits,
unmerged tags and lightweight tags. HTTP indentation overrides are bounded before body reads;
the client now transmits integer widths. These fixes have source and Release regression coverage.
