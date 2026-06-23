# ТДД: редизайн билдера дорог (оффсет ломаных) + граница как класс глубины

Status: Выполнено

## Контекст

`BlocksToRoads` строит дорогу из прямоугольной полосы на каждое ребро (`BuildStrip`) и объединяет (`Union`). На углах и стыках полосы не сходятся (щель на выпуклом угле, нахлёст на вогнутом), на ресемпленной границе это особенно заметно. Замена: оффсет связных ломаных целиком (открытые пути Clipper с настраиваемыми join/cap), ширина по классам глубины. Граница становится отдельным классом глубины `0` без отдельного флага.

Опирается на гео-классификацию рёбер (`260622-1547`, внедрена): рёбра границы читаются как `cutDepth = 0`, рёбра-резы — `1..N`.

## Модель классов

Любое ребро квартала — либо часть исходной границы (`cutDepth = 0`), либо рез (`cutDepth = d ≥ 1`, где `d = глубина_рекурсии + 1`). Граница — класс глубины `0` (самый внешний). Отдельные `boundary`-флаг и `BoundaryWidth` не нужны.

## AssignRoadClassByDepth: диапазон глубин

Заменяет гейт из `260622-1524-TDD-road_marking_fix` (Фикс 2).

Нода `Packages/PCG.Polygons/Scripts/City/AssignRoadClassByDepthNode.cs` — добавить поле рядом с `MaxDepth`:

```
		[Input]
		public int MinDepth = 1;
```

Executor `Packages/PCG.Polygons/Editor/Scripts/Exec/AssignRoadClassByDepthNodeExecutor.cs` — прочитать `minDepth` и сменить условие:

```
				var minDepth = GetInputValue(nameof(Data.MinDepth), Data.MinDepth);
```

```
						for (int e = 0; e < polygon.EdgeCount; e++)
						{
							int d = polygon.GetEdge<int>(CityAttributes.CutDepth, e);
							if (d < minDepth || d > maxDepth)
								continue;

							float k = maxDepth > 0 ? (float)d / maxDepth : 0f;
							float width = Data.WidthByDepth.Evaluate(k) * maxWidth;
							polygon.SetEdge(CityAttributes.Width, e, width);
						}
```

`MinDepth = 1` — без периметральной дороги (дефолт). `MinDepth = 0` — граница (`d = 0`, `k = 0`, максимум кривой) становится дорогой. Рёбра вне диапазона ширины не получают → в дорогу не попадают.

Замечание: рёбра-границы препятствия из `PolygonBoolean(Difference)` тоже читаются `cutDepth = 0`; при `MinDepth = 0` станут дорогами. Дефолт `MinDepth = 1` это исключает. Дороги вокруг препятствий — вне этого ТДД.

## Перечисления

`Packages/PCG.Polygons/Scripts/City/RoadJoinType.cs`:

```
namespace PCG.Polygons.City
{
	public enum RoadJoinType
	{
		Round,
		Miter,
		Square
	}
}
```

`Packages/PCG.Polygons/Scripts/City/RoadCapType.cs`:

```
namespace PCG.Polygons.City
{
	public enum RoadCapType
	{
		Butt,
		Square,
		Round
	}
}
```

## BlocksToRoadsNode: настройки

`Packages/PCG.Polygons/Scripts/City/BlocksToRoadsNode.cs` — добавить параметры:

```
		public RoadJoinType Join = RoadJoinType.Round;

		public RoadCapType Cap = RoadCapType.Butt;

		public float MiterLimit = 2f;
```

## Тип дорожного отрезка

`Packages/PCG.Polygons/Scripts/Geometry/RoadSegment.cs`:

```
using Unity.Mathematics;

namespace PCG.Polygons
{
	public struct RoadSegment
	{
		public float2 A;
		public float2 B;
		public int Depth;
		public float Width;
	}
}
```

## Чейнинг рёбер в ломаные

`Packages/PCG.Polygons/Scripts/Geometry/RoadPolylineBuilder.cs` — собирает рёбра-дороги, дедуплицирует, группирует по глубине, связывает в ломаные (открытые цепочки и замкнутые петли). Квантование координат — `1000` (как `PolygonClipper.Scale`).

```
using System.Collections.Generic;
using PCG.Polygons.City;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public static class RoadPolylineBuilder
	{
		public static Dictionary<int, List<RoadSegment>> CollectByDepth(RegionSet blocks)
		{
			var seen = new HashSet<long4>();
			var byDepth = new Dictionary<int, List<RoadSegment>>();

			foreach (var block in blocks.Regions)
			{
				if (!block.HasEdgeData() || !block.EdgeAttributes.HasColumn(CityAttributes.Width))
					continue;

				int n = block.Outer.Length;
				for (int e = 0; e < n; e++)
				{
					float w = block.GetEdge<float>(CityAttributes.Width, e);
					if (w <= 0f)
						continue;

					var a = block.Outer[e];
					var b = block.Outer[(e + 1) % n];
					if (!seen.Add(Key(a, b)))
						continue;

					int d = block.GetEdge<int>(CityAttributes.CutDepth, e);
					if (!byDepth.TryGetValue(d, out var list))
					{
						list = new List<RoadSegment>();
						byDepth[d] = list;
					}

					list.Add(new RoadSegment { A = a, B = b, Depth = d, Width = w });
				}
			}

			return byDepth;
		}

		public static void Chain(List<RoadSegment> segments, List<float2[]> openPaths, List<float2[]> closedPaths)
		{
			var verts = new List<float2>();
			var vid = new Dictionary<long2, int>();
			var ends = new List<int2>();
			var adj = new List<List<int>>();

			for (int i = 0; i < segments.Count; i++)
			{
				int v0 = Vertex(segments[i].A, verts, vid, adj);
				int v1 = Vertex(segments[i].B, verts, vid, adj);
				ends.Add(new int2(v0, v1));
				adj[v0].Add(i);
				adj[v1].Add(i);
			}

			var used = new bool[segments.Count];

			for (int u = 0; u < verts.Count; u++)
			{
				if (adj[u].Count == 2)
					continue;

				for (int j = 0; j < adj[u].Count; j++)
				{
					int e = adj[u][j];
					if (used[e])
						continue;

					openPaths.Add(Trace(u, e, ends, adj, used, verts, false));
				}
			}

			for (int i = 0; i < segments.Count; i++)
			{
				if (used[i])
					continue;

				int start = ends[i].x;
				closedPaths.Add(Trace(start, i, ends, adj, used, verts, true));
			}
		}

		private static float2[] Trace(int startVertex, int startEdge, List<int2> ends, List<List<int>> adj, bool[] used, List<float2> verts, bool closed)
		{
			var points = new List<float2>();
			points.Add(verts[startVertex]);

			int cur = startVertex;
			int e = startEdge;

			while (true)
			{
				used[e] = true;
				int other = ends[e].x == cur ? ends[e].y : ends[e].x;
				points.Add(verts[other]);

				if (adj[other].Count != 2)
					break;

				int next = -1;
				for (int k = 0; k < adj[other].Count; k++)
				{
					int cand = adj[other][k];
					if (!used[cand])
					{
						next = cand;
						break;
					}
				}

				if (next < 0)
					break;

				cur = other;
				e = next;
			}

			if (closed && points.Count > 1)
				points.RemoveAt(points.Count - 1);

			return points.ToArray();
		}

		private static int Vertex(float2 p, List<float2> verts, Dictionary<long2, int> vid, List<List<int>> adj)
		{
			var key = Quant(p);
			if (vid.TryGetValue(key, out int id))
				return id;

			id = verts.Count;
			verts.Add(p);
			vid[key] = id;
			adj.Add(new List<int>());
			return id;
		}

		private static long2 Quant(float2 p)
		{
			return new long2((long)math.round(p.x * 1000.0), (long)math.round(p.y * 1000.0));
		}

		private static long4 Key(float2 a, float2 b)
		{
			var qa = Quant(a);
			var qb = Quant(b);
			bool swap = qa.x > qb.x || (qa.x == qb.x && qa.y > qb.y);
			if (swap)
				return new long4(qb.x, qb.y, qa.x, qa.y);

			return new long4(qa.x, qa.y, qb.x, qb.y);
		}
	}
}
```

Замкнутая петля (граница) распознаётся тем, что все её вершины степени 2 и не стартуют из ветки выше; первый отрезок петли даёт стартовую вершину, дубль последней точки убирается.

## PolygonClipper: оффсет ломаных

`Packages/PCG.Polygons/Scripts/Geometry/PolygonClipper.cs` — добавить оффсет открытых и замкнутых путей одной ширины (delta — полуширина):

```
		public static List<Polygon2D> InflatePolylines(IList<float2[]> openPaths, IList<float2[]> closedPaths, float delta, JoinType joinType, EndType endType, float miterLimit)
		{
			var co = new ClipperOffset(miterLimit);

			if (openPaths != null)
			{
				for (int i = 0; i < openPaths.Count; i++)
					co.AddPath(ToPath(openPaths[i]), joinType, endType);
			}

			if (closedPaths != null)
			{
				for (int i = 0; i < closedPaths.Count; i++)
					co.AddPath(ToPath(closedPaths[i]), joinType, EndType.Joined);
			}

			var solution = new Paths64();
			co.Execute(delta * Scale, solution);
			return ToPolygons(solution);
		}
```

`ClipperOffset`, `JoinType`, `EndType` — из `Clipper2ZLib`. `ToPath`/`ToPolygons` уже есть; `ToPolygons` нормализует результат union'ом.

## BlocksToRoads executor: сборка через оффсет

`Packages/PCG.Polygons/Editor/Scripts/Exec/BlocksToRoadsNodeExecutor.cs` — заменить per-edge цикл на чейнинг + оффсет по классам, затем union. Подключить `using Clipper2ZLib;`.

```
				var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);
				if (input == null)
					return;

				var byDepth = RoadPolylineBuilder.CollectByDepth(input);
				var parts = new List<Polygon2D>();
				var joinType = ToJoinType(Data.Join);
				var endType = ToEndType(Data.Cap);

				foreach (var pair in byDepth)
				{
					var segments = pair.Value;
					float width = segments[0].Width;

					var openPaths = new List<float2[]>();
					var closedPaths = new List<float2[]>();
					RoadPolylineBuilder.Chain(segments, openPaths, closedPaths);

					var ribbons = PolygonClipper.InflatePolylines(openPaths, closedPaths, width * 0.5f, joinType, endType, Data.MiterLimit);
					parts.AddRange(ribbons);

					await scope.Step(ct: ct);
				}

				var roads = new RegionSet();
				roads.PlaneY = input.PlaneY;
				var merged = parts.Count > 0 ? PolygonClipper.Union(parts, new List<Polygon2D>()) : new List<Polygon2D>();
				for (int i = 0; i < merged.Count; i++)
					roads.AddRegion(merged[i]);

				Roads.Value = roads;
```

Маппинг enum'ов (приватные методы executor'а):

```
		private static JoinType ToJoinType(RoadJoinType join)
		{
			switch (join)
			{
				case RoadJoinType.Miter:
					return JoinType.Miter;
				case RoadJoinType.Square:
					return JoinType.Square;
				default:
					return JoinType.Round;
			}
		}

		private static EndType ToEndType(RoadCapType cap)
		{
			switch (cap)
			{
				case RoadCapType.Square:
					return EndType.Square;
				case RoadCapType.Round:
					return EndType.Round;
				default:
					return EndType.Butt;
			}
		}
```

`BuildStrip` в `PolygonEdgeClip` больше не используется дорогами — оставить как есть (может пригодиться) либо удалить отдельным решением; в рамках этого ТДД не трогаем.

## Эффект

- Дорога каждого класса строится как одна лента по связной ломаной с join'ами на углах — стыки и углы чистые, «криво» уходит.
- Граница управляется `MinDepth`: `1` — её нет (артефактов нет), `0` — чистая периметральная дорога.
- Перекрёстки по-прежнему сливаются финальным `Union`.

## Связь с другими ТДД

- Меняет `AssignRoadClassByDepth`: диапазон `MinDepth..MaxDepth` вместо гейта `d - 1 >= MaxDepth` из `260622-1524-TDD-road_marking_fix`. Кривая теперь нормируется как `d / MaxDepth` (граница `d = 0` — максимум).
- Опирается на `260622-1547` (гео-классификация): без неё `cutDepth` на рёбрах неверен и группировка по классам ломается.

## Шаги внедрения

- `AssignRoadClassByDepthNode.cs` + executor: поле `MinDepth`, условие `d < minDepth || d > maxDepth`, `k = d / maxDepth`.
- `City/RoadJoinType.cs`, `City/RoadCapType.cs` — енумы.
- `BlocksToRoadsNode.cs`: поля `Join`, `Cap`, `MiterLimit`.
- `Geometry/RoadSegment.cs` — тип отрезка.
- `Geometry/RoadPolylineBuilder.cs` — `CollectByDepth` + `Chain`.
- `PolygonClipper.cs` — `InflatePolylines`.
- `BlocksToRoadsNodeExecutor.cs` — сборка через чейнинг + оффсет + union, маппинг енумов.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md` (билдер дорог и параметры нод изменились).
