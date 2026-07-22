using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Polygons;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonNetworkTriangulator
	{
		private const int MaxVertices = 2_000_000;
		private const float MinimumArea = 1e-10f;

		internal static bool TryTriangulate(Polygon2D polygon, IReadOnlyList<SweepRibbonSourceTriangle> sources, CancellationToken ct, Action reportProgress, out float2[] vertices, out int[] triangles, out string failure)
		{
			vertices = null;
			triangles = null;
			failure = null;
			if (polygon == null || polygon.Outer == null || polygon.Outer.Length < 3)
			{
				failure = "GlobalBoundaryInvalid";
				return false;
			}

			var points = new List<float2>();
			var keys = new HashSet<(long, long)>();
			if (!AddBoundary(polygon.Outer, points, keys))
			{
				failure = "GlobalOuterInvalid";
				return false;
			}

			var holeOffsets = new int[polygon.Holes.Count];
			for (int hole = 0; hole < polygon.Holes.Count; hole++)
			{
				holeOffsets[hole] = points.Count;
				if (!AddBoundary(polygon.Holes[hole], points, keys))
				{
					failure = "GlobalHoleInvalid-" + hole;
					return false;
				}
			}

			float boundaryTolerance = (float)(0.75 / SweepRibbonPolygonUnion.Scale);
			float boundaryToleranceSq = boundaryTolerance * boundaryTolerance;
			for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
			{
				SweepRibbonSourceTriangle source = sources[sourceIndex];
				AddSteiner(source.A, polygon, boundaryToleranceSq, points, keys);
				AddSteiner(source.B, polygon, boundaryToleranceSq, points, keys);
				AddSteiner(source.C, polygon, boundaryToleranceSq, points, keys);
				AddSteiner((source.A + source.B + source.C) / 3f, polygon, boundaryToleranceSq, points, keys);
				if (points.Count > MaxVertices)
				{
					failure = "GlobalVertexBudgetExceeded";
					return false;
				}
				if ((sourceIndex & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}
			}

			var input = new Vector2[points.Count];
			for (int point = 0; point < points.Count; point++)
				input[point] = new Vector2(points[point].x, points[point].y);

			var triangulation = new detria.Triangulation();
			triangulation.SetPoints(input);
			triangulation.AddOutline(CreatePolyline(polygon.Outer, 0, true));
			for (int hole = 0; hole < polygon.Holes.Count; hole++)
				triangulation.AddHole(CreatePolyline(polygon.Holes[hole], holeOffsets[hole], false));
			if (!triangulation.Triangulate(true))
			{
				failure = "GlobalCdtFailed-" + triangulation.Error.GetType().Name;
				return false;
			}

			var indices = new List<int>();
			foreach (detria.Triangle triangle in triangulation.EnumerateTriangles(false))
			{
				ct.ThrowIfCancellationRequested();
				float orientation = Cross(points[triangle.y] - points[triangle.x], points[triangle.z] - points[triangle.x]);
				if (math.abs(orientation) <= MinimumArea)
					continue;
				indices.Add(triangle.x);
				indices.Add(orientation > 0f ? triangle.y : triangle.z);
				indices.Add(orientation > 0f ? triangle.z : triangle.y);
			}

			if (!Validate(polygon, points, indices))
			{
				failure = "GlobalCdtValidationFailed";
				return false;
			}

			vertices = points.ToArray();
			triangles = indices.ToArray();
			return true;
		}

		private static bool AddBoundary(float2[] ring, List<float2> points, HashSet<(long, long)> keys)
		{
			if (ring == null || ring.Length < 3 || math.abs(SignedArea(ring)) <= MinimumArea)
				return false;
			for (int point = 0; point < ring.Length; point++)
			{
				float2 value = ring[point];
				if (!math.all(math.isfinite(value)) || math.distancesq(value, ring[(point + 1) % ring.Length]) <= 1e-14f || !keys.Add(Key(value)))
					return false;
				points.Add(value);
			}
			return true;
		}

		private static void AddSteiner(float2 point, Polygon2D polygon, float boundaryToleranceSq, List<float2> points, HashSet<(long, long)> keys)
		{
			if (!math.all(math.isfinite(point)) || !polygon.Contains(point) || polygon.DistanceToBoundarySq(point) <= boundaryToleranceSq)
				return;
			if (keys.Add(Key(point)))
				points.Add(point);
		}

		private static int[] CreatePolyline(float2[] ring, int offset, bool counterClockwise)
		{
			var result = new int[ring.Length];
			bool direct = SignedArea(ring) > 0f == counterClockwise;
			for (int point = 0; point < ring.Length; point++)
				result[point] = offset + (direct ? point : ring.Length - 1 - point);
			return result;
		}

		private static bool Validate(Polygon2D polygon, List<float2> vertices, List<int> triangles)
		{
			if (triangles.Count == 0 || triangles.Count % 3 != 0)
				return false;
			double triangleArea = 0.0;
			var expectedBoundary = new HashSet<ulong>();
			int offset = 0;
			AddBoundaryEdges(expectedBoundary, offset, polygon.Outer.Length);
			offset += polygon.Outer.Length;
			for (int hole = 0; hole < polygon.Holes.Count; hole++)
			{
				AddBoundaryEdges(expectedBoundary, offset, polygon.Holes[hole].Length);
				offset += polygon.Holes[hole].Length;
			}

			var counts = new Dictionary<ulong, int>();
			var used = new bool[vertices.Count];
			for (int triangle = 0; triangle < triangles.Count; triangle += 3)
			{
				int a = triangles[triangle];
				int b = triangles[triangle + 1];
				int c = triangles[triangle + 2];
				if (a < 0 || b < 0 || c < 0 || a >= vertices.Count || b >= vertices.Count || c >= vertices.Count || a == b || b == c || a == c)
					return false;
				if (Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]) <= MinimumArea)
					return false;
				triangleArea += CrossDouble(vertices[a], vertices[b], vertices[c]) * 0.5;
				used[a] = true;
				used[b] = true;
				used[c] = true;
				CountEdge(counts, a, b);
				CountEdge(counts, b, c);
				CountEdge(counts, c, a);
			}
			for (int point = 0; point < used.Length; point++)
			{
				if (!used[point])
					return false;
			}
			foreach (ulong edge in expectedBoundary)
			{
				if (!counts.TryGetValue(edge, out int count) || count != 1)
					return false;
			}
			foreach (var pair in counts)
			{
				if (expectedBoundary.Contains(pair.Key) ? pair.Value != 1 : pair.Value != 2)
					return false;
			}
			if (vertices.Count - counts.Count + triangles.Count / 3 != 1 - polygon.Holes.Count)
				return false;
			double polygonArea = Math.Abs(SignedAreaDouble(polygon.Outer));
			for (int hole = 0; hole < polygon.Holes.Count; hole++)
				polygonArea -= Math.Abs(SignedAreaDouble(polygon.Holes[hole]));
			double areaTolerance = Math.Max(1e-5, polygonArea * 1e-7);
			return polygonArea > areaTolerance && Math.Abs(triangleArea - polygonArea) <= areaTolerance;
		}

		private static void AddBoundaryEdges(HashSet<ulong> edges, int offset, int count)
		{
			for (int edge = 0; edge < count; edge++)
				edges.Add(EdgeKey(offset + edge, offset + (edge + 1) % count));
		}

		private static void CountEdge(Dictionary<ulong, int> counts, int a, int b)
		{
			ulong key = EdgeKey(a, b);
			counts.TryGetValue(key, out int count);
			counts[key] = count + 1;
		}

		private static ulong EdgeKey(int a, int b)
		{
			uint minimum = (uint)math.min(a, b);
			uint maximum = (uint)math.max(a, b);
			return ((ulong)minimum << 32) | maximum;
		}

		private static (long, long) Key(float2 point)
		{
			return ((long)math.round((double)point.x * SweepRibbonPolygonUnion.Scale), (long)math.round((double)point.y * SweepRibbonPolygonUnion.Scale));
		}

		private static float SignedArea(IReadOnlyList<float2> ring)
		{
			float area = 0f;
			for (int point = 0; point < ring.Count; point++)
			{
				float2 a = ring[point];
				float2 b = ring[(point + 1) % ring.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}

		private static double SignedAreaDouble(IReadOnlyList<float2> ring)
		{
			double area = 0.0;
			for (int point = 0; point < ring.Count; point++)
			{
				float2 a = ring[point];
				float2 b = ring[(point + 1) % ring.Count];
				area += (double)a.x * b.y - (double)b.x * a.y;
			}
			return area * 0.5;
		}

		private static double CrossDouble(float2 a, float2 b, float2 c)
		{
			return ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
		}

		private static float Cross(float2 first, float2 second)
		{
			return first.x * second.y - first.y * second.x;
		}
	}
}
