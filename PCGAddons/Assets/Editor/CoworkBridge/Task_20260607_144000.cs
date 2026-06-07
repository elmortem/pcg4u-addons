using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class CoworkProbePass2 : CustomPass
{
	public static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

	protected override void Execute(CustomPassContext ctx)
	{
		string key = injectionPoint + "/" + ctx.hdCamera.camera.name;
		Counts.TryGetValue(key, out int c);
		Counts[key] = c + 1;
	}
}

public static class Task_20260607_144000
{
	private static int _ticks;
	private static readonly List<GameObject> _hosts = new List<GameObject>();
	private static int _beginScene;
	private static int _endScene;
	private static readonly Dictionary<string, int> _camRenders = new Dictionary<string, int>();

	public static string Run()
	{
		CoworkProbePass2.Counts.Clear();

		foreach (CustomPassInjectionPoint ip in Enum.GetValues(typeof(CustomPassInjectionPoint)))
		{
			var host = new GameObject("CoworkProbeVolume_" + ip);
			host.hideFlags = HideFlags.HideAndDontSave;
			var volume = host.AddComponent<CustomPassVolume>();
			volume.isGlobal = true;
			volume.injectionPoint = ip;
			var pass = volume.AddPassOfType(typeof(CoworkProbePass2));
			_hosts.Add(host);
		}

		RenderPipelineManager.beginCameraRendering += OnBegin;
		RenderPipelineManager.endCameraRendering += OnEnd;
		_ticks = 0;
		_beginScene = 0;
		_endScene = 0;
		_camRenders.Clear();
		EditorApplication.update += Tick;
		return "Probe2 scheduled";
	}

	private static void OnBegin(ScriptableRenderContext ctx, Camera cam)
	{
		_camRenders.TryGetValue(cam.name, out int c);
		_camRenders[cam.name] = c + 1;
		var sv = SceneView.lastActiveSceneView;
		if (sv != null && cam == sv.camera)
			_beginScene++;
	}

	private static void OnEnd(ScriptableRenderContext ctx, Camera cam)
	{
		var sv = SceneView.lastActiveSceneView;
		if (sv != null && cam == sv.camera)
			_endScene++;
	}

	private static void Tick()
	{
		SceneView.RepaintAll();
		_ticks++;
		if (_ticks > 400)
		{
			EditorApplication.update -= Tick;
			RenderPipelineManager.beginCameraRendering -= OnBegin;
			RenderPipelineManager.endCameraRendering -= OnEnd;

			var sb = new StringBuilder();
			sb.AppendLine("beginScene=" + _beginScene + " endScene=" + _endScene);
			foreach (var kv in _camRenders)
				sb.AppendLine("camera render: " + kv.Key + " x" + kv.Value);
			sb.AppendLine("probe executes:");
			foreach (var kv in CoworkProbePass2.Counts)
				sb.AppendLine("  " + kv.Key + " x" + kv.Value);

			try
			{
				var mi = typeof(CustomPassVolume).GetMethod("GetActivePassVolumes", BindingFlags.NonPublic | BindingFlags.Static);
				if (mi != null)
				{
					var listArg = new List<CustomPassVolume>();
					mi.Invoke(null, new object[] { CustomPassInjectionPoint.BeforeTransparent, listArg });
					sb.AppendLine("GetActivePassVolumes(BeforeTransparent) = " + listArg.Count);
					foreach (var v in listArg)
						sb.AppendLine("  vol: " + v.gameObject.name + " passes=" + v.customPasses.Count);
				}
				else
				{
					sb.AppendLine("GetActivePassVolumes not found");
					var f = typeof(CustomPassVolume).GetField("m_ActivePassVolumes", BindingFlags.NonPublic | BindingFlags.Static);
					if (f != null)
					{
						var l = (IList)f.GetValue(null);
						sb.AppendLine("m_ActivePassVolumes = " + l.Count);
						foreach (var v in l)
							sb.AppendLine("  vol: " + ((CustomPassVolume)v).gameObject.name);
					}
				}
			}
			catch (Exception e)
			{
				sb.AppendLine("registry err: " + e.Message);
			}

			foreach (var h in _hosts)
				UnityEngine.Object.DestroyImmediate(h);
			_hosts.Clear();

			File.WriteAllText("Temp/fastgizmos_diag8.txt", sb.ToString());
		}
	}
}
