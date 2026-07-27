# PointsByAttributeNode

Selects the points that satisfy a comparison against an attribute value.
The node reads the value with a selector. A selector that starts with `$` is a built-in point channel, for example `$position.y`. A selector without the prefix is a named attribute column, for example `variant`.
The node reads Int and Bool columns as float values. A selector that does not exist gives 0.

## Inputs

### Points

The input list of points to select from.

### Threshold

The value that the node compares the attribute to.

## Variables

### Compare

The comparison: Less, LessOrEqual, Greater, GreaterOrEqual, Equal or NotEqual.

### Enabled

Enables the filter. If you disable the node, all points go to Results.

### PreviewRange

The value range that maps the preview attribute to the color ramp.

### PreviewSelector

The attribute that gives the color of the preview points. Keep this field empty to use the default color.

### Selector

The selector that gives the compared value.

## Outputs

### Rejected

The points that do not satisfy the comparison.

### Results

The points that satisfy the comparison.
