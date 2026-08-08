Status: Выполнено

# RegionToMesh: иерархический boundary-клип и параллельная сборка

## Замеры-основание (CityForestV4, District City V4)

- Полная генерация District City V4 — 115 с; `RegionToMeshNodeExecutor` — 46-50 с активного времени, крупнейший потребитель сцены (35%).
- Постадийная раскладка одного вызова `RegionMeshBuilder.BuildFromHeightSampler` (1 merged-полигон, 1908 вершин контура, листья 1777 interior / 4330 boundary, 19440 треугольников): Union — 26 мс, Tree — 744 мс, Interior — 21 мс, **Boundary — 49222 мс (98%)**, Vertex — 28 мс.
- Причина: `AppendBoundary` для каждого из 4330 boundary-листьев зовёт `PolygonClipper.Intersection` ячейки против всего merged-набора (1908 вершин) плюс `Triangulate`. Стоимость каждого клипа пропорциональна полному контуру, а не локальной геометрии ячейки.
- Вторичная причина: `MeshQuadtree.Classify` линейно сканирует все сегменты контура для каждой рассматриваемой ячейки, `RegionContains` — point-in-polygon по всем вершинам для каждой не-boundary ячейки (744 мс Tree).

## Целевые показатели приёмки

- Boundary-стадия на том же входе CityForestV4: ≤ 1000 мс (сейчас 49222 мс).
- Tree-стадия: ≤ 300 мс (сейчас 744 мс).
- Полная генерация District City V4: ≤ 75 с (сейчас 115 с).
- Геометрический паритет: относительное отклонение суммарной площади треугольников от площади Union ≤ 1e-3; вершины на границах полигона могут сместиться не более чем на 2 мм относительно старого результата (двойное квантование Clipper2 на сетке 1 мм).
- Дерево листьев (`MeshQuadtree.Leaves`) — идентично старому поведению бит-в-бит: набор ключей и флагов Boundary не меняется.
- Детерминизм: два прогона на одном входе дают бит-в-бит одинаковые `Vertices`/`Uvs`/`Triangles`.

## Обзор решения

Три изменения, каждое сохраняет публичные контракты:

- `MeshQuadtree` получает грид-индекс сегментов (`SegmentGrid`) и наследование классификации: ячейка без сегментов-кандидатов наследует Inside/Outside от родителя без point-in-polygon теста. Набор листьев не меняется.
- `RegionMeshBuilder` перестаёт клиповать каждый boundary-лист против полного merged-набора. Вместо этого — иерархический спуск от корневых ячеек глубины 0: кусок полигона клипуется ректом ячейки, потомки клипуются уже маленьким куском родителя. Клип boundary-листа работает против локального куска в десятки вершин вместо 1908.
- Сборка получает этапный API `Plan` / `BuildBoundaryChunk` / `Finish`, чтобы `RegionToMeshNodeExecutor` гнал независимые корневые ячейки параллельно через `PcgWorkerScheduler.RunIndexedAsync` с детерминированным слиянием по индексам слотов. Старые сигнатуры `Build` и `BuildFromHeightSampler` остаются и внутри используют тот же этапный путь последовательно — `RegionExtrudeBuilder`, `SweepRibbonCornerFanBuilder`, `SweepRibbonPatchBuilder` не меняются.

`Func<float2, float> heightSampler` вызывается только в `Plan` (ошибка высоты при дроблении) и `Finish` (драпировка вершин) — оба этапа однопоточные, потокобезопасность замыкания не требуется. `BuildBoundaryChunk` высоты не читает.

## Новый файл: `Packages/PCG.Polygons/Scripts/Geometry/SegmentGrid.cs`

Равномерный грид сегментов контуров merged-полигонов. Ячейка грида — `cellSize = maxCellSize` квадродерева, начало — `origin` квадродерева.

```csharp
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class SegmentGrid
	{
		public struct Segment
		{
			public float2 A;
			public float2 B;
			public float2 Min;
			public float2 Max;
		}

		public float2 Origin;
		public float CellSize;
		public int Cols;
		public int Rows;
		public Segment[] Segments;

		private List<int>[] _cells;

		public static SegmentGrid Build(IList<Polygon2D> merged, float2 origin, float cellSize, int cols, int rows)
	}
}
```

- `Build` собирает все сегменты `Outer` и `Holes` каждого полигона (та же нумерация обхода, что в текущем `MeshQuadtree.CollectSegments`), считает bbox сегмента и кладёт индекс сегмента во все ячейки грида, которые bbox пересекает. `_cells` — массив длины `Cols * Rows`, элемент `null`, пока ячейка пуста.
- `public void CollectCandidates(float2 min, float2 max, List<int> buffer)` — очищает `buffer`, обходит ячейки грида, накрытые прямоугольником, добавляет индексы сегментов без дубликатов. Для дедупликации — массив `int[] _stamp` длины `Segments.Length` и счётчик поколения `_stampGeneration`: сегмент добавляется, если `_stamp[i] != _stampGeneration`. `CollectCandidates` непереиспользуем из нескольких потоков — он нужен только в однопоточном `MeshQuadtree.Build`.
- Прямоугольники запросов всегда лежат внутри `[Origin .. Origin + CellSize * (Cols, Rows)]`; индексы ячеек клампить в диапазон.

## Изменения: `Packages/PCG.Polygons/Scripts/Geometry/MeshQuadtree.cs`

- Поле `private List<(float2 A, float2 B, float2 Min, float2 Max)> _segments` заменить на `private SegmentGrid _segmentGrid` и `private readonly List<int> _candidateBuffer = new();`.
- В `Build` после вычисления `Origin`, `cols`, `rows` создавать `_segmentGrid = SegmentGrid.Build(merged, tree.Origin, maxCellSize, cols, rows)`. Метод `CollectSegments` удалить.
- `Classify(float2 min, float2 max)` переписать:

```csharp
private CellClass Classify(float2 min, float2 max, CellClass inherited)
{
	_segmentGrid.CollectCandidates(min, max, _candidateBuffer);

	for (int i = 0; i < _candidateBuffer.Count; i++)
	{
		var s = _segmentGrid.Segments[_candidateBuffer[i]];
		if (s.Max.x < min.x || s.Min.x > max.x || s.Max.y < min.y || s.Min.y > max.y)
			continue;
		if (SegmentIntersectsRect(s.A, s.B, min, max))
			return CellClass.Boundary;
	}

	if (inherited != CellClass.None)
		return inherited;

	float2 center = (min + max) * 0.5f;
	return RegionContains(center) ? CellClass.Inside : CellClass.Outside;
}
```

- В enum `CellClass` (`Scripts/Geometry/CellClass.cs`) добавить значение `None` первым элементом.
- Clipper2ZLib (`Scripts/Clipper2/`) не содержит мутабельного статического состояния — статические хелперы `Clipper.Intersect`/`Union` создают локальные экземпляры движка. Параллельные вызовы из чанков безопасны; новых блокировок не требуется.
- `Subdivide`: элемент стека расширить до `(int Depth, int Ix, int Iz, CellClass Inherited)`; начальный push — `Inherited = CellClass.None`. После классификации ячейки: если `_candidateBuffer.Count == 0` и класс Inside или Outside — потомкам передавать этот класс как `Inherited`; иначе потомкам передавать `CellClass.None`. Условие наследования — именно пустой список кандидатов текущей ячейки: bbox-кандидаты могли не пересечь ячейку геометрически, но пересечь ячейку потомка нельзя, только если кандидатов не было вовсе; при непустом списке потомки обязаны классифицироваться сами.
- `Balance` вызывает `Subdivide(n.Depth + 1, ...)` — эти вызовы передают `CellClass.None`.
- Публичный контракт (`Build` сигнатура, `Leaves`, `TryFindLeaf`, `HasFinerNeighbor`, `CellSize`, `CellMin`) не меняется. Итоговый набор листьев обязан совпасть со старым бит-в-бит — это проверяет тест паритета классификации.

## Новый файл: `Packages/PCG.Polygons/Scripts/Geometry/RegionMeshPlan.cs`

Иммутабельный (после `Plan`) снапшот всего, что нужно чанкам и финишу:

```csharp
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class RegionMeshPlan
	{
		public List<Polygon2D> Merged;
		public MeshQuadtree Tree;
		public float PlaneY;
		public Func<float2, float> HeightSampler;
		public float HeightOffset;
		public float UvScale;
		public HashSet<(int Depth, int Ix, int Iz)> BoundaryBranch;
		public List<(int Ix, int Iz)> BoundaryRoots;
		public bool FlatPath;
		public List<float2[]> FlatTriangles;
	}
}
```

- `FlatPath == true` — ветка без террейна (`heightSampler == null || maxCellSize <= 0`): `FlatTriangles` уже содержит результат `PolygonClipper.Triangulate(Merged)`, `Tree`, `BoundaryBranch`, `BoundaryRoots` — null, чанков нет.
- `BoundaryBranch` — ключи всех boundary-листьев плюс все их предки до глубины 0 включительно.
- `BoundaryRoots` — корневые ячейки глубины 0, присутствующие в `BoundaryBranch`, отсортированные по `Iz`, затем по `Ix`. Порядок фиксирован — он определяет детерминизм слияния.

## Изменения: `Packages/PCG.Polygons/Scripts/Geometry/RegionMeshBuilder.cs`

Публичные сигнатуры `Build` и `BuildFromHeightSampler` не меняются. `BuildCore` разбивается на три этапа.

```csharp
public static RegionMeshPlan Plan(
	RegionSet region,
	Func<float2, float> heightSampler,
	float maxHeightError,
	float minCellSize,
	float maxCellSize,
	int maxDepth,
	float heightOffset,
	float uvScale)

public static List<float2[]> BuildBoundaryChunk(RegionMeshPlan plan, int rootIndex, CancellationToken ct)

public static RegionMeshData Finish(RegionMeshPlan plan, IReadOnlyList<List<float2[]>> boundaryChunks, CancellationToken ct)
```

- `Plan`: `PolygonClipper.Union` → если ветка плоская, `FlatPath = true`, `FlatTriangles = PolygonClipper.Triangulate(merged)` и выход. Иначе `ComputeBounds` → `MeshQuadtree.Build` → обход `Tree.Leaves`: для каждого boundary-листа добавить в `BoundaryBranch` его ключ и ключи всех предков `(depth - 1, ix >> 1, iz >> 1)` до глубины 0 → собрать и отсортировать `BoundaryRoots`.
- `BuildBoundaryChunk` — иерархический клип одного корня. Локальная рекурсия (или явный стек) без обращения к общему изменяемому состоянию:

```csharp
public static List<float2[]> BuildBoundaryChunk(RegionMeshPlan plan, int rootIndex, CancellationToken ct)
{
	var triangles = new List<float2[]>();
	var root = plan.BoundaryRoots[rootIndex];
	Descend(plan, 0, root.Ix, root.Iz, plan.Merged, triangles, ct);
	return triangles;
}

private static void Descend(RegionMeshPlan plan, int depth, int ix, int iz, List<Polygon2D> piece, List<float2[]> triangles, CancellationToken ct)
{
	ct.ThrowIfCancellationRequested();

	float cs = plan.Tree.CellSize(depth);
	float2 min = plan.Tree.CellMin(depth, ix, iz);
	float2 max = min + cs;

	var cell = new Polygon2D();
	cell.Outer = new[]
	{
		new float2(min.x, min.y),
		new float2(max.x, min.y),
		new float2(max.x, max.y),
		new float2(min.x, max.y)
	};

	var clipped = PolygonClipper.Intersection(new List<Polygon2D> { cell }, piece);
	if (clipped.Count == 0)
		return;

	if (plan.Tree.Leaves.TryGetValue((depth, ix, iz), out var leaf) && leaf.Boundary)
	{
		triangles.AddRange(PolygonClipper.Triangulate(clipped));
		return;
	}

	int childDepth = depth + 1;
	int childX = ix * 2;
	int childZ = iz * 2;
	DescendIfBranch(plan, childDepth, childX, childZ, clipped, triangles, ct);
	DescendIfBranch(plan, childDepth, childX + 1, childZ, clipped, triangles, ct);
	DescendIfBranch(plan, childDepth, childX, childZ + 1, clipped, triangles, ct);
	DescendIfBranch(plan, childDepth, childX + 1, childZ + 1, clipped, triangles, ct);
}

private static void DescendIfBranch(RegionMeshPlan plan, int depth, int ix, int iz, List<Polygon2D> piece, List<float2[]> triangles, CancellationToken ct)
{
	if (!plan.BoundaryBranch.Contains((depth, ix, iz)))
		return;
	Descend(plan, depth, ix, iz, piece, triangles, ct);
}
```

- Порядок обхода потомков фиксирован: `(x, z)`, `(x+1, z)`, `(x, z+1)`, `(x+1, z+1)` — часть контракта детерминизма.
- Interior-листья и Outside-территория в `BoundaryBranch` не входят — спуск в них не происходит.
- `Finish`: собрать треугольники в порядке — сначала interior-листья (текущий `AppendInterior`, обход `Tree.Leaves.Values` в текущем порядке вставки, boundary-листья пропускаются), затем boundary-чанки строго по возрастанию `rootIndex`. Дальше — существующий вершинный проход (`EnsureCcw`, `Vertex`, weld 1 мм, отбраковка вырожденных) без изменений, с `ct.ThrowIfCancellationRequested()` каждые 1024 треугольника. Для `FlatPath` — треугольники из `FlatTriangles`.
- `BuildCore` удалить; `Build` и `BuildFromHeightSampler` реализовать так:

```csharp
var plan = Plan(region, heightSampler, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale);
var chunks = new List<List<float2[]>>();
if (!plan.FlatPath)
{
	for (int i = 0; i < plan.BoundaryRoots.Count; i++)
		chunks.Add(BuildBoundaryChunk(plan, i, CancellationToken.None));
}
return Finish(plan, chunks, CancellationToken.None);
```

- `AppendBoundary` удалить. `AppendInterior`, `ComputeBounds`, `EnsureCcw`, `Vertex`, `SampleHeight` остаются.

## Изменения: `Packages/PCG.Polygons/Editor/Scripts/Exec/RegionToMeshNodeExecutor.cs`

Блок от `var work = PcgWorkerScheduler.RunAsync(...)` до `var data = await work;` заменить на этапную оркестрацию:

```csharp
var plan = await PcgWorkerScheduler.RunWithProgressAsync(
	this,
	() => RegionMeshBuilder.Plan(region, heightSampler, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale),
	ct);

List<float2[]>[] chunks = null;
if (!plan.FlatPath && plan.BoundaryRoots.Count > 0)
{
	chunks = new List<float2[]>[plan.BoundaryRoots.Count];
	await PcgWorkerScheduler.RunIndexedWithProgressAsync(this, plan.BoundaryRoots.Count, index =>
	{
		chunks[index] = RegionMeshBuilder.BuildBoundaryChunk(plan, index, ct);
	}, ct);
}

var data = await PcgWorkerScheduler.RunWithProgressAsync(
	this,
	() => RegionMeshBuilder.Finish(plan, chunks ?? (IReadOnlyList<List<float2[]>>)Array.Empty<List<float2[]>>(), ct),
	ct);
```

- Ручной цикл `while (work.Status == UniTaskStatus.Pending) { ReportProgress; Delay(250); }` удаляется — прогресс пампят `RunWithProgressAsync` / `RunIndexedWithProgressAsync`.
- Слоты `chunks[index]` — индексированные, слияние в `Finish` по порядку индексов: детерминизм по принципам проекта.

## Тесты: `Packages/PCG.Polygons/Tests/Editor/RegionMeshBuilderTests.cs`

Сборка `PCG.Polygons.Tests`. Синтетический вход: квадрат 100×100 с дыркой 20×20 в центре (Outer CCW, Hole), высота — `h(p) = 3 * sin(p.x * 0.11) * cos(p.y * 0.07)`, `maxHeightError = 0.25`, `minCellSize = 1`, `maxCellSize = 16`, `maxDepth = 6`, `heightOffset = 0.1`, `uvScale = 0.1`.

- Паритет классификации: эталон в тесте — дословная копия старого построения дерева (старые `Subdivide` со стеком `(Depth, Ix, Iz)`, старый `Classify` с линейным сканом всех сегментов и `Polygon2D.Contains` центра, старый `Balance`), реализованная как локальный класс теста поверх публичных `CellSize`/`CellMin`-формул. Тест строит боевое `MeshQuadtree.Build` и сверяет каждый лист (`Depth`, `Ix`, `Iz`, `Boundary`) с эталоном: наборы ключей равны, флаги равны.
- Паритет boundary-клипа: для каждого boundary-листа тест выполняет прямой клип `PolygonClipper.Intersection(cellRect, merged)` + `Triangulate` и сравнивает суммарную площадь треугольников листа с площадью треугольников этого же листа из `BuildBoundaryChunk`-пути; допуск на лист — `max(1e-4, 4 * cs * 0.002)` кв.м (квантование 1 мм на периметре ячейки).
- Площадь меша: суммарная XZ-площадь треугольников `Finish` равна площади `Union` с относительным допуском 1e-3.
- Драпировка: у каждой вершины результата `y == h(xz) + heightOffset` с допуском 1e-4.
- Детерминизм: два независимых прогона `Plan` → чанки → `Finish` дают поэлементно равные `Vertices`, `Uvs`, `Triangles`.
- Эквивалентность обёрток: `BuildFromHeightSampler` на том же входе даёт бит-в-бит тот же результат, что ручной путь `Plan` → последовательные чанки → `Finish`.
- Плоская ветка: вход без семплера (`maxCellSize = 0`) — площадь и детерминизм; путь `FlatPath`.

Запуск: `agentbridge tests --mode EditMode --assembly PCG.Polygons.Tests`.

## Приёмка на CityForestV4

- До правок снять эталон: открыть `Assets/Examples/CityForestV4/CityForestV4.unity`, сгенерировать District City V4, снять `agentbridge sceneshot` с фиксированной позой камеры над районом (позу записать в json приёмки) и сохранить постадийные цифры из раздела «Замеры-основание».
- После правок: тот же прогон — постадийные цифры (`Union/Window/Tree/Interior/Boundary/Vertex`) через инструментированный прогон стадий, полное время генерации District, тот же sceneshot.
- Критерии: целевые показатели из раздела «Целевые показатели приёмки»; sceneshot совпадает с эталоном попиксельно с допуском (различия только на субпиксельных сдвигах вершин у границ полигона; при сравнении использовать метрику среднего абсолютного отклонения ≤ 1/255 по картинке).
- Прогнать также `SweepDemoScene` (Sweep-патчи используют `RegionMeshBuilder.Build` плоской веткой) и убедиться, что генерация проходит и меши на месте.

## Обновление документации

- `Docs/POLYGONS_MAP.md`: строки про `RegionMeshBuilder` и `MeshQuadtree` — описать этапный API (`Plan` / `BuildBoundaryChunk` / `Finish`), `SegmentGrid`, иерархический boundary-клип и параллельные чанки в `RegionToMeshNodeExecutor`; заметку про ручной памп прогресса заменить на `RunWithProgressAsync` / `RunIndexedWithProgressAsync`.

---

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта под эти изменения.
