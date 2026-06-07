using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

public static class Task_20260607_131000
{
	private static int _ticks;
	private static object _pointsList;
	private static MethodInfo _drawBoxes;
	private static MethodInfo _updateGizmos;
	private static Type _fastGizmosType;
	private static readonly object _owner = new object();

	public static string Run()
	{
		Debug.Log("[A] SceneView.drawGizmos = " + (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.drawGizmos.ToString() : "no sceneview"));

		Type pcgComponentType = FindType("PCG.PcgComponent") ?? FindTypeByName("PcgComponent");
		if (pcgComponentType != null)
		{
			var comps = UnityEngine.Object.FindObjectsOfType(pcgComponentType);
			Debug.Log("[B] PcgComponent in scene: " + comps.Length);
		}
		else
		{
			Debug.Log("[B] PcgComponent type not found");
		}

		var windows = Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Name.Contains("PcgGraph")).ToArray();
		Debug.Log("[C] PcgGraph windows open: " + windows.Length);

		_fastGizmosType = FindType("PCG.Fast.FastGizmos");
		var registryType = FindType("PCG.Fast.FastGizmosBackendRegistry");
		var entriesField = registryType.GetField("_entries", BindingFlags.NonPublic | BindingFlags.Static);
		var entries = entriesField.GetValue(null) as IList;
		Debug.Log("[D] Registry entries: " + entries.Count);

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

		_ticks = 0;
		EditorApplication.update += Tick;
		Debug.Log("[E] Experiment started: drawing 100 red boxes at pivot " + pivot + " for ~150 editor ticks. Results in Temp/fastgizmos_diag.txt");
		return "Experiment scheduled";
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
			File.WriteAllText("Temp/fastgizmos_diag.txt", "TICK EXCEPTION: " + e);
			return;
		}

		_ticks++;
		if (_ticks >= 150)
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
			var backendField = _fastGizmosType.GetField("_backend", BindingFlags.NonPublic | BindingFlags.Static);
			var backend = backendField.GetValue(null);
			sb.AppendLine("_backend = " + (backend != null ? backend.GetType().FullName : "null"));

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
					var pb = b.GetType().GetField("PropertyBlock").GetValue(b);
					var args = b.GetType().GetField("ArgsBuffer").GetValue(b);
					var count = (int)b.GetType().GetField("Count").GetValue(b);
					sb.AppendLine("    branch count=" + count + " pb=" + (pb != null) + " args=" + (args != null));
				}
			}

			var volumes = Resources.FindObjectsOfTypeAll<CustomPassVolume>();
			sb.AppendLine("CustomPassVolume count = " + volumes.Length);
			foreach (var v in volumes)
			{
				var passes = string.Join(", ", v.customPasses.Select(p => p == null ? "null" : p.GetType().Name + "(enabled=" + p.enabled + ")"));
				sb.AppendLine("  '" + v.gameObject.name + "' active=" + v.gameObject.activeInHierarchy + " enabled=" + v.enabled +
					" isGlobal=" + v.isGlobal + " injection=" + v.injectionPoint + " passes=[" + passes + "]");
			}

			var passType = FindType("PCG.Fast.Hdrp.FastGizmosHdrpPass");
			if (passType != null)
			{
				var bf = passType.GetField("Backend", BindingFlags.Public | BindingFlags.Static);
				sb.AppendLine("FastGizmosHdrpPass.Backend = " + (bf.GetValue(null) != null ? "set" : "null"));
			}
		}
		catch (Exception e)
		{
			sb.AppendLine("DUMP EXCEPTION: " + e);
		}

		File.WriteAllText("Temp/fastgizmos_diag.txt", sb.ToString());
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

	private static Type FindTypeByName(string name)
	{
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type[] types;
			try
			{
				types = asm.GetTypes();
			}
			catch
			{
				continue;
			}
			foreach (var t in types)
			{
				if (t.Name == name)
					return t;
			}
		}
		return null;
	}
}
