# PoissonPointsNode

Thins any incoming points down to a minimum distance, scattering them evenly without clumping. Build the Poisson distribution by composition: feed a surface generator into this filter (`*SurfaceNode → PoissonPointsNode`).

## Inputs

### MinDistance

The minimum distance allowed between accepted points. Larger values yield sparser, more evenly spaced points and fewer results.

### Points

The input list(s) of points to thin.

## Outputs

### Results

The thinned points. Selection is greedy in input order: each point is kept only when no already-accepted point lies within `MinDistance`. The node does not cap the result by count, only by distance.
