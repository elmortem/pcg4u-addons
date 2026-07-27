# SplineToRegionNode

Converts closed splines into filled regions.
Each closed spline becomes the outer contour of a region (its area in the XZ plane); the spline is resampled so no segment is longer than MaxSegmentLength.

## Inputs

### Splines

Closed splines to convert. Multiple connections are merged into one set.

### MaxSegmentLength

Maximum length of a contour segment. Curves are resampled to keep segments within this length.

## Outputs

### Result

The region set built from the splines.

## Attributes

Each region gets the attribute row of its source spline. The node moves the rows only if the number of regions is equal to the number of splines. If the node discarded an open spline or a degenerate contour, the counts are different and the regions get empty rows.
