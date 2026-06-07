using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class Task_20260607_125851
{
	public static string Run()
	{
		var sb = new StringBuilder();

		var pipeline = GraphicsSettings.currentRenderPipeline;
		Debug.Log("[1] Pipeline: " + (pipeline != null ? pipeline.GetType().FullName + " / " + pipeline.name : "null (Built-in)"));

		var shader = Shader.Find("PCG4U/FastGizmosShapeHdrp");
		if (shader == null)
		{
			Debug.Log("[2] Shader PCG4U/FastGizmosShapeHdrp: NOT FOUND");
		}
		else
		{
			bool hasErrors = UnityEditor.ShaderUtil.ShaderHasError(shader);
			Debug.Log("[2] Shader found, isSupported=" + shader.isSupported + ", hasErrors=" + hasErrors);
			if (hasErrors)
			{
				var messages = UnityEditor.ShaderUtil.GetShaderMessages(shader);
				foreach (var m in messages)
				{
					Debug.Log("[2] ShaderMsg: " + m.severity + " line " + m.line + ": " + m.message);
				}
			}
		}

		var hdrpAsset = pipeline as HDRenderPipelineAsset;
		if (hdrpAsset != null)
		{
			Debug.Log("[3] supportCustomPass=" + hdrpAsset.currentPlatformRenderPipelineSettings.supportCustomPass);
		}

		try
		{
			var gsType = typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
			var instanceProp = gsType != null ? gsType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
			var gs = instanceProp != null ? instanceProp.GetValue(null) : null;
			if (gs != null)
			{
				var mi = gs.GetType().GetMethod("GetDefaultFrameSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (mi != null)
				{
					var fsObj = mi.Invoke(gs, new object[] { FrameSettingsRenderType.Camera });
					var fs = (FrameSettings)fsObj;
					Debug.Log("[4] DefaultFrameSettings(Camera) CustomPass=" + fs.IsEnabled(FrameSettingsField.CustomPass));
				}
				else
				{
					Debug.Log("[4] GetDefaultFrameSettings method not found");
				}
			}
			else
			{
				Debug.Log("[4] HDRenderPipelineGlobalSettings instance not available");
			}
		}
		catch (Exception e)
		{
			Debug.Log("[4] FrameSettings check failed: " + e.Message);
		}

		var volumes = Resources.FindObjectsOfTypeAll<CustomPassVolume>();
		Debug.Log("[5] CustomPassVolume count: " + volumes.Length);
		foreach (var v in volumes)
		{
			var passes = string.Join(", ", v.customPasses.Select(p => p == null ? "null" : p.GetType().Name + "(enabled=" + p.enabled + ")"));
			Debug.Log("[5] Volume '" + v.gameObject.name + "' active=" + v.gameObject.activeInHierarchy + " enabled=" + v.enabled +
				" isGlobal=" + v.isGlobal + " injection=" + v.injectionPoint + " hideFlags=" + v.gameObject.hideFlags + " passes=[" + passes + "]");
		}

		Type fastGizmosType = null;
		Type hdrpPassType = null;
		Type hdrpBootstrapType = null;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (fastGizmosType == null)
				fastGizmosType = asm.GetType("PCG.Fast.FastGizmos");
			if (hdrpPassType == null)
				hdrpPassType = asm.GetType("PCG.Fast.Hdrp.FastGizmosHdrpPass");
			if (hdrpBootstrapType == null)
				hdrpBootstrapType = asm.GetType("PCG.Fast.Hdrp.FastGizmosHdrpBootstrap");
		}

		Debug.Log("[6] Types: FastGizmos=" + (fastGizmosType != null ? fastGizmosType.Assembly.GetName().Name : "NOT FOUND") +
			", FastGizmosHdrpPass=" + (hdrpPassType != null ? hdrpPassType.Assembly.GetName().Name : "NOT FOUND") +
			", Bootstrap=" + (hdrpBootstrapType != null ? "found" : "NOT FOUND"));

		if (fastGizmosType != null)
		{
			var backendField = fastGizmosType.GetField("_backend", BindingFlags.NonPublic | BindingFlags.Static);
			var backend = backendField != null ? backendField.GetValue(null) : null;
			Debug.Log("[7] FastGizmos._backend = " + (backend != null ? backend.GetType().FullName : "null"));

			var datasField = fastGizmosType.GetField("_gizmoDatas", BindingFlags.NonPublic | BindingFlags.Static);
			var datas = datasField != null ? datasField.GetValue(null) as IDictionary : null;
			Debug.Log("[8] FastGizmos._gizmoDatas count = " + (datas != null ? datas.Count.ToString() : "n/a"));
			if (datas != null)
			{
				foreach (DictionaryEntry kvp in datas)
				{
					var gd = kvp.Value;
					var branchesField = gd.GetType().GetField("BranchBuffers");
					var branches = branchesField != null ? branchesField.GetValue(gd) as IList : null;
					int total = 0;
					int ready = 0;
					if (branches != null)
					{
						total = branches.Count;
						foreach (var b in branches)
						{
							var pb = b.GetType().GetField("PropertyBlock").GetValue(b);
							var args = b.GetType().GetField("ArgsBuffer").GetValue(b);
							var count = (int)b.GetType().GetField("Count").GetValue(b);
							if (pb != null && args != null && count > 0)
								ready++;
						}
					}
					Debug.Log("[8] GizmoData owner=" + kvp.Key + " branches=" + total + " readyBranches=" + ready);
				}
			}
		}

		if (hdrpPassType != null)
		{
			var staticBackendField = hdrpPassType.GetField("Backend", BindingFlags.Public | BindingFlags.Static);
			Debug.Log("[9] FastGizmosHdrpPass.Backend = " + (staticBackendField != null && staticBackendField.GetValue(null) != null ? "set" : "null"));
		}

		var sceneView = SceneView.lastActiveSceneView;
		Debug.Log("[10] SceneView.lastActiveSceneView = " + (sceneView != null ? "ok, camera=" + (sceneView.camera != null ? sceneView.camera.name : "null") : "null"));

		return "Diagnostics complete";
	}
}
