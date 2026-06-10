# RandomIntNode

Generates a random integer in the range [Min, Max].

## Inputs

### Max

Upper bound (inclusive).

### Min

Lower bound (inclusive).

### Seed

Random seed. Use 0 or less for non-deterministic behavior; a generated seed is assigned on bind so the value stays stable across editor restarts.

## Outputs

### Result

Random integer between `Min` and `Max`, both inclusive, deterministic for a fixed `Seed`.
