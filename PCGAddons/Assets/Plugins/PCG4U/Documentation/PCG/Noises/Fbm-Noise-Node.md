# FbmNoiseNode

Wraps a source noise with Fractal Brownian Motion (octave summation).

## Inputs

### Source

Source noise to layer across octaves.

### Octaves

Number of noise layers summed together (clamped to 1..12).

### Lacunarity

Frequency multiplier applied between octaves.

### Persistence

Amplitude multiplier applied between octaves.

## Outputs

### Result

Output FBM noise data wrapping the source.
