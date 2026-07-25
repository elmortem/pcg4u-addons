using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Instances;
using PCG.Polygons.City;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	internal static class RegionExtrudeBuilder
	{
		public static List<MeshInstanceData> Build(RegionSet regions, TerrainData terrain, Vector3 terrainOffset, RegionExtrudeTerrainMode terrainMode, float baseOffset, float height, float uvScale, string name, Material topMaterial, Material sideMaterial, bool collider, CancellationToken ct)
		{
			Func<float2, float> heightSampler = terrain == null
				? null
				: point => SampleTerrain(point, terrain, terrainOffset);
			return BuildFromHeightSampler(regions, heightSampler, terrainMode, baseOffset, height, uvScale, name, topMaterial, sideMaterial, collider, ct);
		}

		public static List<MeshInstanceData> BuildFromHeightSampler(
			RegionSet regions,
			Func<float2, float> heightSampler,
			RegionExtrudeTerrainMode terrainMode,
			float baseOffset,
			float height,
			float uvScale,
			string name,
			Material topMaterial,
			Material sideMaterial,
			bool collider,
			CancellationToken ct)
		{
			var result = new List<MeshInstanceData>();
			float baseY = regions.PlaneY + baseOffset;
			float scale = math.max(0.0001f, math.abs(uvScale));
			float highestY = heightSampler != null && terrainMode == RegionExtrudeTerrainMode.HighestPoint
				? HighestTerrainPoint(regions, heightSampler) + baseOffset
				: baseY;

			var topRegions = regions.Clone();
			topRegions.PlaneY = highestY;
			var top = heightSampler != null && terrainMode == RegionExtrudeTerrainMode.FollowTerrain
				? RegionMeshBuilder.BuildFromHeightSampler(topRegions, heightSampler, 0f, 0f, 0f, 0, baseOffset + height, scale)
				: RegionMeshBuilder.Build(topRegions, null, default, 0f, 0f, 0f, 0, height, scale);
			if (top.Vertices != null && top.Vertices.Length > 0 && top.Triangles != null && top.Triangles.Length > 0)
			{
				result.Add(new MeshInstanceData
				{
					Name = name + " Top",
					Material = topMaterial,
					Collider = collider,
					Vertices = top.Vertices,
					Uvs = top.Uvs,
					Triangles = top.Triangles
				});
			}

			var vertices = new List<Vector3>();
			var uvs = new List<Vector2>();
			var triangles = new List<int>();
			AddBoundarySides(
				regions,
				heightSampler,
				terrainMode,
				baseOffset,
				height,
				highestY,
				scale,
				vertices,
				uvs,
				triangles,
				ct);

			if (vertices.Count > 0 && triangles.Count > 0)
			{
				result.Add(new MeshInstanceData
				{
					Name = name + " Sides",
					Material = sideMaterial,
					Collider = collider,
					Vertices = vertices.ToArray(),
					Uvs = uvs.ToArray(),
					Triangles = triangles.ToArray()
				});
			}

			return result;
		}

		private static void AddBoundarySides(
			RegionSet regions,
			Func<float2, float> heightSampler,
			RegionExtrudeTerrainMode terrainMode,
			float baseOffset,
			float height,
			float highestY,
			float uvScale,
			List<Vector3> vertices,
			List<Vector2> uvs,
			List<int> triangles,
			CancellationToken ct)
		{
			if (regions == null || regions.Regions == null || regions.Regions.Count == 0)
				return;

			for (int i = 0; i < regions.Regions.Count; i++)
			{
				Polygon2D polygon = regions.Regions[i];
				AddRing(polygon.Outer);
				for (int h = 0; h < polygon.Holes.Count; h++)
					AddRing(polygon.Holes[h]);
			}

			void AddRing(float2[] ring)
			{
				if (ring == null || ring.Length < 2)
					return;

				for (int i = 0; i < ring.Length; i++)
				{
					ct.ThrowIfCancellationRequested();
					float2 a = ring[i];
					float2 b = ring[(i + 1) % ring.Length];
					float segment = math.distance(a, b);
					if (segment < 0.0001f)
						continue;

					float baseA = BaseHeight(a, regions.PlaneY, heightSampler, terrainMode, baseOffset);
					float baseB = BaseHeight(b, regions.PlaneY, heightSampler, terrainMode, baseOffset);
					float topA = TopHeight(a);
					float topB = TopHeight(b);
					int start = vertices.Count;

					vertices.Add(new Vector3(a.x, baseA, a.y));
					vertices.Add(new Vector3(a.x, topA, a.y));
					vertices.Add(new Vector3(b.x, topB, b.y));
					vertices.Add(new Vector3(b.x, baseB, b.y));

					float u = segment * uvScale;
					uvs.Add(new Vector2(0f, 0f));
					uvs.Add(new Vector2(0f, math.abs(topA - baseA) * uvScale));
					uvs.Add(new Vector2(u, math.abs(topB - baseB) * uvScale));
					uvs.Add(new Vector2(u, 0f));

					// Region rings are normalized so the filled area is on the left.
					// This winding points the side faces toward the empty area for
					// both outer contours and holes.
					triangles.Add(start);
					triangles.Add(start + 1);
					triangles.Add(start + 2);
					triangles.Add(start);
					triangles.Add(start + 2);
					triangles.Add(start + 3);
				}
			}

			float TopHeight(float2 point)
			{
				if (heightSampler != null && terrainMode == RegionExtrudeTerrainMode.FollowTerrain)
					return heightSampler(point) + baseOffset + height;
				return highestY + height;
			}
		}

		private static float HighestTerrainPoint(RegionSet regions, Func<float2, float> heightSampler)
		{
			float highest = float.MinValue;
			for (int i = 0; i < regions.Regions.Count; i++)
			{
				var polygon = regions.Regions[i];
				highest = math.max(highest, HighestRing(polygon.Outer, heightSampler));
				for (int h = 0; h < polygon.Holes.Count; h++)
					highest = math.max(highest, HighestRing(polygon.Holes[h], heightSampler));
			}
			return highest == float.MinValue ? regions.PlaneY : highest;
		}

		private static float HighestRing(float2[] ring, Func<float2, float> heightSampler)
		{
			float highest = float.MinValue;
			if (ring == null)
				return highest;
			for (int i = 0; i < ring.Length; i++)
				highest = math.max(highest, heightSampler(ring[i]));
			return highest;
		}

		private static float BaseHeight(float2 point, float planeY, Func<float2, float> heightSampler, RegionExtrudeTerrainMode terrainMode, float baseOffset)
		{
			if (heightSampler == null || terrainMode == RegionExtrudeTerrainMode.Planar)
				return planeY + baseOffset;
			return heightSampler(point) + baseOffset;
		}

		private static float SampleTerrain(float2 point, TerrainData terrain, Vector3 terrainOffset)
		{
			Vector3 size = terrain.size;
			float u = math.clamp((point.x - terrainOffset.x) / size.x, 0f, 1f);
			float v = math.clamp((point.y - terrainOffset.z) / size.z, 0f, 1f);
			return terrainOffset.y + terrain.GetInterpolatedHeight(u, v);
		}
	}
}
