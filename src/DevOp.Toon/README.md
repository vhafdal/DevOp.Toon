# DevOp.Toon

`DevOp.Toon` provides TOON encoding and decoding for .NET applications.

## Installation

```bash
dotnet add package DevOp.Toon
```

## Features

- Encode CLR objects and collections to TOON
- Decode TOON payloads back into .NET types
- Configure encoding and decoding behavior with `ToonEncodeOptions` and `ToonDecodeOptions`
- Use shared TOON contracts from the `DevOp.Toon.Core` package

## Basic Usage

```csharp
using DevOp.Toon;

var payload = new
{
    Id = 42,
    Name = "Widget"
};

var toon = ToonEncoder.Encode(payload);
var decoded = ToonDecoder.Decode<dynamic>(toon);
```

## Package Notes

`DevOp.Toon` depends on `DevOp.Toon.Core` for shared TOON enums and attributes.
