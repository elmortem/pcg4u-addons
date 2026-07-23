## Description
Addon for working with Unity Splines. Provides nodes for creating, modifying, and selecting points based on splines.

## Nodes

### Splines
* [[Spline Node|PCG.Splines/Splines/Spline-Node]]
* [[Spline Around Points Node|PCG.Splines/Splines/Spline-Around-Points-Node]]
* [[Random Spline Node|PCG.Splines/Splines/Random-Spline-Node]]
* [[Find Splines Node|PCG.Splines/Splines/Find-Splines-Node]]
* [[Closed Splines Node|PCG.Splines/Splines/Closed-Splines-Node]]
* [[Change Spline Position Node|PCG.Splines/Splines/Change-Spline-Position-Node]]
* [[Spline From Points Node|PCG.Splines/Splines/Spline-From-Points-Node]]
* [[Resample Splines Node|PCG.Splines/Splines/Resample-Splines-Node]]
* **Spline To Terrain**
* [[Smooth Splines Node|PCG.Splines/Splines/Smooth-Splines-Node]]
* [[Offset Splines Node|PCG.Splines/Splines/Offset-Splines-Node]]
* [[Join Splines Node|PCG.Splines/Splines/Join-Splines-Node]]
* [[Spline Intersection Node|PCG.Splines/Splines/Spline-Intersection-Node]]
* [[Split Splines Node|PCG.Splines/Splines/Split-Splines-Node]]

### Transform Points
* [[Density By Distance To Splines Node|PCG.Splines/TransformPoints/Density-By-Distance-To-Splines-Node]]

### Select Points
* [[Points By Spline Node|PCG.Splines/SelectPoints/Points-By-Spline-Node]]
* [[Points Near Splines Node|PCG.Splines/SelectPoints/Points-Near-Splines-Node]]

### Create Points
* [[Points Offset Splines Node|PCG.Splines/CreatePoints/Points-Offset-Splines-Node]]
* [[Splines Surface Node|PCG.Splines/CreatePoints/Splines-Surface-Node]]

## Spline To Terrain

Projects spline knots onto a `TerrainData` heightfield before downstream mesh or point generation.

* **Splines** — source world-space splines.
* **Terrain** — heightfield data. When empty, valid inputs pass through unchanged.
* **Terrain Origin** — world-space position of the Terrain object. `TerrainData` does not store this transform, so this is a coordinate origin rather than a visual offset.
* **Height Offset** — world-Y lift applied after projection.
* **Align To Terrain Normal** — aligns knot Up with the sampled terrain normal while preserving the evaluated curve tangents.
* **Resample / Step** — optionally rebuilds the curve with the same fixed-step AutoSmooth algorithm as `Resample Splines` before projection. Disabled mode copies knots, tangent metadata and embedded spline data.

Out-of-bounds knots keep their original height and orientation. The node fits the spline centreline only; it does not drape the full width of a mesh generated later.
