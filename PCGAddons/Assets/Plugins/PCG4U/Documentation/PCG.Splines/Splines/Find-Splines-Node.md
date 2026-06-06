# FindSplinesNode

Finds and transforms splines from SplineContainer components in the scene.
This node searches for splines either by GameObject name or tag, transforms them
to world space, and provides real-time updates when source splines are modified.

## Inputs

### Name

The name of GameObjects containing SplineContainer components to find.
Takes precedence over Tag if both are specified.

### Tag

The tag to search for GameObjects containing SplineContainer components.
Only used if Name is empty or null.

## Outputs

### Results

The list of found splines, transformed to world space coordinates.
Each spline maintains its closed/open state and tangent modes from the source.

