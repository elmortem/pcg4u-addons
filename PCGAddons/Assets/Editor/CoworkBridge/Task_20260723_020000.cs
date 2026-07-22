using System.Threading.Tasks;
using UnityEngine;

public static class Task_20260723_020000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		int wallTriangles = 0;
		int degenerateUvTriangles = 0;
		float minUvArea = float.PositiveInfinity;
		float maxUvArea = 0f;
		foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
		{
			if (filter.sharedMesh == null || !filter.gameObject.scene.IsValid() || !filter.gameObject.scene.isLoaded)
				continue;

			var vertices = filter.sharedMesh.vertices;
			var triangles = filter.sharedMesh.triangles;
			var uvs = filter.sharedMesh.uv;
			if (uvs.Length != vertices.Length)
				continue;

			for (int i = 0; i + 2 < triangles.Length; i += 3)
			{
				int a = triangles[i];
				int b = triangles[i + 1];
				int c = triangles[i + 2];
				Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
				if (face.sqrMagnitude <= 1e-12f)
					continue;
				face.Normalize();
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
		}

		return $"wallTriangles={wallTriangles} degenerateUv={degenerateUvTriangles} minUvArea={minUvArea:E3} maxUvArea={maxUvArea:E3}";
	}
}
