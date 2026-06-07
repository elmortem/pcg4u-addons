# RandomFloatNode

Generates a random float in the range [Min, Max].

## Inputs

### Max

Upper bound (inclusive).

### Min

Lower bound (inclusive).

### Seed

Random seed. Use -1 for non-deterministic behavior; a generated seed is assigned on bind so the value stays stable across editor restarts.

## Outputs

### Result

Random value between `Min` and `Max`, deterministic for a fixed `Seed`.
