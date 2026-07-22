# ТДД: SplineNode — персистентный сплайн с живым редактированием (PCG.Splines)

Status: Не готов

Ревизия D — по ревью `260713-1506-REVIEW-spline_tool.md`: полный round-trip состояния Unity Splines, транзакционный Undo, единый world-space контракт, идентичность сессии по `Address`, lifecycle и чистота билда. Блокер ревью про DLL снят: release-DLL ядра от 2026-07-13 уже содержат `AutoGenerate`/`PcgGraphRunner`/`PcgAutoGenerateWatcher`.

Ревизия C: без собственного формата хранения — `UnityEngine.Splines.Spline` сериализуем, нода хранит его напрямую. Ревизия B: инструмент — сама `SplineNode`, компонент-тулза отменён; сценарий «нарисовал — граф применился» собирается штатно: `SplineNode` → `SubGraphNode` → `Result` + `AutoGenerate`.

Зависимости:

- Ядро с `260711-2239-TDD-auto_generate.md` — установлено (DLL 2026-07-13); минимально совместимая ревизия ядра фиксируется в этом документе при сдаче.
- Правка ядра (ProjectPCG, до приёмки этого ТДД): `PcgBuildPreprocessor` удаляет **весь GameObject** временного root-объекта, помеченного новым интерфейсом `IPcgTempRoot : IPcgTemp` (сейчас удаляются только компоненты — в билд утекают `Spline Edit` GameObject, `Transform` и `SplineContainer`). Обновление DLL.

Проблемы текущей `SplineNode`, которые закрывает ТДД: сплайн живёт только в памяти executor'а; правки видны графу только по Stop Edit; Start Edit создаёт пустой контейнер; контейнер-сирота после domain reload не подхватывается.

---

## Состав

Рантайм (`Packages/PCG.Splines/Scripts/`):

- `Splines/SplineNode.cs` — правка: сериализованное хранилище.
- `Tools/PcgSplineEditContainer.cs` — маркер edit-контейнера (`IPcgTempRoot`).

Редактор (`Packages/PCG.Splines/Editor/Scripts/`):

- `Exec/SplineNodeExecutor.cs` — правка: выход из данных ноды, запись правок, версия по содержимому.
- `Exec/SplineNodeRenderer.cs` — правка: Start/Stop Edit, подхват сироты, состояние кнопки из реестра.
- `Tools/SplineEditSessions.cs` — реестр сессий.
- `Tools/SplineNodeEditWatcher.cs` — живой синк.

---

## SplineNode

К существующему выходу добавляется сериализованное хранилище — данные принадлежат графу, переживают domain reload и очистку value-cache:

```csharp
[HideInNode]
[PcgMemberInfo("Splines authored with the scene edit tool.", Tags = new[] { "splines", "storage" })]
public List<Spline> Splines = new();
[HideInNode]
[PcgMemberInfo("Knot links between stored splines.", Tags = new[] { "splines", "storage" })]
public KnotLinkCollection Links = new();
```

Поля — параметры (не `[Input]`), редактируются инструментом. Позиции узлов — в мировых координатах (контракт пакета). `Links` — штатный сериализуемый тип Unity Splines: cross-spline связи узлов живут на контейнере, без него Stop → Start Edit теряет линки.

## Полный round-trip состояния

Копирование `Spline` — единый helper (`SplineCopyUtility`, `Editor/Scripts/Tools/`):

- база — копирующий конструктор `new Spline(source)` (переносит узлы, `Closed` и embedded `SplineData`);
- поверх — явный перенос per-knot `TangentMode` (`GetTangentMode`/`SetTangentMode`) и AutoSmooth tension (`GetAutoSmoothTension`/`SetAutoSmoothTension`): копирующий конструктор Unity Splines 2.8.2 их **не** копирует;
- трансформация узлов — `BezierKnot.Transform(float4x4)` (scale-aware, корректно для non-uniform scale), не пара `TransformPoint`/`TransformDirection`.

## SplineNodeExecutor

- `DoCompute()` — `Results.Value` собирается из `Data.Splines` (по ссылке, ноды не мутируют входы).
- `GetVersionSalt()` — явная свёртка содержимого: по каждому сплайну `Count`, `Closed`, per-knot `Position/TangentIn/TangentOut/Rotation`, `(int)GetTangentMode(i)`, `GetAutoSmoothTension(i)`; плюс хеш `Links` (число линков + индексы); плюс счётчики embedded `SplineData` по ключам. `(hash * 397) ^ x` в `unchecked`. Reflection-хеш `ParamVersion` приватные поля `Spline` не видит — версия по содержимому обязательна.
- `public Object SerializationOwner` — сериализованный владелец изменяемых данных: для ноды во вложенном графе — `PcgSubGraph` asset текущего контекста (`OwnerExecutor.Data.SubGraph`, как `SerializationHost` окна графа), иначе `Graph.Host as Object`. `Undo.RegisterCompleteObjectUndo` и `EditorUtility.SetDirty` — только на owner: в nested-контексте пометка host-компонента не сохраняет изменяемый asset.
- `public void WriteBack(SplineContainer container, bool withUndo)` — единственная точка мутации:
	- `withUndo` → `Undo.RegisterCompleteObjectUndo(SerializationOwner, "Edit Spline Node")`;
	- `Data.Splines` пересобирается копиями из `container.Splines` c запеканием `container.transform.localToWorldMatrix` в мировые узлы (`SplineCopyUtility`); пустые сплайны (`Count == 0`) отбрасываются;
	- `Data.Links` — копия `container.KnotLinkCollection`;
	- `EditorUtility.SetDirty(SerializationOwner); OnParametersChanged();`
- `public void PopulateContainer(SplineContainer container)` — атомарная замена: `container.Splines = список копий` (свойством целиком — новый контейнер уже содержит один дефолтный пустой сплайн, `AddSpline` поверх него давал бы мусорный элемент); восстановление линков из `Data.Links`. Пустая нода даёт контейнер с одним пустым сплайном (дефолт Unity) — обратный `WriteBack` его отбросит.
- Прежний `SetData(IReadOnlyList<Spline>)` удаляется. `SetEditContainer` сохраняется; `DrawPreview` — см. «Координатный контракт».

## Координатный контракт

Один контракт по всей цепочке — world space:

- Edit-контейнер создаётся в origin с identity и **не** синхронизируется с host-трансформом: текущее копирование position/rotation/scale host'а в `DrawPreview` удаляется — при non-identity `PcgComponent` мировые узлы трансформировались бы дважды.
- Превью мировых сплайнов рисуется с identity-матрицей: в `SplinesGizmoUtility` добавляется мировая перегрузка `DrawGizmos(IReadOnlyList<Spline> splines)` (без `transform.localToWorldMatrix`); `SplineNodeExecutor.DrawPreview` использует её.
- Перенос/поворот/масштаб контейнера пользователем — валидная правка: watcher ловит изменение матрицы и `WriteBack` запекает её в мировые узлы (включая non-uniform scale через `BezierKnot.Transform`).

Приёмка: один и тот же сплайн при identity и non-identity host (position + rotation + non-uniform scale) численно в тех же мировых координатах до редактирования, во время, после Stop, после domain reload и очистки кеша.

## Undo-контракт

Наблюдаемые инварианты:

- Ctrl+Z во время сессии отменяет штатное действие Unity Splines на контейнере; watcher синхронизирует получившееся состояние в ноду.
- Ctrl+Z после Stop Edit одним шагом возвращает ноду к состоянию **до Start Edit** и не воскрешает `Spline Edit`.
- Временный объект не оставляет пользовательских шагов в Undo-истории.

Реализация:

- Start Edit: сессия сохраняет snapshot — глубокую копию `Data.Splines` + `Data.Links` (`SplineCopyUtility`); контейнер создаётся **без** `Undo.RegisterCreatedObjectUndo`.
- Live-синк: `WriteBack(withUndo: false)` — без Undo-записей (источник правды на время сессии — контейнер, он уже под собственным Undo Unity Splines).
- Stop Edit: `Data ← snapshot` → `Undo.RegisterCompleteObjectUndo(SerializationOwner)` → `Data ← финальное состояние контейнера` → `SetDirty` → `OnParametersChanged()`. Один содержательный шаг, откатывающий к состоянию до Start Edit.
- Контейнер уничтожается `Object.DestroyImmediate` (вне Undo).
- `Undo.undoRedoPerformed` в окне графа вызывает `Sync`/`RebuildContext` — executor сессии после Undo перепривязывается реестром (см. ниже), snapshot живёт в сессии, не в executor'е.

## PcgSplineEditContainer

`Scripts/Tools/PcgSplineEditContainer.cs`, namespace `PCG.Splines.Tools`. Маркер, привязывающий контейнер к ноде; переживает domain reload, позволяет подхватить сироту; `IPcgTempRoot` — при сборке плеера и входе в Play Mode удаляется весь GameObject.

```csharp
public sealed class PcgSplineEditContainer : MonoBehaviour, IPcgTempRoot
{
	public SplineContainer Container;
	public string GraphId;
	public string AddressKey;
}
```

`AddressKey = executor.Address.ToKey()` — полная идентичность ноды с учётом пути вложенных `SubGraphNode`: локальный `NodeId` не уникален между корнем, вложенными графами и инстансами одного сабграфа.

## SplineEditSessions

`Editor/Scripts/Tools/SplineEditSessions.cs`, статический реестр. Сессия индексируется стабильным ключом `(GraphId, AddressKey)`; executor — заменяемая привязка.

- `Begin(executor, container)` / `End(key)` / `Find(key)` / `Active`.
- Сессия хранит: ключ, marker/container, snapshot (для Undo-контракта), привязанный executor.
- `Rebind(key, executor)` — после `Sync`, Undo/Redo, reopen окна, domain reload сессия перепривязывается к актуальному executor'у по ключу; если нода с таким адресом больше не существует — flush последнего состояния невозможен, сессия завершается: контейнер удаляется, warning.
- `FindOrphan(graphId, addressKey)` — поиск сироты `Object.FindObjectsByType<PcgSplineEditContainer>`. Несколько сирот одного ключа — детерминированный выбор (минимальный `GetInstanceID()`), остальные удаляются с warning.

Lifecycle-политики (terminal events):

| Событие | Политика |
|---|---|
| Stop Edit | flush (Undo-шаг) + удаление контейнера |
| Удаление ноды / host | discard + удаление контейнера + warning |
| Закрытие/выгрузка сцены | flush без Undo + удаление контейнера |
| Вход в Play Mode | flush без Undo + End всех сессий + удаление контейнеров |
| Domain reload | `FlushAll` до перезагрузки; после — контейнер живёт сиротой, Start Edit подхватывает |
| Закрытие окна графа | сессия живёт; состояние кнопки восстанавливается по реестру |
| Повторный Start Edit при активной сессии | no-op + `Selection` на контейнер |
| Ручное удаление контейнера | End сессии; данные ноды актуальны на последний синк |

## SplineNodeRenderer

- `StartEdit`: `SplineEditSessions.FindOrphan(...)` → подхват; иначе новый `GameObject("Spline Edit")` в origin + `SplineContainer` + маркер (без Undo-регистрации), `executor.PopulateContainer(container)`; `SetEditContainer`; `Begin`; `Selection.activeGameObject = go;`
- `StopEdit`: транзакция из Undo-контракта; `End`; `SetEditContainer(null)`.
- Состояние кнопки Start/Stop вычисляется по ключу сессии из реестра, не по приватному полю renderer'а: пересоздание layout/rebind окна не даёт ложную кнопку Start при живом контейнере.

## SplineNodeEditWatcher

`Editor/Scripts/Tools/SplineNodeEditWatcher.cs`, статический класс, `[InitializeOnLoadMethod]`.

Подписки: `Spline.Changed`, `SplineContainer.SplineAdded`/`SplineRemoved`/`SplineReordered` (static-события — add/remove/reorder сплайнов иначе не ловятся), `Undo.undoRedoPerformed`, `AssemblyReloadEvents.beforeAssemblyReload`, `EditorApplication.playModeStateChanged`, `EditorApplication.update`.

- События помечают dirty только сессию соответствующего контейнера/сплайна.
- `OnUpdate` — троттлинг 0.25 с: для каждой активной сессии хеш `container.transform.localToWorldMatrix` и хеш `KnotLinkCollection` (линки не имеют своего события — дешёвый поллинг на тике); изменение → dirty. Dirty-сессии — `WriteBack(withUndo: false)`.
- При отсутствии активных сессий тик выходит немедленно и не аллоцирует.
- `FlushAll()` — перед assembly reload и входом в Play Mode.

Инвалидация превью и автогенерация — через `OnParametersChanged`/`GetVersionSalt`, отдельный вотчер-инвалидатор не нужен.

---

## Порядок реализации

- Правка ядра `IPcgTempRoot` + `PcgBuildPreprocessor` (ProjectPCG) + обновление DLL.
- Поля `Splines`/`Links` на `SplineNode`; `SplineCopyUtility`.
- `SplineNodeExecutor` (`DoCompute`/`GetVersionSalt`/`WriteBack`/`PopulateContainer`/`SerializationOwner`).
- `PcgSplineEditContainer`, `SplineEditSessions`.
- `SplineNodeRenderer`, `SplineNodeEditWatcher`; мировая перегрузка `SplinesGizmoUtility`.

## Критерии приёмки

- Нарисованный сплайн сериализуется с графом штатной Unity-сериализацией; сохранение сцены/ассета, domain reload и очистка value-cache не теряют его; `SplineNode` без активной сессии отдаёт `Results` из своих данных.
- Start Edit открывает текущее содержимое ноды (не пустой контейнер и без лишнего пустого сплайна); Stop Edit фиксирует результат одним Undo-шагом, возвращающим к состоянию до Start Edit; контейнер убирается и не воскрешается Undo.
- Полный round-trip Stop → Start Edit → save/reopen: узлы, режимы тангенсов, AutoSmooth tension, embedded `SplineData`, knot links, `Closed` — без потерь.
- Во время сессии: перетаскивание/добавление/удаление узлов, смена `Closed`, add/remove/reorder сплайнов, изменение линков и перенос контейнера обновляют превью зависимых нод не позже 0.25 с; Ctrl+Z правки узла синхронизируется.
- Non-identity host (position + rotation + non-uniform scale): мировые координаты сплайна неизменны на всех стадиях; превью не смещается.
- `SplineNode` внутри `PcgSubGraph`: Undo/SetDirty идут в asset; два host'а с одним сабграфом и совпадающими локальными id не конфликтуют сессиями.
- Domain reload при активной сессии: правки последних 0.25 с не теряются, Start Edit подхватывает сироту; двойная сирота разрешается детерминированно.
- Вход в Play Mode и сборка плеера: в сцене нет `Spline Edit`, `SplineContainer` контейнера и маркера.
- Цепочка `SplineNode` → `SubGraphNode` (CityBlocks/StonePath) → `Result` с `AutoGenerate = true`: сцена перестраивается после стабилизации правки без Stop Edit.
- Перф: sync-тик при отсутствии сессий — 0 аллокаций; p95 полного `WriteBack` на 32 сплайна / 10 000 узлов ≤ 16 мс; последняя правка перед Stop/reload не теряется. Численные пороги подтверждаются профилировкой до перевода в `Выполнено`.

## Done-состав

- Смени статус в начале документа на `Выполнено`; зафиксируй здесь ревизию ядра/DLL.
- Обнови `Docs/PROJECT_MAP.md` (подсистема `Tools/`, поток нода ↔ временный контейнер).
- Обнови `Packages/PCG.Splines/Documentation~/PCG.Splines/Splines/Spline-Node.md`: storage, координатный контракт, Start/Stop, Undo, domain reload, несколько сплайнов, линки.
