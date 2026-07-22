using System.Collections.Generic;
using System.Threading.Tasks;
using PCG;
using PCG.Exec;
using UnityEngine;

public static class Task_20260723_021000
{
	public static async Task<string> Run()
	{
		var host = GameObject.Find("SweepGraph");
		if (host == null)
			return "SweepGraph missing";

		var component = host.GetComponent<PcgComponent>();
		if (component == null)
			return "PcgComponent missing";

		bool generated = await PcgGraphRunner.GenerateAsync(component);
		int wallTriangles = 0;
		int degenerateUvTriangles = 0;
		int hardWallShared = 0;
		int invalidTangentMeshes = 0;
		float minUvArea = float.PositiveInfinity;
		float maxUvArea = 0f;
		foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
		{
			if (filter.sharedMesh == null || !filter.gameObject.scene.IsValid() || !filter.gameObject.scene.isLoaded)
				continue;

			var vertices = filter.sharedMesh.vertices;
			var triangles = filter.sharedMesh.triangles;
			var uvs = filter.sharedMesh.uv;
			var tangents = filter.sharedMesh.tangents;
			if (tangents.Length != vertices.Length)
				invalidTangentMeshes++;
			if (uvs.Length != vertices.Length)
				continue;

			var faces = new List<Vector3>[vertices.Length];
			for (int i = 0; i < faces.Length; i++)
				faces[i] = new List<Vector3>();

			for (int i = 0; i + 2 < triangles.Length; i += 3)
			{
				int a = triangles[i];
				int b = triangles[i + 1];
				int c = triangles[i + 2];
				Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
				if (face.sqrMagnitude <= 1e-12f)
					continue;
				face.Normalize();
				faces[a].Add(face);
				faces[b].Add(face);
				faces[c].Add(face);
				if (Mathf.Abs(face.y) > 0.01f)
					continue;

				wallTriangles++;
				Vector2 ab = uvs[b] - uvs[a];
				Vector2 ac = uvs[c] - uvs[a];
				float area = Mathf.Abs(ab.x * ac.y - ab.y * ac.x) * 0.5f;
				minUvArea = Mathf.Min(minUvArea, area);
				maxUvArea = Mathf.Max(maxUvArea, area);
				if (area <= 1e-8f)
					degenerateUvTriangles++;
			}

			for (int v = 0; v < faces.Length; v++)
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

				float worst = 1f;
				for (int a = 0; a < faces[v].Count; a++)
				{
					for (int b = a + 1; b < faces[v].Count; b++)
						worst = Mathf.Min(worst, Vector3.Dot(faces[v][a], faces[v][b]));
				}
				if (worst < 0.7f)
					hardWallShared++;
			}
		}

		return $"generated={generated} wallTriangles={wallTriangles} degenerateUv={degenerateUvTriangles} minUvArea={minUvArea:E3} maxUvArea={maxUvArea:E3} hardWallShared={hardWallShared} invalidTangentMeshes={invalidTangentMeshes}";
	}
}
