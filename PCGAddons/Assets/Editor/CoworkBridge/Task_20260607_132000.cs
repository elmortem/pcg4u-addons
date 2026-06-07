using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class Task_20260607_132000
{
	private static int _ticks;
	private static object _pointsList;
	private static MethodInfo _drawBoxes;
	private static MethodInfo _updateGizmos;
	private static Type _fastGizmosType;
	private static readonly object _owner = new object();
	private static readonly List<string> _captured = new List<string>();

	public static string Run()
	{
		_fastGizmosType = FindType("PCG.Fast.FastGizmos");

		var cullField = _fastGizmosType.GetField("CalcFrustumCulling", BindingFlags.Public | BindingFlags.Static);
		cullField.SetValue(null, false);
		Debug.Log("CalcFrustumCulling set to false");

		var pointDataType = FindType("PCG.Points.PointData");
		var listType = typeof(List<>).MakeGenericType(pointDataType);
		var list = (IList)Activator.CreateInstance(listType);
		var posField = pointDataType.GetField("Position");
		var normalField = pointDataType.GetField("Normal");
		var scaleField = pointDataType.GetField("Scale");

		Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
		for (int x = 0; x < 10; x++)
		{
			for (int z = 0; z < 10; z++)
			{
				object pd = Activator.CreateInstance(pointDataType);
				posField.SetValue(pd, new Unity.Mathematics.float3(pivot.x + x * 2f, pivot.y, pivot.z + z * 2f));
				normalField.SetValue(pd, new Unity.Mathematics.float3(0, 1, 0));
				scaleField.SetValue(pd, 1f);
				list.Add(pd);
			}
		}
		_pointsList = list;

		_drawBoxes = _fastGizmosType.GetMethod("DrawBoxes", BindingFlags.Public | BindingFlags.Static);
		_updateGizmos = _fastGizmosType.GetMethod("UpdateGizmos", BindingFlags.Public | BindingFlags.Static);

		Application.logMessageReceived += OnLog;
		_ticks = 0;
		EditorApplication.update += Tick;
		Debug.Log("Experiment 2 started, no culling, 300 ticks");
		return "Experiment 2 scheduled";
	}

	private static void OnLog(string condition, string stackTrace, LogType type)
	{
		if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning || condition.Contains("FastGizmos"))
		{
			if (_captured.Count < 50)
				_captured.Add(type + ": " + condition);
		}
	}

	private static void Tick()
	{
		try
		{
			_drawBoxes.Invoke(null, new object[] { _owner, _pointsList, 1f, Color.red, Matrix4x4.identity, null });
			_updateGizmos.Invoke(null, null);
			SceneView.RepaintAll();
		}
		catch (Exception e)
		{
			EditorApplication.update -= Tick;
			Application.logMessageReceived -= OnLog;
			File.WriteAllText("Temp/fastgizmos_diag2.txt", "TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks >= 300)
		{
			EditorApplication.update -= Tick;
			Application.logMessageReceived -= OnLog;
			Dump();
		}
	}

	private static void Dump()
	{
		var sb = new StringBuilder();
		try
		{
			sb.AppendLine("realtime = " + Time.realtimeSinceStartup);

			var backendField = _fastGizmosType.GetField("_backend", BindingFlags.NonPublic | BindingFlags.Static);
			var backend = backendField.GetValue(null);
			sb.AppendLine("_backend = " + (backend != null ? backend.GetType().FullName : "null"));

			var pendingField = _fastGizmosType.GetField("_pendingUpdates", BindingFlags.NonPublic | BindingFlags.Static);
			var pending = (IDictionary)pendingField.GetValue(null);
			sb.AppendLine("_pendingUpdates = " + pending.Count);

			var camField = _fastGizmosType.GetField("_currentSceneCamera", BindingFlags.NonPublic | BindingFlags.Static);
			var cam = camField.GetValue(null) as Camera;
			sb.AppendLine("_currentSceneCamera = " + (cam != null ? cam.name + " pos=" + cam.transform.position : "null"));

			var lastPosField = _fastGizmosType.GetField("_lastCameraPosition", BindingFlags.NonPublic | BindingFlags.Static);
			sb.AppendLine("_lastCameraPosition = " + lastPosField.GetValue(null));

			var datasField = _fastGizmosType.GetField("_gizmoDatas", BindingFlags.NonPublic | BindingFlags.Static);
			var datas = (IDictionary)datasField.GetValue(null);
			sb.AppendLine("_gizmoDatas = " + datas.Count);
			foreach (DictionaryEntry kvp in datas)
			{
				var gd = kvp.Value;
				var t = gd.GetType();
				sb.AppendLine("  owner=" + kvp.Key);
				sb.AppendLine("    Count=" + t.GetField("Count").GetValue(gd));
				sb.AppendLine("    ForceUpdate=" + t.GetField("ForceUpdate").GetValue(gd));
				sb.AppendLine("    UpdateTime=" + t.GetField("UpdateTime").GetValue(gd));
				sb.AppendLine("    CTS=" + (t.GetField("CancellationTokenSource").GetValue(gd) != null ? "ALIVE" : "null"));
				var shape = t.GetField("Shape").GetValue(gd);
				sb.AppendLine("    Shape=" + (shape != null ? shape.GetType().Name : "null"));
				var branches = (IList)t.GetField("BranchBuffers").GetValue(gd);
				sb.AppendLine("    branches=" + branches.Count);
				foreach (var b in branches)
				{
					var count = (int)b.GetType().GetField("Count").GetValue(b);
					sb.AppendLine("      branch count=" + count);
				}
			}

			sb.AppendLine("captured logs (" + _captured.Count + "):");
			foreach (var c in _captured)
				sb.AppendLine("  " + c);
		}
		catch (Exception e)
		{
			sb.AppendLine("DUMP EXCEPTION: " + e);
		}

		File.WriteAllText("Temp/fastgizmos_diag2.txt", sb.ToString());
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
