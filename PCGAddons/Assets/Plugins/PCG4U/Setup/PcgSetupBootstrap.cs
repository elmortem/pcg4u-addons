using UnityEditor;
using UnityEngine;

namespace PCG.Setup
{
	[InitializeOnLoad]
	public static class PcgSetupBootstrap
	{
		static PcgSetupBootstrap()
		{
			EditorApplication.delayCall += TryShow;
		}

		private static void TryShow()
		{
			if (Application.isBatchMode)
				return;
			var requiredInstalled = PcgPackageUtility.IsInstalled(PcgSetupConstants.UniTaskPackageName)
				&& PcgPackageUtility.IsInstalled(PcgSetupConstants.CollectionsPackageName);
			if (requiredInstalled && !PcgRenderPipelineCleanup.IsCleanupNeeded())
			{
				PcgSetupFlow.CompleteSetup();
				return;
			}
			if (SessionState.GetBool(PcgSetupConstants.SetupDismissKey, false))
				return;
			PcgSetupWindow.Open(requiredInstalled ? PcgSetupPage.RenderPipeline : PcgSetupPage.UniTask);
		}
	}
}
