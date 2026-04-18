# Encode Pipeline

This page is the quickest map for changing TOON output behavior.

## Main flow

Public calls start in `ToonEncoder`.

Typical path:

1. validate options
2. resolve encode options
3. try the CLR fast path
4. fall back to native normalization and generic encoding

## Important files

- `src/DevOp.Toon/ToonEncoder.cs`
- `src/DevOp.Toon/Internal/Encode/ClrObjectArrayFastEncoder.cs`
- `src/DevOp.Toon/Internal/Encode/NativeNormalize.cs`
- `src/DevOp.Toon/Internal/Encode/NativeEncoders.cs`
- `src/DevOp.Toon/Internal/Encode/ClrEncodePlan.cs`
- `src/DevOp.Toon/Internal/Encode/CompactBufferWriter.cs`
- `src/DevOp.Toon/Internal/Encode/LineWriter.cs`

## Fast path

`ClrObjectArrayFastEncoder` is the first place to look for performance-sensitive encoding of:

- plain root CLR objects
- arrays or collections of plain CLR objects
- compact tabular or columnar output

Important behaviors visible in this file:

- row collection and declared element-type shortcuts
- row-layout caching
- column suppression when `IgnoreNullOrEmpty` is enabled
- `Columnar` behavior for mixed primitive and nested row shapes

## Fallback path

When the fast path cannot encode the input, `NativeNormalize` converts CLR data into native nodes and `NativeEncoders` writes the final TOON output.

Use this path when changing:

- behavior for complex nested shapes
- generic normalization rules
- non-optimized or unusual object graphs
- correctness fixes that must apply broadly

## Where common option changes land

- delimiter or quoting behavior: `NativeEncoders` and shared literal/string helpers
- key folding: `NativeEncoders` and encode planning
- columnar header selection: `ClrObjectArrayFastEncoder`
- null or empty suppression: `ClrObjectArrayFastEncoder` and related row-writing logic
- empty array output: encode path plus tests for omission behavior

## What to test after encode changes

- `ManualTests/ToonEncoderTests.cs`
- `ManualTests/HybridObjectArrayTests.cs`
- `ManualTests/CompactHybridRegressionTests.cs`
- `ManualTests/IgnoreNullOrEmptyTests.cs`
- `ManualTests/ExcludeEmptyArraysTests.cs`
- `ManualTests/KeyFoldingTests.cs`
