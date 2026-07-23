# ТДД: Пакет PCG.Sweep — меш выметанием профиля вдоль сплайна

Status: Выполнено (пакет `Packages/PCG.Sweep`). Внешняя зависимость закрыта: правки `MeshInstanceMaker` (tangents, world-identity spawn) поставлены обновлением `PCG.dll` (2026-07-18) и присутствуют в шипнутой сборке.

Ревизия B — по ревью `260713-1508-REVIEW-sweep_package.md`: самостоятельная нода с инлайн-профилем, корректная топология закрытых профилей и швов, единый finalize-путь, полный immutable snapshot, terrain-версионирование, окно heightmap, twist/height/caps.

Scope: этап **P2.1** программы P2 из `ProjectPCG/Docs/notes/unreal_pcg_demos_plan.md` — `ProfileNode` + `SweepSplineNode` (дороги, тропы, стены, русла: UV по метражу, ширина/высота по кривым, twist, крышки, драпировка на террейн). `RegionExtrudeNode` (полигон → призма) — отдельный последующий ТДД P2.2; программа P2 не закрывается этим документом.

Зависимости — правки ядра (ProjectPCG; поставлены в `PCG.dll` 2026-07-18, подтверждены в шипнутой сборке):

- `MeshInstanceMaker`: после `RecalculateNormals()` вызывается `RecalculateTangents()` — материалы с normal map требуют полный vertex stream (нужно всем меш-нодам, не только sweep). ✔
- `MeshInstanceMaker`: спавн world-identity — созданный объект получает мировые `position = 0`/`rotation = identity`/`localScale = 1 / lossyScale` независимо от трансформа `Parent`. ✔
- `MeshInstanceData.Collider` — в ядре (DLL 2026-07-13). ✔

---

## Структура пакета

```
Packages/PCG.Sweep/
├─ package.json
├─ Scripts/
│  ├─ PCG.Sweep.asmdef
│  ├─ PcgLibrary.cs
│  └─ Sweep/
│     ├─ ProfileShape.cs
│     ├─ SweepProfile.cs
│     ├─ SweepProfileBuilder.cs
│     ├─ ProfileNode.cs
│     └─ SweepSplineNode.cs
└─ Editor/
   └─ Scripts/
      ├─ PCG.Sweep.Editors.asmdef
      └─ Exec/
         ├─ ProfileNodeExecutor.cs
         ├─ SweepFrame.cs
         ├─ SweepSnapshot.cs
         └─ SweepSplineNodeExecutor.cs
```

- `package.json`: `name = "com.elmortem.pcg.sweep"`, `displayName = "PCG Sweep"`, `version = "0.0.1"`, `unity = "2022.3"`, `author = "Makar Osokin"`, `description` и `keywords` заполнены; `dependencies: { "com.unity.mathematics": "1.2.6", "com.unity.splines": "2.8.2" }` — asmdef и runtime-код используют Unity Splines, зависимость обязана быть задекларирована (по образцу PCG.Polygons/PCG.Splines).
- `Scripts/PCG.Sweep.asmdef`: references `PCG`, `Unity.Splines`, `Unity.Mathematics`, `UniTask`.
- `Editor/Scripts/PCG.Sweep.Editors.asmdef`: references `PCG`, `PCG.Editors`, `PCG.Sweep`, `Unity.Splines`, `Unity.Mathematics`, `UniTask`; `includePlatforms: ["Editor"]`. `PCG.Gizmos.Editor` не подключается (превью-гизмо у ноды нет).
- `Scripts/PcgLibrary.cs`: `[assembly: PcgNodeLibrary("com.elmortem.pcg.sweep", "PCG Sweep", ...)]`.
- Smoke-gate: чистый Unity 2022.3 проект + ядро PCG4U + только задекларированные зависимости — пакет компилируется и ноды видны в каталоге.

---

## ProfileShape

```csharp
public enum ProfileShape
{
	Ribbon,
	Rectangle,
	HalfPipe,
	Custom
}
```

## SweepProfile

Значение, текущее между нодами. Сегментная модель: рёбра профиля задаются явными парами индексов — это единый механизм для hard edges (дублированные вершины углов), UV-швов (дублированная seam-вершина) и произвольных открытых/закрытых форм.

```csharp
public sealed class SweepProfile
{
	public float2[] Points;
	public float[] Us;
	public int[] Segments;
	public bool Closed;

	public int GetContentHash();
}
```

- Пространство: X — вправо поперёк движения, Y — вверх. `Us` — U-координата каждой вершины. `Segments` — пары индексов `(a, b)`, длина чётная.
- Контракт нормалей: наружная нормаль сегмента — направление `a → b`, повёрнутое на +90° в плоскости профиля (`(-dy, dx)`). Билдеры выпускают сегменты так, чтобы нормали смотрели наружу/вверх.
- `Closed` — топологически замкнутый контур (для `CapEnds`); сами swept-стороны определяются только `Segments`.
- `GetContentHash` — свёртка всех массивов и `Closed` (`(hash * 397) ^ x`, `unchecked`); инварианты (`Points.Length == Us.Length`, чётность `Segments`, валидные индексы) гарантирует билдер — тип создаётся только им.

## SweepProfileBuilder

`Scripts/Sweep/SweepProfileBuilder.cs`, статический. Единственная точка построения профиля — используется и `ProfileNodeExecutor`, и инлайн-дефолтом `SweepSplineNodeExecutor` (без дублирования).

`public static SweepProfile Build(ProfileShape shape, float width, float height, IReadOnlyList<Vector2> customPoints, bool customClosed, Action<string> warn)`

- `width = max(0.01f, width)`, `half = width * 0.5f`.
- `Ribbon`: `Points = { (-half, 0), (half, 0) }`, `Us = { 0, 1 }`, `Segments = { 0, 1 }`, `Closed = false`. Нормаль +Y.
- `Rectangle`: `height = max(0.01f, height)` (отрицательная высота недопустима — периметр и winding ломаются). 8 вершин — углы дублированы (hard edges): стороны низ/право/верх/лево обходом, дающим наружные нормали по контракту (низ −Y, право +X, верх +Y, лево −X); `Us` — накопленный периметр, нормированный на полный; seam — через дублированную пару первого угла (`U = 0` и `U = 1`). `Segments` — 4 пары. `Closed = true`.
- `HalfPipe` (жёлоб вниз, гладкий): 9 вершин `(-cos(πj/8) * half, -sin(πj/8) * height)`, `Us[j] = j / 8f`, `Segments` — 8 последовательных пар (вершины разделяются — сглаженные нормали), `Closed = false`.
- `Custom`: фильтрация невалидных точек (NaN/Infinity — отброс с warn), дедуп последовательных совпадающих (`distancesq < 1e-8`); меньше 2 валидных точек — fallback на Ribbon с warn. `customClosed = true` — нормализация обхода по signed area под контракт нормалей (при перевороте — warn) + дублированная seam-вершина (`U = 0/1`); `Us` — накопленная длина, нормированная (нулевая полная длина исключена дедупом). Вершины разделяются (гладкий), `Segments` последовательные.

## ProfileNode

```csharp
[Serializable]
[PcgNodeInfo("Builds a 2D sweep profile.",
	DisplayName = "Profile",
	Category = "Sweep",
	Tags = new[] { "sweep", "profile", "section" })]
public class ProfileNode : PcgNode
{
	[NodeEnum]
	public ProfileShape Shape = ProfileShape.Ribbon;
	[Input]
	public float Width = 4f;
	[Input]
	public float Height = 0.5f;
	public List<Vector2> CustomPoints = new();
	public bool CustomClosed;
	[Output]
	public SweepProfile Profile => default;
}
```

Все поля и выход — с `[PcgMemberInfo]`. Executor — `PcgSyncNodeExecutor<ProfileNode>`, `DoCompute` через `SweepProfileBuilder.Build`; `IsEmpty => Profile.Value == null;`.

## SweepSplineNode

Нода полноценна сама по себе (принцип независимости нод): встроенный профиль строится из собственных полей, порт `Profile` — опциональный override для переиспользования (`ProfileNode`). При подключённом порте инлайн-поля профиля скрываются рендерером ноды.

```csharp
[Serializable]
[PcgNodeInfo("Sweeps a 2D profile along splines and builds meshes.",
	DisplayName = "Sweep Spline",
	Category = "Sweep",
	Tags = new[] { "sweep", "mesh", "spline", "road" })]
public class SweepSplineNode : PcgPreviewNode
{
	public bool Enabled = true;
	[Input]
	public List<Spline> Splines = new();
	[Input(Connection = PcgConnectionType.Override)]
	public SweepProfile Profile;
	[NodeEnum]
	public ProfileShape Shape = ProfileShape.Ribbon;
	[Input]
	public float Width = 4f;
	[Input]
	public float Height = 0.5f;
	public List<Vector2> CustomPoints = new();
	public bool CustomClosed;
	[Input]
	public float Step = 1f;
	public AnimationCurve WidthByT = AnimationCurve.Constant(0f, 1f, 1f);
	public AnimationCurve HeightByT = AnimationCurve.Constant(0f, 1f, 1f);
	public AnimationCurve TwistByT = AnimationCurve.Constant(0f, 1f, 0f);
	public bool CapEnds;
	[Input]
	public float UvScale = 0.25f;
	[Input]
	public TerrainData Terrain;
	[Input]
	public Vector3 TerrainOffset;
	[Input]
	public float HeightOffset = 0.1f;
	[Input]
	public string Name = "Sweep";
	[Input]
	public Material Material;
	public bool Collider;
	[Output]
	public List<MeshInstanceData> Results => default;
}
```

Все поля и выход — с `[PcgMemberInfo]`. Семантика:

- `Step` — **максимальная** длина сегмента вдоль сплайна: `steps = max(1, (int)math.ceil(length / step))`; min `0.05`.
- `WidthByT`/`HeightByT` — множители X/Y профиля по нормированной длине; значение зажимается `max(0.001f, eval)` (taper к нулю без вырожденных треугольников, отрицательные запрещены).
- `TwistByT` — градусы поворота профиля вокруг тангенса; порядок применения на вершину: масштаб X/Y → twist-поворот в плоскости профиля → перенос в мировой кадр.
- `CapEnds` — торцевые крышки для закрытого профиля на открытом сплайне.
- `Terrain` пуст — вершины в локальном кадре сплайна; назначен — драпировка (см. режимы ниже). Нерелевантные поля скрываются рендерером (например `Height` для `Ribbon`).

## SweepSplineNodeExecutor

`: PcgAsyncPreviewNodeExecutor<SweepSplineNode>, INodeInfo, IInstancesNode`; каркас материализации — по образцу `RegionToMeshNodeExecutor`, с исправлениями finalize/cancel ниже.

### Снапшот (главный поток, `OperationScope`)

Полный immutable snapshot — ни один background-метод не обращается к `Data`, `Spline`, `TerrainData`, `AnimationCurve`, `Material` (принцип «Параллелизм по умолчанию»; прецедент `DensityByCurveNodeExecutor` старее принципов и контрактом не является):

1. Профиль: `GetInputValue` порта, иначе `SweepProfileBuilder.Build` из инлайн-полей. Массивы профиля копируются в снапшот.
2. Компактный список валидных сплайнов в стабильном входном порядке: non-null, `Count >= 2`, finite `length > 1e-4`. Невалидные пропускаются с diagnostic. Слоты результата — индексы **компактного** списка: `null` в `Results` не бывает никогда (живой `MeshInstanceMaker.AddMesh` падает на `null`).
3. Кадры (`SweepFrame { Position, Tangent, Up, T, Distance }`): `steps = ceil(length / step)`; открытый — `steps + 1` колец (минимум 2), замкнутый — `steps` колец (минимум 3) **плюс** seam-кольцо в `distance = length` (геометрия совпадает с первым кольцом, `V = length * UvScale`) — иначе последний сегмент интерполирует V назад к нулю. Невалидный `Evaluate` (NaN) — сплайн отбрасывается с diagnostic.
4. Кривые `WidthByT`/`HeightByT`/`TwistByT` семплируются в LUT `float[256]` каждая.
5. Terrain (если назначен): `heightmapResolution`, `size`, и **окно** высот `GetHeights(x0, z0, w, h)` только по texel-области XZ-AABB всех кадров, расширенному на `maxProfileExtent * maxWidthMul + 1` texel (полная карта `4097²` — это ~64 МБ managed-копии на каждый compute при узкой полосе дороги). Маппинг окна фиксируется в снапшоте.
6. Скаляры: `Closed` каждого сплайна, `step`, `uvScale`, `heightOffset`, `capEnds`, `collider`, `name`. `Material` в снапшот не кладётся — присваивается в `MeshInstanceData` на editor-потоке при слиянии.

Тип `SweepSnapshot` (`Editor/Scripts/Exec/SweepSnapshot.cs`) — чистые данные.

### Геометрия (пул потоков)

Батч на сплайн: каждый батч — явный `UniTask.RunOnThreadPool(...)` (не «CPU-цикл до первого await»), результат в индексированный слот компактного списка, `await UniTask.WhenAll(tasks)`. Внутри батча каждые 1024 вершины — `ct.ThrowIfCancellationRequested()` + `PcgComputeSystem.ReportProgress(this)`.

- Кадр: `tangent = normalizesafe(...); right = normalizesafe(cross(up, tangent)); up = cross(tangent, right);` — реортогонализация up. Anti-flip: `dot(right_i, right_{i-1}) < 0` → инверсия right/up (непрерывность вдоль ленты); roll на замкнутом контуре — от knot rotations Unity Splines (непрерывен по построению) + тот же anti-flip.
- Вершина `j`: `p = профильная точка × (widthLut, heightLut)`, поворот на `twistLut` в плоскости профиля, затем:
	- без террейна (local frame): `vertex = framePos + right * p.x + up * p.y;`
	- с террейном (world-up режим, без гибрида local/world): `vertex.xz = framePos.xz + rightXZ * p.x` (right, спроецированный в XZ и перенормированный), `vertex.y = h(vertex.xz) + heightOffset + p.y;` `h` — билинейная интерполяция окна (`[z, x]`). Вершина вне окна/террейна — высота кадра без драпировки + однократный diagnostic warning на ноду (clamp к крайнему texel запрещён).
- Массивы точного размера: `vertices`/`uvs` — `ringCount * Points.Length`; `triangles` — `int[]` ровно `ringPairs * (Segments.Length / 2) * 6` (+ крышки), без `List<int>.ToArray()`.
- Треугольники: для пары колец `i`, `i1` и сегмента `(a, b)`:

```csharp
int ia = i * vpr + a;
int ib = i * vpr + b;
int ja = i1 * vpr + a;
int jb = i1 * vpr + b;
triangles[k++] = ia; triangles[k++] = ja; triangles[k++] = ib;
triangles[k++] = ib; triangles[k++] = ja; triangles[k++] = jb;
```

	Порядок согласован с контрактом нормалей профиля (для `Ribbon` — нормали вверх, для `Rectangle` — наружу; проверяется приёмкой back-face culling).
- `CapEnds` (закрытый профиль, открытый сплайн): контур профиля (уникальные вершины без seam-дубля) триангулируется ear clipping (выпуклые формы вырождаются в fan); передняя крышка — нормаль против тангенса первого кадра, задняя — по тангенсу последнего (порядок индексов зеркалится); вершины крышек — дублированные (hard edge), UV — XY профиля `* UvScale`.
- UV: `uvs[..] = new Vector2(Us[j], frame.Distance * uvScale);`
- Лимит: расчётное число вершин меша `> 2_000_000` — батч не строится, diagnostic error на ноде (вместо OOM).

### Finalize и материализация (editor-поток)

Единый finalize-путь для **всех** исходов — успех, пустой вход, невалидный профиль, отключённая нода:

- `Results.Value` публикуется одним присваиванием только после полной сборки всех мешей; пустой результат — пустой список (нода вычислена с пустым результатом), `IsEmpty => Results.Value == null || Results.Value.Count == 0;`.
- Синхронизация сцены выполняется при любом валидном результате, включая пустой: `RemoveInstances`, затем при непустом — `container.Begin(); await container.AddInstances(Address.ToKey(), null, Results.Value, ct); container.End();` в `try/finally` (`End` гарантирован). Ранний `return` без синхронизации запрещён: успешный пустой compute помечает версию сгенерированной (`ResultNodeExecutor` пишет `GeneratedVersions` по `IsComputed`), и старый меш навсегда остался бы в сцене как «актуальный».
- Отмена: до начала материализации — опубликованный выход и сцена не меняются (прежний валидный результат сохраняется); отмена внутри материализации — `finally` закрывает `End`, повторный trailing-резолв ядра доводит сцену до последней версии.
- `!Data.Enabled` → пустой результат через тот же finalize (эквивалент очистки).
- `MeshInstanceData`: `Name` (`name + " " + index` при нескольких), `Material`, `Vertices`, `Uvs`, `Triangles`, `Collider`.

### Версии

`GetVersionSalt()`: подмешивает `PcgTerrainContentVersion.Get(terrain)` для **разрешённого** `TerrainData` (`GetInputValue` — и прямое поле, и связь/пилюля): `PcgNodeDescriptor` хеширует Unity-объекты по `GetInstanceID()`, правка heightmap кистью иначе не инвалидирует ноду и не будит Auto Generate. Плюс `GetContentHash()` подключённого/встроенного профиля и хеш ключей всех трёх `AnimationCurve`.

`HasNodeInfo`/`NodeInfo` — `"Meshes: N, Triangles: M"`. `DrawPreview` — пустой (результат — реальные объекты сцены). `ClearInstancesAsync` — калька `RegionToMeshNodeExecutor`.

---

## Порядок реализации

- Правки ядра (tangents, world-identity spawn) + DLL.
- Каркас пакета (package.json, asmdef, PcgLibrary).
- `ProfileShape`, `SweepProfile`, `SweepProfileBuilder`, `ProfileNode` + executor.
- `SweepSplineNode` + executor (снапшот → геометрия → finalize).
- Документация (см. Done).

## Критерии приёмки

Геометрия и топология:

- Открытый сплайн + `Ribbon` → лента, видимая сверху; U поперёк 0..1, V тайлится по метражу; текстура не плывёт при правке сплайна.
- `Rectangle` — призма с наружными нормалями (при back-face culling видна снаружи со всех сторон), жёсткими рёбрами и корректным U-швом (без растяжения на замыкающей стороне); `CapEnds = true` закрывает торцы.
- `HalfPipe` — гладкий жёлоб; `Custom` open/closed с произвольным winding нормализуется корректно.
- Замкнутый сплайн — кольцо без геометрического и без UV-шва (seam-кольцо).
- `WidthByT`/`HeightByT` независимо масштабируют профиль; taper к нулю не даёт NaN-нормалей; `TwistByT` вращает профиль (рампа 0→360° на прямом сплайне даёт видимый виток).
- Материал с normal map корректен (tangents после правки ядра).
- С `Terrain` лента облегает рельеф; участок за границей террейна сохраняет высоту сплайна и выдаёт warning; правка террейна кистью инвалидирует ноду и Auto Generate без ручного толчка.
- Non-identity `Parent` у instance maker: полотно в тех же мировых координатах (после правки ядра).

Workflow:

- Нода строит результат сразу после подключения сплайна — без обязательной второй ноды.
- Смешанный вход `valid/null/one-knot/zero-length/valid` → два меша, порядок сохранён, материализация не падает.
- Отключение `Profile`-связи, невалидный custom-профиль, пустой вход, `Enabled = false` — старые объекты сцены удаляются; повторный Generate не считает старый результат актуальным.
- Generate/Clear через `Result` создаёт и полностью удаляет объекты; двойного спавна при активном превью нет; save/reopen сцены сохраняет меш/материал/коллайдер.
- `Collider = true` — `MeshCollider` на объекте.
- Непрерывная правка сплайна/профиля/кривых во время compute → отмена + trailing recompute, финальный результат целостен и соответствует последней версии; частичных объектов в сцене нет.

Перформанс (fixtures): пакет функционально принят и работает; перф-baseline (числа в таблице ниже) вынесен в отдельную работу по качеству и не блокирует закрытие ТДД.

| Сценарий | Варианты | Метрики |
|---|---|---|
| 20 × 1 км | Ribbon / HalfPipe, Step 1 / 0.5 | wall time, max main-thread stall, аллокации |
| Terrain | без / 1025 / 4097 | размер и время снапшота окна |
| Collider | off / on | upload, cooking, cancel latency |
| Editing | одиночная правка / continuous drag | trailing-результат, stale-объекты, пики кадра |

## Done-состав

- Смени статус в начале документа на `Выполнено`.
- `[PcgMemberInfo]` на всех полях/выходах обеих нод; `PcgNodeCatalog` без diagnostics (`MetadataComplete`).
- `Documentation~` пакета (обе ноды, режимы, профили), обновление `Docs/PROJECT_MAP.md`.
- Зафиксируй ревизию ядра/DLL с правками maker'а.
