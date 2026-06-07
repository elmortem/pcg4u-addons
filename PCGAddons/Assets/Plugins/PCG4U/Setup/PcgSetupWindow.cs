using UnityEditor;
using UnityEngine;

namespace PCG.Setup
{
	public class PcgSetupWindow : EditorWindow
	{
		public PcgSetupPage Page;

		public static void Open(PcgSetupPage page)
		{
			var window = GetWindow<PcgSetupWindow>(true, "PCG4U Setup");
			window.Page = page;
			var size = page == PcgSetupPage.UniTask
				? new Vector2(420f, 255f)
				: new Vector2(420f, 285f);
			window.minSize = size;
			window.maxSize = size;
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
				PcgSetupFlow.TryContinue();
				return;
			}
			SessionState.EraseBool(PcgSetupConstants.SetupPendingExtrasKey);
		}

		private void OnDestroy()
		{
			SessionState.SetBool(PcgSetupConstants.SetupDismissKey, true);
		}

		private void OnGUI()
		{
			PcgSetupBanner.Draw();
			GUILayout.BeginHorizontal();
			GUILayout.Space(10f);
			GUILayout.BeginVertical();
			GUILayout.Space(8f);
			if (Page == PcgSetupPage.RenderPipeline)
				DrawRenderPipelinePage();
			else
				DrawUniTaskPage();
			GUILayout.Space(8f);
			GUILayout.EndVertical();
			GUILayout.Space(10f);
			GUILayout.EndHorizontal();
		}

		private void DrawUniTaskPage()
		{
			EditorGUILayout.LabelField("PCG4U requires the UniTask package (MIT license).", EditorStyles.wordWrappedLabel);
			EditorGUILayout.LabelField("Choose installation source:", EditorStyles.wordWrappedLabel);
			GUILayout.Space(2f);
			EditorGUILayout.LabelField(PcgPackageInstaller.IsBusy ? "Installing..." : " ", EditorStyles.miniLabel);
			using (new EditorGUI.DisabledScope(PcgPackageInstaller.IsBusy))
			{
				if (GUILayout.Button("Install via Git", GUILayout.Height(26f)))
				{
					SessionState.SetBool(PcgSetupConstants.SetupPendingExtrasKey, true);
					PcgPackageInstaller.InstallFromGit(PcgSetupConstants.UniTaskGitUrl);
				}
				GUILayout.Space(4f);
				if (GUILayout.Button("Install via OpenUPM", GUILayout.Height(26f)))
				{
					SessionState.SetBool(PcgSetupConstants.SetupPendingExtrasKey, true);
					PcgPackageInstaller.InstallFromOpenUpm(PcgSetupConstants.UniTaskPackageName, PcgSetupConstants.UniTaskOpenUpmVersion);
				}
			}
		}

		private void DrawRenderPipelinePage()
		{
			EditorGUILayout.LabelField("Select the render pipeline used by this project.", EditorStyles.wordWrappedLabel);
			EditorGUILayout.LabelField("Support folders for other pipelines will be removed to avoid shader import errors.", EditorStyles.wordWrappedLabel);
			GUILayout.Space(6f);
			var detected = PcgRenderPipelineCleanup.DetectPipeline();
			if (DrawPipelineButton("Built-in", PcgRenderPipelineKind.BuiltIn, detected))
				ApplyPipeline(PcgRenderPipelineKind.BuiltIn);
			GUILayout.Space(4f);
			if (DrawPipelineButton("URP", PcgRenderPipelineKind.Urp, detected))
				ApplyPipeline(PcgRenderPipelineKind.Urp);
			GUILayout.Space(4f);
			if (DrawPipelineButton("HDRP", PcgRenderPipelineKind.Hdrp, detected))
				ApplyPipeline(PcgRenderPipelineKind.Hdrp);
		}

		private static bool DrawPipelineButton(string label, PcgRenderPipelineKind kind, PcgRenderPipelineKind detected)
		{
			var text = kind == detected ? label + " (detected)" : label;
			return GUILayout.Button(text, GUILayout.Height(26f));
		}

		private static void ApplyPipeline(PcgRenderPipelineKind kind)
		{
			SessionState.SetBool(PcgSetupConstants.SetupPendingExtrasKey, true);
			EditorApplication.delayCall += () =>
			{
				PcgRenderPipelineCleanup.Cleanup(kind);
				PcgSetupFlow.TryContinue();
			};
		}
	}
}
