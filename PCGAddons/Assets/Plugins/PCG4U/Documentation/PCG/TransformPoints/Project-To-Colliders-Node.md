# ProjectToCollidersNode

Projects input points onto physics colliders by casting a ray from each point.
Points that hit a collider are moved to the hit position; points that miss are passed through unchanged to a separate output.

## Inputs

### Direction

The direction rays travel when Mode is Direction.

### MaxDistance

The maximum ray length.

### Points

The input list(s) of points to project.

## Variables

### AlignNormal

If enabled, projected points adopt the hit surface normal.

### Layers

The physics layers the rays can hit.

### Mode

The projection direction: a fixed Direction, or along each point's inverted Normal.

## Outputs

### Missed

Points that did not hit any collider, passed through unchanged.

### Results

Points projected onto the hit colliders.

