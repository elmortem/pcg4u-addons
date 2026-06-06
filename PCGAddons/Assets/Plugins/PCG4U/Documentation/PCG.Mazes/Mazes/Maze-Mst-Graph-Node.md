# MazeMstGraphNode

Generates a maze by computing a minimum spanning tree (MST) from an input graph using randomized edge weights.
Also outputs dead-end points (nodes with single edge).

## Inputs

### Graph

Input graph to convert into maze.

### Seed

Seed for deterministic randomization of edge weights. Use -1 for non-deterministic behavior.

## Outputs

### EndPoints

Dead-end points in the maze (nodes with only one connection).

### Result

Output maze graph (minimum spanning tree).

