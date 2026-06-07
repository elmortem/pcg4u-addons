using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

public class CoworkProbePass : CustomPass
{
	public static int ExecuteCount;
	public static int SceneCameraExecuteCount;
	public static string LastCameraNames = "";

	protected override void Execute(CustomPassContext ctx)
	{
		ExecuteCount++;
		var sv = SceneView.lastActiveSceneView;
		if (sv != null && ctx.hdCamera.camera == sv.camera)
			SceneCameraExecuteCount++;
		if (LastCameraNames.Length < 300 && !LastCameraNames.Contains(ctx.hdCamera.camera.name))
			LastCameraNames += ctx.hdCamera.camera.name + ";";
	}
}

public static class Task_20260607_142000
{
	private static int _ticks;
	private static object _pointsList;
	private static MethodInfo _drawBoxes;
	private static MethodInfo _updateGizmos;
	private static Type _fastGizmosType;
	private static readonly object _owner = new object();
	private static GameObject _probeHost;

	public static string Run()
	{
		_fastGizmosType = FindType("PCG.Fast.FastGizmos");

		_probeHost = new GameObject("CoworkProbeVolume");
		_probeHost.hideFlags = HideFlags.HideAndDontSave;
		var volume = _probeHost.AddComponent<CustomPassVolume>();
		volume.isGlobal = true;
		volume.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
		volume.AddPassOfType(typeof(CoworkProbePass));

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
		CoworkProbePass.ExecuteCount = 0;
		CoworkProbePass.SceneCameraExecuteCount = 0;
		CoworkProbePass.LastCameraNames = "";
		EditorApplication.update += Tick;
		return "Probe pass + draw loop scheduled";
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
		if (_ticks > 600)
		{
			var sb = new StringBuilder();
			sb.AppendLine("probe ExecuteCount = " + CoworkProbePass.ExecuteCount);
			sb.AppendLine("probe SceneCameraExecuteCount = " + CoworkProbePass.SceneCameraExecuteCount);
			sb.AppendLine("probe cameras = " + CoworkProbePass.LastCameraNames);
			try
			{
				var datasField = _fastGizmosType.GetField("_gizmoDatas", BindingFlags.NonPublic | BindingFlags.Static);
				var datas = (IDictionary)datasField.GetValue(null);
				foreach (DictionaryEntry kvp in datas)
				{
					var gd = kvp.Value;
					var branches = (IList)gd.GetType().GetField("BranchBuffers").GetValue(gd);
					sb.AppendLine("gizmoData branches=" + branches.Count);
				}
			}
			catch (Exception e)
			{
				sb.AppendLine("dump err: " + e.Message);
			}
			Finish(sb.ToString());
		}
	}

	private static void Finish(string text)
	{
		EditorApplication.update -= Tick;
		if (_probeHost != null)
			UnityEngine.Object.DestroyImmediate(_probeHost);
		File.WriteAllText("Temp/fastgizmos_diag7.txt", text);
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
