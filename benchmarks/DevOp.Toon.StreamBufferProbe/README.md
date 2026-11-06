# Stream UTF-8 rental probe

This standalone probe uses all 2,051 normalized Product DTOs with lossless omission settings.
The optional fifth argument selects a concrete wire protocol, defaulting to `custom-4.4`.
Use `custom-legacy` or `4.4` to measure those stream paths; the report records `ProtocolVersion`.
`library-sync` and `library-async` measure the full encoder writing to a nonbuffering destination.
`convert-max` and `convert-exact` isolate the byte-counting/transcoding/rental choice using the same
pre-encoded characters; this avoids attributing other encoder changes to UTF-8 sizing.

```bash
devop-build-slot dotnet build benchmarks/DevOp.Toon.StreamBufferProbe/DevOp.Toon.StreamBufferProbe.csproj -c Debug -p:Optimize=true -p:ToonCoreProjectPath=/absolute/core/DevOp.Toon.Core.csproj -v minimal
devop-build-slot dotnet benchmarks/DevOp.Toon.StreamBufferProbe/bin/Debug/net10.0/DevOp.Toon.StreamBufferProbe.dll convert-max /absolute/prods.json 30 50
devop-build-slot dotnet benchmarks/DevOp.Toon.StreamBufferProbe/bin/Debug/net10.0/DevOp.Toon.StreamBufferProbe.dll convert-exact /absolute/prods.json 30 50
devop-build-slot dotnet benchmarks/DevOp.Toon.StreamBufferProbe/bin/Debug/net10.0/DevOp.Toon.StreamBufferProbe.dll library-sync /absolute/prods.json 30 50 custom-legacy
```

Run modes in fresh processes, sequentially, and repeat in reverse order. Compare encoded/input
hashes and loaded runtime assembly hashes before timings. Use the same compiler optimization
settings for baseline and candidate; recorded measurements set `DOTNET_TieredCompilation=0` for
both processes. The destination reports the actual backing byte-array capacity passed to
each write, including asynchronous memory writes. That is the pool bucket required by this
operation, not proof of a retained heap size: shared pools can trim. Uncollected heap and cumulative
process peak include setup and are reported separately. No forced GC occurs between timed samples.
All output is checked against BOM-free UTF-8 before measurement. The source fixture is unchanged.
