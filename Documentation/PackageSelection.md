# Package Selection

`DevOp.Toon` now separates the minimal shared contracts from the serializer/runtime package.

## Use `DevOp.Toon.Core`

Choose `DevOp.Toon.Core` when you only need compile-time TOON contracts in shared model libraries:

- `ToonPropertyNameAttribute`
- `ToonDelimiter`
- `ToonKeyFolding`
- `ToonPathExpansion`
- `ToonObjectArrayLayout`

This package is intended for DTO projects that should not take a dependency on the encoder, decoder, or DI/runtime surface.

## Use `DevOp.Toon`

Choose `DevOp.Toon` when you need TOON runtime behavior:

- `ToonEncoder`
- `ToonDecoder`
- `IToonService`
- DI registration
- node graph types
- exceptions and parsing/encoding behavior

`DevOp.Toon` depends on `DevOp.Toon.Core` and continues to use the shared contracts from that package.
