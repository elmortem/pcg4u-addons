Status: Выполнено

# Sweep — junction-патч «плита по контуру» и отдельные объекты патчей — Agent Execution Spec

Замена схемы junction-патча целиком. Текущая схема «сквозная полоса + лофты веток + клинья» дефектна по построению: маппинг колонок полосы только по `x` схлопывает объёмные профили (у Rectangle все точки имеют `x = ±half` — торец полосы вырождается в донную линию), лофт ветки без террейна имеет 2 кольца и сжимается в вертикальную колонку за один ряд квадов (клампы применяются только к несуществующим промежуточным кольцам), кромки полосы и колонки клиньев берутся по одиночному экстремальному индексу профиля (зигзаг и дубль стенок). Новый патч — одна замкнутая «плита»: контур из торцевых колец рукавов и Безье-кромок между углами, ear-clipping верхнего и нижнего листов в плане, боковые ленты вдоль кромок. Отсутствие дыр, нахлёстов и неверного виндинга гарантируется построением: один контур — одна триангуляция. Дополнительно каждый junction становится отдельным `MeshInstanceData` вместо общей склейки `MergePatches`.

## References (not inlined)

- Конвенции и запреты: `CLAUDE.md` репозитория; принципы: `Docs/DESIGN_PRINCIPLES.md`.
- Скилл проверок: `unity-bridge`.
- Текущий код: `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepJunctionMeshBuilder.cs` (заменяется), `SweepNetworkSolver.cs`, `SweepNetworkArm.cs`, `SweepNetworkJunction.cs`, `SweepSplineNodeExecutor.cs`, `SweepMeshBuilder.cs` (`Cleanup`, `ExtractOutline`, `MapOutlineToProfile` переиспользуются).

## Foundations (shared, used across units)

- Контракты кадров кусков не меняются: `SweepFrame.T` — глобальный normalized-параметр исходного сплайна, `SweepFrame.Distance` — локальная дистанция куска; ленты кусков и солвер-сплит с setback-математикой не меняются.
- Кольцевая вершина рукава: формула `LoftEdgeVertex(arm, j, ...)` текущего билдера сохраняется бит-в-бит (`MakeVertex` с XZ-проекцией right в террейн-режиме) — торцевые вершины патча обязаны совпадать с торцом ленты ≤ 1e-4.
- Базис junction: `Center/Axis/E1/E2` из солвера без изменений; `(E1, E2, Axis)` — правая тройка, CCW-контур в координатах `(E1, E2)` даёт нормаль `+Axis` при порядке треугольника «как обошли».
- Разбор профиля — новый тип `SweepProfileChains` (отдельный файл `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepProfileChains.cs`, `internal sealed class`), строится один раз на `Build` из профиля снапшота:
  - Поля: `public int[] UpperChain; public int[] LowerChain; public int[] RightColumn; public int[] LeftColumn; public bool Closed;` — везде индексы точек профиля.
  - Открытый профиль (`ProfileClosed == false`): `UpperChain = [0..vpr-1]` по порядку, `LowerChain = null`, `RightColumn` — один индекс: конец полилинии (`0` или `vpr-1`) с большим `x`, при равенстве — `vpr-1`; `LeftColumn` — другой конец. Лент (bands) и нижнего листа нет.
  - Закрытый профиль: контур через `SweepMeshBuilder.ExtractOutline` + `SweepMeshBuilder.MapOutlineToProfile` (сделать `internal`), нормализованный к CCW (если `SignedArea < 0` — реверс; функцию площади продублировать приватно в `SweepProfileChains`). `tolX = 1e-3 * max(max|x| профиля, 1e-4)`. Правый ран — максимальный циклически-непрерывный отрезок контура с `x >= xmax - tolX`, содержащий вершину с максимальным `x` (при нескольких кандидатах — первый по индексу контура); левый ран симметрично с `x <= xmin + tolX`. `RightColumn`/`LeftColumn` — вершины рана, смапленные в индексы профиля, отсортированные по возрастанию `y`, дедуп по `y` с `tolY = 1e-3 * max(max|y| профиля, 1e-4)`. `UpperChain` — CCW-путь контура от верхнего конца правого рана до верхнего конца левого рана (индексы профиля); `LowerChain` — CCW-путь от нижнего конца левого рана до нижнего конца правого рана.
- Ориентация рукава: `ccwIsMax` — существующая `CcwIsMax(...)`; CCW-колонка рукава = `ccwIsMax ? RightColumn : LeftColumn`, CW-колонка — противоположная.
- Обход торца рукава в контуре листа — всегда от CW-угла к CCW-углу (junction обходится CCW): верхний лист — `UpperChain` в сторону «от CW-колонки к CCW-колонке» (`ccwIsMax == true` → реверс хранимого порядка `UpperChain`, иначе хранимый порядок); нижний лист — `LowerChain` (`ccwIsMax == true` → хранимый порядок, иначе реверс).
- Кромка (rim) между рукавом `k` (CCW-сторона) и рукавом `(k+1)%n` (CW-сторона), отдельная на каждый «этаж» `e`: концы `Ae` — кольцевая вершина рукава `k` в CCW-колонке на слоте `e`, `Be` — кольцевая вершина рукава `k+1` в CW-колонке на слоте `e` (слот клампится `min(e, count-1)` своей колонки); контрольная точка `K` в плане — существующая `ControlPoint(plan(Ae), plan(Be), edgeA, edgeB, max(wA, wB))`, где `edgeA/edgeB` — `EdgeDir` рукавов, `wA/wB = lateralExtent * WidthMul`; кривая — квадратичная Безье в плане. Число сэмплов кромки `M` общее для всех этажей пары: `gamma = NormalizeGap(atan2-азимут plan(B_top) - atan2-азимут plan(A_top))`, `M = max(2, ceil(gamma / maxAngleRad) + 1)`; с террейном дополнительно `M = max(M, ceil(L / step) + 1)`, `L` — длина верхней Безье в плане по 16-сегментной аппроксимации.
- Вершина кромки на сэмпле `t`: `p2 = Bezier(plan(Ae), K, plan(Be), t)`; `pos = Center + p2.x * E1 + p2.y * E2 + Axis * lerp(dot(Ae - Center, Axis), dot(Be - Center, Axis), t)`; `rv = lerp(rvAe, rvBe, t)` (`rv` кольцевых вершин — из `MakeVertex`). Сэмпл `t = 0` — принудительно вершина `Ae`, `t = 1` — `Be` (бит-в-бит, чтобы `Cleanup` сварил с листами и торцами).

## Invariants (must hold throughout)

- Правки только в `Packages/PCG.Sweep/`; `PCG.Splines` и `Assets/Plugins/PCG4U` не трогаются; `*.meta` руками не правятся.
- Одиночный режим (без `Topology`) не меняется бит-в-бит; `SweepMeshBuilder.Build`, солвер-сплит, setback-математика и кадры кусков не меняются (в солвере меняется только состав `SweepNetworkArm`/`SweepNetworkJunction` и удаление through-пары).
- Ассерты bridge-задач не редактируются ради прохождения.
- Тяжёлое — в пуле; Unity API — только на главном потоке; в билдере каждые 1024 вершины/итерации — `ct.ThrowIfCancellationRequested()` + `reportProgress`.

## Execution Plan

Units run in listed order.

### Unit 1 — Плита по контуру

- Goal: `SweepJunctionMeshBuilder.Build` строит патч как два листа + боковые ленты по единому контуру; through-пары, лофты веток и клинья удалены; объёмные профили не схлопываются.
- Touch:
  - Новый файл `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepProfileChains.cs` — тип из Foundations со статическим методом `internal static SweepProfileChains Build(float2[] points, int[] segments, bool closed)`.
  - `SweepMeshBuilder.cs` — `ExtractOutline` и `MapOutlineToProfile` сменить с `private` на `internal`; больше ничего не менять.
  - `SweepNetworkArm.cs` — удалить `VAtCut`, `VSign`, `CornerCcw`, `CornerCw`, `EdgeDirCcw`, `EdgeDirCw`; добавить `public float3 EdgeDir;`.
  - `SweepNetworkJunction.cs` — удалить `ThroughA`, `ThroughB`.
  - `SweepNetworkSolver.cs` — удалить `AssignThroughPair`, константу `ThroughAngle`, вызов назначения пары; в `FillCorners` удалить вычисление угловых вершин и `CornerVertex`, оставить только заполнение `arm.EdgeDir` (прежняя формула `EdgeDirCcw`); убрать заполнение `VAtCut`/`VSign`.
  - `SweepJunctionMeshBuilder.cs` — переписать: удалить `BuildStrip`, `BranchAttach`, `NearestCrossing`, `BuildBranchLoft`, `BuildStarLoft`, `EmitLoftTriangles`, `BuildWedge`, `ComputeBisectors`, `ClampLateral`, `RayBisector`, `ClampStripEdge`, `RayPolyline`, `RaySegment`, `RaySegmentSigned`, `EdgeColumn`, `Bezier3`, `Bezier3Length`; сохранить `MakeVertex`, `SampleLut`, `LoftEdgeVertex`, `CcwIsMax`, `CornerVertex`, `ControlPoint`, `Bezier`, `Planar`, `PlanarUv`, `NormalizeGap`, `NormalizeSigned`; добавить `Bezier2Length(float2, float2, float2, int)` (16 сегментов).
- How:
  - `Build`: при `n == 0` вернуть `default`. Посчитать `SweepProfileChains`, на рукав — `widthMul/heightMul/twist/ccwIsMax` (как сейчас), для каждой пары соседних рукавов — `M` и контрольные точки этажей кромки.
  - Лист (верхний, и нижний при `Closed`): собрать контур в порядке CCW по азимуту: для каждого рукава — цепочка кольцевых вершин (правило обхода из Foundations), затем сэмплы своей кромки этажа «верх цепочки» при `t` от `0` до `1` без первой и последней точки (они равны угловым кольцевым вершинам). Для каждой вершины контура держать `plan(float2)`, `pos(float3)`, `rv(float)`; последовательные дубли в плане (`distancesq < 1e-10`) отбрасывать. Нижний лист использует нижние цепочки и нижние концы колонок.
  - Триангуляция листа — новый приватный `TriangulateLoop(List<float2> loop)`: нормализовать к CCW (реверс при отрицательной знаковой площади); `eps = 1e-7f * maxBBoxDim^2`; цикл: искать первое ухо `i` с `Cross(b - a, c - b) > -eps`, внутри которого нет ни одной другой вершины строго (`d1 > eps && d2 > eps && d3 > eps`); если ушей нет — принудительно клипать вершину с максимальным `Cross` (гарантия завершения, вырожденные треугольники отбросит `Cleanup`); порядок вершин треугольника — как обошли (CCW). Каждые 1024 проверок — `ct`/`reportProgress`.
  - Террейн-режим: после триангуляции листа — равномерное midpoint-подразбиение: `rounds = clamp(ceil(log2(maxPlanEdge / step)), 0, 6)`, каждый раунд делит каждый треугольник на 4 по серединам рёбер, середины общие через `Dictionary<(int, int), int>` с ключом `(min, max)`; `pos`, `plan`, `rv` — среднее концов.
  - Эмиссия листа: вершины + `PlanarUv(pos, ...)`; верхний лист — треугольники в порядке триангуляции (нормаль `+Axis`), нижний — в обратном порядке индексов каждого треугольника (нормаль `-Axis`). Открытый профиль — только один лист, порядок как у верхнего.
  - Ленты: только при `Closed` и обе колонки длиной ≥ 2. Для каждой пары рукавов: `cc = max(len(ccwColA), len(cwColB))`; сетка `w[M, cc]`: вершина `(m, e)` — формула кромки этажа `e` на сэмпле `t = m / (M - 1)`; UV: `U = ProfileUs[ccwColA[min(e, lenA - 1)]]`, `V = arcLen0(m) * uvScale`, где `arcLen0` — накопленная плановая длина нижней (`e = 0`) Безье по сэмплам. Треугольники: `(w[m, e], w[m+1, e], w[m+1, e+1])` и `(w[m, e], w[m+1, e+1], w[m, e+1])` — при `e` вверх и `m` по CCW нормаль наружу от центра.
  - Хвост `Build` без изменений: драпировка на террейн по `ry`, `SweepMeshBuilder.Cleanup`, возврат `SweepMeshData`.
- Gate: bridge-задача `Task_SweepP_U1` (солвер и билдер вызываются рефлексией по образцу задач `Task_SweepU_*`/`Task_SweepT_*`; параметры везде: `Step = 1`, `MaxStep = 8`, `MaxAngle = 5`, `UvScale = 0.25`, `SetbackScale = 1`, без террейна если не сказано иное):
  - Синтетика A — X-крест 90°, два прямых сплайна 60 м, Rectangle `w = 4`, `h = 0.5`: `PASS top` — есть ≥ 1 треугольник с `dot(нормаль, up) > 0.5` и у ВСЕХ таких треугольников все вершины `y ∈ [0.5 - 1e-3, 0.5 + 1e-3]`; `PASS bottom` — симметрично для `dot < -0.5` и `y ∈ [-1e-3, 1e-3]`; `PASS seams` — для каждого рукава каждая из 8 вершин торцевого кольца (формула `LoftEdgeVertex`) имеет вершину патча ближе 1e-4; `PASS walls` — у всех треугольников с `|dot(нормаль, up)| <= 0.2` центроид-нормаль наружу: `dot(нормаль, normalize(планарный центроид - центр)) > 0`; `PASS partition` — сетка 32×32 по баунду патча в плане: точки (вне 1e-3 от рёбер) покрыты не более чем одним треугольником верхнего листа; `PASS nohole` — 16 лучей из центра по азимутам в плане пересекают ≥ 1 треугольник верхнего листа; `PASS compact` — все вершины патча в плане ближе `(maxSetback + 4) * 1.15` к центру.
  - Синтетика B — X-крест 20°, Rectangle: `PASS compactSharp` — ближе `(maxSetback + 4) * 1.3`; `PASS partitionSharp`.
  - Синтетика C — Y-стык 0°/100°/200°, Rectangle: `PASS partitionY`, `PASS seamsY`.
  - Синтетика D — T-стык (сквозной 40 м + ветка в середину под 90°), Rectangle: `PASS partitionT`, `PASS seamsT`, `PASS topT` (верхний лист плоский на `h`).
  - Синтетика E — X-крест 90°, HalfPipe `w = 4`, `h = 1`: `PASS buildsHP` — вершины и треугольники > 0; `PASS seamsHP` — все 9 кольцевых точек каждого рукава ≤ 1e-4; `PASS partitionHP` — сеточная проверка по всем треугольникам патча ≤ 1.
  - Синтетика F — X-крест 90°, Ribbon: `PASS ribbon` — builds + seams + partition.
  - Синтетика G — валентность 2 (один сплайн 40 м, один кут, ветка удалена из топологии): `PASS plate2` — патч построен, seams обоих рукавов.
  - Синтетика H — T-стык из D на террейне 64×64: `PASS drape` — у каждой вершины листов `y - (высота террейна + heightOffset)` в пределах 1e-2 от `0` или от `0.5`; `PASS densityTerrain` — вершин листов больше, чем в прогоне D без террейна.
  - Компиляция чистая, `foreign_errors: false`; рефлексией — в `PCG.Sweep.SweepNetworkJunction` нет полей `ThroughA`/`ThroughB` (`PASS nothrough`).
- On failure: ≤ 3 итерации на гейт, затем стоп и отчёт; не возвращать through-пары, вееры к центру и биссектрисные клампы; ассерты не менять.

### Unit 2 — Отдельный объект на патч

- Goal: каждый junction — свой `MeshInstanceData`; `MergePatches` удалён.
- Touch: `SweepSplineNodeExecutor.cs` — удалить `MergePatches`; добавить `internal static void BuildJunctionResults(SweepMeshData[] meshes, string name, Material material, bool collider, List<MeshInstanceData> results, ref bool outOfBounds)`: пропуск `Vertices == null`, подсчёт построенных, имя `{name} Junction {j}` (`j` — индекс junction в исходном массиве) при количестве построенных > 1, иначе `{name} Junction`; вызывать из `ComputeNetworkAsync` вместо `MergePatches` с материалом `JunctionMaterial ?? Material`.
- How: только перечисленное; порядок результатов — ленты кусков, затем патчи по возрастанию индекса junction.
- Gate: bridge-задача `Task_SweepP_U2`: рефлексией вызвать `BuildJunctionResults` с тремя фейковыми мешами (второй с `Vertices == null`) — `PASS names` при результатах `X Junction 0`, `X Junction 2`; повторно с одним валидным мешем — `PASS single` при имени `X Junction`; `grep -n "MergePatches" Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` пусто, `grep -c "BuildJunctionResults" ...` ≥ 2 (объявление + вызов), лог в транскрипте.
- On failure: ≤ 3 итерации, затем стоп и отчёт.

### Unit 3 — Документация

- Goal: справка описывает плиту и отдельные объекты.
- Touch: `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — в Behavior сетевого режима: патч-«плита» (контур из торцов и Безье-кромок, верхний/нижний листы, боковые ленты), планарный UV листов и шов V на торцах патча, `U/V` лент по периметру профиля и длине кромки, объект на каждый junction (`{Name} Junction {i}`), ограничение: у профилей с невертикальным силуэтом наклон боковины между рукавами стягивается к одной кромочной кривой на лист. Убрать упоминания сквозной полосы/through.
- Gate: `grep -in "through" Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` пусто; `grep -c "Junction" ...` ≥ 3; лог в транскрипте.
- On failure: поправить и повторить.

## Done (/goal condition)

Выполнено, когда bridge-задачи `Task_SweepP_U1` и `Task_SweepP_U2` завершились `status: "success"` со всеми PASS-строками (вывод `wait-for-result.sh` в транскрипте), grep-проверки Unit 2 и Unit 3 дали указанные результаты, `foreign_errors: false` в последней задаче. Ограничения: правки только в `Packages/PCG.Sweep/`; при неподключенной `Topology` вывод ноды не изменён; `SweepMeshBuilder.Build`, солвер-сплит и setback не изменены; ассерты не редактировались. Стоп после 35 ходов или трёх подряд провалов одного гейта.

## End-of-run report (the agent does this when the goal is met or it stops)

- Смени Status в начале документа на `Выполнено`.
- Отчитайся: какие юниты закрыты, какие гейты потребовали повторов, на чём остановился и почему.
- Флаг — сам не делай: уточни у заказчика, нужно ли обновлять проектную документацию (`Docs/SWEEP_MAP.md`, справка нод) под эти изменения.
