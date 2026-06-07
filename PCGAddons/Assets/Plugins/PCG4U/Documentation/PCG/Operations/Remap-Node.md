# RemapNode

Remaps a float value from the input range [FromMin, FromMax] to the output range [ToMin, ToMax].

## Inputs

### FromMax

Upper bound of the input range.

### FromMin

Lower bound of the input range.

### ToMax

Upper bound of the output range.

### ToMin

Lower bound of the output range.

### Value

Value to remap.

## Variables

### Clamp

When enabled, the normalized value is clamped to [0, 1] before remapping, so the result stays within [ToMin, ToMax].

## Outputs

### Result

Remapped value. If `FromMin == FromMax` the result is `ToMin` (no division by zero).
