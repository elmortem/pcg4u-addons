Status: Выполнено

# Sweep Network — объёмные junction-патчи — Agent Execution Spec

Новая нода `Sweep Network` (пакет PCG.Sweep): принимает сплайны и `SplineNetworkTopology` от `Spline Intersection`, режет сплайны на куски переиспользованным `SplineSplitSolver`, свипит куски с отступом (setback) от перекрёстков существующим `SweepMeshBuilder` (трим складок, квантовый шаг, драпировка работают как есть) и закрывает каждый перекрёсток объёмным патчем «радиальная звезда»: арм-лофты полного профиля к центральному кольцу плюс корнер-клинья краевых колонок по филлет-дуге. Работает для любого профиля: Ribbon даёт классический дорожный патч, Rectangle — стык стен с плоским верхом, HalfPipe — воронку-слияние русел.

## References (not inlined)

- Конвенции кода и запреты: `CLAUDE.md` репозитория (табы, типы по отдельным файлам, без комментариев, сериализуемые поля public PascalCase, meta-файлы руками не менять).
- Принципы проектирования: `Docs/DESIGN_PRINCIPLES.md`.
- Скилл для всех проверок в Unity Editor: `unity-bridge` (задачи в `Assets/Editor/CoworkBridge/`, ожидание через `wait-for-result.sh`).
- Эталоны кода: `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` (структура executor, SyncScene, версии), `Packages/PCG.Splines/Editor/Scripts/Exec/SplitSplinesNodeExecutor.cs` (использование сплит-солвера).

## Foundations (shared, used across units)

Существующие типы, на которые опирается работа:

- `PCG.Sweep.SweepProfile` — `Points: float2[]`, `Us: float[]`, `Segments: int[]`, `Closed: bool`, `GetContentHash()`. Файл `Packages/PCG.Sweep/Scripts/Sweep/SweepProfile.cs`.
- `PCG.Sweep.SweepSnapshot`, `SweepFrame`, `SweepMeshBuilder.Build(snapshot, splineIndex, ct, reportProgress) → SweepMeshData`, `SweepTerrainWindow` — `Packages/PCG.Sweep/Editor/Scripts/Exec/`.
- `PCG.Splines.SplineNetworkTopology` — `Junctions: List<SplineJunction>` (`Position: float3`, `Valency: int`), `Cuts: List<SplineCut>` (`SplineIndex`, `CurveIndex`, `CurveT`, `Distance`, `Position`, `JunctionIndex`), `GetContentHash()`. Runtime-сборка `PCG.Splines`.
- `PCG.Splines.Utilities.SplineNetworkInput.Flatten`, `SplineSnapshot.Capture`, `SplineSplitSolver.Solve(snapshots, cuts, points, snapDistance, ct, progress) → SplineSplitResult` — `Packages/PCG.Splines/Editor/Scripts/Network/`. Порядок flatten совпадает с `SplineIndex` кутов. Куски одного сплайна возвращаются в порядке возрастания дистанции; closed-сплайн с C резами даёт C открытых кусков от реза k до реза (k+1) mod C.

Контракт согласованности: кольцо арм-лофта с индексом 0 обязано бит-в-бит совпадать с торцевым кольцом ленты куска (те же формулы вершин, тот же frame) — стык лента/патч без щелей гарантируется построением, а не сваркой.

## Invariants (must hold throughout)

- Изменяются только файлы внутри `Packages/PCG.Sweep/` и два asmdef-референса; пакет `PCG.Splines` не редактируется вовсе.
- `*.meta` файлы руками не создаются и не правятся (Unity генерирует сам).
- Публичное поведение `SweepSplineNode` не меняется: сигнатура `SweepMeshBuilder.Build` сохраняется, единственное допустимое изменение его контракта — замена `SweepSnapshot.CapEnds` на пофайлово описанные в Unit 1 флаги и доступность `Cleanup` внутри сборки.
- Ассерты в валидационных bridge-задачах данного документа не редактируются ради прохождения; при провале правится реализация.
- Все тяжёлые вычисления — в пуле потоков; Unity API (`Spline.Evaluate*`, `TerrainData`) — только на главном потоке при снятии снапшота; каждые 1024 итерации — `ct.ThrowIfCancellationRequested()` и `reportProgress()`.

## Execution Plan

Units run in listed order.

### Unit 1 — Пер-концевые капы и asmdef-зависимости

- Goal: `SweepSnapshot` несёт капы по концам каждого сплайна, старое поведение `SweepSplineNode` сохранено, PCG.Sweep видит PCG.Splines.
- Touch:
  - `Packages/PCG.Sweep/Scripts/PCG.Sweep.asmdef` — в `references` добавить `"PCG.Splines"`.
  - `Packages/PCG.Sweep/Editor/Scripts/PCG.Sweep.Editors.asmdef` — добавить `"PCG.Splines"` и `"PCG.Splines.Editor"`.
  - `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSnapshot.cs` — поле `public bool CapEnds;` заменить на `public bool[] CapStartFlags;` и `public bool[] CapEndFlags;` (индекс = индекс сплайна в `Frames`).
  - `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshBuilder.cs` — `applyCaps` разделить: `bool applyFront = snapshot.CapStartFlags[splineIndex] && snapshot.ProfileClosed && !splineClosed;` и симметрично `applyBack` из `CapEndFlags`; аллокация cap-вершин и треугольников — по сумме включённых сторон; фронт-кап строится при `applyFront`, бэк-кап при `applyBack` (сейчас оба строятся вместе — разнести циклы). Модификатор `private static void Cleanup` заменить на `internal static void Cleanup`.
  - `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` — в `BuildSnapshot` заполнять оба массива значением `Data.CapEnds` на каждый сплайн.
- How: правки строго перечисленные; вершинная раскладка капов: при одном включённом капе используется тот же блок `outline.Count` вершин (front — с индекса `ringCount * vpr`, back — следом за фронтом, если фронт включён, иначе с `ringCount * vpr`).
- Gate: bridge-задача `Task_SweepNet_U1`: рефлексией проверить — `PCG.Sweep.SweepSnapshot` содержит поля `CapStartFlags` и `CapEndFlags` и не содержит `CapEnds`; `SweepMeshBuilder` содержит метод `Cleanup` (internal static); сборка `PCG.Sweep.Editor` в списке референсов имеет `PCG.Splines.Editor` (проверить компиляцией: задача со `using PCG.Splines;` и `typeof(SplineNetworkTopology)` внутри — компиляция bridge-задачи не проверяет asmdef, поэтому вместо этого рефлексией найти тип `PCG.Splines.SplineNetworkTopology` и залогировать `PASS asmdef` при ненулевом `Assembly`). Далее функциональный ассерт: построить в задаче прямой `Spline` из 2 узлов длиной 10, собрать `SweepSnapshot` (Ribbon 2 точки, `Step`-кадры каждые 1 м — 11 кадров руками через `EvaluatePosition/Tangent/UpVector`, LUT из констант 1/1/0, `CapStartFlags/CapEndFlags = {false}`), вызвать `SweepMeshBuilder.Build` и залогировать `PASS build <vertexCount> <triangleCount>` при `vertexCount == 22` и `triangleCount == 60` (20 квадов по 3 индекса). Статус success и все три `PASS` в логах.
- On failure: ≤3 итерации правок; чужие ошибки компиляции (`foreign_errors: true`) — остановиться и доложить; ассерты не менять.

### Unit 2 — Типы данных сети

- Goal: чистые data-типы сети компилируются и видны рефлексии.
- Touch (новые файлы в `Packages/PCG.Sweep/Editor/Scripts/Exec/`):
  - `SweepNetworkArm.cs` — `public sealed class SweepNetworkArm`: `public int PieceIndex; public bool AtPieceStart; public float Azimuth; public float3 Outward; public SweepFrame Frame; public float3 Right; public float3 Up; public float VAtCut; public float VSign; public float CornerRadiusCcw; public float CornerRadiusCw;`
  - `SweepNetworkJunction.cs` — `public sealed class SweepNetworkJunction`: `public float3 Center; public float3 Axis; public float3 E1; public float3 E2; public SweepNetworkArm[] Arms;` (Arms отсортированы по возрастанию Azimuth).
  - `SweepNetworkSnapshot.cs` — `public sealed class SweepNetworkSnapshot`: `public SweepSnapshot Pieces; public SweepNetworkJunction[] Junctions; public float Step; public float MaxAngleRad; public float UvScale; public float HeightOffset; public bool Collider; public string Name; public Material JunctionMaterial;` (`using UnityEngine;`).
- How: только объявления, без логики.
- Gate: bridge-задача `Task_SweepNet_U2`: рефлексией найти все три типа в сборке `PCG.Sweep.Editor`, проверить наличие всех перечисленных полей по именам, лог `PASS types`.
- On failure: ≤3 итерации; ассерты не менять.

### Unit 3 — Солвер сети: сплит, привязка, setback

- Goal: статический солвер превращает (сплайны + топология + параметры) в куски-сплайны с диапазонами и `SweepNetworkJunction[]`, детерминированно.
- Touch: новый `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkSolver.cs` — `internal static class SweepNetworkSolver` c единственной публичной точкой:
  `internal static SweepNetworkSolveResult Solve(List<Spline> flatSplines, SplineNetworkTopology topology, float lateralExtent, float setbackScale, CancellationToken ct)` и новый файл `SweepNetworkSolveResult.cs`: `public sealed class SweepNetworkSolveResult { public List<Spline> PieceSplines; public float[] RangeStart; public float[] RangeEnd; public bool[] FreeStart; public bool[] FreeEnd; public SweepNetworkJunction[] Junctions; public int[][] JunctionArmPiece; }` (последнее поле не обязательно — если не нужно, не заводить).
- How:
  - Сплит: `SplineSnapshot.Capture` по каждому сплайну (вызывается с главного потока до `Solve`; сигнатуру `Solve` расширить параметром `SplineSnapshot[] snapshots`, а `flatSplines` использовать только для длин), `SplineSplitSolver.Solve(snapshots, topology.Cuts, пустой список точек, 0f, ct, null)`, куски собрать в `Spline` как в `SplitSplinesNodeExecutor` (Closed = false). Сплайны без резов проходят целыми.
  - Привязка концов: куты сгруппировать по `SplineIndex` и отсортировать по `Distance` (stable). Для открытого сплайна с K резами кусок k (0..K): начало привязано к куту k-1 (k>0), конец — к куту k (k<K). Для closed-сплайна с K резами кусок k: начало — кут k, конец — кут (k+1) mod K. Свободные концы (начало первого и конец последнего куска открытого сплайна, целые сплайны): привязать к ближайшему junction, если дистанция от конца до `Junction.Position` ≤ `lateralExtent`, иначе конец свободный (`FreeStart/FreeEnd = true`).
  - Junction-плоскость: `Axis = (0,1,0)`; `helper`-правило и `E1/E2` как в `TrimColumns` (`SweepMeshBuilder.cs`, строки с `helper`).
  - Рукав: для привязанного конца куска оценить frame куска на дистанции предварительного setback (см. ниже) — `Position/Tangent/Up` через Spline API куска; `Outward` = нормализованный тангенс, для конца-`start` направленный вперёд по куску, для конца-`end` — назад (`-tangent`); `Azimuth = atan2(dot(Outward,E2), dot(Outward,E1))`; `VSign` = `-1` для `start`-конца, `+1` для `end`-конца.
  - Setback: рукава junction отсортировать по Azimuth; для смежной пары с угловым зазором `γ` (по кругу, сумма зазоров = 2π) митра `m = γ >= PI ? 0 : lateralExtent / tan(γ/2)`, кламп `m = min(m, 6 * lateralExtent)`; setback рукава = `setbackScale * max(0.75 * lateralExtent, максимум митр с двух его сторон)`. Затем по кускам: если `RangeStart + (len - RangeEnd)` съедает более 0.9 длины куска — оба setback куска умножить на `0.9 * len / сумма`. Куски короче 0.05 — лента не строится и рукава не создаются (junction теряет этот рукав), одно `Debug.LogWarning` на вычисление.
  - Итерация: setback влияет на позицию торца, торец — на Azimuth незначительно; выполняется ровно два прохода: проход 1 — azimuth по кадру на дистанции 0 от junction-конца, расчёт setback; проход 2 — финальные frame/Azimuth/Right/Up на дистанции setback (basis по формуле `BuildBasis`: `right = normalizesafe(cross(up, tangent))`, `up = cross(tangent, right)`; для `end`-конца рукава right/up берутся от НЕинвертированного тангенса куска, чтобы совпасть с кольцом ленты). Второй пересчёт setback не выполняется.
  - `CornerRadiusCcw/Cw`: дистанции в плоскости (E1/E2) от `Center` до крайних боковых точек торцевого кольца (`x = max x` и `x = min x` профиля, вершины по формуле кольца); Ccw — та из двух, чей азимут лежит в зазоре против часовой к следующему рукаву, Cw — ко второму соседу.
  - `VAtCut`: для `start`-конца `0`, для `end`-конца `(RangeEnd - RangeStart) * uvScale`… uvScale в солвере не известен — хранить `VAtCut` в единицах дистанции (`0` либо `RangeEnd - RangeStart`), умножение на `UvScale` — в билдере патча.
  - Детерминизм: все сортировки stable с фиксированными компараторами; никаких `Dictionary`-обходов без сортировки.
- Gate: bridge-задача `Task_SweepNet_U3`: собрать синтетику — два прямых перпендикулярных сплайна длиной 40 с пересечением в центре (0,0,0), топологию руками: 1 junction (Position 0,0,0, Valency 4) и 4 кута (по 1 на середины обоих сплайнов — 2 кута, SplineIndex 0 и 1, Distance 20, JunctionIndex 0; валентность 4 достигается 2 кутами). Вызвать `SweepNetworkSolver.Solve` c `lateralExtent = 4`, `setbackScale = 1`. Ассерты в логах:
  - `PASS pieces` при 4 кусках;
  - `PASS attach` если у каждого куска ровно один конец привязан к junction 0 и 4 свободных конца всего;
  - `PASS setback` если все 4 setback равны `4/tan(45°) = 4 ± 0.05` (γ = 90°);
  - `PASS azimuth` если 4 азимута с шагом 90° ± 1°;
  - `PASS range` если у каждого куска `RangeStart` либо `len - RangeEnd` равно setback ± 0.01 со стороны привязанного конца.
- On failure: ≤3 итерации; если `SplineSplitSolver.Solve` имеет иную фактическую сигнатуру — прочитать `SplineSplitSolver.cs` и адаптировать вызов, ассерты не менять; после 3 неудач остановиться и доложить.

### Unit 4 — Кадры кусков с диапазоном и снапшот лент

- Goal: ленты кусков строятся с отступами от junction, капы только на свободных концах, кольцо торца воспроизводимо.
- Touch: `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkFrames.cs` — `internal static class SweepNetworkFrames`:
  - `internal static SweepFrame[] BuildRangeFrames(Spline spline, float rangeStart, float rangeEnd, float step, float maxStep, float maxAngleRad, int vpr, int maxVertices)` — квантовый марш из `SweepSplineNodeExecutor.BuildFrames`, перенесённый на диапазон: квантовая сетка `rangeStart + (rangeEnd - rangeStart) * q / quantCount`, `quantCount = max(1, ceil((rangeEnd - rangeStart)/step))`; `Distance` кадров — от `rangeStart` (т.е. `frame.Distance` = глобальная дистанция куска), `T = (distance - rangeStart)/(rangeEnd - rangeStart)`; марш и лимиты идентичны эталону.
  - `internal static SweepSnapshot BuildPieceSnapshot(...)` — собирает `SweepSnapshot` для всех кусков: `Frames` по кускам, `SplineClosed` все false, LUT — константные массивы (width/height = 1, twist = 0, длина 256), `CapStartFlags[i] = FreeStart[i] && capEnds`, аналогично End; `MaxLateralExtent`, `UvScale`, `HeightOffset`, `Terrain`, `Collider`, `Name` — из параметров.
- How: код маршрута копировать из эталона с заменой границ; UV `v = (frame.Distance - rangeStart) * UvScale`— проверить, что `SweepMeshBuilder` использует `frames[i].Distance` для V: чтобы V начинался с 0 на торце, при формировании кадров записывать в `Distance` значение `distance - rangeStart` (локальная дистанция), а глобальную не хранить — так `VAtCut` из Unit 3 согласован.
- Gate: bridge-задача `Task_SweepNet_U4`: на синтетике Unit 3 построить снапшот и все 4 ленты через `SweepMeshBuilder.Build`. Ассерты:
  - `PASS trim` — ни одна вершина лент в плане не ближе `setback - 0.01` к (0,0) (XZ-дистанция);
  - `PASS ring` — у куска 0 торцевое кольцо (кадр 0) ровно 2 вершины (Ribbon) на боковом ±4 от осевой, позиция центра кольца на дистанции setback от junction ± 0.01;
  - `PASS caps` — суммарное число вершин лент равно `Σ ringCount_i * 2` (капов нет: Ribbon открыт).
- On failure: ≤3 итерации, ассерты не менять, после — стоп и отчёт.

### Unit 5 — Билдер junction-патча (радиальная звезда)

- Goal: один junction превращается в меш «арм-лофты + корнер-клинья + центральное кольцо» без щелей к лентам.
- Touch: новый `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepJunctionMeshBuilder.cs` — `internal static class SweepJunctionMeshBuilder` с `internal static SweepMeshData Build(SweepNetworkSnapshot snapshot, int junctionIndex, CancellationToken ct, Action reportProgress)`.
- How (алгоритм финальный):
  - Обозначения: профиль `P_j = (x_j, y_j)`, `U_j = ProfileUs[j]`, junction `J` с `Center C`, `Axis A`, базис `E1/E2`, рукава по Azimuth.
  - Центральное кольцо: `CV_j = C + A * y_j`. В террейн-режиме все вершины патча строятся как у лент: XZ в плане, `y = базоваяВысота + y_j`, драпировка отдельной фазой в конце (семпл окна террейна, `y = h + HeightOffset + ry`); для этого билдер ведёт массив `ry` на вершину.
  - Арм-лофт рукава k: торцевое кольцо `R_j = Frame.Position + Right * x_j + Up * y_j` (террейн-режим: горизонтальный `rightXz`, как в `SweepMeshBuilder`). Число колец `L = max(2, (int)ceil(distance(C, Frame.Position)/snapshot.Step) + 1)`. Для `i = 0..L-1`: `t = i/(L-1)`, `s = 1 - (t*t*(3 - 2*t))`, центр `B_i = lerp(Frame.Position, C, t)`, вершины `V_{i,j} = B_i + Right * (x_j * s) + Up * y_j`; при `i = L-1` вершины принудительно `CV_j`. Кольцо `i = 0` обязано бит-в-бит совпасть с торцевым кольцом ленты куска — использовать те же `Frame/Right/Up`, что ушли в кадр 0 ленты. Треугольники — квад-стрипы по `ProfileSegments`, как в `SweepMeshBuilder`, с тем же виндингом относительно направления «от торца к центру» для `start`-конца и зеркальным для `end`-конца (виндинг выбирается по `VSign`: при `VSign = +1` порядок индексов в квадах как в `SweepMeshBuilder`, при `-1` — поменять местами второй и третий индексы каждого треугольника). UV: `U = U_j`, `V = (VAtCut + VSign * distance(Frame.Position, B_i)) * UvScale`.
  - Корнер-клин между рукавом a и следующим по CCW рукавом b: зазор `γ` от азимута CCW-угловой точки a (`φ0`, радиус `r0 = a.CornerRadiusCcw`) до азимута CW-угловой точки b (`φ1`, радиус `r1 = b.CornerRadiusCw`), проходимый в сторону возрастания азимута. Краевая колонка стороны: индексы точек профиля с `|x_j - xExtreme| <= 1e-3 * lateralExtent`, где `xExtreme` — `max x` или `min x` в зависимости от того, какая крайняя колонка дала угловую точку; порядок — по индексу профиля. Сэмплы `M = max(2, (int)ceil(γ / snapshot.MaxAngleRad) + 1)`; `m = 0..M-1`: `t = m/(M-1)`, `φ = φ0 + γ*t`, `r = lerp(r0, r1, t)`, базовая точка `B_m = C + (cos φ * E1 + sin φ * E2) * r`, вершины `W_{m,e} = B_m + A * y_e` по краевой колонке. При `m = 0` вершины принудительно равны угловым вершинам торцевого кольца a, при `m = M-1` — рукава b. Треугольники: лента по колонке — для соседних `e, e+1` и `m, m+1` два треугольника `(W_{m,e}, W_{m,e+1}, W_{m+1,e+1})`, `(W_{m,e}, W_{m+1,e+1}, W_{m+1,e})`; радиальный веер — для каждого `e` и каждого `m`: `(CV_{j(e)}, W_{m+1,e}, W_{m,e})`. UV клина: `U = U_{j(e)}`, `V = (VAtCut_a + дуговая дистанция от φ0 до φ) * UvScale`.
  - Валентность 1: только арм-лофт, клиньев нет. Зазор `γ < 1e-3` — клин пропустить.
  - После сборки всех рукавов и клиньев: если есть террейн — фаза драпировки (идентична ленточной); затем `SweepMeshBuilder.Cleanup` (сварка позиция+UV, выброс дегенератов, компакция).
  - Ориентация: после сборки посчитать сумму нормалей треугольников веера с весом площади; если `dot(среднее, Axis) < 0` — инвертировать порядок индексов всех треугольников веера и лент клиньев (не арм-лофтов). Выполняется один раз, до Cleanup.
- Gate: bridge-задача `Task_SweepNet_U5`: на синтетике Unit 3/4 построить патч junction 0. Ассерты:
  - `PASS seam` — для каждого рукава каждая вершина кольца 0 арм-лофта совпадает с вершиной торцевого кольца соответствующей ленты с точностью 1e-4;
  - `PASS center` — все вершины патча в плане не дальше `max setback + lateralExtent` от центра, и существует вершина в точности (±1e-4) на `C + A*y_j` для каждого `j`;
  - `PASS sane` — вершин > 0, треугольников > 0, NaN нет;
  - `PASS normals` — среднее нормалей треугольников с `dot > 0` к Axis покрывает ≥ 90% суммарной площади патча (Ribbon: патч плоский, все нормали вверх);
  - `PASS degenerate` — треугольников с площадью < 1e-8 нет.
- On failure: ≤3 итерации; если `PASS normals` падает — применить предписанную инверсию виндинга, не изобретать другое; после 3 неудач — стоп и отчёт.

### Unit 6 — Нода и executor

- Goal: нода `Sweep Network` работает в графе end-to-end: топология + сплайны на входе → ленты и патчи в сцене, инвалидация и отмена штатные.
- Touch:
  - Новый `Packages/PCG.Sweep/Scripts/Sweep/SweepNetworkNode.cs` — по образцу `SweepSplineNode`: `[PcgNodeInfo("Sweeps a profile along a spline network and builds junction patches.", DisplayName = "Sweep Network", Category = "Sweep", Tags = new[] { "sweep", "network", "road", "junction" })]`. Поля: `Enabled = true`; `[Input] List<Spline> Splines`; `[Input(Connection = PcgConnectionType.Override)] SplineNetworkTopology Topology`; `[Input(Connection = PcgConnectionType.Override)] SweepProfile Profile`; инлайн-профиль `Shape/Width/Height/CustomPoints/CustomClosed` как у `SweepSplineNode`; `[Input] float Step = 1f; [Input] float MaxStep = 8f; [Input] float MaxAngle = 5f; [Input] float SetbackScale = 1f;` `bool CapEnds;` `[Input] float UvScale = 0.25f;` `[Input] TerrainData Terrain; [Input] Vector3 TerrainOffset; [Input] float HeightOffset = 0.1f;` `[Input] string Name = "Sweep Network"; [Input] Material Material; [Input] Material JunctionMaterial;` `bool Collider;` `[Output] List<MeshInstanceData> Results => default;` Кривых Width/Height/Twist-By-T у ноды нет.
  - Новый `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkNodeExecutor.cs` — по образцу `SweepSplineNodeExecutor`: `PcgAsyncPreviewNodeExecutor<SweepNetworkNode>, INodeInfo, IInstancesNode`. Последовательность `DoComputeAsync`:
    - главный поток (OperationScope): резолв профиля (та же `ResolveProfile`-логика), параметры, flatten сплайнов, `SplineSnapshot.Capture` всех сплайнов, читка топологии и материалов;
    - пул: `SweepNetworkSolver.Solve`;
    - главный поток (OperationScope со `Step`): кадры кусков `SweepNetworkFrames.BuildRangeFrames`, фреймы рукавов уже в решении солвера; захват окна террейна по объединённым баундам кадров, центров junction и `margin = lateralExtent + max setback`;
    - пул: параллельно `SweepMeshBuilder.Build` по кускам и `SweepJunctionMeshBuilder.Build` по junction (по образцу текущего цикла `UniTask.RunOnThreadPool` + `WhenAll`);
    - главный поток: сборка `List<MeshInstanceData>` — ленты с `Material` и именами `"{Name} {i}"`, патчи слить в чанки ≤ 2 000 000 вершин конкатенацией массивов со смещением индексов, материал `JunctionMaterial != null ? JunctionMaterial : Material`, имена `"{Name} Junctions"` (+` {n}` при нескольких чанках); `SyncSceneAsync` — скопировать из эталона.
    - Топология null или без junctions — сплошной свип целых сплайнов без патчей (couски = исходные сплайны, диапазон полный, капы по `CapEnds`).
  - `GetVersionSalt`: как у эталона (террейн-версия, профиль-хеш) плюс `topology?.GetContentHash() ?? 0`.
  - `INodeInfo`: `"Meshes: X, Triangles: Y"` — скопировать подсчёт.
- Gate: bridge-задача `Task_SweepNet_U6`: рефлексией — тип ноды с полями `Topology`, `SetbackScale`, `JunctionMaterial` существует, executor существует и наследует `PcgAsyncPreviewNodeExecutor`; компиляция чистая (`compiler_errors` пуст, `foreign_errors` false). Лог `PASS node`.
- On failure: ≤3 итерации; при конфликте с фактическими сигнатурами базовых классов — читать эталонный executor и повторять его структуру дословно; стоп и отчёт после 3.

### Unit 7 — Документация

- Goal: аддон документирует новую ноду.
- Touch: `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — в `## Nodes` добавить `Sweep Network`; новый раздел `## Sweep Network` с перечислением полей (по образцу раздела Sweep Spline) и абзацем Behavior: сплит по топологии, setback с митрами, радиальная звезда (арм-лофты к центральному кольцу, корнер-клинья по филлет-дуге), непрерывность U/V через торец, материал патчей, поведение без топологии.
- Gate: bridge-задача не нужна; проверка `grep -n "Sweep Network" Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` возвращает ≥ 2 совпадения (лог в транскрипте).
- On failure: поправить файл, повторить grep.

## Done (/goal condition)

Выполнено, когда: bridge-задачи `Task_SweepNet_U1`, `Task_SweepNet_U2`, `Task_SweepNet_U3`, `Task_SweepNet_U4`, `Task_SweepNet_U5`, `Task_SweepNet_U6` все завершились со `status: "success"` и всеми своими `PASS`-строками в логах (вывод `wait-for-result.sh` в транскрипте), `grep -n "Sweep Network" Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` даёт ≥ 2 строки, и последняя bridge-задача подтверждает `foreign_errors: false`. Ограничения: правки только в `Packages/PCG.Sweep/` и двух asmdef; `PCG.Splines` не изменён (`git status` по каталогу пуст); ассерты валидационных задач не редактировались. Стоп после 40 ходов или при трёх подряд провалах одного гейта.

## End-of-run report (the agent does this when the goal is met or it stops)

- Смени Status в начале документа на `Выполнено`.
- Отчитайся: какие юниты закрыты, какие гейты потребовали повторов, на чём остановился и почему (если остановился).
- Отметь флагом — сам не делай: уточни у заказчика, нужно ли обновлять проектную документацию (`Docs/PROJECT_MAP.md`, справка нод) под эти изменения.
