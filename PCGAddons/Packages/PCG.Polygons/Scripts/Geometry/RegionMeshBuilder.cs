using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public static class RegionMeshBuilder
	{
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
	}
}
