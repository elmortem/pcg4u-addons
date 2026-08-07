# CityForest V4

A village in a forest. The demo shows a full PCG4U city pipeline: road network, houses on the street
line, front yards with stone paths, a plaza with a town hall, city greenery and a forest that
surrounds the village.

Open `CityForestV4.unity` and press Generate on the four `PcgComponent` objects under
`CityForest V3 - Generated`.

## What the demo generates

| Layer | Source |
|---|---|
| Roads, sidewalks, markings | 8 BSP districts → `BlocksToRoads` → `UnionRegions` → `RegionToMesh` draped on the terrain; markings sweep along the arterial splines |
| Houses | `LotFrontagePoints` puts one house per lot on the street-facing edge, with a setback and the facade turned to the road; district 1 lots subtract the road footprint with `PolygonBoolean` |
| House palettes | `PointsByAttribute` splits the points by `roadClass` and `lotWidth` into palettes A (large), B (medium) and C (small) |
| Front yards | `AssemblyCapture` reads three yard collage prefabs (`VillageYardA/B/C`); `CopyPointsToPoints` stamps them on the house points; a `PruneOverlappingPoints` per variant keeps the yards off the houses and the roads |
| Plaza | A town hall (`town-plot-r` ×1.3) on a sidewalk-stone plinth; three radial stone paths; benches and planters along the paths; a ring of large trees; a light wooden fence (`fence_simple`) around the plaza |
| Street decor | Lamps only on the arterials (one side, 40 m step) and on the plaza; sparse parked cars (one side, 55 m step, 50 % thinning); no bins |
| Greenery | `DensityByNoise` → `PointsByDensity` → `PoissonPoints` gives blue-noise clusters instead of a grid; street trees follow the arterial splines |
| Overlap control | `PruneOverlappingPoints` resolves conflicts between houses, trees, bushes and ground cover by port priority |
| Forest | Three components (trees, shrubs, ground cover) share the `ForestV4` subgraph; the Fbm noise makes the edge ragged; `DensityToScale` grades the tree size |
| Mountain rocks | A separate stream in the Trees component scatters large NatureKit rocks on slopes of 45–60° |

## Public variables

The `District City V4` component exposes:

- `Town Seed` — all decoration, palettes and jitter derive from this value. Change it to reshuffle
  the village without changing the street layout.
- `Layout Seed` — the BSP subdivision of the 8 districts. Change it to get a new street plan.
- `ArterialWidth` — width of the arterial roads. The road corridor region and the spline width both
  read this variable, so they can not drift apart.
- Prefab palettes: `Ground Cover Prefabs`, `Yard Bush Prefabs`, `Yard Tree Prefabs`,
  `Park Tree Prefabs`, `Park Understory Prefabs`, `Plaza Fence Parts`.
- Prism heights: `Road Prism Height`, `Sidewalk Prism Height`, `Quarter Grass Spacing`,
  `Building Road Clearance`.

The three forest components expose `Seed`, `CandidateCount`, `Spacing`, `SlopeMin`/`SlopeMax`,
`ScaleMin`/`ScaleMax`, `ExcludeSplines` (town boundary) and `RoadSplines`.

## Differences from V3

- Houses stand on the frontage line of their lot, not on the lot centroid.
- Every house has a front yard: a stone path from the door to the street, bushes at the porch,
  a tree or flowers, stamped from three collage prefabs.
- The house prefab is chosen by road class and lot width, not by district index.
- Roads are one draped mesh over the unified road footprint, not per-district extruded prisms.
- The plaza is a composed ensemble in the Kenney palette: town hall on a plinth, radial paths,
  benches that face the paths, a tree ring and a light wooden fence.
- Street decor uses village densities: lamps on arterials only, sparse cars, no bins.
- The forest uses dark green V4 leaf materials (`ForestTree*V4`, `ForestBush*V4` wrappers) instead
  of the mint embedded FBX materials; steep slopes carry rocks instead of staying bare.
- Ground cover is blue-noise with density clusters, not a jittered grid.
- All overlaps between layers are resolved by `PruneOverlappingPoints`.

## New nodes used by this demo

- `LotFrontagePoints` (`PCG.Polygons`) — one point per lot on the street-facing edge.
- `PruneOverlappingPoints` (`PCG.Octree`) — priority-based overlap resolution between point layers.
- `AssemblyCapture` + `CopyPointsToPoints` — yard collages stamped per house point.

## Credits

- Kenney kits (CC0): Nature Kit, City Kit Roads, City Kit Suburban, Retro Urban Kit, Car Kit.
  See `Assets/ThirdParty/Kenney*/SOURCE.md`.
- Quaternius Ultimate Fantasy RTS (CC0): imported models remain in `Assets/ThirdParty/`; the demo
  itself no longer places them. See `Assets/ThirdParty/QuaterniusFantasyRTS/SOURCE.md`.

## Screenshots

`Screenshots/` holds `Overview`, `Hero`, `Street`, `Junction`, `Plaza` and `ForestEdge`.
