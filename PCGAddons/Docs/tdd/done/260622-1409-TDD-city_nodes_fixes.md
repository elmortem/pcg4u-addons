# ТДД: фиксы городских нод (RegionToPoints, маркировка резов)

Status: Выполнено

## Контекст

Правки к реализации city-нод (ТДД `260621-1841-TDD-city_nodes`). Два независимых блока:

- `RegionToPoints` — режимы Random/Grid/Centroid работают неверно; добавляется параметр `Margin` (отступ от границы региона) для всех режимов.
- Маркировка рёбер-резов (`Subdivide` → `AssignRoadClassByDepth`) — внешняя граница ошибочно получает ширину дороги, из-за чего `BlocksToRoads` строит полосы вдоль исходной кривой («гребёнка»).

Корни проблем:

- Random: `RegionFill.FillRandom` крутит цикл по `results.Count`, а `results` — общий аккумулятор на все регионы; после первого региона условие сразу ложно.
- Grid: сетка идёт по bounding box без отступа от контура; `Spacing` — только шаг.
- Centroid: `AddCentroid` берёт среднее вершин `Outer`; из-за плотного ресемпла исходной кривой (`SplineRegionConvert`, шаг ~1 м) центр утягивается к кривой, на вогнутых кварталах вылетает наружу.
- Гребёнка: `cutDepth` не отличает «реза не было» от «рез глубины 0» (дефолт int = 0). `AssignRoadClassByDepth` пишет `width` во все рёбра, дефолтная кривая на глубине 0 даёт максимум.

## Блок 1: RegionToPoints

### Нода

Файл: `Packages/PCG.Polygons/Scripts/City/RegionToPointsNode.cs`

Добавить поле после `Spacing`:

```
		[Input]
		public float Margin = 0f;
```

### RegionFill

Файл: `Packages/PCG.Polygons/Scripts/Geometry/RegionFill.cs`

Обе заливки принимают набор полигонов (результат inset одного региона) и трактуют его как один регион. `FillRandom` считает добавленные точки локальным счётчиком, а не длиной общего списка.

```
		public static async UniTask FillRandom(OperationScope scope, List<PointData> results, IList<Polygon2D> polygons, float planeY, int count, int seed, CancellationToken ct = default)
		{
			if (count <= 0 || polygons.Count == 0)
				return;

			count = math.min(count, PCG.MaxListPoints);
			GetBounds(polygons, out var min, out var max);
			var random = PcgRandom.Create(seed);

			int added = 0;
			int tryCount = count * 8;
			while (added < count && tryCount-- > 0)
			{
				var sample = new float2(random.NextFloat(min.x, max.x), random.NextFloat(min.y, max.y));
				if (ContainsAny(polygons, sample))
				{
					results.Add(new PointData
					{
						Position = new float3(sample.x, planeY, sample.y),
						Normal = new float3(0f, 1f, 0f),
						Scale = 1f
					});
					added++;
				}

				await scope.Step(ct: ct);
			}
		}

		public static async UniTask FillGrid(OperationScope scope, List<PointData> results, IList<Polygon2D> polygons, float planeY, float spacing, CancellationToken ct = default)
		{
			if (spacing <= 0f || polygons.Count == 0)
				return;

			GetBounds(polygons, out var min, out var max);

			for (float x = min.x; x <= max.x; x += spacing)
			{
				for (float y = min.y; y <= max.y; y += spacing)
				{
					var sample = new float2(x, y);
					if (ContainsAny(polygons, sample))
					{
						results.Add(new PointData
						{
							Position = new float3(sample.x, planeY, sample.y),
							Normal = new float3(0f, 1f, 0f),
							Scale = 1f
						});
					}

					await scope.Step(ct: ct);
				}
			}
		}

		public static bool ContainsAny(IList<Polygon2D> polygons, float2 p)
		{
			for (int i = 0; i < polygons.Count; i++)
			{
				if (polygons[i].Contains(p))
					return true;
			}

			return false;
		}

		private static void GetBounds(IList<Polygon2D> polygons, out float2 min, out float2 max)
		{
			min = new float2(float.MaxValue, float.MaxValue);
			max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < polygons.Count; i++)
			{
				polygons[i].GetBounds(out var pmin, out var pmax);
				min = math.min(min, pmin);
				max = math.max(max, pmax);
			}
		}
```

### Executor

Файл: `Packages/PCG.Polygons/Editor/Scripts/Exec/RegionToPointsNodeExecutor.cs`

Прочитать `Margin`:

```
				var margin = GetInputValue(nameof(Data.Margin), Data.Margin);
```

Per-region цикл строит inset и диспетчеризует режим на полученных кусках:

```
					for (int i = 0; i < input.Regions.Count; i++)
					{
						var pieces = Inset(input.Regions[i], margin);
						if (pieces.Count == 0)
						{
							await scope.Step(ct: ct);
							continue;
						}

						switch (Data.Mode)
						{
							case RegionToPointsMode.Centroid:
								AddCentroid(results, pieces, input.PlaneY);
								break;
							case RegionToPointsMode.Random:
								await RegionFill.FillRandom(scope, results, pieces, input.PlaneY, count, seed + i, ct);
								break;
							case RegionToPointsMode.Grid:
								await RegionFill.FillGrid(scope, results, pieces, input.PlaneY, spacing, ct);
								break;
						}

						await scope.Step(ct: ct);
					}
```

Inset через `PolygonClipper.Inflate` (при `Margin <= 0` — исходный полигон без изменений):

```
		private static List<Polygon2D> Inset(Polygon2D polygon, float margin)
		{
			var single = new List<Polygon2D> { polygon };
			if (margin <= 0f)
				return single;

			return PolygonClipper.Inflate(single, -margin);
		}
```

`AddCentroid` переписывается на центроид площади (не зависит от плотности вершин), точка ставится только если центр попал внутрь кусков:

```
		private static void AddCentroid(List<PointData> results, IList<Polygon2D> pieces, float planeY)
		{
			if (!TryAreaCentroid(pieces, out var center))
				return;

			if (!RegionFill.ContainsAny(pieces, center))
				return;

			results.Add(new PointData
			{
				Position = new float3(center.x, planeY, center.y),
				Normal = new float3(0f, 1f, 0f),
				Scale = 1f
			});
		}

		private static bool TryAreaCentroid(IList<Polygon2D> pieces, out float2 centroid)
		{
			centroid = float2.zero;
			double area2 = 0.0;
			double cx = 0.0;
			double cy = 0.0;

			for (int i = 0; i < pieces.Count; i++)
			{
				AccumulateRing(pieces[i].Outer, ref area2, ref cx, ref cy);
				for (int h = 0; h < pieces[i].Holes.Count; h++)
					AccumulateRing(pieces[i].Holes[h], ref area2, ref cx, ref cy);
			}

			if (math.abs(area2) < 1e-9)
				return false;

			double inv = 1.0 / (3.0 * area2);
			centroid = new float2((float)(cx * inv), (float)(cy * inv));
			return true;
		}

		private static void AccumulateRing(float2[] ring, ref double area2, ref double cx, ref double cy)
		{
			if (ring == null || ring.Length < 3)
				return;

			int n = ring.Length;
			for (int i = 0; i < n; i++)
			{
				var p0 = ring[i];
				var p1 = ring[(i + 1) % n];
				double cross = (double)p0.x * p1.y - (double)p1.x * p0.y;
				area2 += cross;
				cx += (p0.x + p1.x) * cross;
				cy += (p0.y + p1.y) * cross;
			}
		}
```

Знак ориентации колец нормализован (`PolygonClipper.NormalizeWinding`: внешний контур CCW, дырки CW), поэтому дырки вычитаются автоматически; при инверсии всего полигона знак `area2` и моментов меняется согласованно — центр корректен.

## Блок 2: маркировка резов

### SubdivideRegionNodeExecutor

Файл: `Packages/PCG.Polygons/Editor/Scripts/Exec/SubdivideRegionNodeExecutor.cs`, строка 77.

```
					int cutDepth = depth + 1;
```

Теперь рёбра-резы хранят `cutDepth >= 1`, граничные рёбра остаются с дефолтом `0`.

### AssignRoadClassByDepthNodeExecutor

Файл: `Packages/PCG.Polygons/Editor/Scripts/Exec/AssignRoadClassByDepthNodeExecutor.cs`, цикл по рёбрам (строки 40-46).

```
						for (int e = 0; e < polygon.EdgeCount; e++)
						{
							int d = polygon.GetEdge<int>(CityAttributes.CutDepth, e);
							if (d <= 0)
								continue;

							float k = maxDepth > 0 ? (float)(d - 1) / maxDepth : 0f;
							float width = Data.WidthByDepth.Evaluate(k) * maxWidth;
							polygon.SetEdge(CityAttributes.Width, e, width);
						}
```

Граничные рёбра (`d == 0`) ширины не получают → `BlocksToRoads` их пропускает → полос вдоль исходной кривой нет. Маппинг глубины в ширину для настоящих резов совпадает с прежним (`(d-1)` равно старому `depth`).

## Шаги внедрения

- `RegionToPointsNode.cs`: добавить `[Input] public float Margin = 0f;`.
- `RegionFill.cs`: сменить сигнатуры `FillRandom`/`FillGrid` на `IList<Polygon2D>`; локальный счётчик `added` в `FillRandom`; `ContainsAny` (public) и `GetBounds`.
- `RegionToPointsNodeExecutor.cs`: читать `Margin`; per-region `Inset`; диспетчеризация на куски; `AddCentroid` через центроид площади со skip при центре вне кусков.
- `SubdivideRegionNodeExecutor.cs`: `cutDepth = depth + 1`.
- `AssignRoadClassByDepthNodeExecutor.cs`: пропуск рёбер с `d <= 0`, ширина по `(d - 1)`.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md` под изменения.
