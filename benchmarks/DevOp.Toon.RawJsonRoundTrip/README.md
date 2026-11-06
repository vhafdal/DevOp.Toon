# Original JSON preservation check

This is a one-shot correctness check, separate from the timed Product DTO benchmark. It reads
the original UTF-16 JSON and round-trips it through JSON DOM → official/custom 4.4 lossless TOON
→ JSON DOM. It includes unknown DTO members such as `Publication`.

The comparator checks every property, array position, string, boolean and null. Object order may
change with tabular encoding. Numeric text is normalized as an exact signed decimal coefficient
and exponent, so formatting changes are allowed without accepting rounding differences.

Build against an already-built runtime/Core directory, then supply the original fixture path:

```bash
devop-build-slot dotnet build benchmarks/DevOp.Toon.RawJsonRoundTrip/DevOp.Toon.RawJsonRoundTrip.csproj -p:RuntimeDirectory=/absolute/compiled/runtime-directory -v minimal
devop-build-slot dotnet benchmarks/DevOp.Toon.RawJsonRoundTrip/bin/Debug/net10.0/DevOp.Toon.RawJsonRoundTrip.dll /absolute/prods.json
```

The JSON report records input/runtime hashes, row counts, publication-member counts and the first
difference. A mismatch returns exit code 1. No timing or allocation conclusion is drawn from this
DOM check.

The default `RuntimeDirectory` points to the runtime project's Debug .NET 10 output under this checkout. Build the runtime and its Core dependency first, or override that directory to validate a specific compiled binary. The checker does not rebuild that binary.

Raw reports are retained locally and excluded from the public repository. The measured summary is in [ProductBenchmarkResults.md](../../Documentation/ProductBenchmarkResults.md); this checker adds no timing or allocation claims.
