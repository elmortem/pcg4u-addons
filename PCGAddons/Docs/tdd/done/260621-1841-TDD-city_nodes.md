# ТДД: городские ноды (subdivide / roads / lots / points)

Status: Выполнено

## Контекст

Завершает цепочку: ядро атрибутов (ТДД-1), пакет `PCG.Polygons` (ТДД-2), edge-aware полигоны (ТДД-2.1). Здесь — собственно городские ноды поверх готовых утилит `PolygonClipper`/`PolygonEdgeClip`/`RegionFill`/`BuildStrip`. Кастомной геометрии нет, только композиция и две раскладки (рекурсия квартала, фронтажные лоты).

Пайплайн графа:

```
Splines → SplineToRegion → SubdivideRegion → AssignRoadClassByDepth ─┬→ BlocksToRoads ─→ (Roads RegionSet)
                                                                     └→ LotsFromBlock → RegionToPoints → (points → weighted instancer)
Obstacle (Object→Region) ─→ PolygonBoolean(Difference) встраивается перед/после Subdivide для обтекания.
```

Вариант финала точек — **A** (решено): `RegionToPoints` отдаёт обычные `List<PointData>` (позиция + ориентация к дороге через `Angle`); выбор конкретного дома — существующим weighted-инстансером. Перенос `lotId`/типа в точки — отдельный ТДД-4 (`PcgPointCloud`).

## Расположение

Пакет `PCG.Polygons` (тот же). Data-ноды — `Packages/PCG.Polygons/Scripts/City/`; executor'ы — `Packages/PCG.Polygons/Editor/Scripts/Exec/`; енумы — рядом с нодами, по файлу на тип.

Состав:

- `City/SubdivideRegionNode.cs`, `City/AssignRoadClassByDepthNode.cs`, `City/BlocksToRoadsNode.cs`, `City/PolygonBooleanNode.cs`, `City/PolygonBooleanMode.cs`, `City/InsetRegionNode.cs`, `City/LotsFromBlockNode.cs`, `City/RegionToPointsNode.cs`, `City/RegionToPointsMode.cs`.
- Editor: одноимённые `*Executor.cs`.

Атрибутные ключи (константы, завести `City/CityAttributes.cs`): `cutDepth` (edge, int), `width` (edge, float), `boundary` (edge, bool), `depth` (region, int), `lotId` (region, int).

## Предпосылки от ТДД-2.1

Используются как есть:

- `PolygonClipper.SplitByLine(Polygon2D region, float2 a, float2 b, Action<PcgAttributeSet,int> newEdgeWriter, List<Polygon2D> left, List<Polygon2D> right)` — рез с тегированием новых рёбер.
- `PolygonEdgeClip.Union/Intersection/Difference(subject, clip, newEdgeWriter)`.
- `PolygonClipper.Union/Inflate`, `PolygonClipper.BuildStrip(a, b, width)`, `RegionFill.FillRandom/FillGrid`.
- `Polygon2D.GetEdge/SetEdge`, `RegionSet.AddRegion`, `RegionSet.Attributes`.

## SubdivideRegionNode

Data:

```
using PCG.Graph;
using PCG.Polygons;

namespace PCG.Polygons.City
{
	public sealed class SubdivideRegionNode : PcgPreviewNode
	{
		[Input(PcgConnectionType.Override)]
		public RegionSet Region;

		[Input]
		public float MinSize = 20f;

		[Input]
		public int MaxDepth = 6;

		[Input]
		public float SplitJitter = 0.1f;

		[Input]
		public int Seed;

		[Output]
		public RegionSet Blocks => default;
	}
}
```

Executor (`PcgAsyncPreviewNodeExecutor<SubdivideRegionNode>`), ключевая логика:

```
protected override async UniTask DoComputeAsync(CancellationToken ct)
{
	var input = GetInputValue<RegionSet>(nameof(Data.Region));
	var result = new RegionSet();
	if (input != null)
		result.PlaneY = input.PlaneY;

	var random = PcgRandom.Create(Data.Seed);
	var queue = new Queue<(Polygon2D polygon, int depth)>();
	if (input != null)
	{
		for (int i = 0; i < input.Regions.Count; i++)
			queue.Enqueue((input.Regions[i], 0));
	}

	var left = new List<Polygon2D>();
	var right = new List<Polygon2D>();

	while (queue.Count > 0)
	{
		var (polygon, depth) = queue.Dequeue();
		polygon.GetBounds(out var min, out var max);
		var size = max - min;
		float maxDim = math.max(size.x, size.y);

		if (maxDim < Data.MinSize || depth >= Data.MaxDepth)
		{
			int row = result.AddRegion(polygon);
			result.Attributes.Set(CityAttributes.Depth, row, depth);
			await scope.Step(ct: ct);
			continue;
		}

		bool splitX = size.x >= size.y;
		float t = 0.5f + random.NextFloat(-Data.SplitJitter, Data.SplitJitter);
		float2 a;
		float2 b;
		if (splitX)
		{
			float x = math.lerp(min.x, max.x, t);
			a = new float2(x, min.y - 1f);
			b = new float2(x, max.y + 1f);
		}
		else
		{
			float y = math.lerp(min.y, max.y, t);
			a = new float2(min.x - 1f, y);
			b = new float2(max.x + 1f, y);
		}

		int cutDepth = depth;
		left.Clear();
		right.Clear();
		PolygonClipper.SplitByLine(polygon, a, b, (attrs, row) => attrs.Set(CityAttributes.CutDepth, row, cutDepth), left, right);

		for (int i = 0; i < left.Count; i++)
			queue.Enqueue((left[i], depth + 1));
		for (int i = 0; i < right.Count; i++)
			queue.Enqueue((right[i], depth + 1));

		await scope.Step(ct: ct);
	}

	Blocks.Value = result;
}
```

## AssignRoadClassByDepthNode

Data: `[Input(PcgConnectionType.Override)] RegionSet Blocks`; `public AnimationCurve WidthByDepth = AnimationCurve.Linear(0, 1, 1, 0.2f);`; `[Input] float MaxWidth = 8f`; `[Input] int MaxDepth = 6`; `[Output] RegionSet Result => default;`.

Executor: клонирует вход, для каждого региона по каждому ребру с данными `cutDepth` пишет `width`:

```
var input = GetInputValue<RegionSet>(nameof(Data.Blocks));
var result = input.Clone();
foreach (var polygon in result.Regions)
{
	if (!polygon.HasEdgeData())
		continue;

	for (int e = 0; e < polygon.EdgeCount; e++)
	{
		if (!polygon.EdgeAttributes.HasColumn(CityAttributes.CutDepth))
			break;

		int d = polygon.GetEdge<int>(CityAttributes.CutDepth, e);
		float k = Data.MaxDepth > 0 ? (float)d / Data.MaxDepth : 0f;
		float width = Data.WidthByDepth.Evaluate(k) * Data.MaxWidth;
		polygon.SetEdge(CityAttributes.Width, e, width);
	}
}

Result.Value = result;
```

Рёбра без `cutDepth` (внешняя граница) остаются без `width` → дорогой не становятся.

## BlocksToRoadsNode

Data: `[Input(PcgConnectionType.Override)] RegionSet Blocks`; `[Output] RegionSet Roads => default;`.

Executor: собирает полосы по рёбрам с `width > 0`, объединяет:

```
var input = GetInputValue<RegionSet>(nameof(Data.Blocks));
var strips = new List<Polygon2D>();
foreach (var polygon in input.Regions)
{
	if (!polygon.HasEdgeData() || !polygon.EdgeAttributes.HasColumn(CityAttributes.Width))
		continue;

	for (int e = 0; e < polygon.Outer.Length; e++)
	{
		float width = polygon.GetEdge<float>(CityAttributes.Width, e);
		if (width <= 0f)
			continue;

		var a = polygon.Outer[e];
		var b = polygon.Outer[(e + 1) % polygon.Outer.Length];
		var strip = PolygonClipper.BuildStrip(a, b, width);
		if (strip != null)
			strips.Add(strip);

		await scope.Step(ct: ct);
	}
}

var roads = new RegionSet();
roads.PlaneY = input.PlaneY;
var merged = strips.Count > 0 ? PolygonClipper.Union(strips, new List<Polygon2D>()) : new List<Polygon2D>();
for (int i = 0; i < merged.Count; i++)
	roads.AddRegion(merged[i]);

Roads.Value = roads;
```

Каждое внутреннее ребро встречается в двух кварталах → полоса строится дважды, `Union` дедуплицирует — корректно.

## PolygonBooleanNode

`City/PolygonBooleanMode.cs`: `enum PolygonBooleanMode { Union, Intersection, Difference }`.

Data: `[Input(PcgConnectionType.Override)] RegionSet A`; `[Input(PcgConnectionType.Override)] RegionSet B`; `public PolygonBooleanMode Mode = PolygonBooleanMode.Difference`; `[Output] RegionSet Result => default;`.

Executor: вызывает соответствующий `PolygonEdgeClip.*`, новые рёбра тегаются `boundary = true`:

```
var a = GetInputValue<RegionSet>(nameof(Data.A));
var b = GetInputValue<RegionSet>(nameof(Data.B));
Action<PcgAttributeSet, int> tag = (attrs, row) => attrs.Set(CityAttributes.Boundary, row, true);

List<Polygon2D> polygons;
switch (Data.Mode)
{
	case PolygonBooleanMode.Union:
		polygons = PolygonEdgeClip.Union(a.Regions, b.Regions, tag);
		break;
	case PolygonBooleanMode.Intersection:
		polygons = PolygonEdgeClip.Intersection(a.Regions, b.Regions, tag);
		break;
	default:
		polygons = PolygonEdgeClip.Difference(a.Regions, b.Regions, tag);
		break;
}

var result = new RegionSet();
result.PlaneY = a.PlaneY;
for (int i = 0; i < polygons.Count; i++)
	result.AddRegion(polygons[i]);

Result.Value = result;
```

Региональные атрибуты (`depth`/`lotId`) при булевых не сохраняются 1:1 (геометрия сливается/делится) — выходные регионы получают чистые строки. Рёберные атрибуты переносятся механизмом ТДД-2.1.

## InsetRegionNode

Data: `[Input(PcgConnectionType.Override)] RegionSet Region`; `[Input] float Delta = -1f`; `[Output] RegionSet Result => default;`.

Executor: пер-региональный `Inflate` с сохранением региональной строки атрибутов:

```
var input = GetInputValue<RegionSet>(nameof(Data.Region));
var result = new RegionSet();
result.PlaneY = input.PlaneY;
var single = new List<Polygon2D>(1);

for (int i = 0; i < input.Regions.Count; i++)
{
	single.Clear();
	single.Add(input.Regions[i]);
	var inflated = PolygonClipper.Inflate(single, Data.Delta);
	for (int j = 0; j < inflated.Count; j++)
	{
		result.Regions.Add(inflated[j]);
		result.Attributes.AppendRow(input.Attributes, i);
	}

	await scope.Step(ct: ct);
}

Result.Value = result;
```

Длины держатся согласованными без нового API: на каждый `Regions.Add` идёт один `Attributes.AppendRow(input.Attributes, i)` (ТДД-1) — он добавляет новую строку, копируя значения строки `i` источника по совпадающим колонкам. `AddRegion` здесь не используется, потому что атрибуты копируются из источника, а не создаются дефолтными.

## LotsFromBlockNode

Data: `[Input(PcgConnectionType.Override)] RegionSet Blocks`; `[Input] float LotWidth = 12f`; `[Output] RegionSet Lots => default;`.

Executor: для каждого квартала ось фронтажа = направление длинного ребра; квартал режется полосами-прямоугольниками поперёк оси с шагом `LotWidth`, каждая пересекается с кварталом:

```
var input = GetInputValue<RegionSet>(nameof(Data.Blocks));
var result = new RegionSet();
result.PlaneY = input.PlaneY;
int lotId = 0;

foreach (var block in input.Regions)
{
	float2 dir = LongestEdgeDir(block);
	float2 normal = new float2(-dir.y, dir.x);
	ProjectRange(block, dir, out float minT, out float maxT);
	ProjectRange(block, normal, out float minN, out float maxN);

	float span = maxT - minT;
	int count = math.max(1, (int)math.round(span / Data.LotWidth));
	float step = span / count;
	float2 origin = OriginOf(block, dir, normal, minT, minN);

	for (int i = 0; i < count; i++)
	{
		var stripRect = BuildAlignedRect(origin, dir, normal, minT + i * step, minT + (i + 1) * step, minN - 1f, maxN + 1f);
		var lots = PolygonEdgeClip.Intersection(new List<Polygon2D> { block }, new List<Polygon2D> { stripRect }, null);
		for (int j = 0; j < lots.Count; j++)
		{
			int row = result.AddRegion(lots[j]);
			result.Attributes.Set(CityAttributes.LotId, row, lotId);
			lotId++;
		}

		await scope.Step(ct: ct);
	}
}

Lots.Value = result;
```

Хелперы `LongestEdgeDir`/`ProjectRange`/`OriginOf`/`BuildAlignedRect` — приватные в executor'е: `LongestEdgeDir` берёт нормализованное направление самого длинного ребра `Outer`; `ProjectRange` — min/max скалярных проекций вершин на ось; `BuildAlignedRect` строит прямоугольник в осях `(dir, normal)` из диапазонов `[t0,t1] × [n0,n1]` (4 вершины `origin + dir*t + normal*n`). Полосы режут через edge-aware `Intersection` (рёберные данные квартала наследуются, ключи лотов — региональные).

## RegionToPointsNode

`City/RegionToPointsMode.cs`: `enum RegionToPointsMode { Centroid, Random, Grid }`.

Data: `[Input(PcgConnectionType.Override)] RegionSet Region`; `[Input(PcgConnectionType.Override)] RegionSet Roads`; `public RegionToPointsMode Mode = RegionToPointsMode.Centroid`; `[Input] int Count = 1`; `[Input] float Spacing = 5f`; `[Input] int Seed`; `[Output] List<PointData> Results => default;`.

Executor (`PcgAsyncPreviewNodeExecutor`, выход — пул точек):

```
var input = GetInputValue<RegionSet>(nameof(Data.Region));
var roads = GetInputValue<RegionSet>(nameof(Data.Roads));
var results = Results.Rent(input != null ? input.Count : 0);

for (int i = 0; i < input.Regions.Count; i++)
{
	var polygon = input.Regions[i];
	switch (Data.Mode)
	{
		case RegionToPointsMode.Centroid:
			AddCentroid(results, polygon, input.PlaneY);
			break;
		case RegionToPointsMode.Random:
			await RegionFill.FillRandom(scope, results, polygon, input.PlaneY, Data.Count, Data.Seed + i, ct);
			break;
		case RegionToPointsMode.Grid:
			await RegionFill.FillGrid(scope, results, polygon, input.PlaneY, Data.Spacing, ct);
			break;
	}

	await scope.Step(ct: ct);
}

OrientToNearestEdge(results, roads);
```

`AddCentroid` — добавляет точку в центроиде полигона (среднее вершин внешнего контура), `Normal = up`, `Scale = 1`. `OrientToNearestEdge` — для каждой точки находит ближайшее ребро дорог (если `Roads` подключён) либо ребро своего полигона, и ставит `PointData.Angle` лицом к нему (yaw из направления к ближайшей точке ребра). Ориентация через `Angle` доступна и без атрибутов точек.

## Превью

Ноды с выходом `RegionSet` рисуют превью через `RegionGizmoUtility` (ТДД-2). `RegionToPoints` рисует точки стандартным превью точечных нод.

## Шаги внедрения

- Завести `City/CityAttributes.cs` с константами ключей.
- Реализовать data-ноды и executor'ы по сигнатурам выше; рекурсия/раскладки — в executor'ах.
- Проверка в графе на замкнутом сплайне: `SplineToRegion → SubdivideRegion → AssignRoadClassByDepth → BlocksToRoads` даёт сеть дорог; ветка `→ LotsFromBlock → RegionToPoints` + weighted-инстансер ставит дома; вставка `PolygonBoolean(Difference)` с препятствием перестраивает город вокруг него. Повторная генерация читает value-cache.

## Интеграция и дальнейшее

3D-меш дороги (поднять/затекстурить полотно), а также перенос `lotId`/типа дома в точки (`PcgPointCloud`, ТДД-4) — вне этого ТДД. Текущий выход: дороги и кварталы как `RegionSet`, дома как точки с ориентацией.

---

После реализации:

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить проектную документацию (`Docs/PROJECT_MAP.md` в `pcg4u-addons`) под городские ноды.
