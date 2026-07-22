using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class Task_20260723_012000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var sceneView = SceneView.lastActiveSceneView;
		if (sceneView == null || sceneView.camera == null)
			return "SceneView camera missing";

		const int width = 1024;
		const int height = 768;
		var camera = sceneView.camera;
		var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
		var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
		var previousTarget = camera.targetTexture;
		var previousActive = RenderTexture.active;
		string path = Path.GetFullPath(Path.Combine("Temp", "CodexSweepSceneView.png"));
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

		return path;
	}
}
