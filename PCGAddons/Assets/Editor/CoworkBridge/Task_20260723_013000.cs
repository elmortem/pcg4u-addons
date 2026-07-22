using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class Task_20260723_013000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var groups = new Dictionary<Vector3Int, List<Vector4>>();
		foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
		{
			if (filter.sharedMesh == null || !filter.gameObject.scene.IsValid() || !filter.gameObject.scene.isLoaded)
				continue;

			var vertices = filter.sharedMesh.vertices;
			var triangles = filter.sharedMesh.triangles;
			var normals = filter.sharedMesh.normals;
			var faces = new List<Vector3>[vertices.Length];
			for (int i = 0; i < faces.Length; i++)
				faces[i] = new List<Vector3>();

			for (int i = 0; i + 2 < triangles.Length; i += 3)
			{
				int a = triangles[i];
				int b = triangles[i + 1];
				int c = triangles[i + 2];
				Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
				if (normal.sqrMagnitude <= 1e-12f)
					continue;
				normal.Normalize();
				faces[a].Add(normal);
				faces[b].Add(normal);
				faces[c].Add(normal);
			}

			for (int v = 0; v < vertices.Length; v++)
			{
				bool wall = faces[v].Count > 0;
				for (int f = 0; f < faces[v].Count; f++)
				{
					if (Mathf.Abs(faces[v][f].y) > 0.01f)
					{
						wall = false;
						break;
					}
				}
				if (!wall)
					continue;

				Vector3 position = filter.transform.TransformPoint(vertices[v]);
				Vector3 normal = filter.transform.TransformDirection(normals[v]).normalized;
				var key = new Vector3Int(
					Mathf.RoundToInt(position.x * 1000f),
					Mathf.RoundToInt(position.y * 1000f),
					Mathf.RoundToInt(position.z * 1000f));
				if (!groups.TryGetValue(key, out var entries))
				{
					entries = new List<Vector4>();
					groups.Add(key, entries);
				}
				entries.Add(new Vector4(normal.x, normal.y, normal.z, filter.GetInstanceID()));
			}
		}

		int sharedPositions = 0;
		int comparedPairs = 0;
		int mismatchedPairs = 0;
		float worstDot = 1f;
		foreach (var pair in groups)
		{
			bool shared = false;
			for (int a = 0; a < pair.Value.Count; a++)
			{
				for (int b = a + 1; b < pair.Value.Count; b++)
				{
					if (pair.Value[a].w == pair.Value[b].w)
						continue;
					shared = true;
					comparedPairs++;
					float dot = Vector3.Dot(pair.Value[a], pair.Value[b]);
					worstDot = Mathf.Min(worstDot, dot);
					if (dot < 0.95f)
						mismatchedPairs++;
				}
			}
			if (shared)
				sharedPositions++;
		}

		return new StringBuilder()
			.Append("sharedPositions=").Append(sharedPositions)
			.Append(" comparedPairs=").Append(comparedPairs)
			.Append(" mismatchedPairs=").Append(mismatchedPairs)
			.Append(" worstDot=").Append(worstDot.ToString("F3"))
			.ToString();
	}
}
