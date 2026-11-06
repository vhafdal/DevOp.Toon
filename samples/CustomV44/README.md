# Official and custom 4.4 sample

This .NET 10 console sample uses the published **DevOp.Toon 0.3.0**, **API 0.2.0** and
**Client 0.2.0** packages, with their **Core 0.2.0** dependency. It does not use local runtime
project references or require a running HTTP server.

From the repository root:

```bash
devop-build-slot dotnet restore samples/CustomV44/CustomV44.csproj --source https://api.nuget.org/v3/index.json
devop-build-slot dotnet build samples/CustomV44/CustomV44.csproj -c Release --no-restore -v minimal
devop-build-slot dotnet samples/CustomV44/bin/Release/net10.0/CustomV44.dll
```

The sample prints actual official and custom output for the same products. It checks complete
typed-model round trips, nested header groups shared by both formats, custom keyed rows, and
null/empty preservation with both omission options disabled. It also registers MVC and Client
options and verifies the configured runtime service. No HTTP request is sent; registration and
local codec checks are not end-to-end HTTP validation.

The small sample prints wire layouts and verifies behavior. It does not measure performance.
See [the dedicated custom 4.4 guide](../../Documentation/CustomV44.md) for profile selection,
copy/paste configuration, HTTP headers, measured benefits and compatibility tradeoffs.
