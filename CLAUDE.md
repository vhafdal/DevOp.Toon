@AGENTS.md

# Claude Code notes

`AGENTS.md` (imported above) is the canonical guide. This file adds only what is specific to Claude Code.

## Subagents (`.claude/agents/`)

- `toon-scout` — read-only search: "where is X", "does a test for Y exist", how an option or
  protocol version flows through encode and decode. Use it instead of a general-purpose agent.
- `toon-build` — one focused `dotnet build` or filtered `dotnet test`; returns the verdict, not the log.
- `toon-reviewer` — adversarial review of a finished change before merge (wire-format
  regressions, silent data loss in round trips, unverified claims). Never edits.

## Context economy

Send build and test output to a file under the ignored `artifacts/` directory and grep it
(`error|Passed!|Failed!`) instead of reading whole logs. Build or test the one project you changed.

## Architecture map (`src/DevOp.Toon/Internal/`)

Encode (`Internal/Encode/`):
- `NativeNormalize` — reflects CLR values into `NativeNode` trees (`NativeNodes`), honouring
  `ToonPropertyNameAttribute`; caches property metadata.
- `ClrEncodePlan` (`ClrEncodePlanCache`, `ClrObjectPlan`) — cached per-type plans; `ClrObjectArrayFastEncoder`
  uses them for the uniform object-array fast path.
- `NativeEncoders` (with `NativePrimitives`, `NativeFolding`) — v3/legacy writer for primitives, objects, arrays, key folding.
- `V4Encoder`, `V4ClrEncoder` — official 4.x and custom 4.4 writers for nodes and CLR objects.
- `LineWriter`, `CompactBufferWriter` — output writers; `ByteSequenceText` — byte-array text forms;
  `ResolvedEncodeOptions` — the immutable resolved form of `ToonEncodeOptions`.

Decode (`Internal/Decode/`):
- `Scanner` (`ScanResult`, `LineCursor`) — splits text into parsed lines.
- `Parser` (`ArrayHeaderInfo`, `ArrayHeaderParseResult`) — headers, keys and values for v3/legacy.
- `NativeDecoders` — builds the node tree; `NativePathExpansion` — dotted-key path expansion.
- `TypedDecoder`, `DirectMaterializer`, `NativeTypedMaterializer` — typed CLR materialization.
- `V4Decoder`, `V4SourceLine`, `V4Materialization`, `V4ProtocolDetector` — 4.x decoding,
  typed materialization and `Auto` version detection.
- `Validation` (`ResolvedDecodeOptions`) — structural and strict-mode checks.

Shared (`Internal/Shared/`): `FloatUtils`, `NumericUtils`, `LiteralUtils`, `StringUtils`,
`JsonTextConverter`, `ToonNodeConverter`, `ValidationShared`.
