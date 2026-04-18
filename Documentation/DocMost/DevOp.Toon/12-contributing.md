# Contributing

This page is the concise contributor guide for `DevOp.Toon`.

## Development expectations

- use nullable-safe C#
- follow standard .NET conventions
- run the test suite before opening a pull request
- update documentation when behavior or usage changes

## Core workflow

```bash
dotnet restore
dotnet build
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

Format code when needed:

```bash
dotnet format
```

## Test expectations

- all new features should include tests
- maintain strong coverage, with `85%+` line coverage as the project target
- cover spec compliance and edge cases when encode or decode behavior changes

Generated tests must remain aligned with the TOON specification. Run:

```bash
./specgen.sh
```

or:

```powershell
./specgen.ps1
```

## Pull request guidance

Good pull requests should include:

- a clear summary of the behavior change
- local validation results
- linked issue or discussion when relevant
- updated docs when user-facing behavior changes

Conventional Commit prefixes such as `feat:`, `fix:`, `docs:`, `build:`, and `chore:` are a good fit for this repository.

## Contribution mindset

Changes that affect TOON behavior should be grounded in the specification, validated with tests, and documented clearly enough that downstream users understand what changed and why.
