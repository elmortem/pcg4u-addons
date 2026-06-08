# JoinSplinesNode

Joins splines whose endpoints are close to each other into single continuous splines.
Two splines are merged when the gap between their endpoints is below Threshold.

## Inputs

### Splines

The input list of splines to join.

### Threshold

The maximum distance between endpoints for two splines to be joined.

## Outputs

### Results

The output list of joined splines.
