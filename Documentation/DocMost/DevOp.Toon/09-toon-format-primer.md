# TOON Format Primer

This page gives application developers enough TOON syntax to read examples and understand what `DevOp.Toon` emits.

For the canonical format definition, use the official TOON site. This page is intentionally implementation-oriented.

## What TOON looks like

Simple values:

```toon
name: Ada
active: true
count: 3
```

Nested object:

```toon
user:
  id: 1
  name: Ada
```

Primitive array:

```toon
tags[3]: red,green,blue
```

Tabular array of objects:

```toon
items[2]{id,name}:
  1,Ada
  2,Bob
```

## Key ideas

- objects use `key: value`
- arrays include a count such as `[3]:`
- arrays of uniform objects can share a header
- indentation expresses nesting

## Quoting rules

Strings should be quoted when they would otherwise be ambiguous, for example when they:

- are empty
- contain leading or trailing whitespace
- look like booleans, `null`, or numbers
- include active syntax characters
- contain the active delimiter

Example:

```toon
version: "123"
enabled: "true"
note: "hello, world"
name: ""
```

## Delimiters

The encoder supports different delimiters for inline arrays and tabular rows through `ToonEncodeOptions.Delimiter`.

Common examples:

- comma
- pipe
- tab

## Columnar layout in this library

`DevOp.Toon` supports object-array layouts through `ToonObjectArrayLayout`. The most important implementation-specific mode is `Columnar`.

`Columnar` keeps shared primitive fields in a row header, then spills complex nested values below each row. That allows the library to preserve compactness even when rows contain nested arrays or objects.

This is one reason the package performs well on real-world catalog and warehouse data instead of only on perfectly flat records.
