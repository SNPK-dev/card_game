# ADR-0001: Implementation Stack

## Status

Accepted.

## Context

The project documentation contains Rust-oriented architectural guidance: strict typing, deterministic design, absence of ambient global state, composition over inheritance, and careful boundary control.

The current repository bootstrap chooses a .NET implementation while preserving those architectural principles.

## Decision

Use .NET 10 LTS and C# 14 for the initial implementation.

The repository pins the SDK through `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

## Consequences

- Existing Rust recommendations remain architectural orientation, not current implementation stack.
- Deterministic design, strict contracts, one-way dependencies, no hidden global state, and composition remain mandatory.
- Domain code is implemented in C# and protected by architecture tests and source scans.
- Migration to Rust, a mixed stack, or a separate engine runtime requires a dedicated OpenSpec change and architecture review.
