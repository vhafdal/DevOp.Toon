# Protocol version sample

This runnable .NET 10 sample uses the local runtime project. It round-trips typed nested DTOs
and arrays with official 4.4 and custom 4.4, explicitly preserving null/empty values. It also
detects ambiguous comment syntax and applies a supplied message body version before decoding.

From the repository root, use the normal Debug local Core project override when testing the
unreleased enum/API together with its Core worktree:

```bash
dotnet build samples/ProtocolVersions/ProtocolVersions.csproj -c Debug -p:ToonCoreProjectPath=/absolute/DevOp.Toon.Core/DevOp.Toon.Core.csproj -v minimal
dotnet samples/ProtocolVersions/bin/Debug/net10.0/ProtocolVersions.dll
```

Each round trip checks the complete nested array values and throws if data is lost. The sample
prints the selected transport version names and both wire layouts. See
[the version guide](../../Documentation/ProtocolVersions.md) for HTTP metadata and migration.
