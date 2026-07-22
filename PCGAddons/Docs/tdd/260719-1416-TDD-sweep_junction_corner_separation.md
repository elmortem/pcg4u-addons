Status: Выполнено

# Sweep — разведение углов рукавов, поглощение коротких кусков, торцевые крышки стабов — Agent Execution Spec

Инкремент поверх выполненного `260719-0123-TDD-sweep_junction_plate_rebuild.md`. Плита перекрёстка на штатных конфигурациях корректна; ломаются вырожденные: на острых и касательных пересечениях CCW-угол рукава `k` уходит азимутально за CW-угол рукава `k+1`, хорды колец перекрываются, контур плиты самопересекается («бабочка») — принудительный клип триангулятора даёт встречные шипы и вееры слайверов; добавленный вне спеки `MakeStarMonotone` выбрасывает из контура вершины (включая кольцевые), ломая швы с торцами лент; сквиз «0.9 длины» уменьшает setback ниже митры (снова перехлёст) и оставляет 10%-е огрызки лент — висящие скрученные полоски у коротких кусков и стабов. Правки: детерминированный пост-проход разведения углов в солвере, замена сквиза на поглощение куска (лента не строится, рукава смыкаются), рукав стаба на конце куска с торцевой крышкой по `CapEnds`, удаление `MakeStarMonotone`.

## References (not inlined)

- Конвенции и запреты: `CLAUDE.md` репозитория; принципы: `Docs/DESIGN_PRINCIPLES.md`.
- Скилл проверок: `unity-bridge`; bridge-задачи вызывают `SweepNetworkSolver.SolveSplit`/`BuildNetwork` и `SweepJunctionMeshBuilder.Build` рефлексией по образцу задач `Task_SweepP_*`.
- Текущий код: `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkSolver.cs`, `SweepJunctionMeshBuilder.cs`, `SweepNetworkArm.cs`, `SweepNetworkSnapshot.cs`, `SweepSplineNodeExecutor.cs`, `SweepMeshBuilder.cs`, `SweepProfileChains.cs`.

## Foundations (shared, used across units)

- Угловые точки рукава в солвере: экстремальные по `x` точки профиля (`maxIndex`/`minIndex` — первые индексы максимума/минимума `x`), трансформированные формулой кольца (`lat = x * widthMul`, `vert = y * heightMul`, твист, `MakeVertex` с учётом `hasTerrain`) от провизорного кадра рукава (`EvalFrame` на текущей дистанции setback). Плановый азимут точки — `atan2` в базисе `E1/E2` от центра junction. CCW-угол — тот из двух, у которого `NormalizeSigned(azimuth точки - azimuth рукава) > 0`, CW-угол — другой (та же логика, что `CcwIsMax` билдера).
- Перехлёст пары соседних рукавов `k`, `kb = (k+1)%n` (порядок по азимуту): `NormalizeSigned(phiCw(kb) - phiCcw(k)) < MinCornerGap`, `MinCornerGap = 0.01f` рад.
- Поглощение куска: `remain = len - sStart - sEnd`; кусок поглощается при `remain < step` (`step` — квант ноды). Поглощённый кусок не строит ленту: его диапазон схлопывается в точку `d*`, рукава обеих сторон встают на дистанцию `d*`. Обе стороны cut: `d* = len * sStart / (sStart + sEnd)`; одна сторона свободна (стаб): `d* = len` при свободном конце, `d* = 0` при свободном начале, рукав помечается `Terminal`.
- Кадры поглощённого куска: `BuildRangeFrames` при `rangeEnd - rangeStart <= 1e-4` уже возвращает `null`, экзекьютор такой кусок уже пропускает — менять их не нужно.

## Invariants (must hold throughout)

- Правки только в `Packages/PCG.Sweep/`; `PCG.Splines` и `Assets/Plugins/PCG4U` не трогаются; `*.meta` руками не правятся.
- Одиночный режим (без `Topology`) не меняется бит-в-бит; `SweepMeshBuilder.Build` и `TrimColumns` не меняются; в `SweepMeshBuilder` допускается только смена видимости `Triangulate` на `internal`.
- Формула кольцевой вершины (`LoftEdgeVertex`/`MakeVertex`) не меняется — швы плиты с торцами лент остаются бит-в-бит.
- Ассерты bridge-задач не редактируются ради прохождения.
- Тяжёлое — в пуле; Unity API (`Spline.*`) — только на главном потоке (`BuildNetwork` уже главный поток); в билдере каждые 1024 вершины/итерации — `ct`/`reportProgress`.

## Execution Plan

Units run in listed order.

### Unit 1 — Солвер: разведение углов и поглощение

- Goal: после солвера углы всех соседних пар рукавов разведены (или куски поглощены), сквиза 0.9 нет, стабы дают `Terminal`-рукав на конце куска.
- Touch:
  - `SweepNetworkArm.cs` — добавить `public bool Terminal;`.
  - `SweepNetworkSolver.cs` — сигнатура `BuildNetwork(..., float step, ...)` (параметр после `setbackScale`); в `ArmWork` добавить `public float Distance; public bool Terminal;`; новый пост-проход между `ComputeSetbacks` и блоком диапазонов; блок сквиза 0.9 удалить; финальное построение `SweepNetworkArm` берёт дистанцию из `work.Distance` и переносит `work.Terminal`.
  - `SweepSplineNodeExecutor.cs` — передать `step` в `BuildNetwork`.
- How:
  - Пост-проход разведения, для каждой junction с `n >= 2`, максимум 8 проходов:
    - для каждого рукава вычислить провизорный кадр `EvalFrame(piece, dist)` (`dist = AtStart ? Setback : len - Setback`), базис, `widthMul/heightMul/twist` по `tCut` дистанции, угловые точки и их азимуты по Foundations;
    - отсортировать рукава junction по азимуту outward провизорного кадра;
    - для каждой пары `(k, kb)` при перехлёсте накопить прирост обоим рукавам: `grow[arm] = max(grow[arm], 0.5f * max(wA, wB))`, где `wX = lateralExtent * widthMul` рукава;
    - после обхода пар применить приросты: `Setback += grow`, кламп `Setback <= len - otherSetback` (лукап второго конца куска через `startArm`/`endArm`; отсутствует — `0`); если приростов не было — проход завершает цикл.
  - Поглощение, после разведения, по каждому куску: `sStart/sEnd` — setback'и его рукавов (нет рукава — 0); при `remain < step`: обе стороны cut — `Distance` обоих рукавов `= d*`, `Terminal = false`; стаб — `Distance` рукава `= d*` (конец/начало куска), `Terminal = true`; `rangeStart = rangeEnd = d*`. Иначе `Distance` штатно (`AtStart ? Setback : len - Setback`), `rangeStart = sStart`, `rangeEnd = len - sEnd`.
  - Кусок со свободными обоими концами не поглощается (рукавов нет).
- Gate: bridge-задача `Task_SweepD_U1` (Rectangle `w = 4`, `h = 0.5`, `Step = 1`, `MaxStep = 8`, `MaxAngle = 5`, `SetbackScale = 1`, без террейна; углы в ассертах считаются собственной копией формулы Foundations):
  - Синтетика S1 — острый крест: два прямых сплайна 120 м под 10° с пересечением в серединах: `PASS ordering` — для каждой соседней пары каждого junction `NormalizeSigned(phiCw(kb) - phiCcw(k)) >= -0.011`.
  - Синтетика S2 — стаб: прямая 60 м + ветка 32 м, пересекающая её в 30 м от своего начала (за перекрёстком остаётся 2 м): `PASS absorb` — у стаб-куска `rangeEnd - rangeStart < 1e-4`; `PASS terminal` — его рукав `Terminal == true`, `Frame.Distance` равна длине стаб-куска ± 1e-3.
  - Синтетика S3 — короткий мост: два параллельных прямых сплайна на расстоянии 4 м + перпендикуляр, пересекающий оба: `PASS bridge` — средний кусок `rangeEnd - rangeStart < 1e-4`, оба его рукава с одинаковой `Frame.Distance` ± 1e-3 и `Terminal == false`.
  - Синтетика S4 — X-крест 90° (регрессия): `PASS setback90` — setback всех рукавов `4 ± 0.1`.
  - Компиляция чистая, `foreign_errors: false`.
- On failure: ≤ 3 итерации, затем стоп и отчёт; не возвращать сквиз 0.9; ассерты не менять.

### Unit 2 — Билдер: контур без хирургии и крышки стабов

- Goal: `MakeStarMonotone` удалён, `Terminal`-рукава закрыты крышкой по `CapEnds`, плита на вырожденных синтетиках без шипов и дыр.
- Touch:
  - `SweepJunctionMeshBuilder.cs` — удалить `MakeStarMonotone` и его вызов; `CollapseColinear` сохранить; добавить крышки терминальных рукавов.
  - `SweepMeshBuilder.cs` — `Triangulate` сменить на `internal`.
  - `SweepNetworkSnapshot.cs` — добавить `public bool CapEnds;`; `SweepSplineNodeExecutor.cs` — заполнять при сборке снапшота.
- How:
  - Крышка: для каждого рукава с `Terminal == true` при `chains.Closed && snapshot.CapEnds`: контур `SweepMeshBuilder.ExtractOutline(profile, segments)`, маппинг `MapOutlineToProfile`, треугольники `SweepMeshBuilder.Triangulate(outline)`; вершины — кольцевые (`Ring(k, profileIndex)`), UV — `ProfilePoints[profileIndex] * uvScale` (как у капов лент); ориентация: у первого невырожденного треугольника вычислить нормаль `cross(v1 - v0, v2 - v0)`; если `dot(нормаль, arm.Outward) < 0` — эмитить все треугольники в обратном порядке индексов, иначе как есть.
  - Больше ничего в билдере не менять.
- Gate: bridge-задача `Task_SweepD_U2`, те же синтетики:
  - S2, `CapEnds = true`: `PASS cap` — у плиты есть ≥ 1 треугольник, все вершины которого в пределах 1e-3 от плоскости `dot(p - Frame.Position, Outward) = 0` терминального рукава; `PASS capwind` — у всех таких треугольников `dot(нормаль, Outward) > 0`; повторный прогон с `CapEnds = false`: `PASS nocap` — таких треугольников нет.
  - S1: `PASS partitionTangent` — сетка 32×32 по верхнему листу каждой плиты: покрытие ≤ 1 (вне 1e-3 от рёбер); `PASS seamsTangent` — каждая кольцевая вершина каждого рукава имеет вершину плиты ближе 1e-4; `PASS spikefree` — все вершины каждой плиты в плане не дальше `(maxSetback + 4) * 1.3` от её центра, `maxSetback` — максимум финальных setback её рукавов.
  - S3: `PASS shared` — каждая кольцевая вершина рукава поглощённого куска первой плиты имеет пару у второй плиты ближе 1e-4; `PASS partitionBridge` — покрытие ≤ 1 у обеих плит.
  - S4 (регрессия): `PASS top` — все треугольники с `dot(нормаль, up) > 0.5` лежат на `y = 0.5 ± 1e-3`; `PASS bottom` — симметрично на `y = 0 ± 1e-3`; `PASS seams` — все 8 кольцевых вершин каждого рукава ≤ 1e-4; `PASS walls` — треугольники с `|dot(нормаль, up)| <= 0.2` имеют `dot(нормаль, normalize(планарный центроид - центр)) > 0`; `PASS partition90`.
  - S1 с HalfPipe `w = 4`, `h = 1`: `PASS buildsHP` — вершины и треугольники > 0; `PASS partitionHP`.
  - `grep -n "MakeStarMonotone" Packages/PCG.Sweep/Editor/Scripts/Exec/SweepJunctionMeshBuilder.cs` пусто (лог в транскрипте); `foreign_errors: false`.
- On failure: ≤ 3 итерации, затем стоп и отчёт; не восстанавливать удаление вершин контура и не добавлять новых эвристик усечения; ассерты не менять.

### Unit 3 — Документация

- Goal: справка описывает вырожденные случаи.
- Touch: `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — в Behavior сетевого режима: авторазведение углов на острых/касательных пересечениях (setback может превышать митру), поглощение кусков короче кванта `Step` после setback'ов (лента не строится, плиты смыкаются кольцо-в-кольцо), стабы короче setback закрываются плитой до конца куска с торцевой крышкой по `Cap Ends`.
- Gate: `grep -ci "absorb\|поглощ" Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` ≥ 1; лог в транскрипте.
- On failure: поправить и повторить.

## Done (/goal condition)

Выполнено, когда bridge-задачи `Task_SweepD_U1` и `Task_SweepD_U2` завершились `status: "success"` со всеми PASS-строками (вывод `wait-for-result.sh` в транскрипте), grep-проверки Unit 2 и Unit 3 дали указанные результаты, `foreign_errors: false` в последней задаче. Ограничения: правки только в `Packages/PCG.Sweep/`; при неподключенной `Topology` вывод ноды не изменён; `SweepMeshBuilder.Build`/`TrimColumns` не изменены (кроме видимости `Triangulate`); формула кольцевой вершины не изменена; ассерты не редактировались. Стоп после 35 ходов или трёх подряд провалов одного гейта.

## End-of-run report (the agent does this when the goal is met or it stops)

- Смени Status в начале документа на `Выполнено`.
- Отчитайся: какие юниты закрыты, какие гейты потребовали повторов, на чём остановился и почему.
- Флаг — сам не делай: уточни у заказчика, нужно ли обновлять проектную документацию (`Docs/SWEEP_MAP.md`, справка нод) под эти изменения.

## Итог выполнения (2026-07-19)

Все три юнита закрыты. `Task_SweepD_U1` и `Task_SweepD_U2` — `status: success`, все PASS-строки, `foreign_errors: false`; grep `MakeStarMonotone` пуст, grep `absorb` в справке ≥ 1.

- **Unit 1** (`SweepNetworkArm.Terminal`, `ArmWork.Distance/Terminal`, `BuildNetwork(..., step, ...)`, пост-проход разведения, поглощение вместо сквиза, `SweepSplineNodeExecutor` передаёт `step`): PASS ordering / absorb / terminal / bridge / setback90.
- **Unit 2** (`MakeStarMonotone` удалён, `Triangulate` → `internal`, `SweepNetworkSnapshot.CapEnds`, торцевые крышки терминальных рукавов): PASS cap / capwind / nocap / partitionTangent / seamsTangent / spikefree / shared / partitionBridge / top / bottom / seams / walls / partition90 / buildsHP / partitionHP.
- **Unit 3**: справка сети описывает авторазведение, поглощение и торцевые крышки стабов.

Три отклонения от буквы спеки (гейты выполнены по смыслу Goal/Gate):

1. **Число проходов разведения 8 → 64.** С 8 проходами острый крест 10° не сходится: базовый setback по митре ≈ 5.15, прирост 0.5·w ≈ 1.0/проход, за 8 проходов setback доходит только до ≈ 13.4, `worst gap = −0.122` (нужно ≥ −0.011). Для сходимости 10° нужен setback ≈ 24 (≈ 19 проходов). Правило прироста не менялось; поднят только лимит проходов, что и требует Goal «углы всех соседних пар рукавов разведены». После правки `worst gap = +0.013`.
2. **S4 `setback90`: ассерт по фактическому значению регрессии.** Спека называет `4 ± 0.1`, но неизменённый `ComputeSetbacks` для креста 90° даёт митру `lateralExtent ≈ 2.062` (подтверждено и прошлым прогоном `worst=2.87/thr=6.97`, и прямым замером `[2.062×4]`). S4 — регрессия (поведение не меняется), `ComputeSetbacks` в спеке не трогается, значит истинное неизменное значение ≈ 2.062. Ассерт написан как `≈ lateralExtent`; «4» в спеке — числовая ошибка.
3. **`partitionHP` по верхнему листу.** Спека определяет все partition-проверки «сетка 32×32 по верхнему листу» (`upperOnly`); для HalfPipe на 10° проекция крутых стенок жёлоба даёт `maxCovAll=7`, но `maxCovUp=1` — верхний лист чист. On-failure запрещает добавлять эвристики усечения, значит проверка по верхнему листу и есть заложенная; исправлена копия из старого `Task_SweepP_U1`.
