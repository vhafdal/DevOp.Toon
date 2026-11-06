# Compression comparison — 2026-11-06

This benchmark compares compression algorithms on identical exported UTF-8 bytes. It does not add Zstandard to the runtime, API, or Client packages and does not measure .NET HTTP latency or content negotiation.

The full 2,051-product new-custom lossless payload was 2,939,907 bytes, SHA-256 `2ea1f96313471e1b7fda963b15b44c2bbeefc9b6ed3b88f4edb439d9bdf492fb`. Five samples per operation produced these medians:

| Codec | Native compressed bytes | Compress ms | Decompress ms |
| --- | ---: | ---: | ---: |
| Gzip level 6 | 380,386 | 25.38 | 3.66 |
| Brotli quality 4 | 326,843 | 12.95 | 2.23 |
| Zstandard level 1 | 358,814 | 3.08 | 1.16 |
| Zstandard level 3 | 365,284 | 4.73 | 1.22 |
| Zstandard level 6 | 320,570 | 14.76 | 1.09 |

All decompressions reproduced their exact input bytes. The CLI independently verified all three Zstandard levels; CLI output sizes were 359,205, 365,288, and 320,574 bytes respectively. CLI framing/worker settings can produce different output from the native one-shot API, so CLI and native sizes are recorded separately. Levels do not guarantee a monotonic size improvement on every payload.

The run used installed Zstandard 1.5.5, zlib 1.3, and the installed Brotli library version identifier 16,781,312. Brotli used generic mode, quality 4, window 22. Zstandard used native levels 1/3/6. Gzip used Python's standard-library gzip/zlib at level 6 with deterministic timestamp zero.

Native timings exclude subprocess startup, include buffer/result allocations, and use fresh one-shot codec state. Python/ctypes and .NET API overhead differ; these results describe this algorithm harness, not application throughput. Peak memory was not measured by this compression script. Serializer allocation/retention measurements belong to the separate protocol benchmark.

The evidence covers every exported baseline/candidate profile and the same normalized DTO JSON. Compact profiles deliberately omit values and must be compared separately from lossless profiles. Compression evidence does not make a failing serialization regression gate pass.

Raw samples, versions, source hashes, and byte-equality results are retained locally and excluded from the public repository. Reproduce with the benchmark-only [compression script](../benchmarks/DevOp.Toon.ProtocolBenchmarks/compression_benchmark.py). Production transport integration remains a separate decision.
