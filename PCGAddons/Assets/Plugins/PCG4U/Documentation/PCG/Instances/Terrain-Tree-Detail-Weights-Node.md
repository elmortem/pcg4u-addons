# TerrainTreeDetailWeightsNode

Spawns terrain tree detail instances at input points using a weighted list of tree details.
Each point selects a detail according to weights; an optional seed enables determinism.

## Inputs

### Points

Input points where tree details will be created.

#### Remarks
Each point will select a tree detail according to the weights in the `Details` list.

### Seed

Seed for deterministic weighted selection. Use -1 for non-deterministic behavior.

#### Remarks
If -1, the node will use a random seed for each computation.

## Variables

### Details

Weighted tree detail entries to choose from.

#### Remarks
Each entry in the list has a weight associated with it; the node will select a detail
according to these weights.

### Enabled

Enables or disables spawning.

#### Remarks
If false, the node will not spawn any tree detail instances.

## Outputs

### Results

Output list of generated tree detail instances.

#### Remarks
The node will output a list of tree detail instances, each containing the point where the
instance was spawned, the detail info used, and the height and width of the instance.

