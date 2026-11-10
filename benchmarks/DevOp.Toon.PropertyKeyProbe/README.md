# Property-key encoding probe

Measures repeated small CLR payload encoding for plain aliases, quoted aliases, and eight complex rows in official 4.4 and custom 4.4 modes. Each profile uses 30 warmup batches and 30 measured batches of 1,000 encodes. Setup, output hash calculation, and correctness comparisons stay outside timing. No forced garbage collection runs inside measured batches.

Build the same probe source against separate before/after runtime worktrees, using the same optimization and runtime settings. Debug project references permit local Core substitution through `DEVOP_TOON_CORE_CSPROJ`. The runtime project defaults to this checkout's library and can be overridden:

```bash
dotnet build benchmarks/DevOp.Toon.PropertyKeyProbe/DevOp.Toon.PropertyKeyProbe.csproj -c Debug -p:Optimize=true -p:RuntimeProject=/path/to/before/src/DevOp.Toon/DevOp.Toon.csproj -o /tmp/toon-key-before
dotnet build benchmarks/DevOp.Toon.PropertyKeyProbe/DevOp.Toon.PropertyKeyProbe.csproj -c Debug -p:Optimize=true -p:RuntimeProject=/path/to/after/src/DevOp.Toon/DevOp.Toon.csproj -o /tmp/toon-key-after
DOTNET_TieredCompilation=0 dotnet /tmp/toon-key-before/DevOp.Toon.PropertyKeyProbe.dll /tmp/toon-key-before.json
DOTNET_TieredCompilation=0 dotnet /tmp/toon-key-after/DevOp.Toon.PropertyKeyProbe.dll /tmp/toon-key-after.json
```

Run separate processes and repeat in reversed order before attributing a timing change. Reports include runtime identity, library SHA256, output SHA256, payload length, raw per-operation timing, allocation, and sample settings. Compare matching output hashes and allocations before interpreting median timing. Stopwatch overhead is included once per batch and is identical in both runs. This probe measures warmed string encoding; it does not establish cold-cache, transport, stream, or full-product performance.
