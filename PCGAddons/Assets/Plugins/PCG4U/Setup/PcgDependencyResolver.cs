using System.Collections.Generic;
using UnityEngine;

namespace PCG.Setup
{
	public static class PcgDependencyResolver
	{
		public static bool TryBuildInstallList(PcgExtrasPackageEntry entry, PcgExtrasCatalog catalog, string rootIdentifier, out List<string> identifiers)
		{
			identifiers = new List<string>();
			var visited = new HashSet<string> { entry.PackageName };
			if (!Collect(entry, catalog, visited, identifiers))
				return false;
			identifiers.Add(rootIdentifier);
			return true;
		}

		public static PcgExtrasPackageEntry FindEntry(PcgExtrasCatalog catalog, string packageName)
		{
			foreach (var candidate in catalog.Packages)
			{
				if (candidate.PackageName == packageName)
					return candidate;
			}
			return null;
		}

		private static bool Collect(PcgExtrasPackageEntry entry, PcgExtrasCatalog catalog, HashSet<string> visited, List<string> identifiers)
		{
			foreach (var addonName in entry.AddonDependencies)
			{
				if (!visited.Add(addonName))
					continue;
				var addonEntry = FindEntry(catalog, addonName);
				if (addonEntry == null)
				{
					Debug.LogError("PCG4U Extras: dependency '" + addonName + "' is not found in PcgExtrasCatalog. Installation aborted.");
					return false;
				}
				if (!Collect(addonEntry, catalog, visited, identifiers))
					return false;
				if (NeedsInstall(addonEntry.PackageName, addonEntry.Version))
					identifiers.Add(PcgPackageIdentifier.Build(addonEntry.GitUrl, addonEntry.PackageName, addonEntry.Version));
			}
			foreach (var gitDependency in entry.GitDependencies)
			{
				if (!visited.Add(gitDependency.PackageName))
					continue;
				if (NeedsInstall(gitDependency.PackageName, gitDependency.Version))
					identifiers.Add(PcgPackageIdentifier.Build(gitDependency.GitUrl, gitDependency.PackageName, gitDependency.Version));
			}
			return true;
		}

		private static bool NeedsInstall(string packageName, string version)
		{
			var installedVersion = PcgPackageUtility.GetInstalledVersion(packageName);
			if (installedVersion == null)
				return true;
			if (string.IsNullOrEmpty(version))
				return false;
			return installedVersion != version;
		}
	}
}
