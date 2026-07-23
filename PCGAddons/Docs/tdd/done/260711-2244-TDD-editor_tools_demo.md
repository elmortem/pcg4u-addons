# ТДД: Демо-сцена EditorTools (демка 1 — кисть, сплайн-тулзы, волюм)

Status: Собрано и сгенерировано; интерактивная приёмка (Paint/spline-drag/volume/undo) и перф — вручную (см. «Журнал сборки»)

Ревизия B — по ревью `260711-2244-TDD-editor_tools_demo-review.md`: честная таблица зависимостей, мейкеры и wiring явно, камни по обочинам через `Points Offset Splines`, пакетные зависимости, prerequisites ядра (планировщик AutoGenerate, взаимное исключение SceneView-инструментов, детерминизм батчей), измеримая приёмка.

Сборка демки 1 из `ProjectPCG/Docs/notes/unreal_pcg_demos_plan.md`: кистью красим лес, замкнутым сплайном выращиваем город, открытым сплайном кладём каменную дорожку, волюмом выключаем поляну. Терраформинг и покраска splatmap не используются (отложены до terrain-аддона) — дорога и тропа выполнены мешами и инстансами. Помимо сцены создаются два пресета-сабграфа, поставляемые с пакетами.

---

## Зависимости

Реализация сцены не начинается, пока каждая строка не в статусе «готово» и не пройден указанный smoke.

| Зависимость | Артефакт | Статус на 2026-07-13 | Gate |
|---|---|---|---|
| `260711-2237-TDD-paint_mask.md` (ProjectPCG) | PaintMask в ядре | Выполнено | — |
| `260711-2238-TDD-volume_zones.md` (ProjectPCG) | Volumes в ядре | Выполнено | — |
| `260711-2239-TDD-auto_generate.md` (ProjectPCG) | `AutoGenerate`/`PcgGraphRunner` | Выполнено; DLL 2026-07-13 установлены | зафиксировать ревизию ядра |
| `260711-2240-TDD-forest_preset.md` (ProjectPCG) | `Examples/SubGraphs/Forest.asset` + правка `DensityByPaintMaskNodeRenderer` (маска через `GetInputValue`, не только `Data.Mask` — иначе Paint не работает с инлайн-переменной `SubGraphNode`) | Не готов | ассет существует, Paint работает через пилюлю |
| `260711-2242-TDD-spline_tool.md` (ревизия D) | персистентная `SplineNode` | Не готов | приёмка ТДД пройдена |
| `260711-2243-TDD-sweep_package.md` (ревизия B) | пакет `PCG.Sweep` | Не готов | приёмка ТДД пройдена |

Prerequisites ядра (ProjectPCG, отдельные ТДД; без них соответствующие обещания демки снимаются):

1. **Планировщик AutoGenerate.** Текущий вотчер: тик 0.3 с × round-robin по одному компоненту × два одинаковых hash подряд × глобальный busy-gate — при трёх компонентах старт генерации через 0.9–1.8 с до самой генерации, обещание «≤1 с» невыполнимо. Нужны: dirty-set всех компонентов, trailing debounce от последнего изменения, coalescing, гарантированный финальный rerun после busy/cancel. До правки в критериях сцены используется измеренный SLA, не «≤1 с».
2. **Взаимное исключение SceneView-инструментов.** `PaintMaskPainter` и `PcgVolumeSceneEditor` независимо подписаны на `duringSceneGui` и вместе со spline-хендлами конкурируют за ЛКМ. Нужен единый контракт сессий: запуск Paint/Volume/Spline-edit завершает предыдущий инструмент; Esc завершает текущий. До правки сценарий ролика содержит явные Stop между шагами (записано ниже).
3. **Индексированные батч-слоты в `ChangeScaleNodeExecutor`/`ChangeAngleNodeExecutor`.** Сейчас слияние `lock + AddRange` в порядке завершения потоков — порядок точек недетерминирован, `Seed` не гарантирует воспроизводимость вниз по цепочке (`DESIGN_PRINCIPLES.md` прямо требует индексированные слоты и называет перевод старых нод частью доработки). До правки Seed-детерминизм фиксируется как известное ограничение в приёмке.
4. **`MeshInstanceMaker`: world-identity spawn при non-identity `Parent`** — из ТДД Sweep (там же и обновление DLL).

---

## Пресет CityBlocks (в пакет PCG.Polygons)

Ассет `Packages/PCG.Polygons/Presets/CityBlocks.asset` (`PcgSubGraph`, папку `Presets/` создать).

Пакетные зависимости: пресет использует `SplinesValue` (типы `PCG.Splines`); `PCG.Polygons` уже ссылается на assembly `PCG.Splines`, но `package.json` этого не декларирует — добавить `"com.elmortem.pcg.splines"` совместимой версии в dependencies (аддоны распространяются вместе; отсутствие декларации в отдельной установке даёт missing references). Smoke: чистый проект + пакет + задекларированные зависимости → пресет биндится без missing node/type/port.

Переменные блекборда:

| Имя | Тип | Дефолт |
|---|---|---|
| Terrain | TerrainObjectValue | пусто |
| Houses | GameObjectWeightsValue | пусто |
| RoadMaterial | MaterialValue | пусто |
| Seed | IntValue | 1 |

Граф:

- `Sub Graph Input` (`SubGraphInputNode`): `Name = "Splines"`, `Value` — тип `SplinesValue`.
- `Spline To Region` (`SplineToRegionNode`): `Splines` ← вход Splines, `MaxSegmentLength = 2`.
- `Subdivide Region` (`SubdivideRegionNode`): `Region` ← `SplineToRegion.Result`, `MinSize = 25`, `MaxDepth = 6`, `SplitJitter = 0.15`, `Seed` ← пилюля Seed.
- `Assign Road Class By Depth` (`AssignRoadClassByDepthNode`): `Blocks` ← `Subdivide.Blocks`, `MaxWidth = 8`, `MinDepth = 1`, `MaxDepth = 4`, кривая `WidthByDepth` дефолтная.
- `Blocks To Roads` (`BlocksToRoadsNode`): `Blocks` ← `AssignRoadClass.Result`, `Join = Round`, `Cap = Butt`.
- `Region To Mesh` (`RegionToMeshNode`): `Region` ← `BlocksToRoads.Roads`, `Terrain`/`Offset` ← пилюля Terrain, `Material` ← пилюля RoadMaterial, `Name = "Roads"`, `HeightOffset = 0.15`.
- `Sub Graph Output`: `Name = "Roads"`, тип `InstanceDatasValue`, вход ← `RegionToMesh.Results`.
- Ветка домов:
	- `Inset Region` (`InsetRegionNode`): `Region` ← `AssignRoadClass.Result`, `Delta = -6`.
	- `Lots From Block` (`LotsFromBlockNode`): `Blocks` ← `Inset.Result`, `LotWidth = 14`.
	- `Region To Points` (`RegionToPointsNode`): `Region` ← `Lots.Lots`, `Roads` ← `BlocksToRoads.Roads`, `Mode = Centroid`, `Margin = 1`, `Seed` ← пилюля Seed.
	- `Point To Terrain` (`PointToTerrainNode`): `Terrain`/`Offset` ← пилюля Terrain, `ProjectionMode = Surface`, `ProjectNormal = false`.
	- `Game Object Weights` (`GameObjectWeightsNode`): `Weights` ← пилюля Houses, `Seed` ← пилюля Seed.
	- `Sub Graph Output`: `Name = "Houses"`, тип `InstanceDatasValue`, вход ← `GameObjectWeights.Results`.

## Пресет StonePath (в пакет PCG.Sweep)

Ассет `Packages/PCG.Sweep/Presets/StonePath.asset` (`PcgSubGraph`; папку `Presets/` создать).

Пакетные зависимости: `com.unity.splines` уже в dependencies Sweep (ТДД 2243); пресет дополнительно использует `PointsOffsetSplinesNode` из `PCG.Splines` — добавить `"com.elmortem.pcg.splines"` в dependencies `PCG.Sweep`. Тот же isolated smoke, что у CityBlocks.

Переменные блекборда:

| Имя | Тип | Дефолт |
|---|---|---|
| Terrain | TerrainObjectValue | пусто |
| PathMaterial | MaterialValue | пусто |
| Stones | GameObjectWeightsValue | пусто |
| Width | FloatValue | 3 |
| StoneOffset | FloatValue | 1.8 |
| Seed | IntValue | 1 |

`StoneOffset` — боковое смещение камней от оси; инвариант пресета: `StoneOffset >= Width / 2 + радиус самого крупного камня` (камни не на полотне).

Граф:

- `Sub Graph Input`: `Name = "Splines"`, тип `SplinesValue`.
- Полотно:
	- `Sweep Spline` (`SweepSplineNode`): `Splines` ← вход Splines, встроенный профиль `Shape = Ribbon`, `Width` ← пилюля Width (отдельная нода `Profile` не нужна — Sweep самодостаточен, ревизия B ТДД 2243), `Terrain`/`TerrainOffset` ← пилюля Terrain, `HeightOffset = 0.08`, `UvScale = 0.5`, `Material` ← пилюля PathMaterial, `Name = "Path"`, `Collider = true`.
	- `Sub Graph Output`: `Name = "Path"`, тип `InstanceDatasValue`, вход ← `SweepSpline.Results`.
- Камни по обочинам:
	- `Points Offset Splines` (`PointsOffsetSplinesNode`): `Splines` ← вход Splines, `Distance = 1.2`, `Offset` ← пилюля StoneOffset, `BothSides = true` — точки на двух обочинах, следующие изгибам (мировой XZ-джиттер из прежней редакции клал камни на полотно и не образовывал обочин на поворотах).
	- `Point To Terrain` (`PointToTerrainNode`): `Terrain`/`Offset` ← пилюля Terrain, `ProjectionMode = Surface`, `ProjectNormal = true`.
	- `Change Scale` (`ChangeScaleNode`): `Min = 0.3`, `Max = 0.9`, `Mode = Set`, `Seed` ← пилюля Seed.
	- `Change Angle` (`ChangeAngleNode`): `Min = 0`, `Max = 360`, `Mode = Set`, `Seed` ← пилюля Seed.
	- `Game Object Weights`: `Weights` ← пилюля Stones, `Seed` ← пилюля Seed.
	- `Sub Graph Output`: `Name = "Stones"`, тип `InstanceDatasValue`, вход ← `GameObjectWeights.Results`.

## Контент сцены

Папка `Assets/Examples/EditorTools/` в проекте PCGAddons. Контент фиксируется точными путями — витрина продукта не должна зависеть от исполнителя:

- `EditorToolsScene.unity` — сцена.
- `Terrain/EditorToolsTerrain.asset` — `TerrainData` 500×500×60, heightmap resolution 513, пологие холмы, один травяной `TerrainLayer` (`Terrain/Grass.terrainlayer`).
- `ForestMask.asset` — `PaintMask` (`Resolution = 1024`); world-привязка на нодах: `Offset` — позиция террейна (порт Offset пилюли Terrain), размер — переменная `MaskSize` пресета Forest.
- `Materials/Road.mat` — тёмный асфальт/брусчатка, tiling под UV `RegionToMesh`; `Materials/Path.mat` — гравий, тайлится по метражу свипа. Шейдеры — штатный lit-шейдер render pipeline проекта PCGAddons; missing/pink материалы — фейл приёмки.
- `Prefabs/Houses/House1..3.prefab` — три дома из масштабированных кубов (разные пропорции/материалы, пивот в основании, bounds ~8–14 м).
- `Prefabs/Stones/Stone1..3.prefab` — камни (лоуполи-сферы со смятыми вершинами), радиус ≤ 0.5 м при `Scale = 1` (согласовано со `StoneOffset = 1.8` при `Width = 3`).
- Деревья — префабы из `Assets/Plugins/PCG4U/Examples` (лес из примера Forest ядра).
- Свет/окружение: один Directional Light (~50°/−30°, мягкие тени), штатный skybox; camera bookmark стартового кадра (весь участок: лес, город, тропа) сохраняется в сцене.

## Объекты сцены

- `Terrain` — из `EditorToolsTerrain.asset`, позиция `(0, 0, 0)`.
- `Forest`:
	- `PcgComponent` (`AutoGenerate = true`) + `GameObjectInstanceMaker`.
	- Граф: `SubGraphNode` (ассет `ForestDemo` — локальная копия поставляемого `Forest` из `Assets/Plugins/PCG4U/Examples/SubGraphs/`, в `Assets/Examples/EditorTools/`; поляны-волюмы сериализуются в ассете сабграфа — поставляемый не правится) → `Result`; связь `ForestDemo.<выход леса> → Result.Instances`.
	- Инлайн-значения: `Terrain` → сценовый Terrain, `Mask` → `ForestMask`, `MaskSize = (500, 500)`, `Trees` → веса деревьев, `CandidateCount = 15000`, остальное дефолт.
- Поляна: открыть `ForestDemo`, на `Points By Volumes` — `Add Box` (~35×30×25), хендлами внутрь будущей закрашенной зоны.
- `City` — `PcgComponent` + **`GameObjectInstanceMaker` + `MeshInstanceMaker`** (меню PCG Object не добавляет `MeshInstanceMaker` — добавить вручную и проверить, что оба в `PcgComponent.InstanceMakerComponents`), `AutoGenerate = true`:
	- Граф: `SplineNode` → `SubGraphNode` (`CityBlocks`) → `Result`; wiring буквально: `CityBlocks.Roads → Result.Instances` **и** `CityBlocks.Houses → Result.Instances` (оба dynamic-выхода).
	- На `SplineNode` — Start Edit, замкнутый сплайн (`Closed`) неправильным контуром ~180×140 м на ровном участке; Stop Edit.
	- Инлайн: `Terrain`, `Houses`, `RoadMaterial`, `Seed = 7`.
- `Path` — `PcgComponent` + **`GameObjectInstanceMaker` + `MeshInstanceMaker`** (без него `MeshInstanceData` полотна некому материализовать — появились бы камни без тропы), `AutoGenerate = true`:
	- Граф: `SplineNode` → `SubGraphNode` (`StonePath`) → `Result`; wiring: `StonePath.Path → Result.Instances` **и** `StonePath.Stones → Result.Instances`.
	- Открытый сплайн от города через лес, 200+ м (Start Edit → Stop Edit).
	- Инлайн: `Terrain`, `PathMaterial`, `Stones`, `Width = 3`, `StoneOffset = 1.8`, `Seed = 3`.

## Сценарий проверки (он же сценарий ролика)

До готовности prerequisite 2 (взаимное исключение инструментов) каждый шаг начинается с явного завершения предыдущего инструмента (Stop Paint / Stop Edit / выход из Edit Volumes) — одновременно активные Paint, volume- и spline-хендлы конкурируют за ЛКМ.

- Открыть сабграф Forest в контексте компонента (даблклик `SubGraphNode`), выбрать `Density By Paint Mask`, Paint: закрасить массив леса вокруг будущего города — лес дорастает по мере покраски (SLA из prerequisite 1); Ctrl+ЛКМ выстригает просеки. Stop Paint.
- Ctrl+Z/Ctrl+Y последнего штриха: маска и лес откатываются/возвращаются согласованно.
- На `SplineNode` объекта `City` — Start Edit, потянуть ручку: кварталы, дорога-меш и дома перестраиваются после стабилизации правки без Stop Edit; быстрые повторные правки не порождают stale/дублей — материализуется последнее состояние. Stop Edit.
- Потянуть сплайн `Path`: полотно и камни следуют за кривой, полотно облегает рельеф, камни остаются на двух обочинах S-образного участка и не пересекают полотно (с учётом bounds камней).
- `Edit Volumes` на `Points By Volumes`: подвинуть бокс-поляну — деревья исчезают на новом месте, вырастают на старом. Выйти из Edit Volumes.
- Ручной `Generate`/`Clear` из окна графа; автогенерация работает при закрытом окне.
- Save → reopen сцены: графы, маска, волюмы, сплайны, сгенерированное — без потерь; Generate после reopen не создаёт дублей.
- Domain reload при активной spline-сессии: правки не теряются, сирота подхватывается.

## Критерии приёмки

Функционал и изоляция:

- Все строки таблицы зависимостей в статусе «готово»; ревизия ядра/DLL зафиксирована в этом документе.
- Пресеты лежат в своих пакетах, не ссылаются на ассеты вне пакета и ядра; оба пресета проходят isolated smoke (чистый проект + задекларированные зависимости, bind без missing types/ports).
- У `City` и `Path` оба мейкера зарегистрированы; все четыре dynamic-выхода подключены и материализуются: у `Path` есть mesh-объект с `MeshRenderer`/`MeshFilter`/`MeshCollider` и инстансы камней.
- Сцена собирается без ошибок консоли; полный сценарий проверки проходит; в сцене нет покраски/деформации террейна.
- Пустой/невалидный вход свипа (обрыв сплайна) немедленно чистит полотно; отмена при быстрой правке не оставляет частичных объектов.

Детерминизм:

- После prerequisite 3: два полных recompute (с очисткой кеша) при неизменных входах дают идентичный набор prefab/transform. До него — известное ограничение, фиксируется здесь и не маскируется.

Перформанс (эталонная машина фиксируется; численные пороги — после baseline, до перевода в `Выполнено`):

| Кейс | Фиксированные входы | Метрики |
|---|---|---|
| Forest stroke | маска 1024, 15k candidates | debounce→start, end-to-visible, max main-thread stall, GC alloc |
| City drag | контур ~180×140 | end-to-visible, `RegionMeshBuilder` stall, spawn/removal |
| Path drag | 200+ м, Step 1, камни 1.2, Collider on | mesh build, collider cooking, cancel latency |
| Rapid edits | 10 быстрых правок каждого инструмента | число запущенных/отменённых генераций, отсутствие stale/дублей |
| Generate/Clear loop | 10 циклов на объект | утечки объектов/мешей/памяти |

Витрина:

- Нет розовых/отсутствующих материалов; дороги читаются на террейне; дома не пересекают дороги; камни не на полотне; лес не закрывает город с ракурса bookmark.
- 4 эталонных скриншота сохранены рядом со сценой: initial, painted forest, edited city, edited path + volume.

## Done-состав

- Смени статус в начале документа на `Выполнено`.
- Обнови `Docs/PROJECT_MAP.md` (PCG.Sweep, Presets, демо-сцена), `Docs/notes/city_pipeline.md` (канонический пресет CityBlocks), `ProjectPCG/Docs/notes/preset_subgraphs_library.md` (Forest, CityBlocks, StonePath), `Documentation~` пакетов (использование пресетов, обязательные мейкеры).

---

## Журнал сборки (2026-07-19)

Собрано и проверено генерацией через unity-bridge (Unity 2022.3.62f2, HDRP):

**Пресеты (задекларированы зависимости `com.elmortem.pcg.splines`):**
- `Packages/PCG.Polygons/Presets/CityBlocks.asset` — 17 нод, 22 связи, 5 переменных. Собран программно по спецификации ТДД (дороги + дома). Проверен генерацией: 1 меш `Roads` + инстансы домов (49).
- `Packages/PCG.Sweep/Presets/StonePath.asset` — 15 нод, 19 связей, 7 переменных. Проверен генерацией: 1 меш `Path` c `MeshCollider` + инстансы камней (452 на двух обочинах).
- Конвенция входа сплайнов — блекборд `SplinesValue` (`Id="Splines"`) → мультипорт `vSplines` на `SubGraphNode`; выходы — `o<id>` в `Result.Instances`. Мейкеры хоста: `GameObjectInstanceMaker` + `MeshInstanceMaker`.

**Сцена `Assets/Examples/EditorTools/EditorToolsScene.unity`:**
- Террейн `EditorToolsTerrain` (500×500×60, res 513, пологие холмы, травяной слой), материалы `Road`/`Path`, префабы `House1..3` (пивот в основании), `Stone1..3` (r≤0.5), `ForestMask` (PaintMask 1024), `ForestDemo` (локальная копия `Forest` + бокс-поляна на `Points By Volumes`).
- Объекты `Forest` (`ForestDemo`, 8013 деревьев, поляна + вырез вокруг города), `City` (`CityBlocks`, оба мейкера), `Path` (`StonePath`, оба мейкера). Все три генерируются без ошибок консоли, без розовых материалов.
- Скриншот-обзор `Screenshots/01-initial.png`.

**Осталось на ручную/интерактивную приёмку (bridge headless не воспроизводит):**
- Интерактивный сценарий: Paint кистью и SLA дорастания, live-перестройка при перетаскивании `SplineNode` без Stop Edit, `Edit Volumes`, Undo/Redo, domain reload при активной сессии.
- 3 из 4 эталонных скриншотов (painted forest / edited city / edited path+volume) — это интерактивные состояния.
- Детерминизм двух recompute (после prerequisite 3 ядра) и перф-базлайны эталонной машины.

Статус переведён в `Выполнено` не будет, пока эти пункты не пройдены вручную.
