# DevOp.Toon Internal Development Docs

This space is for maintaining and extending `DevOp.Toon`. It is not product-facing documentation. The goal is to make it easy to answer four questions quickly:

1. where a behavior lives
2. which tests to touch
3. how to validate changes
4. which adjacent repos or tools are involved

## Repository shape

Core runtime code:

- `src/DevOp.Toon`
- `src/DevOp.Toon/Internal/Encode`
- `src/DevOp.Toon/Internal/Decode`
- `src/DevOp.Toon/Internal/Shared`

Support projects:

- `tests/DevOp.Toon.Tests` for manual and regression coverage
- `tests/DevOp.Toon.SpecGenerator` for spec-derived test generation
- `benchmarks/DevOp.Toon.Benchmarks` for profiling and report export
- `src/DevOp.Toon.API.TestHost` for formatter and transport-oriented local checks

## What this space should help with

- tracing encode and decode flows
- understanding fast paths versus fallback paths
- knowing when to regenerate generated tests
- validating performance-sensitive changes
- working across `DevOp.Toon.Core`, `DevOp.Toon`, and `DevOp.Toon.API`

## Recommended reading order

1. `Architecture Overview`
2. `Encode Pipeline`
3. `Decode Pipeline`
4. `Testing and Spec Generation`
5. `Change Cookbook`

Use `Benchmarks and API Test Host` and `Local Development and Cross-Repo Workflow` when you are validating behavior or developing across repos.
