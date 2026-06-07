using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class Task_20260607_134000
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
		var flags = BindingFlags.Public | BindingFlags.Static;

		foreach (var name in new[] { "MaxDrawDistance", "DetailLevels", "CameraMovementThreshold", "CameraRotationThreshold", "UpdateDelay", "FramesToRemove", "CalcFrustumCulling", "MinProcessingPoints", "MaxProcessingPoints" })
		{
			var f = _fastGizmosType.GetField(name, flags);
			Debug.Log("[cfg] " + name + " = " + (f != null ? f.GetValue(null) : "<no field>"));
		}

		var cullField = _fastGizmosType.GetField("CalcFrustumCulling", flags);
		cullField.SetValue(null, true);

		var sv = SceneView.lastActiveSceneView;
		var cam = sv.camera;
		Vector3 pivot = sv.pivot;

		var planesField = _fastGizmosType.GetField("_lastFrustumPlanes", BindingFlags.NonPublic | BindingFlags.Static);
		var planes = planesField.GetValue(null);
		if (planes == null)
		{
			Debug.Log("[planes] _lastFrustumPlanes = null");
		}
		else
		{
			DumpPlanes(planes, "stored");
			var testMi = planes.GetType().GetMethod("TestPoint", new[] { typeof(Unity.Mathematics.float3) });
			object r = testMi.Invoke(planes, new object[] { new Unity.Mathematics.float3(pivot.x, pivot.y, pivot.z) });
			Debug.Log("[planes] stored TestPoint(pivot) = " + r);

			var updateMi = planes.GetType().GetMethod("UpdateData");
			updateMi.Invoke(planes, new object[] { cam });
			DumpPlanes(planes, "refreshed");
			r = testMi.Invoke(planes, new object[] { new Unity.Mathematics.float3(pivot.x, pivot.y, pivot.z) });
			Debug.Log("[planes] refreshed TestPoint(pivot) = " + r);
		}

		var pointDataType = FindType("PCG.Points.PointData");
		var listType = typeof(List<>).MakeGenericType(pointDataType);
		var list = (IList)Activator.CreateInstance(listType);
		var posField = pointDataType.GetField("Position");
		var normalField = pointDataType.GetField("Normal");
		var scaleField = pointDataType.GetField("Scale");
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
		_ticks = 0;
		EditorApplication.update += Tick;
		return "Experiment 3 scheduled (culling ON, planes refreshed)";
	}

	private static void DumpPlanes(object planes, string label)
	{
		var sb = new StringBuilder("[planes " + label + "] ");
		foreach (var fname in new[] { "left", "right", "bottom", "top" })
		{
			var f = planes.GetType().GetField(fname, BindingFlags.NonPublic | BindingFlags.Instance);
			sb.Append(fname + "=" + (f != null ? f.GetValue(planes).ToString() : "<none>") + " ");
		}
		Debug.Log(sb.ToString());
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
			EditorApplication.update -= Tick;
			File.WriteAllText("Temp/fastgizmos_diag3.txt", "TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks >= 300)
		{
			EditorApplication.update -= Tick;
			Dump();
		}
	}

	private static void Dump()
	{
		var sb = new StringBuilder();
		try
		{
			var datasField = _fastGizmosType.GetField("_gizmoDatas", BindingFlags.NonPublic | BindingFlags.Static);
			var datas = (IDictionary)datasField.GetValue(null);
			sb.AppendLine("_gizmoDatas = " + datas.Count);
			foreach (DictionaryEntry kvp in datas)
			{
				var gd = kvp.Value;
				var branches = (IList)gd.GetType().GetField("BranchBuffers").GetValue(gd);
				sb.AppendLine("  owner=" + kvp.Key + " branches=" + branches.Count);
				foreach (var b in branches)
				{
					sb.AppendLine("    branch count=" + b.GetType().GetField("Count").GetValue(b));
				}
			}
		}
		catch (Exception e)
		{
			sb.AppendLine("DUMP EXCEPTION: " + e);
		}
		File.WriteAllText("Temp/fastgizmos_diag3.txt", sb.ToString());
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
