using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepJunctionTriangulator
	{
		private const float KeyScale = 100000f;
		private const int MaxVertices = 2_000_000;

		internal static bool Triangulate(List<float2> loop, float h, CancellationToken ct, Action reportProgress, out List<float2> verts, out List<int> tris, out string failure)
		{
			var component = new SweepJunctionPlanComponent
			{
				Outer = loop?.ToArray(),
				Holes = Array.Empty<float2[]>(),
				OuterEdgePortalArms = loop == null ? null : CreateEmptyTags(loop.Count),
				HoleEdgePortalArms = Array.Empty<int[]>()
			};
			var domain = new SweepJunctionPlanDomain
			{
				Components = new[] { component }
			};
			return Triangulate(domain, h, ct, reportProgress, out verts, out tris, out failure);
		}

		internal static bool Triangulate(SweepJunctionPlanDomain domain, float h, CancellationToken ct, Action reportProgress, out List<float2> verts, out List<int> tris, out string failure)
		{
			verts = new List<float2>();
			tris = new List<int>();
			failure = null;
			if (domain == null || domain.Components == null || domain.Components.Length == 0)
			{
				failure = "DomainEmpty";
				return false;
			}

			h = math.max(h, 1e-3f);
			int holeCount = 0;
			for (int componentIndex = 0; componentIndex < domain.Components.Length; componentIndex++)
			{
				ct.ThrowIfCancellationRequested();
				SweepJunctionPlanComponent component = domain.Components[componentIndex];
				int budget = MaxVertices - verts.Count;
				if (budget <= 0)
				{
					failure = "BudgetExceeded";
					return false;
				}
				if (!TriangulateComponent(component, h, budget, ct, reportProgress, out List<float2> componentVerts, out List<int> componentTris, out string componentFailure))
				{
					failure = "Component-" + componentIndex + "-" + componentFailure;
					return false;
				}

				int offset = verts.Count;
				verts.AddRange(componentVerts);
				for (int triangle = 0; triangle < componentTris.Count; triangle++)
					tris.Add(offset + componentTris[triangle]);
				holeCount += component.Holes?.Length ?? 0;
				reportProgress?.Invoke();
			}

			if (!ValidateCombined(domain.Components.Length, holeCount, verts, tris))
			{
				failure = "CombinedValidationFailed";
				return false;
			}
			return true;
		}

		private static bool TriangulateComponent(SweepJunctionPlanComponent component, float h, int maxVertices, CancellationToken ct, Action reportProgress, out List<float2> verts, out List<int> tris, out string failure)
		{
			verts = new List<float2>();
			tris = new List<int>();
			failure = null;
			if (component == null || component.Outer == null || component.Outer.Length < 3)
			{
				failure = "BoundaryEmpty";
				return false;
			}

			float2[][] holes = component.Holes ?? Array.Empty<float2[]>();
			var keys = new HashSet<long>();
			if (!AddBoundaryLoop(component.Outer, "BoundaryInvalid", verts, keys, out failure))
				return false;
			for (int hole = 0; hole < holes.Length; hole++)
			{
				if (!AddBoundaryLoop(holes[hole], "HoleInvalid-" + hole, verts, keys, out failure))
					return false;
			}
			if (verts.Count > maxVertices)
			{
				failure = "BudgetExceeded";
				return false;
			}

			int boundaryCount = verts.Count;
			AddInteriorPoints(component.Outer, holes, h, maxVertices, verts, keys, ct, reportProgress);
			if (verts.Count > maxVertices)
			{
				failure = "BudgetExceeded";
				return false;
			}

			var points = new Vector2[verts.Count];
			for (int i = 0; i < verts.Count; i++)
				points[i] = new Vector2(verts[i].x, verts[i].y);

			var triangulation = new detria.Triangulation();
			triangulation.SetPoints(points);
			int offset = 0;
			triangulation.AddOutline(CreatePolyline(component.Outer, offset, true));
			offset += component.Outer.Length;
			for (int hole = 0; hole < holes.Length; hole++)
			{
				triangulation.AddHole(CreatePolyline(holes[hole], offset, false));
				offset += holes[hole].Length;
			}

			if (!triangulation.Triangulate(true))
			{
				failure = "CdtFailed-" + triangulation.Error.GetType().Name;
				return false;
			}

			foreach (detria.Triangle triangle in triangulation.EnumerateTriangles(false))
			{
				ct.ThrowIfCancellationRequested();
				AddTriangle(tris, triangle.x, triangle.y, triangle.z, verts);
			}
			if (!ValidateComponent(component.Outer, holes, boundaryCount, verts, tris))
			{
				failure = "ValidationFailed";
				return false;
			}
			return true;
		}

		private static int[] CreateEmptyTags(int count)
		{
			var tags = new int[count];
			for (int i = 0; i < tags.Length; i++)
				tags[i] = -1;
			return tags;
		}

		private static bool AddBoundaryLoop(float2[] loop, string error, List<float2> verts, HashSet<long> keys, out string failure)
		{
			failure = null;
			if (loop == null || loop.Length < 3 || math.abs(SignedArea(loop)) < 1e-10f)
			{
				failure = error;
				return false;
			}

			for (int i = 0; i < loop.Length; i++)
			{
				float2 point = loop[i];
				if (!math.all(math.isfinite(point)) || math.distancesq(point, loop[(i + 1) % loop.Length]) < 1e-12f || !keys.Add(Key(point)))
				{
					failure = error;
					return false;
				}
				verts.Add(point);
			}
			return true;
		}

		private static int[] CreatePolyline(float2[] loop, int offset, bool counterClockwise)
		{
			var indices = new int[loop.Length];
			bool direct = SignedArea(loop) > 0f == counterClockwise;
			for (int i = 0; i < indices.Length; i++)
				indices[i] = offset + (direct ? i : indices.Length - 1 - i);
			return indices;
		}

		private static void AddInteriorPoints(float2[] outer, float2[][] holes, float h, int maxVertices, List<float2> verts, HashSet<long> keys, CancellationToken ct, Action reportProgress)
		{
			float2 min = new float2(float.MaxValue, float.MaxValue);
			float2 max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < outer.Length; i++)
			{
				min = math.min(min, outer[i]);
				max = math.max(max, outer[i]);
			}

			float dy = h * 0.8660254f;
			float margin = math.max(1e-4f, h * 0.12f);
			int row = 0;
			int operations = 0;
			for (float y = min.y + dy; y < max.y; y += dy)
			{
				float offset = (row & 1) == 1 ? h * 0.5f : 0f;
				for (float x = min.x + h + offset; x < max.x; x += h)
				{
					operations++;
					if ((operations & 1023) == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress?.Invoke();
					}
					float2 point = new float2(x, y);
					if (!PointInDomain(outer, holes, point) || DistanceToDomainBoundary(outer, holes, point) < margin)
						continue;
					if (keys.Add(Key(point)))
						verts.Add(point);
					if (verts.Count >= maxVertices)
						return;
				}
				row++;
			}
		}

		private static void AddTriangle(List<int> tris, int a, int b, int c, List<float2> verts)
		{
			float orientation = Orient(verts[a], verts[b], verts[c]);
			if (orientation > 0f)
			{
				tris.Add(a);
				tris.Add(b);
				tris.Add(c);
			}
			else if (orientation < 0f)
			{
				tris.Add(a);
				tris.Add(c);
				tris.Add(b);
			}
		}

		private static bool ValidateComponent(float2[] outer, float2[][] holes, int boundaryCount, List<float2> verts, List<int> tris)
		{
			if (tris.Count == 0 || tris.Count % 3 != 0)
				return false;
			var boundaryEdges = new HashSet<ulong>();
			int offset = 0;
			AddBoundaryEdges(boundaryEdges, offset, outer.Length);
			offset += outer.Length;
			for (int hole = 0; hole < holes.Length; hole++)
			{
				AddBoundaryEdges(boundaryEdges, offset, holes[hole].Length);
				offset += holes[hole].Length;
			}

			if (offset != boundaryCount)
				return false;
			var counts = new Dictionary<ulong, int>();
			var used = new bool[verts.Count];
			if (!CollectTopology(verts, tris, counts, used))
				return false;
			for (int i = 0; i < used.Length; i++)
			{
				if (!used[i])
					return false;
			}
			foreach (ulong edge in boundaryEdges)
			{
				if (!counts.TryGetValue(edge, out int count) || count != 1)
					return false;
			}
			foreach (var pair in counts)
			{
				if (boundaryEdges.Contains(pair.Key) ? pair.Value != 1 : pair.Value != 2)
					return false;
			}
			return verts.Count - counts.Count + tris.Count / 3 == 1 - holes.Length;
		}

		private static bool ValidateCombined(int componentCount, int holeCount, List<float2> verts, List<int> tris)
		{
			if (tris.Count == 0 || tris.Count % 3 != 0)
				return false;
			var counts = new Dictionary<ulong, int>();
			var used = new bool[verts.Count];
			if (!CollectTopology(verts, tris, counts, used))
				return false;
			for (int i = 0; i < used.Length; i++)
			{
				if (!used[i])
					return false;
			}
			foreach (var pair in counts)
			{
				if (pair.Value != 1 && pair.Value != 2)
					return false;
			}
			return verts.Count - counts.Count + tris.Count / 3 == componentCount - holeCount;
		}

		private static bool CollectTopology(List<float2> verts, List<int> tris, Dictionary<ulong, int> counts, bool[] used)
		{
			for (int triangle = 0; triangle < tris.Count; triangle += 3)
			{
				int a = tris[triangle];
				int b = tris[triangle + 1];
				int c = tris[triangle + 2];
				if (a < 0 || b < 0 || c < 0 || a >= verts.Count || b >= verts.Count || c >= verts.Count || a == b || b == c || a == c)
					return false;
				if (Orient(verts[a], verts[b], verts[c]) <= 0f)
					return false;
				used[a] = true;
				used[b] = true;
				used[c] = true;
				CountEdge(counts, a, b);
				CountEdge(counts, b, c);
				CountEdge(counts, c, a);
			}
			return true;
		}

		private static void AddBoundaryEdges(HashSet<ulong> edges, int offset, int count)
		{
			for (int i = 0; i < count; i++)
				edges.Add(EdgeKey(offset + i, offset + (i + 1) % count));
		}

		private static void CountEdge(Dictionary<ulong, int> counts, int a, int b)
		{
			ulong key = EdgeKey(a, b);
			counts.TryGetValue(key, out int count);
			counts[key] = count + 1;
		}

		private static ulong EdgeKey(int a, int b)
		{
			uint min = (uint)math.min(a, b);
			uint max = (uint)math.max(a, b);
			return ((ulong)min << 32) | max;
		}

		private static bool PointInDomain(float2[] outer, float2[][] holes, float2 point)
		{
			if (!PointInPolygon(outer, point))
				return false;
			for (int hole = 0; hole < holes.Length; hole++)
			{
				if (PointInPolygon(holes[hole], point))
					return false;
			}
			return true;
		}

		private static float DistanceToDomainBoundary(float2[] outer, float2[][] holes, float2 point)
		{
			float best = DistanceToLoop(outer, point);
			for (int hole = 0; hole < holes.Length; hole++)
				best = math.min(best, DistanceToLoop(holes[hole], point));
			return best;
		}

		private static bool PointInPolygon(IReadOnlyList<float2> loop, float2 point)
		{
			bool inside = false;
			for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
			{
				float2 a = loop[i];
				float2 b = loop[j];
				if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
					inside = !inside;
			}
			return inside;
		}

		private static float DistanceToLoop(IReadOnlyList<float2> loop, float2 point)
		{
			float best = float.MaxValue;
			for (int i = 0; i < loop.Count; i++)
				best = math.min(best, DistanceToSegment(point, loop[i], loop[(i + 1) % loop.Count]));
			return best;
		}

		private static float DistanceToSegment(float2 point, float2 a, float2 b)
		{
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			float t = lengthSq > 1e-12f ? math.saturate(math.dot(point - a, ab) / lengthSq) : 0f;
			return math.distance(point, a + t * ab);
		}

		private static float SignedArea(IReadOnlyList<float2> loop)
		{
			float area = 0f;
			for (int i = 0; i < loop.Count; i++)
			{
				float2 a = loop[i];
				float2 b = loop[(i + 1) % loop.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}

		private static float Orient(float2 a, float2 b, float2 c)
		{
			return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
		}

		private static long Key(float2 point)
		{
			int x = (int)math.round(point.x * KeyScale);
			int y = (int)math.round(point.y * KeyScale);
			return ((long)x << 32) ^ (uint)y;
		}
	}
}
