# GameObjectWeightsNode

Spawns GameObject instances at input points using a weighted list of prefabs.
Each point selects a prefab according to the configured weights, optionally using a seed for reproducibility.

## Inputs

### Points

Input points where instances will be placed.

### Seed

Seed for deterministic weighted selection. Use 0 or less for non-deterministic behavior — a random seed is generated and stored on bind.

## Variables

### Enabled

Enables or disables spawning at runtime/compute.

### Weights

Weighted prefab entries used to choose which prefab to spawn per point.

## Outputs

### Results

Output list of instantiated objects data.

