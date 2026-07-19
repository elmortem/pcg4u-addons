using UnityEditor;
using UnityEngine;

namespace PCG.Setup
{
	public static class PcgSetupFlow
	{
		public static void TryContinue()
		{
			if (!PcgPackageUtility.IsInstalled(PcgSetupConstants.UniTaskPackageName))
				return;
			if (!PcgPackageUtility.IsInstalled(PcgSetupConstants.CollectionsPackageName))
				return;
			if (PcgRenderPipelineCleanup.IsCleanupNeeded())
			{
				PcgSetupWindow.Open(PcgSetupPage.RenderPipeline);
				return;
			}
			CompleteSetup();
		}

		public static void CompleteSetup()
		{
			if (!SessionState.GetBool(PcgSetupConstants.SetupPendingExtrasKey, false))
				return;
			SessionState.EraseBool(PcgSetupConstants.SetupPendingExtrasKey);
			CloseSetupWindows();
			PcgExtrasWindow.Open();
			PcgConsoleUtility.Clear();
		}

		private static void CloseSetupWindows()
		{
			var windows = Resources.FindObjectsOfTypeAll<PcgSetupWindow>();
			foreach (var window in windows)
				window.Close();
		}
	}
}
