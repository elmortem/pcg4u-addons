using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class CoworkCapturePass : CustomPass
{
	public static bool DoCapture;
	public static bool Done;

	protected override void Execute(CustomPassContext ctx)
	{
		var sv = SceneView.lastActiveSceneView;
		if (!DoCapture || Done || sv == null || ctx.hdCamera.camera != sv.camera)
			return;

		Done = true;
		var rt = ctx.cameraColorBuffer.rt;
		AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
		{
			if (request.hasError)
			{
				File.WriteAllText("Temp/capture2_fail.txt", "readback error");
				return;
			}
			var data = request.GetData<byte>();
			var tex = new Texture2D(request.width, request.height, TextureFormat.RGBA32, false);
			tex.LoadRawTextureData(data);
			tex.Apply();
			File.WriteAllBytes("Temp/sceneview_pass.png", tex.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(tex);
		});
	}
}

public static class Task_20260607_150000
{
	private static int _ticks;
	private static object _pointsList;
	private static MethodInfo _drawBoxes;
	private static MethodInfo _updateGizmos;
	private static Type _fastGizmosType;
	private static readonly object _owner = new object();
	private static GameObject _host;

	public static string Run()
	{
		_fastGizmosType = FindType("PCG.Fast.FastGizmos");

		_host = new GameObject("CoworkCaptureVolume");
		_host.hideFlags = HideFlags.HideAndDontSave;
		var volume = _host.AddComponent<CustomPassVolume>();
		volume.isGlobal = true;
		volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
		volume.AddPassOfType(typeof(CoworkCapturePass));
		CoworkCapturePass.DoCapture = false;
		CoworkCapturePass.Done = false;

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
		return "Draw + pass capture scheduled";
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
			Finish("TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks == 500)
			CoworkCapturePass.DoCapture = true;

		if (_ticks > 700)
		{
			AsyncGPUReadback.WaitAllRequests();
			Finish("done, captured=" + CoworkCapturePass.Done);
		}
	}

	private static void Finish(string text)
	{
		EditorApplication.update -= Tick;
		if (_host != null)
			UnityEngine.Object.DestroyImmediate(_host);
		File.WriteAllText("Temp/fastgizmos_diag10.txt", text);
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
