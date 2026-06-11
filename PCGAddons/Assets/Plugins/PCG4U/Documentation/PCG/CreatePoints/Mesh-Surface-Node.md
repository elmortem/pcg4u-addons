# MeshSurfaceNode

Generates points on a mesh surface with specified parameters.
This node creates points on a given mesh using various generation modes and projection techniques,
allowing for customization of point distribution, count, and transformation.

## Inputs

### Angle

The rotation angle (in degrees) applied to the generated points.

### CellSize

The size of cells used in the projection grid for point generation.

### Count

The number of points to generate on the mesh surface.

### Mesh

The input mesh on which points will be generated.

### Normal

The normal vector used for orienting the generated points.

### Offset

The offset applied to the generated points.

### Scale

The scale factor applied to the generated points.

### Seed

The seed for the random number generator.

## Variables

### PointMode

The mode used for generating points on the mesh surface.

### ProjectionMode

The projection mode used for mapping points onto the mesh surface.

### ShowGrid

If enabled, draws the projection grid used for point generation.

### ShowMeshBounds

If enabled, draws the axis-aligned bounds of the mesh in the preview.

## Outputs

### Results

The list of generated points, available as an output.

