# Protocol versions and migration

This change is unreleased. Protocol conformance, integration and existing-path regression checks
have passed; publication remains a separate action. See [the measured performance tradeoffs](ProductBenchmarkResults.md).

`ToonProtocolVersion` lives in `DevOp.Toon.Core`. It selects a wire grammar rather than a NuGet package version. The encoder defaults to `V4_4`; the decoder defaults to `Auto`.

```csharp
using DevOp.Toon;
using DevOp.Toon.Core;

var encode = new ToonEncodeOptions
{
    ProtocolVersion = ToonProtocolVersion.V4_4,
    Indent = 2
};
var text = ToonEncoder.Encode(new[]
{
    new { Id = 1, Customer = new { Name = "Ada", Country = "IS" } },
    new { Id = 2, Customer = new { Name = "Bob", Country = "GB" } }
}, encode);
var decoded = ToonDecoder.Decode(text, new ToonDecodeOptions
{
    ProtocolVersion = ToonProtocolVersion.V4_4,
    Indent = 2
});
```

The table header is `[2]{Id,Customer{Name,Country}}:`. Nested fields occupy primitive cells in depth-first header order.

The [runnable protocol sample](../samples/ProtocolVersions/README.md) exercises typed nested DTOs,
arrays, lossless custom options and explicit version hints for ambiguous input.

## Modes

| Enum | Intended use |
| --- | --- |
| `V3_0` | Original official 3.0 grammar and options |
| `V4_0` through `V4_4` | Explicit official protocol rules, pinned to a documented fixture snapshot |
| `CustomLegacy` | Existing columnar layout, including complex fields below rows |
| `CustomV4_4` | Version 4.4 grammar extended with grouped columnar rows and keyed-table spills |
| `Auto` | Decode-only syntax inspection; never an encoder target |

Official modes default to preserving empty collections and null/empty columns. Custom modes default to the previous compact omission policy. Use `IgnoreNullOrEmpty = false` and `ExcludeEmptyArrays = false` when preserving those values matters. These settings normalize input before layout selection; compact results must not be compared with full-fidelity results as though they contained identical data.

Selecting `CustomLegacy` alone restores that profile's default layout and omission policy. Explicitly assigned options continue to override profile defaults, including after cloning or changing the version. Official v4 rejects columnar spill layout and key folding. Dotted keys are literal in official v4 and the new custom mode. Existing folding/path-expansion workflows must select a legacy grammar explicitly.

CLR serialization reads public instance getters and skips indexers and private getters.
Deserialization can populate public setters independently of getter visibility. Serialization
supports finite values whose declared CLR types refer to themselves or each other; metadata
construction does not recursively expand a type cycle. The historical typed decoder still rejects
recursive type schemas; DOM/JSON decoding is available there. Actual object-reference cycles
remain a separate concern.

## Custom grouped rows

The new custom mode retains a table even when some fields cannot be columns. Shared scalar fields and fully uniform nested objects become header entries. Arrays, uneven objects, and fields absent from a row remain beneath that row:

```toon
items[2]{Id,Customer{Name,Country}}:
  1,Ada,IS
    Tags[2]: a,b
  2,Bob,GB
    Tags[1]: c
```

The same extension applies to keyed tables: each entry row begins with its key and colon, and that entry's spills appear one level deeper. At least two object-valued entries and one shared leaf are required. If no eligible columns exist, normal object/list encoding applies. Header fields and spills cannot overwrite each other. Official versions do not accept these extensions.

## Detection and ambiguity

`ToonDecoder.DetectVersion(text)` returns a `ToonVersionDetectionResult`. `DetectedVersion` is nullable because many documents share syntax. `SelectedVersion` is the compatible decoder choice, not evidence of the producer's exact version. `RequiresVersionHint` means decoding could change the meaning and requires explicit `ToonDecodeOptions.ProtocolVersion`.

A line beginning with `#` after ASCII spaces is data under legacy semantics and a comment in v4. Some numeric tokens and whitespace also changed interpretation. The automatic decoder rejects conflicting meanings rather than silently deleting or changing data. An explicit version never triggers a retry with another parser.

Official-v4 and new-custom decoders impose a nesting limit of 256 on objects, arrays, and header groups and report deeper input as a format error. Strict UTF-8 decoding applies to official input; historical 4.2/4.3 non-strict modes retain their specified replacement recovery.

Transport metadata is preferable when producer and consumer know the version. Do not insert an invented version directive into TOON text.

## HTTP package integration

The API and Client packages use two independent headers:

```http
Content-Type: application/toon
X-Toon-Version: 4.4
X-Toon-Option-ProtocolVersion: custom-4.4
```

`X-Toon-Version` identifies that message's body. A response carries its own value. `X-Toon-Option-ProtocolVersion` requests the response encoding, independently of the request body version. Accepted transport names are `3.0`, `4.0` through `4.4`, `custom-legacy`, and `custom-4.4`.

Malformed or conflicting declarations return 400; unsupported request-body versions return 415; unsupported requested response versions return 406. Without metadata, automatic decoding applies. An explicitly configured decoder and contradictory message header produce an error. Client error-body decoding retains the response's version metadata.

```csharp
var clientOptions = new DevOp.Toon.Client.ToonClientOptions
{
    EncodeOptions = new ToonEncodeOptions { ProtocolVersion = ToonProtocolVersion.V4_4 },
    ResponseEncodeOverrides = new DevOp.Toon.Client.ToonResponseEncodeOverrideOptions
    {
        ProtocolVersion = ToonProtocolVersion.CustomV4_4
    }
};
```

## Validation evidence

Official fixture snapshots, immutable commit references, and upstream fixture attribution are in `tests/DevOp.Toon.Tests/ProtocolFixtures`. The fixture license applies to copied fixture material; implementation authorship and licensing remain separate.

Use `benchmarks/DevOp.Toon.ProtocolBenchmarks` for the full 2,051-product baseline/candidate comparison. Preserve the input fixture files. Run validation through `devop-build-slot` and publish observed results only after correctness and regression gates pass.
