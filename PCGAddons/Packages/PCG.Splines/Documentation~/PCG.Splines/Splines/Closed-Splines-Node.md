# ClosedSplinesNode

Separates input splines into two categories: closed and open splines.
This node takes a list of splines and sorts them based on their closed property,
providing separate outputs for closed and open splines.

## Inputs

### Splines

The input list of splines to be sorted.
Can contain both closed and open splines.

## Outputs

### OpenedSplines

The output list containing only open splines (Spline.Closed = false).

### Results

The output list containing only closed splines (Spline.Closed = true).

