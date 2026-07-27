## How it works

Instancers are MonoBehaviour components that create and manage objects in the scene. They receive InstanceData from [[Result Node|PCG/Result-Node]] and convert it into actual scene objects.

The system works through several steps:
1. PCG nodes generate lists of [[InstanceData|PCG/Instances/Instance-Data]]
2. [[Result Node|PCG/Result-Node]] collects these lists from connected nodes
3. Result Node passes InstanceData to compatible Instancers on the same GameObject
4. Each Instancer creates its specific object type (GameObjects, terrain details, etc.)

You can have multiple Instancers on one GameObject - each handles its own InstanceData type.

## Built-in Instancers

* [[GameObjectInstanceMaker|PCG/Instances/Game-Object-Instance-Maker]] - spawns prefabs from GameObjectInstanceData; applies the non-uniform `Scale3` multiplier on top of the point scale
* [[TerrainDetailInstanceMaker|PCG/Instances/Terrain-Detail-Instance-Maker]] - places terrain grass/details from TerrainGrassDetailInstanceData

## Custom Instancers

Create custom Instancers by implementing [[IInstanceMaker|PCG/Instances/IInstance-Maker]] interface. You may also need custom InstanceData and nodes to generate it.

See [[InstanceData|PCG/Instances/Instance-Data]] for base class and [[IInstanceMaker|PCG/Instances/IInstance-Maker]] for interface details.