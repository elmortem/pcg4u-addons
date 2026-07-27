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

### Centerlines

The centerline of each road as a spline. Each centerline carries the road width in the embedded `pcg.width` channel, because the width can change along a road.

## Attributes

The node writes three attributes to each centerline.

* `roadClass` — the depth class of the edges that made the road. Assign Road Class By Depth uses the same class to set the width.
* `width` — the width of the road, in world units. The value is constant for the full centerline.
* `closed` — true if the centerline is a loop.

Use `roadClass` to make a difference between a main street and a small street downstream. For example, put lamps only along the roads with a low class value.
