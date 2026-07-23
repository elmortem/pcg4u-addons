Status: Выполнено

# Spline To Terrain + удаление terrain-драпировки из PCG.Sweep — Agent Execution Spec

Нода `Spline To Terrain` (`PCG.Splines`) укладывает сам сплайн на heightfield террейна. Она может пересобрать сплайн с фиксированным шагом для более точного прилегания, поднять его по мировому Y и направить Up узлов по нормали террейна.

`Sweep Spline` остаётся чисто геометрической нодой: строит профиль по входному 3D-сплайну, не читает террейн и не драпирует вершины. Собственный `HeightOffset` у Sweep сохраняется как полезный независимый мировой Y-сдвиг.

`StonePath` — существующий ассет-пресет `PcgSubGraph` по пути `Packages/PCG.Sweep/Presets/StonePath.asset`, не отдельная нода. Он переводится на `Spline To Terrain → Sweep Spline`.

## Обязательные источники

- `CLAUDE.md`.
- `Docs/DESIGN_PRINCIPLES.md`.
- `Docs/SPLINES_MAP.md`.
- `Docs/SWEEP_MAP.md`.
- skill `unity-bridge`.
- Authoring API `PCG.Authoring` для изменения `StonePath.asset`.
- `Docs/tdd/260711-2244-TDD-editor_tools_demo.md` для headless-генерации демо-сцены.

## Зафиксированная семантика

### TerrainOrigin и HeightOffset

`TerrainData` не содержит мировую позицию объекта `Terrain`.

- `TerrainOrigin` — мировая позиция террейна по X/Y/Z. Это координатная привязка heightmap, а не художественный отступ. В `StonePath` сюда подключается порт `Offset` переменной `TerrainObjectValue`.
- `HeightOffset` — дополнительный подъём результата по мировому Y.

Эти значения не взаимозаменяемы. Удалять `TerrainOrigin` нельзя: без него корректно поддерживается только террейн в `(0, 0, 0)`.

### Прилегание

На террейн проецируются knots сплайна. Sweep не меняет высоты входного сплайна, кроме собственного `HeightOffset`, и сохраняет жёсткий поперечный профиль.

Это не драпировка всей ширины полотна. На поперечном уклоне края Ribbon могут находиться выше или ниже террейна; проверяется центральная линия полотна.

### Resample

При `Resample = false` копируется исходный сплайн через `SplineCopyUtility.CopySpline`: сохраняются tangent modes, tensions и embedded `SplineData`.

При `Resample = true` до проекции строится новая AutoSmooth-сетка с фиксированным arc-length шагом `Step`, с теми же правилами, что у существующей ноды `Resample Splines`. Топология knots намеренно заменяется; исходные tangent modes, tensions и embedded `SplineData` не переносятся.

Общий алгоритм выносится в `SplineResampleUtility` и переиспользуется `ResampleSplinesNodeExecutor` и `SplineToTerrainNodeExecutor`.

### Terrain normal

`AlignToTerrainNormal = false` сохраняет исходный Up каждого узла.

`AlignToTerrainNormal = true` задаёт knot Up по нормали heightfield в спроецированной позиции. В Unity Splines 2.8.2 `BezierKnot.Rotation` вращает локальные tangents, поэтому простая замена Rotation запрещена. Реализация обязана:

1. сохранить мировые `TangentIn`/`TangentOut`;
2. построить новый ортонормальный frame из tangent и terrain normal;
3. записать новую Rotation;
4. перевести сохранённые мировые tangents в локальное пространство новой Rotation.

Включение `AlignToTerrainNormal` не должно менять позиции кривой относительно результата с выключенной опцией.

### Пустые и граничные случаи

- Нет `Terrain` — валидные входные сплайны проходят без изменений; `Resample`, `HeightOffset` и normal alignment не применяются.
- Null, пустые и одноточечные элементы пропускаются.
- Knot вне bounds террейна сохраняет исходные Y и Up; один compute пишет одно предупреждение.
- Порядок валидных входных сплайнов сохраняется.
- Исходные сплайны не мутируются.

## Публичный контракт SplineToTerrainNode

`Packages/PCG.Splines/Scripts/Splines/SplineToTerrainNode.cs`:

- `[Input] List<Spline> Splines = new();`
- `[Input] TerrainData Terrain;`
- `[Input] Vector3 TerrainOrigin;`
- `[Input] float HeightOffset = 0.1f;`
- `bool AlignToTerrainNormal;`
- `bool Resample;`
- `[Input] float Step = 2f;`
- `[Output] List<Spline> Results => default;`

Display name: `Spline To Terrain`. Category: `Splines`.

## Инварианты

- `Assets/Plugins/PCG4U/**` не изменяется.
- `Packages/PCG.Polygons/Presets/CityBlocks.asset` не изменяется.
- `*.meta` не создаются, не редактируются и не удаляются вручную.
- `StonePath.asset` изменяется только через `PCG.Authoring`.
- Сцены и ассеты Unity изменяются только через Unity Editor / Bridge.
- Геометрические формулы Sweep, не относящиеся к terrain-веткам, не меняются.
- `SweepSplineNode.HeightOffset` и его применение во всех mesh-builder путях сохраняются.
- Unit-ы выполняются по порядку. При исчерпании лимита исправлений текущего Unit выполнение останавливается.

## Unit 1 — Spline To Terrain

### Touch

- создать `Packages/PCG.Splines/Scripts/Splines/SplineToTerrainNode.cs`;
- создать `Packages/PCG.Splines/Editor/Scripts/Exec/SplineToTerrainNodeExecutor.cs`;
- создать `Packages/PCG.Splines/Editor/Scripts/Exec/SplineTerrainWindow.cs`;
- создать `Packages/PCG.Splines/Editor/Scripts/Tools/SplineResampleUtility.cs`;
- обновить `Packages/PCG.Splines/Editor/Scripts/Exec/ResampleSplinesNodeExecutor.cs`.

### Реализация

- Все сплайны в графе трактуются как world-space.
- Heightmap и его bounds снимаются на главном потоке.
- Высоты и нормали knots вычисляются из immutable heightmap-window в thread pool.
- Каждые 1024 итерации: cancellation + `PcgComputeSystem.ReportProgress`.
- Создание/изменение `Spline`, установка knots и финальное заполнение `Results` выполняются на editor thread через `OperationScope`.
- `SplineTerrainWindow.TrySampleHeight` делает билинейный семпл.
- `SplineTerrainWindow.TrySampleNormal` вычисляет нормализованный градиент того же билинейного heightfield в world-space.
- `GetVersionSalt` подмешивает `PcgTerrainContentVersion.Get` разрешённого `TerrainData`.
- Preview использует `SplinesGizmoUtility`.

### Gate

Bridge-задача с `AssetDatabase.Refresh` завершается без compiler errors.

Лимит: до трёх исправлений по фактическим compiler errors.

## Unit 2 — Smoke высоты, нормали и копирования

### Touch

Только временные Bridge-задачи.

### Проверки

На Terrain из `Assets/Examples/EditorTools/EditorToolsScene.unity`:

1. `SplineTerrainWindow.TrySampleHeight` в 10 точках сравнивается с `TerrainData.GetInterpolatedHeight + TerrainOrigin.y`.
2. `TrySampleNormal` сравнивается с `TerrainData.GetInterpolatedNormal`; допускается угловая погрешность не более `1°`.
3. Проекция без resample:
	- исходный spline hash не изменился;
	- количество knots не изменилось;
	- tangent modes, tensions и embedded data сохранены;
	- Y каждого in-bounds knot равен terrain height + `HeightOffset`.
4. Проекция с resample:
	- knot count увеличился на достаточно длинном тестовом сплайне;
	- количество сегментов и позиции совпадают с существующим fixed-step алгоритмом `Resample Splines`;
	- все новые knots лежат на heightfield с заявленным offset.
5. Normal alignment:
	- knot Up совпадает с terrain normal с допуском `1°`;
	- evaluated positions результатов с alignment on/off совпадают с допуском `0.001`.
6. Out-of-bounds knot сохраняет исходные Y и Up.

### Gate

Bridge возвращает:

`OK maxHeightDelta=<...> maxNormalAngle=<...> maxAlignedPositionDelta=<...> sourceUnchanged=True`

Пороги: `maxHeightDelta <= 0.001`, `maxNormalAngle <= 1`, `maxAlignedPositionDelta <= 0.001`.

Лимит: до двух исправлений окна/normal alignment.

## Unit 3 — Удаление terrain-зависимостей из текущего PCG.Sweep

`HeightOffset` не удаляется.

### Touch

- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepTerrainWindow.cs` — удалить через Unity `FileUtil` вместе с `.meta`: package-path не принимается `AssetDatabase.DeleteAsset` как валидный asset path.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSnapshot.cs` — удалить только `Terrain`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshData.cs` — удалить `TerrainOutOfBounds`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs`:
	- удалить `using PCG.Terrains`;
	- удалить перенос `Terrain` в piece snapshot и `Terrain = null` в основном snapshot;
	- удалить все `outOfBounds`, `TerrainOutOfBounds` и terrain-warning;
	- сохранить чтение, snapshot и применение `HeightOffset`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshBuilder.cs`:
	- удалить `Terrain`, `hasTerrain`, `verticalOffsets`, `rightXz`;
	- позиция вершины до trim: `basePos + right * rx + up * ry`;
	- сохранить финальный `p.y += snapshot.HeightOffset`;
	- убрать `out bool outOfBounds`.
- `Packages/PCG.Sweep/Editor/Scripts/Patch/SweepProfileMeshBuilder.cs` — удалить terrain-ветвь и флаг, сохранить мировой Y-сдвиг.
- `Packages/PCG.Sweep/Editor/Scripts/Patch/SweepRibbonMeshBuilder.cs` — `Elevate` заменить простым применением `HeightOffset`, убрать флаг.
- `Packages/PCG.Sweep/Editor/Scripts/Patch/SweepRibbonCornerFanBuilder.cs` — удалить terrain-семплинг и флаг; оставить уже вычисленные геометрические Y с `HeightOffset`.
- `Packages/PCG.Sweep/Editor/Scripts/Patch/SweepRibbonSplitResult.cs` — удалить `TerrainOutOfBounds`.

`SweepRibbonPatchBuilder` не правится: он не содержит terrain-зависимостей и продолжает применять `HeightOffset`.

Отсутствующие в текущем репозитории старые `SweepNetworkSnapshot`, `SweepNetworkSolver`, `SweepJunctionMeshBuilder`, `SweepJunctionInterpolator` не входят в работу.

### Gate

- Bridge-компиляция без ошибок.
- `rg -n -i "terrain|TerrainOutOfBounds" Packages/PCG.Sweep/Scripts Packages/PCG.Sweep/Editor` — пусто.
- `rg -n "HeightOffset" Packages/PCG.Sweep/Scripts Packages/PCG.Sweep/Editor` — непусто.
- `dotnet build PCG.Sweep.Editor.csproj --no-restore` — 0 errors.
- Live Sweep smoke сохраняет:
	- Ribbon generation;
	- HalfPipe generation при `MergeIntersections=false` и `true`;
	- complete topology, 0 degenerate triangles, finite vertices/UV/normals/tangents.

Лимит: до трёх исправлений по compiler errors или live-smoke failures.

## Unit 4 — StonePath

### Touch

`Packages/PCG.Sweep/Presets/StonePath.asset` только через `PCG.Authoring`.

### Изменение графа

Через snapshot найти:

- variable-node `Splines`;
- `SweepSplineNode`;
- `PointsOffsetSplinesNode`;
- variable-node `Terrain`;
- существующий `PointToTerrainNode` для камней.

В одной `PcgGraphEditSession`:

1. Добавить `SplineToTerrainNode`.
2. Установить:
	- `Resample = true`;
	- `Step = 2`;
	- `AlignToTerrainNormal = true`;
	- `HeightOffset = 0.08`.
3. Старый `SweepSplineNode` содержит две осиротевшие связи к уже отсутствующим портам `Terrain` и `TerrainOffset`. `Disconnect` не может удалить edge с отсутствующим target-port, поэтому:
	- сохранить параметры и все валидные связи Sweep;
	- удалить старый `SweepSplineNode`, что удалит и осиротевшие edges;
	- добавить текущий `SweepSplineNode`;
	- восстановить параметры и валидные связи, кроме прямого входа Splines;
	- установить новому `SweepSplineNode.HeightOffset = 0`.
4. Отключить input Splines → `PointsOffsetSplinesNode.Splines`.
5. Подключить пять новых edges:
	- input Splines → `SplineToTerrainNode.Splines`;
	- Terrain value → `SplineToTerrainNode.Terrain`;
	- Terrain offset → `SplineToTerrainNode.TerrainOrigin`;
	- `SplineToTerrainNode.Results` → `SweepSplineNode.Splines`;
	- `SplineToTerrainNode.Results` → `PointsOffsetSplinesNode.Splines`.
6. Существующий `PointToTerrainNode` после `PointsOffsetSplinesNode` оставить: боковые точки камней проецируются отдельно.
7. `Validate`, `AutoLayout`, `Commit(Save)`.

### Gate

Bridge возвращает успешный Commit, `errors=0` и snapshot с пятью перечисленными edges. В snapshot ровно один `SplineToTerrainNode`, отдельного `ResampleSplinesNode` нет.

Лимит: один повтор после rollback/Dispose.

## Unit 5 — EditorToolsScene

### Touch

- `Assets/Examples/EditorTools/EditorToolsScene.unity` — только через открытие, генерацию и сохранение в Unity Editor.
- `Assets/Examples/EditorTools/Screenshots/SplineToTerrain.png`.

### Проверка

1. Открыть сцену.
2. Выполнить headless generation объекта `Path` через штатный `PcgGraphRunner`.
3. Убедиться, что дочерний результат содержит `MeshFilter`, `MeshRenderer`, `MeshCollider`.
4. Для Ribbon брать 10 равноудалённых поперечных пар вершин. Перевести вершины в world-space, взять midpoint пары и сравнить:

`midpoint.y` против `terrainHeight(midpoint.x, midpoint.z) + 0.08`.

Края Ribbon с террейном не сравниваются.

5. Проверить отсутствие console errors.
6. Сохранить сцену и SceneView screenshot.

### Gate

Bridge возвращает:

`OK maxCenterDev=<...> vertices=<...> errors=0`

Пороги: `maxCenterDev <= 0.5`, `vertices > 100`.

Лимит: не более двух генераций; при превышении допуска один диагностический вывод 10 пар.

## Unit 6 — Документация

### Touch

- `Docs/SWEEP_MAP.md`;
- `Docs/SPLINES_MAP.md`;
- `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md`;
- `Packages/PCG.Splines/Documentation~/Splines-Addon.md`.

### Требования

- Sweep-документация не обещает terrain-драпировку и не содержит `Terrain`, `TerrainOffset`, `SweepTerrainWindow`, `TerrainOutOfBounds`.
- `SweepSplineNode.HeightOffset` остаётся документирован как мировой Y-сдвиг.
- Удаляются устаревшие описания отсутствующей topology/network-архитектуры.
- Документируется текущий `MergeIntersections` pipeline без изменения его кода.
- Splines-документация описывает `SplineToTerrainNode`, `TerrainOrigin`, оба режима resample, normal alignment, out-of-bounds и отсутствие полной драпировки полотна.
- `StonePath` описан как asset preset с цепочкой `Spline To Terrain → Sweep Spline`.

### Gate

- `rg -n -i "drap|драпиров|SweepTerrainWindow|TerrainOutOfBounds" Docs/SWEEP_MAP.md Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — пусто.
- `rg -n "HeightOffset" Docs/SWEEP_MAP.md Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — непусто.
- `rg -n "SplineToTerrainNode|Spline To Terrain" Docs/SPLINES_MAP.md Packages/PCG.Splines/Documentation~/Splines-Addon.md` — непусто.

## Done

Готово только когда:

- все Unit 1–6 закрыты;
- все Bridge gates имеют `status=success`;
- Unit 2 прошёл численные пороги;
- PCG.Sweep не содержит terrain-зависимостей и сохраняет `HeightOffset`;
- `StonePath` закоммичен через Authoring и содержит требуемые пять edges;
- `EditorToolsScene` прошла centerline-проверку;
- документационные grep-gates сходятся;
- `git status` не показывает изменений `Assets/Plugins/PCG4U/**` и `CityBlocks.asset`;
- новые `.meta` созданы Unity, удалённые `.meta` удалены Unity.

## End-of-run

### Фактические результаты

- Unity compile: 0 compiler errors.
- Численная проверка heightfield: `maxHeightDelta=0.0003080368`, `maxNormalAngle=0.07401875°`, `maxAlignedPositionDelta=0`, исходный сплайн не изменён.
- `PCG.Sweep`: terrain-зависимости отсутствуют; `dotnet build PCG.Sweep.Editor.csproj --no-restore` — 0 errors.
- HalfPipe live smoke:
	- `MergeIntersections=false`: 2 меша, 6417 вершин, 11376 треугольников;
	- `MergeIntersections=true`: 2 меша, 6417 вершин, 11376 треугольников;
	- вершины, normals и tangents конечны; вырожденных треугольников нет.
- `StonePath.asset`: Authoring Commit успешен, `errors=0`, один `SplineToTerrainNode`, пять требуемых связей.
- `EditorToolsScene`: Path сгенерирован и сохранён; 544 вершины, 272 станции, 20 проверенных midpoint; `maxOffsetError=0.0005`.
- Screenshot: `Assets/Examples/EditorTools/Screenshots/SplineToTerrain.png`.

### Отклонения и повторы

- Старые связи `Terrain → SweepSpline.Terrain` и `Offset → SweepSpline.TerrainOffset` уже ссылались на отсутствующие порты. Authoring API не позволяет вызвать `Disconnect` для отсутствующего порта, поэтому `SweepSplineNode` пересоздан в той же edit-session с сохранением всех параметров и валидных связей. Это удалило обе осиротевшие связи без ручного редактирования YAML.
- `AssetDatabase.DeleteAsset` не принял физический package-path `Packages/PCG.Sweep/...`; `SweepTerrainWindow.cs` и `.meta` удалены следующей Bridge-задачей через Unity `FileUtil`.
- Первый screenshot-helper использовал отложенный callback, который не завершился внутри Bridge-задачи. Screenshot повторён синхронным рендером камеры `SceneView`.
- `SweepDemoScene` содержит существующую null managed-reference запись и не открывается через Authoring API. HalfPipe smoke переключал живой `SweepSplineNode` только в памяти, восстановил исходные значения и закрыл сцену без сохранения.

### Сохранённые обязанности HeightOffset

`SweepSplineNode.HeightOffset` остаётся публичным input и одинаково применяется как мировой Y-сдвиг в обычном profile-builder пути, Ribbon, HalfPipe, merge pieces, corner fans и junction patches. Он не зависит от наличия террейна.
