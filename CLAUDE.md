# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

`DevOp.Toon` is a .NET runtime package for TOON (Token-Oriented Object Notation), a token-efficient alternative to JSON optimized for compact structured payloads. Current version: `0.2.7`. Published to NuGet as `DevOp.Toon`; depends on `DevOp.Toon.Core`.

## Common Commands

All commands run from the repository root. The solution file is `DevOp.Toon.slnx`.

```bash
dotnet restore DevOp.Toon.slnx
dotnet build DevOp.Toon.slnx
dotnet test DevOp.Toon.slnx
dotnet test DevOp.Toon.slnx --collect:"XPlat Code Coverage"
dotnet test DevOp.Toon.slnx --filter "FullyQualifiedName~MyTestClass"
dotnet pack src/DevOp.Toon/DevOp.Toon.csproj -c Release
dotnet format
```

Run benchmarks from `benchmarks/DevOp.Toon.Benchmarks/`:

```bash
dotnet run -c Release --project benchmarks/DevOp.Toon.Benchmarks
```

### Spec-Generated Tests

Tests under `tests/DevOp.Toon.Tests/GeneratedTests/` are auto-generated from the TOON spec (branch `v3.0.0`). Regenerate after any encode/decode behavior change:

```bash
./specgen.sh   # Linux/macOS
./specgen.ps1  # Windows
```

### Local Cross-Repo Development

Debug builds swap NuGet references for local project references via env vars:

| Variable | Points to |
|---|---|
| `DEVOP_TOON_CORE_CSPROJ` | `DevOp.Toon.Core/DevOp.Toon.Core.csproj` |

Set the variable before running `dotnet build -c Debug`. Release builds always use NuGet.

## Architecture

### Public API (`src/DevOp.Toon/`)

- `ToonEncoder` — static entry point; uses `ToonEncodeOptions`. Default options: columnar layout, comma delimiter, key folding off, nulls/empties ignored.
- `ToonDecoder` — static entry point; uses `ToonDecodeOptions`.
- `ToonService` / `IToonService` — wraps both encoders with shared `ToonServiceOptions`; registered via `AddToon(...)` DI extension.
- `ToonNode` — the untyped node graph returned by schema-free decoding.
- `Options/` — `ToonEncodeOptions`, `ToonDecodeOptions`, `ToonServiceOptions`.

### Internal Encoding Pipeline (`Internal/Encode/`)

1. `NativeNormalize` — reflects CLR types, applies `ToonPropertyNameAttribute`, caches property metadata.
2. `ClrEncodePlanCache` / `ClrEncodePlan` — builds and caches per-type reflection plans for the columnar fast path. Returns `null` for non-plain-object types.
3. `NativeEncoders` — writes primitives and collections.
4. `NativeNodes` — encodes `ToonNode` graphs.
5. `LineWriter` / `CompactBufferWriter` — output writers for indented and compact modes.
6. `ClrObjectArrayFastEncoder` — fast path for uniform object arrays using the cached plan.

### Internal Decoding Pipeline (`Internal/Decode/`)

1. `Scanner` — tokenizes raw TOON text into lines/tokens.
2. `Parser` — produces `ArrayHeaderInfo` / `ArrayHeaderParseResult` and drives the decode tree.
3. `TypedDecoder` — reconstructs strongly typed CLR objects.
4. `NativeDecoders` — converts individual TOON tokens to BCL types.
5. `NativeTypedMaterializer` / `DirectMaterializer` — materialize nodes into typed instances.
6. `NativePathExpansion` — expands dot-notation and bracket-notation path keys.
7. `Validation` — enforces structural rules during decode.

### Shared Utilities (`Internal/Shared/`)

`FloatUtils`, `NumericUtils`, `LiteralUtils`, `StringUtils`, `JsonTextConverter`, `ToonNodeConverter`, `ValidationShared`.

### Test Host (`src/DevOp.Toon.API.TestHost/`)

Minimal ASP.NET Core host for integration and profiling scenarios. Not shipped as a package.

### Test Layout (`tests/DevOp.Toon.Tests/`)

- `ManualTests/` — hand-written tests for regressions, options, service integration, and BCL scalar round-trips.
- `GeneratedTests/Encode/` — spec-derived encode fixtures: `Primitives`, `Objects`, `ArraysPrimitive`, `ArraysTabular`, `ArraysNested`, `ArraysObjects`, `KeyFolding`, `Delimiters`, `Whitespace`, `LegacyEncodeOptions`.
- `GeneratedTests/Decode/` — spec-derived decode fixtures: matching categories plus `PathExpansion`, `ValidationErrors`, `IndentationErrors`, `RootForm`, `BlankLines`, `Numbers`.

Coverage target: 85%+ line coverage on `DevOp.Toon`.

### Target Frameworks

Library targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Tests run on `net8.0;net9.0;net10.0` in CI; locally also `net481` on Windows. Controlled by `Directory.Build.props`.

## Before Committing

Follow the full workflow defined in `AGENTS.md`.

### Key Responsibilities

- Inspect staged changes before committing (`git diff --staged`)
- Identify whether any **packable projects** (`.csproj` with `PackageId`) are affected
- Update `PackageReleaseNotes` in all relevant projects **if the change impacts package consumers**
- Keep release notes aligned with the actual change (no vague or invented entries)
- Stage updated `.csproj` files before committing

### Commit Message

- Use **Conventional Commit format** (`feat:`, `fix:`, `perf:`, `docs:`, etc.)
- Always reflect the **intent of the change**, not just modified files
- Include a **descriptive body** for non-trivial changes
- Base the message strictly on the staged diff

### DevOp.Toon Specific

- If encode/decode behavior changes:
  - Run `./specgen.sh`
  - Stage any regenerated tests or artifacts

### Signing

- Assembly signing uses `DevOp.snk`
- Do not modify or replace this file