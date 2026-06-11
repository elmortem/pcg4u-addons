using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SceneViewShotSettingsIO
{
	public static string GetSettingsPath()
	{
		string projectRoot = Directory.GetParent(Application.dataPath).FullName;
		return Path.Combine(projectRoot, "ProjectSettings", "SceneViewShot.json");
	}

	public static SceneViewShotSettings Load()
	{
		string path = GetSettingsPath();
		if (!File.Exists(path))
		{
			return new SceneViewShotSettings();
		}

		string json = File.ReadAllText(path);
		SceneViewShotSettings settings = JsonUtility.FromJson<SceneViewShotSettings>(json);
		if (settings == null)
		{
			return new SceneViewShotSettings();
		}

		if (settings.Items == null)
		{
			settings.Items = new List<SceneViewShotItem>();
		}

		return settings;
	}

	public static void Save(SceneViewShotSettings settings)
	{
		string path = GetSettingsPath();
		string json = JsonUtility.ToJson(settings, true);
		File.WriteAllText(path, json);
	}
}
