# Vector3ToPointsNode

This class `Vector3ToPointsNode` converts Vector3 input values into a list of PointData objects.
It takes a Vector3 input value, creates PointData objects with the input value as the position,
sets the normal to Vector3.up, and the scale to 1f.
The resulting list of PointData objects is stored in the Results output field.

## Inputs

### Value

Input Vector3 value(s) that will be converted into points at the given position.

## Outputs

### Results

Output list of created points.

