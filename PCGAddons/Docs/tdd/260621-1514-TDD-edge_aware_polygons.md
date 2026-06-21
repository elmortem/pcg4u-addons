# ТДД: edge-aware polygons (рёберные атрибуты + проброс через геометрию)

Status: Выполнено

## Контекст

Расширяет пакет `PCG.Polygons` (ТДД-2): `Polygon2D` получает per-edge именованные атрибуты (на базе `PcgAttributeSet`, ТДД-1), а булевы операции пробрасывают рёберные атрибуты через геометрию точно (Clipper Z-callback). Это backbone для городских нод (ТДД-3): рез помечает новое ребро глубиной (`cutDepth`), а дороги собираются как полосы вдоль рёбер с шириной, заданной на ребре — без приближений per-region.

Единственный новый механизм — проброс рёберных атрибутов. Переменный mitered-inset НЕ делаем: дорога — это полоса-прямоугольник вдоль ребра шириной ребра, объединённая (`Union`) по всем рёбрам; равномерный отступ (тротуар/внутренняя часть участка) делается уже существующим `PolygonClipper.Inflate`.

## Предусловие: USINGZ в Clipper2

Z-callback требует поля `Point64.Z`. Включить `USINGZ`: добавить символ `USINGZ` в Project Settings → Player → Scripting Define Symbols (Editor + Standalone). Вендоренный Clipper2 — единственный потребитель символа; существующий код `PolygonClipper` (ТДД-2) остаётся валидным (`new Point64(x, y)` оставляет `Z = 0`, `ToPath`/`FromPath` игнорируют `Z`).

> **Факт реализации.** `USINGZ` задан не глобально, а через `versionDefines` рантайм-asmdef `PCG.Polygons` (`name: com.elmortem.pcg.polygons`, `expression: ""` → всегда активен для сборки): пакет самодостаточен и не зависит от ручной правки Project Settings (безопаснее при запущенном редакторе, портативно). При `USINGZ` вендоренный Clipper2 меняет namespace `Clipper2Lib` → `Clipper2ZLib` (upstream), поэтому в `PolygonClipper`/`PolygonEdgeClip` подключён `Clipper2ZLib` — это единственная правка `using` в существующем коде; логика `PolygonClipper` не изменилась.

## Polygon2D: рёберные атрибуты

`Polygon2D` дополняется набором атрибутов на рёбра. Соглашение индексации рёбер (плоское): сначала рёбра внешнего контура `[0 .. Outer.Length-1]` (ребро `i` = `Outer[i] → Outer[(i+1) % Outer.Length]`), затем рёбра дырок по порядку. Длина `EdgeAttributes` — либо `0` (рёберных данных нет → чтение даёт `default`), либо ровно `EdgeCount`.

```
using System.Collections.Generic;
using PCG.Attributes;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed partial class Polygon2D
	{
		public PcgAttributeSet EdgeAttributes { get; } = new();

		public int EdgeCount
		{
			get
			{
				int count = Outer.Length;
				for (int i = 0; i < Holes.Count; i++)
				{
					count += Holes[i].Length;
				}

				return count;
			}
		}

		public int HoleEdgeOffset(int hole)
		{
			int offset = Outer.Length;
			for (int i = 0; i < hole; i++)
			{
				offset += Holes[i].Length;
			}

			return offset;
		}

		public bool HasEdgeData()
		{
			return EdgeAttributes.Count == EdgeCount && EdgeCount > 0;
		}

		public T GetEdge<T>(string name, int edgeIndex) where T : struct
		{
			if (!HasEdgeData())
				return default;

			return EdgeAttributes.Get<T>(name, edgeIndex);
		}

		public void SetEdge<T>(string name, int edgeIndex, T value) where T : struct
		{
			if (EdgeAttributes.Count < EdgeCount)
				EdgeAttributes.EnsureCount(EdgeCount);

			EdgeAttributes.Set(name, edgeIndex, value);
		}
	}
}
```

`Polygon2D.Clone` (из ТДД-2) дополняется копией рёберных атрибутов; `EdgeAttributes` — get-only, поэтому копируется через `Append`:

```
public Polygon2D Clone()
{
	var copy = new Polygon2D();
	copy.Outer = (float2[])Outer.Clone();
	for (int i = 0; i < Holes.Count; i++)
	{
		copy.Holes.Add((float2[])Holes[i].Clone());
	}

	copy.EdgeAttributes.Append(EdgeAttributes);
	return copy;
}
```

`Polygon2D.GetContentHash` (из ТДД-2) подмешивает `EdgeAttributes.GetContentHash()` в конце.

## Проброс рёберных атрибутов через Clipper

Механизм: каждому ребру субъекта присваивается глобальный id (≥1), записанный в `Z` его вершины (исходящее ребро). Вершины клипа получают `Z = 0`. На пересечениях Z-callback переносит id субъектного ребра на новую точку. После операции каждое выходное ребро классифицируется: если оно коллинеарно ребру-кандидату (id с его концов) — наследует атрибуты этого ребра; иначе считается новым (рёбра реза/клипа) и получает атрибуты от делегата операции.

Кандидат берётся по `Z` (точно, без перебора), геометрия лишь подтверждает коллинеарность — это не эвристика «ближайшего ребра».

```
using System;
using System.Collections.Generic;
using Clipper2Lib;
using PCG.Attributes;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public static class PolygonEdgeClip
	{
		private struct EdgeSource
		{
			public Polygon2D Polygon;
			public int LocalEdge;
			public float2 A;
			public float2 B;
		}

		public static List<Polygon2D> Difference(IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			return Execute(ClipType.Difference, subject, clip, newEdgeWriter);
		}

		public static List<Polygon2D> Intersection(IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			return Execute(ClipType.Intersection, subject, clip, newEdgeWriter);
		}

		public static List<Polygon2D> Union(IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			return Execute(ClipType.Union, subject, clip, newEdgeWriter);
		}

		private static List<Polygon2D> Execute(ClipType clipType, IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			var table = new List<EdgeSource>();
			var subjectPaths = BuildSubjectPaths(subject, table);
			var clipPaths = BuildClipPaths(clip);

			var clipper = new Clipper64();
			clipper.ZCallback = OnZ;
			clipper.AddSubject(subjectPaths);
			clipper.AddClip(clipPaths);

			var tree = new PolyTree64();
			var open = new Paths64();
			clipper.Execute(clipType, FillRule.NonZero, tree, open);

			var result = new List<Polygon2D>();
			BuildPolygons(tree, table, newEdgeWriter, result);
			return result;
		}

		private static void OnZ(Point64 e1bot, Point64 e1top, Point64 e2bot, Point64 e2top, ref Point64 ip)
		{
			long z = e1bot.Z;
			if (e1top.Z > z)
				z = e1top.Z;
			if (e2bot.Z > z)
				z = e2bot.Z;
			if (e2top.Z > z)
				z = e2top.Z;

			ip.Z = z;
		}

		private static Paths64 BuildSubjectPaths(IList<Polygon2D> subject, List<EdgeSource> table)
		{
			var paths = new Paths64();
			for (int p = 0; p < subject.Count; p++)
			{
				var polygon = subject[p];
				AppendRing(paths, table, polygon, polygon.Outer, 0);
				int offset = polygon.Outer.Length;
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					AppendRing(paths, table, polygon, polygon.Holes[h], offset);
					offset += polygon.Holes[h].Length;
				}
			}

			return paths;
		}

		private static void AppendRing(Paths64 paths, List<EdgeSource> table, Polygon2D polygon, float2[] ring, int localOffset)
		{
			var path = new Path64(ring.Length);
			for (int i = 0; i < ring.Length; i++)
			{
				int next = (i + 1) % ring.Length;
				int id = table.Count + 1;
				table.Add(new EdgeSource
				{
					Polygon = polygon,
					LocalEdge = localOffset + i,
					A = ring[i],
					B = ring[next]
				});

				var point = new Point64((long)(ring[i].x * PolygonClipper.Scale), (long)(ring[i].y * PolygonClipper.Scale));
				point.Z = id;
				path.Add(point);
			}

			paths.Add(path);
		}

		private static Paths64 BuildClipPaths(IList<Polygon2D> clip)
		{
			var paths = new Paths64();
			for (int p = 0; p < clip.Count; p++)
			{
				var polygon = clip[p];
				paths.Add(ClipRing(polygon.Outer));
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					paths.Add(ClipRing(polygon.Holes[h]));
				}
			}

			return paths;
		}

		private static Path64 ClipRing(float2[] ring)
		{
			var path = new Path64(ring.Length);
			for (int i = 0; i < ring.Length; i++)
			{
				var point = new Point64((long)(ring[i].x * PolygonClipper.Scale), (long)(ring[i].y * PolygonClipper.Scale));
				point.Z = 0;
				path.Add(point);
			}

			return path;
		}

		private static void BuildPolygons(PolyTree64 tree, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, List<Polygon2D> result)
		{
			for (int i = 0; i < tree.Count; i++)
			{
				var node = tree[i];
				var polygon = new Polygon2D();
				polygon.Outer = ResolveRing(node.Polygon, table, newEdgeWriter, polygon.EdgeAttributes);
				for (int h = 0; h < node.Count; h++)
				{
					polygon.Holes.Add(ResolveRing(node[h].Polygon, table, newEdgeWriter, polygon.EdgeAttributes));
				}

				PolygonClipper.NormalizeWinding(polygon);
				result.Add(polygon);
			}
		}

		private static float2[] ResolveRing(Path64 path, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, PcgAttributeSet edgeAttributes)
		{
			int n = path.Count;
			var ring = new float2[n];
			for (int i = 0; i < n; i++)
			{
				ring[i] = new float2((float)(path[i].X / PolygonClipper.Scale), (float)(path[i].Y / PolygonClipper.Scale));
			}

			for (int i = 0; i < n; i++)
			{
				int next = (i + 1) % n;
				int sourceId = ClassifyEdge(ring[i], ring[next], path[i].Z, path[next].Z, table);
				if (sourceId > 0)
				{
					var src = table[sourceId - 1];
					if (src.Polygon.HasEdgeData())
					{
						edgeAttributes.AppendRow(src.Polygon.EdgeAttributes, src.LocalEdge);
						continue;
					}
				}

				int row = edgeAttributes.AddRow();
				newEdgeWriter?.Invoke(edgeAttributes, row);
			}

			return ring;
		}

		private static int ClassifyEdge(float2 a, float2 b, long za, long zb, List<EdgeSource> table)
		{
			int candidate = TryCandidate(a, b, za, table);
			if (candidate > 0)
				return candidate;

			return TryCandidate(a, b, zb, table);
		}

		private static int TryCandidate(float2 a, float2 b, long id, List<EdgeSource> table)
		{
			if (id <= 0 || id > table.Count)
				return 0;

			var src = table[(int)id - 1];
			if (IsCollinearOverlap(a, b, src.A, src.B))
				return (int)id;

			return 0;
		}

		private static bool IsCollinearOverlap(float2 a, float2 b, float2 c, float2 d)
		{
			const float eps = 0.001f;
			var dir = d - c;
			float len = math.length(dir);
			if (len < eps)
				return false;

			dir /= len;
			float distA = math.abs(Cross(dir, a - c));
			float distB = math.abs(Cross(dir, b - c));
			return distA < eps && distB < eps;
		}

		private static float Cross(float2 u, float2 v)
		{
			return u.x * v.y - u.y * v.x;
		}
	}
}
```

`PolygonClipper.NormalizeWinding` и константа `Scale` из ТДД-2 становятся `internal`/`public` для переиспользования здесь.

## Операции реза и булевых для города

- `SplitByLine` (апгрейд ТДД-2): рез = `PolygonEdgeClip.Intersection(region, halfPlaneRect, newEdgeWriter)` для каждой стороны; `newEdgeWriter` пишет на новое ребро атрибут `cutDepth` (передаётся вызывающей нодой `SubdivideRegion`). Унаследованные рёбра сохраняют свои `cutDepth`/классы.
- Обтекание препятствия: `PolygonEdgeClip.Difference(blocks, obstacle, newEdgeWriter)`, где `newEdgeWriter` ставит флаг `boundary = true` (рёбра вдоль препятствия — не дороги).
- `Union`/`Intersection` — аналогично, с подходящим `newEdgeWriter` (для дорог-полос новые рёбра тегаются `boundary`).

`newEdgeWriter` — это `Action<PcgAttributeSet, int>`; нода-вызыватель кладёт в строку `row` нужные значения, например:

```
(attrs, row) => attrs.Set("cutDepth", row, depth);
```

## Хелпер дорожной полосы

Дорога вдоль ребра — прямоугольник шириной ребра; сеть дорог — `Union` всех полос.

```
public static Polygon2D BuildStrip(float2 a, float2 b, float width)
{
	var dir = b - a;
	float len = math.length(dir);
	if (len < 1e-4f)
		return null;

	dir /= len;
	var offset = new float2(-dir.y, dir.x) * (width * 0.5f);
	var polygon = new Polygon2D();
	polygon.Outer = new[] { a + offset, b + offset, b - offset, a - offset };
	return polygon;
}
```

## Сериализация и хеш

- `RegionSetSerializer` (ТДД-2) дополняется: после геометрии каждого `Polygon2D` пишется/читается его `EdgeAttributes` через `PcgAttributeSetCacheIO.Write/Read`. Порядок: внешнее кольцо + дырки (геометрия) → затем `EdgeAttributes`.
- `RegionSet.GetContentHash` уже сворачивает `Regions[i].GetContentHash()`, который теперь включает `EdgeAttributes`.

## Шаги внедрения

- Включить `USINGZ` в Scripting Define Symbols.
- Дополнить `Polygon2D` рёберными атрибутами и хелперами (partial-файл `Polygon/Polygon2DEdges.cs` либо правка `Polygon2D.cs`), обновить `Clone`/`GetContentHash`.
- Сделать `PolygonClipper.Scale`/`NormalizeWinding` доступными для пакета.
- Добавить `Geometry/PolygonEdgeClip.cs` (механизм проброса) и `BuildStrip` (в `PolygonEdgeClip` или `PolygonClipper`).
- Перевести `SplitByLine` на `PolygonEdgeClip.Intersection` с `newEdgeWriter`.
- Расширить `RegionSetSerializer` записью/чтением `EdgeAttributes`.
- Проверка в графе: сплайн → регион → один рез `SubdivideRegion` (заглушка с одним шагом) → у нового ребра `cutDepth` задан, у старых сохранён; `RegionToSpline` рисует контуры; повторная генерация читает кеш.

## Интеграция с ТДД-3

Город собирается поверх: `SubdivideRegion` использует `SplitByLine` с `cutDepth`; `AssignRoadClassByDepth` читает рёберный `cutDepth` → пишет рёберный `width`; дороги = `Union` всех `BuildStrip(edge, width)` по внутренним (не `boundary`) рёбрам; кварталы под застройку = `PolygonEdgeClip.Difference(blocks, roads, …)`; тротуар/внутренняя часть участка = `PolygonClipper.Inflate`.

---

После реализации:

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить проектную документацию (`Docs/PROJECT_MAP.md` в `pcg4u-addons`) под рёберные атрибуты и механизм проброса.
