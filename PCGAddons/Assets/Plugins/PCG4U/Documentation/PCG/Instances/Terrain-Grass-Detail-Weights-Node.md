# TerrainGrassDetailWeightsNode

Spawns terrain grass detail patches by weighted selection from provided detail infos.
Points are partitioned into patches per selected detail and emitted as results.

## Inputs

### Points

Input points where grass patches will be created.

#### Remarks
Grass patches will be spawned at these points.

### Seed

Seed for deterministic weighted selection. Use -1 for non-deterministic behavior.

## Variables

### Details

Weighted grass detail entries to choose from.

#### Remarks
The weights of the entries will be used to determine the probability of each detail being chosen.

### Enabled

Enables or disables spawning.

#### Remarks
If set to false, no grass patches will be spawned.

## Outputs

### Results

Output list of generated grass detail patches.

#### Remarks
Each entry in the list represents a single grass patch spawned at one of the input points.

