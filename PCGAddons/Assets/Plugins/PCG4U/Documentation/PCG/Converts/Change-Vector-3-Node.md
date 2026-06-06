# ChangeVector3Node

Modifies a Vector3 value according to the selected mode using a random or fixed vector in range [Min, Max].

## Inputs

### Max

Maximum range value (inclusive) for random generation.

### Min

Minimum range value (inclusive) for random generation.

### Seed

Random seed. Use -1 for non-deterministic behavior.

### Value

Source Vector3 value.

## Variables

### Mode

Mode of modification applied to the input value using a sampled vector R from [Min, Max]:
- Add: Result = Value + R
- Mult: Result = Value component-wise multiplied by R (Value.Mul(R))
- Set: Result = R (ignores Value)
Notes:
- If Min == Max, R equals Min (no randomness).
- Randomness is controlled by Seed; Seed == -1 means non-deterministic.

## Outputs

### Result

Output value after modification.

