using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class Task_20260607_135000
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
		float spread = Mathf.Max(2f, sv.size * 0.02f);
		for (int x = -5; x < 5; x++)
		{
			for (int z = -5; z < 5; z++)
			{
				object pd = Activator.CreateInstance(pointDataType);
				posField.SetValue(pd, new Unity.Mathematics.float3(pivot.x + x * spread, pivot.y, pivot.z + z * spread));
				normalField.SetValue(pd, new Unity.Mathematics.float3(0, 1, 0));
				scaleField.SetValue(pd, Mathf.Max(1f, spread * 0.4f));
				list.Add(pd);
			}
		}
		_pointsList = list;

		_drawBoxes = _fastGizmosType.GetMethod("DrawBoxes", BindingFlags.Public | BindingFlags.Static);
		_updateGizmos = _fastGizmosType.GetMethod("UpdateGizmos", BindingFlags.Public | BindingFlags.Static);
		_ticks = 0;
		EditorApplication.update += Tick;
		Debug.Log("Long draw loop started at pivot " + pivot + " spread=" + spread + ". Stops when Temp/stop_draw.txt appears or after ~2 min.");
		return "Long draw loop started";
	}

	private static void Tick()
	{
		try
		{
			_drawBoxes.Invoke(null, new object[] { _owner, _pointsList, 1f, Color.green, Matrix4x4.identity, null });
			_updateGizmos.Invoke(null, null);
			if (_ticks % 10 == 0)
				SceneView.RepaintAll();
		}
		catch (Exception e)
		{
			EditorApplication.update -= Tick;
			File.WriteAllText("Temp/fastgizmos_diag4.txt", "TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks % 100 == 0 && File.Exists("Temp/stop_draw.txt"))
		{
			EditorApplication.update -= Tick;
			File.WriteAllText("Temp/fastgizmos_diag4.txt", "stopped by flag at tick " + _ticks);
			return;
		}
		if (_ticks >= 20000)
		{
			EditorApplication.update -= Tick;
			File.WriteAllText("Temp/fastgizmos_diag4.txt", "stopped by tick limit");
		}
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
