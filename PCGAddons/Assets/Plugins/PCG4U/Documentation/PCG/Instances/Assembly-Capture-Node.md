# AssemblyCaptureNode

Captures an assembly that an artist made by hand and gives one point for each element of the assembly.

The node reads the descendants of the source object. It captures only the descendants that are **prefab instance roots**. A descendant that is not a prefab instance is not captured. The node shows the number of these descendants in its info line. When the source is a prefab asset, the node captures each descendant that has a Renderer component on the object.

Each point holds the transform of the element related to the source object. Thus you can use the points immediately. Each point also holds these hierarchy attributes:

* `id` - the number of the element, from 0, in the order of the capture;
* `parentId` - the `id` of the nearest captured parent, or -1 when there is no captured parent;
* `depth` - the number of captured parents;
* `relPosition`, `relEuler`, `relScale` - the transform related to the nearest captured parent. `relEuler` holds degrees in the ZXY rotation order;
* `prefabIndex` - the index of the prefab of the element in the Prefabs output;
* `scale3` - the scale of the element related to the source object. The node always writes 1 into the point scale and puts all the scale into `scale3`. Thus a non-uniform scale is not lost.

For each registered name in the Tags list, the node makes a bool attribute with the same name. The value is true when the element has this Unity tag. The node ignores duplicate registered names after the first name. It also ignores empty and invalid names. The node reports the number of invalid names in its info line.

The node tracks the validated tag list and the tag state of each element. A tag registration change or a tag value change causes a new capture.

Use the Apply Hierarchy node to build the transforms again after you filter or change the elements.

## Inputs

### Source

The root of the assembly. It is a prefab asset or an object in the scene.

## Variables

### Enabled

Enables or disables the capture. When it is off, the outputs are empty.

### IncludeInactive

Also captures the descendants that are not active.

### CaptureRoot

Also captures the root object, but only when the root object is a prefab instance.

### Tags

The list of Unity tags. Each unique registered tag makes one bool attribute with the same name. Empty and invalid names are ignored and reported.

## Outputs

### Elements

One point for each captured element of the assembly.

### Prefabs

The different prefabs of the captured elements. The `prefabIndex` attribute indexes this list.
