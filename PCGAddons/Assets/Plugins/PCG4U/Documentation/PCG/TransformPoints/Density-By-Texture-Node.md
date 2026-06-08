# DensityByTextureNode

Changes point density based on a texture channel sampled over a rectangle in the XZ plane.
The texture is read without the Read/Write flag (via a temporary RenderTexture blit) and sampled bilinearly.

A point at `Offset` maps to pixel (0,0); a point at `Offset + (Size.x, *, Size.y)` maps to the opposite corner.
Points outside the rectangle get value 0.

## Inputs

### Texture

The Texture2D to sample. Read/Write does not need to be enabled.

### Offset

World position mapped to the texture origin (pixel 0,0).

### Size

The world-space width (X) and depth (Z) the texture is stretched across.

### Points

The input list(s) of points to process.

## Variables

### Channel

The channel to sample: R, G, B, A or Luminance.

### Mode

How the sampled value is applied to existing density: Add, Mult or Set.

## Outputs

### Results

The processed list of points with updated density values, clamped to [0,1].
