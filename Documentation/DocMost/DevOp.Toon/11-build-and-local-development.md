# Build and Local Development

This page is for contributors and cross-repo developers working on `DevOp.Toon` alongside other TOON repositories.

## Standard commands

Run these from the repository root:

```bash
dotnet restore
dotnet build
dotnet test
dotnet test --collect:"XPlat Code Coverage"
dotnet format
```

If encoding or decoding behavior changes, regenerate spec-driven tests:

```bash
./specgen.sh
```

or on Windows:

```powershell
./specgen.ps1
```

## Local cross-repo debug workflow

`Debug` builds can resolve sibling TOON repos from local project paths rather than NuGet packages.

Important environment variables:

- `DEVOP_TOON_CORE_CSPROJ`
- `DEVOP_TOON_CSPROJ`
- `DEVOP_TOON_API_CSPROJ`

Example:

```bash
export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj
dotnet build -c Debug
```

`Release` builds should continue using NuGet so outputs stay reproducible.

## Practical guidance

- use environment variables for recurring local development
- use MSBuild properties for one-off builds
- do not hardcode machine-specific paths into the repository
- keep release packaging on published package references

## Why this exists

The local override model makes it practical to work on `DevOp.Toon.Core`, `DevOp.Toon`, and related projects together without turning the repo into a machine-specific setup.
