PCG4U (Procedural Content Generation for Unity) - node-based tool for creating procedural content in Unity Editor and runtime.

## Overview

PCG4U provides a visual graph-based system for procedural generation. It lets you build complex generation pipelines by connecting nodes that process and transform data.

**Key features:**
* **Node-based workflow** - visual graph editor for building generation logic
* **ScriptableObject graphs** - reusable generation assets
* **Scene integration** - MonoBehaviour component to bridge graphs and scene
* **Extensible system** - create custom nodes, instancers, and data types
* **Performance optimized** - node caching, async execution, preview controls
* **Multiple addons** - Splines, Mazes, SpriteShapes, Octree, BRG support

**Basic workflow:**
1. Create [[PCG Asset|PCG/Pcg-Asset]] (graph with nodes)
2. Add [[PCG Scene Asset|PCG/Pcg-Scene-Asset]] to GameObject in scene
3. Assign graph to scene component
4. Add Instancer components to create actual objects
5. Connect nodes to process data and generate content
6. Use [[Result Node|PCG/Result-Node]] to output instances to scene

## Documentation

* [[PCG Asset|PCG/Pcg-Asset]] - graph asset and editor
* [[PCG Scene Asset|PCG/Pcg-Scene-Asset]] - scene integration component
* [[Nodes|Nodes]] - node types and how they work
* [[Instancers|Instancers]] - creating objects in scene

## Addons
* [[Splines|Splines-Addon]] - Unity Splines integration
* [[SpriteShapes|SpriteShapes-Addon]] - 2D SpriteShape support
* [[Mazes|Mazes-Addon]] - maze and graph generation
* [[BRG|BRG-Addon]] - BatchRendererGroup rendering
