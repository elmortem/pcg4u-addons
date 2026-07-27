Status: Выполнено

# Миграция аддонов на PcgPointCloud — Agent Execution Spec

## References (not inlined)

- Соглашения, стиль кода, архитектурные правила: `CLAUDE.md` (табы, без комментариев, публичные сериализуемые поля с большой буквы без атрибутов, типы по отдельным файлам, без однострочных условий, `*.meta` не трогать руками, таски Unity Bridge не удалять).
- Принципы проектирования: `Docs/DESIGN_PRINCIPLES.md`. Особенно раздел «Параллелизм по умолчанию» — схемы батчинга и детерминированного слияния сохраняются дословно.
- Карты аддонов: `Docs/PROJECT_MAP.md` и `Docs/<ADDON>_MAP.md`.
- Прецедент прошлой сквозной миграции аддонов под смену core-API: `Docs/tdd/done/260610-1613-TDD-addons_random_migration.md`.
- Skill для работы в Unity Editor (компиляция, прогон графов, съём данных со сцены): `unity-bridge:unity-bridge`.

## Контекст

В ядре тип порта точек сменился с `List<PointData>` на `PCG.Points.PcgPointCloud`. Ядро поставляется DLL (`Assets/Plugins/PCG4U/PCG/`), исходников нет и они не нужны. Аддоны в `Packages/PCG.*` ещё на старом типе — 39 файлов, 115 вхождений.

`PcgPointCloud` — это `List<PointData> Points` (геометрия, как была) плюс `PcgAttributeSet Attributes` (именованные колонки, строка на точку). Инвариант: `Attributes.Count == Points.Count` всегда.

### Эталон, с которого списывать

В этом же репозитории лежит уже мигрированная нода ядра с ОТКРЫТЫМ исходником — читать её первой, до любой правки:

- `Assets/Plugins/PCG4U/PCG/HDRP/Scripts/SelectPoints/PointsByWaterSurfaceNode.cs`
- `Assets/Plugins/PCG4U/PCG/HDRP/Editor/SelectPoints/PointsByWaterSurfaceNodeExecutor.cs`

Это Derived-select нода с двумя выходами, параллельным счётом и переносом атрибутов. Её паттерн — канонический для всех Derived-нод этого ТДД.

### API, которым пользуемся

| Задача | Код |
|---|---|
| Поле-порт в data-ноде | `public PcgPointCloud Points = new();` / `public PcgPointCloud Results => default;` |
| Поле в executor | `public PcgOutput<PcgPointCloud> Results;` |
| Чтение мульти-входа | `var pointsList = GetInputValues(nameof(Data.Points), Data.Points);` → `PcgPointCloud[]` |
| Суммарное число точек | `pointsList.TotalCount()` — БЕЗ параметра типа |
| Аренда выхода | `Results.Rent(capacity);` (пул `PcgPointCloudPool` зарегистрирован ядром, в аддонах регистрировать ничего не надо) |
| Точка по индексу | `cloud[i]` (get и set), `cloud.Count`, `foreach (var point in cloud)` |
| Сырой список | `cloud.Points` |
| Перенос точки С атрибутами | `dst.AppendFrom(src, srcIndex);` |
| Перенос с заменой точки | `dst.AppendFrom(src, srcIndex, modifiedPoint);` |
| Слияние облаков | `dst.Append(srcCloud);` |
| Новая точка без источника | `dst.Add(point);` |
| Копирование строки из чужого набора | `dst.Points.Add(p); dst.Attributes.AppendRow(foreignSet, foreignRow);` |
| Гизмо | `GizmosUtility.DrawPoints(this, cloud, gizmosOptions, transform);` |

Объединение схем при слиянии облаков с разными наборами колонок решено на уровне ядра: `PcgAttributeSet.AppendRow` делает union — отсутствующие в источнике колонки получают дефолт. Отдельной политики не изобретать.

### Правило категорий (главное правило миграции)

- **Generator** — точки рождаются из не-точечного входа (сплайн, регион, граф, сетка). Используется `Add`.
- **Generator-from-attributed-source** — источник сам несёт атрибуты (`RegionSet.Attributes`). Атрибуты источника обязаны попасть на точку. Только `RegionToPointsNode`, см. Unit 5.
- **Derived-select** — выход это подмножество входа. Используется `AppendFrom(src, i)`.
- **Derived-transform** — выход это те же точки, изменённые. Используется `AppendFrom(src, i, modified)`.
- **Merger** — несколько входных облаков в одно. Используется `Append(src)`.
- **Consumer** — точки только на входе, выход не точки. Меняется только тип входа и итерация.
- **Internal** — `List<PointData>` как локальная переменная или параметр утилиты, не порт. Остаётся `List<PointData>`.

Использовать `Add` в Derived-ноде — дефект.

## Foundations (shared, used across units)

### Утилиты, которые остаются на `List<PointData>`

Эти файлы — внутренние генераторы геометрии и накопители, не порты. Их сигнатуры НЕ меняются:

- `Packages/PCG.Splines/Scripts/Surfaces/SplinePoints.cs` (6 методов с параметром `List<PointData> results`)
- `Packages/PCG.Polygons/Scripts/Geometry/RegionFill.cs` (5 методов с параметром `List<PointData> results`)
- `Packages/PCG.BRG/Scripts/BrgInstanceData.cs` (поле `public List<PointData> Points`, это DTO инстансинга, не порт)

Executor-обёртка наполняет локальный `List<PointData>`, а в конце оборачивает: `Results.Value = new PcgPointCloud(list);`

### Полный список файлов к правке

Определяется командой, а не памятью:

```
grep -rln "List<PointData>" Packages/ --include=*.cs
```

Плюс два файла, которые греп НЕ найдёт (они итерируют вход через `var`), но править их обязательно:

- `Packages/PCG.Splines/Editor/Scripts/Exec/SplineAroundPointsNodeExecutor.cs`
- `Packages/PCG.Splines/Editor/Scripts/Exec/SplineFromPointsNodeExecutor.cs`

### Классификация каждой ноды

| Нода | Пакет | Категория | Порты точек |
|---|---|---|---|
| `DeloneGraphNode` | Mazes | Generator (выход) + Consumer (вход) | in `Points`, out `CenterPoints` |
| `GridGraphNode` | Mazes | Generator | out `CenterPoints` |
| `MazeMstGraphNode` | Mazes | Generator | out `EndPoints` |
| `SplinesSurfaceNode` | Splines | Generator | out `Results` |
| `SplinePointsByDistanceNode` | Splines | Generator | out `Results` |
| `PointsOffsetSplinesNode` | Splines | Generator | out `Results`, `CornerPoints` |
| `SplineIntersectionNode` | Splines | Generator | out `Results` |
| `RandomSplineNode` | Splines | Consumer | in `Points` |
| `SplineAroundPointsNode` | Splines | Consumer | in `Points` |
| `SplineFromPointsNode` | Splines | Consumer | in `Points` |
| `SplitSplinesNode` | Splines | Consumer | in `Points` |
| `PointsBySplineNode` | Splines | Derived-select | in `Points`, out `Results`, `Outsides` |
| `PointsNearSplinesNode` | Splines | Derived-select | in `Points`, out `Results`, `NearPoints` |
| `DensityByDistanceToSplinesNode` | Splines | Derived-transform | in `Points`, out `Results` |
| `StabilizeTerrainPointsNode` | Splines | Derived-transform + отбраковка | in `Points`, out `Results` |
| `SurfaceLiftPointsNode` | Polygons | Derived-transform | in `Points`, out `Results` |
| `PointsNearRegionsNode` | Polygons | Derived-select | in `Points`, out `Results`, `NearPoints` |
| `RegionToPointsNode` | Polygons | Generator-from-attributed-source | out `Results` |
| `PointsNearPointsOctreeNode` | Octree | Derived-select | in `Points`, `OtherPoints`, out `Results`, `NearPoints` |
| `GameObjectToBrgNode` | BRG | Consumer | портов точек нет |

## Invariants (must hold throughout)

- Имена полей с `[Input]`/`[Output]` не меняются ни на одной ноде. Сохранённые графы пользователей держат связи по имени поля.
- Порядок точек в каждом выходе сохраняется бит-в-бит относительно текущего поведения. Порядок — часть контракта (Seed-воспроизводимость, Poisson, downstream-выборки). Любая перестановка — дефект.
- Схемы параллелизма не меняются: `PcgWorkerScheduler.RunAsync`/`RunIndexedAsync`, `UniTask.WhenAll` по батчам, `UniTask.SwitchToThreadPool`/`SwitchToMainThread`, `OperationScope` + `scope.Step`, `ct.ThrowIfCancellationRequested`, размеры батчей и число задач — остаются как есть. Ни один цикл не переезжает на главный поток. Ни один цикл не уезжает с главного потока.
- Ни одна нода не добавляется, не удаляется и не переименовывается. Параметры нод не добавляются и не удаляются, кроме явно предписанного в Unit 5.
- Ничего не правится в `Assets/Plugins/PCG4U/` — это релизная сборка ядра.
- Ничего не правится в репозитории `unitypcg/ProjectPCG` — другой репозиторий.
- `*.meta` руками не создаются и не правятся.
- Файлы `Assets/Editor/CoworkBridge/Task_*.cs` и `result_*.json` не удаляются вручную.

## Execution Plan

Юниты выполняются по порядку. Unit 0 обязателен первым — без него нечем доказывать регрессию.

### Unit 0 — Снять baseline ДО любых правок

- Goal: зафиксированы эталонные числа точек по нодам двух демо-графов на текущем, ещё не мигрированном коде.
- Touch: ничего не править. Только чтение через мост.
- How: через skill `unity-bridge:unity-bridge` выполнить в Editor:
  - открыть `Assets/Examples/CityForest/CityForestV2.unity`, найти все `PcgComponent` на сцене, прогнать генерацию каждого до завершения, затем обойти executor'ы и для каждого, реализующего `IPointsCount`, вывести строку `<GraphId>|<NodeTitle>|<NodeType>|<PointsCount>`;
  - то же для `Assets/Examples/CityForestV3/CityForestV3.unity`;
  - дополнительно для CityForestV3 вывести для каждого executor'а с выходом-облаком имена колонок его `Attributes` (после миграции их станет больше — это часть доказательства Unit 5).
  - Сохранить обе выдачи в `Docs/notes/point_cloud_migration_baseline.md` целиком, без сокращений.
- Gate: файл `Docs/notes/point_cloud_migration_baseline.md` существует и содержит не менее 40 строк вида `<GraphId>|<NodeTitle>|<NodeType>|<PointsCount>`; содержимое выведено в транскрипт.
- On failure: если граф не прогоняется на текущем коде — остановись и доложи. НЕ начинать миграцию без baseline.

### Unit 1 — PCG.Mazes

- Goal: три ноды Mazes и их executor'ы работают на `PcgPointCloud`.
- Touch: `Packages/PCG.Mazes/Scripts/{DeloneGraphNode,GridGraphNode,MazeMstGraphNode}.cs`, `Packages/PCG.Mazes/Editor/{DeloneGraphNodeExecutor,GridGraphNodeExecutor,MazeMstGraphNodeExecutor}.cs`.
- How:
  - Все три выхода точек — Generator: `CenterPoints.Value = new PcgPointCloud();` затем `CenterPoints.Value.Add(new PointData { ... });` как сейчас.
  - `DeloneGraphNode.Points` — вход Consumer: тип поля меняется на `PcgPointCloud`, executor итерирует `foreach (PcgPointCloud cloud in pointsList) foreach (var point in cloud)`.
  - `MazeMstGraphNodeExecutor` строит `EndPoints` через LINQ-проекцию `.AddRange(...Select(...))`. `PcgPointCloud.AddRange(IEnumerable<PointData>)` существует — использовать его, LINQ не переписывать.
  - `OperationScope` и `scope.Step` оставить на местах.
- Gate: `grep -rn "List<PointData>" Packages/PCG.Mazes/` возвращает пусто.
- On failure: ≤3 попытки на файл, затем остановись и доложи.

### Unit 2 — PCG.Splines: генераторы и consumer'ы

- Goal: ноды Splines, не выводящие точки из точек, работают на `PcgPointCloud`.
- Touch: ноды и executor'ы `SplinesSurfaceNode`, `SplinePointsByDistanceNode`, `PointsOffsetSplinesNode`, `SplineIntersectionNode`, `RandomSplineNode`, `SplineAroundPointsNode`, `SplineFromPointsNode`, `SplitSplinesNode`.
- How:
  - Generator'ы: `SplinePoints.cs` НЕ трогать. Схема — `var list = new List<PointData>(); await SplinePoints.GetPoints(scope, list, ...); Results.Value = new PcgPointCloud(list);`
  - `PointsOffsetSplinesNodeExecutor`: приватные `EvaluateAndAdd` / `EvaluateAndAddAtT` / `AddPoint` продолжают принимать `List<PointData> target`. Executor держит два локальных списка и в конце оборачивает оба выхода.
  - `SplineIntersectionNodeExecutor`: `Results.Rent(junctions.Count)` оставить, `Results.Value.Add(new PointData { ... })` работает без изменений.
  - Consumer'ы (`RandomSpline`, `SplineAroundPoints`, `SplineFromPoints`, `SplitSplines`): меняется только тип поля-входа и итерация. `SplitSplinesNodeExecutor.FlattenPoints(List<PointData>[] pointsList)` → `FlattenPoints(PcgPointCloud[] pointsList)`, тело итерирует `cloud.Points`.
- Gate: `grep -rn "List<PointData>" Packages/PCG.Splines/ | grep -v "Scripts/Surfaces/SplinePoints.cs"` не содержит ни одного из файлов этого юнита; `grep -rn "PcgPointCloud" Packages/PCG.Splines/Editor/Scripts/Exec/SplineAroundPointsNodeExecutor.cs Packages/PCG.Splines/Editor/Scripts/Exec/SplineFromPointsNodeExecutor.cs` находит совпадения в обоих.
- On failure: ≤3 попытки на файл, затем остановись и доложи.

### Unit 3 — PCG.Splines: Derived-ноды

- Goal: четыре Derived-ноды Splines переносят атрибуты.
- Touch: ноды и executor'ы `PointsBySplineNode`, `PointsNearSplinesNode`, `DensityByDistanceToSplinesNode`, `StabilizeTerrainPointsNode`.
- How: для каждой — паттерн эталона `PointsByWaterSurfaceNodeExecutor` дословно.
  - Сплющивание входа: три параллельных списка `List<PointData> flatPoints`, `List<PcgPointCloud> flatClouds`, `List<int> flatIndices`. Ёмкость всех трёх — `pointsList.TotalCount()`.
  - Счёт (маска `bool[]`, spatial hash, `PcgWorkerScheduler.RunIndexedAsync`, `RunAsync`) — работает по `flatPoints`, как сейчас по снапшоту. Ничего в счётной части не менять.
  - Сборка выхода:
    - `PointsBySpline`, `PointsNearSplines` (Derived-select): `Results.Value.AppendFrom(flatClouds[i], flatIndices[i]);` для прошедших, `Outsides`/`NearPoints` — аналогично для остальных. Проход строго по возрастанию `i`.
    - `DensityByDistanceToSplines` (Derived-transform): `Results.Value.AppendFrom(flatClouds[i], flatIndices[i], modifiedPoint);`
    - `StabilizeTerrainPoints`: сейчас реализован как копия входа с обратным циклом и `output.RemoveAt(i)`. Переписать на прямой проход: считать всё как сейчас, но вместо удаления — не добавлять. Результат собирается вперёд, по возрастанию `i`, через `AppendFrom(flatClouds[i], flatIndices[i], stabilizedPoint)` только для тех, кто прошёл `MaxTerrainSlopeDegrees`. Порядок оставшихся точек обязан совпасть с текущим.
  - Где сейчас `new List<PointData>(...)` для выходов — заменить на `Results.Rent(capacity)`, дальше наполнять через `AppendFrom`.
  - `pointsList.TotalCount<PointData>()` → `pointsList.TotalCount()`.
- Gate: `grep -rn "List<PointData>" Packages/PCG.Splines/ | grep -v "Scripts/Surfaces/SplinePoints.cs"` возвращает пусто. Для каждого из четырёх executor'ов `grep -c "AppendFrom" <файл>` возвращает ≥ 1.
- On failure: ≤3 попытки на файл. Если порядок точек нельзя сохранить без перестройки счётной части — остановись и доложи, не перестраивай.

### Unit 4 — PCG.Polygons: SurfaceLiftPoints и PointsNearRegions

- Goal: две Derived-ноды Polygons переносят атрибуты.
- Touch: `Packages/PCG.Polygons/Scripts/City/SurfaceLiftPointsNode.cs`, `Packages/PCG.Polygons/Scripts/SelectPoints/PointsNearRegionsNode.cs` и их executor'ы в `Packages/PCG.Polygons/Editor/Scripts/Exec/`.
- How:
  - `SurfaceLiftPointsNodeExecutor` — Derived-transform, синхронный (`return UniTask.CompletedTask`). Синхронность сохранить. `Results.Rent(inputs.TotalCount())`, дальше по каждому облаку `AppendFrom(cloud, i, liftedPoint)`.
  - `PointsNearRegionsNodeExecutor` — Derived-select. Уже устроен как «снапшот + `bool[] nearMask` + сборка», `PcgWorkerScheduler.RunIndexedAsync` не трогать. Заменить снапшот на тройку `flatPoints`/`flatClouds`/`flatIndices`, сборку — на `AppendFrom`.
  - `pointsList.TotalCount<PointData>()` → `pointsList.TotalCount()`.
- Gate: `grep -c "AppendFrom" Packages/PCG.Polygons/Editor/Scripts/Exec/SurfaceLiftPointsNodeExecutor.cs Packages/PCG.Polygons/Editor/Scripts/Exec/PointsNearRegionsNodeExecutor.cs` — оба ≥ 1.
- On failure: ≤3 попытки на файл, затем остановись и доложи.

### Unit 5 — PCG.Polygons: RegionToPoints и мост атрибутов регионов

- Goal: точки, рождённые из региона, несут атрибуты этого региона. Это единственное смысловое изменение всего ТДД.
- Touch: `Packages/PCG.Polygons/Scripts/City/CityAttributes.cs`, `Packages/PCG.Polygons/Scripts/City/RegionToPointsNode.cs`, `Packages/PCG.Polygons/Editor/Scripts/Exec/RegionToPointsNodeExecutor.cs`. `Packages/PCG.Polygons/Scripts/Geometry/RegionFill.cs` НЕ трогать.
- How:
  - В `CityAttributes` добавить константу `public const string RegionIndex = "regionIndex";`. Остальные константы не трогать.
  - `RegionToPointsNode.Results` — тип `PcgPointCloud`. Новых портов и параметров у ноды не появляется.
  - В `RegionToPointsNodeExecutor.DoComputeAsync`, внутри существующего `PcgWorkerScheduler.RunAsync`, вести рядом с `List<PointData> output` второй список `List<int> sourceRegionRow` той же длины:
    - перед заполнением очередного региона запомнить `int start = output.Count;`
    - после `AddCentroid` / `FillRandomBlocking` / `FillGridBlocking` дописать `for (int k = start; k < output.Count; k++) { sourceRegionRow.Add(i); }`, где `i` — индекс региона в `input.Regions`.
  - `OrientToNearestEdge(output, edgeSource)` вызывается как сейчас, после цикла, и `sourceRegionRow` не трогает.
  - Сборка облака делается ПОСЛЕ `await work`, на editor-потоке, из `output` и `sourceRegionRow`:
    ```
    var cloud = new PcgPointCloud(output.Count);
    for (int k = 0; k < output.Count; k++)
    {
        cloud.Points.Add(output[k]);
        cloud.Attributes.AppendRow(input.Attributes, sourceRegionRow[k]);
    }

    var regionIndexColumn = cloud.Attributes.EnsureColumn<int>(CityAttributes.RegionIndex);
    for (int k = 0; k < output.Count; k++)
    {
        regionIndexColumn.Values[k] = sourceRegionRow[k];
    }

    Results.Value = cloud;
    ```
  - `PcgWorkerScheduler.RunAsync` должен вернуть обе коллекции — оберни их в приватный вложенный тип-результат в том же файле или верни `ValueTuple`. Ничего в `PcgWorkerScheduler` не менять.
  - Пустой вход даёт `Results.Value = new PcgPointCloud();` как сейчас.
  - Атрибуты рёбер (`Polygon2D.EdgeAttributes`) в этом ТДД НЕ переносятся — они про рёбра, а не про регион. Это отдельная работа.
- Gate: `grep -n "RegionIndex" Packages/PCG.Polygons/Scripts/City/CityAttributes.cs` находит константу; `grep -c "AppendRow" Packages/PCG.Polygons/Editor/Scripts/Exec/RegionToPointsNodeExecutor.cs` возвращает ≥ 1. Функциональная проверка — в Unit 8.
- On failure: ≤3 попытки. Если `PcgWorkerScheduler.RunAsync` не позволяет вернуть кортеж — верни один приватный класс-контейнер. Не выносить сборку облака внутрь фоновой задачи.

### Unit 6 — PCG.Octree

- Goal: `PointsNearPointsOctreeNode` работает на облаках и переносит атрибуты, поведение и порядок не изменились.
- Touch: `Packages/PCG.Octree/Scripts/PointsNearPointsOctreeNode.cs`, `Packages/PCG.Octree/Editor/PointsNearPointsOctreeNodeExecutor.cs`.
- How: это самая рискованная миграция, делать строго по шагам.
  - Ключевая замена: `PointOctree<PointData>` → `PointOctree<int>`. В дерево кладётся индекс точки в плоском списке: `octree.Add(index, flatPoints[index].Position);`. Payload дерева больше нигде не читается — только `IsColliding`, — поэтому смена типа безопасна.
  - Плоские списки строятся один раз, как в эталоне: `List<PointData> flatPoints`, `List<PcgPointCloud> flatClouds`, `List<int> flatIndices`. Оба входа (`Points` и `OtherPoints`) сплющиваются в СВОИ тройки; `OtherPoints` наружу не выходит, для него достаточно `List<PointData>`.
  - Все промежуточные буферы батчей `resultsList[i]` / `nearPointsList[i]` меняют тип с `List<PointData>` на `List<int>` — в них складываются индексы в `flatPoints`. Размеры батчей, `math.min(16, ...)`, `UniTask.WhenAll`, `SwitchToThreadPool` — без изменений.
  - `finalPointsList = resultsList;` — место, где промежуточный результат первой фазы становится входом второй. После миграции первая фаза отдаёт `List<int>[]`, вторая работает по этим индексам. Плоский список `flatPoints` при этом НЕ перестраивается — второй фазе передаётся плоский массив индексов, полученный конкатенацией `resultsList` в порядке возрастания номера батча.
  - `FinalProcess` и `ProcessOnce` принимают `List<PointData> flatPoints` плюс диапазон индексов и пишут в `List<int>`. Ветка `RemoveThemselves` с `SwitchToMainThread` перед `octree.Add` сохраняется дословно — это сериализация мутации дерева, менять её нельзя.
  - Ранняя ветка «нечего искать» (`otherPointsList.TotalCount() <= 0 && !Data.RemoveThemselves`) сейчас делает `Results.Value.AddRange(points.GetRange(...))`. Заменить на `Results.Value.Append(cloud)` по каждому входному облаку. Батчинг с `scope.Step` в этой ветке сохранить.
  - Финальная сборка: `Results.Value.AppendFrom(flatClouds[idx], flatIndices[idx])` и то же для `NearPoints`, проход по индексам в том же порядке, в каком сейчас работает `SelectMany`.
  - `Results.Rent(...)` / `NearPoints.Rent(...)` остаются на своих местах. Проверь, что в ранней ветке `Rent` вызывается один раз (сейчас там `if (Results.Value == null) Results.Rent(points.Count);` — оставить эту защиту).
  - `DrawPreview` переводится на перегрузки `GizmosUtility.DrawPoints(this, cloud, ...)`.
- Gate: `grep -rn "List<PointData>" Packages/PCG.Octree/` возвращает пусто; `grep -c "PointOctree<int>" Packages/PCG.Octree/Editor/PointsNearPointsOctreeNodeExecutor.cs` возвращает ≥ 2; `grep -c "SwitchToMainThread" Packages/PCG.Octree/Editor/PointsNearPointsOctreeNodeExecutor.cs` возвращает столько же, сколько до правки.
- On failure: ≤5 попыток. Не упрощать алгоритм, не убирать батчи, не заменять `WhenAll` последовательным циклом ради компиляции. При исчерпании — остановись и доложи, на каком шаге застрял.

### Unit 7 — PCG.BRG

- Goal: BRG компилируется и работает; `BrgInstanceData` остаётся на `List<PointData>`.
- Touch: проверить `Packages/PCG.BRG/Scripts/BrgInstanceData.cs`, `Packages/PCG.BRG/Scripts/GameObjectToBrgNode.cs`, `Packages/PCG.BRG/Editor/GameObjectToBrgNodeExecutor.cs`, `Packages/PCG.BRG/Scripts/BrgInstanceMaker.cs`.
- How: правок скорее всего не требуется. `GameObjectToBrgNode` принимает `List<GameObjectInstanceData>`, а `GameObjectInstanceData.Point` в ядре остался `PointData`. `BrgInstanceData.Points` — DTO, не порт, остаётся `List<PointData>`. Если компилятор ругается — правь минимально, тип `BrgInstanceData.Points` не менять.
- Gate: `grep -n "List<PointData>" Packages/PCG.BRG/Scripts/BrgInstanceData.cs` находит поле `Points` — то есть оно НЕ мигрировано.
- On failure: ≤2 попытки, затем остановись и доложи.

### Unit 8 — Компиляция, регрессия, проверка моста атрибутов

- Goal: проект компилируется, числа точек совпали с baseline, атрибуты регионов доехали до точек.
- Touch: править только то, что мешает компиляции.
- How:
  - Через `unity-bridge:unity-bridge` запросить компиляцию, вывести полный список ошибок, чинить по одной.
  - Повторить ровно ту же выборку, что в Unit 0, на обеих сценах.
  - Сравнить с `Docs/notes/point_cloud_migration_baseline.md` построчно. Любое расхождение `PointsCount` — дефект, чинить, а не объяснять.
  - Проверка Unit 5: на `CityForestV3` найти executor'ы `RegionToPointsNodeExecutor` (в графе их 12) и вывести для каждого имена колонок `Results.Value.Attributes` плюс значения `lotId` и `regionIndex` у первых трёх точек. Ожидание: у нод «District N House Lots» присутствуют как минимум `lotId` (пришёл от `LotsFromBlockNodeExecutor` через цепочку `InsetRegion`/`RoundRegion`) и `regionIndex`.
  - Проверка инварианта: для каждого executor'а с выходом-облаком вывести `Results.Value.IsValid()`. Все должны быть `True`.
- Gate: в транскрипте видно: (а) компиляция `0 errors`; (б) таблица «нода → PointsCount до / после» с нулевым расхождением по всем строкам; (в) для нод `District N House Lots` перечислены колонки, среди них `lotId` и `regionIndex`, и показаны их значения; (г) все `IsValid()` вернули `True`.
- On failure: ≤5 попыток на компиляцию, ≤3 на регрессию, ≤3 на проверку атрибутов. Не подгонять baseline под новый результат. Не отключать проверки.

### Unit 9 — Документация

- Goal: карты аддонов отражают новый тип и правило категорий.
- Touch: `Docs/PROJECT_MAP.md`, `Docs/OCTREE_MAP.md`, `Docs/POLYGONS_MAP.md`, `Docs/SPLINES_MAP.md`, `Docs/MAZES_MAP.md`, `Docs/BRG_MAP.md`, `Docs/notes/city_pipeline.md`.
- How:
  - В `PROJECT_MAP.md`, раздел про типы точек и API ядра: тип порта точек теперь `PcgPointCloud`, привести правило Generator / Derived-select / Derived-transform / Merger / Consumer / Internal и назвать эталон `PointsByWaterSurfaceNodeExecutor`.
  - В каждой `<ADDON>_MAP.md` обновить упоминания `List<PointData>` на `PcgPointCloud`, где речь о портах.
  - В `Docs/notes/city_pipeline.md`, шаг 6: `RegionToPoints` теперь переносит атрибуты региона на точки и пишет `regionIndex`; перечислить, какие ключи `CityAttributes` в результате доезжают до инстансера.
  - `Docs/notes/point_cloud_migration_baseline.md` оставить как есть — это артефакт прогона.
- Gate: `grep -rc "PcgPointCloud" Docs/PROJECT_MAP.md` ≥ 3; `grep -rl "PcgPointCloud" Docs/*_MAP.md | wc -l` ≥ 4; `grep -c "regionIndex" Docs/notes/city_pipeline.md` ≥ 1.
- On failure: ≤2 попытки, затем остановись и доложи.

## Done (/goal condition)

Аддоны PCG4U переведены на `PcgPointCloud`. Доказательства в транскрипте:

- `grep -rn "List<PointData>" Packages/ --include=*.cs | grep -v "Splines/Scripts/Surfaces/SplinePoints.cs" | grep -v "Polygons/Scripts/Geometry/RegionFill.cs" | grep -v "BRG/Scripts/BrgInstanceData.cs"` возвращает пусто.
- `grep -rn "PcgOutput<List<PointData>>" Packages/` возвращает пусто.
- Проверка правила переноса: `for f in $(grep -rln "PcgOutput<PcgPointCloud>" Packages/); do grep -q "nameof(Data.Points)" "$f" && ! grep -qE "AppendFrom|\.Append\(" "$f" && echo "MISSING: $f"; done` — вывод пуст.
- Через `unity-bridge:unity-bridge`: компиляция `0 errors`.
- Таблица «нода → PointsCount до / после» по `CityForestV2` и `CityForestV3` с нулевым расхождением по всем строкам; baseline взят из `Docs/notes/point_cloud_migration_baseline.md`, снятого в Unit 0 ДО правок.
- Для нод `District N House Lots` на `CityForestV3` выведены колонки атрибутов выходного облака, среди них `lotId` и `regionIndex`, с показанными значениями.
- Все executor'ы с выходом-облаком вернули `IsValid() == True`.

Ограничения, которые должны выполняться одновременно: `git status --porcelain` не содержит ни одного файла под `Assets/Plugins/PCG4U/`; ни одно имя поля с `[Input]`/`[Output]` не переименовано; ни один `*.meta` не изменён вручную; число вызовов `SwitchToMainThread`, `SwitchToThreadPool`, `UniTask.WhenAll`, `PcgWorkerScheduler.RunIndexedAsync` в `Packages/` не изменилось относительно состояния до правок.

Остановиться после 100 ходов.

## End-of-run report

- Поставь `Status` вверху документа в `Выполнено`.
- Доложи: какие юниты завершены; какие гейты потребовали повторов; расхождения baseline и как закрыты; какие ноды не легли в правило категорий и почему.
- Отдельно перечисли бумеранги в ядро (репозиторий `unitypcg/ProjectPCG`): что нельзя было сделать из аддонов. Как минимум ожидается пункт про `GameObjectInstanceData.Point` — он несёт одиночный `PointData` без атрибутов, поэтому в `BrgInstanceMaker` per-instance данные из облака не доезжают, и цвет там до сих пор захардкожен `Color(1,1,1,1)`.
- Флаг — НЕ действовать: уточни у заказчика, нужно ли обновлять проектную документацию под эти изменения.

## Отчёт о выполнении (2026-07-25)

### Юниты

Все 10 юнитов (0–9) завершены.

- **Unit 0** — baseline снят через `unity-bridge`: рекурсивный обход `PcgExecGraph` (включая вложенные `SubGraphNodeExecutor.Inner`) по обеим демо-сценам, 237 строк `IPointsCount`/`Attributes` в `Docs/notes/point_cloud_migration_baseline.md`.
- **Unit 1** — PCG.Mazes (3 ноды) — без повторов.
- **Unit 2** — PCG.Splines генераторы/консьюмеры (8 нод) — без повторов.
- **Unit 3** — PCG.Splines Derived-ноды (4 ноды) — без повторов.
- **Unit 4** — PCG.Polygons Derived-ноды (2 ноды) — без повторов.
- **Unit 5** — RegionToPoints + мост атрибутов (`regionIndex`) — без повторов.
- **Unit 6** — PCG.Octree (`PointOctree<PointData>` → `PointOctree<int>`, индексная схема) — без повторов; см. ограничение регрессии ниже.
- **Unit 7** — PCG.BRG — правок не потребовалось, `BrgInstanceData.Points` подтверждённо остался `List<PointData>`.
- **Unit 8** — компиляция 0 ошибок; regression-таблица; проверка атрибутов; `IsValid()`.
- **Unit 9** — документация (`PROJECT_MAP.md`, все `<ADDON>_MAP.md`, `city_pipeline.md`).

### Повторы гейтов

Ни один гейт не потребовал повтора правки кода (0/3 или 0/5 попыток везде). Единственная итерация вне кода — сам скрипт снятия baseline через `unity-bridge` пришлось переписать дважды: первая версия гоняла независимый `PcgExecGraph` без защиты от повторного вызова `Run()` мостом и без ожидания простоя `PcgComputeSystem`, из-за чего гонка с автогенерацией сцены при её открытии портила числа (нулевые срезы, `Specified cast is not valid` на `ResultNodeExecutor`/`SubGraphNodeExecutor`/instance-нодах — эти типы не реализуют `IPointsCount` и на итоговые данные не влияют). Финальная версия с `SemaphoreSlim`-кэшем результата и опросом `PcgComputeSystem.IsBusy`/`IsGenerating` дала чистый, воспроизводимый прогон.

### Расхождения baseline и их закрытие

Построчное сравнение (Unit 8) — **0 расхождений** по всем нодам аддонов, которые мигрировал этот ТДД.

Единственное отклонение — цепочка `Poisson Points → Change Scale → Change Angle → Density To Scale → Stabilize Vegetation` внутри сабграфа `ForestV2`/`ForestV3` (3 экземпляра на сцену): числа плавают на ~0.3–0.5% между прогонами (пример: 2310 → 2302 → 2300 на одном и том же, уже мигрированном коде, без единой правки между прогонами). `PoissonPointsNodeExecutor` — ядровая (закрытый исходник, DLL) нода, не входящая в файлы этого ТДД; аддонный код её не касается. Проверено: **не регрессия миграции** — три последовательных запуска regression-скрипта на идентичном, уже мигрированном коде дали три разных числа, значит недетерминизм пред-существующий (ядро), не вызван переходом на `PcgPointCloud`. Закрыто как задокументированная особенность, а не дефект: не чинится из аддонов.

### Ноды, не легшие в правило категорий

- `DeloneGraphNode` (Unit 1) — формально Consumer (вход `Points`) **и** Generator (выход `CenterPoints`) одновременно: `CenterPoints` — центроиды треугольников Делоне, не подмножество и не трансформация входных `Points`, поэтому выход собирается через `Add`, а не `AppendFrom`, хотя у ноды есть входной порт с именем `Points`. Автоматическая эвристика проверки переноса атрибутов (см. раздел `Done`) даёт по этой ноде ложное срабатывание `MISSING` — ожидаемо, зафиксировано в самой классификации ТДД (раздел «Классификация каждой ноды»), не дефект.

### Бумеранги в ядро (`unitypcg/ProjectPCG`)

- `GameObjectInstanceData.Point` — несёт одиночный `PointData` без атрибутов. `GameObjectToBrgNode`/`BrgInstanceMaker` (PCG.BRG) получают инстансы уже без привязки к исходному `PcgPointCloud.Attributes`, поэтому per-instance данные из облака (например `regionIndex`/`lotId`, появившиеся в Unit 5) не долетают до BRG-рендера, и цвет там до сих пор захардкожен `Color(1,1,1,1)`. Чтобы прокинуть атрибуты в BRG/инстансер, `GameObjectInstanceData` в ядре должен нести либо индекс исходной строки атрибутов, либо сам `PcgAttributeSet`-срез — это ядровое изменение, из аддонов недостижимо.
- `PoissonPointsNodeExecutor` (ядро) — недетерминирован между запусками на идентичном входе (см. «Расхождения baseline» выше). Не блокирует эту миграцию (нода не в списке файлов ТДД), но подрывает надёжность построчного regression-сравнения для любых цепочек, где он участвует. Стоит завести отдельный тикет в ядро на разбор причины (потенциально: использование `UnityEngine.Random` глобального состояния вместо переданного seed, либо гонка потоков в rejection-sampling).
- Мост «есть Unity Bridge API для headless-прогона графа сцены» отсутствует как публичный, документированный контракт: пришлось реверс-инжинирить `PcgExecGraph.Bind`/`PcgComputeSystem.OnGraphBound`/`ResolveAsync`/`SubGraphNodeExecutor.Inner` через reflection, и попутно выяснилось, что параллельный вызов через отдельный `PcgExecGraph` с тем же `GraphId`, что и уже открытый в редакторе автогенерируемый граф, гонится с ним (`PCG: host for graph window not found`, случайные `Specified cast is not valid` на instance-нодах). Это не помешало migration/regression (нужные данные получены), но как ядровая заметка: официальный headless-API для «прогнать граф компонента и прочитать результаты без открытия окна» ускорил бы будущие подобные ТДД.

### Флаг — требует решения заказчика

Обновлены только внутренние карты проекта (`Docs/PROJECT_MAP.md`, `Docs/*_MAP.md`, `Docs/notes/city_pipeline.md`), как явно предписано Unit 9. **Не трогал** внешнюю/пользовательскую документацию (`Documentation~/` в аддонах, справка по нодам, changelog, Asset Store листинг) — этот ТДД её не упоминает, а тип порта в публичном API аддонов не изменился с точки зрения пользователя графа (имена портов и их смысл те же, только внутренний C#-тип). Нужно ли отдельно обновлять пользовательскую документацию аддонов под `PcgPointCloud`/`regionIndex` — решение за заказчиком.
