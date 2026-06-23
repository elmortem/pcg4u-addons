# RegionToPointsNode

Places points inside regions.
Margin insets each region before filling. Points are oriented to face the nearest Roads edge, or the nearest region edge when Roads is empty.

## Inputs

### Region

Region set to fill with points.

### Roads

Optional region set whose edges are used to orient the points. If empty, the region's own edges are used.

### Count

Number of points per region in Random mode.

### Spacing

Grid step in Grid mode.

### Margin

Inset applied to each region before filling.

### Seed

Seed for Random mode.

## Variables

### Mode

Placement mode. Centroid puts one point at the area centroid; Random scatters Count points; Grid fills with a regular grid of step Spacing.

## Outputs

### Results

The generated points.
