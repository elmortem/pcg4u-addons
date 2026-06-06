# PairPointsCycleNode

A node that processes input points in pairs, allowing operations on each pair.
Key features:
- Accepts a list of points through the Points input port
- Splits input points into pairs
- For each pair:
- Outputs the first point of the pair through the First port
- Outputs the second point of the pair through the Second port
- Allows processing this pair through a subgraph
- Collects processing results through the StepResults input port
- Combines all processed results into a final output list

## Inputs

### Points

Input list(s) of points to be processed in pairs.

#### Remarks
This input port accepts a list of points, which are then split into pairs.

### StepResults

Results produced by the subgraph for the current pair (override input).

## Outputs

### Results

Accumulated results produced by processing each pair.

#### Remarks
This output port contains the accumulated results of processing each pair.

### Second

The second element of the current pair.

\r