# PcgSplineSet

A set of splines that moves between nodes as one value. The set holds the spline geometry and a table of named attributes. The table has one row for each spline.

Each spline port in the graph uses this type. The type is the equivalent of `PcgPointCloud` for points and `RegionSet` for regions.

## Properties

### Splines

The splines in the set.

### Attributes

Named attribute rows. There is one row for each spline.

### Count

The number of splines in the set.

## Attributes or embedded channel

There are two different places for spline data. Use the correct one.

* If the value changes along the spline, put the value in an embedded Unity channel. The width of a road is an example. The width channel uses the key `pcg.width`.
* If the value is constant for the full spline, put the value in an attribute row. The road class is an example.

An attribute row cannot show a change along the spline, because there is only one row for each spline.

## Attribute names

| Name | Type | Description |
|---|---|---|
| `splineIndex` | int | Index of the source spline in the flattened input order. |
| `splineT` | float | Normalized position along the spline, from 0 to 1. The value is `-1` if the mode is a volume mode. |
| `splineDistance` | float | Distance along the spline, in world units. The value is `-1` if the mode is a volume mode. |
| `splineWidth` | float | Width of the spline at the sample position. The value comes from the `pcg.width` channel. |
| `splineSide` | int | Side of the spline: `+1`, `-1` or `0`. |
| `closed` | bool | True if the spline is closed. |
| `sourceSplineIndex` | int | Index of the source spline or the source graph edge. |
| `pieceIndex` | int | Index of the piece in the source spline. |
| `startJunction` | int | Index of the junction at the start of the spline. The value is `-1` if there is no junction. |
| `endJunction` | int | Index of the junction at the end of the spline. The value is `-1` if there is no junction. |
| `junctionIndex` | int | Index of the junction. |
| `junctionValency` | int | Number of unique branches at the junction. |

City nodes add more names. Refer to the Polygons addon.

## Which node writes which attribute

| Node | Output | Attributes |
|---|---|---|
| Blocks To Roads (Polygons) | Centerlines | `roadClass`, `width`, `closed` |
| Region To Spline (Polygons) | Splines | The attributes of the source region, and `regionIndex` |
| Graph To Spline (Mazes) | Splines | `sourceSplineIndex`, `startJunction`, `endJunction`, `weight` |
| Split Splines | Results | The attributes of the source spline, and `sourceSplineIndex`, `pieceIndex`, `startJunction`, `endJunction` |
| Closed Splines | Results, OpenedSplines | The attributes of the source spline, and `closed` |
| Spline Intersection | Results | `junctionIndex`, `junctionValency` |
| Spline Points By Distance | Results | The attributes of the source spline, and `splineIndex`, `splineT`, `splineDistance`, `splineWidth` |
| Splines Surface | Results | The same as Spline Points By Distance |
| Points Offset Splines | Results | The same, and `splineSide` |
| Points Offset Splines | CornerPoints | The attributes of the source spline, and `splineIndex` |

## How the nodes keep the attributes

A node that changes the geometry keeps the attribute row of the source spline. A node that makes new splines from a different input starts with an empty row.

* If the output is the same spline in a new shape, the node moves the source row to the new spline.
* If the output is a part of the input, the node moves the row of each selected spline.
* If the node cuts one spline into more than one piece, each piece gets the row of the source spline.
* If the node joins more than one spline into one spline, the result gets the row of the first spline in the chain.

## Cache

The graph cache keeps the geometry and the attributes. The cache also keeps the embedded float channels of each spline, for example `pcg.width`. The cache does not keep embedded channels of the types float4, int and Object. If a spline has such a channel, the cache writes a warning one time.
