# PolygonBooleanNode

Combines two region sets with a boolean operation.
Edges introduced by the operation are tagged as boundary; existing edges keep their attributes.

## Inputs

### A

First region set.

### B

Second region set.

## Variables

### Mode

Boolean operation: Union (A or B), Intersection (A and B), or Difference (A minus B).

## Outputs

### Result

The combined region set.
