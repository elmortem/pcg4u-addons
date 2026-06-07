using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class Task_20260607_143000
{
	public static string Run()
	{
		var hdrpAssembly = typeof(HDRenderPipelineAsset).Assembly;
		var gsType = hdrpAssembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
		var instanceProp = gsType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		var gs = instanceProp.GetValue(null);
		Debug.Log("GlobalSettings asset: " + (gs != null ? gs.ToString() : "null"));

		if (gs != null)
		{
			foreach (var f in gsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
			{
				if (f.FieldType == typeof(FrameSettings))
				{
					var fs = (FrameSettings)f.GetValue(gs);
					Debug.Log("[FS field] " + f.Name +
						": CustomPass=" + fs.IsEnabled(FrameSettingsField.CustomPass) +
						" OpaqueObjects=" + fs.IsEnabled(FrameSettingsField.OpaqueObjects) +
						" TransparentObjects=" + fs.IsEnabled(FrameSettingsField.TransparentObjects));
				}
			}
		}

		var cams = Resources.FindObjectsOfTypeAll<HDAdditionalCameraData>();
		foreach (var cd in cams)
		{
			Debug.Log("[HDCameraData] " + cd.name + " customRenderingSettings=" + cd.customRenderingSettings +
				(cd.customRenderingSettings ? " CustomPassOverride=" + cd.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.CustomPass) : ""));
		}

		return "Frame settings dump complete";
	}
}
