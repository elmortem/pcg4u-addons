using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public static class RegionMeshBuilder
	{
		public static RegionMeshData Build(RegionSet region, TerrainData terrain, Vector3 terrainPosition, float maxHeightError, float minCellSize, float maxCellSize, int maxDepth, float heightOffset, float uvScale)
		{
			Func<float2, float> heightSampler = terrain == null
				? null
				: p => SampleHeight(p, region.PlaneY, terrain, terrainPosition);
			return BuildSequential(region, heightSampler, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale);
		}

		public static RegionMeshData BuildFromHeightSampler(
			RegionSet region,
			Func<float2, float> heightSampler,
			float maxHeightError,
			float minCellSize,
			float maxCellSize,
			int maxDepth,
			float heightOffset,
			float uvScale)
		{
			return BuildSequential(region, heightSampler, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale);
		}

		public static RegionMeshPlan Plan(
			RegionSet region,
			Func<float2, float> heightSampler,
			float maxHeightError,
			float minCellSize,
			float maxCellSize,
			int maxDepth,
			float heightOffset,
			float uvScale)
		{
			var merged = PolygonClipper.Union(region.Regions, Array.Empty<Polygon2D>());

			var plan = new RegionMeshPlan
			{
				Merged = merged,
				PlaneY = region.PlaneY,
				HeightSampler = heightSampler,
				HeightOffset = heightOffset,
				UvScale = uvScale
			};

			if (merged.Count <= 0)
			{
				plan.FlatPath = true;
				plan.FlatTriangles = new List<float2[]>();
				return plan;
			}

			if (heightSampler == null || maxCellSize <= 0f)
			{
				plan.FlatPath = true;
				plan.FlatTriangles = PolygonClipper.Triangulate(merged);
				return plan;
			}

			ComputeBounds(merged, out var boundsMin, out var boundsMax);
			plan.Tree = MeshQuadtree.Build(merged, boundsMin, boundsMax, maxCellSize, minCellSize, maxDepth, heightSampler, maxHeightError);

			plan.BoundaryBranch = new HashSet<(int Depth, int Ix, int Iz)>();
			foreach (var leaf in plan.Tree.Leaves.Values)
			{
				if (!leaf.Boundary)
					continue;

				int depth = leaf.Depth;
				int ix = leaf.Ix;
				int iz = leaf.Iz;
				while (plan.BoundaryBranch.Add((depth, ix, iz)) && depth > 0)
				{
					depth--;
					ix >>= 1;
					iz >>= 1;
				}
			}

			var roots = new List<(int Ix, int Iz)>();
			foreach (var key in plan.BoundaryBranch)
			{
				if (key.Depth == 0)
					roots.Add((key.Ix, key.Iz));
			}

			roots.Sort(CompareRoots);
			plan.BoundaryRoots = roots;
			return plan;
		}

		public static List<float2[]> BuildBoundaryChunk(RegionMeshPlan plan, int rootIndex, CancellationToken ct)
		{
			var triangles = new List<float2[]>();
			var root = plan.BoundaryRoots[rootIndex];
			Descend(plan, 0, root.Ix, root.Iz, plan.Merged, triangles, ct);
			return triangles;
		}

		public static RegionMeshData Finish(RegionMeshPlan plan, IReadOnlyList<List<float2[]>> boundaryChunks, CancellationToken ct)
		{
			var triangles = new List<float2[]>();
			if (plan.FlatPath)
			{
				triangles.AddRange(plan.FlatTriangles);
			}
			else
			{
				foreach (var leaf in plan.Tree.Leaves.Values)
				{
					if (leaf.Boundary)
						continue;
					AppendInterior(leaf, plan.Tree, triangles);
				}

				for (int i = 0; i < boundaryChunks.Count; i++)
					triangles.AddRange(boundaryChunks[i]);
			}

			var vertices = new List<Vector3>();
			var uvs = new List<Vector2>();
			var indices = new List<int>();
			var map = new Dictionary<(long, long), int>();

			for (int i = 0; i < triangles.Count; i++)
			{
				if ((i & 1023) == 0)
					ct.ThrowIfCancellationRequested();

				var t = triangles[i];
				EnsureCcw(ref t);
				int i0 = Vertex(t[0], plan.PlaneY, plan.HeightSampler, plan.HeightOffset, plan.UvScale, vertices, uvs, map);
				int i1 = Vertex(t[1], plan.PlaneY, plan.HeightSampler, plan.HeightOffset, plan.UvScale, vertices, uvs, map);
				int i2 = Vertex(t[2], plan.PlaneY, plan.HeightSampler, plan.HeightOffset, plan.UvScale, vertices, uvs, map);
				if (i0 == i1 || i1 == i2 || i2 == i0)
					continue;
				if (Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]).sqrMagnitude < 0.00000001f)
					continue;
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

		private static RegionMeshData BuildSequential(
			RegionSet region,
			Func<float2, float> heightSampler,
			float maxHeightError,
			float minCellSize,
			float maxCellSize,
			int maxDepth,
			float heightOffset,
			float uvScale)
		{
			var plan = Plan(region, heightSampler, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale);
			var chunks = new List<List<float2[]>>();
			if (!plan.FlatPath)
			{
				for (int i = 0; i < plan.BoundaryRoots.Count; i++)
					chunks.Add(BuildBoundaryChunk(plan, i, CancellationToken.None));
			}

			return Finish(plan, chunks, CancellationToken.None);
		}

		private static int CompareRoots((int Ix, int Iz) a, (int Ix, int Iz) b)
		{
			if (a.Iz != b.Iz)
				return a.Iz.CompareTo(b.Iz);
			return a.Ix.CompareTo(b.Ix);
		}

		private static void Descend(RegionMeshPlan plan, int depth, int ix, int iz, List<Polygon2D> piece, List<float2[]> triangles, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();

			float cs = plan.Tree.CellSize(depth);
			float2 min = plan.Tree.CellMin(depth, ix, iz);
			float2 max = min + cs;

			var cell = new Polygon2D();
			cell.Outer = new[]
			{
				new float2(min.x, min.y),
				new float2(max.x, min.y),
				new float2(max.x, max.y),
				new float2(min.x, max.y)
			};

			var clipped = PolygonClipper.Intersection(new List<Polygon2D> { cell }, piece);
			if (clipped.Count == 0)
				return;

			if (plan.Tree.Leaves.TryGetValue((depth, ix, iz), out var leaf) && leaf.Boundary)
			{
				triangles.AddRange(PolygonClipper.Triangulate(clipped));
				return;
			}

			int childDepth = depth + 1;
			int childX = ix * 2;
			int childZ = iz * 2;
			DescendIfBranch(plan, childDepth, childX, childZ, clipped, triangles, ct);
			DescendIfBranch(plan, childDepth, childX + 1, childZ, clipped, triangles, ct);
			DescendIfBranch(plan, childDepth, childX, childZ + 1, clipped, triangles, ct);
			DescendIfBranch(plan, childDepth, childX + 1, childZ + 1, clipped, triangles, ct);
		}

		private static void DescendIfBranch(RegionMeshPlan plan, int depth, int ix, int iz, List<Polygon2D> piece, List<float2[]> triangles, CancellationToken ct)
		{
			if (!plan.BoundaryBranch.Contains((depth, ix, iz)))
				return;
			Descend(plan, depth, ix, iz, piece, triangles, ct);
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

		private static int Vertex(float2 p, float planeY, Func<float2, float> heightSampler, float heightOffset, float uvScale, List<Vector3> vertices, List<Vector2> uvs, Dictionary<(long, long), int> map)
		{
			var key = ((long)math.round(p.x * 1000.0), (long)math.round(p.y * 1000.0));
			if (map.TryGetValue(key, out int id))
				return id;

			float y = (heightSampler != null ? heightSampler(p) : planeY) + heightOffset;
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
