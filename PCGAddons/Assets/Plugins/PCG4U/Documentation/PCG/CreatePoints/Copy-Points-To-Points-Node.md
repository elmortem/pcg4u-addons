# CopyPointsToPointsNode

Stamps an assembly on each target point. For each target point and each source element the node gives one point.

The node computes the final transform of each element immediately. Thus the preview is correct and you do not need the Apply Hierarchy node for a simple graph.

The node gives each output point a dense identifier. It gives source rows dense indexes in source-cloud order and row order. For each copy, the output `id` range starts after the preceding copy. Duplicate or sparse source IDs cannot make output IDs collide.

The node resolves a parent only in the source cloud that owns the row. It then maps the parent into the same copy. A missing parent stays missing and cannot bind to a row from another source cloud or copy. The node also writes the `copyIndex` attribute with the number of the target point.

For the elements that have no parent (`parentId` is -1) the node writes the `relPosition`, `relEuler` and `relScale` attributes again. The new values hold the transform of the target point together with the transform of the element. Thus the Apply Hierarchy node gives the same result.

**The merge rule for the attributes**: the source wins. The node copies all the attributes of the source element. Then it writes the value of the target point only for the attribute names that the source does not have.

## Inputs

### Source

The assembly points. The node stamps these points on each target point. You can connect more than one source. The node keeps each source cloud as a separate parent-ID namespace before it merges the rows.

### Targets

The target points. Each target point receives one copy of the assembly.

## Variables

### Enabled

Enables or disables the copy. When it is off, the results are the target points.

### InheritTargetRotation

Applies the rotation of the target point to the copy.

### InheritTargetScale

Applies the scale of the target point to the copy.

## Outputs

### Results

One point for each element of each copy.
