# TOON Format Protocol And This Library's Columnar Customization

This document summarizes the TOON format as used by this repository and explains the object-array `Columnar` behavior implemented by this .NET library.

Primary references:
- https://toonformat.dev/guide/format-overview
- https://toonformat.dev/reference/syntax-cheatsheet

This file is intentionally short and implementation-oriented. For the canonical format definition, use the official TOON site.

## What TOON Is

TOON is a compact text format for structured data. At a high level:
- objects are written as `key: value` pairs
- arrays carry an explicit length header like `[3]:`
- arrays of uniform objects can be written in tabular form with a shared header like `{id,name}`
- nested data is expressed through indentation

The format is designed to reduce repeated field names compared with JSON, especially for arrays of similarly shaped objects.

## Core Syntax

Common forms used by the protocol:

```toon
name: Ada
active: true
count: 3
```

Object with nested members:

```toon
user:
  id: 1
  name: Ada
```

Primitive array:

```toon
tags[3]: red,green,blue
```

Root primitive array:

```toon
[3]: x,y,z
```

Tabular array of objects:

```toon
items[2]{id,name}:
  1,Ada
  2,Bob
```

Array of arrays:

```toon
pairs[2]:
  - [2]: 1,2
  - [2]: 3,4
```

Empty array:

```toon
items[0]:
```

## Quoting Rules

From the official syntax cheat sheet, strings must be quoted when they would otherwise be ambiguous or syntactically active. In practice that includes values that:
- are empty
- have leading or trailing whitespace
- look like booleans, `null`, or numbers
- contain syntax characters such as `:`, `"`, `\`, `[`, `]`, `{`, `}`
- contain the active delimiter

Examples:

```toon
version: "123"
enabled: "true"
note: "hello, world"
name: ""
```

## Delimiters

TOON supports different delimiters for inline primitive arrays and tabular rows. This library exposes that through `ToonEncodeOptions.Delimiter`.

Examples:
- comma: `a,b,c`
- tab
- pipe

The active delimiter also affects quoting. A string containing the active delimiter must be quoted.

## Arrays Of Objects In This Library

This library supports two object-array layout modes through `ToonObjectArrayLayout`:
- `Auto`
- `Columnar`

Implementation reference:
- [Constants.cs](/home/valdi/Projects/toon-dotnet/src/DevOp.Toon/Constants.cs)
- [ToonEncodeOptions.cs](/home/valdi/Projects/toon-dotnet/src/DevOp.Toon/Options/ToonEncodeOptions.cs)

### `Auto`

`Auto` keeps the classic behavior:
- arrays of objects become tabular only when all relevant row fields are primitive and the shape is fully tabular
- otherwise the encoder falls back to list-style object encoding

### `Columnar`

`Columnar` is this library's main customization for object arrays.

It works like this:
1. Find keys that are present in every row.
2. Keep only the keys whose values are primitive in every row.
3. Use those shared primitive keys as the tabular header.
4. Write the remaining non-header fields under each row as nested content.

Implementation reference:
- [NativeEncoders.cs](/home/valdi/Projects/toon-dotnet/src/DevOp.Toon/Internal/Encode/NativeEncoders.cs)

Relevant implementation points:
- `TryExtractColumnarHeader(...)`
- `EncodeArrayOfObjectsAsColumnar(...)`

## Columnar Example

JSON input:

```json
{
  "items": [
    {
      "status": "active",
      "count": 2,
      "users": [
        { "id": 1, "name": "Ada" },
        { "id": 2, "name": "Bob" }
      ],
      "tags": ["a", "b", "c"]
    }
  ]
}
```

Columnar output in this library:

```toon
items[1]{status,count}:
  active,2
    users[2]{id,name}:
      1,Ada
      2,Bob
    tags[3]: a,b,c
```

This is not plain tabular output and not plain list output. It is a mixed layout:
- shared primitive columns are emitted once in the header
- complex values spill below the corresponding row

## Default Behavior In This Repository

The encoder defaults in this repository are:
- `Indent = 2`
- `KeyFolding = Safe`
- `ObjectArrayLayout = Columnar`
- `Delimiter = COMMA`

Implementation reference:
- [ToonEncoder.cs](/home/valdi/Projects/toon-dotnet/src/DevOp.Toon/ToonEncoder.cs)

That means `ToonEncoder.Encode(...)` without explicit options already prefers columnar object-array encoding.

## Why Columnar Exists Here

`Columnar` is useful when an array of objects has:
- a stable primitive core that benefits from tabular compression
- additional nested objects or arrays that would prevent full tabular encoding

It preserves more of TOON's tabular compactness while still supporting richer row payloads.

In practical terms, it is meant to reduce repeated field names in partially uniform collections without forcing everything into list-style nested objects.

## Notes On Compatibility

When documenting or discussing TOON behavior in this repository, distinguish between:
- official TOON protocol behavior from `toonformat.dev`
- library-specific encoding policy choices such as `Columnar`

`Columnar` is an encoder policy exposed by this implementation. It is not just a syntax example; it changes how arrays of objects are chosen for output.
