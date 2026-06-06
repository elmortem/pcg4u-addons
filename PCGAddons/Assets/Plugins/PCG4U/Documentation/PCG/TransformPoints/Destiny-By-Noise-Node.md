# DestinyByNoiseNode

Assigns density to points by sampling 2D noise at their positions.
Uses either XZ or XY coordinates for noise lookup.

## Inputs

### Noise

Noise generator to sample for density values.

### Points

Input points whose density will be determined by noise.

## Variables

### NoiseAxes

Which axes to use when sampling noise (XZ or XY).

## Outputs

### Results

Output points with noise-based density values.

