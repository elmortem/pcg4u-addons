# PointsBySlopeNode

Filters points by the angle between their normal and an up direction, outputting selected and removed sets.

## Inputs

### MaxAngle

Maximum slope angle in degrees (inclusive).

### MinAngle

Minimum slope angle in degrees (inclusive).

### Points

The input list(s) of points to filter.

### Up

The reference up direction the slope angle is measured against.

## Outputs

### RemovedPoints

Points whose slope angle is outside [MinAngle, MaxAngle].

### Results

Points whose slope angle is in [MinAngle, MaxAngle].

