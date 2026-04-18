# Why TOON

TOON is worth evaluating when JSON payload size matters and the data shape is structured enough to benefit from a more compact representation.

## The short version

Current benchmark material in this repository shows:

- `Products` payloads about `47.98%` smaller than JSON
- `Warehouses` payloads about `68.11%` smaller than JSON
- `Flat orders` payloads about `63.50%` smaller than JSON
- continued savings after compression, including `Flat orders` about `15.74%` smaller with gzip

That is the core value proposition: less data to move, less data to store, and better efficiency on real structured business payloads.

## Where TOON fits best

TOON is especially compelling when payloads include:

- repeated rows with shared keys
- nested arrays
- repetitive change history
- catalog or inventory collections

These are common enterprise shapes, not edge cases.

## Why this package matters

`DevOp.Toon` is the package that turns TOON from an idea into an application integration:

- encode CLR objects to TOON
- decode TOON back into `ToonNode` or strongly typed models
- bridge TOON and JSON during migration
- register a reusable service through dependency injection

## Honest positioning

TOON should be positioned as a strong fit for the right workloads, not as a universal replacement story.

**Strongest claim:** TOON materially reduces payload size on repeated-row and nested collection data, and this package gives .NET teams a production-friendly way to adopt it.

**Important caveat:** the biggest wins come from data that actually benefits from compact tabular or columnar representation. Not every payload shape will tell the same story.
