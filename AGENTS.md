# Repository Guidelines

## Project Structure & Module Organization

The main library lives in `src/DevOp.Toon`, with public runtime APIs such as `ToonEncoder`, `ToonDecoder`, options, and DI registration. Internal parser and encoder code is grouped under `Internal/Decode`, `Internal/Encode`, and `Internal/Shared`. `src/DevOp.Toon.API.TestHost` contains a minimal ASP.NET host used for integration and profiling scenarios. Tests live in `tests/DevOp.Toon.Tests`, split between `ManualTests` for hand-written coverage and `GeneratedTests` for TOON spec fixtures. Benchmark code and sample payloads live in `benchmarks/DevOp.Toon.Benchmarks`. Supporting docs are under `Documentation/`.

## Build, Test, and Development Commands

Run commands from the repository root:

```bash
dotnet restore DevOp.Toon.slnx
dotnet build DevOp.Toon.slnx
dotnet test DevOp.Toon.slnx
dotnet test DevOp.Toon.slnx --collect:"XPlat Code Coverage"
./specgen.sh
```

`dotnet build` compiles the multi-targeted library (`netstandard2.0`, `net8.0`, `net9.0`, `net10.0`). `dotnet test` runs the xUnit suite for the OS-supported test frameworks defined in `Directory.Build.props`. Use `specgen.sh` or `specgen.ps1` after changing encoding or decoding behavior so generated tests stay aligned with the TOON specification.

## Coding Style & Naming Conventions

Follow standard C# conventions: 4-space indentation, PascalCase for public types and members, camelCase for locals and parameters, and one type per file named after the type. Nullable reference types and implicit usings are enabled; keep new code nullable-safe and avoid `dynamic` unless the API requires it. Prefer small focused helpers inside the existing `Internal/*` folders rather than adding cross-cutting utility files.

## Testing Guidelines

This repository uses xUnit with `coverlet.collector`. Name test files with the feature under test plus `Tests` or `Manual`, for example `ToonDecoderTests.cs` or `ArraysObjectsManual.cs`. Add or update generated fixtures when behavior tracks the TOON spec, and keep manual tests for regressions, options, and service integration. Maintain high coverage; `CONTRIBUTING.md` sets the target at 85%+ line coverage.

## Documentation Process

Maintain documentation in Markdown inside this repository. Update `README.md`, `src/DevOp.Toon/README.md`, `Documentation/`, and `Documentation/DocMost/` when package usage, benchmarks, protocol notes, or development workflow change. Do not update Confluence as part of the workflow. If the DocMost site should reflect the change, call out the specific pages that need manual syncing.

## Commit & Pull Request Guidelines

Recent history follows short Conventional Commit style prefixes such as `fix:`, `docs:`, and `build:`. Keep subjects imperative and specific, for example `fix: align hybrid encoding output`. Pull requests should explain the behavior change, list affected frameworks or packages, link the relevant issue, and note any spec, benchmark, or documentation updates. Include sample payloads or screenshots only when the API test host or docs output changes visibly.
