using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class Task_20260722_231000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var rows = new List<Row>();
		foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
		{
			if (filter == null || filter.sharedMesh == null || !filter.gameObject.scene.IsValid() || !filter.gameObject.scene.isLoaded)
				continue;
			var mesh = filter.sharedMesh;
			var vertices = mesh.vertices;
			var triangles = mesh.triangles;
			var normals = mesh.normals;
			if (vertices.Length == 0 || triangles.Length < 3 || normals.Length != vertices.Length)
				continue;

			var incident = new List<Vector3>[vertices.Length];
			for (int i = 0; i < incident.Length; i++)
				incident[i] = new List<Vector3>();
			for (int i = 0; i + 2 < triangles.Length; i += 3)
			{
				int a = triangles[i];
				int b = triangles[i + 1];
				int c = triangles[i + 2];
				Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
				if (face.sqrMagnitude <= 1e-12f)
					continue;
				face.Normalize();
				incident[a].Add(face);
				incident[b].Add(face);
				incident[c].Add(face);
			}

			int sharpShared = 0;
			float worstDot = 1f;
			float weakestAlignment = 1f;
			for (int v = 0; v < incident.Length; v++)
			{
				var faces = incident[v];
				float vertexWorst = 1f;
				for (int a = 0; a < faces.Count; a++)
				{
					for (int b = a + 1; b < faces.Count; b++)
						vertexWorst = Mathf.Min(vertexWorst, Vector3.Dot(faces[a], faces[b]));
				}
				worstDot = Mathf.Min(worstDot, vertexWorst);
				if (vertexWorst < 0.7f)
				{
					sharpShared++;
					float best = -1f;
					for (int f = 0; f < faces.Count; f++)
						best = Mathf.Max(best, Vector3.Dot(normals[v], faces[f]));
					weakestAlignment = Mathf.Min(weakestAlignment, best);
				}
			}

			rows.Add(new Row
			{
				Name = filter.gameObject.name,
				Mesh = mesh.name,
				Vertices = vertices.Length,
				Triangles = triangles.Length / 3,
				SharpShared = sharpShared,
				WorstDot = worstDot,
				WeakestAlignment = weakestAlignment,
				BoundsY = mesh.bounds.size.y
			});
		}

		var selected = rows.OrderByDescending(x => x.SharpShared).ThenByDescending(x => x.Vertices).Take(20).ToList();
		var text = new StringBuilder();
		text.Append("sceneMeshes=").Append(rows.Count);
		foreach (var row in selected)
		{
			text.Append(" | ").Append(row.Name).Append('/').Append(row.Mesh)
				.Append(" v/t=").Append(row.Vertices).Append('/').Append(row.Triangles)
				.Append(" sharpShared=").Append(row.SharpShared)
				.Append(" worstDot=").Append(row.WorstDot.ToString("F3"))
				.Append(" weakAlign=").Append(row.WeakestAlignment.ToString("F3"))
				.Append(" boundsY=").Append(row.BoundsY.ToString("F3"));
		}
		return text.ToString();
	}

	private sealed class Row
	{
		public string Name;
		public string Mesh;
		public int Vertices;
		public int Triangles;
		public int SharpShared;
		public float WorstDot;
		public float WeakestAlignment;
		public float BoundsY;
	}
}
