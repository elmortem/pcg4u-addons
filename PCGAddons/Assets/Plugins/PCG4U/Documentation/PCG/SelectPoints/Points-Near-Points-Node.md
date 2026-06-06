# PointsNearPointsNode

Splits input points into two groups based on proximity to other points or among themselves.
Produces two outputs: points outside the radius (Results) and points within the radius (NearPoints).

## Inputs

### OtherPoints

Additional points to check proximity against. If empty and RemoveThemselves is false, passes through.

#### Remarks
Additional points to check proximity against. If empty and RemoveThemselves is false, passes through.

### Points

Input points to be tested against the radius condition.

#### Remarks
Input points to be tested against the radius condition.

### Radius

Radius threshold used to determine whether points are near.

#### Remarks
Radius threshold used to determine whether points are near.

## Variables

### RemoveThemselves

If true, points qualifying as near are removed from further checks (added into the octree as they are found).

#### Remarks
If true, points qualifying as near are removed from further checks (added into the octree as they are found).

### UseScale

If true, scales the radius per point using its Scale.

#### Remarks
If true, scales the radius per point using its Scale.

### WorldCenter

Center of the octree used for spatial queries.

#### Remarks
Center of the octree used for spatial queries.

### WorldSize

Size of the octree used for spatial queries.

#### Remarks
Size of the octree used for spatial queries.

## Outputs

### NearPoints

Points that are within the radius condition.

#### Remarks
Points that are within the radius condition.

### Results

Points that are outside the radius condition.

#### Remarks
Points that are outside the radius condition.

