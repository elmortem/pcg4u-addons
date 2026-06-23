# SubdivideRegionNode

Subdivides regions into city blocks using a recursive BSP split.
Each region is split along its long axis with a random jitter, recursively, until pieces are smaller than MinSize or the recursion reaches MaxDepth.
Cut edges are tagged with a depth class (cutDepth >= 1, the original boundary stays class 0); each block stores its recursion depth.

## Inputs

### Region

Region set to subdivide.

### MinSize

Minimum block size. A piece is no longer split once it is smaller than this.

### MaxDepth

Maximum recursion depth of the split.

### SplitJitter

Random offset of each split line from the center, as a fraction of the span (0 = always centered).

### Seed

Seed for the random jitter.

## Outputs

### Blocks

The resulting blocks, with cut edges tagged by depth class.
