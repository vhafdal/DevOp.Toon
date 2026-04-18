# API and Options Reference

This page is a concise reference for the public surface most teams use when integrating `DevOp.Toon`.

## Primary APIs

### `ToonEncoder`

Use `ToonEncoder` for direct static encoding.

Key members:

- `Encode<T>(T data)`
- `Encode<T>(T data, ToonEncodeOptions options)`
- `EncodeToBytes<T>(T data)`
- `Json2Toon(string json)`
- `DefaultOptions`

Current static defaults:

- `Indent = 2`
- `KeyFolding = Safe`
- `ObjectArrayLayout = Columnar`
- `Delimiter = COMMA`

### `ToonDecoder`

Use `ToonDecoder` for direct static decoding and conversion.

Key members:

- `Decode(string toonString)`
- `Decode<T>(string toonString)`
- `Toon2Json(string toonString)`
- `DetectOptions(string toonString)`
- `DefaultOptions`

### `IToonService` and `ToonService`

Use the service API when TOON should be configured once and reused across an application.

Key capabilities:

- sync and async encode/decode
- JSON to TOON conversion
- TOON to JSON conversion
- decode option detection

### `AddToon(...)`

Use `AddToon(...)` to register `IToonService` and shared `ToonServiceOptions` in DI.

## Encode options

`ToonEncodeOptions` controls output shape:

- `Indent`
- `Delimiter`
- `KeyFolding`
- `FlattenDepth`
- `ObjectArrayLayout`
- `IgnoreNullOrEmpty`
- `ExcludeEmptyArrays`

Practical guidance:

- `Columnar` is the strongest current default story for real business data
- `IgnoreNullOrEmpty = true` helps reduce low-value tabular columns
- `ExcludeEmptyArrays = true` removes empty array properties from output

## Decode options

`ToonDecodeOptions` controls parser behavior:

- `Indent`
- `Strict`
- `ExpandPaths`
- `ObjectArrayLayout`

Practical guidance:

- keep `Strict = true` unless you have a specific compatibility reason not to
- use `ExpandPaths = Safe` only when dotted keys should become nested objects

## Related types

- `ToonNode` for DOM-style access
- `ToonMediaTypes.Application` for `application/toon`
- `ToonMediaTypes.Text` for `text/toon`

## Recommendation

Start with `IToonService` for applications and use the static APIs for local transforms, tests, utilities, or focused serialization tasks.
