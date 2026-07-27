# PrunePointsNode

Merges all input point streams and removes the points that overlap.
The node compares the boxes that the Point Bounds node writes to the points.
The overlap test uses the footprint of the box on the XZ plane and the interval of the box on the Y axis.
The node ignores the tilt of the normal and uses only the yaw angle of the point.
The node multiplies the box by the scale of the point at the moment of the test, thus a scale change after the Point Bounds node is correct.

The Set Attribute node writes the priority into the `priority` attribute.
A point with a higher priority keeps its position, and the points that overlap it go to the Rejected output.
When two points have an equal priority, the point that comes first in the order of the inputs wins.
A point without the `boundsExtents` attribute has a box with a size of zero.

The order of the points in the outputs is equal to the order of the points in the inputs.

## Inputs

### Padding

The value that the node adds to each half size component before the overlap test.

### Points

The input point streams.
The node merges all streams before the prune.

## Variables

### CheckVertical

Compares the Y interval of the two boxes.
Set it to off to prune points that stand above each other.

### Enabled

Enables or disables the prune.
When it is disabled, all points go to Results and Rejected stays empty.

### PrioritySelector

The selector that gives the priority.
A selector that starts with '$' is a built-in channel.
Each other selector is a named attribute column.
A point without this attribute has a priority of 0.

## Outputs

### Rejected

The points that the prune removed.

### Results

The points that stay after the prune.
