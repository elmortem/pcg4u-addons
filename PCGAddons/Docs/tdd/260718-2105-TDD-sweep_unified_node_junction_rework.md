Status: Не готов

# Sweep — единая нода и переработка junction-патча — Agent Execution Spec

Два изменения одним документом. Первое: `Sweep Network` удаляется, `Sweep Spline` получает опциональный порт `Topology` — подключен, значит сеть с перекрёстками, не подключен — прежний одиночный свип; полный набор параметров (включая `WidthByT/HeightByT/TwistByT`) работает в обоих режимах. Второе: junction-патч перерабатывается — текущая геометрия дефектна: обод клина строится дугой окружности вокруг центра (при зазоре ~180° и валентности 2 получаются диски), setback с капом `6w` раздувает патч на острых углах, кромка арм-лофта (smoothstep-кривая) не совпадает с прямой границей веера клина (щели и нахлёсты в середине), арм-лофты соседних рукавов тянутся к центру полной шириной и перекрывают чужие сектора, глобальный флип виндинга переворачивает часть треугольников. Новый патч: линейное сужение лофтов, обод — квадратичная Безье между угловыми точками по линиям кромок, setback с капом `2.5w`, виндинг по построению.

## References (not inlined)

- Конвенции и запреты: `CLAUDE.md` репозитория; принципы: `Docs/DESIGN_PRINCIPLES.md`.
- Скилл проверок: `unity-bridge`.
- Эталоны: `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs`, `SweepNetworkSolver.cs` (текущие `SolveSplit`/`BuildNetwork` после `260718-2042-TDD-sweep_network_solver_thread_fix.md`), `SweepJunctionMeshBuilder.cs` (текущая версия — заменяется), `SweepNetworkFrames.cs`.

## Foundations (shared, used across units)

- Режим сети включается фактом подключения `Topology` (читать через `GetInputValue(nameof(Data.Topology), Data.Topology)`; null → одиночный режим, ни одна ветка сети не выполняется).
- Контракт кадров кусков: `SweepFrame.T` — глобальный normalized-параметр ИСХОДНОГО сплайна (`(rangeStart + локальная дистанция) / длина исходного сплайна`), `SweepFrame.Distance` — локальная дистанция от начала диапазона куска. LUT-кривые сэмплируются по `T` (непрерывны через перекрёсток), V-координата — по `Distance` (обнуляется на торце). `SweepMeshBuilder` уже читает поля именно так — его не менять.
- Торцевое кольцо рукава = кольцо кадра 0 (или последнего) ленты куска, включая width/height/twist множители по его глобальному `T`. Арм-лофт кольцом 0 совпадает с ним бит-в-бит.
- Боковой вылет рукава `wArm` = `lateralExtent * widthLut(T торца)`; митры и радиусы патча считаются от `wArm` каждого рукава, не от глобального `lateralExtent`.

## Invariants (must hold throughout)

- Правки только в `Packages/PCG.Sweep/`; `PCG.Splines` и релизный `Assets/Plugins/PCG4U` не трогаются; `*.meta` руками не правятся (при удалении `.cs` Unity сам убирает осиротевший meta).
- При неподключенной `Topology` вывод `Sweep Spline` бит-в-бит равен выводу до правок (существующие сцены не меняются).
- Ассерты bridge-задач не редактируются ради прохождения.
- Тяжёлое — в пуле; Unity API (`Spline.*`) — только на главном потоке; батчи 1024 с `ct`/`reportProgress` в билдерах.

## Execution Plan

Units run in listed order.

### Unit 1 — Слияние нод

- Goal: `SweepNetworkNode` удалён, `Sweep Spline` несёт сетевые порты, одиночный режим не изменился.
- Touch:
  - `Packages/PCG.Sweep/Scripts/Sweep/SweepSplineNode.cs` — добавить после `Splines`:
    `[Input(Connection = PcgConnectionType.Override)] [PcgMemberInfo("Optional network topology from Spline Intersection; connected builds junction patches.", Tags = new[] { "topology", "network", "junction" })] public SplineNetworkTopology Topology;`
    после `CapEnds`: `[Input] [PcgMemberInfo("Multiplier of the automatic junction setback.", Tags = new[] { "setback", "junction" })] public float SetbackScale = 1f;`
    после `Material`: `[Input] [PcgMemberInfo("Material of junction patches; empty reuses Material.", Tags = new[] { "material", "junction" })] public Material JunctionMaterial;`
    (`using PCG.Splines;`).
  - Удалить файлы: `Packages/PCG.Sweep/Scripts/Sweep/SweepNetworkNode.cs`, `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkNodeExecutor.cs`.
  - `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` — `DoComputeAsync` ветвится: `topology` прочитан и не null и `topology.Junctions.Count > 0` → сетевой путь (перенести из удаляемого `SweepNetworkNodeExecutor`: capture → `SolveSplit` в пуле → `BuildNetwork` + кадры кусков + окно террейна на главном → билдеры в пуле → результаты); иначе — прежний код без изменений. `GetVersionSalt` — добавить `topology?.GetContentHash() ?? 0` (читать вход и в salt). Имена мешей: ленты `"{Name} {i}"`, патчи `"{Name} Junctions"`.
  - Сетевой путь передаёт в снапшот кусков НАСТОЯЩИЕ LUT из `Data.WidthByT/HeightByT/TwistByT` (не константы) и капы: `CapStartFlags[i] = FreeStart[i] && Data.CapEnds`, аналогично End.
- How: существующая графовая семантика (`SweepNetworkNode` в сохранённых графах станет missing-нодой — ожидаемо, пользователь удаляет её руками; в коде на это не реагировать).
- Gate: bridge-задача `Task_SweepU_U1`: рефлексией — тип `PCG.Sweep.SweepNetworkNode` отсутствует во всех сборках (`PASS removed`); `SweepSplineNode` содержит `Topology`, `SetbackScale`, `JunctionMaterial` (`PASS fields`); компиляция чистая, `foreign_errors: false`.
- On failure: ≤3 итерации, затем стоп и отчёт.

### Unit 2 — Солвер: глобальный T, ширины рукавов, кап setback

- Goal: сетевые данные несут глобальный `T`, углы патча считаются от реальных кромок, setback ограничен.
- Touch: `SweepNetworkSolver.cs`, `SweepNetworkFrames.cs`, `SweepNetworkArm.cs`.
  - `SweepNetworkFrames.BuildRangeFrames` — принимает `sourceLength` (длина исходного сплайна) и `sourceOffset` (глобальная дистанция начала куска в исходном сплайне: сумма `rangeStart` куска и дистанции реза от начала сплайна); `frame.T = (sourceOffset + localDistance) / sourceLength`, `frame.Distance = localDistance`. Для целых сплайнов (без резов) `sourceOffset = 0`, поведение прежнее.
  - `SweepNetworkArm` — заменить `CornerRadiusCcw/Cw` на: `public float3 CornerCcw; public float3 CornerCw; public float3 EdgeDirCcw; public float3 EdgeDirCw; public float WidthMul;` (позиции угловых точек торцевого кольца в мире и единичные направления кромок в плоскости junction — направление кромки = проекция `Outward` на плоскость, взятая в точке угла; `WidthMul = widthLut(T торца)`).
  - `SweepNetworkSolver.BuildNetwork` — митра пары рукавов: `wA = lateralExtent * a.WidthMul`, `wB = lateralExtent * b.WidthMul`, `m = γ >= PI ? 0 : max(wA, wB) / tan(γ/2)`, кап `m = min(m, 2.5f * max(wA, wB))`; setback рукава = `setbackScale * max(0.75 * wArm, максимум митр двух сторон)`; масштабирование на короткие куски — как сейчас. Угловые точки и направления кромок заполняются на финальном проходе по фактическим крайним колонкам профиля с width-множителем торца.
- How: точечные правки перечисленного; вся остальная логика солвера сохраняется.
- Gate: bridge-задача `Task_SweepU_U2`: синтетика A — X-крест 90°, `lateralExtent = 4`: `PASS setback90` при setback `4 ± 0.05` у всех рукавов. Синтетика B — X-крест с углом 20°: `PASS setbackCap` при setback ≤ `2.5 * 4 * 1.01` у всех рукавов. Синтетика C — один сплайн длиной 40 с одним резом на 20: у кадров второго куска `PASS globalT` при `T` первого кадра ≈ `20/40 ± 0.01` и `Distance` первого кадра `0 ± 1e-4`.
- On failure: ≤3 итерации, затем стоп и отчёт.

### Unit 3 — Junction-патч: линейные лофты и Безье-обод

- Goal: патч компактен (не шире дорог с митрами), без щелей между секторами, виндинг корректен по построению.
- Touch: `SweepJunctionMeshBuilder.cs` — метод `Build` переписывается; `MakeVertex`, `EdgeColumn`, `Planar` сохраняются.
- How:
  - Арм-лофт: как сейчас, но `s = 1 - t` (линейное сужение вместо smoothstep). Кольцо `i = rings-1` — принудительно центральное (`MakeVertex` от `center`). Кромка лофта — прямая от угловой точки к центру.
  - Разбиение секторов: рукав ограничен двумя биссектрисами — единичные направления `bisCcw`/`bisCw` в плоскости junction, делящие пополам угловые зазоры к соседним рукавам (при валентности 1 ограничения нет). Каждая вершина колец `i >= 1` лофта зажимается боковым лучом своего кольца: для стороны CCW параметр `sMax` — пересечение луча `B_i + s * dir` (2D в `E1/E2`) с прямой `center + u * bisCcw`; если пересечение существует при `u > 0` и `s > 0`, боковое смещение вершины клампится `min(|x_j| * scale, sMax)`; сторона CW симметрично. Кольцо `i = 0` не клампится (бит-в-бит с торцом ленты). Лофты соседних рукавов после клампа не пересекаются: каждый лежит в своём биссектрисном секторе.
  - Клин между рукавами a (CCW-угол `A0 = a.CornerCcw`) и b (CW-угол `B0 = b.CornerCw`): контрольная точка `K` — пересечение прямых `A0 + s * a.EdgeDirCcw` и `B0 + s * b.EdgeDirCw` в плоскости junction (2D через `Planar`); если знаменатель пересечения по модулю < 1e-4 или точка дальше `3 * max(wA, wB)` от середины `(A0+B0)/2` — `K = (A0 + B0) / 2` (фаска). Обод: квадратичная Безье `P(t) = (1-t)^2*A0 + 2(1-t)t*K + t^2*B0`, сэмплов `M = max(2, ceil(γ / maxAngleRad) + 1)`, где `γ` — угловой зазор азимутов углов. Вертикаль: по краевой колонке (как сейчас), базовая высота обода — `lerp(высота A0, высота B0, t)` в не-террейн режиме; в террейн-режиме XZ по Безье, `ry` по колонке.
  - Слайс `m = 0` — принудительно вершины углового столбца рукава a (бит-в-бит с кольцом 0 его лофта), `m = M-1` — рукава b.
  - Веер к центру: для каждого сэмпла пары `(m, m+1)` и точки колонки `e` — треугольник `(CV_e, W_{m+1,e}, W_{m,e})`; лента по колонке между `e` и `e+1` — как сейчас. Обход клиньев всегда в сторону возрастания азимута (CCW в базисе `E1/E2`) — виндинг фиксирован построением; блок глобального флипа по сумме нормалей удалить.
  - Границы секторов: веер клина у `m = 0` опирается на прямую `A0 → CV` — та же прямая, что кромка линейного лофта рукава a; совпадение вершин обеспечить общей формулой (`MakeVertex` от одинаковых аргументов), чтобы `Cleanup` их сварил.
  - Центральные вершины `CV_e` создавать один раз на клин, как сейчас, UV `V = a.VAtCut * uvScale`.
- Gate: bridge-задача `Task_SweepU_U3`: синтетика A (X 90°, w = 4): `PASS compact` — все вершины патча в плане не дальше `(setback + 4) * 1.15` от центра; `PASS seam` — каждая вершина кольца 0 каждого лофта совпадает с вершиной торца своей ленты ≤ 1e-4; `PASS boundary` — для каждого клина вершины его слайса `m=0` совпадают с вершинами кромки лофта рукава a (прямая угол→центр, проверять все кольца лофта по крайней колонке) ≤ 1e-4; `PASS normals` — ВСЕ треугольники патча имеют `dot(нормаль, Axis) > 0` (Ribbon, плоский случай) без какого-либо флипа в коде; `PASS nohole` — луч из центра в плане в 16 равномерных азимутах пересекает ≥ 1 треугольник патча или ленты (нет дыр вокруг центра). `PASS partition` — фиксированная сетка 32×32 точек в плане поверх баунда патча: каждая точка, не лежащая ближе 1e-3 к ребру какого-либо треугольника, покрыта не более чем одним треугольником патча (сектора не перекрываются). Синтетика B (X 20°): `PASS compactSharp` — вершины патча не дальше `(setback + 4) * 1.3` от центра. Синтетика C — Y-стык из трёх рукавов с азимутами 0°, 100°, 200° (валентность 3, неравные зазоры): `PASS partitionY` — та же сеточная проверка покрытия ≤ 1.
- On failure: ≤3 итерации, затем стоп и отчёт; не вводить обратно глобальный флип и дуги вокруг центра.

### Unit 4 — Документация

- Goal: справка отражает единую ноду.
- Touch: `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — раздел `Sweep Network` удалить; в раздел `Sweep Spline` добавить поля `Topology`, `Setback Scale`, `Junction Material` и абзац Behavior про сетевой режим (сплит по топологии, setback по митрам с капом 2.5 ширины, Безье-обод по кромкам, непрерывность кривых XByT через глобальный T, missing-нода вместо старой Sweep Network).
- Gate: `grep -c "Sweep Network" Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` ≤ 2 (упоминания только в тексте про удаление/патчи), `grep -n "Setback Scale" ...` ≥ 1.
- On failure: поправить и повторить.

## Done (/goal condition)

Выполнено, когда bridge-задачи `Task_SweepU_U1`, `Task_SweepU_U2`, `Task_SweepU_U3` завершились `status: "success"` со всеми `PASS`-строками (вывод `wait-for-result.sh` в транскрипте), grep-проверки Unit 4 дали указанные результаты, `foreign_errors: false` в последней задаче, тип `SweepNetworkNode` отсутствует в сборках. Ограничения: правки только в `Packages/PCG.Sweep/`; при неподключенной топологии вывод ноды не изменён (подтверждается прохождением `Task_SweepNet_U1`-ассерта `PASS build 22 60` из прежнего документа, повторить его внутри `Task_SweepU_U1`); ассерты не редактировались. Стоп после 35 ходов или трёх подряд провалов одного гейта.

## End-of-run report (the agent does this when the goal is met or it stops)

- Смени Status в начале документа на `Выполнено`.
- Отчитайся: какие юниты закрыты, какие гейты потребовали повторов, на чём остановился и почему.
- Флаг — сам не делай: уточни у заказчика, нужно ли обновлять проектную документацию (`Docs/PROJECT_MAP.md`, справка нод) под эти изменения.
