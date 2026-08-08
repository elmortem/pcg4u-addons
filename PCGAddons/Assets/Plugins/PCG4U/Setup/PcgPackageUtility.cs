using UnityEditor.PackageManager;

namespace PCG.Setup
{
	public static class PcgPackageUtility
	{
		public static bool IsInstalled(string packageName)
		{
			return GetInstalledVersion(packageName) != null;
		}

		public static string GetInstalledVersion(string packageName)
		{
			var packages = PackageInfo.GetAllRegisteredPackages();
			foreach (var package in packages)
			{
				if (package.name == packageName)
					return package.version;
			}
			return null;
		}
	}
}
