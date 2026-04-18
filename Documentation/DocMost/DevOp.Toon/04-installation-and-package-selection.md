# Installation and Package Selection

Use this page when deciding which TOON package belongs in which project.

## Install `DevOp.Toon`

```bash
dotnet add package DevOp.Toon
```

This package depends on `DevOp.Toon.Core` and brings the runtime surface with it.

## Choose the right package

### Use `DevOp.Toon.Core`

Choose `DevOp.Toon.Core` when a project only needs compile-time TOON contracts, for example:

- `ToonPropertyNameAttribute`
- `ToonDelimiter`
- `ToonKeyFolding`
- `ToonPathExpansion`
- `ToonObjectArrayLayout`

This is the right choice for shared DTO libraries that should not take a dependency on serialization behavior.

### Use `DevOp.Toon`

Choose `DevOp.Toon` when a project needs runtime behavior:

- `ToonEncoder`
- `ToonDecoder`
- `ToonService` and `IToonService`
- DI registration through `AddToon(...)`
- node graph types and parsing exceptions

## Practical guidance

Use `DevOp.Toon` in:

- application services
- APIs
- integration workers
- data transformation utilities

Use `DevOp.Toon.Core` in:

- shared contract packages
- DTO-only projects
- libraries that should stay serializer-agnostic

## Suggested architecture

In larger solutions, keep `DevOp.Toon.Core` in the model layer and `DevOp.Toon` at application boundaries where serialization actually happens.
