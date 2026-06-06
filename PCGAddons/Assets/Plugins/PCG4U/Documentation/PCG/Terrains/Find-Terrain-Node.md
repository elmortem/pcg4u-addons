# FindTerrainNode

Finds a Terrain in the scene by name or tag and exposes its TerrainData and position.

## Inputs

### Name

GameObject name to search for. Leave empty to search by tag.

#### Remarks
If empty, the node will search for a GameObject with the tag specified in `Tag`.

### Tag

GameObject tag to search for. Used when Name is empty.

#### Remarks
If Name is empty, the node will search for a GameObject with this tag.

## Outputs

### Position

World position of the found Terrain transform.

#### Remarks
The world position of the found Terrain transform.

### Terrain

Output TerrainData of the found Terrain.

#### Remarks
The TerrainData of the found Terrain.

