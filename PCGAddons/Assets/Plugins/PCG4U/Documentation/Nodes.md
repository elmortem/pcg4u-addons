## How it works

PCG nodes are XNode-based graph elements that process and generate procedural data. Nodes connect via input/output ports to form processing pipelines.

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

### Select Points
* [[Percent Points Node|PCG/SelectPoints/Percent-Points-Node]]
* [[Points By Density Node|PCG/SelectPoints/Points-By-Density-Node]]

### Instances
* [[GameObjects Node|PCG/Instances/Game-Objects-Node]] - converts points to GameObject instances
* [[Terrain Grass Detail Node|PCG/Instances/Terrain-Grass-Detail-Node]] - converts points to terrain details

## Addon Nodes

* [[Spline Nodes|Splines-Addon]]
* [[Maze Nodes|Mazes-Addon]]
* [[SpriteShape Nodes|SpriteShapes-Addon]]
* [[Octree Nodes|Octree-Addon]]
* [[BRG Nodes|BRG-Addon]]