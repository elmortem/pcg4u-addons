using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepSurfaceTopologyValidator
	{
		private const float MinimumTriangleAreaSquared = 1e-20f;

		internal static bool TryValidate(Vector3[] vertices, Vector2[] uvs, int[] triangles, bool requireClosed, out string failure)
		{
			failure = null;
			if (vertices == null || uvs == null || triangles == null || vertices.Length == 0 || vertices.Length != uvs.Length || triangles.Length == 0 || triangles.Length % 3 != 0)
			{
				failure = "SurfaceMeshEmpty";
				return false;
			}

			var welded = new int[vertices.Length];
			var positions = new Dictionary<(long, long, long), int>();
			for (int vertex = 0; vertex < vertices.Length; vertex++)
			{
				Vector3 value = vertices[vertex];
				Vector2 uv = uvs[vertex];
				if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || !float.IsFinite(uv.x) || !float.IsFinite(uv.y))
				{
					failure = "SurfaceVertexInvalid-" + vertex;
					return false;
				}
				var key = (
					(long)math.round((double)value.x * SweepRibbonPolygonUnion.Scale),
					(long)math.round((double)value.y * SweepRibbonPolygonUnion.Scale),
					(long)math.round((double)value.z * SweepRibbonPolygonUnion.Scale));
				if (!positions.TryGetValue(key, out int index))
				{
					index = positions.Count;
					positions.Add(key, index);
				}
				welded[vertex] = index;
			}

			var edgeCounts = new Dictionary<ulong, int>();
			var edgeBalances = new Dictionary<ulong, int>();
			var triangleKeys = new HashSet<(int, int, int)>();
			var links = new Dictionary<int, List<(int, int)>>();
			for (int triangle = 0; triangle < triangles.Length; triangle += 3)
			{
				int ia = triangles[triangle];
				int ib = triangles[triangle + 1];
				int ic = triangles[triangle + 2];
				if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length || ia == ib || ib == ic || ia == ic)
				{
					failure = "SurfaceIndexInvalid-" + triangle / 3;
					return false;
				}
				float3 a = ToFloat3(vertices[ia]);
				float3 b = ToFloat3(vertices[ib]);
				float3 c = ToFloat3(vertices[ic]);
				if (math.lengthsq(math.cross(b - a, c - a)) <= MinimumTriangleAreaSquared)
				{
					failure = "SurfaceTriangleDegenerate-" + triangle / 3;
					return false;
				}
				int wa = welded[ia];
				int wb = welded[ib];
				int wc = welded[ic];
				if (wa == wb || wb == wc || wa == wc)
				{
					failure = "SurfaceWeldedDegenerate-" + triangle / 3;
					return false;
				}
				int x = wa;
				int y = wb;
				int z = wc;
				Sort(ref x, ref y, ref z);
				if (!triangleKeys.Add((x, y, z)))
				{
					failure = "SurfaceDuplicateTriangle-" + triangle / 3;
					return false;
				}
				CountEdge(edgeCounts, edgeBalances, wa, wb);
				CountEdge(edgeCounts, edgeBalances, wb, wc);
				CountEdge(edgeCounts, edgeBalances, wc, wa);
				AddLink(links, wa, wb, wc);
				AddLink(links, wb, wc, wa);
				AddLink(links, wc, wa, wb);
			}

			var boundaryDegrees = new Dictionary<int, int>();
			foreach (KeyValuePair<ulong, int> pair in edgeCounts)
			{
				if (pair.Value < 1 || pair.Value > 2)
				{
					failure = "SurfaceNonManifoldEdge";
					return false;
				}
				if (pair.Value == 2 && edgeBalances[pair.Key] != 0)
				{
					failure = "SurfaceWindingMismatch";
					return false;
				}
				if (pair.Value == 2)
					continue;
				if (requireClosed)
				{
					failure = "SurfaceOpenEdge";
					return false;
				}
				Increment(boundaryDegrees, (int)(pair.Key >> 32));
				Increment(boundaryDegrees, (int)(pair.Key & uint.MaxValue));
			}

			foreach (KeyValuePair<int, int> pair in boundaryDegrees)
			{
				if (pair.Value != 2)
				{
					failure = "SurfaceBoundaryBranch-" + pair.Key;
					return false;
				}
			}
			foreach (KeyValuePair<int, List<(int, int)>> pair in links)
			{
				if (!ValidateLink(pair.Value, boundaryDegrees.ContainsKey(pair.Key)))
				{
					failure = "SurfaceVertexFanInvalid-" + pair.Key;
					return false;
				}
			}
			return true;
		}

		private static bool ValidateLink(List<(int, int)> edges, bool boundary)
		{
			if (edges == null || edges.Count == 0)
				return false;
			var adjacency = new Dictionary<int, List<int>>();
			for (int edge = 0; edge < edges.Count; edge++)
			{
				AddNeighbor(adjacency, edges[edge].Item1, edges[edge].Item2);
				AddNeighbor(adjacency, edges[edge].Item2, edges[edge].Item1);
			}
			int endpoints = 0;
			foreach (KeyValuePair<int, List<int>> pair in adjacency)
			{
				if (pair.Value.Count == 1)
					endpoints++;
				else if (pair.Value.Count != 2)
					return false;
			}
			if (boundary ? endpoints != 2 : endpoints != 0)
				return false;
			var visited = new HashSet<int>();
			var pending = new Stack<int>();
			foreach (int vertex in adjacency.Keys)
			{
				pending.Push(vertex);
				break;
			}
			while (pending.Count > 0)
			{
				int vertex = pending.Pop();
				if (!visited.Add(vertex))
					continue;
				List<int> neighbors = adjacency[vertex];
				for (int neighbor = 0; neighbor < neighbors.Count; neighbor++)
					pending.Push(neighbors[neighbor]);
			}
			return visited.Count == adjacency.Count;
		}

		private static void AddLink(Dictionary<int, List<(int, int)>> links, int center, int first, int second)
		{
			if (!links.TryGetValue(center, out List<(int, int)> values))
			{
				values = new List<(int, int)>();
				links.Add(center, values);
			}
			values.Add((first, second));
		}

		private static void AddNeighbor(Dictionary<int, List<int>> adjacency, int vertex, int neighbor)
		{
			if (!adjacency.TryGetValue(vertex, out List<int> values))
			{
				values = new List<int>();
				adjacency.Add(vertex, values);
			}
			values.Add(neighbor);
		}

		private static void CountEdge(Dictionary<ulong, int> counts, Dictionary<ulong, int> balances, int first, int second)
		{
			uint minimum = (uint)math.min(first, second);
			uint maximum = (uint)math.max(first, second);
			ulong key = ((ulong)minimum << 32) | maximum;
			counts.TryGetValue(key, out int count);
			balances.TryGetValue(key, out int balance);
			counts[key] = count + 1;
			balances[key] = balance + (first < second ? 1 : -1);
		}

		private static void Increment(Dictionary<int, int> values, int key)
		{
			values.TryGetValue(key, out int value);
			values[key] = value + 1;
		}

		private static void Sort(ref int first, ref int second, ref int third)
		{
			if (first > second)
				Swap(ref first, ref second);
			if (second > third)
				Swap(ref second, ref third);
			if (first > second)
				Swap(ref first, ref second);
		}

		private static void Swap(ref int first, ref int second)
		{
			int value = first;
			first = second;
			second = value;
		}

		private static float3 ToFloat3(Vector3 value)
		{
			return new float3(value.x, value.y, value.z);
		}
	}
}
