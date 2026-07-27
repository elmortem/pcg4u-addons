# RandomAttributeNode

Writes a random value into a point attribute.
The node writes to the target with a selector. A selector that starts with `$` is a built-in point channel, for example `$angle`. A selector without the prefix is a named attribute column, for example `variant`.
The node uses one random value for each point. The same seed always gives the same sequence.

## Inputs

### Max

The upper bound of the random range.

### Min

The lower bound of the random range.

### Points

The input list of points to process.

### Seed

The seed of the random sequence. A value of -1 selects a random seed.

## Variables

### Enabled

Enables the write. If you disable the node, the output is equal to the input.

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

The processed list of points with the random attribute value.
