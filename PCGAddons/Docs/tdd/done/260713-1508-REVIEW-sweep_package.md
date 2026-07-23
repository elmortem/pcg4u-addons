# Ревью ТДД пакета PCG.Sweep

Дата ревью: 2026-07-13  
Ревьюируемый документ: `Docs/tdd/260711-2243-TDD-sweep_package.md`  
Статус ревью: требуется доработка ТДД до начала реализации

## Итоговый вердикт

Направление правильное: отдельный аддон, стандартный вход `List<Spline>`, выход через ядровый `MeshInstanceData`, материализация существующим `MeshInstanceMaker`, сэмплирование по длине, terrain snapshot и детерминированные индексированные слоты хорошо укладываются в архитектуру PCG4U.

Но текущий контракт годится только для узкого happy path: валидный открытый сплайн, `Ribbon`, профиль обязательно подключён второй нодой, террейн небольшой или отсутствует, вычисление не отменяется во время материализации. Для заявленных дорог, стен, русел, замкнутых контуров и постоянной автогенерации в Unity Editor ТДД пока не готов.

До реализации нужно закрыть замечания P1. Главные причины:

- пакет в заявленном виде не самодостаточен по зависимостям;
- `Rectangle` строится с внутренними нормалями, а UV замкнутых профилей и сплайнов имеют неправильные швы;
- пустой или невалидный вход оставляет старые scene objects и может пометить их актуальными;
- `Sweep Spline` не работает сам по себе и имеет сломанный unconnected backing UX;
- worker читает живые `Data`, `Spline` и `AnimationCurve`, хотя ТДД заявляет immutable snapshot;
- terrain content не участвует в версии ноды для прямого backing value;
- performance-критерий учитывает только часть CPU-геометрии и не учитывает наиболее дорогие main-thread/GC стадии.

## Сводка замечаний

| Приоритет | Проблема | Последствие |
|---|---|---|
| P1 | В `package.json` нет `com.unity.splines` | Чистая установка пакета не компилируется при наличии только заявленных зависимостей |
| P1 | Неверный winding `Rectangle`, нет корректных UV-швов и hard-edge topology | Закрытая форма видна изнутри, текстура тянется на швах, прямоугольник выглядит сглаженным |
| P1 | Ранний выход на пустом/невалидном профиле не очищает инстансы | Старый меш остаётся в сцене и может считаться актуально сгенерированным |
| P1 | Пропущенные сплайны оставляют `null` в индексированных слотах | `MeshInstanceMaker` получает `null` и падает; нулевые/короткие closed-сплайны дают degenerate mesh |
| P1 | `Sweep Spline` требует обязательную ноду `Profile` | Новая нода инертна сама по себе и нарушает принцип независимости нод |
| P1 | Worker читает живые `Data.WidthByT`, `Data.Collider` и `spline.Closed` | Результат одного compute может смешать состояния до и после правки; snapshot-контракт не выполнен |
| P1 | Нет version salt для прямого `TerrainData` | Правка высот террейна не обязана инвалидировать `Sweep Spline` и запускать Auto Generate |
| P1 | Full-heightmap snapshot и materialization не имеют бюджета | Большой GC/main-thread spike, collider cooking и upload мешей могут фризить Editor несмотря на thread-pool геометрию |
| P2 | Не закрыт заявленный product scope: caps, depth curve, twist | Стены/трубы/водопад и часть closed-кейсов остаются прототипными |
| P2 | Не определены валидация профиля, frame continuity и terrain edge policy | NaN, вырожденные треугольники, flips/twist и странное поведение за границей Terrain |
| P2 | Не проверены coordinate space, save/reload и player build | Меш может сдвигаться под non-identity parent либо не пережить stripping/serialization путь |
| P2 | Нет metadata, автоматизированных тестов и обязательной документации | Каталог `PCG.Authoring` выдаёт диагностики, а регрессии остаются ручными |

---

## P1. Пакет не декларирует зависимость от Unity Splines

ТДД указывает в `package.json` только:

```json
"dependencies": {
  "com.unity.mathematics": "1.2.6"
}
```

При этом обе asmdef ссылаются на `Unity.Splines`, а runtime-код использует `UnityEngine.Splines.Spline`. Живой `Packages/PCG.Polygons/package.json`, на который ссылается ТДД как на образец, декларирует и `com.unity.mathematics`, и `com.unity.splines: 2.8.2`. То же делают `PCG.Splines`, `PCG.Mazes` и `PCG.SpriteShapes`.

### Требуемое изменение

- Добавить `"com.unity.splines": "2.8.2"` в зависимости пакета.
- Добавить compile/import smoke-test в чистом Unity 2022.3 проекте с ядром PCG4U и только зависимостями из `package.json`.
- Удалить ссылку `PCG.Gizmos.Editor` из editor asmdef, если код пакета действительно не использует её API.

---

## P1. Топология `Rectangle`, normals и UV-швы некорректны

### Winding закрытого профиля

Точки `Rectangle` идут против часовой стрелки в плоскости XY:

```text
(-half, 0) -> (half, 0) -> (half, height) -> (-half, height)
```

Для движения вдоль `+Z` ТДД создаёт первый треугольник нижней стороны как `a,c,b`. Его нормаль равна `+Y`, хотя внешняя нормаль нижней стороны прямоугольника должна быть `-Y`. На правой стороне получается `-X` вместо `+X`. То есть winding, специально подобранный для верхней стороны `Ribbon`, разворачивает закрытый CCW-профиль внутрь.

При обычном back-face culling прямоугольная труба/стена будет видна изнутри, а не снаружи. Один общий порядок индексов нельзя молча считать корректным одновременно для открытого «лицевого» профиля и закрытой оболочки.

### U-шов закрытого профиля

У `Rectangle` четыре вершины и четыре значения `U`; последняя вершина имеет `U < 1`, после чего замыкающий сегмент соединяет её с первой вершиной `U = 0`. Для корректного texture seam нужна геометрически дублированная первая вершина с `U = 1`. Без неё UV последней стороны интерполируется назад к нулю и даёт растяжение/переворот текстуры.

### V-шов замкнутого сплайна

Для closed spline ТДД не создаёт кольцо в `distance = length`: последнее кольцо имеет `V < length * UvScale`, а затем соединяется с первым кольцом `V = 0`. Это геометрически замыкает меш, но создаёт неправильную UV-интерполяцию на последнем сегменте. «Кольцо без шва» из критериев приёмки этим алгоритмом не обеспечивается.

### Shading и tangents

Живой `MeshInstanceMaker` выполняет `RecalculateNormals()` на общей сетке вершин. У `Rectangle` одна вершина разделяется соседними сторонами, поэтому угол будет сглажен. Для жёстких граней нужны дублированные вершины по сторонам либо явно переданные normals. Кроме того, maker не строит tangents, поэтому дорожный материал с normal map не имеет полного vertex stream.

### CapEnds

`Closed = true` у профиля замыкает только боковой периметр. Открытый сплайн остаётся без торцевых крышек, поэтому результат — открытая труба, а не закрытая «призма», обещанная критерием приёмки.

### Требуемое изменение

- Разделить winding policy для open/front-facing и closed/outward-facing профилей; для custom closed profile учитывать signed area либо нормализовать winding при создании профиля.
- Дублировать seam vertices: профильное `U = 0/1` и кольцо closed spline с `V = 0/length * scale`, не связывая разные UV через одну вершину.
- Зафиксировать smooth/hard edge contract. `Rectangle` должен иметь hard edges; `HalfPipe` — гладкую внутреннюю поверхность; `Ribbon` — стабильную верхнюю нормаль.
- Добавить tangents: либо опциональные `Normals`/`Tangents` в `MeshInstanceData`, либо ядровый `RecalculateTangents()` после валидных UV и normals.
- Добавить `CapEnds` хотя бы для открытого сплайна с закрытым профилем; явно описать winding, normals и UV крышек.
- Добавить тесты на outward normals, back-face visibility, hard edges, normal-mapped material и непрерывный tiling на обоих типах швов.

---

## P1. Пустой вход оставляет старую геометрию и помечает её актуальной

По ТДД `Results.Value` сначала становится пустым списком, но при `profile == null` или `profile.Points.Length < 2` executor сразу возвращается. `ClearInstancesAsync()` вызывается только при `Enabled == false`.

Это особенно опасно в генерационном пути:

1. Пользователь успешно генерирует дорогу.
2. Отключает `Profile`, удаляет связь или делает профиль невалидным.
3. Новый compute возвращается до `RemoveInstances`.
4. `PcgComputeSystem` считает compute завершённым.
5. Живой `ResultNodeExecutor.GenerateAsync()` после успешного resolve записывает текущий `EffectiveVersion` в `GeneratedVersions`, если `node.IsComputed`.
6. Старый scene mesh остаётся и на следующем Generate может быть пропущен как актуальный.

«Точная калька `RegionToMeshNodeExecutor`» не решает проблему: живой executor тоже возвращается на пустом регионе до materialization. Для новой ноды этот дефект нельзя закреплять как контракт.

Дополнительно `IsEmpty => Results.Value == null` считает пустой список непустым результатом.

### Требуемое изменение

- Любой валидный empty-result обязан синхронизировать сцену: удалить owned objects и удалить/обновить generated version согласно единому контракту.
- Все ранние выходы (`no splines`, `no profile`, invalid profile, no valid frames) должны проходить через один finalize/commit путь.
- `IsEmpty` должен учитывать `Results.Value == null || Results.Value.Count == 0` либо executor должен хранить `null` для отсутствующего результата последовательно во всех ветках.
- Acceptance: после disconnect профиля/сплайнов, invalid custom profile и отключения ноды в сцене нет старых объектов; повторный Generate не считает старый результат актуальным.
- Materialization должна быть cancellation-safe: `Begin/End` — через `try/finally`, а отмена после добавления части мешей не оставляет partial generation. Предпочтительно сохранить старый валидный результат до успешной сборки нового; если для этого нужен staging/swap в ядре, расширить ядро, а не делать локальный обход в `PCG.Sweep`.

---

## P1. Индексированные слоты не согласованы с пропуском невалидных сплайнов

ТДД требует «сквозной индекс» и пропускает `spline.Count <= 1`, но затем добавляет `meshes[index]` в `Results` по всем индексам. Для пропущенного элемента слот остаётся `null`.

Живой `MeshInstanceMaker.TryAdd()` кастует коллекцию в `MeshInstanceData` и вызывает `AddMesh(data, ...)`; `AddMesh` сразу читает `data.Name`. `null` в `Results` закончится `NullReferenceException` во время materialization.

Отдельные вырожденные случаи тоже не закрыты:

- `Spline` может быть `null` внутри списка;
- два и более knot не гарантируют ненулевую длину;
- closed spline при `steps = 1` получает одно кольцо и треугольники, ссылающиеся на то же кольцо;
- `round(length / step)` не делает `Step` максимальной длиной сегмента и может дать сегмент заметно длиннее настройки.

### Требуемое изменение

- До аллокаций собрать компактный, стабильно упорядоченный список валидных spline snapshots: non-null, минимум два knot, finite length больше epsilon, успешные finite frames.
- Результаты хранить в слотах этого компактного списка; `null` никогда не попадает в `Results`.
- Для open spline гарантировать минимум два кольца; для closed — минимум три невырожденных кольца.
- Если `Step` означает максимальный spacing, использовать `ceil(length / step)`, а не `round`; иначе переименовать поле и точно описать семантику.
- Добавить тест смешанного входа `valid/null/one-knot/zero-length/valid`: два результата, исходный относительный порядок сохранён, materialization не падает.

---

## P1. `Sweep Spline` не является самостоятельной нодой

В ТДД поле:

```csharp
[Input(Connection = PcgConnectionType.Override)]
public SweepProfile Profile;
```

по умолчанию равно `null`, поэтому только что добавленная `Sweep Spline` ничего не строит без обязательной второй ноды `Profile`.

Это прямо расходится с `Docs/DESIGN_PRINCIPLES.md`: нода должна быть полноценна сама по себе и не требовать второй ноды-компаньона. `ProfileNode` полезна для переиспользования профиля, но должна быть optional override, а не обязательным setup step.

Есть и конкретная проблема Unity UX. `SweepProfile` не помечен `[Serializable]`. Живой быстрый редактор считает любое public-поле body field (`PcgNodeTypeCache`), но `SerializedObject.FindPropertyRelative("Profile")` не вернёт property для несериализуемого типа. Пока порт не подключён, `PortDrawsBacking()` просит рисовать backing value, а renderer получает `null` и оставляет пустую строку вместо понятного поля/лейбла. После подключения появляется только label порта. Это не нормальный Unity workflow.

### Требуемое изменение

- `Sweep Spline` должна иметь сериализуемый inline default, минимум `Ribbon` с разумной шириной, и строить результат сразу после подключения сплайна.
- `ProfileNode` остаётся переиспользуемым override-путём.
- Допустимые варианты: сериализуемый `SweepProfileDefinition` как backing value либо встроенные `DefaultShape/Width/Height/...` поля в `SweepSplineNode`, скрываемые при подключённом profile port.
- Не показывать пользователю сырые массивы как основной UX. Для `Custom` нужны как минимум понятный список с `Closed`, validation message и Undo; для product-quality authoring предпочтительны extras renderer и 2D/SceneView handles по протоколу из `DESIGN_PRINCIPLES.md`.
- Нерелевантные параметры должны скрываться: `Height` не нужен `Ribbon`, `Width/Height` не должны притворяться активными для произвольного `Custom`.

---

## P1. Worker получает живые изменяемые объекты вместо immutable snapshot

ТДД декларирует правильный принцип: Unity API используется на главном потоке только для снятия snapshot. Но worker-контракт затем читает:

- `Data.WidthByT.Evaluate(frame.T)`;
- `Data.Collider` при формировании результата;
- `spline.Closed` при построении topology;
- массивы mutable-объекта `SweepProfile` без явного ownership/snapshot контракта.

То есть в фон всё же передаются живые `Data`, `AnimationCurve`, `Spline` и профиль. При правке curve, закрытости сплайна или ноды во время длинного compute один меш может быть собран из разных состояний. Ссылка на `DensityByCurveNodeExecutor` показывает существующий прецедент, но не отменяет более новый прямой запрет `DESIGN_PRINCIPLES.md:56`.

Фраза «`await UniTask.SwitchToThreadPool();` батч на сплайн» также недостаточна для гарантии параллелизма. Если создание каждой задачи начинает CPU-цикл до собственного первого `await`, батчи будут последовательно выполнены при построении списка задач.

### Требуемое изменение

- На editor thread снять полный pure-data snapshot: frames, `Closed`, копию validated profile arrays, sampled/immutable curve representation, step/UV/terrain settings, name/collider flags и прочие scalar values.
- Ни один background method не должен обращаться к `Data`, `Spline`, `TerrainData`, `AnimationCurve`, `Material` API или другим живым Unity-объектам. Unity object references можно присоединить к готовому `MeshInstanceData` после возврата на editor thread.
- Каждый CPU batch должен явно начинаться с `SwitchToThreadPool()`/`RunOnThreadPool`, иметь отдельный индексированный слот и читать только snapshot.
- Acceptance: непрерывная правка spline/profile/curve во время compute приводит к отмене/trailing recompute и одному целостному финальному результату последней версии.

---

## P1. Изменение прямого `TerrainData` не входит в версию ноды

`PcgNodeDescriptor.ComputeParamVersion()` хеширует `UnityEngine.Object` по `GetInstanceID()`. Содержимое heightmap в этот хеш не входит. Подключённый `TerrainObjectValue`/`TerrainDataValue` добавляет `PcgTerrainContentVersion` через `VariableNodeExecutor`, но прямой backing value `Data.Terrain` остаётся с тем же effective version после terrain brush.

ТДД не определяет `GetVersionSalt()` для `SweepSplineNodeExecutor`, хотя `DESIGN_PRINCIPLES.md:39-40` требует учитывать внешние ссылки и из связей, и из полей `Data`. Это особенно важно, потому что ТДД прямо зависит от Auto Generate и обещает интерактивную перестройку после правки внешних источников.

### Требуемое изменение

- Добавить version salt/invalidation contract для реально разрешённого `TerrainData`, включая `PcgTerrainContentVersion.Get(terrain)` для direct backing path.
- Проверить оба пути: прямое поле ноды и variable pill `TerrainObjectValue`.
- Если `TerrainOffset` подключён отдельно, тестировать изменение позиции Terrain через multipоrt value и согласованность пары `Terrain/Offset`.
- Acceptance: terrain brush инвалидирует и обновляет активный preview и Auto Generate без ручного изменения параметра ноды.

---

## P1. Performance-критерий измеряет не тот pipeline

Требование «20 сплайнов по 1 км с шагом 0.5 не фризят редактор» полезно как нагрузка, но ТДД связывает его только с построением геометрии в thread pool. Пользователь ощущает весь pipeline:

```text
Spline Evaluate / Terrain GetHeights
  -> managed geometry allocations
  -> Mesh.SetVertices/SetUVs/SetTriangles
  -> RecalculateNormals/RecalculateBounds/(Tangents)
  -> GameObject/renderer creation
  -> optional MeshCollider cooking
  -> удаление старых scene objects и dirtying сцены
```

Большая часть второй половины выполняется живым `MeshInstanceMaker` на editor thread.

### Full-heightmap snapshot

`terrain.GetHeights(0, 0, res, res)` копирует всю heightmap при каждом compute. Для `4097 x 4097` это 16 785 409 `float`, примерно 64 MiB managed memory, даже если дорога занимает узкую полосу террейна. Сам вызов не имеет cancellation point и выполняется до thread-pool sampling.

### Геометрические аллокации

ТДД заранее знает точное число индексов, но строит `List<int>` и затем делает `ToArray()`, создавая лишнюю копию. На каждом spline drag дополнительно создаются frames, vertices, UV и triangle arrays, затем данные копируются в native `Mesh`. При включённом collider добавляется синхронный cooking.

### Неограниченный размер

Минимальный `Step = 0.05` без max-vertex/max-triangle/chunk policy позволяет длинному сплайну создать миллионы колец. `MeshInstanceMaker` уже использует `IndexFormat.UInt32`, поэтому лимит 65k не является проблемой, но память, upload, normals и collider cooking остаются неограниченными. Одна задача на каждый входной сплайн также не имеет верхнего concurrency budget.

### Требуемое изменение

- Снимать только высоты texel-области, реально покрывающей sweep bounds, с корректным mapping локального окна; рассмотреть кеш snapshot по `TerrainData + content version` в общем ядровом механизме, если им будут пользоваться несколько нод.
- Аллоцировать точные массивы vertices/UV/indices без `List<int> -> ToArray`.
- Ввести понятный budget/chunk policy: предел vertices/triangles на mesh/job, контролируемое число worker batches, диагностика вместо OOM.
- Измерять end-to-end preview/generate, отдельно geometry и materialization, collider on/off.
- Задать численные критерии после baseline: wall time, max editor-thread stall, managed allocations, peak memory, cancellation latency и время collider cooking.
- Проверить steady-state continuous spline drag и повторные пересчёты, а не только один холодный Generate.

Минимальная benchmark matrix:

| Сценарий | Варианты | Что измерять |
|---|---|---|
| 20 × 1 км | Ribbon / HalfPipe, Step 1 / 0.5 | wall time, max main-thread frame, allocations, peak memory |
| Terrain | none / 1025 / 4097 heightmap | snapshot time/bytes, editor responsiveness |
| Collider | off / on | mesh upload, cooking time, cancel latency |
| Editing | single change / continuous drag | trailing result, stale objects, frame spikes |
| Extreme input | tiny Step / many profile points / many splines | bounded concurrency, chunking, diagnostic instead of OOM |

---

## P2. ТДД не закрывает заявленный scope продукта

Исходная заметка `ProjectPCG/Docs/notes/spline_mesh_generation.md` перечисляет `CapEnds`, twist и изменение ширины; план waterfall demo (`unreal_pcg_demos_plan.md:313`) называет обязательными UV по длине, width/depth по `AnimationCurve` и twist. Текущий ТДД реализует только `WidthByT`, причём только по X профиля.

Для заявленных кейсов последствия заметны:

- стена/прямоугольная труба открыта на торцах;
- русло/водопад нельзя независимо менять ширину и глубину;
- нет author-controlled twist/roll;
- closed spline не имеет контракта согласования roll в точке замыкания.

Кроме того, программа P2 в `unreal_pcg_demos_plan.md:29` включает `RegionExtrudeNode`, которого в этом ТДД нет.

### Требуемое изменение

Либо довести package contract до заявленного product scope:

- `WidthByT`/`HeightByT` (или `ScaleXByT`/`ScaleYByT`);
- `TwistByT` с точно указанными единицами и порядком применения;
- `CapEnds`;
- closed-loop roll/seam policy;

либо явно назвать документ этапом P2.1/P2.2, убрать заявления о полном P2 и перечислить обязательные последующие ТДД. С учётом принципа «продукт, а не MVP» нельзя помечать всю программу P2 выполненной после текущего урезанного набора.

Пересечения и T/X-junction дорог разумно оставить вне scope: исходная заметка уже выделяет их как отдельную сложную тему.

---

## P2. Не хватает валидации профиля и sampling/frame contract

### Профиль

- `Height` не ограничен. Отрицательное значение ломает формулу периметра `Rectangle`; при `height = -width` знаменатель становится нулём.
- `CustomPoints.Count >= 2` не гарантирует ненулевую длину. Совпадающие точки дают деление на ноль при нормализации `Us`.
- Не определены NaN/Infinity, повторные точки, self-intersection, winding и возможность `Custom.Closed`.
- `SweepProfile.GetContentHash()` предполагает non-null массивы одинаковой длины, но сам тип не гарантирует инварианты. При этом этот метод не участвует автоматически в `EffectiveVersion` обычного output port; если он оставлен для будущего `PcgValue`, назначение надо описать.
- `WidthByT` может вернуть отрицательный или non-finite multiplier. Нужно решить, разрешён ли taper в ноль и как строится topology в точке схлопывания.

### Frames

- После `right = normalize(cross(up, tangent))` нужно заново ортогонализовать `up`; текущий код продолжает использовать исходный `up`.
- Fallback `(1,0,0)` может создать резкий flip при почти параллельных `up` и `tangent`.
- Нужен continuity policy: parallel transport либо контролируемое использование spline up с коррекцией flips и closed-loop roll error.
- Не определено поведение при неуспешном/неfinite `Spline.Evaluate`.

### Terrain

- `clamp` координат за границей Terrain приклеивает внешние вершины к крайнему texel. Нужна явная policy: оставить исходную высоту, не строить участок, clamp с warning либо другой выбранный режим.
- Алгоритм сначала применяет `up * p.y`, затем перезаписывает `vertex.y` как `terrainHeight + p.y`, оставляя возможный XZ-вклад наклонённого `up`. Это гибрид local-frame и world-up semantics. Нужно выбрать одно: drape baseline + local profile либо world-up profile для terrain mode.

### Обязательные проверки

- горизонтальный, вертикальный и S-образный сплайн;
- closed loop с согласованным roll;
- repeated/zero-length knots;
- custom open/closed profile с разным winding;
- taper до нуля и попытка отрицательного multiplier;
- sweep частично за границей Terrain;
- rolled spline с terrain projection.

---

## P2. Не определены coordinate space и сохранность результата в build

В документации ядра пространство графа заявлено мировым, а `SplinesValue` переводит knots через `SplineContainer.transform`. Одновременно `MeshInstanceMaker` parent-ит новый объект и задаёт ему `localPosition = 0`, `localRotation = identity`, `localScale = 1`. Если `InstanceMaker.Parent` имеет non-identity transform, world-space vertices могут получить transform второй раз.

ТДД должен не предполагать identity setup, а закрепить coordinate contract тестом. Возможные решения относятся к ядровому maker либо к явному преобразованию snapshot в local space целевого parent перед созданием `MeshInstanceData`.

Исходная заметка `spline_mesh_generation.md:17` также оставляет открытым вопрос, как editor-created mesh переживает scene serialization и build. Живой `PcgBuildPreprocessor` удаляет `IPcgTemp` instance maker и `PcgComponent`; запечённые MeshFilter/MeshRenderer/MeshCollider и их mesh data должны гарантированно остаться.

### Требуемые acceptance cases

- identity и non-identity `PcgComponent`/`InstanceMaker.Parent`: одинаковое мировое положение sweep;
- save scene -> close/reopen -> mesh/material/collider сохранены;
- domain reload и Generate/Clear сохраняют ownership без дублей;
- player build после stripping содержит видимый mesh и рабочий collider, но не authoring components;
- удалённый/отменённый result не оставляет orphan Mesh objects.

---

## P2. Metadata, тесты и документация отсутствуют в составе работ

Ноды имеют `[PcgNodeInfo]`, но ни одно поле и ни один output не имеют `[PcgMemberInfo]`. Живой `PcgNodeCatalog` помечает такие типы как `MetadataComplete = false` и создаёт `MissingMemberMetadata` diagnostic для каждого параметра/output. Это ухудшает `PCG.Authoring`, discovery и будущую автоматическую сборку demo graph.

В структуре пакета нет `Tests/Editor`, а критерии приёмки полностью ручные. Для topology, cancellation и content-versioning этого недостаточно.

Финальный пункт «уточнить, нужно ли обновить документацию» тоже слабее product contract. Новый публичный пакет, тип порта и две ноды обязаны попасть в карту и package docs в той же поставке.

### Требуемое изменение

- Добавить `[PcgMemberInfo]` ко всем полям и output properties обеих нод; проверить `PcgNodeCatalog.GetDiagnostics()` и `MetadataComplete`.
- Добавить `Tests/Editor/PCG.Sweep.Editor.Tests.asmdef` и автоматизированные тесты, перечисленные ниже.
- Сделать обязательными `Documentation~`, обновление `Docs/PROJECT_MAP.md` и, если пакет участвует в extras/setup, соответствующего каталога ядра.
- Дополнить `package.json` как настоящий package artifact: `description`, `keywords`, `category` и согласованные зависимости, а не только минимальный набор полей.

---

## Обязательная тестовая матрица

### Geometry EditMode tests

- open `Ribbon`: число вершин/индексов, upward normals, U 0..1, V по метражу;
- `Rectangle`: outward winding, hard edges, корректный perimeter U seam, caps on/off;
- `HalfPipe`: внутренняя гладкая поверхность, корректные края;
- custom open/closed profile, reversed winding, duplicate points, zero-length profile;
- closed spline: геометрический seam, UV seam, normal/roll continuity;
- width/height/twist curves, включая closed endpoints и taper-to-zero policy;
- mixed valid/invalid spline list с детерминированным порядком;
- длина 0, очень короткий spline и Step больше длины;
- terrain bilinear sample, terrain offset, edge/outside policy и rolled profile semantics;
- фиксированный input после cache clear/rebind даёт тот же ordered `MeshInstanceData` snapshot.

### Materialization/Unity workflow tests

- preview -> Result Generate не создаёт дубль;
- disconnect/invalid/disable очищает old owned objects;
- cancel во время geometry и во время materialization не оставляет partial objects и не помечает версию успешной;
- `Collider = false/true`, physics raycast, cooking budget;
- save/reopen/domain reload/build stripping;
- non-identity parent transform;
- normal-mapped material получает корректные tangents;
- package import и node catalog metadata без diagnostics.

### Performance validation

- benchmark matrix из раздела P1 с profiler markers для snapshot, geometry, upload, normals/tangents, collider cooking и cleanup;
- отдельно cold Generate, warm Generate и continuous edit;
- численные budgets фиксируются в ТДД после baseline и становятся критерием приёмки;
- cancellation latency измеряется на largest supported chunk, а не формулируется как «без зависших буферов».

---

## Что в ТДД уже сделано хорошо

- Правильно выбран отдельный package boundary и `PcgNodeLibrary` вместо добавления sweep-логики в ядро.
- Выход через общий `MeshInstanceData` и материализация через `IInstanceMakerContainer` соответствуют архитектуре PCG4U; второй instancing pipeline не создаётся.
- Зависимый core-контракт `MeshInstanceData.Collider` уже присутствует в живом исходнике и обновлённой DLL; maker использует один mesh для `MeshFilter` и `MeshCollider`.
- `MeshInstanceMaker` уже выставляет `IndexFormat.UInt32`, поэтому искусственно дробить меш только ради 65k-индексов не требуется.
- Сэмплирование по distance и `V = distance * UvScale` — правильная основа стабильного продольного tiling.
- Terrain sampling `[z, x]` и bilinear interpolation выбраны правильно как базовый метод.
- Идея main-thread spline/terrain snapshot и CPU geometry в worker pool верна; требуется довести её до действительно immutable контракта.
- Индексированные result slots — правильная основа детерминированного порядка после параллельных batch; нужно только сначала компактно отфильтровать невалидные входы.
- Пересечения и T/X-junction дорог разумно не включены в sweep package и должны остаться отдельной задачей.

## Минимальный набор правок исходного ТДД

Перед реализацией необходимо:

1. Исправить `package.json` и добавить чистый install/compile gate.
2. Сделать `Sweep Spline` самостоятельной нодой с inline default profile; оставить `ProfileNode` optional override.
3. Зафиксировать validated immutable `SweepProfile` contract, custom closed/winding policy и понятный authoring UX.
4. Перепроектировать topology для outward closed surfaces, U/V seam duplication, hard/smooth normals, tangents и caps.
5. Отфильтровывать invalid splines до создания индексированных slots; задать min rings и exact Step semantics.
6. Провести все empty/error/cancel ветки через единый scene synchronization contract без stale/partial objects.
7. Снять полный immutable snapshot до worker pool и запретить worker-доступ к `Data`, `Spline`, `AnimationCurve`, `TerrainData` и другим Unity objects.
8. Добавить terrain content version/invalidation для backing value и connection path.
9. Ограничить full-heightmap, managed allocations, concurrency и mesh size; измерять весь materialization pipeline.
10. Добавить depth/twist/caps либо явно ограничить документ до P2.1/P2.2 и не закрывать всю программу P2.
11. Зафиксировать frame continuity, terrain bounds/drape и world/local coordinate semantics.
12. Проверить scene serialization, domain reload и player build после PCG stripping.
13. Добавить `[PcgMemberInfo]`, EditMode tests, package documentation и обязательное обновление `PROJECT_MAP`.
14. Заменить субъективное «не фризит» численными performance/cancellation budgets после baseline.

После этих изменений решение будет соответствовать идее PCG4U как node-centric Unity-native продукта, сохранит привычный Generate/Clear/Preview workflow и сможет безопасно использоваться не только в одной демке, но и в реальных дорожных, стеновых и river/waterfall графах.
