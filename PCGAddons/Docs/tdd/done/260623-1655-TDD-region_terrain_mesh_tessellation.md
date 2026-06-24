# ТДД: RegionToTerrain — адаптивная тесселяция меша по рельефу

Status: Выполнено

## Контекст

Нода `RegionToTerrainNode` строит меш региона, задрапированный на террейн (`RegionMeshBuilder.Build`). Текущий алгоритм даёт плохой меш:

- Глобальное равномерное midpoint-дробление: `SubdivisionLevel` берёт один уровень на весь меш по самой длинной грани, `Subdivide` дробит каждый треугольник 1→4 на этот уровень. При `MaxSubdivisions = 4` это до 256× треугольников, причём везде одинаково. Midpoint-дробление сохраняет форму, поэтому тонкий треугольник даёт 4 тонких — слайверы не убираются, а размножаются.
- Плотность не зависит от рельефа: на плоском участке столько же треугольников, сколько на склоне.
- Каждый регион триангулируется отдельным вызовом `PolygonClipper.Triangulate` — при наложении регионов треугольники накладываются (z-fighting).
- Базовый CDT строится только по вершинам контура (без внутренних точек) → длинные растянутые треугольники через площадь.

Цель рефактора: чистый, равномерный, crack-free меш, плотность которого следует кривизне рельефа; работает для региона любой формы.

Тип: рефакторинг `RegionMeshBuilder` + смена параметров `RegionToTerrainNode`. Поведение материализации в сцену, превью, `INodeInfo`, `RegionMeshData`, `MeshInstanceData`, `MeshInstanceMaker` не меняется.

## Идея

- Слить все регионы в один чистый набор `Polygon2D` (внешние контуры + дырки) через `PolygonClipper.Union` → наложения исчезают, швы между соседними регионами консистентны.
- Построить restricted (2:1) quadtree по AABB объединённого набора, выровненный по мировым осям. Квад дробится, пока отклонение высоты террейна от билинейной аппроксимации по углам больше `MaxHeightError`.
- Внутренние листы триангулировать transition-фанами (на ребре, где сосед мельче, добавляется середина) → без трещин.
- Граничные листы (форсированы до `MinCellSize`) обрезать по полигону (`PolygonClipper.Intersection`) и триангулировать существующим CDT (`PolygonClipper.Triangulate`).
- Поднять вершины на террейн, сварить по квантованной XZ-позиции, отдать `RegionMeshData` (без изменений формата).
- Без террейна (`Terrain == null`) quadtree не нужен: один CDT объединённого полигона на плоскости `PlaneY`.

## Расположение

Пакет `PCG.Polygons`.

Меняем:

- `Scripts/City/RegionToTerrainNode.cs` — параметры.
- `Editor/Scripts/Exec/RegionToTerrainNodeExecutor.cs` — чтение входов и вызов `Build`.
- `Scripts/Geometry/RegionMeshBuilder.cs` — оркестрация + триангуляция листов; убрать `SubdivisionLevel`/`Subdivide`; `Vertex`/`SampleHeight` оставить.

Новые файлы:

- `Scripts/Geometry/QuadLeaf.cs` — лист quadtree.
- `Scripts/Geometry/MeshQuadtree.cs` — построение, балансировка, запросы.

Используем существующее: `PolygonClipper.Union/Intersection/Triangulate`, `Polygon2D.Outer/Holes/Contains/GetBounds`.

## RegionToTerrainNode

Заменить `MaxEdgeLength`/`MaxSubdivisions` на `MaxHeightError`/`MinCellSize`/`MaxCellSize`/`MaxDepth`. Остальные поля без изменений.

```
using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class RegionToTerrainNode : PcgPreviewNode
	{
		public bool Enabled = true;

		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Region;

		[Input]
		public TerrainData Terrain;

		[Input]
		public Vector3 Offset;

		[Input]
		public float MaxHeightError = 0.25f;

		[Input]
		public float MinCellSize = 1f;

		[Input]
		public float MaxCellSize = 16f;

		[Input]
		public int MaxDepth = 6;

		[Input]
		public float HeightOffset = 0.1f;

		[Input]
		public float UvScale = 0.1f;

		[Input]
		public string Name = "Road";

		public Material Material;

		[Output]
		public List<MeshInstanceData> Results => default;
	}
}
```

## RegionToTerrainNodeExecutor

В `DoComputeAsync` поменять блок чтения входов и вызов `Build`. Всё остальное в исполнителе (превью, `INodeInfo`, `IInstancesNode`, материализация) без изменений.

```
			var region = GetInputValue(nameof(Data.Region), Data.Region);
			if (region == null || region.Count <= 0)
				return;

			var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
			var terrainPosition = GetInputValue(nameof(Data.Offset), Data.Offset);
			var maxHeightError = GetInputValue(nameof(Data.MaxHeightError), Data.MaxHeightError);
			var minCellSize = GetInputValue(nameof(Data.MinCellSize), Data.MinCellSize);
			var maxCellSize = GetInputValue(nameof(Data.MaxCellSize), Data.MaxCellSize);
			var maxDepth = GetInputValue(nameof(Data.MaxDepth), Data.MaxDepth);
			var heightOffset = GetInputValue(nameof(Data.HeightOffset), Data.HeightOffset);
			var uvScale = GetInputValue(nameof(Data.UvScale), Data.UvScale);
			var name = GetInputValue(nameof(Data.Name), Data.Name);

			using (var scope = OperationScope.Start(this))
			{
				var data = RegionMeshBuilder.Build(region, terrain, terrainPosition, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale);
				Results.Value.Add(new MeshInstanceData
				{
					Name = name,
					Material = Data.Material,
					Vertices = data.Vertices,
					Uvs = data.Uvs,
					Triangles = data.Triangles
				});

				await scope.Step(ct: ct);
			}
```

## QuadLeaf

`Scripts/Geometry/QuadLeaf.cs`. Лист идентифицируется целочисленными координатами в мировой сетке на своём уровне.

```
namespace PCG.Polygons
{
	public struct QuadLeaf
	{
		public int Depth;
		public int Ix;
		public int Iz;
		public bool Boundary;
	}
}
```

## MeshQuadtree

`Scripts/Geometry/MeshQuadtree.cs`. Сетка выровнена по мировым осям. Размер ячейки на глубине `d`: `MaxCellSize / 2^d` (глубина 0 = `MaxCellSize`). Начало координат сетки привязано к кратному `MaxCellSize`, чтобы сетка не «плавала» от формы региона.

Координаты листа `(Depth, Ix, Iz)` → мировой прямоугольник: `min = Origin + (Ix, Iz) * CellSize(Depth)`, `max = min + CellSize(Depth)`.

Поля и точка входа:

```
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class MeshQuadtree
	{
		public float2 Origin;
		public float MaxCellSize;
		public float MinCellSize;
		public int MaxDepth;
		public Dictionary<(int, int, int), QuadLeaf> Leaves = new();

		private IList<Polygon2D> _merged;
		private List<(float2 A, float2 B, float2 Min, float2 Max)> _segments;
		private Func<float2, float> _sampleHeight;
		private float _maxHeightError;

		public float CellSize(int depth)
		{
			return MaxCellSize / (1 << depth);
		}

		public float2 CellMin(int depth, int ix, int iz)
		{
			float cs = CellSize(depth);
			return Origin + new float2(ix, iz) * cs;
		}
	}
}
```

### Build

```
		public static MeshQuadtree Build(IList<Polygon2D> merged, float2 boundsMin, float2 boundsMax, float maxCellSize, float minCellSize, int maxDepth, Func<float2, float> sampleHeight, float maxHeightError)
```

- Создать дерево, заполнить поля. `MinCellSize = clamp(minCellSize, 1e-3f, maxCellSize)`.
- `Origin = floor(boundsMin / MaxCellSize) * MaxCellSize` покомпонентно.
- Собрать `_segments`: для каждого `merged[i]` рёбра кольца `Outer` и каждого кольца `Holes` (пары соседних точек, замкнуто), плюс AABB ребра.
- Перебрать корневые ячейки сетки `MaxCellSize`, покрывающие `[Origin .. boundsMax]`, и для каждой вызвать `Subdivide(0, ix, iz)`.
- Вызвать `Balance()`.
- Вернуть дерево.

Перебор корневых ячеек:

```
		int cols = (int)math.ceil((boundsMax.x - Origin.x) / MaxCellSize);
		int rows = (int)math.ceil((boundsMax.y - Origin.y) / MaxCellSize);
		for (int iz = 0; iz < rows; iz++)
			for (int ix = 0; ix < cols; ix++)
				Subdivide(0, ix, iz);
```

### Subdivide

Рекурсивное построение. `Outside` отбрасывается; `Boundary` форсируется до `MinCellSize`; `Inside` дробится по ошибке высоты.

```
		private void Subdivide(int depth, int ix, int iz)
		{
			float cs = CellSize(depth);
			float2 min = CellMin(depth, ix, iz);
			float2 max = min + cs;

			var cls = Classify(min, max);
			if (cls == CellClass.Outside)
				return;

			bool canSplit = cs > MinCellSize && depth < MaxDepth;
			bool split;
			if (cls == CellClass.Boundary)
				split = canSplit;
			else
				split = canSplit && _sampleHeight != null && HeightError(min, max) > _maxHeightError;

			if (split)
			{
				Subdivide(depth + 1, ix * 2, iz * 2);
				Subdivide(depth + 1, ix * 2 + 1, iz * 2);
				Subdivide(depth + 1, ix * 2, iz * 2 + 1);
				Subdivide(depth + 1, ix * 2 + 1, iz * 2 + 1);
				return;
			}

			Leaves[(depth, ix, iz)] = new QuadLeaf
			{
				Depth = depth,
				Ix = ix,
				Iz = iz,
				Boundary = cls == CellClass.Boundary
			};
		}
```

`CellClass` — внутренний enum `{ Inside, Outside, Boundary }` (отдельный файл `Scripts/Geometry/CellClass.cs`).

### Classify

Ребро региона пересекает ячейку → `Boundary`. Иначе ячейка целиком внутри или снаружи — решает тест центра.

```
		private CellClass Classify(float2 min, float2 max)
		{
			for (int i = 0; i < _segments.Count; i++)
			{
				var s = _segments[i];
				if (s.Max.x < min.x || s.Min.x > max.x || s.Max.y < min.y || s.Min.y > max.y)
					continue;
				if (SegmentIntersectsRect(s.A, s.B, min, max))
					return CellClass.Boundary;
			}

			float2 center = (min + max) * 0.5f;
			return RegionContains(center) ? CellClass.Inside : CellClass.Outside;
		}

		private bool RegionContains(float2 p)
		{
			for (int i = 0; i < _merged.Count; i++)
				if (_merged[i].Contains(p))
					return true;
			return false;
		}
```

`SegmentIntersectsRect(a, b, min, max)`: true, если `a` или `b` внутри прямоугольника, либо отрезок `a-b` пересекает любую из 4 сторон прямоугольника (через `SegmentsIntersect`). `SegmentsIntersect` — стандартная проверка пересечения двух отрезков по знакам векторных произведений. Обе вспомогательные — `private static` в `MeshQuadtree`.

### HeightError

Высоты в 4 углах задают билинейную плоскость; ошибка — максимум модуля отклонения сэмпла террейна от неё в центре и серединах рёбер.

```
		private float HeightError(float2 min, float2 max)
		{
			float h00 = _sampleHeight(new float2(min.x, min.y));
			float h10 = _sampleHeight(new float2(max.x, min.y));
			float h01 = _sampleHeight(new float2(min.x, max.y));
			float h11 = _sampleHeight(new float2(max.x, max.y));

			float err = 0f;
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0.5f, 0.5f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0.5f, 0f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0.5f, 1f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0f, 0.5f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 1f, 0.5f));
			return err;
		}

		private float TestError(float2 min, float2 max, float h00, float h10, float h01, float h11, float u, float v)
		{
			float2 p = new float2(math.lerp(min.x, max.x, u), math.lerp(min.y, max.y, v));
			float approx = math.lerp(math.lerp(h00, h10, u), math.lerp(h01, h11, u), v);
			return math.abs(_sampleHeight(p) - approx);
		}
```

### FindLeaf

Лист, содержащий точку. Перебор глубин: лист существует ровно на одной.

```
		public bool TryFindLeaf(float2 p, out QuadLeaf leaf)
		{
			for (int depth = 0; depth <= MaxDepth; depth++)
			{
				float cs = CellSize(depth);
				int ix = (int)math.floor((p.x - Origin.x) / cs);
				int iz = (int)math.floor((p.y - Origin.y) / cs);
				if (Leaves.TryGetValue((depth, ix, iz), out leaf))
					return true;
			}

			leaf = default;
			return false;
		}
```

### Balance

2:1: соседние по ребру листы отличаются не больше чем на 1 уровень. Если сосед грубее на ≥2 уровня — раздробить его.

```
		private void Balance()
		{
			var stack = new Stack<(int, int, int)>(Leaves.Keys);
			while (stack.Count > 0)
			{
				var key = stack.Pop();
				if (!Leaves.TryGetValue(key, out var leaf))
					continue;

				float cs = CellSize(leaf.Depth);
				float2 min = CellMin(leaf.Depth, leaf.Ix, leaf.Iz);
				float eps = MinCellSize * 0.25f;
				float2 c = min + cs * 0.5f;

				Span<float2> probes = stackalloc float2[4];
				probes[0] = new float2(c.x, min.y - eps);
				probes[1] = new float2(c.x, min.y + cs + eps);
				probes[2] = new float2(min.x - eps, c.y);
				probes[3] = new float2(min.x + cs + eps, c.y);

				for (int i = 0; i < 4; i++)
				{
					if (!TryFindLeaf(probes[i], out var n))
						continue;
					if (n.Depth >= leaf.Depth - 1)
						continue;

					Leaves.Remove((n.Depth, n.Ix, n.Iz));
					Subdivide(n.Depth + 1, n.Ix * 2, n.Iz * 2);
					Subdivide(n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2);
					Subdivide(n.Depth + 1, n.Ix * 2, n.Iz * 2 + 1);
					Subdivide(n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2 + 1);

					stack.Push((n.Depth + 1, n.Ix * 2, n.Iz * 2));
					stack.Push((n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2));
					stack.Push((n.Depth + 1, n.Ix * 2, n.Iz * 2 + 1));
					stack.Push((n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2 + 1));
					stack.Push(key);
				}
			}
		}
```

### HasFinerNeighbor

Для transition-триангуляции: на ребре нужна середина, если сосед за ребром мельче (глубже).

```
		public bool HasFinerNeighbor(QuadLeaf leaf, float2 probe)
		{
			if (!TryFindLeaf(probe, out var n))
				return false;
			return n.Depth > leaf.Depth;
		}
```

## RegionMeshBuilder

`Scripts/Geometry/RegionMeshBuilder.cs`. Новый `Build`: Union → ветка без террейна (CDT) либо quadtree → сборка вершин (как раньше). `Vertex` и `SampleHeight` без изменений. `SubdivisionLevel`/`Subdivide` удалить.

```
		public static RegionMeshData Build(RegionSet region, TerrainData terrain, Vector3 terrainPosition, float maxHeightError, float minCellSize, float maxCellSize, int maxDepth, float heightOffset, float uvScale)
		{
			var merged = PolygonClipper.Union(region.Regions, Array.Empty<Polygon2D>());

			var triangles = new List<float2[]>();
			if (merged.Count > 0)
			{
				if (terrain == null || maxCellSize <= 0f)
				{
					triangles.AddRange(PolygonClipper.Triangulate(merged));
				}
				else
				{
					ComputeBounds(merged, out var boundsMin, out var boundsMax);
					float planeY = region.PlaneY;
					var tree = MeshQuadtree.Build(merged, boundsMin, boundsMax, maxCellSize, minCellSize, maxDepth, p => SampleHeight(p, planeY, terrain, terrainPosition), maxHeightError);

					foreach (var leaf in tree.Leaves.Values)
					{
						if (leaf.Boundary)
							AppendBoundary(leaf, tree, merged, triangles);
						else
							AppendInterior(leaf, tree, triangles);
					}
				}
			}

			var vertices = new List<Vector3>();
			var uvs = new List<Vector2>();
			var indices = new List<int>();
			var map = new Dictionary<(long, long), int>();

			for (int i = 0; i < triangles.Count; i++)
			{
				var t = triangles[i];
				EnsureCcw(ref t);
				int i0 = Vertex(t[0], region.PlaneY, terrain, terrainPosition, heightOffset, uvScale, vertices, uvs, map);
				int i1 = Vertex(t[1], region.PlaneY, terrain, terrainPosition, heightOffset, uvScale, vertices, uvs, map);
				int i2 = Vertex(t[2], region.PlaneY, terrain, terrainPosition, heightOffset, uvScale, vertices, uvs, map);
				indices.Add(i0);
				indices.Add(i2);
				indices.Add(i1);
			}

			return new RegionMeshData
			{
				Vertices = vertices.ToArray(),
				Uvs = uvs.ToArray(),
				Triangles = indices.ToArray()
			};
		}
```

### ComputeBounds

AABB по `Outer` всех объединённых полигонов (через `Polygon2D.GetBounds`).

```
		private static void ComputeBounds(List<Polygon2D> merged, out float2 min, out float2 max)
		{
			min = new float2(float.MaxValue, float.MaxValue);
			max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < merged.Count; i++)
			{
				merged[i].GetBounds(out var lo, out var hi);
				min = math.min(min, lo);
				max = math.max(max, hi);
			}
		}
```

### AppendInterior

Внутренний лист: 4 угла CCW; на ребре с более мелким соседом добавляется середина. Без середин — 2 треугольника; с середадинами — фан из центра.

```
		private static void AppendInterior(QuadLeaf leaf, MeshQuadtree tree, List<float2[]> triangles)
		{
			float cs = tree.CellSize(leaf.Depth);
			float2 min = tree.CellMin(leaf.Depth, leaf.Ix, leaf.Iz);
			float2 max = min + cs;
			float eps = tree.MinCellSize * 0.25f;
			float2 c = min + cs * 0.5f;

			float2 c00 = new float2(min.x, min.y);
			float2 c10 = new float2(max.x, min.y);
			float2 c11 = new float2(max.x, max.y);
			float2 c01 = new float2(min.x, max.y);

			bool mS = tree.HasFinerNeighbor(leaf, new float2(c.x, min.y - eps));
			bool mE = tree.HasFinerNeighbor(leaf, new float2(max.x + eps, c.y));
			bool mN = tree.HasFinerNeighbor(leaf, new float2(c.x, max.y + eps));
			bool mW = tree.HasFinerNeighbor(leaf, new float2(min.x - eps, c.y));

			if (!mS && !mE && !mN && !mW)
			{
				triangles.Add(new[] { c00, c10, c11 });
				triangles.Add(new[] { c00, c11, c01 });
				return;
			}

			var ring = new List<float2>(8) { c00 };
			if (mS) ring.Add(new float2(c.x, min.y));
			ring.Add(c10);
			if (mE) ring.Add(new float2(max.x, c.y));
			ring.Add(c11);
			if (mN) ring.Add(new float2(c.x, max.y));
			ring.Add(c01);
			if (mW) ring.Add(new float2(min.x, c.y));

			for (int i = 0; i < ring.Count; i++)
			{
				float2 p = ring[i];
				float2 q = ring[(i + 1) % ring.Count];
				triangles.Add(new[] { c, p, q });
			}
		}
```

### AppendBoundary

Граничный лист (на уровне `MinCellSize`): прямоугольник ячейки пересекаем с объединённым полигоном и триангулируем существующим CDT. Грид-рёбра, лежащие внутри региона, сохраняют угловые вершины сетки → стыковка с внутренними листами без трещин (середины на стороне грубого соседа добавляет он сам в `AppendInterior`).

```
		private static void AppendBoundary(QuadLeaf leaf, MeshQuadtree tree, List<Polygon2D> merged, List<float2[]> triangles)
		{
			float cs = tree.CellSize(leaf.Depth);
			float2 min = tree.CellMin(leaf.Depth, leaf.Ix, leaf.Iz);
			float2 max = min + cs;

			var cell = new Polygon2D();
			cell.Outer = new[]
			{
				new float2(min.x, min.y),
				new float2(max.x, min.y),
				new float2(max.x, max.y),
				new float2(min.x, max.y)
			};

			var clipped = PolygonClipper.Intersection(new List<Polygon2D> { cell }, merged);
			triangles.AddRange(PolygonClipper.Triangulate(clipped));
		}
```

### EnsureCcw

Гарантирует положительную площадь в XZ (нормали вверх с порядком индексов `0,2,1`).

```
		private static void EnsureCcw(ref float2[] t)
		{
			float area = (t[1].x - t[0].x) * (t[2].y - t[0].y) - (t[2].x - t[0].x) * (t[1].y - t[0].y);
			if (area < 0f)
			{
				var tmp = t[1];
				t[1] = t[2];
				t[2] = tmp;
			}
		}
```

## Поток данных

```
RegionSet (region.Regions)
   → PolygonClipper.Union → merged: List<Polygon2D> (без наложений, с дырками)
   → terrain == null ? PolygonClipper.Triangulate(merged)
                      : MeshQuadtree.Build → листы
                           Inside  → AppendInterior (2 треуг. либо фан с середин рёбер)
                           Boundary→ AppendBoundary (Intersection + CDT)
   → сварка вершин + драпировка (Vertex/SampleHeight) → RegionMeshData
```

## Граничные случаи

- `merged.Count == 0` → пустой `RegionMeshData` (массивы нулевой длины).
- `Terrain == null` или `MaxCellSize <= 0` → одиночный CDT объединённого полигона, плоскость на `PlaneY` (минимум треугольников).
- `MinCellSize` клампится в `[1e-3, MaxCellSize]`; рекурсия ограничена `MaxDepth` и `MinCellSize` одновременно.
- Дырки: учитываются в `Polygon2D.Contains` и в `_segments` (кольца `Holes`) → внутри дырки ячейки классифицируются как `Outside`/`Boundary`.
- Швы граничных ячеек: точка пересечения контура с общим грид-ребром у соседних ячеек одна и та же → после сварки по квантованной позиции (1 мм) трещины нет.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md` (раздел PCG.Polygons: новые типы `MeshQuadtree`/`QuadLeaf`/`CellClass`, новый алгоритм меширования и параметры `RegionToTerrainNode`) и закрыть/пометить исходный `Docs/tdd/260622-2044-TDD-region_to_mesh_drape.md`.
