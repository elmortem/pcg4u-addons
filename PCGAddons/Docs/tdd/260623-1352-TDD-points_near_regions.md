# TDD: Нода PointsNearRegions

Status: Выполнено

## Контекст

Нужна нода `PointsNearRegions` по образцу `PointsNearSplines` и `PointsNearPointsOctreeNode`: делит входные точки на два выхода — `NearPoints` (точки рядом с регионами) и `Results` (остальные).

Модель близости (как у `PointsNearPoints`, где радиус — это «размер точки»):

- Точка трактуется как диск радиуса `Radius` в плоскости XZ.
- Регион — залитая площадь (`Polygon2D`: контур `Outer` + дырки `Holes`), плоскость XZ, высота набора — `RegionSet.PlaneY`.
- Точка попадает в `NearPoints`, если её диск касается площади региона хотя бы краем: центр внутри региона (`Polygon2D.Contains`) **или** расстояние от центра до контура `<= Radius`. Иначе — в `Results`.
- Только 2D: считаем по XZ. Высота точки и `RegionSet.PlaneY` не учитываются (режима 3D нет — регионы плоские).
- `UseScale`: при включении эффективный радиус для точки = `Radius * point.Scale`.
- Дырки: точка внутри дырки трактуется как вне региона (`Contains` уже так делает), но если она ближе `Radius` к ребру дырки — она рядом (ребро дырки — тоже граница, `DistanceToBoundarySq` сканирует и дырки).
- Несколько полигонов в наборе: точка рядом, если касается любого из них.
- Вход `Regions` — один `RegionSet` (connection `Override`, как у `RegionToPointsNode`). Несколько наборов сливаются выше по графу.

## Размещение

Категория меню нод определяется неймспейсом (`Documentation/Nodes.md`: «Select Points» = `PCG/SelectPoints/...`). Чтобы нода встала в ту же категорию «Select Points», что и `PointsNearSplines`, она лежит в namespace `PCG.SelectPoints`, но физически шипается из пакета `PCG.Polygons` (нужен тип `RegionSet`). Неймспейс не зависит от сборки — это допустимо (`PointsNearSplinesNode` тоже в `PCG.SelectPoints`, но в пакете `PCG.Splines`).

asmdef не трогаем: рантайм- и эдитор-сборки `PCG.Polygons` уже ссылаются на нужные типы — `RegionToPointsNode` / `RegionToPointsNodeExecutor` используют `PcgPreviewNode`, `PointData`, `PcgAsyncPreviewNodeExecutor`, `PcgOutput`, `OperationScope`, `GizmosUtility`, `IShowResults`, `IPointsCount`, `TotalCount()`.

## Затрагиваемые файлы

Новые:

- `Packages/PCG.Polygons/Scripts/SelectPoints/PointsNearRegionsNode.cs` — нода.
- `Packages/PCG.Polygons/Scripts/Polygon/Polygon2DDistance.cs` — partial-метод `Polygon2D.DistanceToBoundarySq`.
- `Packages/PCG.Polygons/Editor/Scripts/Exec/PointsNearRegionsNodeExecutor.cs` — исполнитель.

`RegionToPointsNodeExecutor` не трогаем — его приватные `ScanRing` / `ClosestOnSegment` остаются как есть, геометрию дистанции выносим отдельно на уровень `Polygon2D`.

## Нода

`Packages/PCG.Polygons/Scripts/SelectPoints/PointsNearRegionsNode.cs`:

```
using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;
using PCG.Polygons;

namespace PCG.SelectPoints
{
	public class PointsNearRegionsNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> NearPoints => default;

		[Input] public List<PointData> Points = new();

		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Regions;

		[Input] public float Radius = 1f;

		public bool UseScale;
	}
}
```

## Геометрия: расстояние до контура

`Packages/PCG.Polygons/Scripts/Polygon/Polygon2DDistance.cs` — partial-класс `Polygon2D`. Метод `DistanceToBoundarySq` возвращает квадрат расстояния от точки до ближайшего ребра среди `Outer` и всех `Holes`. Логика идентична приватным `ScanRing` / `ClosestOnSegment` в `RegionToPointsNodeExecutor`, но живёт на геометрическом типе.

```
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed partial class Polygon2D
	{
		public float DistanceToBoundarySq(float2 point)
		{
			float best = float.MaxValue;
			ScanBoundaryRing(Outer, point, ref best);
			for (int i = 0; i < Holes.Count; i++)
			{
				ScanBoundaryRing(Holes[i], point, ref best);
			}

			return best;
		}

		private static void ScanBoundaryRing(float2[] ring, float2 point, ref float best)
		{
			if (ring == null || ring.Length < 2)
				return;

			for (int i = 0; i < ring.Length; i++)
			{
				var a = ring[i];
				var b = ring[(i + 1) % ring.Length];
				var c = ClosestOnSegment(a, b, point);
				float d = math.distancesq(c, point);
				if (d < best)
					best = d;
			}
		}

		private static float2 ClosestOnSegment(float2 a, float2 b, float2 point)
		{
			var ab = b - a;
			float len = math.lengthsq(ab);
			if (len < 1e-8f)
				return a;

			float t = math.clamp(math.dot(point - a, ab) / len, 0f, 1f);
			return a + ab * t;
		}
	}
}
```

## Исполнитель

`Packages/PCG.Polygons/Editor/Scripts/Exec/PointsNearRegionsNodeExecutor.cs`.

Структура — копия `PointsNearSplinesNodeExecutor`: ранний выход по `Radius` / пустым входам, аренда выходных списков, проход по всем точкам через `OperationScope`, сплит в `NearPoints` / `Results`, превью с тоглом `ShowResults`.

`_boundsMin` / `_boundsMax` — ленивый кэш AABB регионов (по образцу `_pointsCache` в `PointsNearSplines`): чистится в начале `DoComputeAsync`, строится на первой точке. Используется как ранний отсев: если точка дальше `effectiveRadius` от AABB региона — полигон пропускается без сканирования рёбер.

```
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Polygons;
using PCG.Utilities;

namespace PCG.SelectPoints
{
	public class PointsNearRegionsNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsNearRegionsNode>, IPointsCount, IShowResults
	{
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> NearPoints;

		private readonly List<float2> _boundsMin = new();
		private readonly List<float2> _boundsMax = new();

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count ?? 0 : NearPoints.Value?.Count ?? 0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var radius = GetInputValue(nameof(Data.Radius), Data.Radius);
			if (radius < 0.0001f)
				return;

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var regions = GetInputValue(nameof(Data.Regions), Data.Regions);
			if (regions == null || regions.Count <= 0)
				return;

			_boundsMin.Clear();
			_boundsMax.Clear();

			int totalCount = pointsList.TotalCount();
			var results = Results.Rent(totalCount);
			var nearPoints = NearPoints.Rent(totalCount / 10 + 10);
			using (var scope = OperationScope.Start(this))
			{
				foreach (var points in pointsList)
				{
					if (points == null)
						continue;

					foreach (var point in points)
					{
						if (CheckNearRegion(point, regions, radius))
							nearPoints.Add(point);
						else
							results.Add(point);

						await scope.Step(ct: ct);
					}
				}
			}
		}

		private bool CheckNearRegion(PointData point, RegionSet regions, float radius)
		{
			if (_boundsMin.Count <= 0)
			{
				for (int i = 0; i < regions.Regions.Count; i++)
				{
					regions.Regions[i].GetBounds(out var min, out var max);
					_boundsMin.Add(min);
					_boundsMax.Add(max);
				}
			}

			var effectiveRadius = radius;
			if (Data.UseScale)
				effectiveRadius *= point.Scale;

			var sqrRadius = effectiveRadius * effectiveRadius;
			var p = new float2(point.Position.x, point.Position.z);

			for (int i = 0; i < regions.Regions.Count; i++)
			{
				var min = _boundsMin[i];
				var max = _boundsMax[i];
				if (p.x < min.x - effectiveRadius || p.x > max.x + effectiveRadius)
					continue;
				if (p.y < min.y - effectiveRadius || p.y > max.y + effectiveRadius)
					continue;

				var polygon = regions.Regions[i];
				if (polygon.Contains(p))
					return true;

				if (polygon.DistanceToBoundarySq(p) <= sqrRadius)
					return true;
			}

			return false;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			if (ShowResults)
				GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(NearPoints.Value, gizmosOptions, transform);
		}
	}
}
```

## Поведение

- `Radius < 0.0001` или пустой `Points` / `Regions` — нода ничего не считает, выходы остаются пустыми (как в `PointsNearSplines`).
- Точка внутри региона — всегда `NearPoints` (`Contains` истинна), независимо от удалённости от рёбер.
- Точка снаружи в пределах `Radius` от любого ребра (контура или дырки) — `NearPoints`.
- Точка внутри дырки и дальше `Radius` от её рёбер — `Results` (дырка = вне региона).
- `UseScale = false` — порог для всех точек равен `Radius`; `UseScale = true` — `Radius * point.Scale` для каждой точки.
- Превью переключается между `Results` и `NearPoints` через `ShowResults` (`IShowResults`), счётчик точек — `IPointsCount`.

---

## После выполнения

- Поменяй статус вверху документа на `Выполнено`.
- Проверь, что нода появилась в меню в категории «Select Points» (категория берётся из неймспейса `PCG.SelectPoints`).
- Уточни у заказчика, нужно ли обновлять документацию проекта (`Docs/PROJECT_MAP.md`, раздел `PCG.Polygons`; при необходимости — страница ноды в `Packages/PCG.Polygons/Documentation~/`).
