# Variable Node

A pill node that references a blackboard variable by id and exposes its value to the graph.

Variables are declared in the blackboard panel (each has a name and a typed value). Drag a variable from the panel onto the canvas to drop a pill that outputs that variable's value. The concrete value is authored as the blackboard default and can be overridden per scene in the `PcgComponent` inspector.

## Output Ports

The output ports come from the variable's value type:

* Most types expose a single output port (`Value`) typed after the value.
* Multi-port types expose several outputs, drawn as labelled rows below the title.

## Value Types

In addition to the basic types (Float, Int, Vector2/3, Vector2Int, Points, Object To Points, Splines, Sprite Shape), the terrain types are available:

### Terrain Object

A scene `Terrain` reference. The pill exposes two outputs:

* **Terrain** (`TerrainData`) — the terrain's data asset.
* **Offset** (`Vector3`) — the terrain's world position (the same offset used by Find Terrain).

Wire **Terrain** and **Offset** into a Terrain Surface Node to place points on that terrain. Because it stores a scene reference, set the value in the `PcgComponent` inspector (or the graph default when the graph lives directly on a `PcgComponent`). This type is only offered in the blackboard add-variable menu, not in the generic value picker.

Editing the terrain heightmap with a brush invalidates the variable and recomputes any preview that reads it, without a manual refresh.

### Terrain Data

A `TerrainData` asset reference. The pill exposes a single **TerrainData** output, wired into any `TerrainData` input.
