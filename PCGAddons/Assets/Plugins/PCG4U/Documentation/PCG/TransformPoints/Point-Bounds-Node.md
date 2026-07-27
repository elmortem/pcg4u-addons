# PointBoundsNode

Writes an oriented box to each input point.
The node stores the box in the `boundsExtents` and `boundsCenter` attributes.
The values are in the local space of the point, before the scale, the angle and the normal of the point are applied.
The Prune Points node reads these attributes and removes points that overlap.

## Inputs

### Center

The offset of the box center in the local space of the point.
The node uses this value when Source is Explicit.

### Extents

The half size of the box in the local space of the point.
The node uses this value when Source is Explicit.

### Padding

The value that the node adds to each half size component.
A negative value can reduce a half size to zero. It cannot make a half size negative.

### Points

The input list of points to process.

### Prefab

The prefab that gives the box size for all points.
The node uses this value when Source is Prefab.
The node computes the box from the renderers of the prefab.
A prefab without a renderer gives a box with a size of zero.
The node recomputes the bounds when the prefab content or a renderer transform changes.

### Prefabs

The prefab list that gives the box size for each point.
The node uses this value when Source is PrefabList.
The node reads the prefab index from the attribute that IndexSelector gives.
An index outside of the list gives a box with a size of zero.

## Variables

### Enabled

Enables or disables the bounds write.
When it is disabled, the output is equal to the input.

### IndexSelector

The selector that gives the prefab index for the prefab list.
A selector that starts with '$' is a built-in channel.
Each other selector is a named attribute column.

### PreviewBoxLimit

The maximum number of boxes that the preview shows.
The preview uses the final uniform and non-uniform scale of each point.

### Source

Selects where the box size comes from.
Explicit uses the Extents and Center values.
Prefab uses one prefab.
PrefabList uses a prefab from the list.

## Outputs

### Results

The processed list of points with the bounds attributes.
All written half sizes are zero or positive. A negative point scale mirrors the center offset and uses the scale magnitude for the box size.
