# JoinSplinesNode

Greedily merges open splines whose endpoints are close together into longer chains.
Each open spline becomes a chain of knot positions; chains are connected end-to-end when their endpoints fall within Threshold, with no duplicate knots at the joints.
A chain that loops back to its own start becomes a closed spline. Closed input splines pass through unchanged.

## Inputs

### Splines

The input list of splines to join.
Splines with one knot or less are skipped.

### Threshold

Maximum distance between two endpoints for them to be joined, in world units.

## Outputs

### Results

The list of joined splines. Merged chains that loop back on themselves are marked Closed.
