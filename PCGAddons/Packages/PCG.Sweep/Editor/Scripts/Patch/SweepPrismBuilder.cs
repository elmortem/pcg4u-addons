using System.Collections.Generic;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepPrismBuilder
	{
		internal static SweepMeshData Extrude(SweepMeshData top, float height)
		{
			if (top.Vertices == null || top.Vertices.Length < 3 || top.Triangles == null || top.Triangles.Length < 3)
				return top;

			int n = top.Vertices.Length;
			var down = Vector3.up * height;

			var directed = new HashSet<long>();
			for (int t = 0; t + 2 < top.Triangles.Length; t += 3)
			{
				int a = top.Triangles[t];
				int b = top.Triangles[t + 1];
				int c = top.Triangles[t + 2];
				directed.Add(Key(a, b));
				directed.Add(Key(b, c));
				directed.Add(Key(c, a));
			}

			var boundaryVertices = new HashSet<int>();
			foreach (long edge in directed)
			{
				int a = (int)(edge >> 32);
				int b = (int)(uint)edge;
				if (directed.Contains(Key(b, a)))
					continue;
				boundaryVertices.Add(a);
				boundaryVertices.Add(b);
			}

			var vertices = new Vector3[n * 2 + boundaryVertices.Count * 2];
			var uvs = new Vector2[vertices.Length];
			for (int i = 0; i < n; i++)
			{
				vertices[i] = top.Vertices[i];
				vertices[i + n] = top.Vertices[i] - down;
				uvs[i] = top.Uvs[i];
				uvs[i + n] = top.Uvs[i];
			}

			var wallTop = new int[n];
			var wallBottom = new int[n];
			for (int i = 0; i < n; i++)
			{
				wallTop[i] = -1;
				wallBottom[i] = -1;
			}

			int wallVertex = n * 2;
			for (int i = 0; i < n; i++)
			{
				if (!boundaryVertices.Contains(i))
					continue;

				wallTop[i] = wallVertex;
				wallBottom[i] = wallVertex + 1;
				vertices[wallVertex] = top.Vertices[i];
				vertices[wallVertex + 1] = top.Vertices[i] - down;
				uvs[wallVertex] = top.Uvs[i];
				uvs[wallVertex + 1] = top.Uvs[i];
				wallVertex += 2;
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

			for (int t = 0; t + 2 < top.Triangles.Length; t += 3)
			{
				int a = top.Triangles[t];
				int b = top.Triangles[t + 1];
				int c = top.Triangles[t + 2];

				AddWall(triangles, directed, vertices, wallTop, wallBottom, a, b, c);
				AddWall(triangles, directed, vertices, wallTop, wallBottom, b, c, a);
				AddWall(triangles, directed, vertices, wallTop, wallBottom, c, a, b);
			}

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles.ToArray()
			};
		}

		private static void AddWall(List<int> triangles, HashSet<long> directed, Vector3[] vertices, int[] wallTop, int[] wallBottom, int a, int b, int inner)
		{
			if (directed.Contains(Key(b, a)))
				return;

			Vector3 mid = (vertices[a] + vertices[b]) * 0.5f;
			Vector3 outward = mid - vertices[inner];
			outward.y = 0f;

			int topA = wallTop[a];
			int topB = wallTop[b];
			int bottomA = wallBottom[a];
			int bottomB = wallBottom[b];
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

		private static long Key(int a, int b)
		{
			return ((long)a << 32) | (uint)b;
		}
	}
}
