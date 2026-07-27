# SortPointsByAttributeNode

Sorts the points by an attribute value.
The node reads the sort key with a selector. A selector that starts with `$` is a built-in point channel, for example `$position.x`. A selector without the prefix is a named attribute column, for example `height`.
The sort is stable: points with equal keys keep their input order. The node keeps the attributes of each point.

## Inputs

### Points

The input list of points to sort.

## Variables

### Descending

Sorts from the largest value to the smallest value.

### Enabled

Enables the sort. If you disable the node, the output keeps the input order.

### PreviewRange

The value range that maps the preview attribute to the color ramp.

### PreviewSelector

The attribute that gives the color of the preview points. Keep this field empty to use the default color.

### Selector

The selector that gives the sort key.

## Outputs

### Results

The points in the sorted order.
