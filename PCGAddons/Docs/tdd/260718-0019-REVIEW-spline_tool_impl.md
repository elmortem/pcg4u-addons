# Ревью реализации: SplineNode — персистентный сплайн с живым редактированием

По ТДД `260711-2242-TDD-spline_tool.md` (ревизия D).

Метод: вычитаны все 11 новых/изменённых файлов пакета PCG.Splines и 2 правки ядра; каждая API-ссылка сверена с исходниками ядра (ProjectPCG), исходниками Unity Splines 2.8.2 (PackageCache) и бинарно — с DLL в аддон-репо.

Состав проверенного:

- Ядро: `IPcgTempRoot.cs`, `PcgBuildPreprocessor.cs`.
- Рантайм пакета: `SplineNode.cs`, `PcgSplineEditContainer.cs`, `SplinesGizmoUtility.cs`, `SplinesUtility.cs`.
- Редактор пакета: `SplineNodeExecutor.cs`, `SplineNodeRenderer.cs`, `SplineCopyUtility.cs`, `SplineHashUtility.cs`, `SplineEditSession.cs`, `SplineEditSessions.cs`, `SplineNodeEditWatcher.cs`.

Вердикт: архитектура реализована по ТДД, все использованные API существуют и сигнатурно корректны, ключевые обходы (copy-последовательность, JSON для линков, реконсиляция drop+links) — верные. Найдено 2 высоких пробела в lifecycle, 4 средних дефекта и ряд низких. До перевода ТДД в `Выполнено` высокие и средние стоит закрыть.

---

## Подтверждено корректным

- Copy-последовательность `new Spline(knots, closed)` → `SetTangentModeNoNotify` → `SetAutoSmoothTensionNoNotify` — верна по исходникам 2.8.2: конструктор кладёт узлы сырыми (`m_Knots = knots.ToList()`, метаданные не трогает); у `Broken` нет ветки в switch `ApplyTangentModeNoNotify` — произвольные тангенсы сохраняются; для `AutoSmooth` порядок mode→tension критичен и соблюдён: `SetAutoSmoothTensionNoNotify` повторно пересчитывает тангенсы уже с правильным tension. Оговорка: Continuous/Mirrored-узлы, неконформные своему режиму (тангенсы правились в обход API), при копировании «запекутся» — допустимо.
- Реконсиляция `links.SplineRemoved(dropped[i])` по убыванию индексов — корректна против реализации `KnotLinkCollection.SplineRemoved` (декремент индексов выше удалённого).
- JSON-путь для `KnotLinkCollection` валиден: единственное сериализуемое поле — `[SerializeField] KnotLink[]`, вложенные типы `[Serializable]`, всё поля, не свойства.
- Undo-контракт `CommitStop`: снапшот присваивается до `RegisterCompleteObjectUndo`, контейнер создаётся и уничтожается вне Undo — один шаг возвращает к состоянию до Start Edit, контейнер не воскрешается. `Undo.RegisterCompleteObjectUndo(owner)` восстановит поля ноды: цепочка `PcgComponent/PcgSubGraph → PcgGraphData → [SerializeReference] Nodes → SplineNode.Splines/Links` сериализуема.
- `SerializationOwner` — дословно конструкция самого ядра (`PcgExecGraph.cs:165`): `OwnerExecutor != null ? OwnerExecutor.Data.SubGraph : Host as Object`.
- Идентичность сессий для вложенных графов: `SubGraphNodeExecutor` биндит вложенный граф с `GraphId` родителя (`_inner.Bind(subGraph.GraphData, Graph.GraphId, Address)`) — два host'а с одним сабграфом получают разные ключи, критерий приёмки структурно выполним.
- Отдача `Results.Value = Data.Splines` по ссылке безопасна: в `PcgOutputPools` зарегистрирован только `List<PointData>`, для `List<Spline>` `Release()` лишь обнуляет ссылку, `Clear()` не вызывается.
- Ложного dirty при Start Edit нет: события сеттера `container.Splines` летят во время `PopulateContainer`, до `Begin` — сессии в реестре ещё нет.
- `PopulateContainer` заменяет содержимое контейнера целиком (сеттер `Splines` пересоздаёт массив) — мусорного пустого сплайна нет; пустая нода даёт ровно один пустой сплайн.
- Препроцессор: `DestroyTempRoots` удаляет весь GameObject маркера до общего прохода по `IPcgTemp`; `IProcessSceneWithReport.OnProcessScene` срабатывает и на входе в Play Mode — двойная страховка к flush'у вотчера.
- `EditorSplineUtility.SetKnotPlacementTool` — public static void без параметров, совместим с `delayCall`.
- Тик вотчера при отсутствии сессий — ранний выход без аллокаций; `Initialize` идемпотентен (отписка перед подпиской).

---

## Высокие

### H1. Lifecycle «Удаление ноды / host» не реализован

Строка таблицы ТДД «Удаление ноды / host → discard + удаление контейнера + warning» не имеет реализации: `Rebind(key, null)` (ведущий к `TerminateNoFlush`) никем не вызывается — единственный вызов `Rebind` в `SplineNodeRenderer.DrawExtras` всегда передаёт живой executor. Подписки на `PcgExecGraph.NodeRemoved` (публичное событие, `PcgExecGraph.cs:33`) нет, валидации executor'а в тике нет.

Последствия: удаление `SplineNode` из графа или host-объекта оставляет зомби-сессию и вечный «Spline Edit» в сцене; `WriteBack` продолжает писать в отвязанный `Data` и звать `SetDirty` по возможно уничтоженному owner.

Рекомендация: подписка вотчера на `PcgExecGraph.NodeRemoved` + в тике проверка живости (`session.Executor.Graph == null` / нода отсутствует в графе / owner уничтожен → `TerminateNoFlush` с warning).

### H2. Rebind только при отрисовке окна графа — Undo при закрытом окне теряет правки

После Undo/Redo десериализация `[SerializeReference]` создаёт новые инстансы нод; `session.Executor.Data` остаётся ссылкой на осиротевший объект. Перепривязка происходит только в `DrawExtras`, т.е. при открытом окне графа с видимой нодой. При закрытом окне `OnUndoRedo` помечает сессию dirty, и следующий `WriteBack` пишет в мёртвый инстанс — правки молча теряются до первого открытия окна.

Рекомендация: на `Undo.undoRedoPerformed` валидировать привязку каждой сессии (минимум — детектировать отвязку и приостанавливать запись с warning; лучше — ре-резолвить живой executor по ключу).

---

## Средние

### M1. Подхват сироты без реконсиляции контейнер → нода

В ветке подхвата `StartEdit` не выполняется ни `WriteBack`, ни `session.Dirty = true`. Штатный domain reload прикрыт `FlushAll`, но после краша редактора или reopen сцены с сохранённой сиротой (а также после правок сироты без активной сессии — её узлы можно двигать в сцене напрямую) данные ноды отстают от контейнера до первого события правки. Превью при этом скрыто, downstream считает по устаревшим данным.

Фикс — одна строка: пометить сессию dirty сразу после `Begin` в ветке подхвата.

### M2. Копия embedded SplineData теряет PathIndexUnit и DefaultValue

`SetFloatData`/`Set*Data` в 2.8.2 копируют через `new SplineData<T>(value)`, который резолвится в конструктор из `IEnumerable<DataPoint<T>>`: переносятся только точки, `m_IndexUnit` сбрасывается в `Knot`, `DefaultValue` — в `default(T)` (тот же дефект — в copy-конструкторе `Spline`). Критерий приёмки «embedded SplineData без потерь» в общем случае не выполняется.

Фикс: в `CopyEmbeddedData` после `Set*Data` взять хранимый инстанс через `TryGet*Data(target)` (возвращает ссылку) и перенести `PathIndexUnit` и `DefaultValue` из источника.

### M3. GetVersionSalt не видит правок embedded SplineData

Хешируются только имена ключей — ни количество точек, ни значения, ни `PathIndexUnit`. Правка данных не инвалидирует ноду и не будит AutoGenerate. ТДД требовал минимум счётчики по ключам; реализация слабее.

Фикс: подмешивать `Count` каждого канала и свёртку точек (Index + Value.GetHashCode()).

### M4. Non-uniform scale запекается приближённо

`BezierKnot.Transform(float4x4)` извлекает rotation как чистый кватернион из матрицы, отбрасывая shear от неоднородного масштаба — сам Unity для non-uniform контейнеров идёт через `NativeSpline`, а не через этот метод. Формулировка ТДД «scale-aware, корректно для non-uniform scale» неточна — это унаследованный дефект ТДД. Критерий «численно в тех же мировых координатах» при non-uniform scale контейнера может не сойтись точно.

Решение: либо зафиксировать в ТДД допуск (rigid/uniform — точно, non-uniform — приближение), либо запекать через `NativeSpline`.

---

## Низкие

- `FindOrphan` использует `FindObjectsByType` без `FindObjectsInactive.Include` — неактивная сирота не подхватится и не зачистится как дубль.
- `LinksHash` считается через `JsonUtility.ToJson` каждый тик 0.25 с на сессию — аллокации при активной сессии (контракт «0 аллокаций без сессий» соблюдён). Возможная оптимизация: быстрый чек `Count`, JSON — лениво.
- Нет flush на `EditorSceneManager.sceneSaving`: Ctrl+S в пределах 0.25 с после правки сохраняет сцену с чуть отставшими данными ноды. Дёшево добавить `FlushAll(false)`.
- Латентный футган: `SplineListPool.Return` делает `list.Clear()`; если когда-либо зарегистрировать `List<Spline>` в `PcgOutputPools` с этим пулом, `Release()` начнёт чистить авторский `Data.Splines`. Зафиксировано здесь как предохранитель.
- Выход отдаётся ссылкой на `Data.Splines` — конвенция «ноды не мутируют входы» обязана соблюдаться downstream (в ядре принята).

---

## DLL в аддон-репо

`PCG.dll`/`PCG.Editors.dll` (сборка 2026-07-17 23:56) содержат весь публичный контракт spline_tool: `IPcgTempRoot`, `OwnerExecutor`, `GraphId`, `PcgSyncPreviewNodeExecutor`, `PcgBuildPreprocessor` — компиляция пакета обеспечена. Бинарный анализ показал отсутствие приватных членов и строковых литералов тел методов (похоже на сборку публичной поверхности/обфускацию) — доказать по бинарнику, что новая логика `DestroyTempRoots` (удаление всего GameObject) попала в DLL, невозможно. Проверяется только вживую — пункт чеклиста ниже.

---

## Чеклист ручной приёмки

Статикой не доказывается — прогнать в редакторе до перевода ТДД в `Выполнено`:

- Round-trip: узлы всех режимов тангенсов (включая Broken с несимметричными тангенсами и AutoSmooth с ненулевым недефолтным tension), `Closed`, линки, embedded SplineData (после фикса M2 — с `Distance`-индексацией и DefaultValue) — Stop → Start → save/reopen без потерь.
- Non-identity host (position + rotation + non-uniform scale) — мировые координаты на всех стадиях; допуск по M4.
- Два host'а с одним `PcgSubGraph` — независимые сессии, Undo/SetDirty в asset.
- Undo: во время сессии, после Stop (один шаг, контейнер не воскресает), после domain reload; отдельно — Undo при закрытом окне графа (H2).
- Удаление ноды и удаление host'а при активной сессии (H1).
- Domain reload при активной сессии; двойная сирота; сирота после reopen сцены (M1).
- Вход в Play Mode и сборка плеера: «Spline Edit» отсутствует целиком (заодно подтверждает свежесть DLL).
- Перф: p95 `WriteBack` 32 сплайна / 10 000 узлов ≤ 16 мс; тик без сессий — 0 аллокаций.
- Цепочка `SplineNode` → `SubGraphNode` → `Result` с `AutoGenerate = true` — перестройка без Stop Edit.
