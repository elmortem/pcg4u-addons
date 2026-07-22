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

			var vertices = new Vector3[n * 2];
			var uvs = new Vector2[n * 2];
			for (int i = 0; i < n; i++)
			{
				vertices[i] = top.Vertices[i];
				vertices[i + n] = top.Vertices[i] - down;
				uvs[i] = top.Uvs[i];
				uvs[i + n] = top.Uvs[i];
			}

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

				AddWall(triangles, directed, vertices, a, b, c, n);
				AddWall(triangles, directed, vertices, b, c, a, n);
				AddWall(triangles, directed, vertices, c, a, b, n);
			}

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles.ToArray()
			};
		}

		private static void AddWall(List<int> triangles, HashSet<long> directed, Vector3[] vertices, int a, int b, int inner, int n)
		{
			if (directed.Contains(Key(b, a)))
				return;

			Vector3 mid = (vertices[a] + vertices[b]) * 0.5f;
			Vector3 outward = mid - vertices[inner];
			outward.y = 0f;

			Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[b + n] - vertices[a]);

			if (Vector3.Dot(normal, outward) >= 0f)
			{
				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(b + n);

				triangles.Add(a);
				triangles.Add(b + n);
				triangles.Add(a + n);
			}
			else
			{
				triangles.Add(a);
				triangles.Add(b + n);
				triangles.Add(b);

				triangles.Add(a);
				triangles.Add(a + n);
				triangles.Add(b + n);
			}
		}

		private static long Key(int a, int b)
		{
			return ((long)a << 32) | (uint)b;
		}
	}
}
