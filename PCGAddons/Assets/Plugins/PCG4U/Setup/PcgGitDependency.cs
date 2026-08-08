using System;

namespace PCG.Setup
{
	[Serializable]
	public class PcgGitDependency
	{
		public string PackageName;
		public string GitUrl;
		public string Version;
	}
}
