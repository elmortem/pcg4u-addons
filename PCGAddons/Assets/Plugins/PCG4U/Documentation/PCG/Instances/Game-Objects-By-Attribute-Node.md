# GameObjectsByAttributeNode

Spawns game object instances and selects the prefab set by an integer point attribute.
The node reads the rule key with a selector. A selector that starts with `$` is a built-in point channel, for example `$density`. A selector without the prefix is a named attribute column, for example `lotId`.
The node rounds the value to an integer and then finds a rule. In the rule, the node selects a prefab with a weighted random. If no rule applies, the node uses the fallback list. If the fallback list is empty, the node skips the point.
If the points have a `scale3` attribute, the node applies this attribute as a non-uniform scale multiplier on the instance.

## Inputs

### Fallback

The weighted prefab list for the points that no rule applies to.

### Points

The input points that give the position of the instances.

### Rules

The rules that map a key to a weighted prefab list.

### Seed

The seed of the weighted prefab selection. A value of -1 selects a random seed.

## Variables

### Enabled

Enables the instance generation.

### Match

Selects how the attribute value finds a rule. Exact uses the rule with the same key. Modulo uses the rule at the index that the value modulo the rule count gives.

### Selector

The selector that gives the rule key.

## Outputs

### Results

The generated game object instances.
