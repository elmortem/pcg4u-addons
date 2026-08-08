using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.Tests
{
	public class RegionMeshBuilderTests
	{
		private const float MaxHeightError = 0.25f;
		private const float MinCellSize = 1f;
		private const float MaxCellSize = 16f;
		private const int MaxDepth = 6;
		private const float HeightOffset = 0.1f;
		private const float UvScale = 0.1f;

		private static float Height(float2 p)
		{
			return 3f * math.sin(p.x * 0.11f) * math.cos(p.y * 0.07f);
		}

		private static RegionSet MakeRegion()
		{
			var poly = new Polygon2D();
			poly.Outer = new[]
			{
				new float2(0f, 0f),
				new float2(100f, 0f),
				new float2(100f, 100f),
				new float2(0f, 100f)
			};
			poly.Holes.Add(new[]
			{
				new float2(40f, 40f),
				new float2(40f, 60f),
				new float2(60f, 60f),
				new float2(60f, 40f)
			});

			var region = new RegionSet();
			region.PlaneY = 0f;
			region.AddRegion(poly);
			return region;
		}

		private static RegionMeshPlan MakePlan()
		{
			return RegionMeshBuilder.Plan(MakeRegion(), Height, MaxHeightError, MinCellSize, MaxCellSize, MaxDepth, HeightOffset, UvScale);
		}

		private static List<List<float2[]>> BuildChunks(RegionMeshPlan plan)
		{
			var chunks = new List<List<float2[]>>();
			for (int i = 0; i < plan.BoundaryRoots.Count; i++)
				chunks.Add(RegionMeshBuilder.BuildBoundaryChunk(plan, i, CancellationToken.None));
			return chunks;
		}

		private static void Bounds(IList<Polygon2D> merged, out float2 min, out float2 max)
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

		private static double SignedArea(float2[] ring)
		{
			double area = 0.0;
			int j = ring.Length - 1;
			for (int i = 0; i < ring.Length; i++)
			{
				area += ((double)ring[j].x + ring[i].x) * ((double)ring[j].y - ring[i].y);
				j = i;
			}

			return area * 0.5;
		}

		private static double RegionArea(IList<Polygon2D> polygons)
		{
			double area = 0.0;
			for (int i = 0; i < polygons.Count; i++)
			{
				area += math.abs(SignedArea(polygons[i].Outer));
				for (int h = 0; h < polygons[i].Holes.Count; h++)
					area -= math.abs(SignedArea(polygons[i].Holes[h]));
			}

			return area;
		}

		private static double TriangleArea(float2 a, float2 b, float2 c)
		{
			double cross = ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)c.x - a.x) * ((double)b.y - a.y);
			return math.abs(cross) * 0.5;
		}

		private static double MeshArea(RegionMeshData data)
		{
			double area = 0.0;
			for (int i = 0; i < data.Triangles.Length; i += 3)
			{
				Vector3 a = data.Vertices[data.Triangles[i]];
				Vector3 b = data.Vertices[data.Triangles[i + 1]];
				Vector3 c = data.Vertices[data.Triangles[i + 2]];
				area += TriangleArea(new float2(a.x, a.z), new float2(b.x, b.z), new float2(c.x, c.z));
			}

			return area;
		}

		[Test]
		public void QuadtreeClassificationMatchesReference()
		{
			var region = MakeRegion();
			var merged = PolygonClipper.Union(region.Regions, Array.Empty<Polygon2D>());
			Bounds(merged, out var min, out var max);

			var tree = MeshQuadtree.Build(merged, min, max, MaxCellSize, MinCellSize, MaxDepth, Height, MaxHeightError);
			var reference = ReferenceQuadtree.Build(merged, min, max, MaxCellSize, MinCellSize, MaxDepth, Height, MaxHeightError);

			Assert.AreEqual(reference.Leaves.Count, tree.Leaves.Count, "Leaf count differs");
			foreach (var pair in reference.Leaves)
			{
				Assert.IsTrue(tree.Leaves.TryGetValue(pair.Key, out var leaf), $"Missing leaf {pair.Key}");
				Assert.AreEqual(pair.Value.Boundary, leaf.Boundary, $"Boundary flag differs at {pair.Key}");
			}
		}

		[Test]
		public void BoundaryChunksMatchDirectClipPerLeaf()
		{
			var plan = MakePlan();
			var chunks = BuildChunks(plan);

			var chunkArea = new Dictionary<(int, int, int), double>();
			for (int c = 0; c < chunks.Count; c++)
			{
				var triangles = chunks[c];
				for (int i = 0; i < triangles.Count; i++)
				{
					var t = triangles[i];
					float2 centroid = (t[0] + t[1] + t[2]) / 3f;
					Assert.IsTrue(plan.Tree.TryFindLeaf(centroid, out var leaf), "Chunk triangle outside the tree");
					var key = (leaf.Depth, leaf.Ix, leaf.Iz);
					chunkArea.TryGetValue(key, out double acc);
					chunkArea[key] = acc + TriangleArea(t[0], t[1], t[2]);
				}
			}

			foreach (var leaf in plan.Tree.Leaves.Values)
			{
				if (!leaf.Boundary)
					continue;

				float cs = plan.Tree.CellSize(leaf.Depth);
				float2 min = plan.Tree.CellMin(leaf.Depth, leaf.Ix, leaf.Iz);
				float2 max = min + cs;

				var cell = new Polygon2D();
				cell.Outer = new[]
				{
					new float2(min.x, min.y),
					new float2(max.x, min.y),
					new float2(max.x, max.y),
					new float2(min.x, max.y)
				};

				var clipped = PolygonClipper.Intersection(new List<Polygon2D> { cell }, plan.Merged);
				var direct = PolygonClipper.Triangulate(clipped);

				double expected = 0.0;
				for (int i = 0; i < direct.Count; i++)
					expected += TriangleArea(direct[i][0], direct[i][1], direct[i][2]);

				chunkArea.TryGetValue((leaf.Depth, leaf.Ix, leaf.Iz), out double actual);
				double tolerance = math.max(1e-4, 4.0 * cs * 0.002);
				Assert.AreEqual(expected, actual, tolerance, $"Boundary area differs at ({leaf.Depth}, {leaf.Ix}, {leaf.Iz})");
			}
		}

		[Test]
		public void MeshAreaMatchesUnionArea()
		{
			var plan = MakePlan();
			var data = RegionMeshBuilder.Finish(plan, BuildChunks(plan), CancellationToken.None);

			double expected = RegionArea(plan.Merged);
			double actual = MeshArea(data);
			Assert.AreEqual(expected, actual, expected * 1e-3, "Mesh area differs from union area");
		}

		[Test]
		public void VerticesFollowHeightSampler()
		{
			var plan = MakePlan();
			var data = RegionMeshBuilder.Finish(plan, BuildChunks(plan), CancellationToken.None);

			Assert.Greater(data.Vertices.Length, 0);
			for (int i = 0; i < data.Vertices.Length; i++)
			{
				var v = data.Vertices[i];
				float expected = Height(new float2(v.x, v.z)) + HeightOffset;
				Assert.AreEqual(expected, v.y, 1e-4f, $"Vertex {i} is not draped");
			}
		}

		[Test]
		public void StagedBuildIsDeterministic()
		{
			var first = MakePlan();
			var firstData = RegionMeshBuilder.Finish(first, BuildChunks(first), CancellationToken.None);

			var second = MakePlan();
			var secondData = RegionMeshBuilder.Finish(second, BuildChunks(second), CancellationToken.None);

			AssertSameMesh(firstData, secondData);
		}

		[Test]
		public void WrapperMatchesStagedPath()
		{
			var plan = MakePlan();
			var staged = RegionMeshBuilder.Finish(plan, BuildChunks(plan), CancellationToken.None);

			var wrapped = RegionMeshBuilder.BuildFromHeightSampler(MakeRegion(), Height, MaxHeightError, MinCellSize, MaxCellSize, MaxDepth, HeightOffset, UvScale);

			AssertSameMesh(staged, wrapped);
		}

		[Test]
		public void FlatPathKeepsAreaAndIsDeterministic()
		{
			var plan = RegionMeshBuilder.Plan(MakeRegion(), null, MaxHeightError, MinCellSize, 0f, MaxDepth, HeightOffset, UvScale);
			Assert.IsTrue(plan.FlatPath);
			Assert.IsNull(plan.Tree);

			var first = RegionMeshBuilder.Finish(plan, new List<List<float2[]>>(), CancellationToken.None);
			double expected = RegionArea(plan.Merged);
			Assert.AreEqual(expected, MeshArea(first), expected * 1e-3, "Flat mesh area differs from union area");

			var other = RegionMeshBuilder.Plan(MakeRegion(), null, MaxHeightError, MinCellSize, 0f, MaxDepth, HeightOffset, UvScale);
			var second = RegionMeshBuilder.Finish(other, new List<List<float2[]>>(), CancellationToken.None);
			AssertSameMesh(first, second);
		}

		private static void AssertSameMesh(RegionMeshData a, RegionMeshData b)
		{
			Assert.AreEqual(a.Vertices.Length, b.Vertices.Length, "Vertex count differs");
			Assert.AreEqual(a.Uvs.Length, b.Uvs.Length, "Uv count differs");
			Assert.AreEqual(a.Triangles.Length, b.Triangles.Length, "Index count differs");

			for (int i = 0; i < a.Vertices.Length; i++)
				Assert.AreEqual(a.Vertices[i], b.Vertices[i], $"Vertex {i} differs");
			for (int i = 0; i < a.Uvs.Length; i++)
				Assert.AreEqual(a.Uvs[i], b.Uvs[i], $"Uv {i} differs");
			for (int i = 0; i < a.Triangles.Length; i++)
				Assert.AreEqual(a.Triangles[i], b.Triangles[i], $"Index {i} differs");
		}
	}
}
