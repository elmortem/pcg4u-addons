# BlocksToRoadsNode

Builds road ribbons from block edges that carry a Width attribute.
Edges are grouped by depth class, chained into polylines, offset sideways into ribbons of their width, and merged together into a single region set.

## Inputs

### Blocks

Blocks whose edges carry a Width attribute (output of Assign Road Class By Depth).

## Variables

### Join

Corner style where road segments meet: Round, Miter or Square.

### Cap

End style of open road ends: Butt, Square or Round.

### MiterLimit

Miter length limit, used when Join is Miter.

## Outputs

### Roads

The merged road ribbons as a region set.
