# AssignRoadClassByDepthNode

Assigns a road width to block edges based on their cut-depth class.
For each cut edge the width is MaxWidth multiplied by WidthByDepth evaluated at the normalized class (cutDepth / MaxDepth). Only edges whose class is within MinDepth..MaxDepth receive a width.
The original boundary is class 0: the default MinDepth of 1 excludes it, while MinDepth 0 turns the perimeter into a road as well.

## Inputs

### Blocks

Blocks with cut edges tagged by depth class (output of Subdivide Region).

### MaxWidth

Maximum road width, multiplied by the WidthByDepth curve.

### MinDepth

Lowest cut-depth class that receives a width. 0 includes the outer boundary.

### MaxDepth

Highest cut-depth class that receives a width, and the value used to normalize the class for the curve.

## Variables

### WidthByDepth

Curve mapping the normalized class (0..1) to a width factor (0..1). By default wide near the boundary and narrow deeper in.

## Outputs

### Result

The blocks with a Width attribute written on qualifying edges.
