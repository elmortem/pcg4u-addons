# Карта проекта PCG4U Addons

Навигация по репозиторию: где что лежит, ключевые классы, базовый API ядра, инвентарь нод аддонов и потоки данных.

> Обновляй этот файл при изменении структуры (новые папки/подсистемы/ноды).

---

## 1. Что это за проект

Проект — **рабочее окружение для разработки аддонов к PCG4U**.

- **PCG4U** — нодовый редактор процедурной генерации для Unity (граф нод, похожий на XNode). Ядро поставляется **скомпилированными DLL** — исходников нет, трогать не нужно.
- **Аддоны** (`Packages/PCG.*`) — это то, что мы разрабатываем в этом репозитории. Каждый добавляет новые ноды/типы в граф PCG.
- Рендер-конвейер сцены: **HDRP** (см. `Packages/manifest.json`, `Assets/Settings/HDRenderPipelineAsset.asset`).
- Целевая версия Unity: **2022.3** (asmdef'ы аддонов).

---

## 2. Раскладка по папкам

```
PCGAddons/
├─ Assets/
│  ├─ Editor/CoworkBridge/        ← рабочая папка моста (входящие Task_*.cs + result_*.json)
│  ├─ Plugins/PCG4U/              ← РЕЛИЗ ядра PCG4U (НЕ ТРОГАТЬ, обновляется отдельно)
│  │  ├─ PCG/                     ← PCG.dll, PCG.Editors.dll, PCG.Gizmos.Editor.dll + Icons/Resources
│  │  │  └─ Gizmos.HDRP/          ← FastGizmos backend под HDRP (3 .cs, исходники)
│  │  ├─ Setup/                   ← мастер первичной настройки + каталог extras (исходники)
│  │  ├─ Documentation/PCG/       ← .md-документация по всем нодам ядра (по категориям)
│  │  └─ Examples/                ← демо-сцены (Forest, Grass, Sample), модели, префабы, террейны
│  ├─ Scenes/SampleScene.unity    ← рабочая сцена проекта
│  ├─ Settings/                   ← HDRenderPipelineAsset
│  ├─ Resources/Memcpy.compute    ← compute-шейдер (используется BRG-инстансингом)
│  └─ HDRPDefaultResources/
├─ Packages/
│  ├─ PCG.BRG/                    ← аддон: BatchRendererGroup-инстансинг
│  ├─ PCG.Mazes/                  ← аддон: графы и лабиринты
│  ├─ PCG.Octree/                 ← аддон: пространственный поиск точек через Octree
│  ├─ PCG.Splines/                ← аддон: работа со сплайнами Unity.Splines
│  ├─ PCG.SpriteShapes/           ← аддон: 2D SpriteShape вдоль сплайнов
│  ├─ CoworkBridge/Editor/        ← ядро моста «выполни C# в Editor» (исходники)
│  ├─ com.unity.render-pipelines.high-definition-config/  ← локальная копия HDRP-конфига
│  └─ manifest.json               ← зависимости проекта
├─ Docs/                          ← документация проекта
│  ├─ PROJECT_MAP.md              ← этот файл
│  ├─ tdd/                        ← технические задания (по мере появления)
│  └─ notes/                      ← заметки (по мере появления)
└─ *.csproj / PCGAddons.sln       ← генерируются Unity, не редактируются вручную
```

Внешние git-зависимости ядра (см. `manifest.json`): `com.elmortem.brg`, `com.elmortem.octree`, `com.elmortem.delone`, `com.cysharp.unitask`.

---

## 3. Базовый API ядра PCG (восстановлен по использованию в аддонах)

Ядро — в DLL. Ниже — публичные типы, на которые опираются аддоны. Это контракт, которому **должен следовать любой новый аддон/нода**.

### 3.1 Ноды (runtime, namespace `PCG.GraphModel`)
- `PcgNode` — базовая нода графа (только данные).
- `PcgPreviewNode` — нода с поддержкой превью (gizmos). **Большинство нод аддонов наследуют её.**
- Порты задаются полями с атрибутами:
  - `[Input] public T Field = ...;` — входной порт.
  - `[Output] public T Field => default;` — выходной порт (тело-заглушка; реальное значение считает Executor).
- Обычные публичные поля (без атрибута) — параметры ноды, редактируются в инспекторе.

Пример (паттерн):
```csharp
public class FooNode : PcgPreviewNode
{
    public bool Enabled = true;            // параметр
    [Input] public List<PointData> Points = new();
    public float Radius = 1f;              // параметр
    [Output] public List<PointData> Results => default;
}
```

### 3.2 Исполнители нод (editor, namespace `PCG.Exec`)
- `PcgAsyncPreviewNodeExecutor<TNode>` — базовый асинхронный исполнитель с превью (основной).
- `PcgSyncPreviewNodeExecutor<TNode>` — синхронный вариант.
- `PcgNodeExecutor` — базовый исполнитель без превью.
- Связь Executor↔Node — через generic-параметр `<TNode>`.

Ключевые члены исполнителя:
- `Data` — типизированная ссылка на ноду (`TNode`).
- `PcgOutput<T> SomeOutput;` — публичное поле под выходной порт; `.Value` (get/set результат), `.Rent(capacity)`.
- `protected override async UniTask DoComputeAsync(CancellationToken ct)` — вычисление выходов.
- `public override void DrawPreview(Transform transform)` — отрисовка gizmos-превью.
- `public override bool IsEmpty` — пусто/нет результата.
- Получение входов: `GetInputValue(nameof(Data.Field), Data.Field)` (скаляр), `GetInputValues(...)` / `GetInputPort(name).GetInputValues()` (массив значений со всех подключённых связей).
- Превью: `GetGizmosOptions()` → `GizmosOptions` (цвет и пр.), `GizmosUtility.DrawPoints(...)`.

Опциональные интерфейсы исполнителя (UI-инфо/переключение превью):
- `INodeInfo` — `HasNodeInfo`, `NodeInfo` (строка в шапке ноды, напр. «Objects: N / M»).
- `IShowResults` / `IPointsCount` / `IShowCenterPoints` — переключатели того, что показывать в превью.

### 3.3 Кооперативная асинхронность — `OperationScope` (namespace `PCG.Utilities`)
- `using (var scope = OperationScope.Start(this)) { ... await scope.Step(ct: ct); }`
- `scope.Step()` — точка кооперативного прерывания/прогресса/отмены внутри тяжёлых циклов.
- Тяжёлые вычисления часто уходят в пул потоков: `UniTask.SwitchToThreadPool()` → работа → `UniTaskEditor.SwitchToEditorThread()` перед возвратом (см. PCG.Octree).

### 3.4 Типы точек и инстансов
- `PCG.Points.PointData` — единица размещения: `Position` (Vector3), `Normal` (Vector3), `Angle` (float, вокруг Normal), `Scale` (float), `Density` (float, [0..1]).
- `PCG.Points.GeneratePointMode` — Surface/Volume × Regular/Random.
- `PCG.Points.ChangeDensityMode` — Set/Add/Mult (как менять плотность).
- `PCG.Instances.InstanceData` — базовый тип «что породить» (наследуется аддонами).
- `PCG.Instances.GameObjectInstanceData` — `Prefab` + одиночная `Point` (ядро).
- `PCG.Instances.InstanceMakerBase` — «мейкер»: превращает `InstanceData` в объекты сцены.
  - `Begin()`, `async UniTask<bool> TryAdd(ownerKey, groupName, IEnumerable<InstanceData>, ct)`, свойство `Parent`.
  - Паттерн: `if (data is МойInstanceData) { ... } else return false;` (мейкер берёт только свой тип).

### 3.5 Значения графа — `PcgValue` (namespace `PCG.Values`)
- `PcgValue` — обёртка ассета/данных для прокидывания в граф как переменной (методы вида `GetValue()`, `GetContentHash()`).
- `PcgPortAdapter` — адаптер типов между несовместимыми портами (напр. `List<GameObject>` → `List<Spline>`).
- `CustomPcgNodeRenderer` (namespace `PCG.Editors`) — кастомный UI ноды в графе (кнопки и т.п.).

### 3.6 Категории нод ядра (из `Documentation/PCG/`)
| Категория | Примеры нод (по именам .md) |
|---|---|
| Constants | Float/Int/Vector2/Vector3 Constant |
| Converts | Float↔Int, Points↔Vector3, Change Vector3 |
| CreatePoints | Plane/Box/Sphere/Mesh/Terrain/Collider Surface, Around Points, Copy Points |
| Operations (50 .md) | Float/Int/Vector3 арифметика, Abs/Min/Max/Clamp/Lerp/Remap, Random |
| TransformPoints (34) | Change Position/Scale/Angle/Normal, Density-by-* , Project to Colliders |
| SelectPoints | Percent, By Height/Slope/Density, Near Points |
| Noises (22) | Perlin/Simplex/Worley/Fbm/Ridged |
| Instances (32) | Game Objects (+Weights), Terrain Grass/Tree Detail |
| Points / Variables / Options / Meshes / Terrains / Functions / Utilities | Combine/Shuffle Points, Variable, Gizmos Options, Find Mesh/Terrain, Pair Points Cycle, UniTaskEditor |

---

## 4. Аддоны (`Packages/PCG.*`)

**Структура каждого аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.X`); `Editor/` — исполнители и редакторские адаптеры (asmdef `PCG.X.Editor`); `Documentation~/` — справка. Все ссылаются на `PCG`, `PCG.Editors`, `PCG.Gizmos.Editor` (DLL), `Unity.Mathematics`, `UniTask`.

### 4.1 PCG.Splines — сплайны (Unity.Splines)
Самый крупный аддон. Все типы вокруг `UnityEngine.Splines.Spline`.

**Ноды:**

| Нода | Категория | Назначение | Input → Output |
|---|---|---|---|
| `SplineFromPointsNode` | Splines | сплайн из облака точек | `Points` → `Results: List<Spline>` |
| `SplineAroundPointsNode` | Splines | замкнутый сплайн вокруг каждой точки | `Points, Radius, PointsCount, Up, Seed` → `Results` |
| `FindSplinesNode` | Splines | найти сплайны в сцене по Name/Tag | `Name, Tag` → `Results` |
| `JoinSplinesNode` | Splines | склейка открытых сплайнов по близким концам | `Splines, Threshold` → `Results` |
| `OffsetSplinesNode` | Splines | боковое смещение сплайна | `Splines, Offset, Up` → `Results` |
| `ResampleSplinesNode` | Splines | передискретизация с шагом | `Splines, Step` → `Results` |
| `SmoothSplinesNode` | Splines | лапласово сглаживание | `Splines, Iterations, Strength` → `Results` |
| `RandomSplineNode` | Splines | случайные сплайны через пары точек | `Points, Up, Segments, Height, Seed` → `Results` |
| `ClosedSplinesNode` | Splines | разделить на замкнутые/открытые | `Splines` → `Results, OpenedSplines` |
| `ChangeSplinePositionNode` | Splines | случайное смещение вершин | `Splines, Min, Max, Seed` → `Results` |
| `SplineNode` | Splines | базовая нода-сплайн (редактируемый) | → `Results` |
| `PointsBySplineNode` | SelectPoints | точки внутри/снаружи замкнутого сплайна | `Points, Splines` → `Results, Outsides` |
| `PointsNearSplinesNode` | SelectPoints | точки близко/далеко от сплайна (режим 3D/2D, UseScale) | `Points, Splines, Distance` → `Results, NearPoints` |
| `PointsOffsetSplinesNode` | CreatePoints | точки вдоль сплайна с offset | `Splines, Offset, Distance, Count, Spacing` → `Results, CornerPoints` |
| `SplinePointsByDistanceNode` | CreatePoints | точки вдоль сплайна с шагом по длине дуги | `Splines, Distance, Distribute` → `Results` |
| `SplinesSurfaceNode` | CreatePoints | точки на поверхности/в объёме замкнутого сплайна | `Splines, Offset, PointMode, Count, Seed` → `Results` |
| `DensityByDistanceToSplinesNode` | TransformPoints | плотность точек по расстоянию до сплайна | `Points, Splines, Radius, Curve, Mode` → `Results` |

**Опорные типы:**
- `SplinesValue` (`PcgValue`) — массив `SplineContainer` как значение графа (с пересчётом локал→мир).
- `SplineListPool` — пул `List<Spline>`.
- `SplinesCache` — кэш позиций на сплайнах; инвалидация по изменению/undo/prefab.
- `SplinesUtility` — point-in-spline (ray-crossing) для замкнутых сплайнов.
- `SplinesGizmoUtility` — отрисовка линий сплайнов в превью.
- `SplineSpacingMode` (enum) — Distance / Count / Fit.
- `SplinePoints` (static) — генерация `PointData` на сплайне (Surface/Volume × Regular/Random; `SurfaceRegular` — по длине дуги). `GetPointsByDistance` — точки с шагом по дистанции (с `Distribute`).
- `SplineNodeRenderer` (`CustomPcgNodeRenderer`) — кнопки Start/Stop Edit в ноде.
- `GameObjectsToSplinesAdapter` (`PcgPortAdapter`) — `List<GameObject>` → `List<Spline>`.

### 4.2 PCG.Mazes — графы и лабиринты
Зависит от `PCG.Splines`, `TriangulationDelone` (Делоне), `Unity.Splines`.

**Ноды:**

| Нода | Назначение | Input → Output |
|---|---|---|
| `GridGraphNode` | граф-сетка | `Size: Vector2Int, CellSize: Vector2` → `Result: Graph, CenterPoints: List<PointData>` |
| `DeloneGraphNode` | граф триангуляции Делоне по точкам | `Points, MinDistance, MinRatio` → `Result: Graph, CenterPoints` |
| `MazeMstGraphNode` | лабиринт через MST (алгоритм Прима) | `Graph, Seed` → `Result: Graph, EndPoints: List<PointData>` |
| `GraphMinusGraphNode` | вычитание графов (удаление пересекающихся рёбер) | `Graph, Minus` → `Result: Graph` |
| `GraphToSplineNode` | рёбра графа → bezier-сплайны | `Graph, AutoSmooth` → `Splines: List<Spline>` |

**Опорные типы (`Scripts/Graphs/`):**
- `Graph` — контейнер `List<GraphNode>` + `List<GraphEdge>` (методы FindNode/FindEdge/Clear). Это **value-тип, передаваемый между нодами**.
- `GraphNode` — вершина: `Vector2 Point` + список рёбер. `GraphEdge` — ребро (две вершины + `Weight`).
- `GraphBuilder` — `BuildGraph()` (из треугольников Делоне), `BuildGrid()` (из параметров сетки).
- `MazeGenerator` — генерация лабиринта (Prim's MST).
- `GraphGizmoUtility` — отрисовка графа (2D→3D, Y=0) в превью.

### 4.3 PCG.BRG — инстансинг через BatchRendererGroup
Зависит от `BRG` (`com.elmortem.brg`). Высокопроизводительный рендер множества копий.

**Ноды:**

| Нода | Назначение | Input → Output |
|---|---|---|
| `GameObjectToBrgNode` | сгруппировать `GameObjectInstanceData` по префабам для BRG | `Enabled, Instances: List<GameObjectInstanceData>` → `Results: List<BrgInstanceData>` |

**Опорные типы:**
- `BrgInstanceData` (`InstanceData`) — `Prefab` + `List<PointData> Points` (все точки одного префаба в группе).
- `BrgInstanceMaker` (`InstanceMakerBase`) — на каждый префаб создаёт `BrgContainer` (компонент из BRG), бьёт точки на батчи по 65000, заполняет `BrgItem` (позиция/ротация из Normal+Angle/масштаб). Использует `Memcpy.compute` (`MemcpyShader`).

### 4.4 PCG.SpriteShapes — 2D SpriteShape вдоль сплайнов
Зависит от `PCG.Splines`, `Unity.2D.SpriteShape.Runtime`, `Unity.Splines`.

**Ноды:**

| Нода | Назначение | Input → Output |
|---|---|---|
| `SpriteShapeInstanceNode` | данные SpriteShape из сплайнов | `Splines, Name, SpriteShape, Height` → `Results: List<SpriteShapeInstanceData>` |

**Опорные типы:**
- `SpriteShapeInstanceData` (`InstanceData`) — `Name`, `Spline`, `SpriteShape`, `Height`.
- `SpriteShapeValue` (`PcgValue`) — обёртка ассета `SpriteShape`.
- `SpriteShapeInstanceMaker` (`InstanceMakerBase`) — создаёт GameObject + `SpriteShapeController`, конвертирует 3D-сплайн (Unity.Splines) в 2D-сплайн (U2D), swap Y/Z, копирует точки/тангенты/режимы, ставит высоту, рефрешит.

### 4.5 PCG.Octree — пространственный поиск точек
Зависит от `Octree` (`com.elmortem.octree`), `Unity.Burst`, `Unity.Mathematics`.

**Ноды:**

| Нода | Назначение | Input → Output |
|---|---|---|
| `PointsNearPointsOctreeNode` | разделить точки на «есть/нет сосед в радиусе» через Octree | `Points, OtherPoints, Radius, WorldCenter, WorldSize, RemoveThemselves, UseScale` → `Results` (без соседей), `NearPoints` (с соседями) |

**Особенности исполнителя:** строит `PointOctree<PointData>` с адаптивным размером узла; батч-обработка (5k/батч); при `RemoveThemselves` — параллельный самопоиск дублей (`UniTask.WhenAll`, до 16 батчей по 100k). Превью рисует куб octree + точки выбранного выхода.

### 4.6 PCG.Polygons — 2D-полигоны и регионы
Фундамент под городские ноды (subdivide/boolean/inset/lots, отдельный ТДД-3). 2D-полигональный тип данных с именованными атрибутами (на регион **и на ребро**), геом-бэкенд Clipper2, заливка точками, конверсии со сплайнами. Плоскость XZ (`float2 = (x, z)`), высота набора — `RegionSet.PlaneY`. Зависит от `PCG`, `PCG.Splines`, `Unity.Splines`, `Unity.Mathematics`, `UniTask`.

**Z-callback Clipper2 (`USINGZ`).** Рантайм-asmdef `PCG.Polygons` объявляет символ `USINGZ` через `versionDefines` (привязан к самому пакету `com.elmortem.pcg.polygons`, expression пустой → всегда активен для этой сборки). `USINGZ` включает в Clipper2 поле `Point64.Z` и хук `ZCallback` — это backbone проброса рёберных атрибутов через булевы операции. **Важно:** под `USINGZ` вендоренный Clipper2 меняет namespace `Clipper2Lib` → `Clipper2ZLib` (upstream-дизайн), поэтому потребители (`PolygonClipper`, `PolygonEdgeClip`) подключают `Clipper2ZLib`.

**Ноды:**

| Нода | Назначение | Input → Output |
|---|---|---|
| `SplineToRegionNode` | замкнутые сплайны → регионы (с ресемплом) | `Splines, MaxSegmentLength` → `Result: RegionSet` |
| `RegionToSplineNode` | регионы → замкнутые сплайны (контур + дырки) | `Region` → `Splines: List<Spline>` |

**Опорные типы (`Scripts/`):**
- `Polygon2D` — контур `Outer` + дырки `Holes` + геометрия (Contains/GetBounds/Clone/Hash). `partial`: рёберные атрибуты вынесены в `Polygon/Polygon2DEdges.cs`.
- `Polygon2D` рёберные атрибуты (`Polygon2DEdges.cs`) — `PcgAttributeSet EdgeAttributes` + индексация рёбер (плоская: рёбра `Outer` `[0..N)`, затем рёбра дырок по порядку). `EdgeCount`, `HoleEdgeOffset(hole)`, `HasEdgeData()`, `GetEdge<T>/SetEdge<T>`. Длина `EdgeAttributes` — либо `0` (данных нет → чтение даёт `default`), либо ровно `EdgeCount`.
- `RegionSet` (`IPcgAttributeData`) — `List<Polygon2D>` + `PlaneY` + `PcgAttributeSet Attributes` (один регион = одна строка атрибутов). **Value-тип, передаваемый между нодами.**
- `RegionSetValue` (`PcgValue`) — регистрация типа `RegionSet` в пикере/блекборде (инлайн пустой).
- `PolygonClipper` (static) — обёртка Clipper2 (`Clipper2ZLib`): Union/Intersection/Difference/Inflate; `SplitByLine` (half-plane) теперь идёт через `PolygonEdgeClip.Intersection` с `Action<PcgAttributeSet,int> newEdgeWriter` (старые рёбра наследуют атрибуты, рез помечается). Масштаб метры×1000 → `Int64`; нормализация винтинга (внешний CCW, дырки CW; `NormalizeWinding` — `internal`).
- `PolygonEdgeClip` (static) — булевы операции с **пробросом рёберных атрибутов** через Z-callback Clipper2: id ребра субъекта → `Point64.Z`, на пересечениях переносится, выходное ребро классифицируется по `Z` + проверка коллинеарности (наследует атрибуты исходного ребра либо отдаётся `newEdgeWriter` как новое). `Difference/Intersection/Union(subject, clip, newEdgeWriter)` + `BuildStrip(a, b, width)` (прямоугольная полоса вдоль ребра — для дорог).
- `RegionFill` (static) — заливка полигона точками: `FillRandom` (rejection), `FillGrid`.
- `SplineRegionConvert` (static) — конверсии spline↔region (ресемпл по длине дуги).
- `Clipper2/` — вендоренный Clipper2 (Boost License), входит в asmdef `PCG.Polygons`. Namespace `Clipper2ZLib` под `USINGZ` (иначе `Clipper2Lib`).

**Editor (`Editor/Scripts/`):**
- `SplineToRegionNodeExecutor` / `RegionToSplineNodeExecutor` — исполнители (превью через `RegionGizmoUtility` / `SplinesGizmoUtility`).
- `SplinesToRegionAdapter` (`PcgPortAdapter`) — `List<Spline>` → `RegionSet` (автоконверсия с дефолтным разрешением).
- `RegionSetSerializer` (`IPcgCacheSerializer`, `TypeId=2`) — value-cache регионов (блобы `float2[]` чанками + `PcgAttributeSetCacheIO`). Порядок: по региону геометрия → его `EdgeAttributes`; затем регион-уровневые `set.Attributes`.
- `PcgPolygonsBootstrap` (`InitializeOnLoadMethod`) — регистрирует сериализатор в `PcgCacheSerializerRegistry`.
- `RegionGizmoUtility` — отрисовка регионов (контуры + дырки) на высоте `PlaneY`.

---

## 5. Подсистема Setup (`Assets/Plugins/PCG4U/Setup/`)
Мастер первичной настройки ядра и каталог доп. пакетов. Это исходники ядра (на ум — не аддон, но в проекте присутствуют).

- `PcgSetupBootstrap` (`InitializeOnLoad`) — на старте редактора проверяет наличие UniTask и необходимость cleanup pipeline, открывает окно настройки.
- `PcgSetupWindow` / `PcgSetupFlow` / `PcgSetupPage` — окно и оркестрация шагов: выбор источника UniTask (Git/OpenUPM) → выбор render pipeline → каталог extras.
- `PcgSetupBanner` / `PcgSetupConstants` — шапка окна и константы (имя/URL/версия UniTask, имена asmdef gizmo-папок).
- `PcgExtrasWindow` / `PcgExtrasCatalog` (ScriptableObject) / `PcgExtrasPackageEntry` — каталог доп. пакетов (аддонов) с установкой «via Git» / «via OpenUPM».
- `PcgPackageInstaller` — установка через `Client.Add()`; `PcgPackageUtility.IsInstalled()` — проверка; `PcgManifestRegistryUtility.EnsureOpenUpmScope()` — прописывает scoped registry в `manifest.json`.
- `PcgRenderPipelineKind` (BuiltIn/Urp/Hdrp) + `PcgRenderPipelineCleanup` — определяет текущий pipeline и удаляет конфликтующие gizmo-папки невыбранного pipeline.
- `PcgConsoleUtility` — очистка консоли (рефлексия).

---

## 6. FastGizmos HDRP (`Assets/Plugins/PCG4U/PCG/Gizmos.HDRP/`)
Быстрая отрисовка превью-гизмо под HDRP (исходники, остальной gizmo-бэкенд — в DLL).
- `FastGizmosHdrpBootstrap` — регистрирует backend, если активен HDRP.
- `FastGizmosHdrpPass` — HDRP `CustomPass` (BeforePostProcess) → вызывает `Backend.Draw()`.
- `FastGizmosHdrpBackend` (`IFastGizmosRenderBackend`) — `CustomPassVolume` + шейдер `PCG4U/FastGizmosShapeHdrp`, отрисовка через `DrawMeshInstancedIndirect`.

---

## 7. CoworkBridge — мост «выполни C# в Unity Editor»
Инструмент для запуска C#-кода в редакторе из внешнего инструмента (Claude Code). **Используется этим окружением — см. skill `unity-bridge`.**

- **Ядро:** `Packages/CoworkBridge/Editor/` — `CoworkBridge` (диспетчер, `InitializeOnLoad`, скан раз в ~1с), `TaskRunner` (поиск класса рефлексией + вызов `Run()`), `TaskResult`/`ResultWriter` (запись результата в JSON + маркер `.done`), `CompilerError(List)`, `TaskData`.
- **Рабочая папка:** `Assets/Editor/CoworkBridge/` — сюда кладутся входящие `Task_*.cs` (класс с `static string Run()`), туда же пишутся `result_*.json` + `*.done`.
- **Поток:** новый `Task_*.cs` → запрос компиляции → выполнение `Run()` → `result_*.json` + `.done` (внешний инструмент ждёт `.done`, читает JSON).
- **Тесты:** поддержка EditMode/PlayMode через `TestRunnerApi`, результат в `testresult_*.json` (см. `Packages/CoworkBridge/UNITYCOWORK.md`).

---

## 8. Как добавить новую ноду в аддон (чек-лист)
1. **Рантайм-нода** в `Packages/PCG.X/Scripts/<Категория>/MyNode.cs`: наследуй `PcgPreviewNode`, поля-параметры публичные с большой буквы, входы `[Input]`, выходы `[Output] ... => default;`.
2. **Исполнитель** в `Packages/PCG.X/Editor/.../MyNodeExecutor.cs`: наследуй `PcgAsyncPreviewNodeExecutor<MyNode>`, поле `PcgOutput<...> Results;`, реализуй `DoComputeAsync` (через `OperationScope` + `scope.Step`) и `DrawPreview`, переопредели `IsEmpty`.
3. Входы читай через `GetInputValues(nameof(Data.Field), Data.Field)`; результат в `Results.Value`.
4. Для UI-инфо в шапке ноды реализуй `INodeInfo`; для переключения превью — `IShowResults`/`IPointsCount`.
5. Новые типы данных инстансов наследуй от `InstanceData` + сделай `InstanceMakerBase` для материализации в сцену.
6. Соблюдай правила из `CLAUDE.md` (табы, public-поля с большой буквы без атрибута сериализации, кэш `GetComponent` полем, классы по отдельным файлам, без комментариев).
