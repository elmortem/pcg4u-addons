# AttributeMathNode

Applies an arithmetic operation to two point values and writes the result into a target attribute.
The node reads and writes with selectors. A selector that starts with `$` is a built-in point channel, for example `$scale` or `$normal`. A selector without the prefix is a named attribute column, for example `height`.
The second operand is a constant or a second selector.

## Inputs

### ConstantB

The constant second operand. The node uses this value when UseConstantB is on.

### LerpT

The blend factor for the Lerp operation. The node keeps the factor in the range 0 to 1.

### Points

The input list of points to process.

## Variables

### Enabled

Enables the operation. If you disable the node, the output is equal to the input.

### Op

The arithmetic operation: Add, Subtract, Multiply, Divide, Min, Max, Power or Lerp. A division by zero gives 0.

### PreviewRange

The value range that maps the preview attribute to the color ramp.

### PreviewSelector

The attribute that gives the color of the preview points. Keep this field empty to use the default color.

### SelectorA

The selector of the first operand.

### SelectorB

The selector of the second operand. The node uses this selector when UseConstantB is off.

### TargetSelector

The selector that receives the result.

### Type

The value type of the target attribute column. The Float3 type reads and writes vectors, and the constant operand goes to all three components.

### UseConstantB

Uses the constant value as the second operand in place of SelectorB.

## Outputs

### Results

The processed list of points with the operation result.
