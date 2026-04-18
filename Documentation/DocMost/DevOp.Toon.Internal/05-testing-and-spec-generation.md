# Testing and Spec Generation

The test strategy has two layers: hand-written regression tests and generated spec-alignment tests.

## Test projects

### `tests/DevOp.Toon.Tests`

Primary hand-written coverage. The `ManualTests` folder is where targeted regression tests live.

Useful files:

- `ToonEncoderTests.cs`
- `ToonDecoderTests.cs`
- `ToonServiceTests.cs`
- `HybridObjectArrayTests.cs`
- `CompactHybridRegressionTests.cs`
- `IgnoreNullOrEmptyTests.cs`
- `ExcludeEmptyArraysTests.cs`
- `JsonComplexRoundTripTests.cs`
- `ToonAsyncTests.cs`

### `tests/DevOp.Toon.SpecGenerator`

Generates checked-in tests from the TOON spec repository.

Important files:

- `Program.cs`
- `SpecGenerator.cs`
- `FixtureWriter.cs`
- `SpecSerializer.cs`

## Useful commands

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"
dotnet test --filter "FullyQualifiedName~ToonDecoder"
```

## Spec generation workflow

The repo provides `specgen.sh`, which:

1. builds the spec generator
2. pulls the TOON spec repo
3. writes generated tests into `tests/DevOp.Toon.Tests/GeneratedTests`

Current script target:

- spec repo branch `v3.0.0`

Use spec generation when:

- syntax compliance changes
- encode or decode behavior changes in spec-governed areas
- generated tests need refreshing after upstream spec changes

## Testing sharp edge

`DefaultOptionsCollection` disables parallelization for tests that mutate static defaults. If you touch:

- `ToonEncoder.DefaultOptions`
- `ToonDecoder.DefaultOptions`

make sure tests remain isolated and do not leak global state between runs.
