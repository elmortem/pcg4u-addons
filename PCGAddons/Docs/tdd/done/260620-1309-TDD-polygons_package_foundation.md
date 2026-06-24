# ТДД: пакет PCG.Polygons — фундамент (Polygon2D / RegionSet)

Status: Выполнено (Clipper2 завендорен из upstream main 2026-04-20; сборка проверена netstandard2.0 + nullable off — 0 ошибок, 0 предупреждений)

## Контекст

Новый аддон-пакет `PCG.Polygons` вводит 2D-полигональный тип данных с именованными атрибутами, геометрический бэкенд (Clipper2 + half-plane split), заливку точками и конверсии со сплайнами. Это фундамент под городские ноды (subdivide/boolean/inset/lots) — они идут отдельным ТДД-3. Слой атрибутов (`PcgAttributeSet`, ТДД-1) уже в ядре `PCG`.

Пакет живёт в репозитории `pcg4u-addons` (`Packages/PCG.Polygons/`), как `PCG.Splines`/`PCG.Mazes`. В основную поставку PCG4U не входит.

В этом ТДД нет городских нод. Только: типы, геом-утилиты, конверсии, две ноды-конвертера, value/cache/gizmo/adapter.

## Расположение, сборки, зависимости

- `Packages/PCG.Polygons/package.json` — `com.elmortem.pcg.polygons` (по образцу `com.elmortem.pcg.splines`).
- Рантайм asmdef `Packages/PCG.Polygons/Scripts/PCG.Polygons.asmdef`: `references` = `PCG`, `Unity.Mathematics`, `Unity.Splines`, `UniTask`. `rootNamespace` = `PCG.Polygons`.
- Editor asmdef `Packages/PCG.Polygons/Editor/Scripts/PCG.Polygons.Editors.asmdef`: `references` = `PCG`, `PCG.Editors`, `PCG.Polygons`, `PCG.Splines`, `Unity.Splines`, `Unity.Mathematics`, `UniTask`.
- Clipper2 вендорится исходником в `Packages/PCG.Polygons/Scripts/Clipper2/` (upstream C#, namespace `Clipper2Lib`), входит в asmdef `PCG.Polygons`. В пакете кладётся файл лицензии Boost.

## Геометрические соглашения

- `float2 = (x, z)` — мировая плоскость XZ. Высота всего набора — `RegionSet.PlaneY` (мировой Y).
- Винтинг: внешний контур CCW, дырки CW. Нормализует обёртка `PolygonClipper` на входе и выходе.
- Масштаб для Clipper: метры × `PolygonClipper.Scale` (1000) → `Int64`/`Path64`, обратно делением.
- `Polygon2D`/`RegionSet`/`RegionSetValue` — рантайм-объекты вычисления; контент в Unity-сериализации сцен/ассетов не персистится (как `PcgAttributeSet`), только через value-cache.

## Состав (по файлу на тип)

Рантайм (`Scripts/`):

- `Polygon/Polygon2D.cs` — контур + дырки + геометрия.
- `Polygon/RegionSet.cs` — набор регионов + атрибуты (`IPcgAttributeData`).
- `Values/RegionSetValue.cs` — `PcgValue`-носитель.
- `Geometry/PolygonClipper.cs` — обёртка Clipper2 (boolean/inflate/split).
- `Geometry/RegionFill.cs` — заливка полигона точками.
- `Geometry/SplineRegionConvert.cs` — конверсии spline↔region.
- `Polygon/SplineToRegionNode.cs`, `Polygon/RegionToSplineNode.cs` — data-ноды.
- `Clipper2/…` — вендоренный Clipper2.

Editor (`Editor/Scripts/`):

- `Exec/SplineToRegionNodeExecutor.cs`, `Exec/RegionToSplineNodeExecutor.cs`.
- `Adapters/SplinesToRegionAdapter.cs` — порт-адаптер `List<Spline>` → `RegionSet`.
- `Cache/RegionSetSerializer.cs` — value-cache сериализатор.
- `PcgPolygonsBootstrap.cs` — `[InitializeOnLoadMethod]`, регистрирует сериализатор.
- `Utilities/RegionGizmoUtility.cs` — отрисовка регионов.

## Типы

### Polygon2D

```
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class Polygon2D
	{
		public float2[] Outer;
		public List<float2[]> Holes = new();

		public bool Contains(float2 point)
		{
			if (!ContainsRing(Outer, point))
				return false;

			for (int i = 0; i < Holes.Count; i++)
			{
				if (ContainsRing(Holes[i], point))
					return false;
			}

			return true;
		}

		public void GetBounds(out float2 min, out float2 max)
		{
			min = new float2(float.MaxValue, float.MaxValue);
			max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < Outer.Length; i++)
			{
				min = math.min(min, Outer[i]);
				max = math.max(max, Outer[i]);
			}
		}

		public Polygon2D Clone()
		{
			var copy = new Polygon2D();
			copy.Outer = (float2[])Outer.Clone();
			for (int i = 0; i < Holes.Count; i++)
			{
				copy.Holes.Add((float2[])Holes[i].Clone());
			}

			return copy;
		}

		public int GetContentHash()
		{
			unchecked
			{
				int hash = 17;
				hash = HashRing(hash, Outer);
				for (int i = 0; i < Holes.Count; i++)
				{
					hash = HashRing(hash, Holes[i]);
				}

				return hash;
			}
		}

		private static int HashRing(int hash, float2[] ring)
		{
			hash = (hash * 397) ^ ring.Length;
			for (int i = 0; i < ring.Length; i++)
			{
				hash = (hash * 397) ^ ring[i].GetHashCode();
			}

			return hash;
		}

		private static bool ContainsRing(float2[] ring, float2 point)
		{
			bool inside = false;
			int j = ring.Length - 1;
			for (int i = 0; i < ring.Length; i++)
			{
				var a = ring[i];
				var b = ring[j];
				if (a.y > point.y != b.y > point.y)
				{
					float t = (point.y - a.y) / (b.y - a.y);
					if (point.x < a.x + t * (b.x - a.x))
						inside = !inside;
				}

				j = i;
			}

			return inside;
		}
	}
}
```

### RegionSet

```
using System.Collections.Generic;
using PCG.Attributes;

namespace PCG.Polygons
{
	public sealed class RegionSet : IPcgAttributeData
	{
		public List<Polygon2D> Regions = new();
		public float PlaneY;

		public PcgAttributeSet Attributes { get; } = new();

		public int Count => Regions.Count;

		public int AddRegion(Polygon2D polygon)
		{
			Regions.Add(polygon);
			return Attributes.AddRow();
		}

		public RegionSet Clone()
		{
			var copy = new RegionSet();
			copy.PlaneY = PlaneY;
			for (int i = 0; i < Regions.Count; i++)
			{
				copy.Regions.Add(Regions[i].Clone());
			}

			copy.Attributes.Append(Attributes);
			return copy;
		}

		public int GetContentHash()
		{
			unchecked
			{
				int hash = 17;
				hash = (hash * 397) ^ PlaneY.GetHashCode();
				for (int i = 0; i < Regions.Count; i++)
				{
					hash = (hash * 397) ^ Regions[i].GetContentHash();
				}

				hash = (hash * 397) ^ Attributes.GetContentHash();
				return hash;
			}
		}
	}
}
```

`AddRegion` держит `Regions` и `Attributes` в одной длине (один регион = одна строка атрибутов). `Clone` глубоко копирует регионы и атрибуты (`Append` копирует все строки в пустой набор).

### RegionSetValue

```
using System;
using PCG.Values;
using UnityEngine;

namespace PCG.Polygons
{
	[Serializable]
	[PcgValueMenuPath("Polygons/Region Set")]
	public sealed class RegionSetValue : PcgValue
	{
		public override Type ValueType => typeof(RegionSet);

		public override object GetValue(Transform transform)
		{
			return new RegionSet();
		}

		public override int GetContentHash()
		{
			return 0;
		}
	}
}
```

Назначение `RegionSetValue` — регистрация типа порта/переменной `RegionSet` в пикере и блекборде; инлайн-значение пустое (регионы приходят с нод-конвертеров/городских нод). Поток `RegionSet` по рёбрам идёт через слот `PcgOutput<RegionSet>`.

## Геом-бэкенд: PolygonClipper

Обёртка над вендоренным Clipper2. Конвертация `float2` (метры) ↔ `Point64` через `Scale`; нормализация винтинга.

```
using System.Collections.Generic;
using Clipper2Lib;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public static class PolygonClipper
	{
		public const double Scale = 1000.0;

		public static List<Polygon2D> Union(IList<Polygon2D> a, IList<Polygon2D> b)
		{
			var subject = ToPaths(a);
			var clip = ToPaths(b);
			var solution = Clipper.Union(subject, clip, FillRule.NonZero);
			return ToPolygons(solution);
		}

		public static List<Polygon2D> Intersection(IList<Polygon2D> subject, IList<Polygon2D> clip)
		{
			var solution = Clipper.Intersect(ToPaths(subject), ToPaths(clip), FillRule.NonZero);
			return ToPolygons(solution);
		}

		public static List<Polygon2D> Difference(IList<Polygon2D> subject, IList<Polygon2D> clip)
		{
			var solution = Clipper.Difference(ToPaths(subject), ToPaths(clip), FillRule.NonZero);
			return ToPolygons(solution);
		}

		public static List<Polygon2D> Inflate(IList<Polygon2D> input, float delta)
		{
			var paths = ToPaths(input);
			var solution = Clipper.InflatePaths(paths, delta * Scale, JoinType.Miter, EndType.Polygon);
			return ToPolygons(solution);
		}

		public static void SplitByLine(Polygon2D region, float2 a, float2 b, List<Polygon2D> left, List<Polygon2D> right)
		{
			var dir = math.normalize(b - a);
			var normal = new float2(-dir.y, dir.x);

			var subject = new List<Polygon2D> { region };
			var leftRect = HalfPlaneRect(region, a, normal);
			var rightRect = HalfPlaneRect(region, a, -normal);

			left.AddRange(Intersection(subject, new List<Polygon2D> { leftRect }));
			right.AddRange(Intersection(subject, new List<Polygon2D> { rightRect }));
		}

		private static Polygon2D HalfPlaneRect(Polygon2D region, float2 origin, float2 normal)
		{
			region.GetBounds(out var min, out var max);
			float size = math.length(max - min) + 1f;
			var tangent = new float2(-normal.y, normal.x);
			var center = origin + normal * size;

			var poly = new Polygon2D();
			poly.Outer = new[]
			{
				origin - tangent * size,
				origin + tangent * size,
				center + tangent * size,
				center - tangent * size
			};

			return NormalizeWinding(poly);
		}

		private static Paths64 ToPaths(IList<Polygon2D> polygons)
		{
			var paths = new Paths64();
			for (int i = 0; i < polygons.Count; i++)
			{
				var polygon = polygons[i];
				paths.Add(ToPath(polygon.Outer));
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					paths.Add(ToPath(polygon.Holes[h]));
				}
			}

			return paths;
		}

		private static Path64 ToPath(float2[] ring)
		{
			var path = new Path64(ring.Length);
			for (int i = 0; i < ring.Length; i++)
			{
				path.Add(new Point64((long)(ring[i].x * Scale), (long)(ring[i].y * Scale)));
			}

			return path;
		}

		private static List<Polygon2D> ToPolygons(Paths64 paths)
		{
			var tree = new PolyTree64();
			var open = new Paths64();
			var clipper = new Clipper64();
			clipper.AddSubject(paths);
			clipper.Execute(ClipType.Union, FillRule.NonZero, tree, open);
			return FromPolyTree(tree);
		}

		private static List<Polygon2D> FromPolyTree(PolyTree64 tree)
		{
			var result = new List<Polygon2D>();
			for (int i = 0; i < tree.Count; i++)
			{
				var outerNode = tree[i];
				var polygon = new Polygon2D();
				polygon.Outer = FromPath(outerNode.Polygon);
				for (int h = 0; h < outerNode.Count; h++)
				{
					polygon.Holes.Add(FromPath(outerNode[h].Polygon));
				}

				result.Add(NormalizeWinding(polygon));
			}

			return result;
		}

		private static float2[] FromPath(Path64 path)
		{
			var ring = new float2[path.Count];
			for (int i = 0; i < path.Count; i++)
			{
				ring[i] = new float2((float)(path[i].X / Scale), (float)(path[i].Y / Scale));
			}

			return ring;
		}

		private static Polygon2D NormalizeWinding(Polygon2D polygon)
		{
			if (SignedArea(polygon.Outer) < 0f)
				System.Array.Reverse(polygon.Outer);

			for (int h = 0; h < polygon.Holes.Count; h++)
			{
				if (SignedArea(polygon.Holes[h]) > 0f)
					System.Array.Reverse(polygon.Holes[h]);
			}

			return polygon;
		}

		private static float SignedArea(float2[] ring)
		{
			float area = 0f;
			int j = ring.Length - 1;
			for (int i = 0; i < ring.Length; i++)
			{
				area += (ring[j].x + ring[i].x) * (ring[j].y - ring[i].y);
				j = i;
			}

			return area * 0.5f;
		}
	}
}
```

`SplitByLine` режет регион прямой `a→b` на две стороны через пересечение с двумя полуплоскостями-прямоугольниками (для вогнутых/с дырками каждая сторона может дать несколько полигонов — поэтому выход списками). Это основа `SubdivideRegion`/`CutByLine` в ТДД-3.

## Заливка: RegionFill

```
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public static class RegionFill
	{
		public static async UniTask FillRandom(OperationScope scope, List<PointData> results, Polygon2D polygon, float planeY, int count, int seed, CancellationToken ct = default)
		{
			if (count <= 0)
				return;

			count = math.min(count, PCG.MaxListPoints);
			polygon.GetBounds(out var min, out var max);
			var random = PcgRandom.Create(seed);

			int tryCount = count * 4;
			while (results.Count < count && tryCount-- > 0)
			{
				var sample = new float2(random.NextFloat(min.x, max.x), random.NextFloat(min.y, max.y));
				if (polygon.Contains(sample))
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

		public static async UniTask FillGrid(OperationScope scope, List<PointData> results, Polygon2D polygon, float planeY, float spacing, CancellationToken ct = default)
		{
			if (spacing <= 0f)
				return;

			polygon.GetBounds(out var min, out var max);

			for (float x = min.x; x <= max.x; x += spacing)
			{
				for (float y = min.y; y <= max.y; y += spacing)
				{
					var sample = new float2(x, y);
					if (polygon.Contains(sample))
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
	}
}
```

## Конверсии: SplineRegionConvert

```
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public static class SplineRegionConvert
	{
		public const float DefaultMaxSegmentLength = 1f;

		public static RegionSet SplinesToRegions(IList<Spline> splines, float maxSegmentLength)
		{
			var set = new RegionSet();
			float ySum = 0f;
			int yCount = 0;

			for (int i = 0; i < splines.Count; i++)
			{
				var spline = splines[i];
				if (!spline.Closed)
				{
					Debug.LogWarning("SplineToRegion: open spline skipped.");
					continue;
				}

				var ring = Resample(spline, maxSegmentLength, out float y);
				if (ring.Length < 3)
					continue;

				var polygon = new Polygon2D { Outer = ring };
				set.AddRegion(polygon);
				ySum += y;
				yCount++;
			}

			set.PlaneY = yCount > 0 ? ySum / yCount : 0f;
			return set;
		}

		public static List<Spline> RegionsToSplines(RegionSet set)
		{
			var result = new List<Spline>();
			for (int i = 0; i < set.Regions.Count; i++)
			{
				var polygon = set.Regions[i];
				result.Add(RingToSpline(polygon.Outer, set.PlaneY));
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					result.Add(RingToSpline(polygon.Holes[h], set.PlaneY));
				}
			}

			return result;
		}

		private static float2[] Resample(Spline spline, float maxSegmentLength, out float planeY)
		{
			float length = spline.GetLength();
			int count = math.max(3, Mathf.CeilToInt(length / math.max(0.001f, maxSegmentLength)));
			var ring = new float2[count];
			float ySum = 0f;

			for (int i = 0; i < count; i++)
			{
				float t = (float)i / count;
				spline.Evaluate(t, out var position, out _, out _);
				ring[i] = new float2(position.x, position.z);
				ySum += position.y;
			}

			planeY = ySum / count;
			return ring;
		}

		private static Spline RingToSpline(float2[] ring, float planeY)
		{
			var spline = new Spline();
			spline.Closed = true;
			for (int i = 0; i < ring.Length; i++)
			{
				spline.Add(new BezierKnot(new float3(ring[i].x, planeY, ring[i].y)), TangentMode.Linear);
			}

			return spline;
		}
	}
}
```

## Ноды и executor'ы

- `SplineToRegionNode` (data): `[Input]` `List<Spline> Splines` (`PcgConnectionType.Override`); `[Input] float MaxSegmentLength` (дефолт `1`); `[Output] RegionSet Result`. По образцу `SplineFromPointsNode`.
- `SplineToRegionNodeExecutor` (`PcgAsyncPreviewNodeExecutor<SplineToRegionNode>`): читает входные сплайны через `GetInputValue`, в `DoComputeAsync` зовёт `SplineRegionConvert.SplinesToRegions`, кладёт в слот `Result`. `DrawPreview` — через `RegionGizmoUtility`. Слот `public PcgOutput<RegionSet> Result;`.
- `RegionToSplineNode` (data): `[Input] RegionSet Region`; `[Output] List<Spline> Splines`. Executor зовёт `SplineRegionConvert.RegionsToSplines`; превью — существующий сплайновый гизмо (`SplinesGizmoUtility`).
- Слот `RegionSet` без пула (как сплайны): фабрика слота — дефолтная, `Result.Value = ...` присваивается напрямую.

## Порт-адаптер

`SplinesToRegionAdapter : PcgPortAdapter<List<Spline>, RegionSet>` (editor, по образцу `GameObjectsToSplinesAdapter`): `Convert(value, consumer)` зовёт `SplineRegionConvert.SplinesToRegions(value, SplineRegionConvert.DefaultMaxSegmentLength)`. Даёт автоконверсию сплайнового выхода в `RegionSet`-вход без нод-конвертера (с дефолтным разрешением; для контроля разрешения ставится `SplineToRegionNode`).

## Value-cache

`RegionSetSerializer : IPcgCacheSerializer`, `TypeId => 2`, `CanHandle(type) => type == typeof(RegionSet)`. `Snapshot` возвращает `((RegionSet)value).Clone()` (фоновая запись не видит мутирующийся объект). Регистрация — `[InitializeOnLoadMethod]` в `PcgPolygonsBootstrap`: `PcgCacheSerializerRegistry.Register(new RegionSetSerializer())`.

Формат `Write`/`Read`:

- `PlaneY` (float), число регионов (int).
- По региону: длина внешнего кольца + блоб `float2[]`, число дырок, по дырке длина + блоб `float2[]` (блоб — `ToArray()` + `MemoryMarshal.AsBytes`, чанками, как `PointListSerializer`).
- Затем атрибуты: `PcgAttributeSetCacheIO.Write(writer, set.Attributes)` / `Read`.

`Read` пересобирает регионы и зовёт `RegionSet.AddRegion` нельзя (он добавит строку атрибутов) — поэтому регионы кладутся прямо в `Regions`, а атрибуты восстанавливаются отдельно через `PcgAttributeSetCacheIO.Read`; длины совпадут по построению записи.

## Гизмо

`RegionGizmoUtility.Draw(RegionSet set, Color outerColor, Color holeColor)` — рисует внешние контуры и дырки замкнутыми полилиниями на высоте `PlaneY` (по образцу `SplinesGizmoUtility`). Используется в `DrawPreview` executor'а `SplineToRegionNode`.

## Шаги внедрения

- Завести пакет `Packages/PCG.Polygons/` с `package.json` и двумя asmdef по образцу `PCG.Splines`.
- Вендорить Clipper2 в `Scripts/Clipper2/` + файл лицензии.
- Реализовать рантайм-типы: `Polygon2D`, `RegionSet`, `RegionSetValue`.
- Реализовать утилиты: `PolygonClipper`, `RegionFill`, `SplineRegionConvert`.
- Реализовать ноды и executor'ы `SplineToRegion`/`RegionToSpline`.
- Реализовать адаптер `SplinesToRegionAdapter`, сериализатор `RegionSetSerializer`, бутстрап `PcgPolygonsBootstrap`, гизмо `RegionGizmoUtility`.
- Проверить в графе: нарисовать замкнутый сплайн → `SplineToRegion` → превью региона; `RegionToSpline` обратно; пройти граф через генерацию (value-cache регионов читается/пишется).

## Интеграция с ТДД-3

Городские ноды ТДД-3 (Subdivide/CutByLine, PolygonBoolean, Inset, RoadClassByDepth, Lots, RegionToPoints) оборачивают утилиты этого ТДД: `PolygonClipper.SplitByLine`/`Difference`/`Inflate`, `RegionFill`, и пишут служебные колонки (`depth`/`roadClass`/`lotId`) в `RegionSet.Attributes`.

---

После реализации:

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить проектную документацию (`Docs/PROJECT_MAP.md` в `pcg4u-addons`) под новый пакет.
