using System.IO;
using UnityEditor;
using UnityEngine.Rendering;

namespace PCG.Setup
{
	public static class PcgRenderPipelineCleanup
	{
		public static bool IsCleanupNeeded()
		{
			if (FindFolder(PcgSetupConstants.UrpGizmosAsmdefName) == null)
				return false;
			if (FindFolder(PcgSetupConstants.HdrpGizmosAsmdefName) == null)
				return false;
			if (PcgPackageUtility.IsInstalled(PcgSetupConstants.UrpPackageName)
				&& PcgPackageUtility.IsInstalled(PcgSetupConstants.HdrpPackageName))
				return false;
			return true;
		}

		public static PcgRenderPipelineKind DetectPipeline()
		{
			var asset = GraphicsSettings.currentRenderPipeline;
			if (asset == null)
				return PcgRenderPipelineKind.BuiltIn;
			var typeName = asset.GetType().Name;
			if (typeName == "UniversalRenderPipelineAsset")
				return PcgRenderPipelineKind.Urp;
			if (typeName == "HDRenderPipelineAsset")
				return PcgRenderPipelineKind.Hdrp;
			return PcgRenderPipelineKind.BuiltIn;
		}

		public static void Cleanup(PcgRenderPipelineKind kind)
		{
			if (kind != PcgRenderPipelineKind.Urp)
				DeleteFolder(PcgSetupConstants.UrpGizmosAsmdefName);
			if (kind != PcgRenderPipelineKind.Hdrp)
				DeleteFolder(PcgSetupConstants.HdrpGizmosAsmdefName);
		}

		private static string FindFolder(string asmdefName)
		{
			var guids = AssetDatabase.FindAssets(asmdefName);
			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.EndsWith("/" + asmdefName + ".asmdef"))
					continue;
				return Path.GetDirectoryName(path).Replace('\\', '/');
			}
			return null;
		}

		private static void DeleteFolder(string asmdefName)
		{
			var folder = FindFolder(asmdefName);
			if (folder == null)
				return;
			AssetDatabase.DeleteAsset(folder);
		}
	}
}
