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
			PcgSetupBanner.Draw();
			EditorGUILayout.Space(6f);
			DrawRequiredRow();
			EditorGUILayout.Space();
			if (_catalog == null)
			{
				EditorGUILayout.HelpBox("PcgExtrasCatalog asset not found.", MessageType.Warning);
				return;
			}
			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			foreach (var entry in _catalog.Packages)
				DrawRow(entry);
			EditorGUILayout.EndScrollView();
		}

		private void DrawRequiredRow()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField("UniTask + Unity Collections (required)", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Allocation-free async/await library and native collections used by the PCG4U compute pipeline.", EditorStyles.wordWrappedMiniLabel);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (_installed.Contains(PcgSetupConstants.UniTaskPackageName) && _installed.Contains(PcgSetupConstants.CollectionsPackageName))
			{
				EditorGUILayout.LabelField("Installed", EditorStyles.boldLabel, GUILayout.Width(70f));
			}
			else
			{
				EditorGUILayout.LabelField("Install:", GUILayout.Width(50f));
				using (new EditorGUI.DisabledScope(PcgPackageInstaller.IsBusy))
				{
					if (GUILayout.Button("Git", GUILayout.Width(80f)))
						PcgPackageInstaller.Install(new[] { PcgSetupConstants.UniTaskGitUrl, PcgSetupConstants.CollectionsPackageName });
					if (GUILayout.Button("OpenUPM", GUILayout.Width(80f)))
					{
						PcgManifestRegistryUtility.EnsureOpenUpmScope(PcgSetupConstants.UniTaskPackageName);
						PcgPackageInstaller.Install(new[] { PcgSetupConstants.UniTaskPackageName + "@" + PcgSetupConstants.UniTaskOpenUpmVersion, PcgSetupConstants.CollectionsPackageName });
					}
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}

		private void DrawRow(PcgExtrasPackageEntry entry)
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField(entry.DisplayName, EditorStyles.boldLabel);
			if (!string.IsNullOrEmpty(entry.Description))
				EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedMiniLabel);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			var hasGit = !string.IsNullOrEmpty(entry.GitUrl);
			var hasOpenUpm = !string.IsNullOrEmpty(entry.OpenUpmVersion) && !string.IsNullOrEmpty(entry.PackageName);
			if (!string.IsNullOrEmpty(entry.PackageName) && _installed.Contains(entry.PackageName))
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
						InstallEntry(entry, entry.GitUrl, false);
					if (hasOpenUpm && GUILayout.Button("OpenUPM", GUILayout.Width(80f)))
						InstallEntry(entry, entry.PackageName + "@" + entry.OpenUpmVersion, true);
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}

		private void InstallEntry(PcgExtrasPackageEntry entry, string rootIdentifier, bool useOpenUpm)
		{
			if (!PcgDependencyResolver.TryBuildInstallList(entry, _catalog, rootIdentifier, out var identifiers))
				return;
			if (useOpenUpm)
				PcgManifestRegistryUtility.EnsureOpenUpmScope(entry.PackageName);
			PcgPackageInstaller.Install(identifiers.ToArray());
		}
	}
}
