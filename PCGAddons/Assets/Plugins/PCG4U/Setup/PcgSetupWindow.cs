using UnityEditor;
using UnityEngine;

namespace PCG.Setup
{
	public class PcgSetupWindow : EditorWindow
	{
		public static void Open()
		{
			var window = GetWindow<PcgSetupWindow>(true, "PCG4U Setup");
			window.minSize = new Vector2(420f, 150f);
			window.maxSize = new Vector2(420f, 150f);
		}

		private void OnEnable()
		{
			PcgPackageInstaller.Completed += OnInstallCompleted;
		}

		private void OnDisable()
		{
			PcgPackageInstaller.Completed -= OnInstallCompleted;
		}

		private void OnInspectorUpdate()
		{
			if (PcgPackageInstaller.IsBusy)
				Repaint();
		}

		private void OnInstallCompleted()
		{
			if (PcgPackageUtility.IsInstalled(PcgSetupConstants.UniTaskPackageName))
			{
				PcgSetupFlow.CompleteUniTaskInstall();
				return;
			}
			SessionState.EraseBool(PcgSetupConstants.SetupPendingExtrasKey);
		}

		private void OnGUI()
		{
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("PCG4U requires the UniTask package (MIT license).", EditorStyles.wordWrappedLabel);
			EditorGUILayout.LabelField("Choose installation source:", EditorStyles.wordWrappedLabel);
			GUILayout.FlexibleSpace();
			if (PcgPackageInstaller.IsBusy)
				EditorGUILayout.LabelField("Installing...", EditorStyles.miniLabel);
			using (new EditorGUI.DisabledScope(PcgPackageInstaller.IsBusy))
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Install via Git"))
				{
					SessionState.SetBool(PcgSetupConstants.SetupPendingExtrasKey, true);
					PcgPackageInstaller.InstallFromGit(PcgSetupConstants.UniTaskGitUrl);
				}
				if (GUILayout.Button("Install via OpenUPM"))
				{
					SessionState.SetBool(PcgSetupConstants.SetupPendingExtrasKey, true);
					PcgPackageInstaller.InstallFromOpenUpm(PcgSetupConstants.UniTaskPackageName, PcgSetupConstants.UniTaskOpenUpmVersion);
				}
				if (GUILayout.Button("Later"))
				{
					SessionState.SetBool(PcgSetupConstants.SetupDismissKey, true);
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.Space();
		}
	}
}
