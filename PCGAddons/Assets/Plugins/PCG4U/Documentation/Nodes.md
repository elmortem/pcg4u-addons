## How it works

PCG nodes are graph elements that process and generate procedural data. Nodes connect via input/output ports to form processing pipelines.

Node execution flow:
1. Nodes with inputs request data from connected upstream nodes
2. Upstream nodes compute their results (with caching)
3. Node processes received data and produces output
4. Output can be used by downstream nodes or final [[Result Node|PCG/Result-Node]]

Many nodes have 2 checkboxes in bottom-left corner:
* **Left checkbox** - enables Gizmo visualization in Scene view
* **Right checkbox** - locks/caches calculations for optimization (useful for large generations)

## Core Nodes

* [[Result Node|PCG/Result-Node]] - final node that applies instances to scene via Instancers

## Node Categories

### Create Points
* [[Plane Surface Node|PCG/CreatePoints/Plane-Surface-Node]]
* [[Box Surface Node|PCG/CreatePoints/Box-Surface-Node]]
* [[Sphere Surface Node|PCG/CreatePoints/Sphere-Surface-Node]]
* [[Mesh Surface Node|PCG/CreatePoints/Mesh-Surface-Node]]
* [[Terrain Surface Node|PCG/CreatePoints/Terrain-Surface-Node]]
* [[Collider Surface Node|PCG/CreatePoints/Collider-Surface-Node]]
* [[Copy Points To Points Node|PCG/CreatePoints/Copy-Points-To-Points-Node]] - stamps an assembly on each target point

### Transform Points

A point can carry an oriented box. The box makes it possible to keep apart props that a radius does not describe, for example a house or a fence. The box is stored in the `boundsExtents` and `boundsCenter` attributes, in the local space of the point, before the scale, the angle and the normal are applied. The scale of the point is applied to the box at the moment of the overlap test, thus a scale change after the Point Bounds node is correct.

The usual chain is **Point Bounds → Set Attribute (`priority`) → Prune Points**. There is no separate node for the priority: write it with the Set Attribute node into the `priority` attribute.

* [[Point Bounds Node|PCG/TransformPoints/Point-Bounds-Node]] - writes an oriented box to each point from a value, a prefab or a prefab list
* [[Apply Hierarchy Node|PCG/TransformPoints/Apply-Hierarchy-Node]] - builds the transform of each point again from the hierarchy attributes and removes the orphans

### Select Points
* [[Percent Points Node|PCG/SelectPoints/Percent-Points-Node]]
* [[Points By Density Node|PCG/SelectPoints/Points-By-Density-Node]]
* [[Points By Slope Node|PCG/SelectPoints/Points-By-Slope-Node]]
* [[Points By Height Node|PCG/SelectPoints/Points-By-Height-Node]]
* [[Points By Attribute Node|PCG/Attributes/Points-By-Attribute-Node]] - selects points by a comparison against an attribute
* [[Poisson Points Node|PCG/SelectPoints/Poisson-Points-Node]] - thins points to a minimum distance
* [[Prune Points Node|PCG/SelectPoints/Prune-Points-Node]] - merges point streams and removes the boxes that overlap, by priority
* [[Points Near Points Node|PCG/SelectPoints/Points-Near-Points-Node]] - selects the points that are near the target points

### Points
* [[Sort Points By Attribute Node|PCG/Attributes/Sort-Points-By-Attribute-Node]] - stable sort by an attribute value

### Attributes

Points carry named attributes in addition to their position, normal, angle, scale and density. Attribute nodes address the data with a **selector** string:

* a selector that starts with `$` is a built-in point channel - `$position` (and `$position.x/.y/.z`), `$normal` (and `.x/.y/.z`), `$angle`, `$scale`, `$density`;
* a selector without the prefix is a named attribute column, for example `variant` or `lotId`.

Names are case-sensitive. A read of a column that does not exist gives 0. You can write into a named column with the Float, Int, Bool or Float3 type. Into `$position` and `$normal` you can write Float3 values only, into the other channels Float values only.

Each attribute node has the **PreviewSelector** and **PreviewRange** fields. If you set a selector, the Scene view colors the preview points by the attribute value with a blue-green-red ramp.

* [[Set Attribute Node|PCG/Attributes/Set-Attribute-Node]] - writes a constant value into an attribute
* [[Attribute Math Node|PCG/Attributes/Attribute-Math-Node]] - applies an arithmetic operation to two point values
* [[Attribute Remap Node|PCG/Attributes/Attribute-Remap-Node]] - remaps a value through a curve into a new range
* [[Random Attribute Node|PCG/Attributes/Random-Attribute-Node]] - writes a random value into an attribute
* [[Points By Attribute Node|PCG/Attributes/Points-By-Attribute-Node]] - selects points by a comparison against an attribute
* [[Sort Points By Attribute Node|PCG/Attributes/Sort-Points-By-Attribute-Node]] - stable sort by an attribute value

An assembly is a group of prefabs that an artist made by hand. The Assembly Capture node turns such a group into points with hierarchy attributes (`id`, `parentId`, `depth`, `relPosition`, `relEuler`, `relScale`, `prefabIndex`). The Copy Points To Points node stamps the assembly on other points, and the Apply Hierarchy node builds the transforms again after you filter or change the elements. The usual chain is **Assembly Capture → Copy Points To Points → Points By Attribute → Apply Hierarchy → GameObjects By Attribute**.

### Noises
* [[Perlin Noise Node|PCG/Noises/Perline-Noise-Node]]
* [[Simplex Noise Node|PCG/Noises/Simplex-Noise-Node]]
* [[Worley Noise Node|PCG/Noises/Worley-Noise-Node]]
* [[FBM Noise Node|PCG/Noises/Fbm-Noise-Node]] - wraps a source noise with octave summation
* [[Ridged Noise Node|PCG/Noises/Ridged-Noise-Node]] - wraps a source noise to produce ridges

### Instances
* [[GameObjects Node|PCG/Instances/Game-Objects-Node]] - converts points to GameObject instances
* [[GameObjects By Attribute Node|PCG/Instances/Game-Objects-By-Attribute-Node]] - selects the prefab set by an integer point attribute
* [[GameObjects Assembly Node|PCG/Instances/Game-Objects-Assembly-Node]] - stamps a prefab collage on each point as per-element instances
* [[Assembly Capture Node|PCG/Instances/Assembly-Capture-Node]] - turns an assembly that an artist made by hand into points with hierarchy attributes
* [[Terrain Grass Detail Node|PCG/Instances/Terrain-Grass-Detail-Node]] - converts points to terrain details

## Addon Nodes

Addon nodes are shipped in their own packages. See [[Addons|Addons]] for the list of addons and how to install them; each addon's node documentation is included inside its package.