# Карта проекта PCG4U Addons

Навигация по репозиторию: где что лежит, ключевые классы, базовый API ядра и ссылки на карты аддонов.

> Это индекс: общая структура, API ядра и подсистемы. Инвентарь нод и типов каждого аддона — в отдельном `<ADDON>_MAP.md` (раздел 4).
>
> Обновляй этот файл при изменении общей структуры (новые папки/подсистемы, новый аддон). При изменении конкретного аддона правь его `<ADDON>_MAP.md`.

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
│  ├─ Examples/                   ← демо-сцены проекта; актуальная — CityForestV4 (см. её README.md)
│  ├─ ThirdParty/                 ← сторонние паки (CC0), у каждого SOURCE.md + License.txt
│  ├─ Scenes/SampleScene.unity    ← рабочая сцена проекта
│  ├─ Settings/                   ← HDRenderPipelineAsset
│  ├─ Resources/Memcpy.compute    ← compute-шейдер (используется BRG-инстансингом)
│  └─ HDRPDefaultResources/
├─ Packages/
│  ├─ PCG.BRG/                    ← аддон: BatchRendererGroup-инстансинг
│  ├─ PCG.Mazes/                  ← аддон: графы и лабиринты
│  ├─ PCG.Octree/                 ← аддон: пространственный поиск точек через Octree
│  ├─ PCG.Polygons/               ← аддон: 2D-полигоны/регионы + городские ноды (Scripts/City/)
│  ├─ PCG.Splines/                ← аддон: работа со сплайнами Unity.Splines
│  ├─ PCG.Sweep/                  ← аддон: меш выметанием 2D-профиля вдоль сплайнов
│  ├─ PCG.SpriteShapes/           ← аддон: 2D SpriteShape вдоль сплайнов
│  ├─ CoworkBridge/Editor/        ← ядро моста «выполни C# в Editor» (исходники)
│  ├─ com.unity.render-pipelines.high-definition-config/  ← локальная копия HDRP-конфига
│  └─ manifest.json               ← зависимости проекта
├─ Docs/                          ← документация проекта
│  ├─ PROJECT_MAP.md              ← этот файл (индекс: ядро + ссылки на карты аддонов)
│  ├─ <ADDON>_MAP.md              ← карта по каждому аддону (SPLINES_MAP, POLYGONS_MAP, …)
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
    [Input] public PcgPointCloud Points = new();
    public float Radius = 1f;              // параметр
    [Output] public PcgPointCloud Results => default;
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
- Получение входов: `GetInputValue(nameof(Data.Field), Data.Field)` (скаляр), `GetInputValues(...)` / `GetInputPort(name).GetInputValues()` (массив значений со всех подключённых связей). Источник, помеченный `PcgValue.IsArray` (массив-значение — напр. переменная точек/сплайнов/регионов в сабграфе), на мульти-входе разворачивается в несколько элементов; `GetInputValue` (одиночный) берёт первый.
- Превью: `GetGizmosOptions()` → `GizmosOptions` (цвет и пр.), `GizmosUtility.DrawPoints(...)`.

Опциональные интерфейсы исполнителя (UI-инфо/переключение превью):
- `INodeInfo` — `HasNodeInfo`, `NodeInfo` (строка в шапке ноды, напр. «Objects: N / M»).
- `IShowResults` / `IPointsCount` / `IShowCenterPoints` — переключатели того, что показывать в превью.

### 3.3 Кооперативная асинхронность — `OperationScope` (namespace `PCG.Utilities`)
- `using (var scope = OperationScope.Start(this)) { ... await scope.Step(ct: ct); }`
- `scope.Step()` — точка кооперативного прерывания/прогресса/отмены внутри тяжёлых циклов.
- Тяжёлые pure-data вычисления выполняются через общий ограниченный `PcgWorkerScheduler`; Unity API остаётся на editor thread, снимает immutable-снапшот и квантуется через `OperationScope`.

### 3.4 Типы точек и инстансов
- `PCG.Points.PointData` — единица размещения: `Position` (Vector3), `Normal` (Vector3), `Angle` (float, вокруг Normal), `Scale` (float), `Density` (float, [0..1]).
- `PCG.Points.PcgPointCloud` — тип порта точек: `List<PointData> Points` (геометрия) + `PcgAttributeSet Attributes` (именованные колонки, строка на точку). Инвариант: `Attributes.Count == Points.Count`. `List<PointData>` остаётся только у внутренних генераторов/накопителей (см. «Правило категорий» ниже) — портов `[Input]`/`[Output]` он больше не типизирует.
  - API: `cloud[i]` (get/set), `cloud.Count`, `foreach` по точкам, `cloud.Points` (сырой список), `dst.AppendFrom(src, srcIndex)` (перенос точки с атрибутами), `dst.AppendFrom(src, srcIndex, modifiedPoint)` (перенос с заменой точки), `dst.Append(srcCloud)` (слияние облаков), `dst.Add(point)` (новая точка без источника), `Results.Rent(capacity)` (аренда из пула).
  - **Правило категорий** (какой метод сборки выхода использовать): **Generator** (точки из не-точечного входа) — `Add`; **Derived-select** (выход — подмножество входа) — `AppendFrom(src, i)`; **Derived-transform** (те же точки, изменённые) — `AppendFrom(src, i, modified)`; **Merger** (несколько входов в один) — `Append(src)`; **Consumer** (точки только на входе) — тип входа меняется, выход не точки; **Internal** (локальная переменная/параметр утилиты, не порт) — остаётся `List<PointData>`. Использовать `Add` в Derived-ноде — дефект.
  - Эталон миграции Derived-select с переносом атрибутов и параллельным счётом: `Assets/Plugins/PCG4U/PCG/HDRP/Scripts/SelectPoints/PointsByWaterSurfaceNode.cs` + `.../Editor/SelectPoints/PointsByWaterSurfaceNodeExecutor.cs` (ядро, открытый исходник).
- `PCG.Splines.PcgSplineSet` — тип порта сплайнов (аддон `PCG.Splines`): `List<Spline> Splines` (геометрия) + `PcgAttributeSet Attributes` (строка на сплайн). Инвариант: `Attributes.Count == Splines.Count`. Правило категорий то же, что у точек (Generator — `Add`; Derived-select — `AppendFrom(src, i)`; Derived-transform — `AppendFrom(src, i, newSpline)`; Derived-fanout — `AppendFrom(src, i, piece)` на кусок; Merger — `Append(src)`; Internal — остаётся `List<Spline>`). Пула для сплайнов нет: `Results.Rent(...)` на сплайновых выходах не применяется. Разделение «канал или атрибут»: переменное вдоль сплайна живёт во встроенном канале Unity (`pcg.width`), постоянное на сплайн — в `Attributes`. Подробности и таблица «какая нода какие атрибуты пишет» — в [`SPLINES_MAP.md`](SPLINES_MAP.md).
- `PCG.Polygons.RegionSet` — тип порта регионов: `List<Polygon2D> Regions` + `PcgAttributeSet Attributes` (строка на регион).
- **Кеш значений.** `PCG.Cache.PcgCacheSerializerRegistry` держит `IPcgCacheSerializer` по `TypeId`. Занятые номера: `1`, `3` — ядро (`PcgPointCloudSerializer`, `PointListSerializer`), `2` — `RegionSetSerializer` (PCG.Polygons), `4` — `PcgSplineSetSerializer` (PCG.Splines). Регистрация — из `[InitializeOnLoadMethod]` в bootstrap-классе аддона. Для `SplineNetworkTopology` сериализатора нет, поэтому `SplineIntersectionNode` не кешируется.
- `PCG.Points.GeneratePointMode` — Surface/Volume × Regular/Random.
- `PCG.Points.ChangeDensityMode` — Set/Add/Mult (как менять плотность).
- `PCG.Instances.InstanceData` — базовый тип «что породить» (наследуется аддонами).
- `PCG.Instances.GameObjectInstanceData` — `Prefab` + одиночная `Point` (ядро).
- `PCG.Instances.InstanceMakerBase` — «мейкер»: превращает `InstanceData` в объекты сцены.
  - `Begin()`, `async UniTask<bool> TryAdd(ownerKey, groupName, IEnumerable<InstanceData>, ct)`, свойство `Parent`.
  - Паттерн: `if (data is МойInstanceData) { ... } else return false;` (мейкер берёт только свой тип).

### 3.5 Значения графа — `PcgValue` (namespace `PCG.Values`)
- `PcgValue` — обёртка ассета/данных для прокидывания в граф как переменной (методы вида `GetValue()`, `GetContentHash()`). Виртуальный `IsArray` (дефолт `false`) помечает тип как массив-значение: на границе сабграфа его порт становится мультивходовым и зеркалит несколько связей внутрь массивом, который внутренние ноды разворачивают фан-аутом. В аддонах помечены `SplinesValue` и `RegionSetValue` (в ядре — `PointsValue`).
- `PcgPortAdapter` — адаптер типов между несовместимыми портами (напр. `List<GameObject>` → `PcgSplineSet`, `PcgSplineSet` → `RegionSet`).
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

Подробная карта каждого аддона — в отдельном файле рядом с этим. Открывай только карту нужного аддона.

| Аддон | Карта | Кратко |
|---|---|---|
| **PCG.Splines** | [`SPLINES_MAP.md`](SPLINES_MAP.md) | Сплайны (Unity.Splines). Самый крупный аддон: генерация/поиск/склейка/оффсет/сглаживание сплайнов, точки вдоль и внутри контура, топология пересечений сети и точный разрез (`SplineNetworkTopology`, солверы), `SplinesValue`/`SplinesCache`. |
| **PCG.Mazes** | [`MAZES_MAP.md`](MAZES_MAP.md) | Графы и лабиринты. Сетка/Делоне → граф, MST-лабиринт (Прим), вычитание графов, граф → bezier-сплайны. Value-тип `Graph`. Зависит от PCG.Splines + Делоне. |
| **PCG.BRG** | [`BRG_MAP.md`](BRG_MAP.md) | Инстансинг через BatchRendererGroup. Группирует `GameObjectInstanceData` по префабам, батчи по 65k, `Memcpy.compute`. Зависит от `com.elmortem.brg`. |
| **PCG.SpriteShapes** | [`SPRITESHAPES_MAP.md`](SPRITESHAPES_MAP.md) | 2D SpriteShape вдоль сплайнов. Конверсия 3D-сплайна в 2D-контур `SpriteShapeController`. Зависит от PCG.Splines + Unity.2D.SpriteShape. |
| **PCG.Octree** | [`OCTREE_MAP.md`](OCTREE_MAP.md) | Пространственный поиск точек через Octree: разделение «есть/нет сосед в радиусе», батчи, параллельный самопоиск дублей. Зависит от `com.elmortem.octree` + Burst. |
| **PCG.Polygons** | [`POLYGONS_MAP.md`](POLYGONS_MAP.md) | 2D-полигоны и регионы: `RegionSet`/`Polygon2D` с рёберными атрибутами, бэкенд Clipper2, городские ноды (регион → кварталы → дороги/участки/точки), меш регионов на террейн. Мультивход `RegionSet`. |
| **PCG.Sweep** | [`SWEEP_MAP.md`](SWEEP_MAP.md) | Меш выметанием 2D-профиля (`SweepProfile`) вдоль сплайнов: дороги/тропы/стены/русла, UV по метражу, адаптивный шаг, трим складок, драпировка на террейн. `SweepSplineNode` с портом `Topology` — сетевой режим: свип сети сплайнов с патчами-«плитами» перекрёстков (setback + контур из торцов и Безье-кромок, ear-clipping листов). Зависит от PCG.Splines (топология сети, сплит). |

Ядровые типы `MeshInstanceData` / `MeshInstanceMaker` (используются PCG.Polygons и PCG.Sweep) живут в `PCG.Instances` (`PCG.dll`).

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
4a. **Длинные вычисления пампят прогресс.** Если тело ноды считается дольше 10 секунд, не жди его простым `await` от `PcgWorkerScheduler.RunAsync`: ядро убьёт ноду watchdog'ом «no progress for longer than 10 s». Бери `PcgWorkerScheduler.RunWithProgressAsync(this, Action, ct)` и `RunIndexedWithProgressAsync(this, count, Action<int>, ct)` — они пампят прогресс сами. Образцы: `RegionToMeshNodeExecutor` (этапы `Plan` / чанки / `Finish`), `LotFrontagePointsNodeExecutor`. **Generic-перегрузка `RunWithProgressAsync<T>(owner, Func<T>, ct)` в текущей сборке ядра рекурсивно зовёт сама себя и падает в `StackOverflowException`** — результат клади в захваченную локальную переменную через лямбду-**блок** `() => { x = F(); }`; лямбда-выражение `() => x = F()` возвращает значение и снова попадёт в сломанную перегрузку. Альтернатива без прогресс-хелперов — цикл ожидания с `PcgComputeSystem.ReportProgress(this)` и `await UniTask.Delay(250, cancellationToken: ct)`, пока `work.Status == UniTaskStatus.Pending`.
4b. **Тесты.** Новая нода = тесты в `Packages/PCG.X/Tests/Editor/` (asmdef `PCG.X.Tests` по образцу `PCG.Polygons.Tests`). Тестируй чистый солвер из `Scripts/`, а не исполнитель — исполнителю нужен живой граф. Прогон: `agentbridge tests --mode EditMode --assembly PCG.X.Tests`.
5. Новые типы данных инстансов наследуй от `InstanceData` + сделай `InstanceMakerBase` для материализации в сцену.
6. Соблюдай правила из `CLAUDE.md` (табы, public-поля с большой буквы без атрибута сериализации, кэш `GetComponent` полем, классы по отдельным файлам, без комментариев).
