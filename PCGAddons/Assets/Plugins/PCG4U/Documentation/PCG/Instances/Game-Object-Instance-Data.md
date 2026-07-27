# GameObjectInstanceData

Instance data for GameObject prefabs. Contains reference to prefab and point placement data.

## Variables

### Point

Point data defining position, rotation, scale, and other properties.

### Prefab

Prefab to instantiate.

### Scale3

Non-uniform scale multiplier. The instancer applies `Point.Scale * Scale3` as the local scale of the object. The default value is (1, 1, 1) and keeps the uniform point scale.

