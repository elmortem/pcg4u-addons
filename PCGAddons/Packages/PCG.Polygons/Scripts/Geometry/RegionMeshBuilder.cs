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
