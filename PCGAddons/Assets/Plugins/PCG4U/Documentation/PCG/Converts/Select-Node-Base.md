# SelectNodeBase

Base node that selects a subrange from concatenated input lists [Begin, End).
Negative indices are supported and resolved relative to the total count.

## Inputs

### Begin

Inclusive start index. Supports negative indices.

### Elements

Input list(s) of elements to select from. Multiple inputs are flattened.

### End

Exclusive end index. Supports negative indices.

## Outputs

### Results

Output list containing the selected subrange.

