# Architecture Overview

`DevOp.Toon` is organized around a small public surface and a larger internal pipeline.

## Public entrypoints

Main entrypoints live in `src/DevOp.Toon`:

- `ToonEncoder`
- `ToonDecoder`
- `ToonService`
- `IToonService`
- `ToonServiceCollectionExtensions`
- option types under `Options/`

These are the stable APIs most callers use. Most development work happens below them.

## Internal subsystems

### Encode

Files under `Internal/Encode` handle:

- CLR object inspection and planning
- native-node normalization
- tabular and columnar output
- compact writer paths

The important split is:

- fast CLR-first encoding via `ClrObjectArrayFastEncoder`
- general fallback encoding via `NativeNormalize` and `NativeEncoders`

### Decode

Files under `Internal/Decode` handle:

- scanning raw text into parsed lines
- native-node decode
- direct typed decode attempts
- fallback materialization into CLR types
- validation and path expansion

The important split is:

- fast typed decode via `DirectMaterializer`
- fallback decode via `NativeDecoders` plus `NativeTypedMaterializer`

### Shared

`Internal/Shared` contains conversion and low-level helpers used across both paths, including JSON conversion and `ToonNode` bridging.

## Key implementation pattern

The codebase repeatedly prefers:

- optimized fast paths for common shapes
- fallback paths for correctness and feature completeness

That means when behavior changes, you usually need to verify both:

- the specialized fast path
- the normalized generic path
