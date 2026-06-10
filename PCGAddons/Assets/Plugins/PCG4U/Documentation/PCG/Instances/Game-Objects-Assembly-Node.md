# GameObjectsAssemblyNode

Stamps a prefab "collage" onto every input point. The prefab is split into its direct
child elements, and a copy of the whole collage is placed at each point, turning every
element into a separate instance with per-element variations (position/angle/scale jitter
and random drop-out). If the prefab has no children, the prefab itself is placed on each
point as a degenerate single-element collage.

Output is a `List<GameObjectInstanceData>`, fully compatible with `GameObjectInstanceMaker`.

## Inputs

### Points

Input points where collage copies will be placed.

### Prefab

Prefab collage to stamp. Its direct children become the collage elements; a nested group is
instanced as a single element.

### Seed

Seed for deterministic variations. Use 0 or less for non-deterministic behavior — a random seed is generated and stored on bind.

### Position Jitter

Amplitude of the random per-element world-space offset: offset = `Range(-PositionJitter, PositionJitter)`.

### Angle Jitter Min

Lower bound of the random yaw added to each element, in degrees.

### Angle Jitter Max

Upper bound of the random yaw added to each element, in degrees.

### Scale Jitter

Random scale multiplier range applied per element (x — min, y — max).

### Keep Chance

Probability `[0..1]` of keeping each element. 1 keeps the whole collage.

## Variables

### Enabled

Enables or disables spawning at runtime/compute.

## Outputs

### Results

Output list of instantiated objects data.
