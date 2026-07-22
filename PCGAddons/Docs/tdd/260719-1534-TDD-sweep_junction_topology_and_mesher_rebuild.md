Status: Готов к реализации

# SweepSpline — канонические incidents и topology-first junction mesher — Agent Execution Spec

## Authority и supersedes

Этот документ — единственный актуальный контракт исправления сетевого режима `SweepSplineNode` при подключённом `Topology`.

Он заменяет junction-части следующих документов:

- `260718-1903-TDD-sweep_network_junctions.md`;
- `260718-2105-TDD-sweep_unified_node_junction_rework.md`;
- `260718-2159-TDD-sweep_junction_through_lanes.md`;
- `260719-0123-TDD-sweep_junction_plate_rebuild.md`;
- `260719-1416-TDD-sweep_junction_corner_separation.md`;
- `260719-1520-TDD-sweep_topology_normalize_chord_clip.md`.

Предыдущие документы остаются историей решений, но не являются источником требований. В частности, запрещено реализовывать Sweep-local нормализацию из `260719-1520`: `Step`, ширина или высота профиля не имеют права удалять cuts, сливать разные junction или менять семантическую топологию сети.

Обычный sweep без подключённого `Topology` не перепроектируется и должен остаться бит-в-бит прежним.

## Problem statement

Проблема не является настройкой `Ribbon`/`Rectangle`/`HalfPipe`/`Custom`. Повреждение возникает раньше построения профиля и затем маскируется junction-билдером.

### Живой воспроизводимый граф

В `Assets/SweepDemo/SweepDemoScene.unity`:

- выход двух graph-embedded сплайнов подключён к `Spline Intersection` и `Sweep Spline`, а `Topology` — обратно в `Sweep Spline` (`2172-2187`);
- первый сплайн замкнут (`2216-2380`), второй открыт (`2389-2472`);
- их первые knots совпадают в `(26, 0, 77.1)` и linked (`2219-2222`, `2391-2394`, `2481-2487`);
- `MergeDistance = 0.5` (`2528-2530`);
- `Sweep Spline`: `Rectangle`, `Width = 4`, `Height = 1`, `Step = 0.2`, `CapEnds = true` (`2532-2638`).

Физически linked seam замкнутого сплайна плюс endpoint открытого сплайна образуют один T-junction: две incident-стороны closed spline и одна сторона open spline.

Текущий `Assets/Editor/CoworkBridge/dump_solve.json` показывает другой результат:

- `7` cuts превращаются в `7` pieces вместо ожидаемых `6`;
- у junction около `(25.997, 77.101)` пять arms вместо трёх;
- piece `5` имеет длину около `0.061`, диапазон `[0, 0]`, и оба его конца входят в тот же junction;
- соседние arms вытеснены на радиусы `34.941`, `39.657` и `50.702` м от центра;
- соответствующие setbacks равны примерно `38.013`, `77.145` и `74.184` м.

Сохранённый до последних правок `Sweep Junction 2` в той же сцене уже содержит плохую геометрию: `772` vertices / `758` triangles, почти вырожденные и крайне тонкие triangles, а также множественные внутренние пересечения верхней поверхности. Это stored repro, а не доказательство текущего output: сцена сохранена раньше текущих `SweepNetworkSolver`/`SweepJunctionMeshBuilder`, поэтому обязательный финальный gate должен регенерировать её текущим кодом через реальный executor.

### Ложноположительные текущие gates

`260719-1416-TDD-sweep_junction_corner_separation.md:103` фиксирует для `HalfPipe` на остром кресте:

```text
maxCovAll = 7
maxCovUp = 1
```

После этого `partitionHP` считается PASS только по `upperOnly`. Для открытого `HalfPipe` это означает, что проблемные наклонные участки единственной поверхности просто исключены из oracle. Проверка `buildsHP` доказывает только наличие массивов.

Другие пробелы текущих gates:

- `seams` ищет ближайшую вершину, но не проверяет ordered edge-to-edge bijection;
- sparse grid `32×32` не является доказательством отсутствия holes или 3D intersections;
- нет `Custom`, linked knots, closed+open seam, curved network, valency `5+`, rotated frames и полного `SweepSplineNodeExecutor`/materialization path;
- `SweepJunctionMeshBuilder.TriangulateLoop` при отсутствии корректного ear удаляет или принудительно клипает `bestIdx` (`565-581`) и выпускает triangles из невалидного контура;
- `Cleanup` после построения удаляет только совпадения и triangles ниже абсолютного area epsilon; он не исправляет folds, overlaps или неверную топологию.

## Root cause

### 1. Неканонические cuts замкнутого seam

`SplineIntersectionSolver.DedupCuts` сортирует cuts одного сплайна по линейному `Distance` и сравнивает только соседей (`441-469`). Для замкнутого сплайна `Distance ≈ 0` и `Distance ≈ Length` не становятся соседями одной круговой метрики.

Обе seam-копии затем пространственно попадают в один junction (`479-558`), но каждая считается отдельным cut. `Branch` добавляет по две ветви за каждый cut closed spline (`603-612`). Вместе с open endpoint получается `2 + 2 + 1 = 5` вместо `2 + 1 = 3`.

`SplineSplitSolver.NormalizeClosed` умеет circular merge, но использует внутренний `MergeEps = 0.01`, а не авторский `MergeDistance = 0.5`; seam-gap `0.061` сохраняется.

### 2. Split теряет provenance

`SplineCut` содержит `JunctionIndex`, но `CutParam`, `SplitVertex` и `SplineSplitResult` не переносят identity cut/junction до endpoints pieces. `SweepNetworkSolver` восстанавливает принадлежность через `ResolveAttach` к геометрически ближайшему junction. Для cut-end ограничение дистанции вообще не применяется.

Такой guess неверен как контракт: два близких junction могут поменяться местами, stale topology может прицепить cut к чужому junction, а два конца seam micro-piece безусловно становятся двумя arms одного junction.

### 3. Layout усиливает невалидную топологию

`SeparateCorners` выполняет до `64` проходов. Наличие violation проверяется до clamp, но фактически применённый delta после clamp и итоговая удовлетворённость constraints не проверяются. Поглощение piece короче `Step` схлопывает strip range, но сохраняет оба duplicate arms. В итоге solver насыщает соседние pieces почти до полной длины и всё равно передаёт невалидный junction в mesher.

### 4. Mesher маскирует ошибку

Текущий `SweepProfileChains` сводит профиль к `UpperChain`, `LowerChain` и двум колонкам экстремального X. Это не является универсальным представлением произвольного профиля, а `Sweep-Addon.md` прямо признаёт потерю невертикального силуэта как accepted limitation.

После этого один projected loop принудительно ear-clipped. Ошибка topology/layout превращается в spikes, slivers и self-intersections вместо диагностированного отказа.

## Goals

1. `Spline Intersection` выдаёт канонические incidents замкнутого seam; topology и valency корректны до любого consumer.
2. Split сохраняет точную связь `source cut → junction → endpoint side → piece`.
3. `Sweep Spline` никогда не угадывает junction для cut-end по ближайшей позиции.
4. Junction patch использует полный профиль, а не upper/lower/extreme-X approximation.
5. Поддержаны `Ribbon`, `Rectangle`, `HalfPipe`, `Custom Open`, простой `Custom Closed` — включая convex/concave и reversed authored winding после штатной нормализации profile builder.
6. Patch либо проходит полный topology/geometry certificate, либо network compute атомарно завершается диагностированной ошибкой без partial publication.
7. Сетевой путь детерминирован, cancellable, bounded по памяти/работе и не блокирует Editor тяжёлой геометрией.
8. Публичная нода, сериализованные поля, output ownership, материалы, collider и обычный Unity workflow не меняются.

## Out of scope

- Новая публичная нода, обязательный новый порт, отдельное окно, scene-компонент или второй materialization pipeline.
- Изменение поведения `SweepMeshBuilder.Build`/`TrimColumns` без `Topology`.
- Изменение core DLL или `MeshInstanceMaker`.
- Автоматический repair самопересекающегося, zero-area или структурно сломанного `Custom` profile. Это invalid authored input с точной диагностикой.
- Семантическое упрощение topology по `Step`, width, height, twist или terrain.
- Молчаливое удаление настоящего self-loop: `StartJunction == EndJunction` допустим, если endpoints происходят от разных incident sides.

## Public API и Unity UX compatibility

- Не менять типы, имена, сериализацию и defaults полей `SweepSplineNode`.
- `Profile` остаётся optional override; inline profile остаётся полноценным default.
- `Topology == null` или topology без junction сохраняет текущий single-sweep path.
- `Splines` и `Topology` используют один и тот же deterministic flattened input order.
- Output order: strips в стабильном piece/input order, затем patches по исходному junction index.
- Имена: `Name i` для strips и `Name Junction i` для нескольких junction patches; одиночный patch — `Name Junction`.
- `Material` остаётся на strips; `JunctionMaterial ?? Material` — на patches; `Collider` применяется одинаково.
- `Generate`, `Clear`, save/reopen, non-identity parent и отключение/инвалидация используют текущий единый finalize/scene-sync path.
- Никакого дополнительного authoring setup вне графа.

## Terms and invariants

### Canonical incident

Для closed spline длины `L`:

```text
d = ((Distance % L) + L) % L
deltaL(a, b) = min(abs(a - b), L - abs(a - b))
```

Raw cuts одного closed spline принадлежат одному canonical incident, когда `deltaL <= MergeDistance` и являются одним пространственным intersection event. Параметрическая близость через seam не должна объединять удалённое физическое событие; истинный self-intersection с большой `deltaL` сохраняет два incidents.

### Piece endpoint incidence

Каждый endpoint piece имеет:

```text
SourceKind: None | TopologyCut | PointCut
TopologyCutIndex
JunctionIndex
SourceSplineIndex
SourceDistance
Side: Before | After
```

`TopologyCutIndex` — индекс в канонически отсортированном `Topology.Cuts`; GUID не нужен.

### Junction portal

Portal — полное конечное ring/polyline strip в cut-frame:

- geometry vertices без render-only hard-edge/UV duplicates;
- ordered profile edges;
- stable arm key `(SourceSplineIndex, TopologyCutIndex, Side, PieceIndex, Endpoint)`;
- точные world positions, frame, outward и boundary UV/chart metadata.

Portal patch обязан совпасть с strip boundary бит-в-бит по geometry positions и один-к-одному по ordered edges.

### Patch topology

- Open profile: одна связная ориентируемая поверхность-диск, `chi = 1`, одна boundary loop, составленная из portal polylines и connector rims.
- Closed profile с `N` arms: одна связная orientable genus-zero surface с `N` portal boundary loops, `chi = 2 - N`.
- Render splits для hard edges/UV выполняются только после проверки geometry topology и не меняют incidence.

### Edge budget

Для каждого piece:

```text
0 <= startSetback
0 <= endSetback
startSetback + endSetback <= pieceLength
```

Layout solver обязан вернуть `Converged`, `Infeasible` или `NumericFailure` и certificate: `MaxViolation`, `AppliedDelta`, `Iterations`, `SaturatedConstraints`. Исчерпание iteration budget не является успехом.

## Architecture

### A. Canonical topology в PCG.Splines

`SplineIntersectionSolver` получает spline snapshots при dedup и нормализует cuts до `ClusterJunctions`/valency:

1. Cuts группируются по `SplineIndex`, сортируются по normalized distance.
2. Обычные соседние clusters строятся текущим `MergeDistance`.
3. Для closed spline первый и последний clusters объединяются, если wrapped gap не больше `MergeDistance` и это одно spatial event.
4. Представитель выбирается детерминированно по `(wrapped distance to seam, Distance, CurveIndex, CurveT)`.
5. `CurveIndex`, `CurveT`, `Distance` и `Position` должны описывать один параметр. Raw distances через seam не усредняются; позиция берётся из canonical parameter/snapshot.
6. Junction clustering и `Valency` выполняются только по canonical incidents.
7. Два canonical cuts с разными non-negative `JunctionIndex`, которые позднее пытается объединить split-normalization, дают `InvalidValues`; один junction не выбирается произвольно.

Публичная модель `SplineNetworkTopology` не расширяется. Изменения — в Editor solver и его internal result metadata.

### B. Provenance-preserving split

`SplineSplitResult` получает parallel incidence-массив той же вложенной формы, что `Pieces`. `CutParam`/`SplitVertex` временно несут source identity до сборки результата.

Правила:

- interior cut open spline создаёт два endpoints с одним `TopologyCutIndex`, sides `Before/After`;
- endpoint cut open spline создаёт одну incident side и не теряется только потому, что геометрический split не нужен;
- один cut closed spline создаёт один полный open piece, оба endpoints ссылаются на один cut/junction, но на разные sides;
- K cuts closed spline дают piece `k` от `After(cut k)` до `Before(cut (k+1) mod K)`;
- point cuts получают `JunctionIndex = -1`;
- callers, которым incidence не нужна, продолжают читать `Pieces` без изменения публичного поведения.

### C. Exact network solve в PCG.Sweep

`SweepNetworkSolver` использует incidence, а не геометрический guess:

- topology cut-end прикрепляется только к своему `JunctionIndex`;
- nearest proximity attachment разрешён только для действительно свободного endpoint и помечается отдельным source kind;
- число exact cut-arms каждого junction сверяется с `Topology.Valency`; proximity arms считаются отдельно;
- duplicate stable arm key, finite/index mismatch, stale cut parameter или нарушенная valency завершают network solve как invalid;
- same-junction piece допустим, если sides/incidents различны; duplicate incident не repair'ится в Sweep;
- absorbed piece не создаёт strip, но его корректные incidence сохраняются для двух разных junction или двух разных sides настоящего self-loop.

Текущий `SeparateCorners` удаляется. Его заменяет bounded `JunctionLayoutSolver`, работающий по полным projected portal boundaries, а не по двум extrema:

1. Начальный setback — текущий mitre policy с документированным cap.
2. На каждой итерации строятся exact portals и полный boundary-intersection certificate.
3. Выбирается наиболее нарушенная соседняя пара в stable order.
4. Минимальный дополнительный setback ищется bracket + bisection в доступном edge budget; fixed additive growth запрещён.
5. После clamp проверяется фактический `AppliedDelta`. `AppliedDelta <= scaleEpsilon` при remaining violation даёт `Infeasible`.
6. После absorption layout и все constraints пересчитываются.
7. Успех требует повторной полной проверки portals, connector boundaries и edge budget.

### D. Geometry/render split

`SweepProfileChains` и extreme-X approximation удаляются из junction path.

Новый `JunctionPortalBuilder` использует те же frame/LUT/twist/terrain-free formulas, что strip ring, и выпускает:

- unique geometry boundary с profile-vertex identity;
- render mapping для duplicated hard edges и UV seam;
- open/closed connectivity прямо из `ProfileSegments`;
- boundary winding, согласованный с arm side/outward.

`SweepMeshBuilder.Cleanup` не используется как junction repair. Junction finalizer weld'ит только одинаковый geometry id внутри одного smoothing/UV chart; совпавшие position+UV не являются достаточным основанием сварки.

### E. Canonical-domain junction mesher

World XZ projection больше не является domain triangulation. Она используется только layout/collision oracle.

#### Open profiles

`OpenJunctionSurfaceBuilder` создаёт канонический convex parameter-domain disk:

- arms идут в stable cyclic order;
- каждому portal выделена boundary arc с полным ordered profile chain;
- промежутки заняты connector-rim samples;
- domain boundary по построению simple и не зависит от world projection;
- constrained triangulation должна вернуть ровно одну disk component.

Boundary vertices фиксируются в exact 3D portal/rim positions. Interior embedding решается `BoundaryConstrainedEmbedder`. Вся цепь `Ribbon`, все девять точек `HalfPipe` и все точки `Custom Open` участвуют в boundary; фильтра `upperOnly` нет.

#### Closed profiles

`ClosedJunctionSurfaceBuilder` создаёт canonical planar domain с `N` boundary loops: stable arm `0` — outer loop, остальные `N-1` — holes. Это parameterization genus-zero N-port surface; выбор outer arm определяется stable key, а не случайным порядком dictionary.

Каждая boundary loop содержит полный unique closed profile. Верх/низ/стенки не выделяются эвристикой, поэтому concave и невертикальный silhouette не теряются. Hard-edge/UV duplicates добавляются после geometry validation.

#### Domain triangulation dependency

Не писать третий permissive ear clip. Использовать `Clipper2ZLib.Delaunay`, уже поставляемый `PCG.Polygons`, только для integer canonical parameter-domain:

- `PCG.Sweep` получает одностороннюю package/asmdef dependency на `PCG.Polygons`;
- quantization применяется к dimensionless domain, не к world vertices;
- world portal coordinates не округляются;
- triangulator возвращает connectivity и boundary ids; невозможность constrained triangulation — ошибка, не fallback fan.

#### Boundary-constrained embedding

`BoundaryConstrainedEmbedder`:

1. Держит все portal vertices fixed.
2. Строит deterministic harmonic initial embedding отдельным solve по X/Y/Z в stable vertex order.
3. Выполняет bounded local/global fairing с orientation/intersection barrier; iteration count, tolerances и backtracking schedule — constants, включённые в mesh version salt.
4. Проверяет non-adjacent triangle candidates пространственным BVH/hash на каждой принятой итерации.
5. При fold выполняет deterministic refinement только затронутых domain triangles, не более трёх levels и в общем vertex budget.
6. Если valid embedding не получена, возвращает `Infeasible`/`NumericFailure`; mesh не публикуется.

Алгоритм считается реализованным не по факту окончания iterations, а только по certificate `JunctionMeshValidator`.

### F. JunctionMeshValidator

Validator работает до render splits и до materialization:

- finite positions/UV and valid indices;
- ни одного duplicate face;
- scale-aware triangle quality
  `q = 2*sqrt(3)*|cross| / (e0^2 + e1^2 + e2^2) >= 1e-4`;
- area epsilon относительно `junctionScale^2`, а не только абсолютный `1e-8`;
- согласованный edge winding и ожидаемая Euler characteristic;
- отсутствие intersections не-соседних triangles в 3D;
- portal boundary bijection edge-to-edge ровно один раз;
- expected boundary loops и connected components;
- все profile segments представлены, ни один silhouette edge не потерян;
- AABB/vertex radius ограничены фактическими portals и layout certificate, а не self-referential `maxSetback` threshold;
- UV charts finite, seams явно duplicated, случайной сварки между charts нет;
- hard vertex/index limits соблюдены до больших аллокаций.

Forced `bestIdx`, удаление ring vertex ради ear clip и публикация `Vertices == null` patch рядом с успешными patches запрещены.

### G. Atomic executor contract

Network compute имеет три исхода:

- authored empty/invalid input: один точный diagnostic, пустой committed result, единый finalize удаляет owned stale objects;
- cancellation: новый output не commit'ится, последний committed result остаётся до trailing recompute;
- topology/layout/mesher internal failure на непустой сети: весь network compute fail, strips и другие patches этой версии не публикуются.

`BuildJunctionResults` не пропускает молча invalid patch. Commit выполняется только после успешной валидации всех indexed strip/patch slots.

## Threading, determinism and performance

- Unity `Spline` API и Unity objects читаются только на main thread при immutable capture.
- Circular topology solve, canonical domain, embedding, validation и mesh arrays работают по pure data в bounded worker batches.
- `JunctionMaterial`/`Material`/`TerrainData` не читаются worker geometry code.
- Стабильные сортировки всегда имеют tie-breaker: stable arm key, source spline, cut index, side, piece index.
- Dictionary iteration не определяет output order.
- Cancellation/progress — минимум каждые `1024` candidate/vertex/triangle operations.
- Нельзя создавать unbounded task на каждый junction; использовать bounded batches с indexed result slots.
- До крупных аллокаций вычисляется upper bound vertices/indices. Limit `2_000_000` применяется отдельно к каждому strip/patch и ко всему compute budget; превышение даёт diagnostic, не OOM.
- Embed/refine имеет фиксированные iteration/refinement budgets; recursive repair без hard bound запрещён.
- Mesh version salt включает topology/embedding algorithm revision и численные constants.
- Повторный compute, cache clear и domain reload дают одинаковые ordered mesh hashes.

## Files

### PCG.Splines

- `Editor/Scripts/Network/SplineIntersectionSolver.cs` — circular canonical incidents.
- `Editor/Scripts/Network/CutParam.cs`, `SplitVertex.cs`, `SplineSplitSolver.cs`, `SplineSplitResult.cs` — provenance/incidence.
- Новые internal файлы `PieceEndpointSource.cs`, `PieceEndpointSide.cs`, `PieceEndpointIncidence.cs`, `SplinePieceIncidence.cs`.
- Постоянная Editor test assembly для network topology/split.
- `Documentation~/PCG.Splines/Splines/Spline-Intersection-Node.md`, `Split-Splines-Node.md`, `Docs/SPLINES_MAP.md`.

### PCG.Sweep

- `SweepNetworkSolver.cs`, `SweepNetworkSolveResult.cs`, `SweepNetworkArm.cs`, `SweepNetworkJunction.cs`, `SweepNetworkSnapshot.cs`.
- `SweepSplineNodeExecutor.cs` — exact incidence, bounded batches, atomic commit.
- `SweepJunctionMeshBuilder.cs` заменяется orchestration facade; permissive loop triangulation удаляется.
- `SweepProfileChains.cs` удаляется после перевода всех callers.
- Новые internal файлы:
  - `JunctionPortal.cs`, `JunctionPortalBuilder.cs`;
  - `JunctionLayoutResult.cs`, `JunctionLayoutSolver.cs`;
  - `JunctionDomain.cs`, `JunctionDomainTriangulator.cs`;
  - `OpenJunctionSurfaceBuilder.cs`, `ClosedJunctionSurfaceBuilder.cs`;
  - `BoundaryConstrainedEmbedder.cs`;
  - `JunctionMeshCertificate.cs`, `JunctionMeshValidator.cs`;
  - `JunctionRenderMeshBuilder.cs`.
- `package.json`, `PCG.Sweep.Editors.asmdef` — one-way `PCG.Polygons` dependency.
- Постоянная `Tests/Editor/PCG.Sweep.Editor.Tests.asmdef`.
- `Documentation~/Sweep-Addon.md`, `Docs/SWEEP_MAP.md`.

Не менять `*.meta` вручную. Не трогать `Assets/Plugins/PCG4U` и core DLL в этой работе.

## Execution plan

Units выполняются строго по порядку. Production unit не начинается, пока предыдущий gate не зафиксирован.

### Unit 0 — Frozen regressions, independent oracles and baseline

**Goal:** до production edits создать постоянные fixtures/oracles, которые воспроизводят дефект и не зависят от текущего builder.

**How:**

- Зафиксировать pure-data fixture точной `SweepDemoScene`: closed 8-knot spline, open 4-knot spline, linked seam, node parameters.
- Зафиксировать upstream seam fixture `cuts at 0.02 and L-0.041`, `MergeDistance = 0.5`.
- Добавить independent 3D triangle intersection oracle, edge-incidence/Euler oracle, ordered portal seam oracle и scale-aware quality oracle.
- Снять baseline отдельно: main-thread capture/solve, worker wall time, allocations/peak memory, materialization/collider cooking для 100/1000 junction.
- Existing bad output обязан давать RED: seam topology, real SweepDemo, `HalfPipe X10 maxCovAll`.

**Gate:** тесты доказуемо падают на текущем коде по ожидаемой причине; oracle unit tests проходят на вручную заданных valid/invalid meshes.

**On failure:** исправлять fixture/oracle, не production code и не expected output.

### Unit 1 — Circular incidents and split provenance

**Goal:** canonical topology и exact piece incidence в `PCG.Splines`.

**How:** реализовать Architecture A/B без Sweep-local repair.

**Gate:**

- closed cuts `0.02` и `L-0.041`, gap `< 0.5` → один cut;
- тот же geometry после переноса spline seam/knot index → тот же topology hash;
- wrapped gap `> MergeDistance` → два cuts;
- настоящий self-intersection с большой `deltaL` → два cuts, valency `4`;
- linked closed seam + open endpoint → `2` cuts, один junction valency `3`;
- closed one-cut → один full-length open piece, start/end с одним cut id и разными sides;
- closed two-cut same-junction → два legitimate pieces с разными cut ids;
- conflict junction ids → `InvalidValues`, не nearest choice;
- все старые X/T/Y/endpoint/bridge/split-shape gates остаются зелёными.

**On failure:** нельзя переносить canonicalization в Sweep или повышать hardcoded split epsilon.

### Unit 2 — Exact incidence, layout status and portals

**Goal:** Sweep строит корректные arms/portals или возвращает диагностированный solve failure.

**How:** реализовать Architecture C/D; удалить `ResolveAttach` для topology cuts и текущий `SeparateCorners`.

**Gate:**

- точный SweepDemo solve: `6 cuts`, `6 pieces`, target junction `3 arms`, нет `0.061` duplicate piece;
- ни один target arm не дальше layout certificate; нет радиусов `35-51` м;
- два пространственно близких junction не меняют cut attachment;
- closed one-cut self-loop сохраняет два sides и не удаляется;
- non-progress/saturated layout → `Infeasible` за bounded iterations;
- every portal bit-exact совпадает с strip end ring по ordered geometry edges;
- single mode golden hashes всех четырёх profiles не изменились.

**On failure:** нельзя удалять short pieces или сливать junction по `Step`/profile extent.

### Unit 3 — Canonical-domain mesher

**Goal:** полный open/closed profile строится без extreme-X loss, permissive triangulation и partial geometry.

**How:** реализовать Architecture E/F; подключить canonical-domain Delaunay; удалить junction path через `SweepProfileChains`.

**Gate:** cross-product:

- Profiles: `Ribbon`, `Rectangle`, `HalfPipe`, asymmetric `Custom Open`, convex/reversed/concave simple `Custom Closed`.
- Layouts: valency-2 straight/bent, T, irregular Y, X90, X10, near-tangent, valency 5/8, close bridge, terminal stab, real linked seam.
- Frames: horizontal, sloped, rolled, vertical-ish, differing arm up vectors.
- Modulation: unequal width/height, twist `0/45/90`, global-T continuity.
- Terrain: none, flat, slope, uneven, partial out-of-bounds.

Каждый fixture проходит полный `JunctionMeshValidator`; `HalfPipe` проверяется целиком, без `upperOnly`. Bow-tie/non-simple domain и injected numeric failure дают error без triangles.

**On failure:** запрещено добавлять forced ear/fan, удалять portal vertices или ослаблять oracle по normal direction.

### Unit 4 — Executor, cancellation and atomic materialization

**Goal:** network output публикуется только целиком и через существующий maker path.

**Gate:**

- invalid patch среди valid patches не публикует ни strips, ни соседние patches новой версии;
- cancellation на split/layout/embed/validation/materialization не оставляет partial objects;
- trailing recompute публикует только последнюю version;
- Generate/Clear, disable, empty inputs и stale topology удаляют owned objects единым finalize;
- output order/names/material fallback/collider сохранены;
- save/reopen и non-identity parent корректны;
- 20 повторов, cache clear и domain reload дают exact ordered mesh hash.

**On failure:** нельзя возвращать частичную сеть или обходить `IInstanceMakerContainer`.

### Unit 5 — Performance and real Editor acceptance

**Goal:** доказать качество на полном workflow и бюджеты.

**Gate:**

- Открыть `Assets/SweepDemo/SweepDemoScene.unity`, выполнить реальный `Spline Intersection → Sweep Spline → Result`, регенерировать текущим кодом.
- Для target linked junction: valency `3`, локальные portals, простой patch, ноль non-adjacent 3D intersections, portal seam bijection.
- Ожидаемая materialization: `6` strips + `3` junction objects для текущего fixture.
- Сохранить diagnostic JSON и top/oblique/underside wireframe captures для всех пяти profile variants на T/X/acute fixtures.
- Benchmark 100/1000 junction, valency 2/3/4/8, profile 2/9/16/64/256 points, terrain/collider off/on.
- Не хуже frozen baseline по main-thread stall; worker/memory thresholds фиксируются в Unit 0 до реализации и не переписываются под результат.

Runtime observation выполняется через Unity Bridge Editor task, не обычным Play Mode. Bridge tasks/assertions не редактируются ради PASS.

### Unit 6 — Documentation reconciliation

**Goal:** документация соответствует фактическому контракту.

**How:**

- Удалить из `Sweep-Addon.md` и `SWEEP_MAP.md` утверждения о no-overlap «by construction», current 64-pass separation, forced clip и accepted limitation невертикального silhouette.
- Описать canonical closed-seam incidents, exact topology provenance, full-profile N-port patch, atomic failure и UV chart contract.
- Обновить `Spline Intersection`/`Split Splines` docs circular/provenance semantics.
- Указать one-way dependency Sweep → Polygons.

**Gate:** doc claims имеют прямые permanent test anchors; старые superseded TDD не используются как current behavior.

## Acceptance matrix

Обязательные проверки для каждого релевантного fixture:

| Область | Критерий |
|---|---|
| Topology | canonical cuts, correct valency, exact incidence ids/sides |
| Data | finite positions/UV, indices in range, no duplicate faces |
| Quality | scale-aware `q >= 1e-4`, no relative-area degenerates/slivers |
| Surface | no non-adjacent 3D triangle intersections |
| Topology mesh | connectedness, expected boundary loops, Euler characteristic |
| Seam | ordered portal edge bijection exactly once |
| Profile | every profile segment represented; no extreme-X silhouette loss |
| Winding | orientable and consistent; outward/front-facing contract preserved |
| UV | finite charts, explicit seams, no accidental cross-chart weld |
| Layout | bounded certificate, no spikes/distant arms/self-referential threshold |
| Terrain | world-space drape preserves seams/topology |
| Determinism | exact ordered mesh hash over reruns/reload |
| Workflow | executor, maker, collider, Generate/Clear, save/reopen |
| Regression | single mode bitwise golden for every profile |

Projected coverage — дополнительный oracle, не замена 3D checks. Для closed `Rectangle` верх и низ закономерно перекрываются в XZ и проверяются как разные sheets/charts; для open `HalfPipe` проверяется вся единственная surface, а не только upward normals.

## Failure diagnostics

Diagnostic должен включать node address, junction index, stable arm keys и certificate summary. Минимальные коды:

- `InvalidTopologyCut`;
- `TopologyIncidenceMismatch`;
- `DuplicateIncident`;
- `LayoutInfeasible`;
- `DomainTriangulationFailed`;
- `EmbeddingInfeasible`;
- `SelfIntersection`;
- `PortalSeamMismatch`;
- `MeshBudgetExceeded`;
- `InvalidCustomProfile`.

Один compute не спамит одинаковым warning на каждый triangle/arm.

## Done condition

Работа выполнена только когда:

1. Все Units 0-6 закрыты по порядку.
2. Permanent Editor tests зелёные без изменения ожидаемых значений под фактический output.
3. Реальная `SweepDemoScene` регенерирована текущим executor и проходит topology/3D/seam gates.
4. `HalfPipe maxCovAll=7` больше не принимается как PASS.
5. Нет forced triangulation, Sweep-local topology simplification, nearest guess для cuts, partial network publication и accepted profile limitation.
6. Обычный sweep без `Topology` совпадает с golden arrays/hashes.
7. Документация и карты синхронизированы с тестируемым поведением.

Stop rule: после трёх подряд провалов одного и того же gate без нового доказанного изменения причины остановиться, сохранить diagnostic artifacts и отчитаться. Нельзя ослаблять oracle, менять fixture или расширять tolerance без отдельного доказательства scale/numeric contract.

## End-of-run report

Исполнитель обязан перечислить:

- закрытые Units и exact test/Bridge artifacts;
- изменения topology counts/valency на SweepDemo;
- mesh certificates по каждому profile/layout family;
- performance baseline/final numbers;
- отклонения от этого TDD с обоснованием;
- оставшиеся failures и причину остановки, если Done не достигнут.

Status меняется на `Выполнено` только после полного Done condition. Validator green не заменяет визуальный Editor gate, а screenshots не заменяют geometry certificates.
