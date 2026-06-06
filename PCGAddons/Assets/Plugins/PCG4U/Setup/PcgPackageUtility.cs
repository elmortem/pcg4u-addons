using UnityEditor.PackageManager;

namespace PCG.Setup
{
	public static class PcgPackageUtility
	{
		public static bool IsInstalled(string packageName)
		{
			var packages = PackageInfo.GetAllRegisteredPackages();
			foreach (var package in packages)
			{
				if (package.name == packageName)
					return true;
			}
			return false;
		}
	}
}
