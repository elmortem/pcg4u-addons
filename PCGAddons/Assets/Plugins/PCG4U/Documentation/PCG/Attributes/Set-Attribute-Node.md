# SetAttributeNode

Writes a constant value into a point attribute.
The node reads the target with a selector. A selector that starts with `$` is a built-in point channel, for example `$density` or `$position`. A selector without the prefix is a named attribute column, for example `lotId`.
The node copies the input points to the output and then writes the value into each output point.

## Inputs

### Points

The input list of points to process.

### Value

The scalar value for the Float, Int and Bool types.

### Value3

The vector value for the Float3 type.

## Variables

### Enabled

Enables the write. If you disable the node, the output is equal to the input.

### Mode

Selects how the node combines the new value with the current value: Set, Add or Multiply.

### PreviewRange

The value range that maps the preview attribute to the color ramp.

### PreviewSelector

The attribute that gives the color of the preview points. Keep this field empty to use the default color.

### Selector

The target channel or attribute column.

### Type

The value type of the target attribute column: Float, Int, Bool or Float3.

## Outputs

### Results

The processed list of points with the new attribute value.
