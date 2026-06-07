using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class Task_20260607_140000
{
	private static int _ticks;
	private static object _pointsList;
	private static MethodInfo _drawBoxes;
	private static MethodInfo _updateGizmos;
	private static Type _fastGizmosType;
	private static readonly object _owner = new object();

	public static string Run()
	{
		_fastGizmosType = FindType("PCG.Fast.FastGizmos");

		var pointDataType = FindType("PCG.Points.PointData");
		var listType = typeof(List<>).MakeGenericType(pointDataType);
		var list = (IList)Activator.CreateInstance(listType);
		var posField = pointDataType.GetField("Position");
		var normalField = pointDataType.GetField("Normal");
		var scaleField = pointDataType.GetField("Scale");

		var sv = SceneView.lastActiveSceneView;
		Vector3 pivot = sv.pivot;
		float spread = Mathf.Max(2f, sv.size * 0.05f);
		for (int x = -5; x < 5; x++)
		{
			for (int z = -5; z < 5; z++)
			{
				object pd = Activator.CreateInstance(pointDataType);
				posField.SetValue(pd, new Unity.Mathematics.float3(pivot.x + x * spread, pivot.y, pivot.z + z * spread));
				normalField.SetValue(pd, new Unity.Mathematics.float3(0, 1, 0));
				scaleField.SetValue(pd, Mathf.Max(1f, spread * 0.3f));
				list.Add(pd);
			}
		}
		_pointsList = list;

		_drawBoxes = _fastGizmosType.GetMethod("DrawBoxes", BindingFlags.Public | BindingFlags.Static);
		_updateGizmos = _fastGizmosType.GetMethod("UpdateGizmos", BindingFlags.Public | BindingFlags.Static);
		_ticks = 0;
		EditorApplication.update += Tick;
		Debug.Log("Draw + capture loop started at pivot " + pivot + " spread=" + spread);
		return "Draw + capture scheduled";
	}

	private static void Tick()
	{
		try
		{
			_drawBoxes.Invoke(null, new object[] { _owner, _pointsList, 1f, Color.green, Matrix4x4.identity, null });
			_updateGizmos.Invoke(null, null);
			SceneView.RepaintAll();

			if (_ticks == 400 || _ticks == 800)
			{
				Capture(_ticks);
			}
		}
		catch (Exception e)
		{
			EditorApplication.update -= Tick;
			File.WriteAllText("Temp/fastgizmos_diag5.txt", "TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks > 800)
		{
			EditorApplication.update -= Tick;
			File.WriteAllText("Temp/fastgizmos_diag5.txt", "done");
		}
	}

	private static void Capture(int tick)
	{
		var sv = SceneView.lastActiveSceneView;
		var cam = sv.camera;
		var rt = cam.targetTexture;
		if (rt == null)
		{
			File.WriteAllText("Temp/capture_fail.txt", "scene camera targetTexture is null");
			return;
		}

		var prev = RenderTexture.active;
		RenderTexture.active = rt;
		var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		tex.Apply();
		RenderTexture.active = prev;

		var png = tex.EncodeToPNG();
		UnityEngine.Object.DestroyImmediate(tex);
		File.WriteAllBytes("Temp/sceneview_" + tick + ".png", png);
	}

	private static Type FindType(string fullName)
	{
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			var t = asm.GetType(fullName);
			if (t != null)
				return t;
		}
		return null;
	}
}
