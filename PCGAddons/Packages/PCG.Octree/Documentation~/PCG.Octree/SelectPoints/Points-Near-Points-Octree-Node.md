# PointsNearPointsOctreeNode

Splits input points into two groups based on proximity to other points, using an octree for fast spatial queries.
Produces two outputs: points outside the radius (Results) and points within the radius (NearPoints).
This is an octree-accelerated alternative to the core Points Near Points node, intended for large point sets.

## Inputs

### Points

Input points to be tested against the radius condition.

### OtherPoints

Additional points to check proximity against. If empty and RemoveThemselves is false, points pass through.

### Radius

Radius threshold used to determine whether points are near.

## Variables

### WorldCenter

Center of the octree used for spatial queries.

### WorldSize

Size of the octree used for spatial queries.

### RemoveThemselves

If true, points qualifying as near are removed from further checks as they are found.

### UseScale

If true, scales the radius per point using its Scale.

## Outputs

### Results

Points that are outside the radius condition.

### NearPoints

Points that are within the radius condition.
