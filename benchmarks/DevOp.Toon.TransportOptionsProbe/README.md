# Transport option resolution probe

Measures the real MVC resource-filter/output-formatter path (`api`) and the real typed HTTP client (`client`) for a small payload. HTTP uses an in-memory handler; these are not network throughput results. Each process performs 1,000 warmups and 30 samples of 1,000 requests. Output values and response version are checked. Reports include process-wide allocated bytes; no forced collections occur between samples.

Build with Debug local project references, supplying `ApiProject`, `ClientProject`, `ToonProjectPath` and `ToonCoreProjectPath` as absolute project-file paths:

```bash
dotnet build benchmarks/DevOp.Toon.TransportOptionsProbe/DevOp.Toon.TransportOptionsProbe.csproj -c Debug -p:ApiProject="$API_PROJECT" -p:ClientProject="$CLIENT_PROJECT" -p:ToonProjectPath="$RUNTIME_PROJECT" -p:ToonCoreProjectPath="$CORE_PROJECT"
dotnet benchmarks/DevOp.Toon.TransportOptionsProbe/bin/Debug/net10.0/DevOp.Toon.TransportOptionsProbe.dll api > artifacts/options-api.json
dotnet benchmarks/DevOp.Toon.TransportOptionsProbe/bin/Debug/net10.0/DevOp.Toon.TransportOptionsProbe.dll client > artifacts/options-client.json
```

For comparisons, keep the same compiled harness and runtime binary, replacing only the API or Client assembly between separate process runs. Use matching compiler settings and repeat in reverse order. The API scenario includes early version validation and output formatting with version and indentation overrides. The client scenario includes UTF-8 reading and metadata-selected typed decoding. Internal filter activation by reflection is benchmark setup only.
