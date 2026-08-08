namespace PCG.Setup
{
	public static class PcgPackageIdentifier
	{
		public static string Build(string gitUrl, string packageName, string version)
		{
			if (string.IsNullOrEmpty(version))
				return gitUrl;
			return gitUrl + "#" + packageName + "/" + version;
		}
	}
}
