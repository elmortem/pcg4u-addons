using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace PCG.Setup
{
	public static class PcgPackageInstaller
	{
		public static event Action Completed;

		private static AddRequest _request;

		public static bool IsBusy => _request != null && !_request.IsCompleted;

		public static void InstallFromGit(string gitUrl)
		{
			Start(gitUrl);
		}

		public static void InstallFromOpenUpm(string packageName, string version)
		{
			PcgManifestRegistryUtility.EnsureOpenUpmScope(packageName);
			Start(packageName + "@" + version);
		}

		private static void Start(string identifier)
		{
			if (IsBusy)
				return;
			_request = Client.Add(identifier);
			EditorApplication.update += OnUpdate;
		}

		private static void OnUpdate()
		{
			if (!_request.IsCompleted)
				return;
			EditorApplication.update -= OnUpdate;
			var request = _request;
			_request = null;
			if (request.Status == StatusCode.Failure)
				EditorUtility.DisplayDialog("PCG4U Setup",
					"Package installation failed:\n" + request.Error.message +
					"\n\nIf you installed via Git, make sure Git is installed and available in PATH.",
					"OK");
			Completed?.Invoke();
		}
	}
}
