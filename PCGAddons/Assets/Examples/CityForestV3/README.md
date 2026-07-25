# City Forest V3

`CityForestV3.unity` is the readable wrapper scene for the reusable forest-town generator.

## Scene controls

Open `City Forest V3 Controls` in the hierarchy:

- `Town Boundary` defines the outer town footprint.
- `Town Roads` defines the arterial road centerlines crossing the town.
- `Forest Mask` owns the paintable forest mask used by every forest layer.

Select `Forest Mask`, create or assign a mask asset, then use `Paint Mask in Scene` in the Inspector. The same mask source is passed to trees, shrubs, and ground cover through the `Forest Mask Source` graph variable.

## Town graph

`District City V3` contains a small wrapper graph. Its public variables control terrain, town boundary, road splines, vegetation palettes, road and sidewalk heights, building-to-road clearance, and quarter grass spacing.

The implementation lives in `Graphs/CityForestTown.asset`:

- all district roads and arterial spline corridors are unioned into one road footprint;
- `Blocks To Roads` can iteratively prune dangling centerline branches shorter than `Minimum Dead End Length` before buffering, preventing small disconnected road fingers;
- tiny enclosed holes created by near-touching corridor joins are removed by the road union's `Minimum Hole Area` cleanup;
- zero-area and near-zero-area triangles are rejected before the generated road mesh is committed;
- one rectangular road extrusion creates a continuous asphalt volume whose terrain-adaptive top and sides share the same boundary;
- extrusion sides are generated from the source region contours, never from adaptive top-mesh seams;
- the sidewalk is the expanded road footprint minus the road itself;
- one taller rectangular extrusion creates a continuous raised curb and sidewalk;
- house points subtract the expanded unified road footprint before prefab selection, so the full building footprint stays off district and arterial roads;
- quarter and park grass use deterministic jitter instead of visible rows, then receive a surface lift matching their raised ground meshes;
- quarter grass, yard bushes, zero-to-two yard trees, and the park layers are independent branches;
- all generated instance streams are merged by `Combine Town Instances`.

The graph is saved with Auto Layout and can be reused as a subgraph in another `PcgComponent`.

The forest wrappers bind their external terrain and shared scene-mask variables explicitly. Ground-cover candidates are shuffled before density and Poisson filtering so the dense forest floor does not inherit a regular grid.

## Urban polish pass

The town graph feeds the unified road footprint into the quarter ground-cover exclusion input, including an additional verge clearance. This keeps grass, flowers, and bushes off the generated asphalt after every regeneration.

The Kenney town-plot wrappers are normalized so their lowest visible geometry sits at the instance root, which keeps houses grounded on both flat and sloped terrain.

`CityForestTown.asset` generates street lamps, bins, benches, parked cars, and the central plaza fence. Every urban object is part of the `Town Instances` graph output. The scene does not contain a separate authored dressing layer.

Roadside placement reads the absolute width channel from each road centerline. The graph adds the required verge or lane offset to one half of the road width before it projects each point to the terrain.

`FenceAlongSpline` is an embedded reusable subgraph. It accepts fence splines, terrain, weighted fence prefabs, segment length, lateral offset, model rotation, model scale, and seed. The default segment length is `2.4`. Segment points use fitted section centers so closed runs do not duplicate the first and last fence sections.

Trees, bushes, grass, flowers, and rocks are normalized wrapper prefabs around the imported Kenney Nature Kit models.

## Third-party art

All imported packs are distributed by Kenney under Creative Commons Zero (CC0). Their original `License.txt` files are stored beside the FBX sources:

- City Kit (Suburban)
- City Kit (Roads)
- Car Kit
- Nature Kit
- Retro Urban Kit

## Mask authoring

There are two equivalent mask workflows:

- `Paint Mask` creates and paints a mask directly inside a graph.
- `PaintMaskComponent` supplies scene-authored `PaintMaskData` through a `Paint Mask Object` graph variable.

Use the scene component when level designers need to author the mask outside the city subgraph.
