# Repository Guidelines

The single contributor guide for humans and coding agents (Claude Code reads it through
`CLAUDE.md`; `GEMINI.md` is a link to this file). Everything needed to build, test and change
this repository is public: a clone plus the .NET 10 SDK is enough. No private feed, machine,
wrapper or account is required.

## Project Structure

- `src/DevOp.Toon` — the `DevOp.Toon` NuGet package: `ToonEncoder`, `ToonDecoder`,
  `ToonService`/`IToonService` with `AddToon(...)` DI registration, `ToonNode`, and
  `Options/` (`ToonEncodeOptions`, `ToonDecodeOptions`, `ToonServiceOptions`). Internal code is
  grouped under `Internal/Encode`, `Internal/Decode` and `Internal/Shared`. It depends on the
  `DevOp.Toon.Core` package, which defines `ToonProtocolVersion` and the shared contracts.
- `src/DevOp.Toon.API.TestHost` — minimal ASP.NET Core host for integration and profiling. Not shipped.
- `tests/DevOp.Toon.Tests` — xUnit suite:
  - `ManualTests/` — hand-written regression, option, protocol-version and service tests.
  - `ProtocolFixtures/` — unmodified upstream conformance fixtures, one snapshot per spec tag,
    run by `VersionedProtocolFixtureTests` under the matching `ToonProtocolVersion`.
  - `GeneratedTests/` — older C# suites generated from the v3 spec; they pin `CustomLegacy` behaviour.
- `tests/DevOp.Toon.SpecGenerator` — the tool behind `specgen.sh` / `specgen.ps1`.
- `benchmarks/` — BenchmarkDotNet suite (`DevOp.Toon.Benchmarks`, with the checked-in product
  fixtures in `TestData/`) and focused probes, each with its own `README.md`.
- `samples/` — runnable `ProtocolVersions` (local runtime project) and `CustomV44` (published packages).
- `Documentation/` — protocol, version and benchmark notes. `Documentation/DocMost/` is a legacy
  export; do not treat it as current.

## Protocol Defaults

Verify against `src/DevOp.Toon/Options/` before relying on these; see `Documentation/ProtocolVersions.md`.

- Encoding defaults to official TOON 4.4 (`ToonProtocolVersion.V4_4`); decoding defaults to `Auto`.
- Supported versions: official `V3_0`, `V4_0` … `V4_4`, plus the custom profiles `CustomLegacy`
  (the pre-0.3.0 wire format) and `CustomV4_4` (grouped rows and spills; see `Documentation/CustomV44.md`).
- `ToonEncodeOptions` defaults: indent 1, comma delimiter, key folding off, byte arrays as base64.
  Object-array layout is `Auto` for official versions and `Columnar` for custom ones;
  `IgnoreNullOrEmpty` and `ExcludeEmptyArrays` are off for official versions and on for custom ones.

## Build, Test, and Development Commands

Run from the repository root:

```bash
dotnet build tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj     # builds the library too
dotnet test tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj
dotnet test tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj --filter "FullyQualifiedName~ToonEncoderTests"
dotnet test tests/DevOp.Toon.Tests/DevOp.Toon.Tests.csproj --collect:"XPlat Code Coverage"
dotnet pack src/DevOp.Toon/DevOp.Toon.csproj -c Release
./specgen.sh          # or ./specgen.ps1
```

The library targets `netstandard2.0`, `net8.0`, `net9.0` and `net10.0`. Tests run on
`net8.0;net9.0;net10.0` (plus `net481` on Windows outside CI), set in `Directory.Build.props`.
`DevOp.Toon.slnx` also lists sibling checkouts of `DevOp.Toon.Core` and `DevOp.Toon.API`
(`../DevOp.Toon.Core`, `../DevOp.Toon.API`); without them, build individual projects as above.
Prefer building or testing the one project you changed, and keep large build/test output in an
ignored directory such as `artifacts/` rather than pasting it.

`specgen.sh` / `specgen.ps1` refresh `tests/DevOp.Toon.Tests/ProtocolFixtures` from
https://github.com/toon-format/spec at the tags `v3.0.0`, `v4.0.0`, `v4.1.3`, `v4.2.1`, `v4.3.0`
and `v4.4.0`. Set `TOON_SPEC_REPOSITORY` to a local clone to work offline. Run them after any
encode/decode behaviour change and commit the regenerated files.

### Local cross-repository development

`Debug` builds can use a local checkout of `DevOp.Toon.Core` instead of the NuGet package: set the
`DEVOP_TOON_CORE_CSPROJ` environment variable, or pass `-p:ToonCoreProjectPath=...`, to the path of
`DevOp.Toon.Core.csproj` (`DEVOP_TOON_API_CSPROJ` / `ToonApiProjectPath` does the same for the
test host's API package). Leave the variable unset to use the NuGet package. `Release` builds
always use NuGet packages. Never commit machine-specific paths.

## Coding Style & Naming Conventions

Standard C# conventions: 4-space indentation, PascalCase for public types and members, camelCase
for locals and parameters, one type per file named after the type. Nullable reference types and
implicit usings are enabled; keep new code nullable-safe and avoid `dynamic` unless an API requires
it. Prefer small focused helpers inside the existing `Internal/*` folders over new cross-cutting
utility files. Public API carries XML documentation.

## Testing Guidelines

xUnit with `coverlet.collector`. Name test files after the feature under test plus `Tests`, for
example `ToonDecoderTests.cs`. A bug fix comes with a test that fails without it. Keep manual tests
for regressions, options and service integration; spec conformance belongs in `ProtocolFixtures`.
The line-coverage target is 85%+ on `DevOp.Toon` (see `CONTRIBUTING.md`).

## Documentation

Documentation is Markdown in this repository. `README.md` links to the published documentation
site. Update `README.md`, `src/DevOp.Toon/README.md` (the NuGet package readme) and the relevant
`Documentation/` files when package usage, protocol behaviour, benchmarks or development workflow change.

## Agent Configuration

`.claude/agents/` holds three generic Claude Code subagents that need nothing beyond this
repository: `toon-scout` (read-only search), `toon-build` (one focused build or filtered test run,
returning the verdict) and `toon-reviewer` (adversarial review of a finished change). Local agent
worktrees (`.claude/worktrees/`, `.worktrees/`) and `.claude/settings.local.json` are ignored.

## Git Commit Workflow (with Package Release Notes)

When committing:

1. Inspect the staged changes (`git diff --staged`). If nothing is staged, say so; do not guess.
2. Identify the intent and consumer impact of the change.
3. Find affected **packable projects** (a `.csproj` with `<PackageId>`; here `src/DevOp.Toon`).
4. Update `PackageReleaseNotes` in those projects when consumers are affected, and stage the `.csproj`.
5. If encode/decode behaviour changed, run `./specgen.sh` and stage the regenerated files.
6. Write a Conventional Commit message (`feat:`, `fix:`, `perf:`, `docs:`, `chore:`, …) based
   strictly on the staged diff, with a body explaining why for non-trivial changes.

### When to update `PackageReleaseNotes`

Update them **only if consumers are affected**: new features, bug fixes, behaviour changes,
performance improvements, serialization/protocol changes, compatibility changes (.NET targets,
dependencies), or documentation that changes usage. Do **not** update them for internal
refactoring, formatting, renames, test-only or repository-tooling changes.

### Release notes format

- First line: a short summary sentence, followed by 3–6 bullets.
- Each bullet starts with `Adds`, `Improves`, `Fixes`, `Breaking` (must be explicit) or
  `Updates` (meaningful dependency/platform updates only).
- Specific, consumer-focused, present tense, plain text (NuGet-friendly), no marketing language.

```xml
<PackageReleaseNotes>Improves TOON protocol handling and serialization consistency.
- Adds support for updated TOON 4.x structures
- Fixes edge cases in nested object serialization
- Improves compatibility across .NET target frameworks</PackageReleaseNotes>
```

### Signing

Assemblies are signed with `src/DevOp.Toon/DevOp.snk` (public signing). Do not modify or replace it.
