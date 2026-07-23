# PCG.Splines — сплайны (Unity.Splines)

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.Splines`); `Editor/` — исполнители и редакторские адаптеры (asmdef `PCG.Splines.Editor`); `Documentation~/` — справка. Ссылается на `PCG`, `PCG.Editors`, `PCG.Gizmos.Editor` (DLL), `Unity.Mathematics`, `UniTask`.

Самый крупный аддон. Все типы вокруг `UnityEngine.Splines.Spline`.

## Ноды

| Нода | Категория | Назначение | Input → Output |
|---|---|---|---|
| `SplineFromPointsNode` | Splines | сплайн из облака точек | `Points` → `Results: List<Spline>` |
| `SplineAroundPointsNode` | Splines | замкнутый сплайн вокруг каждой точки | `Points, Radius, PointsCount, Up, Seed` → `Results` |
| `FindSplinesNode` | Splines | найти сплайны в сцене по Name/Tag | `Name, Tag` → `Results` |
| `JoinSplinesNode` | Splines | склейка открытых сплайнов по близким концам | `Splines, Threshold` → `Results` |
| `OffsetSplinesNode` | Splines | боковое смещение сплайна | `Splines, Offset, Up` → `Results` |
| `ResampleSplinesNode` | Splines | передискретизация с шагом | `Splines, Step` → `Results` |
| `SplineToTerrainNode` | Splines | укладывает knots сплайна на heightfield, опционально передискретизирует и выравнивает Up | `Splines, Terrain, TerrainOrigin, HeightOffset, AlignToTerrainNormal, Resample, Step` → `Results` |
| `SmoothSplinesNode` | Splines | лапласово сглаживание | `Splines, Iterations, Strength` → `Results` |
| `RandomSplineNode` | Splines | случайные сплайны через пары точек | `Points, Up, Segments, Height, Seed` → `Results` |
| `ClosedSplinesNode` | Splines | разделить на замкнутые/открытые | `Splines` → `Results, OpenedSplines` |
| `ChangeSplinePositionNode` | Splines | случайное смещение вершин | `Splines, Min, Max, Seed` → `Results` |
| `SplineNode` | Splines | базовая нода-сплайн (редактируемый) | → `Results` |
| `SplineIntersectionNode` | Splines | точки пересечений сплайновой сети в XZ (адаптивно, с валентностью) | `Splines, IntersectionTolerance, MergeDistance, MaxHeightDifference` → `Topology: SplineNetworkTopology, Results: List<PointData>` |
| `SplitSplinesNode` | Splines | точный разрез сплайнов по резам топологии или точкам (без ресемпла) | `Splines, Cuts: SplineNetworkTopology, Points, SnapDistance` → `Results: List<Spline>` |
| `PointsBySplineNode` | SelectPoints | точки внутри/снаружи замкнутого сплайна | `Points, Splines` → `Results, Outsides` |
| `PointsNearSplinesNode` | SelectPoints | точки близко/далеко от сплайна (режим 3D/2D, UseScale) | `Points, Splines, Distance` → `Results, NearPoints` |
| `PointsOffsetSplinesNode` | CreatePoints | точки вдоль сплайна с offset | `Splines, Offset, Distance, Count, Spacing` → `Results, CornerPoints` |
| `SplinePointsByDistanceNode` | CreatePoints | точки вдоль сплайна с шагом по длине дуги | `Splines, Distance, Distribute` → `Results` |
| `SplinesSurfaceNode` | CreatePoints | точки на поверхности/в объёме замкнутого сплайна | `Splines, Offset, PointMode, Count, Seed` → `Results` |
| `DensityByDistanceToSplinesNode` | TransformPoints | плотность точек по расстоянию до сплайна | `Points, Splines, Radius, Curve, Mode` → `Results` |

## Топология сети (`Scripts/Splines/`, рантайм)
First-class тип для передачи пересечений между нодами (не переиспользует `PointData`, чтобы не ломать семантику точек).
- `SplineCut` (struct) — рез: `SplineIndex` (стабильный flattened-индекс входа), `CurveIndex`, `CurveT`, `Distance` (дистанция вдоль сплайна), `Position`, `JunctionIndex`.
- `SplineJunction` (struct) — перекрёсток: `Position`, `Valency` (число уникальных инцидентных ветвей: рез внутри = 2, на конце = 1).
- `SplineNetworkTopology` (class) — контейнер `List<SplineJunction> Junctions` + `List<SplineCut> Cuts` + `GetContentHash()` (свёртка полей).

## Сетевые хелперы (`Editor/Scripts/Network/`)
Снапшот и фоновые солверы для обеих нод.
- `SplineSnapshot` (class) — immutable-снапшот сплайна (knots/modes/tensions/curves/lengths/prefix/closed/embedded), снимается на главном потоке (`Capture`), считается в пуле.
- `SplineNetworkMath` (static) — `SubCurve` (точная вырезка кубической по `[t0,t1]` через `CurveUtility.Split`), `PartialLength`, `ChordErrorXz`, пересечение/дистанция отрезков в XZ.
- `SplineIntersectionSolver` (static) — адаптивная дискретизация → spatial-hash broad phase (guard >64 клеток) → бисекция-refinement на исходных кривых → height policy → дедуп резов → union-find кластеризация junctions; детерминированный порядок. Типы: `NetworkSegment`, `SplineIntersectionResult`.
- `SplineSplitSolver` (static) — сбор резов (топология + fuzzy nearest), нормализация (open/closed с circular-дедупом), построение точных кусков через вершинную развёртку и `CurveUtility.Split`; сохраняет исходные узлы, граничные фиксирует в `Broken`. Типы: `CutParam`, `SplitVertex`, `KnotInstruction`, `SplineSplitResult`.
- `SplineNetworkInput` (static) — `Flatten` мультивхода `List<Spline>[]` в стабильный порядок (nulls-заглушки сохраняют индексы, общие для обеих нод).

## Опорные типы
- `SplinesValue` (`PcgValue`) — массив `SplineContainer` как значение графа (с пересчётом локал→мир). `IsArray=true` → переменная сплайнов в сабграфе принимает несколько связей (зеркалятся внутрь массивом).
- `SplineListPool` — пул `List<Spline>`.
- `SplinesCache` — кэш позиций на сплайнах; инвалидация по изменению/undo/prefab.
- `SplinesUtility` — point-in-spline (ray-crossing) для замкнутых сплайнов.
- `SplinesGizmoUtility` — отрисовка линий сплайнов в превью.
- `SplineSpacingMode` (enum) — Distance / Count / Fit.
- `SplinePoints` (static) — генерация `PointData` на сплайне (Surface/Volume × Regular/Random; `SurfaceRegular` — по длине дуги). `GetPointsByDistance` — точки с шагом по дистанции (с `Distribute`).
- `SplineNodeRenderer` (`CustomPcgNodeRenderer`) — кнопки Start/Stop Edit в ноде.
- `GameObjectsToSplinesAdapter` (`PcgPortAdapter`) — `List<GameObject>` → `List<Spline>`.
- `SplineResampleUtility` — общий fixed-step AutoSmooth алгоритм для `ResampleSplinesNodeExecutor` и `SplineToTerrainNodeExecutor`.
- `SplineTerrainWindow` — immutable bilinear height-window и world-space normal sampling для `SplineToTerrainNodeExecutor`.

## Spline To Terrain

`SplineToTerrainNode` проецирует центральную линию сплайна на `TerrainData`. `TerrainOrigin` задаёт мировую позицию объекта Terrain, потому что сам `TerrainData` трансформа не содержит; `HeightOffset` — отдельный художественный подъём по мировому Y. `AlignToTerrainNormal` меняет knot frame с компенсацией локальных tangents, поэтому форма кривой не меняется от одного только выравнивания Up.

При `Resample=false` используется полная копия исходного сплайна с metadata и embedded data. При `Resample=true` сначала строится новая AutoSmooth-сетка тем же алгоритмом, что у `Resample Splines`. Узлы вне bounds сохраняют исходные Y и Up. Нода укладывает только сплайн: драпировка полной ширины будущего меша не выполняется.
