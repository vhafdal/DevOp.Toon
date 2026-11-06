# Pinned TOON protocol fixtures

These JSON files are unmodified upstream conformance fixtures from https://github.com/toon-format/spec.
`manifest.json` records the immutable tag commits and source case counts. Each snapshot also contains
`source.json`. The upstream MIT license is preserved in `LICENSE`.

## Attribution scope

The Johann Schopplich copyright notice in this directory's `LICENSE` applies only to the copied
upstream specification fixtures. It does not describe authorship of the DevOp.Toon implementation.
The independently written library is copyright 2026 Þorvaldur Hafdal (DevOp), as stated in
[`src/DevOp.Toon/LICENSE.md`](../../../src/DevOp.Toon/LICENSE.md).

`VersionedProtocolFixtureTests` runs each snapshot under its explicit protocol enum. It honors
`minSpecVersion`, both `indentSize` and legacy `indent`, strictness, delimiter, and v3 path options.
No unsupported fixture is silently ignored: a fixture above its snapshot's minimum supported version
is excluded by its declared `minSpecVersion`; failures remain visible in the test output.

Run `./specgen.sh` or `./specgen.ps1` from the repository to refresh these exact snapshots. The
scripts use the repository build-slot wrapper and preserve the existing generated legacy suites.
`TOON_SPEC_REPOSITORY` may point to a local clone for offline regeneration.

The old `GeneratedTests` C# suites explicitly use `CustomLegacy`, preserving the implementation's
historical behavior. The independently pinned v3 JSON suite uses `V3_0` and checks official conformance.
