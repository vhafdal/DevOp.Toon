# Protocol versions and migration

Released package scope: **DevOp.Toon 0.3.0**, **DevOp.Toon.Core 0.2.0**,
**DevOp.Toon.API 0.2.0**, and **DevOp.Toon.Client 0.2.0**.
See [the measured performance tradeoffs](ProductBenchmarkResults.md).

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

## Supported TOON protocol versions

Package versions and protocol versions are separate: **DevOp.Toon 0.3.0** supports the following
wire formats through `ToonProtocolVersion` from **DevOp.Toon.Core 0.2.0**. API **0.2.0** and
Client **0.2.0** expose the same runtime choices over HTTP.

| TOON protocol/profile | Enum | Encode | Decode | HTTP version value |
| --- | --- | --- | --- | --- |
| Official 3.0 | `V3_0` | Yes | Yes | `3.0` |
| Official 4.0 | `V4_0` | Yes | Yes | `4.0` |
| Official 4.1 | `V4_1` | Yes | Yes | `4.1` |
| Official 4.2 | `V4_2` | Yes | Yes | `4.2` |
| Official 4.3 | `V4_3` | Yes | Yes | `4.3` |
| Official 4.4 | `V4_4` | **Default** | Yes | `4.4` |
| Legacy custom columnar | `CustomLegacy` | Yes | Yes | `custom-legacy` |
| Custom 4.4 grouped rows and spills | `CustomV4_4` | Yes | Yes | `custom-4.4` |
| Automatic syntax detection | `Auto` | No | **Default** | Decode option only |

This specifies official **3.0**, not every 3.x revision. Custom profiles require compatible readers.
For ambiguous input, supply a concrete decode version. When supplying decode options, match the
producer's indentation or use `ToonDecoder.DetectOptions(text, versionHint)`.

```csharp
using DevOp.Toon;
using DevOp.Toon.Core;

var text = ToonEncoder.Encode(new { Id = 42 }, new ToonEncodeOptions
{
    ProtocolVersion = ToonProtocolVersion.V4_4,
    Indent = 2
});
var value = ToonDecoder.Decode(text, new ToonDecodeOptions
{
    ProtocolVersion = ToonProtocolVersion.V4_4,
    Indent = 2
});
```

Official modes default to preserving empty collections and null/empty columns. Custom modes default to the previous compact omission policy. Use `IgnoreNullOrEmpty = false` and `ExcludeEmptyArrays = false` when preserving those values matters. These settings normalize input before layout selection; compact results must not be compared with full-fidelity results as though they contained identical data.

Selecting `CustomLegacy` alone restores that profile's default layout and omission policy. Explicitly assigned options continue to override profile defaults, including after cloning or changing the version. Official v4 rejects columnar spill layout and key folding. Dotted keys are literal in official v4 and the new custom mode. Existing folding/path-expansion workflows must select a legacy grammar explicitly.

CLR serialization reads public instance getters and skips indexers and private getters.
Deserialization can populate public setters independently of getter visibility. Serialization
supports finite values whose declared CLR types refer to themselves or each other; metadata
construction does not recursively expand a type cycle. The historical typed decoder still rejects
recursive type schemas; DOM/JSON decoding is available there. Actual object-reference cycles
remain a separate concern.

## Custom grouped rows

See the dedicated [official vs custom 4.4 guide](CustomV44.md) for a side-by-side wire example,
measured benefits, complete C# configuration and HTTP usage, plus a
[runnable published-package sample](../samples/CustomV44/README.md).

The new custom mode retains a table even when some fields cannot be columns. Shared scalar fields and fully uniform nested objects become header entries. Arrays, uneven objects, and fields absent from a row remain beneath that row:

```toon
items[2]{Id,Customer{Name,Country}}:
  1,Ada,IS
    Tags[2]: a,b
  2,Bob,GB
    Tags[1]: c
```

The same extension applies to keyed tables: each entry row begins with its key and colon, and that entry's spills appear one level deeper. At least two object-valued entries and one shared leaf are required. If no eligible columns exist, normal object/list encoding applies. Header fields and spills cannot overwrite each other. Official versions do not accept these extensions.

### Constant columns

`ToonEncodeOptions.ConstantColumns` (default `false`) lets `CustomV4_4` write a column whose value is
the same in every row of a table with at least three rows once in the header, as `Name=value`:

```toon
[3]{CompanyId=acme,IsSystem=false,CreatedBy=system,AccountCode,Name,AccountType}:
  6900,Other Expenses,Expense
  1000,Cash,Asset
  2000,Payables,Liability
```

Other protocol versions ignore the option and produce unchanged output. Custom 4.4 decoders in this
release always accept constant columns, and `Auto` selects `CustomV4_4` when a header field list
contains an unquoted `=`. Readers from DevOp.Toon 0.4.0 and earlier cannot decode the output, so
enable it only when every reader is updated. See [the custom 4.4 guide](CustomV44.md#constant-columns).

## Extension data

From **DevOp.Toon 0.4.0** (Core **0.3.0**), a property marked `[ToonExtensionData]` of type
`IDictionary<string, object?>` or `Dictionary<string, object?>` captures object keys that match no
member when decoding, and its entries are written after the declared members when encoding.
A member always wins a key collision, captured numbers are `double`, and a type may declare only
one such property (inherited ones included), otherwise encoding and decoding throw
`NotSupportedException`. See [the custom 4.4 guide](CustomV44.md#keep-unknown-keys-with-toonextensiondata).

In 0.4.0 only the 4.x profiles (official 4.0 to 4.4 and `CustomV4_4`) honoured the attribute.
**After 0.4.0**, `V3_0` and `CustomLegacy` honour it too, which changes their output for attributed
types. Entries become sibling keys, quoted when a key does not start with a letter or `_`:

```toon
Name: Other Expenses
"@id": accounts/6900
"@metadata":
  "@collection": Accounts
```

Uniform entries form table columns (`[2]{Name,"@id"}:`). Entries that differ between rows make
`V3_0` fall back to list items, while `CustomLegacy` keeps the columns every row shares and writes
the rest under the row. The `CustomLegacy` defaults omit null, empty-string and empty-array entries,
and key folding folds entry chains such as `meta.collection: Accounts`. Types without the attribute
encode exactly as before.

Typed legacy decoding captures unmatched keys on every route, including path expansion (an
expanded dotted key is captured as a nested entry) and structs. As on the 4.x profiles and the
JSON route, a key named like the extension data property (its TOON name or CLR name) is an
ordinary entry whatever its value. A document written by 0.4.0 with the dictionary as a nested
object therefore decodes as a single entry named after the property, holding that object; the
`Annotations[2]{Key}:` rows that 0.4.0 `CustomLegacy` wrote likewise stay one entry. Code that
must still read such documents takes the original entries from that one entry.

On every profile, a key naming a declared member that the decoder cannot bind (a computed
getter-only property, a 4.x property without a public setter, or an extra member of a
constructor-bound legacy type) is skipped rather than captured; 0.4.0 captured such keys on 4.x.
`ToonService.Deserialize<T>(body, "application/json")` now captures unmatched keys too, with
numbers as `double`.

## Typed numeric binding

**After 0.4.0**, typed decoding (`ToonDecoder.Decode<T>` and `ToonService.Deserialize<T>`) binds a
token to a numeric or enum member by one rule on every profile and every shape: a settable
property, a constructor parameter, a table row, an array or list item, a dictionary value, the root
and a nested object. Untyped decoding (`ToonNode`, `Toon2Json`, `object` members and extension data)
is unchanged and still reads numbers as `double`.

- An integer member (`byte` to `ulong`, their nullable forms, and enums by their underlying type)
  takes any number whose value is an integer in range, in any form: `42`, `1e3`, `2.0`, `100e-2`,
  `-0.0` (also into unsigned types). A number with a fractional part, such as `1.5`, `0.5` or
  `1e-29`, is never rounded or truncated; 0.4.0 rounded it half to even into `byte`, `sbyte`,
  `short` and `ushort` and failed for the wider types.
- A `float` member is read from the token text with a single rounding; a `double` member is read
  once. Neither ever receives an infinity or NaN: a number beyond the range of the member and text
  such as `Infinity` or `NaN` fail. Negative zero binds as zero.
- A `decimal` member keeps the scale of the token (`1.10`, `0.0`) and fails beyond `decimal` range.
- Text is still accepted where it was: a quoted number (`"42"`), `007`, `+42`, `" 42 "`, `"1,000"`
  into `double`, an enum name, a flags list (`"X, Y"`) or an undefined enum value such as `7`.
  `true` and `false` bind to numeric members as 1 and 0, and `null` into a non-nullable member
  binds as its default: TOON writes NaN and infinities as `null`, so the library's own output
  stays decodable into the same type.
- An unquoted number bound to a `string` member keeps its token text (`1.50`, `9007199254740993`);
  0.4.0 rounded it through a `double` on the 4.x profiles.

On the legacy path-expansion route (`ExpandPaths`) and in `ToonService.Deserialize<T>` with JSON,
numbers follow the same rule, while `null`, `true`, `false` and text in a numeric or enum member
still fail with `NotSupportedException`, as in 0.4.0.

A token that does not fit throws `ToonFormatException` with `Kind` `Syntax` and a message naming
the token and the CLR type, for example `Number 1.5 is not an integer (System.Int16)` or
`Number 300 is outside the range of System.Byte`. On the 4.x profiles `LineNumber` is the line of
the token, and the same applies to `bool`, `Guid`, date and time members given text they cannot
parse. 0.4.0 let `OverflowException`, `FormatException` or `ArgumentException` escape on the 4.x
profiles, so code that caught those types must catch `ToonFormatException` instead. The binding rule does not depend
on `Strict`. Exceptions thrown by a member's own setter or constructor still propagate unchanged.

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

Use `benchmarks/DevOp.Toon.ProtocolBenchmarks` for the full 2,051-product baseline/candidate comparison. Preserve the input fixture files. Run builds and benchmarks sequentially, one at a time, and publish observed results only after correctness and regression gates pass.

## Conformance snapshots

The protocol fixture suite pins the following specification snapshots. These are specification
snapshot tags, not NuGet package versions:

| Selected protocol | Pinned snapshot |
| --- | --- |
| 3.0 | `v3.0.0` |
| 4.0 | `v4.0.0` |
| 4.1 | `v4.1.3` |
| 4.2 | `v4.2.1` |
| 4.3 | `v4.3.0` |
| 4.4 | `v4.4.0` |

Commit references and fixture-only attribution are in
[ProtocolFixtures](../tests/DevOp.Toon.Tests/ProtocolFixtures/).
