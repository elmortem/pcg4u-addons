# PCG.Splines — сплайны (Unity.Splines)

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).
>
> Установка: `https://github.com/elmortem/pcg4u-addons.git?path=PCGAddons/Packages/PCG.Splines#com.elmortem.pcg.splines/<version>`, где `<version>` — значение из `package.json`. Правила веток, версий и тегов — раздел 9 `PROJECT_MAP.md`.

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.Splines`); `Editor/` — исполнители и редакторские адаптеры (asmdef `PCG.Splines.Editor`); `Documentation~/` — справка. Ссылается на `PCG`, `PCG.Editors`, `PCG.Gizmos.Editor` (DLL), `Unity.Mathematics`, `UniTask`.

Самый крупный аддон. Все типы вокруг `UnityEngine.Splines.Spline`.

## Ноды

| Нода | Категория | Назначение | Input → Output |
|---|---|---|---|
| `SplineFromPointsNode` | Splines | сплайн из облака точек | `Points` → `Results: PcgSplineSet` |
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
| `SplineIntersectionNode` | Splines | точки пересечений сплайновой сети в XZ (адаптивно, с валентностью) | `Splines, IntersectionTolerance, MergeDistance, MaxHeightDifference` → `Topology: SplineNetworkTopology, Results: PcgPointCloud, SnappedSplines: PcgSplineSet` |
| `SplitSplinesNode` | Splines | точный разрез сплайнов по резам топологии или точкам (без ресемпла) | `Splines, Cuts: SplineNetworkTopology, Points, SnapDistance` → `Results: PcgSplineSet` |
| `PointsBySplineNode` | SelectPoints | точки внутри/снаружи замкнутого сплайна | `Points, Splines` → `Results, Outsides` |
| `PointsNearSplinesNode` | SelectPoints | точки близко/далеко от сплайна (режим 3D/2D, UseScale) | `Points, Splines, Distance` → `Results, NearPoints` |
| `PointsOffsetSplinesNode` | CreatePoints | точки вдоль сплайна, в том числе в центрах секций и со смещением от ширины сплайна | `Splines, Offset, Distance, Count, Spacing, Placement, UseSplineWidth, WidthMultiplier` → `Results, CornerPoints` |
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
- `PcgWorkerScheduler` (`Scripts/Utilities/`) — общий ограниченный CPU-пул для Splines/Polygons/Sweep; резервирует editor thread, поддерживает отмену и индексированные детерминированные батчи.
- `SplineSnapshot` (class) — immutable-снапшот сплайна (knots/modes/tensions/curves/lengths/prefix/closed/embedded), снимается на главном потоке (`Capture`), считается в пуле.
- `SplineNetworkMath` (static) — `SubCurve` (точная вырезка кубической по `[t0,t1]` через `CurveUtility.Split`), `PartialLength`, `ChordErrorXz`, пересечение/дистанция отрезков в XZ.
- `SplineIntersectionSolver` (static) — адаптивная дискретизация → spatial-hash broad phase (guard >64 клеток) → бисекция-refinement на исходных кривых → height policy → дедуп резов → union-find кластеризация junctions; детерминированный порядок. Типы: `NetworkSegment`, `SplineIntersectionResult`.
- `SplineSplitSolver` (static) — сбор резов (топология + fuzzy nearest), нормализация (open/closed с circular-дедупом), построение точных кусков через вершинную развёртку и `CurveUtility.Split`; сохраняет исходные узлы, граничные фиксирует в `Broken`. Типы: `CutParam`, `SplitVertex`, `KnotInstruction`, `SplineSplitResult`.
- `SplineNetworkInput` (static) — `Flatten` мультивхода `PcgSplineSet[]` в стабильный порядок (nulls-заглушки сохраняют индексы, общие для обеих нод).

## Опорные типы
- `SplinesValue` (`PcgValue`) — массив `SplineContainer` как значение графа (с пересчётом локал→мир), `ValueType` = `PcgSplineSet`. `IsArray=true` → переменная сплайнов в сабграфе принимает несколько связей (зеркалятся внутрь массивом).
- `SplineListPool` — пул `List<Spline>`.
- `SplinesCache` — кэш позиций на сплайнах; инвалидация по изменению/undo/prefab.
- `SplinesUtility` — point-in-spline (ray-crossing) для замкнутых сплайнов и `GetContentHash(Spline)` — единая посплайновая свёртка, которой пользуются `SplinesValue` и `PcgSplineSet`.
- `SplinesGizmoUtility` — отрисовка линий сплайнов в превью.
- `SplineSpacingMode` (enum) — Distance / Count / Fit.
- `SplinePoints` (static) — генерация `PointData` на сплайне (Surface/Volume × Regular/Random; `SurfaceRegular` — по длине дуги). `GetPointsByDistance` — точки с шагом по дистанции (с `Distribute`).
- `SplineNodeRenderer` (`CustomPcgNodeRenderer`) — кнопки Start/Stop Edit в ноде.
- `GameObjectsToSplinesAdapter` (`PcgPortAdapter`) — `List<GameObject>` → `PcgSplineSet`; копирует сплайны через `SplineCopyUtility.CopySpline` с матрицей контейнера, поэтому embedded-каналы (включая `pcg.width`) не теряются.
- `SplineResampleUtility` — общий fixed-step AutoSmooth алгоритм для `ResampleSplinesNodeExecutor` и `SplineToTerrainNodeExecutor`.
- `SplineTerrainWindow` — immutable bilinear height-window и world-space normal sampling для `SplineToTerrainNodeExecutor`.

## Spline To Terrain

`SplineToTerrainNode` проецирует центральную линию сплайна на `TerrainData`. `TerrainOffset` задаёт мировую позицию объекта Terrain, потому что сам `TerrainData` трансформа не содержит; `HeightOffset` — отдельный художественный подъём по мировому Y. `AlignToTerrainNormal` меняет knot frame с компенсацией локальных tangents, поэтому форма кривой не меняется от одного только выравнивания Up.

При `Resample=false` используется полная копия исходного сплайна с metadata и embedded data. При `Resample=true` сначала строится новая AutoSmooth-сетка тем же алгоритмом, что у `Resample Splines`. Узлы вне bounds сохраняют исходные Y и Up. Нода укладывает только сплайн: драпировка полной ширины будущего меша не выполняется.

## Width channel и road-network contract

- `SplineWidthNode` копирует входные сплайны и записывает абсолютную ширину в world units в embedded-канал Unity под ключом `pcg.width` (константа `SplineWidthUtility.DataKey`).
- `SplineWidthUtility` читает, записывает, копирует и семплирует канал. `SplineResampleUtility`, `SplineToTerrainNode` и сетевые преобразования сохраняют его при создании новых сплайнов.
- `SplinesValue.GetContentHash()` включает width channel, поэтому изменение ширины корректно инвалидирует downstream cache.
- `SplineIntersectionNode.EndpointSnapDistance` предварительно объединяет близкие концы ветвей и отдаёт исправленную сеть через `SnappedSplines`; `Topology` и `Results` вычисляются уже по ней.
- Типовая дорожная цепочка: `Spline → SplineWidth → SplineToTerrain → SplineIntersection.SnappedSplines → Sweep`.

**Вырожденный frame при оффсете точек.** `PointsOffsetSplinesNode` берёт боковое направление как `cross(tangent, up)`. На сплайнах, спроецированных на террейн с `AlignToTerrainNormal`, эти два вектора в отдельных узлах становятся коллинеарными, и нормализация нулевого вектора даёт NaN — точка уезжает в `(NaN, y, NaN)`, а Unity потом сыплет `transform.localPosition assign attempt is not valid` на каждый инстанс. Исполнитель проверяет `lengthsq(cross)` на порог и конечность и **пропускает** такую станцию вместо того, чтобы породить битую точку. Любая новая нода, строящая боковой frame из tangent×up, обязана делать ту же проверку.

## PcgSplineSet — атрибуты на сплайнах

Тип порта сплайнов — `PcgSplineSet` (`Scripts/Splines/PcgSplineSet.cs`, неймспейс `PCG.Splines`): `List<Spline> Splines` (геометрия) + `PcgAttributeSet Attributes` (именованные колонки, строка на сплайн). Инвариант: `Attributes.Count == Splines.Count`, проверяется через `IsValid()`. Форма и смысл — те же, что у `PcgPointCloud` для точек и `RegionSet` для регионов.

- API: `set[i]`, `set.Count`, `foreach` по сплайнам, `set.Splines` (сырой список), `Add(spline)`, `AppendFrom(src, i)`, `AppendFrom(src, i, newSpline)`, `Append(src)`, `Clone()`, `GetContentHash()`.
- Пула для `PcgSplineSet` нет и не нужно: сплайнов в графе десятки. `Results.Rent(...)` на сплайновых выходах не используется — только `Results.Value = new PcgSplineSet()`.
- `Clone()` копирует ССЫЛКИ на `Spline`. Нода, меняющая геометрию, обязана создать новый `Spline` через `SplineCopyUtility.CopySpline`.
- Кеш: `PcgSplineSetSerializer` (`Editor/Scripts/Cache/`), `TypeId => 4`, регистрируется в `PcgSplinesBootstrap`. Сериализуются knots (Position/TangentIn/TangentOut/Rotation/TangentMode/AutoSmoothTension), флаг `Closed`, embedded float-каналы и `Attributes`. Каналы типов `float4`/`int`/`Object` не сериализуются — при их наличии пишется `Debug.LogWarning`.

### Главное правило: канал или атрибут

- **Переменное вдоль сплайна** — embedded-канал Unity (`SplineData<float>`, напр. `pcg.width`). Ширина меняется вдоль дороги, поэтому она живёт именно там.
- **Постоянное на весь сплайн** — колонка в `PcgSplineSet.Attributes`. Строка одна на сплайн, вдоль него она не меняется.

### Правило категорий (какой метод сборки выхода использовать)

- **Generator** — сплайны рождаются не из сплайнов (из точек, графа, региона, руками): `Add`. Если источник несёт атрибуты, они обязаны переехать.
- **Derived-transform** — 1:1, тот же сплайн изменён: `AppendFrom(src, i, newSpline)`.
- **Derived-select** — подмножество: `AppendFrom(src, i)`.
- **Derived-fanout** — 1:N: `AppendFrom(src, sourceIndex, piece)` на каждый кусок.
- **Merger** — несколько наборов в один: `Append(src)`.
- **Consumer** — сплайны только на входе.
- **Internal** — `List<Spline>` как локальная переменная, параметр утилиты или хранилище ноды: остаётся `List<Spline>` (`SplineNode.Splines`, `SplineListPool`, `SplineCopyUtility`, `SplineNetworkInput.Flatten`, `SplinesGizmoUtility`, `SplinesUtility`, `SplinesCache`).

Использовать `Add` в Derived-ноде — дефект.

### Имена атрибутов (`SplineAttributes`)

`splineIndex`, `splineT`, `splineDistance`, `splineWidth`, `splineSide`, `closed`, `sourceSplineIndex`, `pieceIndex`, `startJunction`, `endJunction`, `junctionIndex`, `junctionValency`. Типы: `splineT`/`splineDistance`/`splineWidth` — `float`; `closed` — `bool`; остальные — `int`. `splineSide` принимает `-1`, `0` или `+1`.

### Какая нода что пишет

| Нода | Выход | Пишет |
|---|---|---|
| `BlocksToRoadsNode` (Polygons) | `Centerlines` | `roadClass` (int), `width` (float), `closed` (bool) |
| `RegionToSplineNode` (Polygons) | `Splines` | строку атрибутов региона-источника (`lotId`, `depth`, `cutDepth`, `boundary`) + `regionIndex` |
| `GraphToSplineNode` (Mazes) | `Splines` | `sourceSplineIndex` (индекс ребра), `startJunction`, `endJunction` (индексы узлов), `weight` (float) |
| `SplitSplinesNode` | `Results` | строку исходного сплайна + `sourceSplineIndex`, `pieceIndex`, `startJunction`, `endJunction` |
| `ClosedSplinesNode` | `Results`, `OpenedSplines` | строку исходного сплайна + `closed` |
| `SplineIntersectionNode` | `Results` (точки) | `junctionIndex`, `junctionValency` |
| `SplinePointsByDistanceNode`, `SplinesSurfaceNode` | `Results` (точки) | строку сплайна-источника + `splineIndex`, `splineT`, `splineDistance`, `splineWidth` |
| `PointsOffsetSplinesNode` | `Results` (точки) | то же + `splineSide` |
| `PointsOffsetSplinesNode` | `CornerPoints` (точки) | строку сплайна-источника + `splineIndex` |

Мост сплайн→точка собирают `SplinePointAttributes` и `OffsetPointBuffer` (`Editor/Scripts/Tools/`): они несут параллельные списки `t`/дистанции/ширины/стороны и склеивают их с атрибутами исходного набора. В объёмных режимах (`Volume*`) `t` и дистанция не определены и записываются как `-1`.
