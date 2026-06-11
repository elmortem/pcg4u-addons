using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SceneViewCaptureService
{
	public static void Capture(SceneViewShotItem item, SceneViewShotSettings settings)
	{
		int ppp = Mathf.Max(1, Mathf.RoundToInt(EditorGUIUtility.pixelsPerPoint));
		Rect workArea = EditorGUIUtility.GetMainWindowPosition();

		int left = settings.OffsetLeft;
		int right = settings.OffsetRight;
		int top = settings.OffsetTop;
		int bottom = settings.OffsetBottom;

		Vector2Int targetPx = ClampToWorkArea(item.Width, item.Height, ppp, workArea, left, right, top, bottom);

		int windowWidthPx = targetPx.x + left + right;
		int windowHeightPx = targetPx.y + top + bottom;

		SceneView sv = EditorWindow.CreateWindow<SceneView>();
		sv.drawGizmos = true;
		SceneViewPoseUtility.Apply(sv, item.Pose);

		sv.position = new Rect(workArea.x, workArea.y, windowWidthPx / (float)ppp, windowHeightPx / (float)ppp);
		sv.Focus();
		sv.Repaint();

		EditorApplication.delayCall += () =>
		{
			sv.Focus();
			sv.Repaint();

			EditorApplication.delayCall += () =>
			{
				WriteScreenshot(sv, item, settings.OutputFolder, ppp, targetPx, left, top);
				sv.Close();
			};
		};
	}

	private static Vector2Int ClampToWorkArea(int width, int height, int ppp, Rect workArea, int left, int right, int top, int bottom)
	{
		int maxWidthPx = Mathf.FloorToInt(workArea.width * ppp) - left - right;
		int maxHeightPx = Mathf.FloorToInt(workArea.height * ppp) - top - bottom;

		float scale = 1f;
		if (width > maxWidthPx)
		{
			scale = Mathf.Min(scale, maxWidthPx / (float)width);
		}

		if (height > maxHeightPx)
		{
			scale = Mathf.Min(scale, maxHeightPx / (float)height);
		}

		if (scale < 1f)
		{
			int clampedWidth = Mathf.FloorToInt(width * scale);
			int clampedHeight = Mathf.FloorToInt(height * scale);
			Debug.LogWarning($"Scene View Shot: запрошенное разрешение {width}x{height} не помещается на экране, уменьшено до {clampedWidth}x{clampedHeight}.");
			return new Vector2Int(clampedWidth, clampedHeight);
		}

		return new Vector2Int(width, height);
	}

	private static void WriteScreenshot(SceneView sv, SceneViewShotItem item, string outputFolder, int ppp, Vector2Int targetPx, int left, int top)
	{
		int originX = Mathf.RoundToInt(sv.position.x * ppp) + left;
		int originY = Mathf.RoundToInt(sv.position.y * ppp) + top;

		Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
			new Vector2(originX, originY), targetPx.x, targetPx.y);

		Texture2D tex = new Texture2D(targetPx.x, targetPx.y, TextureFormat.RGB24, false);
		tex.SetPixels(pixels);
		byte[] png = tex.EncodeToPNG();
		UnityEngine.Object.DestroyImmediate(tex);

		Directory.CreateDirectory(outputFolder);
		string fileName = BuildFileName(item.Name);
		string fullPath = Path.Combine(outputFolder, fileName);
		File.WriteAllBytes(fullPath, png);

		Debug.Log($"Scene View Shot: сохранён {fullPath} ({targetPx.x}x{targetPx.y}).");
	}

	private static string BuildFileName(string name)
	{
		string safeName = name;
		foreach (char invalid in Path.GetInvalidFileNameChars())
		{
			safeName = safeName.Replace(invalid, '_');
		}

		if (string.IsNullOrEmpty(safeName))
		{
			safeName = "Shot";
		}

		string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		return $"{safeName}_{timestamp}.png";
	}
}
