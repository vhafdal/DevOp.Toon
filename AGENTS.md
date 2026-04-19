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

## Package Release Notes Before Commit

When performing `git commit`, review whether any changed project produces a NuGet package.

If a `.csproj` file contains package metadata such as `PackageId` or is clearly intended to be packed and published, update the `PackageReleaseNotes` field before committing when the change affects package behavior, features, fixes, compatibility, or documentation relevant to consumers.

### Process
- Inspect the staged diff
- Identify affected packable projects
- Update `PackageReleaseNotes` in the relevant `.csproj` file(s)
- Keep the release notes aligned with the actual change being committed
- Stage the updated project file before creating the commit

### Release Notes Guidance
`PackageReleaseNotes` should:
- briefly summarize the meaningful change
- focus on package consumer impact
- mention fixes, features, behavior changes, or important internal improvements
- avoid vague text such as `updates` or `various fixes`
- stay concise and professional

### Rules
- Do not update release notes for unrelated projects
- Do not invent changes not present in the diff
- If multiple packages are affected, update each relevant project separately
- If the change is trivial and does not matter to package consumers, release notes update may be skipped
- Keep release notes consistent with the commit message, but not necessarily identical

### Default Behavior
If I say `git commit`, the workflow should be:
1. inspect staged changes
2. update relevant `PackageReleaseNotes`
3. stage those updates
4. generate a descriptive commit message
5. perform the commit

## Commit & Pull Request Guidelines

### Commit Message Generation (Agent Behavior)

When performing a `git commit`, always generate a high-quality commit message based on the actual staged changes.

#### Process
- Inspect `git diff --staged` before writing the message
- If nothing is staged, do not guess — indicate that no changes are staged
- Identify the primary intent of the change (not just the files modified)
- Prefer one logical concern per commit
- If multiple unrelated changes are detected, warn and suggest splitting the commit

#### Format

Use Conventional Commit style:

<type>: concise, intent-focused summary

- What changed
- Why it changed
- Important implementation details
- Any side effects, limitations, or follow-up work

#### Allowed Types
- feat
- fix
- docs
- refactor
- perf
- test
- build
- ci
- chore

#### Rules
- Keep subject line imperative and specific  
  (e.g. `fix: align hybrid encoding output`)
- Do not use vague messages such as:
  - update files
  - fixes
  - misc changes
  - work in progress
- Do not describe only *what* changed — explain *why*
- Do not rely on filenames as the message
- For non-trivial changes, always include a descriptive body
- Base the message strictly on the diff, never assumptions

#### Commit Size Guidance
- Small change → summary line only is acceptable
- Medium change → summary + 2–4 bullet points
- Large change → full descriptive body required

#### Default Behavior
If the user only says `git commit`:
- Automatically inspect the staged diff
- Generate the best possible commit message
- Include a body when the change is not trivial
- Avoid unnecessary follow-up questions unless intent is unclear