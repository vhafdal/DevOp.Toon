# Performance and correctness review — 2026-11-06

This review records prepublication work for runtime 0.3.0, covering encoding/decoding,
detection, options, HTTP integration and benchmark methodology. It does not claim that every
possible workload is optimal. Changes are retained for demonstrated memory savings, corrected behavior or removal of
small, directly identified repeated work; speculative gains are not presented as measured wins.

## Retained changes

* Nullable CLR emitters now evaluate a property once before testing/accessing its nullable value.
  Previously a getter returning `42` then null could throw on its second evaluation. The compiled
  local adds no per-value heap storage, and regression tests cover both new modes.
* Typed header fields bind members once per target type within a reader. Wire-name duplicate
  checks remain unchanged, and mixed DTO/ignored/dictionary paths keep their general behavior.
* Quote-aware delimiter search uses a vectorized ordinary-prefix search. Unkeyed table rows search
  for a structural colon only before their first unquoted delimiter.
* Object spill fields use source spans instead of copying a complete line and its value substring.
  A bounded reader-local cache reuses successful exact headers with no inline values. Its key
  includes the full raw count, delimiter and field syntax; failures and context checks are not
  cached. Limits are 256 entries, 4,096 characters per entry and 32,768 total key characters.
* Together, span fields and exact header reuse reduce new custom lossless decode allocation from
  15.04 MB to 10.24 MB per complete 2,051-product operation. A controlled same-profile comparison
  measured 43.69 versus 44.45 ms, approximately 1.7% slower, so this is an allocation improvement,
  not an established speedup. Other runs vary with host conditions and must not be attributed to
  one edit simply because their elapsed times differ.
* Encoder type plans validate only nested object columns. Indentation uses the existing bulk-fill
  writer operation with the same LF setting. These remove identifiable repeated work; changing
  host load also changed unchanged decode timings during their experiment, so no exact speedup
  percentage is attributed to these edits.
* Explicit-version option detection inspects indentation without classifying every value again.
  Legacy `#` rows contribute indentation as data; v4 comments are ignored. Automatic protocol
  detection retains ambiguity checks. There is no public detection flag or source retained by
  options. Regression cases cover legacy rows, comments, quoted hashes, tabs and CRLF.
* Default Auto detection restores parent table contexts after nested spill tables and accounts for
  named headers carried on list-item hyphens. A lightweight indentation prepass establishes the
  final unit before feature classification, including non-strict depth jumps. One parsed-line list
  is reused by the parser; this is not a claim of a single physical source pass. Feature depths
  match the reader's spaces/unit-plus-tabs recovery, while raw lines remain available for strict
  validation. Tabs before `#` remain data, and blank/tab-led lines do not distort indent inference.
* CLR metadata construction now bounds recursive type expansion on its cold cache-building path.
  Finite self/mutually recursive values can be serialized without a metadata stack overflow.
  Indexers and private getters are excluded before getter compilation. Existing warm cache lookups
  add no per-operation recursion set, and public setter decoding remains independent.

The actual Client metadata path was compared using the same compiled client, harness and full
payload, replacing only the runtime binary. With 20 warmups and 50 samples, the earlier runtime
measured 46.83 ms and 21,999,280 allocated bytes; cheap explicit-version indentation measured
34.94 ms and 21,998,320 bytes. Both decoded-model hashes match. This is approximately 25.4%
less elapsed time in the in-memory-handler request/decode scenario, not a network or general
HTTP throughput claim. The original HTTP buffering/cancellation policy is unchanged. Binary
hashes and raw samples are retained locally and excluded from the public repository.

## Rejected experiments

The string-property text setter and vectorized surrogate precheck did not establish a repeatable
throughput improvement and were reverted. Their Unicode boundary tests remain.

A fused expression setter was also proven to compile with span parameters and preserve parser
and user-setter exception boundaries. Its controlled 30-warmup/50-sample comparison measured
33.82 ms before versus 33.93 ms after, with identical 10,241,608-byte allocation and correct
models. The 0.3% slowdown establishes no gain, so the experiment was reverted. Focused null,
parser-failure and exactly-once setter-exception regressions remain; no fused-setter runtime
dependency or framework-specific path is retained.

The full-payload HTTP reading probe produced the following results for the same 2,939,907-byte
lossless payload and identical decoded-model hash:

| Prototype | Median ms | Allocated bytes | Decision |
| --- | ---: | ---: | --- |
| Buffered byte copy + strict UTF-8 | 35.40 | 21,997,040 | Existing production behavior |
| Buffered `StreamReader` | 36.27 | 24,975,408 | Reject: about 3 MB more allocation |
| Public memory-buffer view/fallback | 35.08 | 21,997,176 | Reject: buffer was not publicly visible; fallback adds no gain |
| Headers-only pooled reading | 37.48 | 16,117,424 | Reject: slower and changes completion guarantees |

These are body-reading/typed-decoding probes through a buffered in-memory `HttpClient` handler,
not network or end-to-end server results. The pooled mode uses a different completion option and
cannot replace production reading without preserving total timeout, cancellation,
`CancelPendingRequests`, actual-byte limits and UTF-8/BOM behavior. Lower GC allocation alone
does not establish lower retained pool memory or better latency. No production HTTP reading
policy was changed on the strength of this probe.

Raw reports are retained locally and excluded from the public repository. The
[reproducible probe](../benchmarks/DevOp.Toon.HttpPayloadProbe/README.md) includes the real Client
and prototype modes.

## Retained stream UTF-8 rental improvement

Fast synchronous and asynchronous v4 stream encoding now size the rented byte buffer with the
exact UTF-8 byte count. They retain BOM-free encoding, one destination write, cancellation
forwarding and buffer disposal. Seven regression cases cover all four principal profiles,
multibyte/surrogate characters around large buffer boundaries, legacy replacement behavior,
pre-cancellation and destination failures.

On all 2,051 normalized Product DTOs, the destination-visible byte-array rental decreases from
16,777,216 to 4,194,304 bytes: 75% smaller, without changing output bytes. The controlled full
stream run measured 22.047 → 22.430 ms synchronously (+1.7%) and 22.570 → 22.214 ms asynchronously
(-1.6%). These are memory savings within the 5% whole-operation time budget, not a claimed speedup.
The isolated UTF-8 probe measured 0.47065 → 1.0665 ms, showing the approximately 0.6 ms extra
counting-pass cost rather than hiding it in the encoder total.

The comparison uses optimized Debug runtime binaries `3F6054F7...` and `11EE...`, the same compiled
probe, .NET 10, `DOTNET_TieredCompilation=0`, 30 warmups and 50 samples. The candidate includes other
encoder changes, so full-operation CPU differences cannot be attributed solely to buffer sizing.
An initial comparison used a mismatched optimization build and was excluded. Reports and that
failed experiment remain in ignored local evidence storage. The measured rental capacity is not
a retained-heap estimate: shared pools may trim, and process peaks include setup. Reproduction
and isolated modes are in [the stream probe](../benchmarks/DevOp.Toon.StreamBufferProbe/README.md).

Final guarded-runtime stream verification uses the same compiled probe and optimized runtime
settings for each pair (30 warmups, 50 samples, tiered compilation disabled). All four pairs
preserve output hashes and single-write behavior, add no per-operation allocation, and stay
within the 5% CPU budget. V4 timings include the property-key optimization, so improvements
are not attributed to byte counting alone.

| Profile and operation | Before ms | Final ms | Change | Final byte-rental capacity |
| --- | ---: | ---: | ---: | ---: |
| custom-legacy, sync | 20.428 | 20.797 | +1.8% | 16,777,216 |
| custom-legacy, async | 20.381 | 20.546 | +0.8% | 16,777,216 |
| custom-4.4, sync | 20.012 | 19.146 | -4.3% | 4,194,304 |
| custom-4.4, async | 20.776 | 19.943 | -4.0% | 4,194,304 |

## Additional optimization results

The additional paths were implemented and measured:

* CLR plans cache encoded property names for scalar fields, table headers, child objects, arrays
  and spills. Six small-payload cases improve by 14.6–38.5%, with unchanged per-operation
  allocation and identical output hashes. Paired processes use the same optimized Debug compiler
  settings, disable tiered compilation, and run 30 warmup blocks and 30 samples of 1,000 operations.
  This adds per-type key-plan storage; caller dictionary keys and payload values are not cached.
  Empty/null omission and Unicode regressions cover the CLR's normalization of source strings
  in custom-attribute metadata. See the
  [property-key probe](../benchmarks/DevOp.Toon.PropertyKeyProbe/README.md).
* MVC early validation prepares a request-local option snapshot. The formatter reuses it only
  while all option headers still match; changed headers are resolved and validated again.
  Multi-value backing arrays are cloned so in-place mutation cannot bypass validation.
  Requests without early version validation do not allocate a snapshot. The small-payload
  comparison reduced median allocation by approximately 82 bytes per request; latency was
  effectively unchanged (4.64 versus 4.61 ms per 1,000 operations).
* The client resolves response options once and shares its owned clone across UTF-8 reading,
  typed decoding, JSON fallback and lazy error decoding. Version-header enumeration avoids an
  intermediate array; JSON responses skip unused TOON option cloning. The small-payload probe
  reduced median allocation by approximately 106 bytes per request, with 5.85 versus 5.63 ms per
  1,000 operations. HTTP completion, cancellation and timeout policy remain unchanged. The
  [transport-options probe](../benchmarks/DevOp.Toon.TransportOptionsProbe/README.md) measures
  the real formatter/filter and client paths, using an in-memory handler and 30 samples of
  1,000 requests after 1,000 warmups. Only transport assemblies change between paired processes;
  the runtime and harness stay fixed, with tiered compilation disabled.

The new v4 modes use the smaller UTF-8 rentals described above. Legacy versions retain their
existing worst-case rental strategy: the exact-count experiment produced inconsistent async
regression results (+9.4% in one pair, +3.6% in the reverse-order repeat), so it did not establish
compliance with the 5% legacy budget. Both reports are retained locally. Chunked output was
unnecessary for the measured v4 memory reduction and would change the single-write behavior.
The headers-only HTTP prototype remains rejected: its measured latency was worse and its
completion mode changes timeout/cancellation guarantees. These decisions are resolved trade-offs,
not unfinished release tasks.

Automatic detection is measured separately with `--measure-auto`. All three final new-profile
Auto model hashes match explicit decode. The first full-product Auto experiment exposed lost
parent-table context and failed with a version-hint error despite explicit decoding succeeding;
that failed report is retained. Context/depth corrections have dedicated regressions, including
mixed whitespace. The retained runtime's fresh custom/legacy pair still shows approximately
10.7% slower new-custom decoding with 42% lower allocation; no claim is made that the CPU gap
was eliminated. Independent static reviews have no outstanding correctness findings in the
retained changes. The existing-path regression
gate and its isolated methodology are documented in
[ProductBenchmarkResults.md](ProductBenchmarkResults.md). Earlier failed exploratory runs remain
available alongside successful isolated comparisons.
