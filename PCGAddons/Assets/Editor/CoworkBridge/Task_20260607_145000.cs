using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

public class CoworkProbePass3 : CustomPass
{
	public static int BeforeTransparentCount;

	protected override void Execute(CustomPassContext ctx)
	{
		var sv = SceneView.lastActiveSceneView;
		if (sv != null && ctx.hdCamera.camera == sv.camera)
			BeforeTransparentCount++;
	}
}

public static class Task_20260607_145000
{
	private static int _ticks;
	private static GameObject _host;
	private static bool _originalLighting;

	public static string Run()
	{
		var sv = SceneView.lastActiveSceneView;
		Debug.Log("sceneLighting = " + sv.sceneLighting);
		_originalLighting = sv.sceneLighting;

		sv.sceneLighting = true;

		_host = new GameObject("CoworkProbeVolume3");
		_host.hideFlags = HideFlags.HideAndDontSave;
		var volume = _host.AddComponent<CustomPassVolume>();
		volume.isGlobal = true;
		volume.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
		volume.AddPassOfType(typeof(CoworkProbePass3));

		CoworkProbePass3.BeforeTransparentCount = 0;
		_ticks = 0;
		EditorApplication.update += Tick;
		return "Probe3 scheduled: sceneLighting was " + _originalLighting + ", forced to true";
	}

	private static void Tick()
	{
		SceneView.RepaintAll();
		_ticks++;
		if (_ticks > 200)
		{
			EditorApplication.update -= Tick;
			var sv = SceneView.lastActiveSceneView;
			var sb = new StringBuilder();
			sb.AppendLine("original sceneLighting = " + _originalLighting);
			sb.AppendLine("BeforeTransparent executes with lighting ON = " + CoworkProbePass3.BeforeTransparentCount);
			if (sv != null)
				sv.sceneLighting = _originalLighting;
			UnityEngine.Object.DestroyImmediate(_host);
			File.WriteAllText("Temp/fastgizmos_diag9.txt", sb.ToString());
		}
	}
}
