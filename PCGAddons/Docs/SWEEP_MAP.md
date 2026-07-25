# PCG.Sweep — меш выметанием 2D-профиля вдоль сплайнов

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

`Scripts/` содержит runtime-ноды и типы профиля (`PCG.Sweep`), `Editor/` — исполнители и mesh builders (`PCG.Sweep.Editors`).

Аддон строит дороги, тропы, стены, трубы и русла из world-space `List<Spline>`. UV идут по метражу, ширина/высота управляются кривыми, профиль может вращаться, торцы — закрываться. `SweepSplineNode` не читает heightmap: если ось должна идти по поверхности, перед ним ставится `SplineToTerrainNode` из PCG.Splines. Собственный `HeightOffset` Sweep остаётся независимым мировым Y-сдвигом готовой геометрии.

## Ноды

| Нода | Категория | Назначение | Input → Output |
|---|---|---|---|
| `ProfileNode` | Sweep | строит переиспользуемый 2D-профиль | `Shape, Width, Height, Sides, CustomPoints, CustomClosed` → `Profile: SweepProfile` |
| `SweepSplineNode` | Sweep | выметает inline- или подключённый профиль и строит меши | `Splines, Profile(override), Shape/Width/Height/Sides/Custom*, Step, MaxStep, MaxAngle, Width/Height/TwistByT, CapEnds, MergeIntersections, MergeThickness, UvScale, HeightOffset, Name, Material, JunctionMaterial, Collider` → `Results: List<MeshInstanceData>` |

## Пресет StonePath

`Packages/PCG.Sweep/Presets/StonePath.asset` — `PcgSubGraph`, а не отдельная нода.

- Ось полотна: `Splines → SplineToTerrainNode → SweepSplineNode`.
- `SplineToTerrainNode`: `Resample=true`, `Step=2`, `AlignToTerrainNormal=true`, `HeightOffset=0.08`; `TerrainObjectValue.Terrain → Terrain`, `TerrainObjectValue.Offset → TerrainOrigin`.
- `SweepSplineNode`: Ribbon, `HeightOffset=0`, `Width` и `PathMaterial` приходят из variables, `Collider=true`.
- Тот же спроецированный spline идёт в `PointsOffsetSplinesNode`; после бокового смещения существующий `PointToTerrainNode` отдельно укладывает точки камней.
- Входы: `Splines, Terrain, PathMaterial, Stones, Width, StoneOffset, Seed`. Выходы: `Path`, `Stones`.
- Хост требует `MeshInstanceMaker` и `GameObjectInstanceMaker`. Инвариант: `StoneOffset >= Width / 2 + радиус самого крупного камня`.

## Runtime-типы

- `ProfileShape` — `Ribbon`, `Rectangle`, `HalfPipe`, `Custom`, `Pipe`.
- `SweepProfile` — точки профиля, U-координаты, пары рёбер, признак замкнутости и content hash.
- `SweepProfileBuilder` — единая фабрика для `ProfileNode` и inline-профиля: Ribbon; Rectangle с hard edges; гладкий 9-точечный HalfPipe; Custom с фильтрацией и нормализацией winding; Pipe с заданным числом сегментов.
- `SweepSplineNode.HeightOffset` — `[Input] float`, который не зависит от террейна и не меняет высоту самого профиля.

## Обычный build path

- `SweepSplineNodeExecutor` снимает immutable snapshot на editor thread с `OperationScope`, а splitter и геометрию строит через общий ограниченный `PcgWorkerScheduler` с индексированными слотами; результат публикуется одним finalize-путём.
- `SweepRibbonSampling` даёт общий 3D frame для Ribbon и HalfPipe.
- `SweepRibbonMeshBuilder` строит Ribbon по две вершины на станцию.
- `SweepProfileMeshBuilder` строит полный 9-точечный HalfPipe-кольцевой профиль и соединяет только соседние кольца.
- `SweepMeshBuilder` обслуживает остальные профили, trim складок, caps, cleanup и применяет `HeightOffset`.
- `SweepPrismBuilder` экструдирует верх Rectangle после его построения, поэтому верх, дно и стенки получают одинаковый `HeightOffset`.
- `SweepMeshBuilder.Cleanup` сваривает по `SweepWeldKey`, отбрасывает вырожденные треугольники и детерминированно уплотняет vertex streams.

## Merge Intersections

`MergeIntersections` использует текущий patch pipeline без внешней topology-ноды:

1. `SweepRibbonSplitter` классифицирует равномерные поперечные сечения как green, blue или red с учётом `MergeThickness`.
2. Green pieces строятся обычным sweep path.
3. Blue sharp-corner pieces строятся `SweepRibbonCornerFanBuilder` с fallback на обычный piece.
4. Red overlap regions закрываются `SweepRibbonPatchBuilder`.
5. `Rectangle` экструдируется после сборки верхних поверхностей; `HalfPipe` во всех режимах использует полный профильный ring path.

`HeightOffset` одинаково применяется к обычным мешам, green pieces, blue fans и red patches. `JunctionMaterial` используется для patch-геометрии и при отсутствии значения откатывается к `Material`. `ShowIntersections` и `ShowAllCuts` управляют preview-диагностикой.

## Зависимость от ядра

`MeshInstanceMaker` должен пересчитывать normals и tangents и материализовать меш в world-identity относительно parent. Эта обязанность живёт в ProjectPCG, не в данном аддоне.

## Width channel

`SweepSplineNode` сначала ищет embedded-канал `SplineWidth` на каждом входном сплайне. Если канал присутствует, его world-space значение семплируется вдоль кривой и имеет приоритет над scalar `Width` и `WidthByT`; при отсутствии канала сохраняется прежнее поведение. Контракт одинаков для обычного sweep, Ribbon, профильного build path и `MergeIntersections`, включая junction patches.
