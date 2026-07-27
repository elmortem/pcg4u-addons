# ApplyHierarchyNode

Builds the transform of each point again from the hierarchy attributes. Use this node after you filter the points of an assembly or after you change their relative transform.

The node reads the `id`, `parentId`, `depth`, `relPosition`, `relEuler` and `relScale` attributes. It resolves the points in the order of the `depth` value, from the smallest value. For each point it multiplies the relative transform by the transform of its parent. A point that has no parent (`parentId` is -1) keeps its relative transform as the final transform.

**An orphan is a point whose parent is absent, or whose parent is an orphan.** Thus, when a filter removes a parent, all its children become orphans.

A hierarchy cycle makes each cycle member an orphan. A descendant of a cycle is also an orphan, but it is not a cycle member. The node info reports the number of cycle members, not the number of cycles or descendants.

The node writes 1 into the point scale and puts all the scale into the `scale3` attribute. It writes `scale3` always, also when the input has no such attribute.

The order of the points on the outputs is the same as the order on the input.

When the input has no `depth` attribute, the node uses 0 for all the points. Then only the points that have no parent stay in the Results.

## Inputs

### Points

The points with the `id`, `parentId`, `depth` and `rel*` attributes. You can connect more than one input. The node merges all the inputs.

## Variables

### Enabled

Enables or disables the rebuild. When it is off, the results are the input points.

### RemoveOrphans

Sends the orphans to the Orphans output. When it is off, the orphans stay in the Results with their transform unchanged, and the Orphans output is empty.

## Outputs

### Results

The points with the transform built again.

### Orphans

The points whose parent is absent, is an orphan, or is part of a cycle.
