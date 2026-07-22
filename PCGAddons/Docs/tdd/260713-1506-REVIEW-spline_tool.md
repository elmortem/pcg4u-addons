# Ревью ТДД SplineNode: персистентный сплайн с живым редактированием

Дата ревью: 2026-07-13  
Ревьюируемый документ: `Docs/tdd/260711-2242-TDD-spline_tool.md`  
Статус ревью: требуется доработка ТДД до начала реализации

## Итоговый вердикт

Направление выбрано правильно. Хранить авторский контент непосредственно в `SplineNode`, использовать штатный сериализуемый `UnityEngine.Splines.Spline`, редактировать его через временный `SplineContainer` и инвалидировать граф через `OnParametersChanged()` — это соответствует node-centric архитектуре PCG4U и не создаёт параллельного вычислительного контура.

Однако текущая ревизия C ещё не задаёт безопасный production-контракт. В ней есть шесть блокирующих проблем:

- временный объект не удаляется из player build целиком;
- обещанный единый Undo-шаг фактически не содержит исходного состояния ноды;
- world-space контракт противоречит сохранённому поведению `DrawPreview` и даёт двойную трансформацию;
- идентичности `GraphId + NodeId` недостаточно для nested-графов, а `Graph.Host` не всегда является владельцем изменяемых данных;
- round-trip через `SplineContainer` теряет часть штатного состояния Unity Splines и не отслеживает операции со списком сплайнов;
- установленная в PCGAddons release-DLL ядра ещё не содержит обязательный `AutoGenerate`/`PcgGraphRunner` контракт.

Поэтому ТДД решает исходную проблему только частично: персистентность базовой геометрии и live-sync спроектированы в правильную сторону, но Undo, координаты, lifecycle, build cleanliness и полная повторная редактируемость пока не гарантированы. Реализацию следует начинать после закрытия замечаний P1.

## Что в ТДД уже хорошо

- Владение данными перенесено в ноду, а прежняя идея постоянного компонента-тулзы отменена. Это прямо соответствует `Docs/DESIGN_PRINCIPLES.md:15-34`.
- Выбран штатный `[Serializable]`-тип `Spline`, без собственного параллельного формата. Наличие сериализуемых knots, tangent metadata, `Closed` и embedded spline data подтверждается `Library/PackageCache/com.unity.splines@2.8.2/Runtime/Spline.cs:14-15,87-108`.
- Временный `SplineContainer` используется только как Unity-native поверхность редактирования; постоянным источником правды должна оставаться нода.
- Live-sync коалесцируется, а не выполняется на каждый `Spline.Changed`. Сам Unity-пакет предупреждает, что это событие может приходить много раз за кадр (`Spline.cs:254-277`), поэтому троттлинг нужен.
- Инвалидация направлена в штатный `OnParametersChanged()`, а не в локальную очередь вычислений. Актуальное ядро уже имеет trailing resolve: `PcgComputeSystem.RunResolveAsync` повторяет расчёт, пока `EffectiveVersion` не совпадёт с `LastComputedVersion` (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Exec/PcgComputeSystem.cs:120-184`).
- Явный version salt необходим: generic `PcgNodeDescriptor.HashValue` для `List<Spline>` доходит только до `Spline.GetHashCode()`, а не до приватного сериализованного содержимого (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Exec/PcgNodeDescriptor.cs:16-64`). Само решение считать содержимое явно верное.

## Сводка замечаний

| Приоритет | Проблема | Последствие |
|---|---|---|
| P1 | `IPcgTemp` удаляет только marker-компонент | В build остаются `Spline Edit` GameObject и `SplineContainer` |
| P1 | Undo регистрируется после серии live-writeback | Stop Edit создаёт пустой/неверный Undo, а lifecycle временного объекта добавляет отдельные шаги |
| P1 | World-space данные совмещены с host-transform в `DrawPreview` | При non-identity `PcgComponent` сплайн смещается/поворачивается/масштабируется дважды |
| P1 | Сессия адресуется только `GraphId + NodeId`, мутация идёт через `Graph.Host` | Коллизии nested-графов, запись Undo/dirty не в тот asset, потеря сессии при Sync/Undo |
| P1 | Копирование `Spline` неполное, список контейнера синхронизируется не полностью | Теряются tension, embedded data, knot links; add/remove/reorder могут не попасть в ноду; появляется лишний пустой spline |
| P1 | Release-DLL в PCGAddons не содержит AutoGenerate-зависимость | Интеграционный критерий `SplineNode → SubGraph → Result + AutoGenerate` в текущем checkout невыполним |
| P2 | Lifecycle описан только для domain reload и Stop Edit | Сироты остаются при удалении ноды/host, закрытии сцены, входе в Play Mode и повторном bind |
| P2 | Перформанс ограничен числом `0.25 с`, но без бюджета и профилирования | Полная аллокационная копия и полный хеш могут фризить SceneView на больших сплайнах |
| P2 | Критерии приёмки не покрывают важные Unity-сценарии | Регрессии проявятся только в реальном authoring flow |
| P2 | Обновление карты и package documentation оставлено на последующий вопрос | Новая subsystem `Tools/` и новый lifecycle останутся недокументированными |

## Блокирующие замечания

### P1. В build остаётся временный GameObject со `SplineContainer`

ТДД считает достаточным, что `PcgSplineEditContainer : IPcgTemp`, и обещает, что marker «вырезается из билда» (`260711-2242-TDD-spline_tool.md:74-85,158`). Фактический `PcgBuildPreprocessor` вызывает `DestroyImmediate(comp)` только для компонентов, реализующих `IPcgTemp`; GameObject он не удаляет (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Builds/PcgBuildPreprocessor.cs:20-49`).

После такой обработки в player scene останутся:

- GameObject `Spline Edit`;
- `Transform`;
- `SplineContainer` со всеми сериализованными сплайнами.

Это нарушает и критерий приёмки, и принцип «ничего лишнего в сценах и билде» из `DESIGN_PRINCIPLES.md:7-13`.

Что требуется зафиксировать в ТДД:

- механизм удаления всего временного root-объекта, а не только marker-компонента;
- владелец изменения: предпочтительно явный core-контракт для temporary roots, а не неявная надежда на текущую семантику `IPcgTemp`;
- cleanup при входе в Play Mode, а не только при player build;
- критерий проверки build scene: нет ни `PcgSplineEditContainer`, ни `SplineContainer`, ни GameObject `Spline Edit`.

Следует также разделить два понятия: runtime-тип `PcgSplineEditContainer` всё равно компилируется в package assembly; build preprocessing может удалить scene instance, но не «вырезать тип из сборки».

### P1. Undo-контракт не работает с live-writeback

По ТДД watcher каждые 0.25 с вызывает `WriteBack(..., withUndo: false)` и непосредственно заменяет `Data.Splines` (`260711-2242-TDD-spline_tool.md:131-139`). На Stop Edit выполняется `WriteBack(..., withUndo: true)`, где `Undo.RegisterCompleteObjectUndo` вызывается перед ещё одной записью уже синхронизированного состояния (`:67-70,107-109`).

К этому моменту исходное состояние host уже многократно затёрто. Снимок Stop Edit поэтому фиксирует почти то же состояние, которое записывается следом, и не способен вернуть ноду к состоянию до Start Edit.

Дополнительно:

- `Undo.RegisterCreatedObjectUndo` на Start Edit создаёт отдельный пользовательский Undo-шаг;
- `Undo.DestroyObjectImmediate` на Stop Edit создаёт ещё один шаг и допускает воскрешение временного объекта;
- `SplineContainer` ведёт собственную историю Unity Splines, поэтому без явной группировки порядок Ctrl+Z зависит от смеси host- и container-записей;
- `Undo.undoRedoPerformed` в окне графа вызывает `RootGraph.Sync()` и `RebuildContext()` (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Fast/PcgGraphEditorWindow.cs:503-515`), поэтому executor текущей сессии после Undo нельзя считать неизменным.

ТДД должен задать транзакционную модель, а не только место вызова `RegisterCompleteObjectUndo`:

- исходный сериализованный snapshot ноды фиксируется до первого live-writeback;
- live-preview может обновлять `Data`, но Stop Edit обязан создать один содержательный Undo, возвращающий именно исходный snapshot;
- временный GameObject не должен засорять пользовательскую Undo-историю после завершения сессии;
- Ctrl+Z во время активной сессии отменяет штатное действие Unity Splines и затем синхронизирует получившийся container-state в актуальный executor;
- Ctrl+Z после Stop Edit одним шагом возвращает ноду к состоянию до Start Edit и не воскрешает `Spline Edit`.

Конкретная реализация может использовать session snapshot и контролируемую Undo-group, но перечисленные наблюдаемые инварианты обязательны.

### P1. World-space контракт противоречит текущему preview/edit flow

ТДД утверждает, что `Data.Splines` хранится в мировых координатах, а edit-container создаётся в origin с identity transform (`260711-2242-TDD-spline_tool.md:46,69-71`). Одновременно документ говорит сохранить `SetEditContainer`/`DrawPreview` (`:72`).

Текущий `SplineNodeExecutor.DrawPreview` при активной сессии каждый вызов копирует в edit-container position/rotation/scale host-компонента (`Packages/PCG.Splines/Editor/Scripts/Exec/SplineNodeExecutor.cs:39-46`). Без активной сессии `SplinesGizmoUtility` устанавливает `Gizmos.matrix = transform.localToWorldMatrix` (`Packages/PCG.Splines/Scripts/Utilities/SplinesGizmoUtility.cs:10-31`).

Итог при non-identity `PcgComponent`:

- мировые knots, положенные в identity-container, затем ещё раз трансформируются host-трансформом;
- обычное gizmo-preview также ещё раз применяет host matrix к уже мировым координатам;
- следующий `WriteBack` повторно переводит эти координаты «в мир».

Нужно выбрать и провести один контракт через storage, edit-container, preview и downstream nodes. С учётом уже заявленного package-контракта правильнее оставить world space и тогда:

- edit-container остаётся в identity и не синхронизируется с host transform;
- preview мировых сплайнов не применяет host matrix повторно;
- преобразование knot выполняется scale-aware API пакета, например `BezierKnot.Transform(float4x4)`, который корректно трансформирует позицию, rotation и масштабируемые tangents (`BezierKnot.cs:73-89`), а не парой `TransformPoint`/`TransformDirection`;
- поведение whole-container move/rotate/scale описывается явно, включая non-uniform scale.

Обязательный acceptance case: один и тот же сплайн при host transform identity и при host `(position, rotation, non-uniform scale)` визуально и численно остаётся в тех же мировых координатах до редактирования, во время него, после Stop, после domain reload и после cache clear.

### P1. `GraphId + NodeId` не является полной идентичностью сессии

Marker хранит только `GraphId` и `NodeId`, а реестр привязан к ссылке на `SplineNodeExecutor` (`260711-2242-TDD-spline_tool.md:78-97`). В ядре полная идентичность ноды — `PcgNodeAddress`: nested-граф дописывает в path id каждого `SubGraphNode` и самой ноды (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Scripts/Graph/PcgNodeAddress.cs:5-40`, `ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Exec/PcgExecGraph.cs:47-53,90-92`).

Одинаковые локальные `NodeId` допустимы:

- в корневом графе и во вложенном subgraph;
- в разных subgraph assets;
- в нескольких инстансах одного subgraph внутри одного host.

Есть и вторая проблема. Для nested-графа `SubGraphNodeExecutor` передаёт в `Inner.Host` внешний host-компонент (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Exec/Nodes/SubGraphNodeExecutor.cs:146-150`), тогда как сериализуемым владельцем текущего `Graph.Data` является `OwnerExecutor.Data.SubGraph`. Само окно графа поэтому использует отдельный `SerializationHost` (`ProjectPCG/Assets/Plugins/PCG4U/PCG/Editors/Fast/PcgGraphEditorWindow.cs:31-41`). `Undo.RegisterCompleteObjectUndo(Graph.Host)` и `EditorUtility.SetDirty(Graph.Host)` в nested-контексте пометят компонент, но не изменяемый `PcgSubGraph` asset.

Требуемый контракт:

- стабильный ключ сессии содержит как минимум `GraphId + executor.Address.ToKey()`, а не `Node.Id`;
- отдельно определяется сериализованный owner: текущий `PcgSubGraph` для nested-графа, иначе root host;
- реестр индексируется стабильным ключом, а executor является заменяемой привязкой;
- после `Sync`, Undo/Redo, reopen окна и domain reload сессия либо перепривязывается к актуальному executor, либо безопасно завершается с flush/cleanup;
- duplicate orphan containers для одного ключа не выбираются случайно: ТДД задаёт детерминированную диагностику и cleanup.

### P1. Round-trip Unity `SplineContainer ↔ SplineNode` неполон

#### Лишний пустой spline

Новый `SplineContainer` уже содержит один пустой `Spline` (`SplineContainer.cs:21-27`). Предложенный `PopulateContainer` вызывает `container.AddSpline(copy)` для каждого сохранённого сплайна (`260711-2242-TDD-spline_tool.md:71`), то есть дописывает копии после дефолтного пустого элемента. Повторное редактирование одного сплайна даст как минимум два элемента, а следующий WriteBack сохранит лишний пустой spline в ноду.

`PopulateContainer` должен атомарно заменить `container.Splines` полным списком копий, а поведение пустой ноды должно быть задано отдельно.

#### Теряется штатное состояние `Spline`

Предложенная копия переносит только `Closed`, `BezierKnot` и `TangentMode` (`:66,69,71`). В Unity Splines 2.8.2 у spline также сериализуются:

- per-knot AutoSmooth tension (`Spline.GetAutoSmoothTension`, `Spline.cs:512-575`);
- embedded `int`, `float`, `float4` и `Object` spline data (`Spline.cs:98-118,131-241`).

Кроме того, cross-spline knot links принадлежат `SplineContainer.KnotLinkCollection`, а не отдельным `Spline` (`SplineContainer.cs:131-143`; `KnotLinkCollection.cs:7-34`). Хранение только `List<Spline>` не переживёт Stop → Start Edit для связанных knots.

Это реальная потеря authoring data, несовместимая с заявлением «храним штатный Unity-тип напрямую». ТДД должен либо сохранить все штатные данные прямыми Unity-типами, включая `KnotLinkCollection`, либо явно и обоснованно сузить поддерживаемый Unity Splines contract. Для Asset Store-качества предпочтителен полный round-trip.

`GetVersionSalt()` также обязан учитывать всё состояние, способное изменить выход. Минимум к текущему списку добавляется AutoSmooth tension; если embedded data остаётся частью выходного `Spline`, для неё нужен детерминированный version contract.

#### Не отслеживается изменение списка сплайнов

Watcher подписывается только на `Spline.Changed` (`260711-2242-TDD-spline_tool.md:116-137`). Добавление, удаление и reorder сплайнов имеют отдельные события `SplineContainer.SplineAdded`, `SplineRemoved`, `SplineReordered` (`SplineContainer.cs:35-58,108-115`). Без них multiple-spline acceptance не гарантирован.

Нужно подписаться на container-level события и маркировать только сессию соответствующего container dirty. Критерии должны отдельно проверить add/remove/reorder, а не только редактирование knots и `Closed`.

### P1. Интеграционная зависимость AutoGenerate отсутствует в установленной DLL

ТДД честно объявляет зависимость от `260711-2239-TDD-auto_generate.md` (`260711-2242-TDD-spline_tool.md:7-9,156`). В исходниках `ProjectPCG` этот ТДД уже имеет статус `Выполнено`, и там присутствуют `PcgGraphRunner`/`PcgAutoGenerateWatcher`.

Но release assemblies текущего PCGAddons checkout датированы 2026-07-11 15:40. Проверка metadata через Mono.Cecil показала:

- в `PCG.PcgComponent` нет поля `AutoGenerate`;
- в `PCG.Editors.dll` нет типов `PCG.Exec.PcgGraphRunner` и `PCG.Editors.PcgAutoGenerateWatcher`.

Следовательно, standalone-персистентность `SplineNode` можно реализовать отдельно, но интеграционный критерий с AutoGenerate нельзя принимать на текущих DLL.

В ТДД нужен явный precondition/handoff:

- до интеграционной проверки опубликовать и положить в `Assets/Plugins/PCG4U/PCG/` актуальные `PCG.dll` и `PCG.Editors.dll`;
- проверить наличие требуемых API до начала demo acceptance;
- зафиксировать минимально совместимую версию/ревизию ядра для `PCG.Splines`.

## Важные замечания

### P2. Lifecycle сессии неполон

Сейчас описаны Start, Stop, domain reload и ленивое удаление destroyed references. Не определено поведение при:

- удалении `SplineNode` во время редактирования;
- удалении или выгрузке host-компонента;
- закрытии graph window;
- закрытии/выгрузке сцены;
- входе в Play Mode;
- смене nested context;
- повторном Start Edit при уже активной сессии;
- ручном удалении `Spline Edit` GameObject;
- обнаружении двух orphan containers с одним session key.

Для каждого terminal event требуется политика `flush`, `discard` или `diagnostic + cleanup`. Нельзя оставлять static registry, stale executor и scene object расходиться молча.

Renderer не должен считать своё приватное поле источником истины. Состояние кнопки Start/Stop должно вычисляться по стабильной session identity, иначе пересоздание layout/renderer или rebind окна вернёт ложную кнопку Start при живом container.

### P2. Перформанс описан качественно, но не измеримо

Троттлинг 0.25 с ограничивает частоту, но каждый tick всё равно выполняет:

- полный обход всех knots для membership/content hash;
- создание новых `Spline` и внутренних списков;
- замену полного `Data.Splines`;
- `SetDirty` и `OnParametersChanged`;
- потенциальный downstream preview resolve.

Стоимость одного sync остаётся `O(total knots)` с заметными GC allocations. AutoGenerate debounce защищает материализацию результата, но не обязательно тяжёлые открытые preview nodes.

В revised ТДД нужен benchmark matrix, например:

| Нагрузка | Что измерять | Предлагаемый порог приёмки |
|---|---|---|
| 1 spline / 32 knots | idle и drag | idle: 0 B/frame; не более 4 sync/с |
| 8 splines / 2 048 knots | 10 с непрерывного drag | p95 main-thread sync ≤ 5 мс; без generation до стабилизации |
| 32 splines / 10 000 knots | drag, Stop, Undo, reload flush | p95 sync ≤ 16 мс; ни одного stall > 50 мс |
| Любая активная сессия | последняя правка перед Stop/reload | потеря данных: 0; latency до node-state ≤ 0.5 с |

Дополнительно следует профилировать GC Alloc на sync и не оставлять постоянный `EditorApplication.update`-обход/аллокации при отсутствии активных сессий. Если полное клонирование не проходит бюджет, ТДД должен выбрать более дешёвый snapshot/content-version путь, не ослабляя Undo и fidelity.

### P2. Не хватает Unity acceptance matrix

Текущие критерии проверяют happy path. До статуса `Готов` нужно добавить:

- root `PcgComponent` с identity и non-identity transform;
- `SplineNode` внутри `PcgSubGraph`, открытого напрямую и в контексте компонента;
- два host-компонента с одинаковым subgraph asset и совпадающими локальными node ids;
- пустой spline, 1 spline и несколько splines;
- add/remove/reorder splines, open/Closed, все tangent modes, AutoSmooth tension;
- linked knots и embedded spline data через Stop → Start Edit → save/reopen;
- Ctrl+Z/Ctrl+Y во время активной сессии и после Stop;
- domain reload в пределах последних 0.25 с после правки;
- удаление ноды, host и edit-container;
- вход в Play Mode и возврат в Edit Mode;
- save/reopen scene и asset, очистка value-cache;
- player build inspection на отсутствие всего temporary root;
- AutoGenerate с открытым и закрытым graph window после обновления DLL;
- continuous drag: generation не молотит, после стабилизации строится только последнее состояние.

### P2. Документация должна входить в scope

ТДД создаёт новые `Scripts/Tools` и `Editor/Scripts/Tools`, новый marker, session registry и editor lifecycle. `CLAUDE.md` и `Docs/PROJECT_MAP.md:5` требуют обновлять карту при появлении новых папок/подсистем.

Поэтому пункт «уточнить, нужно ли обновить документацию» недостаточен. В scope реализации нужно сразу включить:

- `Docs/PROJECT_MAP.md` — новые типы и поток `node ↔ temporary container`;
- `Packages/PCG.Splines/Documentation~/PCG.Splines/Splines/Spline-Node.md` — storage, coordinate contract, Start/Stop, Undo, domain reload, multiple splines;
- package-level compatibility note о минимальной версии ядра с AutoGenerate, если этот flow рекламируется как часть продукта.

## Рекомендуемая корректировка архитектуры

Ниже не готовая реализация, а минимальный набор контрактов, который revised ТДД должен закрепить.

1. `SplineNode` остаётся владельцем `List<Spline>`, но дополнительно сохраняет штатное authoring-state, которое живёт на container level (`KnotLinkCollection`), без собственного дублирующего формата.
2. Вводится единый helper полного clone/transform/hash, который сохраняет knots, tangent modes, AutoSmooth tension, embedded data и использует `BezierKnot.Transform` для матриц.
3. Полная identity сессии — `GraphId + PcgNodeAddress`, отдельно хранится сериализованный owner.
4. Реестр по стабильному ключу умеет rebind executor после `Sync`/Undo/domain reload; renderer только отображает состояние реестра.
5. Сессия хранит исходный snapshot для корректного финального Undo и актуальный container-state для live preview.
6. Watcher слушает knot- и container-level изменения, коалесцирует их, не аллоцирует в idle и инвалидирует только актуальный executor.
7. Temporary root имеет явный lifecycle: Stop, node/host removal, scene close, Play Mode и build удаляют весь GameObject; domain reload допускает контролируемый orphan/rebind.
8. World-space контракт проверяется на non-identity host; host transform не применяется повторно.
9. Интеграционный AutoGenerate acceptance запускается только после обновления release-DLL ядра.

## Минимальные критерии готовности revised ТДД

ТДД можно переводить из `Не готов` в `Готов к реализации`, когда в нём одновременно будут закрыты следующие пункты:

- определён полный сериализованный round-trip Unity Spline state;
- устранены default-empty spline и пропущенные container events;
- задан один непротиворечивый coordinate contract;
- описана stable session identity и правильный serialized owner для nested-графов;
- описана наблюдаемая Undo/Redo-семантика до, во время и после сессии;
- весь temporary root гарантированно удаляется при build/Play/lifecycle cleanup;
- добавлены failure paths и duplicate-orphan policy;
- добавлены измеримые performance budgets;
- добавлена полная Unity acceptance matrix;
- зафиксирован и установлен совместимый core build с AutoGenerate;
- обновление `PROJECT_MAP.md` и package documentation включено в scope.

## Итог

Идея тулы сохранена: это должна быть самостоятельная нода с Unity-native SceneView-редактированием, без постоянного компонента и без второго вычислительного движка. Но текущая ревизия C пока ломает несколько базовых ожиданий Unity — Undo, корректность transform, повторное редактирование полного spline-state и чистоту build.

Вердикт: **доработать ТДД; реализацию по текущему тексту не начинать**.
