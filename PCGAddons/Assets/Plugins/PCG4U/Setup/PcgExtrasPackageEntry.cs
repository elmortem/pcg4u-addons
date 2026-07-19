using System;
using System.Collections.Generic;

namespace PCG.Setup
{
	[Serializable]
	public class PcgExtrasPackageEntry
	{
		public string DisplayName;
		public string Description;
		public string PackageName;
		public string GitUrl;
		public string OpenUpmVersion;
		public List<PcgGitDependency> GitDependencies = new();
		public List<string> AddonDependencies = new();
	}
}
