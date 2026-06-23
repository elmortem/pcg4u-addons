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
