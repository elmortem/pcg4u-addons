# LookAtPointsNode

Rotates each point's angle around its normal so it faces a target.
With target points connected, each point faces its nearest target; otherwise all points face the single Target position.

## Inputs

### Points

The input list(s) of points to rotate.

### Target

The world position points face when no target points are connected.

### TargetPoints

The points to face. Each point turns toward its nearest target.

## Outputs

### Results

The processed list of points with updated angles.

