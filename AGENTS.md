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

Maintain documentation in Markdown inside this repository where useful, but treat the live Wiki.js site as the published documentation source of truth. Update `README.md`, `src/DevOp.Toon/README.md`, and relevant `Documentation/` files when package usage, benchmarks, protocol notes, or development workflow change. Do not update Confluence as part of the workflow. Do not assume `Documentation/DocMost/` is current; it is legacy reference material unless the user explicitly asks to use it.

## Git Commit Workflow (with Package Release Notes)

When performing `git commit`, the agent must follow a **structured workflow** that ensures:
- accurate commit messages
- consistent `PackageReleaseNotes`
- alignment between code changes and published packages

---

## 1. Workflow Overview

When the user runs `git commit`, the agent must:

1. Inspect staged changes (`git diff --staged`)
2. Identify intent and impact of the change
3. Detect affected **packable projects** (`.csproj` with `PackageId`)
4. Update `PackageReleaseNotes` where relevant
5. Stage updated `.csproj` files
6. Generate a high-quality commit message
7. Perform the commit

If no files are staged:
- Do not guess
- Respond that there are no staged changes

---

## 2. Package Detection

A project is considered **packable** if:
- `.csproj` contains `<PackageId>`
- OR it is clearly part of a NuGet-distributed package

Only update release notes for:
- projects affected by the staged changes

---

## 3. When to Update `PackageReleaseNotes`

Update `PackageReleaseNotes` **only if the change impacts consumers**, including:

- New features or capabilities
- Bug fixes
- Behavior changes
- Performance improvements
- Serialization / protocol changes (important for DevOp.Toon)
- Compatibility changes (.NET targets, dependencies)
- Documentation that affects usage

Do **NOT** update if:
- purely internal refactoring with no external impact
- formatting, renaming, or non-functional changes
- test-only changes

---

## 4. Release Notes Format

`PackageReleaseNotes` must follow this structure:

- First line: **short summary sentence**
- Followed by **3–6 bullet points**
- Each bullet starts with a **strong verb**

### Allowed prefixes

- `Adds` – new functionality  
- `Improves` – enhancements or performance  
- `Fixes` – bug fixes  
- `Breaking` – breaking changes (must be explicit)  
- `Updates` – meaningful dependency/platform updates only  

### Style rules

- Focus on **consumer impact**
- Be **specific** (no vague phrases like `updates` or `various fixes`)
- Use **present tense**
- Keep concise (max ~5–10 lines)
- Plain text only (NuGet-friendly)
- No marketing language or fluff

---

## 5. Example

```xml
<PackageReleaseNotes>
  Improves TOON protocol handling and serialization consistency.
  - Adds support for updated TOON 3.0 structures
  - Improves token efficiency and output formatting
  - Fixes edge cases in nested object serialization
  - Improves compatibility across .NET target frameworks
</PackageReleaseNotes>