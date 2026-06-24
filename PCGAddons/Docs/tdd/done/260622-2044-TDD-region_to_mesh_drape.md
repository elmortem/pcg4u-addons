# ТДД: RegionToMesh — меш дороги с драпировкой на террейн

Status: Выполнено (заменён)

> Алгоритм равномерного midpoint-дробления из этого ТДД заменён адаптивной тесселяцией по рельефу — см. `Docs/tdd/done/260623-1655-TDD-region_terrain_mesh_tessellation.md`. Нода и исполнитель сохранили имена `RegionToMeshNode` / `RegionToMeshNodeExecutor`; `SubdivisionLevel`/`Subdivide` удалены, параметры `MaxEdgeLength`/`MaxSubdivisions` заменены на `MaxHeightError`/`MinCellSize`/`MaxCellSize`/`MaxDepth`.

## Контекст

Дорожная сеть существует как плоский `RegionSet` (выход `BlocksToRoads`). Нужна нода, которая превращает регионы в меш, лежащий по рельефу террейна и не протыкающий его. Сам террейн не модифицируется (высоты только читаются). Запись в террейн — отдельная доработка позже.

Подход: триангулировать полигоны региона, равномерно подразбить треугольники до целевой длины ребра, посадить каждую вершину на высоту террейна с небольшим подъёмом. Чем мельче подразбиение — тем меньше провисание между вершинами; `HeightOffset` поднимает полотно над рельефом. Материализация в сцену — по паттерну `SpriteShapeInstanceData` + `InstanceMakerBase`.

## Расположение

Пакет `PCG.Polygons`.

- `Scripts/Instances/MeshInstanceData.cs`, `Scripts/Instances/MeshInstanceMaker.cs`
- `Scripts/Geometry/RegionMeshData.cs`, `Scripts/Geometry/RegionMeshBuilder.cs`
- `Scripts/City/RegionToMeshNode.cs`
- `Editor/Scripts/Exec/RegionToMeshNodeExecutor.cs`
- `PolygonClipper.Triangulate` — в существующем `Scripts/Geometry/PolygonClipper.cs`

## PolygonClipper.Triangulate

В `PolygonClipper.cs` добавить обёртку над триангуляцией Clipper2 (`Delaunay.Execute` отдаёт треугольники как `Paths64`, по 3 точки). `ToPaths` уже есть в классе, `Delaunay`/`TriangulateResult` — из `Clipper2ZLib`.

```
		public static List<float2[]> Triangulate(IList<Polygon2D> polygons)
		{
			var paths = ToPaths(polygons);
			var delaunay = new Delaunay();
			var result = delaunay.Execute(paths, out var sol);
			var triangles = new List<float2[]>(sol.Count);
			if (result != TriangulateResult.success)
				return triangles;

			for (int i = 0; i < sol.Count; i++)
			{
				var p = sol[i];
				if (p.Count < 3)
					continue;

				triangles.Add(new[]
				{
					new float2((float)(p[0].X / Scale), (float)(p[0].Y / Scale)),
					new float2((float)(p[1].X / Scale), (float)(p[1].Y / Scale)),
					new float2((float)(p[2].X / Scale), (float)(p[2].Y / Scale))
				});
			}

			return triangles;
		}
```

## RegionMeshData

`Scripts/Geometry/RegionMeshData.cs` — результат построения геометрии (без Unity-объекта Mesh, чтобы сборку можно было держать вне главного потока, а создание `Mesh` отдать мейкеру).

```
using UnityEngine;

namespace PCG.Polygons
{
	public struct RegionMeshData
	{
		public Vector3[] Vertices;
		public Vector2[] Uvs;
		public int[] Triangles;
	}
}
```

## RegionMeshBuilder

`Scripts/Geometry/RegionMeshBuilder.cs` — триангуляция, равномерное подразбиение, драпировка. Вершины свариваются по квантованной XZ-позиции (общие точки соседних треугольников получают один индекс и одну высоту → нет щелей). Порядок индексов `0,2,1` — нормали вверх.

```
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public static class RegionMeshBuilder
	{
		public static RegionMeshData Build(RegionSet region, TerrainData terrain, Vector3 terrainPosition, float maxEdgeLength, int maxSubdivisions, float heightOffset, float uvScale)
		{
			var triangles = new List<float2[]>();
			var single = new List<Polygon2D>(1);
			for (int i = 0; i < region.Regions.Count; i++)
			{
				single.Clear();
				single.Add(region.Regions[i]);
				triangles.AddRange(PolygonClipper.Triangulate(single));
			}

			int level = SubdivisionLevel(triangles, maxEdgeLength, maxSubdivisions);
			var fine = Subdivide(triangles, level);

			var vertices = new List<Vector3>();
			var uvs = new List<Vector2>();
			var indices = new List<int>();
			var map = new Dictionary<(long, long), int>();

			for (int i = 0; i < fine.Count; i++)
			{
				var t = fine[i];
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

		private static int Vertex(float2 p, float planeY, TerrainData terrain, Vector3 terrainPosition, float heightOffset, float uvScale, List<Vector3> vertices, List<Vector2> uvs, Dictionary<(long, long), int> map)
		{
			var key = ((long)math.round(p.x * 1000.0), (long)math.round(p.y * 1000.0));
			if (map.TryGetValue(key, out int id))
				return id;

			float y = SampleHeight(p, planeY, terrain, terrainPosition) + heightOffset;
			id = vertices.Count;
			vertices.Add(new Vector3(p.x, y, p.y));
			uvs.Add(new Vector2(p.x, p.y) * uvScale);
			map[key] = id;
			return id;
		}

		private static float SampleHeight(float2 p, float planeY, TerrainData terrain, Vector3 terrainPosition)
		{
			if (terrain == null)
				return planeY;

			var size = terrain.size;
			float u = math.clamp((p.x - terrainPosition.x) / size.x, 0f, 1f);
			float v = math.clamp((p.y - terrainPosition.z) / size.z, 0f, 1f);
			return terrainPosition.y + terrain.GetInterpolatedHeight(u, v);
		}

		private static int SubdivisionLevel(List<float2[]> triangles, float maxEdgeLength, int maxSubdivisions)
		{
			if (maxEdgeLength <= 0f)
				return 0;

			float maxEdge = 0f;
			for (int i = 0; i < triangles.Count; i++)
			{
				var t = triangles[i];
				maxEdge = math.max(maxEdge, math.length(t[1] - t[0]));
				maxEdge = math.max(maxEdge, math.length(t[2] - t[1]));
				maxEdge = math.max(maxEdge, math.length(t[0] - t[2]));
			}

			int level = 0;
			float e = maxEdge;
			while (e > maxEdgeLength && level < maxSubdivisions)
			{
				e *= 0.5f;
				level++;
			}

			return level;
		}

		private static List<float2[]> Subdivide(List<float2[]> triangles, int level)
		{
			for (int l = 0; l < level; l++)
			{
				var next = new List<float2[]>(triangles.Count * 4);
				for (int i = 0; i < triangles.Count; i++)
				{
					var t = triangles[i];
					var m01 = (t[0] + t[1]) * 0.5f;
					var m12 = (t[1] + t[2]) * 0.5f;
					var m20 = (t[2] + t[0]) * 0.5f;
					next.Add(new[] { t[0], m01, m20 });
					next.Add(new[] { m01, t[1], m12 });
					next.Add(new[] { m20, m12, t[2] });
					next.Add(new[] { m01, m12, m20 });
				}

				triangles = next;
			}

			return triangles;
		}
	}
}
```

## MeshInstanceData

`Scripts/Instances/MeshInstanceData.cs`.

```
using System;
using PCG.Instances;
using UnityEngine;

namespace PCG.Polygons
{
	[Serializable]
	public class MeshInstanceData : InstanceData
	{
		public string Name = "Mesh";
		public Material Material;
		public Vector3[] Vertices;
		public Vector2[] Uvs;
		public int[] Triangles;
	}
}
```

## MeshInstanceMaker

`Scripts/Instances/MeshInstanceMaker.cs` — мейкер, по паттерну `SpriteShapeInstanceMaker`/`BrgInstanceMaker` (ядро находит наследников `InstanceMakerBase` само, отдельной регистрации не нужно). Создаёт `Mesh` из массивов и объект сцены с `MeshFilter`/`MeshRenderer`.

```
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Instances;
using PCG.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace PCG.Polygons
{
	public class MeshInstanceMaker : InstanceMakerBase
	{
		private GameObject AddMesh(MeshInstanceData data, Transform parent)
		{
			var go = new GameObject(string.IsNullOrEmpty(data.Name) ? "Mesh" : data.Name);
			go.transform.parent = parent;
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;

			var mesh = new Mesh();
			mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(data.Vertices);
			mesh.SetUVs(0, data.Uvs);
			mesh.SetTriangles(data.Triangles, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			var filter = go.AddComponent<MeshFilter>();
			filter.sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = data.Material;

			return go;
		}

		public override async UniTask<bool> TryAdd(string ownerKey, string groupName, IEnumerable<InstanceData> instances, CancellationToken ct = default)
		{
			if (instances == null)
				return true;

			var list = instances.ToList();
			if (!list.Any())
				return true;

			if (list.First() is not MeshInstanceData)
				return false;

			var meshes = list.Cast<MeshInstanceData>();

			using (var scope = OperationScope.Start(ownerKey))
			{
				foreach (var data in meshes)
				{
					var item = GetObjectsItem(ownerKey, groupName);
					var go = AddMesh(data, item.Parent);
					item.Objects.Add(go);

					await scope.Step(ct: ct);
				}
			}

			return true;
		}
	}
}
```

## RegionToMeshNode

`Scripts/City/RegionToMeshNode.cs`.

```
using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class RegionToMeshNode : PcgNode
	{
		public bool Enabled = true;

		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Region;

		[Input]
		public TerrainData Terrain;

		[Input]
		public Vector3 TerrainPosition;

		[Input]
		public float MaxEdgeLength = 2f;

		[Input]
		public int MaxSubdivisions = 4;

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

## RegionToMeshNodeExecutor

`Editor/Scripts/Exec/RegionToMeshNodeExecutor.cs` — по паттерну `SpriteShapeInstanceNodeExecutor` (`PcgAsyncNodeExecutor`, без превью).

```
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class RegionToMeshNodeExecutor : PcgAsyncNodeExecutor<RegionToMeshNode>
	{
		public PcgOutput<List<MeshInstanceData>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<MeshInstanceData>();

			if (!Data.Enabled)
				return;

			var region = GetInputValue(nameof(Data.Region), Data.Region);
			if (region == null || region.Count <= 0)
				return;

			var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
			var terrainPosition = GetInputValue(nameof(Data.TerrainPosition), Data.TerrainPosition);
			var maxEdgeLength = GetInputValue(nameof(Data.MaxEdgeLength), Data.MaxEdgeLength);
			var maxSubdivisions = GetInputValue(nameof(Data.MaxSubdivisions), Data.MaxSubdivisions);
			var heightOffset = GetInputValue(nameof(Data.HeightOffset), Data.HeightOffset);
			var uvScale = GetInputValue(nameof(Data.UvScale), Data.UvScale);
			var name = GetInputValue(nameof(Data.Name), Data.Name);

			using (var scope = OperationScope.Start(this))
			{
				var data = RegionMeshBuilder.Build(region, terrain, terrainPosition, maxEdgeLength, maxSubdivisions, heightOffset, uvScale);
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
		}
	}
}
```

## Подключение в графе

```
BlocksToRoads (Roads) → RegionToMesh (Terrain ← FindTerrain.Terrain, TerrainPosition ← FindTerrain.Position) → Results → инстансер
```

`FindTerrain` даёт `Terrain` (TerrainData) и `Position`. Без террейна меш строится плоским на `RegionSet.PlaneY`.

## Настройки качества

- `MaxEdgeLength` — мельче ⇒ плотнее полотно, меньше провисание между вершинами (меньше шанс протыкания на выпуклостях).
- `MaxSubdivisions` — потолок уровней подразбиения (защита от взрыва числа треугольников).
- `HeightOffset` — подъём полотна над рельефом; ставить чуть больше остаточного провисания.

## Замечания

- UV планарные от мировых XZ (`UvScale`) — тайлится дорожная текстура; «течения вдоль дороги» в UV нет (для этого нужен лофт, отдельная нода).
- Меш строится один на весь `RegionSet`. Если вершин станет очень много — резать на части (как BRG бьёт на батчи); пока не требуется, `IndexFormat.UInt32` снимает лимит 65k.
- Нормали через `RecalculateNormals` после драпировки — следуют уклону. Если фронт окажется снизу, поменять порядок индексов на `0,1,2`.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md` (новая нода, типы инстанса меша, `PolygonClipper.Triangulate`) и `Docs/notes/city_pipeline.md` (раздел TODO).
