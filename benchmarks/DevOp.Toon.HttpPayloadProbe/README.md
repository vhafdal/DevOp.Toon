# HTTP payload allocation probe

This benchmark uses all 2,051 products from an exported custom 4.4 lossless UTF-8 payload.
A real `HttpClient` uses an in-memory handler; there is no network, TLS or server measurement.
`client` and `clientmeta` call the real Client package. Other modes compare response-reading
prototypes followed by the same typed decoder:

| Mode | Body path |
| --- | --- |
| `client` | Existing Client package with explicit decode options |
| `clientmeta` | Existing Client package with response version metadata and detected indentation |
| `bytes` | Default buffered HTTP, `ReadAsByteArrayAsync`, strict UTF-8 decoding |
| `reader` | Default buffered HTTP, strict UTF-8 `StreamReader` |
| `buffer` | Default buffered HTTP, public `MemoryStream.TryGetBuffer` view or byte-copy fallback |
| `pool` | Experimental headers-only HTTP completion and bounded pooled byte reading |

The `pool` mode is a benchmark prototype. It does not establish compatible total-request timeout,
`CancelPendingRequests`, error/cancellation or pool-retention behavior. It is not used by production
Client code. Its growth follows actual received bytes, ignores declared content length, enforces
`MaxResponseContentBufferSize` and returns rented storage in `finally`.

Build the unreleased package worktrees together through their normal Debug reference overrides:

```bash
devop-build-slot dotnet build benchmarks/DevOp.Toon.HttpPayloadProbe/DevOp.Toon.HttpPayloadProbe.csproj -c Debug -p:ClientProject=/absolute/client/src/DevOp.Toon.Client/DevOp.Toon.Client.csproj -p:ToonProjectPath=/absolute/runtime/src/DevOp.Toon/DevOp.Toon.csproj -p:ToonCoreProjectPath=/absolute/core/DevOp.Toon.Core.csproj -v minimal
devop-build-slot dotnet benchmarks/DevOp.Toon.HttpPayloadProbe/bin/Debug/net10.0/DevOp.Toon.HttpPayloadProbe.dll bytes /absolute/export/candidate-custom4.4-lossless.toon
```

Run each mode in a fresh process, sequentially, and repeat in reverse order. Reports include 20
warmups, one full decoded-model SHA-256 probe and 50 timed samples. Compare model hashes before
comparing performance. Thread switches make process-wide allocated bytes appropriate for this
async probe. Heap snapshots are uncollected; process peak includes setup. Neither establishes
per-request retained memory. No forced GC occurs between samples.

See [the review and measured tradeoffs](../../Documentation/OptimizationReview.md).
