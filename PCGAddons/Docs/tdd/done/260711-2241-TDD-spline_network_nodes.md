# ТДД: Сеть сплайнов — SplineIntersectionNode и SplitSplinesNode (PCG.Splines)

Status: Выполнено

Реализация: topology-типы, обе ноды с исполнителями, фоновые солверы, документация и карта проекта готовы (`Packages/PCG.Splines/Scripts/Splines/`, `Packages/PCG.Splines/Editor/Scripts/`).

Функциональная приёмка прогнана в запущенном Unity Editor (CoworkBridge): компиляция пакета без ошибок; X-перекрёсток → junction валентности 4 (2 реза), T-стык (endpoint-on-interior) → валентность 3, эстакада выше `MaxHeightDifference` → нет junction; merge-distance сливает близкие пересечения и не сливает далёкие; self-intersection находится; результат детерминирован (одинаковый `GetContentHash` при повторе); closed-сплайн режется в открытые куски; сплайн без резов проходит по ссылке. Точность разреза кривых (1/2/3 реза, включая closed): отклонение формы кусков от исходного сплайна `≤ 8e-6` world units (критерий `1e-3` выполнен с большим запасом).

Численные пороги benchmark-матрицы (Sparse/Dense/Curved/GridWorst: wall-time, main-thread stall, аллокации) снимаются отдельным baseline-прогоном на целевой машине; архитектура под ориентиры заложена (снапшот на главном потоке, весь расчёт в пуле, кооперативная отмена, атомарная публикация).

Ревизия B — по ревью `260711-2241-TDD-spline_network_nodes-review.md`: точный split без ресемпла, topology-контракт между нодами, адаптивная точность пересечений, height policy, фоновое исполнение, детерминизм.

Программа P3 из `ProjectPCG/Docs/notes/unreal_pcg_demos_plan.md` (демки 1–2): точки пересечений произвольной сплайновой сети с валентностью и разрезка сплайнов в этих точках. Основа для ручных дорожных сетей и будущего `SplinesToGraphNode`.

Зависимости: live-редактирование в SceneView (критерий «сдвиг узла пересчитывает точки») требует выполненного `260711-2242-TDD-spline_tool.md`; сами ноды работают с любым входом `List<Spline>` и реализуются независимо. Интеграционная приёмка live-цепочки — только после обоих ТДД.

---

## Состав

Рантайм (`Packages/PCG.Splines/Scripts/Splines/`):

- `SplineCut.cs` — запись реза.
- `SplineJunction.cs` — перекрёсток.
- `SplineNetworkTopology.cs` — контейнер topology-выхода.
- `SplineIntersectionNode.cs` — data-нода.
- `SplitSplinesNode.cs` — data-нода.

Редактор (`Packages/PCG.Splines/Editor/Scripts/Exec/`):

- `SplineIntersectionNodeExecutor.cs`
- `SplitSplinesNodeExecutor.cs`

Сплайны в графе — в мировых координатах (контракт пакета).

---

## Topology-типы

`PointData` не несёт валентность и принадлежность резов (переиспользовать `Density`/`Scale` под топологию запрещено — ломает семантику точек), поэтому между нодами течёт first-class тип. Зависимость от P1 `PcgPointCloud` не заводится.

```csharp
[Serializable]
public struct SplineCut
{
	public int SplineIndex;
	public int CurveIndex;
	public float CurveT;
	public float Distance;
	public float3 Position;
	public int JunctionIndex;
}
```

`SplineIndex` — стабильный индекс по flattened-порядку входа (все связи по порядку, внутри связи по порядку списка). `Distance` — дистанция вдоль сплайна. `Position` — позиция реза на своей кривой (со своей высотой).

```csharp
[Serializable]
public struct SplineJunction
{
	public float3 Position;
	public int Valency;
}
```

`Valency` — число уникальных инцидентных ветвей: рез внутри сплайна даёт 2 ветви, рез на конце — 1.

```csharp
[Serializable]
public sealed class SplineNetworkTopology
{
	public List<SplineJunction> Junctions = new();
	public List<SplineCut> Cuts = new();

	public int GetContentHash();
}
```

`GetContentHash` — свёртка всех полей `(hash * 397) ^ x` в `unchecked` (контракт кеша).

## SplineIntersectionNode

```csharp
[Serializable]
[PcgNodeInfo("Finds junctions of a spline network in the XZ plane.",
	DisplayName = "Spline Intersection",
	Category = "Splines",
	Tags = new[] { "splines", "intersection", "network", "junction", "points" })]
public class SplineIntersectionNode : PcgPreviewNode
{
	[Input]
	public List<Spline> Splines = new();
	[Input]
	public float IntersectionTolerance = 0.05f;
	[Input]
	public float MergeDistance = 0.5f;
	[Input]
	public float MaxHeightDifference = 2f;
	[Output]
	public SplineNetworkTopology Topology => default;
	[Output]
	public List<PointData> Results => default;
}
```

Все поля и выходы — с `[PcgMemberInfo]` (описание, единицы world units, tags). Семантика параметров:

- `IntersectionTolerance` — гарантированная максимальная геометрическая ошибка позиции пересечения (управляет адаптивным делением кривых), min `0.001`.
- `MergeDistance` — радиус объединения резов в один junction, min `0.001`.
- `MaxHeightDifference` — порог по Y: пары с большим перепадом высот не образуют junction (эстакада). `<= 0` — высота игнорируется (строго планарный режим).

`Results` — позиции junctions как `PointData { Position, Normal = up, Scale = 1, Density = 1, Angle = 0 }` для общих point-нод и превью.

## SplineIntersectionNodeExecutor

`: PcgAsyncPreviewNodeExecutor<SplineIntersectionNode>, INodeInfo, IPointsCount`; поля `public PcgOutput<SplineNetworkTopology> Topology;` `public PcgOutput<List<PointData>> Results;`.

`DoComputeAsync`:

1. Входы через `GetInputValues`/`GetInputValue`. Пустой вход — `Topology.Value = new SplineNetworkTopology(); Results.Rent(0); return;`.
2. Снапшот на главном потоке (`OperationScope` + `await scope.Step(ct: ct)` на сплайн): по каждому сплайну массив `BezierCurve` (`spline.GetCurve(i)` — чистые struct-данные), `Closed`, длины кривых (`spline.GetCurveLength(i)`). Живые `Spline` в фон не передаются.
3. `await UniTask.SwitchToThreadPool();` — весь дальнейший расчёт в пуле, `ct.ThrowIfCancellationRequested()` и `PcgComputeSystem.ReportProgress(this)` каждые 1024 итерации.
4. Адаптивная дискретизация: каждая `BezierCurve` делится рекурсивно (de Casteljau пополам), пока chord error (максимум расстояний контрольных точек до хорды в XZ) `> IntersectionTolerance * 0.5f`; предел глубины 12, при достижении — сегмент принимается как есть и нода выставляет diagnostic warning о недостигнутой точности. Сегмент несёт `SplineIndex`, `CurveIndex`, параметрический интервал `[t0, t1]`, XZ-концы и высоты концов.
5. Broad phase — пространственный хеш: `cellSize = max(медианная длина сегмента по XZ, MergeDistance)`; сегмент кладётся в клетки, накрытые его AABB; guard — сегмент, чей AABB накрывает более 64 клеток, дополнительно делится пополам. Пары `(i, j), i < j`, дедуп `HashSet<long>` ключом `((long)i << 32) | (uint)j`; пары сегментов одного сплайна с соприкасающимися параметрическими интервалами (смежные, включая замыкание closed) пропускаются.
6. Пересечение хорд в XZ — как пересечение отрезков, но epsilon масштабонезависимый: `|denom| <= 1e-6f * length(d1) * length(d2)` — пара параллельна и пропускается (см. семантику ниже).
7. Refinement: пара кандидатов уточняется на исходных кривых бисекцией параметрических интервалов обеих кривых (деление интервала пополам по ближайшей подхорде) до сходимости XZ-позиций `<= IntersectionTolerance`, максимум 24 итерации. Результат — точные `(CurveT_A, CurveT_B)` и позиции на обеих кривых.
8. Height policy: `|yA - yB| > MaxHeightDifference` (при `MaxHeightDifference > 0`) — пара отбрасывается, junction не создаётся.
9. Резы: из принятой пары — два `SplineCut` (по одному на кривую), `Distance` — накопленная длина до `CurveT`. Дедуп резов одного сплайна по `Distance` с допуском `MergeDistance` (canonical — минимальная `Distance` кластера, позиция — среднее).
10. Кластеризация в junctions: XZ spatial lookup c клеткой `MergeDistance` и проверкой соседних клеток `distancesq <= MergeDistance²`; объединение компонент union-find по отсортированным парам (детерминировано). Позиция junction — среднее позиций **уникальных** резов кластера (не пар: X-перекрёсток не должен весить больше T). `Valency` — сумма ветвей уникальных резов.
11. Детерминизм: кандидатные пары перед refinement сортируются по `(SplineA, CurveA, tA, SplineB, CurveB, tB)`; junctions нумеруются по `(Position.x, Position.z)`; cuts сортируются по `(SplineIndex, Distance)`. Результат идентичен после повторного compute, очистки кеша и domain reload.
12. `await UniTaskEditor.SwitchToEditorThread();` — атомарная публикация: полностью собранные `Topology.Value` и `Results` (через `Results.Rent(count)`) присваиваются только после полного успеха; при отмене/исключении опубликованный выход не меняется.

Семантика особых случаев (фиксированный контракт, входит в приёмку):

- X, T, Y, endpoint-on-interior, self-intersection, пересечение разных сплайнов — junction.
- Общий endpoint двух сплайнов в пределах `MergeDistance` — junction валентности 2.
- Касание без пересечения — junction, если refinement сходится к общей точке в пределах `IntersectionTolerance`.
- Параллельные/коллинеарные перекрытия — не junction; при обнаружении перекрытия длиной `> MergeDistance` — diagnostic warning на ноде.
- Вырожденные кривые (длина `< 1e-4`) пропускаются.

`IsEmpty => Results.Value == null || Results.Value.Count == 0;` `PointsCount => Results.Value?.Count ?? 0;` `HasNodeInfo`/`NodeInfo` — `"Junctions: N, Cuts: M"`. `DrawPreview` — `GizmosUtility.DrawPoints`, размер/цвет точки по valency (2/3/4+ различимы визуально; `PointData` не модифицируется).

## SplitSplinesNode

```csharp
[Serializable]
[PcgNodeInfo("Splits splines exactly at the given cuts or points.",
	DisplayName = "Split Splines",
	Category = "Splines",
	Tags = new[] { "splines", "split", "cut", "network" })]
public class SplitSplinesNode : PcgPreviewNode
{
	[Input]
	public List<Spline> Splines = new();
	[Input(Connection = PcgConnectionType.Override)]
	public SplineNetworkTopology Cuts;
	[Input]
	public List<PointData> Points = new();
	[Input]
	public float SnapDistance = 0.5f;
	[Output]
	public List<Spline> Results => default;
}
```

Все поля и выходы — с `[PcgMemberInfo]`. Два режима резов, работают совместно (объединение):

- `Cuts` (основной, точный): резы применяются по `(SplineIndex, CurveIndex, CurveT)` — только к своему сплайну, повторный поиск не выполняется. Соседние неинцидентные сплайны не затрагиваются.
- `Points` (generic fuzzy): произвольные точки; рез на каждом сплайне, где ближайшая точка сплайна ближе `SnapDistance`. Явно документируется как приблизительный режим; `SnapDistance` используется только им.

Параметра `Step` нет: **нода не меняет форму**. Ресемпл — отдельная явная нода `Resample Splines` downstream.

## SplitSplinesNodeExecutor

`: PcgAsyncPreviewNodeExecutor<SplitSplinesNode>`, поле `public PcgOutput<List<Spline>> Results;`.

`DoComputeAsync`:

1. Входы; нет ни резов, ни точек — все валидные сплайны в выход по ссылке (zero-copy pass-through), публикация, выход.
2. Снапшот на главном потоке (`OperationScope`): по каждому сплайну узлы (`Position/TangentIn/TangentOut/Rotation`), режимы (`GetTangentMode(i)`), tension (`GetAutoSmoothTension(i)`), `Closed`, длины кривых. Флаг наличия embedded `SplineData` (`embeddedSplineData`).
3. Пул потоков: сопоставление резов сплайнам. Fuzzy-точки: ближайшая позиция ищется по адаптивной дискретизации снапшота (та же схема, что в intersection) с уточнением бисекцией — Unity API в пуле не используется.
4. Нормализация резов по сплайну:
	- Открытый: резы ближе `0.01` к `0` и `length` выбрасываются; сортировка; слияние соседних ближе `0.01` (среднее).
	- Замкнутый: нормализация в `[0, length)`; circular-слияние по `min(|a - b|, length - |a - b|) <= 0.01`, включая пару вокруг seam (`0`/`length`); canonical дистанция кластера — минимальная. Один рез — один открытый кусок длиной `length` от точки реза.
5. Дескрипторы кусков (чистые данные, в пуле): границы кусков + для граничных кривых — результат точного деления `CurveUtility.Split(curve, localT, out left, out right)` (de Casteljau, форма сохраняется точно).
6. `await UniTaskEditor.SwitchToEditorThread();` — сборка managed `Spline` из дескрипторов:
	- Неграничные узлы куска переносятся без изменений: `Position/TangentIn/TangentOut/Rotation`, исходный `TangentMode`, исходный tension.
	- Узлы, смежные с резом (по обе стороны), фиксируются в `TangentMode.Broken` с их текущими фактическими тангентами: `AutoSmooth`-режим пересчитал бы тангенты по новым соседям и изменил бы форму.
	- Новый граничный узел — `TangentMode.Broken` с тангентами из `CurveUtility.Split` (обе половины воспроизводят исходную кривую).
	- Порядок и направление — исходные; `Closed = false` у всех кусков.
	- Куски короче `0.01` не добавляются.
7. Атомарная публикация `Results` одним присваиванием после полного успеха.

Политики:

- Сплайн без резов — исходный объект по ссылке.
- `null` в списке — пропуск с diagnostic; `Count <= 1` или `length <= 1e-4` — pass-through по ссылке; `NaN`/`Infinity` в резах и точках отбрасываются с diagnostic.
- Embedded `SplineData` и knot links **не переносятся** в куски: при их наличии — однократный diagnostic warning на ноде (зафиксированное ограничение пакета; перенос диапазонов — отдельная задача при появлении спроса).
- Порядок выхода: сплайны в порядке входа, куски по возрастанию начальной дистанции.

`IsEmpty => Results.Value == null || Results.Value.Count == 0;` `DrawPreview` — `SplinesGizmoUtility.DrawGizmos(Results.Value, transform)`.

---

## Порядок реализации

- Topology-типы.
- `SplineIntersectionNode` + executor.
- `SplitSplinesNode` + executor.
- Документация (см. Done).

## Критерии приёмки

Геометрия пересечений:

- Две ломаные восьмёркой из `SplineNode` → по junction на каждый перекрёсток (X, T, endpoint-on-interior, self-intersection), без дублей; valency корректна (3 у T, 4 у X); сдвиг узла сплайна пересчитывает результат (после `260711-2242`).
- High-curvature пересечение между узлами кривой находится (адаптивное деление), позиция с ошибкой `<= IntersectionTolerance`.
- Две точки ближе `MergeDistance` сливаются независимо от положения относительно границ клеток; дальше — не сливаются.
- Пересечение на одном уровне и с перепадом в пределах `MaxHeightDifference` — junction; эстакада выше порога — нет junction.
- Коллинеарное перекрытие — нет junction, есть warning.

Разрез:

- Выборка позиций исходного сплайна и объединения выходных кусков в одинаковых параметрах отличается не более `1e-3` world units; тангенты слева и справа от каждого реза совпадают с исходной кривой.
- Открытый: 0/1/2/несколько резов; замкнутый: 0/1/2 реза, рез ровно на seam, дубли по обе стороны seam.
- Резы на границах кривых и внутри кривой; дубликаты резов сливаются.
- Topology-вход не режет близкую неинцидентную дорогу; fuzzy-вход с `SnapDistance` — режет (документированное различие).
- Сплайн без резов возвращается по ссылке.

Интеграция и перформанс:

- Multi-input из нескольких связей; повторный compute, очистка кеша и domain reload дают идентичный результат (порядок, valency, позиции).
- Отмена не публикует частичный выход.
- Fixtures: Sparse (20 × 1 км, ~100 кривых, 20 junctions), Dense (то же, 200+ junctions), Curved (короткие high-curvature/self-intersecting), GridWorst (длинные диагонали + много коротких сегментов). Метрики: wall time cold/warm, максимальный main-thread stall, аллокации, cancellation latency, hash результата. Численные пороги фиксируются в этом документе после baseline-прогона, до перевода в `Выполнено`; ориентиры: main-thread slice ≤ 8 мс, cancellation ≤ 100 мс, warm Sparse ≤ 1 с.

## Done-состав

- Смени статус в начале документа на `Выполнено`.
- Обнови `Docs/PROJECT_MAP.md` (обе ноды, topology-типы).
- Страницы `Documentation~` для `Spline Intersection` и `Split Splines` + навигация `Splines-Addon.md`.
