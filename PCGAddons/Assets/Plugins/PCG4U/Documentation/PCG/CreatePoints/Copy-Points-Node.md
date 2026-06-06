# CopyPointsNode

Copies source points to the positions of input points, allowing for transformations and scaling.
This node creates new points by replicating a set of source points at each input point's position,
with options for pre-offset, scaling, and rotation.

## Inputs

### Points

The input list of points where source points will be copied to.

### PreOffset

An offset applied to source points before other transformations.

### Source

The list of points to be copied to each input point's position.

### UseScale

Determines whether the scale of input points should be applied to copied points.

## Outputs

### Results

The list of generated points, available as an output.

