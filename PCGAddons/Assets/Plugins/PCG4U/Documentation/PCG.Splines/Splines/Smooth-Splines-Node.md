# SmoothSplinesNode

Smooths each input spline by averaging knot positions with their neighbours.
Uses an iterative Laplacian smoothing pass; higher iterations and strength produce a softer curve.
On open splines the endpoints stay fixed; closed splines smooth all knots around the loop.
Splines with two knots or less are passed through unchanged.

## Inputs

### Splines

The input list of splines to smooth.

### Iterations

Number of smoothing passes. Higher values smooth more.

### Strength

Smoothing amount per pass, in the [0..1] range.
0 leaves knots in place, 1 moves each knot fully to the midpoint of its neighbours.

## Outputs

### Results

The list of smoothed splines, preserving the Closed state of each input.
