using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

public class CoworkProbePass4 : CustomPass
{
	public static int Count;

	protected override void Execute(CustomPassContext ctx)
	{
		var sv = SceneView.lastActiveSceneView;
		if (sv != null && ctx.hdCamera.camera == sv.camera)
			Count++;
	}
}

public static class Task_20260607_151000
{
	private static int _ticks;
	private static GameObject _host;
	private static int _phase;
	private static int _withLighting;
	private static int _withoutLighting;

	public static string Run()
	{
		_host = new GameObject("CoworkProbeVolume4");
		_host.hideFlags = HideFlags.HideAndDontSave;
		var volume = _host.AddComponent<CustomPassVolume>();
		volume.isGlobal = true;
		volume.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
		volume.AddPassOfType(typeof(CoworkProbePass4));

		var sv = SceneView.lastActiveSceneView;
		sv.sceneLighting = true;
		CoworkProbePass4.Count = 0;
		_phase = 0;
		_ticks = 0;
		EditorApplication.update += Tick;
		return "A/B lighting test scheduled";
	}

	private static void Tick()
	{
		SceneView.RepaintAll();
		_ticks++;

		if (_phase == 0 && _ticks == 100)
		{
			_withLighting = CoworkProbePass4.Count;
			var sv = SceneView.lastActiveSceneView;
			sv.sceneLighting = false;
			CoworkProbePass4.Count = 0;
			_phase = 1;
		}
		else if (_phase == 1 && _ticks == 200)
		{
			_withoutLighting = CoworkProbePass4.Count;
			var sv = SceneView.lastActiveSceneView;
			sv.sceneLighting = true;
			EditorApplication.update -= Tick;
			Object.DestroyImmediate(_host);
			var sb = new StringBuilder();
			sb.AppendLine("BeforeTransparent executes per 100 ticks:");
			sb.AppendLine("  sceneLighting ON  = " + _withLighting);
			sb.AppendLine("  sceneLighting OFF = " + _withoutLighting);
			File.WriteAllText("Temp/fastgizmos_diag11.txt", sb.ToString());
		}
	}
}
