## Description
Addon for working with Unity Splines. Provides nodes for creating, modifying, and selecting points based on splines.

## Data types
* [[Pcg Spline Set|PCG.Splines/Splines/Pcg-Spline-Set]]

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

## Attributes on splines

Every spline port carries a `PcgSplineSet`. The set holds the splines and a table of named attributes, with one row for each spline. The attributes move along the graph, so a downstream node can select or change points by the properties of the source spline.

Two examples:

* Put lamps only along the main streets. Use the `roadClass` attribute that Blocks To Roads writes.
* Put a bench only on one side of the road. Use the `splineSide` attribute that Points Offset Splines writes.

A node that makes points from a spline copies the attribute row of that spline to each new point. The node also adds the position of the point along the spline.

Keep this rule: a value that changes along the spline goes to an embedded Unity channel, for example `pcg.width`. A value that is constant for the full spline goes to an attribute.

Refer to [[Pcg Spline Set|PCG.Splines/Splines/Pcg-Spline-Set]] for the full list of names.

## Spline To Terrain

Projects spline knots onto a `TerrainData` heightfield before downstream mesh or point generation.

* **Splines** — source world-space splines.
* **Terrain** — heightfield data. When empty, valid inputs pass through unchanged.
* **Terrain Origin** — world-space position of the Terrain object. `TerrainData` does not store this transform, so this is a coordinate origin rather than a visual offset.
* **Height Offset** — world-Y lift applied after projection.
* **Align To Terrain Normal** — aligns knot Up with the sampled terrain normal while preserving the evaluated curve tangents.
* **Resample / Step** — optionally rebuilds the curve with the same fixed-step AutoSmooth algorithm as `Resample Splines` before projection. Disabled mode copies knots, tangent metadata and embedded spline data.

Out-of-bounds knots keep their original height and orientation. The node fits the spline centreline only; it does not drape the full width of a mesh generated later.
