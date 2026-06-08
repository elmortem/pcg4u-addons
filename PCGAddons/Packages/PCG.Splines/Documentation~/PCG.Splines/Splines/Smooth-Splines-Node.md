# SmoothSplinesNode

Smooths each input spline by averaging the positions of neighbouring knots.
Useful for removing sharp turns and noise from generated splines.

## Inputs

### Splines

The input list of splines to smooth.

### Iterations

The number of smoothing passes. More iterations produce a smoother result.

### Strength

The blend amount per pass, from 0 (no change) to 1 (full averaging).

## Outputs

### Results

The output list of smoothed splines.
