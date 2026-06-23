# Polygon2D

A single 2D polygon in the XZ plane: an outer contour with optional holes. It is the building block of a RegionSet and carries named attributes per edge (used by the city pipeline to tag cuts, road widths and boundaries).

## Properties

### Outer

The outer contour as a closed ring of XZ points.

### Holes

Inner rings cut out of the contour.

### EdgeAttributes

Named attributes stored per edge (outer edges first, then hole edges in order).
