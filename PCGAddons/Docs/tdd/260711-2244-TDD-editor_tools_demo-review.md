# Ревью ТДД `260711-2244-TDD-editor_tools_demo.md`

Дата ревью: 2026-07-13  
Ревьюируемый документ: `Docs/tdd/260711-2244-TDD-editor_tools_demo.md`  
Режим ревью: статическая проверка ТДД против текущего `PCGAddons`, зависимых ТДД, принципов продукта и живого кода ядра в `ProjectPCG`.

## Вердикт

**REWORK — направление правильное, но выполнять ТДД в текущем виде рано.**

Демо хорошо укладывается в продуктовую идею PCG4U: пользователь остаётся в графе, авторские данные живут на нодах/в контент-ассете, Scene View используется для прямого редактирования, а сцена показывает композицию готовых примитивов вместо отдельного «демо-фреймворка». Отказ от преждевременной деформации террейна тоже разумен: mesh-дорога и sweep-тропа дают честный законченный вертикальный срез без скрытой зависимости от ещё не существующего terrain-аддона.

Однако сейчас есть блокеры, из-за которых заявленный сценарий либо не запускается, либо не выдерживает собственные UX/performance-обещания:

- ТДД объявляет зависимости выполненными, хотя `Forest`, персистентный `SplineNode` и `PCG.Sweep` в текущих checkout ещё не готовы.
- На `Path` не задан `MeshInstanceMaker`, поэтому появятся камни, но не mesh-полотно тропы.
- У пакетных пресетов есть незадекларированная зависимость от `PCG.Splines`.
- Текущий `AutoGenerate` при трёх компонентах не гарантирует старт генерации за `≤1 с`.
- Paint, volume edit и spline edit могут одновременно владеть Scene View и конфликтовать за ЛКМ/хендлы.
- Камни из описанного графа рассеиваются в мировом XZ-квадрате, а не по обочинам.
- Приёмка производительности, Undo/Redo, persistence, отмены вычислений и package isolation недостаточна.

После закрытия P0/P1 ниже идея становится цельной и достойной флагманской демо-сцены.

## Шкала приоритетов

- **P0** — ТДД нельзя начинать или принять без исправления.
- **P1** — основной сценарий функционально сломан либо создаёт серьёзную UX/дистрибутивную проблему.
- **P2** — важный пробел качества, воспроизводимости или сопровождения.
- **P3** — уточнение контракта, снижающее неоднозначность реализации.

## Что в ТДД сделано хорошо

1. **Нодо-центричная модель сохранена.** `SplineNode`, paint-mask и volumes редактируются через ноды и Scene View, без отдельного постоянного компонента-тулзы. Это соответствует `Docs/DESIGN_PRINCIPLES.md:17-32`.
2. **Правильно разделены reusable-пресеты и сценовый контент.** `CityBlocks`/`StonePath` не получают конкретные материалы и префабы в дефолтах, а сценовая копия `ForestDemo` защищает поставляемый `Forest.asset` от scene-specific правок volumes.
3. **World-привязка маски описана корректно.** `TerrainObjectValue` даёт `Terrain` и `Offset`, а `MaskSize` остаётся параметром пресета; конфигурация не прячется в `PaintMaskAsset`.
4. **Городской граф собирается из существующих контрактов.** Цепочка совпадает с живым пайплайном `SplineToRegion → SubdivideRegion → AssignRoadClassByDepth → BlocksToRoads`, описанным в `Docs/PROJECT_MAP.md:233-237`.
5. **Deferred scope сформулирован честно.** Splatmap и деформация террейна не имитируются; дорога и тропа остаются мешами/инстансами.
6. **Есть понятный showcase-сценарий.** Кисть, закрытый/открытый сплайн и volume показывают разные способы авторинга в одном связном мире, а не в наборе изолированных тестов.

## Найденные проблемы

### [P0] Зависимости объявлены выполненными, но текущий checkout этому противоречит

ТДД утверждает, что все зависимости выполнены (`260711-2244...:5-8`), но на момент ревью:

- `ProjectPCG/Docs/tdd/260711-2240-TDD-forest_preset.md` имеет `Status: Не готов`, а `Assets/Plugins/PCG4U/Examples/SubGraphs/Forest.asset` отсутствует.
- `Docs/tdd/260711-2242-TDD-spline_tool.md` имеет `Status: Не готов`; живой `Packages/PCG.Splines/Scripts/Splines/SplineNode.cs:11-16` всё ещё не содержит сериализованного `List<Spline>`, а executor хранит результат только в value-output (`SplineNodeExecutor.cs:18-37`).
- `Docs/tdd/260711-2243-TDD-sweep_package.md` имеет `Status: Не готов`; каталога `Packages/PCG.Sweep/` нет.
- `Packages/PCG.Polygons/Presets/CityBlocks.asset` также ещё не существует.
- В живом `ProjectPCG/.../DensityByPaintMaskNodeRenderer.cs:12-16,52-64` маска всё ещё читается через `Data.Mask`, поэтому Paint для маски, пришедшей через inline-переменную `SubGraphNode`, не запускается. Исправление только запланировано в Forest-ТДД.
- Рядом уже есть отдельное ревью `Docs/tdd/260713-1506-REVIEW-spline_tool.md:7-20,31-44` с вердиктом «доработать до реализации»: в spline-контракте не закрыты Undo, world-space transform, nested ownership/identity, полный round-trip состояния Unity `Spline`, cleanup и release-DLL dependency.
- `Docs/tdd/260713-1508-REVIEW-sweep_package.md:7-21,23-38` также запрещает реализацию текущей ревизии Sweep: не закрыты clean install, stale output на пустом input, invalid slots, самостоятельный backing UX, immutable snapshot, terrain invalidation и полный performance pipeline.
- Установленная в `PCGAddons` release-DLL, по проверке `260713-1506-REVIEW-spline_tool.md:160-169`, ещё не содержит требуемые `AutoGenerate`/`PcgGraphRunner` типы, хотя актуальные исходники `ProjectPCG` их уже содержат.

Это не недостаток выбранной архитектуры, но это hard gate: без фактических ревизий зависимостей нельзя собрать или профилировать описанную сцену.

**Что изменить в ТДД:**

- Заменить фразу «все выполнены» на проверяемую таблицу `dependency → required revision/artifact → status`.
- Запретить начало реализации сцены, пока не существуют `Forest.asset`, revised persistent `SplineNode`, revised `PCG.Sweep`, не закрыты P1 их отдельных ревью и не пройдены фактические smoke-tests.
- После синхронизации DLL зафиксировать ревизию/commit ядра, против которой собрана сцена.

### [P1] `Path` не сможет материализовать sweep-меш

Для `City` ТДД явно требует `GameObjectInstanceMaker` и `MeshInstanceMaker` (`260711-2244...:94-97`). Для `Path` сказано только «второй PCG Object» (`:98-101`).

Живое меню `GameObject → PCG → PCG Object` добавляет `PcgComponent`, `GameObjectInstanceMaker` и `TerrainDetailInstanceMaker`, но **не** `MeshInstanceMaker` (`ProjectPCG/.../PcgMenuCreator.cs:10-19`). `PcgComponent.AddInstances` останавливается на первом maker, принявшем тип данных (`ProjectPCG/.../PcgComponent.cs:142-154`), поэтому `MeshInstanceData` от `SweepSplineNode` некому создать. Камни появятся, полотно — нет.

**Что изменить в ТДД:**

- Для `Path` явно потребовать `GameObjectInstanceMaker + MeshInstanceMaker`.
- После добавления компонентов проверить, что оба входят в `PcgComponent.InstanceMakerComponents`.
- В приёмке отдельно проверять наличие одного mesh-объекта `Path`, его `MeshRenderer`, `MeshFilter`, `MeshCollider` и набора stone-prefab instances.

### [P1] Пакетные зависимости от `PCG.Splines` скрыты

Оба пресета используют типы/ноды `PCG.Splines`:

- `CityBlocks` использует `SplinesValue`; сам `PCG.Polygons.Editor` уже ссылается на assembly `PCG.Splines` (`Packages/PCG.Polygons/Editor/Scripts/PCG.Polygons.Editors.asmdef:4-11`).
- `StonePath` использует `SplinesValue` и `SplinePointsByDistanceNode` (`260711-2244...:60,66`).

При этом `Packages/PCG.Polygons/package.json:24-27` декларирует только `com.unity.mathematics` и `com.unity.splines`, а будущий `PCG.Sweep` по зависимому ТДД декларирует только `com.unity.mathematics` (`260711-2243-TDD-sweep_package.md:33-35`). У Sweep отсутствует даже прямая compile/import dependency `com.unity.splines`, отмеченная в `260713-1508-REVIEW-sweep_package.md:42-57`; добавление `StonePath` сверху требует ещё и addon dependency `com.elmortem.pcg.splines`. Проверка «не ссылаться на ассеты вне своего пакета и ядра» (`260711-2244...:113`) не ловит отсутствующую assembly/package dependency.

В монорепо это маскируется соседним embedded-пакетом. В отдельной установке пакет получит missing managed references или нерабочий пресет.

**Что изменить в ТДД:**

- Для базового Sweep обязательно задекларировать `com.unity.splines`; для обоих preset-host packages задекларировать `com.elmortem.pcg.splines` совместимой версии, если интеграционные пресеты остаются внутри них.
- Либо вынести интеграционные пресеты в пакет/слой samples, который явно зависит сразу от Polygons, Splines и Sweep.
- Добавить isolated-package smoke-test: чистый Unity-проект, установка только пакета и его задекларированных зависимостей, bind обоих `PcgSubGraph` без missing node/type/port.

### [P1] Обещание `≤1 с` несовместимо с текущим планировщиком `AutoGenerate`

В сцене три компонента с `AutoGenerate = true`: `Forest`, `City`, `Path` (`260711-2244...:89-101`). Watcher в актуальных исходниках `ProjectPCG`, который ещё предстоит синхронизировать в release-DLL:

- делает глобальный тик раз в `0.3 с` (`ProjectPCG/.../PcgAutoGenerateWatcher.cs:41-53`);
- на каждом тике проверяет только один компонент round-robin (`:62-73,108-122`);
- требует два последовательных одинаковых source hash перед запуском (`:92-105`);
- полностью останавливает сканирование при глобальном `PcgComputeSystem.IsBusy` или `_running` (`:43-48,104-105,175-192`).

При трёх компонентах один компонент наблюдается примерно раз в `0.9 с`. Следовательно, стабильное изменение запускает генерацию примерно через `0.9–1.8 с` **до учёта времени самой генерации**. Текущая реализация не может гарантировать заявленные в сценарии `≤1 с` после stroke/release (`260711-2244...:105-107`).

Кроме того, «два одинаковых опроса» — не настоящий trailing debounce: при паузе во время медленного drag тяжёлая генерация может начаться до отпускания хендла.

**Что изменить в ТДД:**

- Либо сделать prerequisite для `AutoGenerate`: dirty-set всех компонентов, `lastChangedAt`, trailing debounce от последнего изменения, coalescing и гарантированный финальный rerun после busy/cancel.
- Либо снять обещание `≤1 с` и принять измеренный SLA на эталонной машине. Для showcase предпочтителен первый вариант.
- Отдельно проверить быстрые чередующиеся правки Forest/City/Path: финальный результат обязан соответствовать последнему состоянию каждого источника без пропущенного trailing rerun.

### [P1] Scene View-инструменты не имеют взаимного исключения

Сценарий оставляет Paint активным и сразу переходит к spline edit (`260711-2244...:105-108`). В живом коде:

- `PaintMaskPainter.Start` подписывает глобальный `SceneView.duringSceneGui`, а Paint остаётся активным до Stop/Esc (`ProjectPCG/.../PaintMaskPainter.cs:55-80`).
- Paint регистрирует default control, на любой ЛКМ назначает свой `GUIUtility.hotControl` и поглощает событие (`:92-121`).
- `PcgVolumeSceneEditor.Start` независимо подписывает ещё один глобальный Scene GUI-handler и не останавливает Paint (`ProjectPCG/.../PcgVolumeSceneEditor.cs:17-29`).
- Spline edit использует Unity spline handles, но ни зависимый ТДД, ни текущий renderer не объявляют общий owner Scene View-сессии.

В результате paint, volume handles и spline handles могут быть активны одновременно; порядок вызова callbacks определит, кто украдёт ЛКМ. Это ломает привычный Unity UX и делает сценарий ролика нестабильным.

**Что изменить в ТДД:**

- Ввести общий exclusive lease/session для PCG Scene View tools (или использовать совместимый `EditorTool`-контракт): запуск Paint/Volume/Spline завершает предыдущий PCG-инструмент.
- Обязательные stop conditions: повторная кнопка, Escape, смена сцены/host, destroyed executor, assembly reload.
- Минимум для сценария до общей инфраструктуры — явно нажать Stop Paint/Stop Editing между шагами, но для продуктового решения этого недостаточно.

### [P1] Граф `StonePath` не кладёт камни «по обочинам»

Текущий граф берёт точки на оси сплайна и применяет `Change Position` с `Mode = Add`, `Min/Max = ±1.8` по мировым XZ (`260711-2244...:65-68`). Живой executor реализует `Add` как `position + random.NextFloat3(min, max)` и не использует tangent/`PointData.Angle` (`ProjectPCG/.../ChangePositionNodeExecutor.cs:118-125`).

Это квадратный world-space jitter: камни окажутся на полотне, пересекут его и не будут формировать две обочины на поворотах.

В `PCG.Splines` уже есть подходящая нода `PointsOffsetSplinesNode`: sideways `Offset`, `Distance` и `BothSides = true` (`Packages/PCG.Splines/Scripts/CreatePoints/PointsOffsetSplinesNode.cs:23-50`), а executor смещает точки по `cross(tangent, up)` (`.../PointsOffsetSplinesNodeExecutor.cs:98-135`).

**Что изменить в ТДД:**

- Заменить `Spline Points By Distance → Change Position` на `Points Offset Splines`.
- Добавить параметр пресета `StoneOffset`/`ShoulderOffset` (например, дефолт `1.8`) либо явно связать offset с шириной полотна.
- Если нужен jitter, он должен быть узким и локальным относительно tangent/right, а не мировым XZ.
- В приёмке проверить на S-образном участке: камни находятся с обеих сторон и не пересекают ribbon с учётом радиуса prefab bounds.

### [P1] Performance-критерий не измерим и не покрывает главные пики

Фраза «FPS не проседает до фриза» (`260711-2244...:116`) не задаёт тестовую машину, размер результата, max main-thread stall, allocations или cancel latency.

Живые узкие места сценария:

- Forest стартует с `CandidateCount = 15000` (`260711-2244...:92`) и материализует деревья как отдельные prefab GameObjects. `GameObjectInstanceMaker` создаёт их на главном потоке и уступает управление только раз в 100 объектов (`ProjectPCG/.../GameObjectInstanceMaker.cs:24-58`).
- `RegionToMeshNodeExecutor` вызывает весь `RegionMeshBuilder.Build(...)` до первого `await scope.Step` (`Packages/PCG.Polygons/Editor/Scripts/Exec/RegionToMeshNodeExecutor.cs:46-59`), то есть один тяжёлый city-road build способен дать монолитный main-thread spike.
- Sweep снимает spline/terrain snapshots и создаёт `Mesh`/`MeshCollider` на главном потоке; повторное collider cooking тоже входит в пользовательскую задержку.
- Зависимый Sweep-ТДД снимает **всю** heightmap через `terrain.GetHeights(0, 0, res, res)` на каждый compute (`260711-2243-TDD-sweep_package.md:185-187`). Для resolution `4097` это около 64 MiB одного managed snapshot до геометрии и без cancellation point; даже для demo-terrain нужно зафиксировать heightmap resolution и измерить этот пик.
- Глобальный busy gate сериализует автогенерацию компонентов, поэтому wall time одного графа влияет на отзывчивость остальных.

**Что изменить в ТДД:**

Добавить benchmark matrix на фиксированной машине и в пустом Editor Profiler capture:

| Кейс | Фиксированные входы | Метрики |
|---|---|---|
| Forest stroke | mask 1024, 15k candidates, заданные tree prefabs | debounce-to-start, end-to-visible, max main-thread frame, GC alloc, итоговое число объектов |
| City drag | контур ~180×140, заданное число knots/blocks/houses | end-to-visible, `RegionMeshBuilder` stall, spawn/removal time, allocations |
| Path drag | 200+ м, фиксированный Step, stone distance 1.2, Collider on | frame sampling, mesh build, collider cooking, spawn/removal, cancel latency |
| Rapid edits | 10 быстрых изменений каждого инструмента | число реально запущенных/отменённых генераций, отсутствие stale/duplicate outputs, время до финального состояния |
| Clear/Generate loop | 10 циклов на каждом объекте | object/mesh leak, рост GC/native memory, orphan buckets |

Для каждой строки нужны численные thresholds. Уже заявленное `≤1 с` следует считать hard SLA только после исправления scheduler и фактического профилирования; иначе его нужно честно заменить измеренным бюджетом.

### [P2] `Seed` не гарантирует стабильный визуальный результат цепочки

В `StonePath` один и тот же вход проходит через `ChangePosition`, `ChangeScale`, `ChangeAngle`, затем `GameObjectWeights` (`260711-2244...:66-72`). Живые `ChangePositionNodeExecutor`, `ChangeScaleNodeExecutor` и `ChangeAngleNodeExecutor` запускают минимум четыре batch-task и сливают результаты через `lock (Results.Value) { AddRange(...) }`. Порядок batch completion не детерминирован, а следующая seed-зависимая нода потребляет уже этот порядок.

Следствие: при том же `Seed` после full recompute/cache clear либо при spline-edit камни могут менять scale/rotation/prefab не только из-за геометрии, но и из-за порядка завершения задач. Аналогичное ограничение уже явно признано в актуальном Forest-ТДД; editor-tools demo его не упоминает.

**Что изменить в ТДД:**

- Либо добавить prerequisite на indexed batch merge/stable source order для затронутых core-нód.
- Либо явно записать это как принятое ограничение и не использовать `Seed` как обещание воспроизводимости.
- Для флагманской демки предпочтителен deterministic gate: два full recompute после cache clear дают тот же отсортированный набор prefab/transform при неизменных входах.

### [P2] Happy path не проверяет критичный Unity lifecycle

Ручной сценарий (`260711-2244...:103-109`) не проверяет свойства, ради которых создавались зависимые editor tools.

Нужно добавить:

- Undo/Redo отдельного paint-stroke, spline drag/add/delete/Closed и volume move/resize/delete; каждый Undo должен приводить и данные, и generated scene к одному состоянию.
- Save → close scene → reopen: графы, inline variables, mask, volumes, splines и generated ownership не теряются.
- Domain reload с активной spline session и после завершённой сессии.
- Закрытие graph window во время активной Scene View-сессии; автогенерация после закрытия окна.
- Быстрое повторное редактирование во время генерации: старый run отменён, финальный run не потерян, stale objects отсутствуют.
- Удаление всех knots/обрыв input/profile: старый sweep mesh немедленно очищается и не помечается generated-current.
- `City`/`Path` под non-identity parent/host transform: storage, preview, handles и materialized output остаются в одном согласованном coordinate space.
- Generate/Clear при `AutoGenerate = true`, повторный Clear, удаление/disable `PcgComponent`, смена/удаление input asset.
- Bind обоих preset-assets после повторного импорта пакетов: нет missing scripts, missing dynamic ports или оборванных edges.
- Если сцена входит в build: build-preprocessor удаляет только authoring-компоненты/`IPcgTemp`, а запечённый результат и ссылки остаются валидными.

Минимальный автоматический слой — EditMode smoke-tests загрузки/bind пресетов и проверки типов outputs; Scene View/Undo/performance остаются Bridge/manual acceptance с сохранённым evidence.

### [P2] Для витринной демки контент и визуальная приёмка слишком неопределённы

`Docs/DESIGN_PRINCIPLES.md:9` определяет demo scenes как витрину продукта. Здесь материалы описаны только словами, камни выбираются как «два-три ... либо готовые, если есть подходящие» (`260711-2244...:81-84`), а camera/light/environment/кадр ролика не заданы.

Это позволяет функционально пройти граф, но получить серую, розовую или плохо читаемую сцену в HDRP; два исполнителя соберут разные demo artifacts.

**Что изменить в ТДД:**

- Зафиксировать точные prefab/material paths, shader family, масштабы bounds и отсутствие внешних package references.
- Описать Directional Light/environment, camera/bookmark и стартовый Scene View framing для каждого шага ролика.
- Добавить визуальные критерии: нет pink/missing materials, дороги читаются на terrain, дома не входят в дороги, камни не перекрывают path, лес не закрывает город, generated hierarchy не содержит orphan objects.
- Сохранить 3–4 эталонных screenshots: initial, painted forest, edited city, edited path/volume.

### [P2] Обновление документации ошибочно оставлено опциональным

ТДД добавляет новый пакет, preset-папки и demo-сцену, но после выполнения предлагает «уточнить у заказчика», обновлять ли карты/документацию (`260711-2244...:120-123`). Это конфликтует с `Docs/PROJECT_MAP.md:3-5`, где обновление карты при новой структуре обязательно.

**Что изменить в ТДД:**

Сделать частью Definition of Done:

- `Docs/PROJECT_MAP.md` — `PCG.Sweep`, presets и demo scene.
- `Docs/notes/city_pipeline.md` — canonical `CityBlocks` preset и его публичные параметры/outputs.
- `ProjectPCG/Docs/notes/preset_subgraphs_library.md` — `Forest`, `CityBlocks`, `StonePath`, зависимости и import path.
- `Documentation~` пакетов — node/preset usage и обязательные instance makers.

### [P3] Сценовые графы и outputs описаны неоднозначно

Формулировки `SplineNode → SubGraphNode → Result` (`260711-2244...:95,99`) не говорят, какие dynamic outputs подключены. У `CityBlocks` их два (`Roads`, `Houses`), у `StonePath` — `Path`, `Stones`. `ResultNode.Instances` поддерживает несколько связей, поэтому ТДД должен задавать wiring буквально.

**Что изменить в ТДД:**

- `CityBlocks.Roads → Result.Instances` и `CityBlocks.Houses → Result.Instances`.
- `StonePath.Path → Result.Instances` и `StonePath.Stones → Result.Instances`.
- В structural smoke-test проверить обе связи и оба типа materialized outputs.

## Рекомендуемая новая последовательность работ

1. Внести правки из `260713-1506-REVIEW-spline_tool.md` и `260713-1508-REVIEW-sweep_package.md`, затем фактически принять `Forest` (`2240`), persistent spline (`2242`) и `PCG.Sweep` (`2243`); синхронизировать DLL ядра.
2. Исправить package dependency contract для `PCG.Polygons`/`PCG.Sweep` и проверить isolated install.
3. Решить взаимное исключение Scene View tools.
4. Исправить scheduler/SLA `AutoGenerate` либо согласовать новый измеренный SLA.
5. Пересобрать `StonePath` через `Points Offset Splines`; определить позицию по determinism gate.
6. Создать и bind-test `CityBlocks.asset`/`StonePath.asset`.
7. Собрать сцену с явным maker inventory и буквальным wiring всех outputs.
8. Пройти persistence/Undo/cancel/closed-window acceptance.
9. Провести benchmark matrix и при необходимости уменьшить scene budgets без ослабления продуктовой демонстрации.
10. Зафиксировать visual evidence и обязательную документацию.

## Предлагаемый минимум критериев приёмки после ревизии

- Все dependency artifacts существуют и имеют принятые статусы; версия DLL ядра зафиксирована.
- Оба preset-assets bind без missing types/ports в чистом проекте с одними задекларированными package dependencies.
- У `City` и `Path` присутствуют и зарегистрированы `GameObjectInstanceMaker` и `MeshInstanceMaker`.
- Все четыре dynamic outputs (`Roads`, `Houses`, `Path`, `Stones`) явно подключены и материализуются.
- Запуск одного PCG Scene View tool завершает предыдущий; Escape всегда возвращает обычное управление Unity.
- Paint, spline и volume изменения проходят Undo/Redo, save/reopen и domain reload без потери данных.
- Пустой/невалидный sweep input очищает прежнюю геометрию; non-identity host transform не даёт двойного смещения/масштаба.
- Автогенерация coalesces rapid edits, не оставляет stale/duplicate objects и всегда доходит до последней ревизии inputs.
- Заявленный latency budget подтверждён benchmark evidence; отдельно ограничены max main-thread stall, allocations и cancel latency.
- Камни стоят на двух обочинах и не пересекают path ribbon.
- Fixed-seed determinism либо подтверждён full-recompute тестом, либо явно записан как известное ограничение.
- Generate/Clear повторяемы, не текут по объектам/mesh/native memory.
- Сцена визуально готова как showcase: фиксированные материалы, lighting/framing, нет missing/pink assets и грубых пересечений.
- `PROJECT_MAP`, pipeline/preset docs и package documentation обновлены до завершения ТДД.

## Итог

ТДД **решает правильную продуктовую задачу** и в целом **укладывается в философию PCG4U**: сильная демо-сцена собирается из самодостаточных нод и reusable-пресетов, а автор работает прямо в графе и Scene View.

Но сейчас документ преждевременно считает зависимости готовыми и не закрывает несколько реальных контрактов интеграции: materialization mesh-выхода, package dependencies, владение Scene View, scheduler latency и воспроизводимую приёмку производительности. Без этих правок демо рискует выглядеть как набор работающих по отдельности фич, которые мешают друг другу в одном Unity workflow.

Рекомендация: **не реализовывать `260711-2244` буквально; сначала внести P0/P1-правки в ТДД и зависимые контракты.** После этого архитектуру демо можно одобрять.
