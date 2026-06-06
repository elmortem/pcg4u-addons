# FindMeshNode

Finds a mesh in the scene by GameObject name or tag and exposes its transform data.
Outputs the found mesh, world position, normal (up vector), angle (Z euler), and average scale.

## Inputs

### Name

GameObject name to search for. Leave empty to search by tag.

### Tag

GameObject tag to search for. Used when Name is empty.

## Outputs

### Angle

Z-axis euler angle of the found mesh rotation.

### Mesh

The found mesh (if any).

### Normal

World-space up vector (normal) of the found mesh.

### Position

World position of the found mesh.

### Scale

Average world scale of the found mesh transform.

