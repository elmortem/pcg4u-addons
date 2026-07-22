using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PCG;
using PCG.Exec;
using UnityEngine;

public static class Task_20260723_011000
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
		int wallVertices = 0;
		int hardWallShared = 0;
		float worstWallDot = 1f;
		foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
		{
			if (filter.sharedMesh == null || !filter.gameObject.scene.IsValid() || !filter.gameObject.scene.isLoaded)
				continue;

			var vertices = filter.sharedMesh.vertices;
			var triangles = filter.sharedMesh.triangles;
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

				wallVertices++;
				float vertexWorst = 1f;
				for (int a = 0; a < faces[v].Count; a++)
				{
					for (int b = a + 1; b < faces[v].Count; b++)
						vertexWorst = Mathf.Min(vertexWorst, Vector3.Dot(faces[v][a], faces[v][b]));
				}
				worstWallDot = Mathf.Min(worstWallDot, vertexWorst);
				if (vertexWorst < 0.7f)
					hardWallShared++;
			}
		}

		var camera = Camera.main;
		if (camera == null)
			return $"generated={generated} wallVertices={wallVertices} hardWallShared={hardWallShared} worstDot={worstWallDot:F3} camera=missing";

		const int width = 1024;
		const int height = 768;
		var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
		var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
		var previousTarget = camera.targetTexture;
		var previousActive = RenderTexture.active;
		string path = Path.GetFullPath(Path.Combine("Temp", "CodexSweepNormals.png"));
		try
		{
			camera.targetTexture = renderTexture;
			RenderTexture.active = renderTexture;
			camera.Render();
			texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
			texture.Apply();
			File.WriteAllBytes(path, texture.EncodeToPNG());
		}
		finally
		{
			camera.targetTexture = previousTarget;
			RenderTexture.active = previousActive;
			Object.DestroyImmediate(texture);
			Object.DestroyImmediate(renderTexture);
		}

		return $"generated={generated} wallVertices={wallVertices} hardWallShared={hardWallShared} worstDot={worstWallDot:F3} screenshot={path}";
	}
}
