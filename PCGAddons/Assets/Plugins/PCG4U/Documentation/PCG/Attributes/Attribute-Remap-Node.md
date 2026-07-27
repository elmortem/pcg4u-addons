# AttributeRemapNode

Remaps a point value from an input range to an output range through a curve.
The node reads and writes with selectors. A selector that starts with `$` is a built-in point channel, for example `$density`. A selector without the prefix is a named attribute column, for example `height`.
The node normalizes the source value in the input range, applies the curve, and then maps the curve value into the output range. If the input range is empty, the node uses 0 as the normalized value.

## Inputs

### InputMax

The upper bound of the input range.

### InputMin

The lower bound of the input range.

### OutputMax

The upper bound of the output range.

### OutputMin

The lower bound of the output range.

### Points

The input list of points to process.

## Variables

### Curve

The curve that shapes the normalized input value.

### Enabled

Enables the remap. If you disable the node, the output is equal to the input.

### PreviewRange

The value range that maps the preview attribute to the color ramp.

### PreviewSelector

The attribute that gives the color of the preview points. Keep this field empty to use the default color.

### SourceSelector

The selector that gives the source value.

### TargetSelector

The selector that receives the remapped value. The node writes float values only.

## Outputs

### Results

The processed list of points with the remapped value.
