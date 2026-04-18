# Benchmarks and API Test Host

Use these projects when validating performance-sensitive changes or transport-facing behavior.

## Benchmarks project

Location:

- `benchmarks/DevOp.Toon.Benchmarks`

Useful files:

- `Program.cs`
- `ProductSerializationBenchmarks.cs`
- `ProductDecodeProfiler.cs`
- `ProductNestedProfilers.cs`
- `NestedShapeProfiler.cs`
- `ServiceEncodeProfiler.cs`
- `BenchmarkReportExporter.cs`

## Important benchmark notes

`Program.cs` overrides static defaults before running benchmarks. That means benchmark behavior is not necessarily using the same defaults as the package defaults everywhere else.

Current benchmark setup includes:

- `KeyFolding = Safe`
- `Delimiter = TAB`
- `Indent = 2`
- `ObjectArrayLayout = Columnar`
- decode `ExpandPaths = Safe`

Keep that in mind when comparing benchmark output with package-level examples or tests.

## API test host

Location:

- `src/DevOp.Toon.API.TestHost`

Purpose:

- local formatter and response checks
- transport-oriented validation
- API profiling support

Important files:

- `Program.cs`
- `Controllers/ProductsController.cs`
- `Services/ProductDataService.cs`

The host wires in `DevOp.Toon.API` and configures TOON output for product data using a columnar profile.

## When to use which tool

- benchmark project: serializer performance, size, allocation, benchmark report updates
- API test host: formatter integration, response shape, transport profiling, local end-to-end behavior
