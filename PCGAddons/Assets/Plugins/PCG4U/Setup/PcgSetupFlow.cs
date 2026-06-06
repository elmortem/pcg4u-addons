using UnityEditor;
using UnityEngine;

namespace PCG.Setup
{
	public static class PcgSetupFlow
	{
		public static void CompleteUniTaskInstall()
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
