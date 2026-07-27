# GameObjectInstanceMaker

Instancer implementation for GameObject prefabs. Manages adding instances to the scene hierarchy.

The local scale of each object is `Point.Scale * Scale3`, where `Scale3` is the non-uniform multiplier on [[GameObjectInstanceData|PCG/Instances/Game-Object-Instance-Data]]. The instance nodes fill this multiplier from the `scale3` point attribute if the points have this column.

