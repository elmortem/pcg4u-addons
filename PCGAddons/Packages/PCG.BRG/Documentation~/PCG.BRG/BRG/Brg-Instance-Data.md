# BrgInstanceData

Instance data for BatchRendererGroup instancing. Contains prefab and points grouped for efficient rendering.

## Variables

### Points

The point cloud that gives the transform matrix of each instance. The cloud also holds the `scale3` attribute, which gives the non-uniform scale of the instance.

The maker multiplies the uniform `Point.Scale` by `scale3` to get the final scale. This is the same rule as in the core Game Object Instance Maker.

### Prefab

Prefab to render in batch.

