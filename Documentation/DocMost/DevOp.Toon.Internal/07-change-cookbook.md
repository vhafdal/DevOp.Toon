# Change Cookbook

Use this page as a “where do I start?” guide before making a change.

## Change output shape for arrays of objects

Start with:

- `Internal/Encode/ClrObjectArrayFastEncoder.cs`
- `Internal/Encode/NativeEncoders.cs`
- `ManualTests/HybridObjectArrayTests.cs`
- `ManualTests/CompactHybridRegressionTests.cs`

Common examples:

- columnar header selection
- spill-row behavior
- tabular versus nested fallback decisions

## Change key folding behavior

Start with:

- `Internal/Encode/NativeEncoders.cs`
- `Internal/Encode/ClrObjectArrayFastEncoder.cs`
- `ManualTests/KeyFoldingTests.cs`

## Change null or empty suppression

Start with:

- `Internal/Encode/ClrObjectArrayFastEncoder.cs`
- `ManualTests/IgnoreNullOrEmptyTests.cs`
- `ManualTests/ExcludeEmptyArraysTests.cs`

## Change parser strictness or indentation rules

Start with:

- `Internal/Decode/Scanner.cs`
- `Internal/Decode/Validation.cs`
- `ManualTests/ToonDecoderTests.cs`
- generated spec tests

## Change typed decode behavior

Start with:

- `Internal/Decode/TypedDecoder.cs`
- `Internal/Decode/DirectMaterializer.cs`
- `Internal/Decode/NativeTypedMaterializer.cs`
- `ManualTests/ToonDecoderTests.cs`
- `ManualTests/ToonRoundTripTests.cs`

## Change dotted-path expansion

Start with:

- `ToonDecoder.cs`
- `Internal/Decode/NativePathExpansion.cs`
- tests covering `ExpandPaths`

## Change public defaults or examples

Check all of these before shipping:

- `ToonEncoder.cs`
- benchmark defaults in `benchmarks/.../Program.cs`
- `README.md`
- DocMost export pages
- tests that assume current defaults
