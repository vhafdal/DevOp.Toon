# Decode Pipeline

This page is the quickest map for changing parser or materialization behavior.

## Main flow

Public decode calls start in `ToonDecoder`.

Typical path:

1. validate decode options
2. scan source text into parsed lines
3. attempt typed fast path when applicable
4. decode into native nodes
5. optionally expand dotted paths
6. materialize into `ToonNode` or the target CLR type

## Important files

- `src/DevOp.Toon/ToonDecoder.cs`
- `src/DevOp.Toon/Internal/Decode/Scanner.cs`
- `src/DevOp.Toon/Internal/Decode/Parser.cs`
- `src/DevOp.Toon/Internal/Decode/NativeDecoders.cs`
- `src/DevOp.Toon/Internal/Decode/TypedDecoder.cs`
- `src/DevOp.Toon/Internal/Decode/DirectMaterializer.cs`
- `src/DevOp.Toon/Internal/Decode/NativeTypedMaterializer.cs`
- `src/DevOp.Toon/Internal/Decode/NativePathExpansion.cs`

## Scanner responsibilities

`Scanner` turns source text into `ParsedLine` records with:

- source-relative content ranges
- indent and depth information
- blank-line tracking
- strict-mode indentation validation

If a bug looks like indentation handling, line depth, tabs, or blank-line behavior, start here.

## Typed decode split

`TypedDecoder` first tries `DirectMaterializer`. If that fails, it records a miss and falls back to:

1. native-node decode via `NativeDecoders`
2. CLR conversion via `NativeTypedMaterializer`

This means typed decode bugs can live in either:

- the direct optimized materializer
- the general native-node conversion path

## Path expansion

`ExpandPaths = Safe` can convert eligible dotted keys into nested objects. This logic is applied selectively and only when root-level conditions allow it. If a bug involves dotted keys or unexpectedly nested output, inspect:

- `ToonDecoder`
- `NativePathExpansion`

## What to test after decode changes

- `ManualTests/ToonDecoderTests.cs`
- `ManualTests/ToonRoundTripTests.cs`
- `ManualTests/JsonComplexRoundTripTests.cs`
- generated tests under `tests/DevOp.Toon.Tests/GeneratedTests`

If strictness, indentation, or syntax handling changes, regenerate and rerun generated tests.
