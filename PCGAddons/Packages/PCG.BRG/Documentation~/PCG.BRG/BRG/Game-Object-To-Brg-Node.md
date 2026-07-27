# GameObjectToBrgNode

Converts GameObject instances into BRG instance data grouped by prefab for batched rendering.

## Inputs

### Instances

Input GameObject instances to be grouped by prefab.

#### Remarks
The input list can contain multiple lists of GameObject instances.

## Variables

### Enabled

Enables or disables conversion.

#### Remarks
If disabled, the node will not perform any conversion and will not output any data.

## Outputs

### Results

Output BRG instance data grouped by prefab.

#### Remarks
The output list contains BRG instance data grouped by prefab.

## Notes

The node copies the non-uniform scale of each instance into the `scale3` attribute of the point cloud. Thus an assembly that uses a non-uniform scale keeps its shape when BatchRendererGroup renders it.

The per-instance color is not available. The renderer uses white for all instances.

