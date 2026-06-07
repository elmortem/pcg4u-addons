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
			var uniTaskInstalled = PcgPackageUtility.IsInstalled(PcgSetupConstants.UniTaskPackageName);
			if (uniTaskInstalled && !PcgRenderPipelineCleanup.IsCleanupNeeded())
			{
				PcgSetupFlow.CompleteSetup();
				return;
			}
			if (SessionState.GetBool(PcgSetupConstants.SetupDismissKey, false))
				return;
			PcgSetupWindow.Open(uniTaskInstalled ? PcgSetupPage.RenderPipeline : PcgSetupPage.UniTask);
		}
	}
}
