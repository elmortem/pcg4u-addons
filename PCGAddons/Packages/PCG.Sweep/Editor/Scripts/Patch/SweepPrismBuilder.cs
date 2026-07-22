using System.Collections.Generic;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepPrismBuilder
	{
		private const float SmoothWallCos = 0.70710678f;

		internal static SweepMeshData Extrude(SweepMeshData top, float height, float uvScale)
		{
			if (top.Vertices == null || top.Vertices.Length < 3 || top.Triangles == null || top.Triangles.Length < 3)
				return top;

			int n = top.Vertices.Length;
			var down = Vector3.up * height;

			var directed = new HashSet<long>();
			var innerByEdge = new Dictionary<long, int>();
			for (int t = 0; t + 2 < top.Triangles.Length; t += 3)
			{
				int a = top.Triangles[t];
				int b = top.Triangles[t + 1];
				int c = top.Triangles[t + 2];
				long ab = Key(a, b);
				long bc = Key(b, c);
				long ca = Key(c, a);
				directed.Add(ab);
				directed.Add(bc);
				directed.Add(ca);
				innerByEdge[ab] = c;
				innerByEdge[bc] = a;
				innerByEdge[ca] = b;
			}

			var boundaryEdges = new List<long>();
			foreach (long edge in directed)
			{
				int a = (int)(edge >> 32);
				int b = (int)(uint)edge;
				if (directed.Contains(Key(b, a)))
					continue;
				boundaryEdges.Add(edge);
			}
			boundaryEdges.Sort();

			int cornerCount = boundaryEdges.Count * 2;
			var cornerParents = new int[cornerCount];
			var edgeNormals = new Vector3[boundaryEdges.Count];
			var incoming = new Dictionary<int, List<int>>();
			var outgoing = new Dictionary<int, List<int>>();
			for (int i = 0; i < boundaryEdges.Count; i++)
			{
				cornerParents[i * 2] = i * 2;
				cornerParents[i * 2 + 1] = i * 2 + 1;
				int a = (int)(boundaryEdges[i] >> 32);
				int b = (int)(uint)boundaryEdges[i];
				Vector3 normal = Vector3.Cross(top.Vertices[b] - top.Vertices[a], down);
				normal.Normalize();
				edgeNormals[i] = normal;
				AddEdge(outgoing, a, i);
				AddEdge(incoming, b, i);
			}
			var seamVertices = FindSeamVertices(boundaryEdges, edgeNormals, outgoing);

			foreach (var pair in outgoing)
			{
				if (seamVertices.Contains(pair.Key))
					continue;
				if (!incoming.TryGetValue(pair.Key, out var previousEdges))
					continue;

				for (int p = 0; p < previousEdges.Count; p++)
				{
					int previous = previousEdges[p];
					for (int q = 0; q < pair.Value.Count; q++)
					{
						int next = pair.Value[q];
						if (Vector3.Dot(edgeNormals[previous], edgeNormals[next]) >= SmoothWallCos)
							Union(cornerParents, previous * 2 + 1, next * 2);
					}
				}
			}

			var cornerUs = BuildCornerUs(boundaryEdges, top.Vertices, cornerParents, incoming, outgoing, uvScale);

			var wallIndexByRoot = new Dictionary<int, int>();
			var cornerTop = new int[cornerCount];
			var cornerBottom = new int[cornerCount];
			int wallGroupCount = 0;
			for (int i = 0; i < cornerCount; i++)
			{
				int root = Find(cornerParents, i);
				if (!wallIndexByRoot.ContainsKey(root))
				{
					wallIndexByRoot.Add(root, wallGroupCount);
					wallGroupCount++;
				}
			}

			var vertices = new Vector3[n * 2 + wallGroupCount * 2];
			var uvs = new Vector2[vertices.Length];
			for (int i = 0; i < n; i++)
			{
				vertices[i] = top.Vertices[i];
				vertices[i + n] = top.Vertices[i] - down;
				uvs[i] = top.Uvs[i];
				uvs[i + n] = top.Uvs[i];
			}

			foreach (var pair in wallIndexByRoot)
			{
				int corner = pair.Key;
				int edge = corner / 2;
				int vertex = corner % 2 == 0
					? (int)(boundaryEdges[edge] >> 32)
					: (int)(uint)boundaryEdges[edge];
				int wallVertex = n * 2 + pair.Value * 2;
				vertices[wallVertex] = top.Vertices[vertex];
				vertices[wallVertex + 1] = top.Vertices[vertex] - down;
				uvs[wallVertex] = new Vector2(cornerUs[corner], 0f);
				uvs[wallVertex + 1] = new Vector2(cornerUs[corner], height * uvScale);
			}

			for (int i = 0; i < cornerCount; i++)
			{
				int wallVertex = n * 2 + wallIndexByRoot[Find(cornerParents, i)] * 2;
				cornerTop[i] = wallVertex;
				cornerBottom[i] = wallVertex + 1;
			}

			var triangles = new List<int>(top.Triangles.Length * 3);

			for (int t = 0; t + 2 < top.Triangles.Length; t += 3)
			{
				int a = top.Triangles[t];
				int b = top.Triangles[t + 1];
				int c = top.Triangles[t + 2];

				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);

				triangles.Add(a + n);
				triangles.Add(c + n);
				triangles.Add(b + n);
			}

			for (int i = 0; i < boundaryEdges.Count; i++)
			{
				AddWall(triangles, vertices, cornerTop[i * 2], cornerTop[i * 2 + 1], cornerBottom[i * 2], cornerBottom[i * 2 + 1], innerByEdge[boundaryEdges[i]]);
			}

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles.ToArray()
			};
		}

		private static void AddWall(List<int> triangles, Vector3[] vertices, int topA, int topB, int bottomA, int bottomB, int inner)
		{
			Vector3 mid = (vertices[topA] + vertices[topB]) * 0.5f;
			Vector3 outward = mid - vertices[inner];
			outward.y = 0f;

			Vector3 normal = Vector3.Cross(vertices[topB] - vertices[topA], vertices[bottomB] - vertices[topA]);

			if (Vector3.Dot(normal, outward) >= 0f)
			{
				triangles.Add(topA);
				triangles.Add(topB);
				triangles.Add(bottomB);

				triangles.Add(topA);
				triangles.Add(bottomB);
				triangles.Add(bottomA);
			}
			else
			{
				triangles.Add(topA);
				triangles.Add(bottomB);
				triangles.Add(topB);

				triangles.Add(topA);
				triangles.Add(bottomA);
				triangles.Add(bottomB);
			}
		}

		private static void AddEdge(Dictionary<int, List<int>> edges, int vertex, int edge)
		{
			if (!edges.TryGetValue(vertex, out var list))
			{
				list = new List<int>();
				edges.Add(vertex, list);
			}
			list.Add(edge);
		}

		private static HashSet<int> FindSeamVertices(List<long> boundaryEdges, Vector3[] edgeNormals, Dictionary<int, List<int>> outgoing)
		{
			var seams = new HashSet<int>();
			var visited = new bool[boundaryEdges.Count];
			for (int start = 0; start < boundaryEdges.Count; start++)
			{
				if (visited[start])
					continue;

				int current = start;
				int hardVertex = -1;
				int smoothestVertex = -1;
				float smoothestDot = -2f;
				bool closed = false;
				while (!visited[current])
				{
					visited[current] = true;
					int vertex = (int)(uint)boundaryEdges[current];
					if (!outgoing.TryGetValue(vertex, out var nextEdges) || nextEdges.Count != 1)
						break;

					int next = nextEdges[0];
					float dot = Vector3.Dot(edgeNormals[current], edgeNormals[next]);
					if (dot < SmoothWallCos && hardVertex < 0)
						hardVertex = vertex;
					if (dot > smoothestDot)
					{
						smoothestDot = dot;
						smoothestVertex = vertex;
					}

					current = next;
					if (current == start)
					{
						closed = true;
						break;
					}
				}

				if (closed)
					seams.Add(hardVertex >= 0 ? hardVertex : smoothestVertex);
			}
			return seams;
		}

		private static float[] BuildCornerUs(List<long> boundaryEdges, Vector3[] vertices, int[] cornerParents, Dictionary<int, List<int>> incoming, Dictionary<int, List<int>> outgoing, float uvScale)
		{
			var previous = new int[boundaryEdges.Count];
			var next = new int[boundaryEdges.Count];
			for (int i = 0; i < boundaryEdges.Count; i++)
			{
				previous[i] = -1;
				next[i] = -1;
			}

			foreach (var pair in outgoing)
			{
				if (!incoming.TryGetValue(pair.Key, out var previousEdges))
					continue;

				for (int p = 0; p < previousEdges.Count; p++)
				{
					int previousEdge = previousEdges[p];
					for (int q = 0; q < pair.Value.Count; q++)
					{
						int nextEdge = pair.Value[q];
						if (Find(cornerParents, previousEdge * 2 + 1) != Find(cornerParents, nextEdge * 2))
							continue;
						next[previousEdge] = nextEdge;
						previous[nextEdge] = previousEdge;
					}
				}
			}

			var cornerUs = new float[boundaryEdges.Count * 2];
			var visited = new bool[boundaryEdges.Count];
			for (int i = 0; i < boundaryEdges.Count; i++)
			{
				if (previous[i] < 0)
					AssignCornerUs(i, boundaryEdges, vertices, next, visited, cornerUs, uvScale);
			}
			for (int i = 0; i < boundaryEdges.Count; i++)
			{
				if (!visited[i])
					AssignCornerUs(i, boundaryEdges, vertices, next, visited, cornerUs, uvScale);
			}
			return cornerUs;
		}

		private static void AssignCornerUs(int start, List<long> boundaryEdges, Vector3[] vertices, int[] next, bool[] visited, float[] cornerUs, float uvScale)
		{
			float u = 0f;
			int current = start;
			while (current >= 0 && !visited[current])
			{
				visited[current] = true;
				int a = (int)(boundaryEdges[current] >> 32);
				int b = (int)(uint)boundaryEdges[current];
				cornerUs[current * 2] = u;
				Vector3 edge = vertices[b] - vertices[a];
				u += Mathf.Sqrt(edge.x * edge.x + edge.z * edge.z) * uvScale;
				cornerUs[current * 2 + 1] = u;
				current = next[current];
			}
		}

		private static int Find(int[] parents, int value)
		{
			while (parents[value] != value)
			{
				parents[value] = parents[parents[value]];
				value = parents[value];
			}
			return value;
		}

		private static void Union(int[] parents, int a, int b)
		{
			a = Find(parents, a);
			b = Find(parents, b);
			if (a != b)
				parents[b] = a;
		}

		private static long Key(int a, int b)
		{
			return ((long)a << 32) | (uint)b;
		}
	}
}
