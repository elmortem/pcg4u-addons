using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class Task_20260607_141000
{
	private static int _ticks;
	private static object _pointsList;
	private static MethodInfo _drawBoxes;
	private static MethodInfo _updateGizmos;
	private static Type _fastGizmosType;
	private static readonly object _owner = new object();
	private static bool _captured;

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
		_captured = false;
		EditorApplication.update += Tick;
		RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
		return "Draw + endCameraRendering capture scheduled";
	}

	private static void Tick()
	{
		try
		{
			_drawBoxes.Invoke(null, new object[] { _owner, _pointsList, 1f, Color.green, Matrix4x4.identity, null });
			_updateGizmos.Invoke(null, null);
			SceneView.RepaintAll();
		}
		catch (Exception e)
		{
			Cleanup();
			File.WriteAllText("Temp/fastgizmos_diag6.txt", "TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks > 900)
		{
			Cleanup();
			File.WriteAllText("Temp/fastgizmos_diag6.txt", "done, captured=" + _captured);
		}
	}

	private static void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
	{
		if (_ticks < 500 || _captured)
			return;
		var sv = SceneView.lastActiveSceneView;
		if (sv == null || cam != sv.camera)
			return;

		var rt = cam.targetTexture;
		if (rt == null)
			return;

		_captured = true;
		var prev = RenderTexture.active;
		RenderTexture.active = rt;
		var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		tex.Apply();
		RenderTexture.active = prev;
		var png = tex.EncodeToPNG();
		UnityEngine.Object.DestroyImmediate(tex);
		File.WriteAllBytes("Temp/sceneview_end.png", png);
	}

	private static void Cleanup()
	{
		EditorApplication.update -= Tick;
		RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
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
