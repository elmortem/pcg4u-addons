# LotsFromBlockNode

Slices each block into lots with parallel strips along its long edge.
Every lot is roughly LotWidth wide and is tagged with a lotId.

## Inputs

### Blocks

Blocks to slice into lots.

### LotWidth

Target width of each lot strip.

## Outputs

### Lots

The lots as a region set, each tagged with a lotId.
