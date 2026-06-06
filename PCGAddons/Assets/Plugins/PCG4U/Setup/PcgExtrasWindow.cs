using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PCG.Setup
{
	public class PcgExtrasWindow : EditorWindow
	{
		private PcgExtrasCatalog _catalog;
		private Vector2 _scroll;
		private readonly HashSet<string> _installed = new();

		[MenuItem("Tools/PCG/Extras...")]
		public static void Open()
		{
			var window = GetWindow<PcgExtrasWindow>("PCG4U Extras");
			window.minSize = new Vector2(480f, 320f);
		}

		private void OnEnable()
		{
			_catalog = LoadCatalog();
			RefreshInstalled();
			Events.registeredPackages += OnRegisteredPackages;
			PcgPackageInstaller.Completed += OnInstallCompleted;
		}

		private void OnDisable()
		{
			Events.registeredPackages -= OnRegisteredPackages;
			PcgPackageInstaller.Completed -= OnInstallCompleted;
		}

		private void OnInspectorUpdate()
		{
			if (PcgPackageInstaller.IsBusy)
				Repaint();
		}

		private void OnRegisteredPackages(PackageRegistrationEventArgs args)
		{
			RefreshInstalled();
			Repaint();
		}

		private void OnInstallCompleted()
		{
			RefreshInstalled();
			Repaint();
		}

		private void RefreshInstalled()
		{
			_installed.Clear();
			var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
			foreach (var package in packages)
				_installed.Add(package.name);
		}

		private static PcgExtrasCatalog LoadCatalog()
		{
			var guids = AssetDatabase.FindAssets("t:PcgExtrasCatalog");
			if (guids.Length == 0)
				return null;
			return AssetDatabase.LoadAssetAtPath<PcgExtrasCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
		}

		private void OnGUI()
		{
			DrawRow("UniTask (required)",
				"Allocation-free async/await library used by the PCG4U compute pipeline.",
				PcgSetupConstants.UniTaskPackageName,
				PcgSetupConstants.UniTaskGitUrl,
				PcgSetupConstants.UniTaskOpenUpmVersion);
			EditorGUILayout.Space();
			if (_catalog == null)
			{
				EditorGUILayout.HelpBox("PcgExtrasCatalog asset not found.", MessageType.Warning);
				return;
			}
			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			foreach (var entry in _catalog.Packages)
				DrawRow(entry.DisplayName, entry.Description, entry.PackageName, entry.GitUrl, entry.OpenUpmVersion);
			EditorGUILayout.EndScrollView();
		}

		private void DrawRow(string displayName, string description, string packageName, string gitUrl, string openUpmVersion)
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
			if (!string.IsNullOrEmpty(description))
				EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			var hasGit = !string.IsNullOrEmpty(gitUrl);
			var hasOpenUpm = !string.IsNullOrEmpty(openUpmVersion) && !string.IsNullOrEmpty(packageName);
			if (!string.IsNullOrEmpty(packageName) && _installed.Contains(packageName))
			{
				EditorGUILayout.LabelField("Installed", EditorStyles.boldLabel, GUILayout.Width(70f));
			}
			else if (!hasGit && !hasOpenUpm)
			{
				EditorGUILayout.LabelField("In Progress", EditorStyles.miniLabel, GUILayout.Width(80f));
			}
			else
			{
				EditorGUILayout.LabelField("Install:", GUILayout.Width(50f));
				using (new EditorGUI.DisabledScope(PcgPackageInstaller.IsBusy))
				{
					if (hasGit && GUILayout.Button("Git", GUILayout.Width(80f)))
						PcgPackageInstaller.InstallFromGit(gitUrl);
					if (hasOpenUpm && GUILayout.Button("OpenUPM", GUILayout.Width(80f)))
						PcgPackageInstaller.InstallFromOpenUpm(packageName, openUpmVersion);
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}
	}
}
