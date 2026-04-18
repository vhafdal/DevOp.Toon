# Local Development and Cross-Repo Workflow

`DevOp.Toon` rarely lives alone during development. The most common adjacent repos are:

- `DevOp.Toon.Core`
- `DevOp.Toon.API`
- `DevOp.Toon.Client`

## Standard validation commands

```bash
dotnet restore
dotnet build
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

## Local project overrides

Debug builds can point at sibling repositories through environment variables or MSBuild properties.

Important variables:

- `DEVOP_TOON_CORE_CSPROJ`
- `DEVOP_TOON_CSPROJ`
- `DEVOP_TOON_API_CSPROJ`

Example:

```bash
export DEVOP_TOON_CORE_CSPROJ=/home/valdi/Projects/DevOp.Toon.Core/DevOp.Toon.Core.csproj
export DEVOP_TOON_API_CSPROJ=/home/valdi/Projects/DevOp.Toon.API/src/DevOp.Toon.API/DevOp.Toon.API.csproj
dotnet build -c Debug
```

## Practical workflow

When changing core contracts:

1. update `DevOp.Toon.Core`
2. point `DevOp.Toon` at the local core project
3. rerun tests and examples

When changing formatter-driven transport behavior:

1. point the API test host at local `DevOp.Toon.API`
2. run the test host locally
3. validate response shape and profiler behavior

## Release discipline

Do not rely on local overrides for release packaging or CI. `Release` builds should resolve published packages so outputs remain reproducible.

## Current repo note

There is legacy export material under `Documentation/DocMost/DevOp.Toon`, but the live Wiki.js site is now the published documentation source of truth.
