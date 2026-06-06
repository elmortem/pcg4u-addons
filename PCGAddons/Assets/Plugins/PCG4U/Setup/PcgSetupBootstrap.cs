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
			if (PcgPackageUtility.IsInstalled(PcgSetupConstants.UniTaskPackageName))
			{
				PcgSetupFlow.CompleteUniTaskInstall();
				return;
			}
			if (SessionState.GetBool(PcgSetupConstants.SetupDismissKey, false))
				return;
			PcgSetupWindow.Open();
		}
	}
}
