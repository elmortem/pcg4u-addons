# PointsNearRegionsNode

Separates input points into two sets based on proximity to filled regions.
Each point is treated as a disc of the given radius in the XZ plane; a region is the filled area of a polygon (outer contour minus holes) at the region set plane.
A point goes to NearPoints when its disc touches a region: its center is inside the region, or the distance from the center to any boundary edge (contour or hole) is within the radius. All other points go to Results.
The test is 2D only — point height and the region set PlaneY are ignored.

## Inputs

### Points

Input points to test for proximity.

### Regions

Region set to measure against. Multiple sets are merged upstream into one. A point is near if it touches any of its polygons.

### Radius

Radius of each point's disc (its "size") used for the proximity test.

## Variables

### UseScale

If true, the effective radius per point is Radius multiplied by the point's Scale.

## Outputs

### Results

Points whose disc does not touch any region.

### NearPoints

Points whose disc touches a region (center inside it, or within Radius of a boundary edge, including hole edges).
