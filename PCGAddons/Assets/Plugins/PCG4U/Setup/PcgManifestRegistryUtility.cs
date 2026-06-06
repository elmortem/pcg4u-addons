using System.IO;

namespace PCG.Setup
{
	public static class PcgManifestRegistryUtility
	{
		private const string ManifestPath = "Packages/manifest.json";
		private const string RegistryUrl = "https://package.openupm.com";

		public static void EnsureOpenUpmScope(string scope)
		{
			var text = File.ReadAllText(ManifestPath);
			if (Apply(text, scope, out var result))
				File.WriteAllText(ManifestPath, result);
		}

		private static bool Apply(string text, string scope, out string result)
		{
			result = text;
			var registriesIndex = text.IndexOf("\"scopedRegistries\"");
			if (registriesIndex < 0)
			{
				var dependenciesIndex = text.IndexOf("\"dependencies\"");
				var block =
					"\"scopedRegistries\": [\n" +
					"    {\n" +
					"      \"name\": \"OpenUPM\",\n" +
					"      \"url\": \"" + RegistryUrl + "\",\n" +
					"      \"scopes\": [\n" +
					"        \"" + scope + "\"\n" +
					"      ]\n" +
					"    }\n" +
					"  ],\n  ";
				result = text.Insert(dependenciesIndex, block);
				return true;
			}

			var urlIndex = text.IndexOf(RegistryUrl, registriesIndex);
			if (urlIndex < 0)
			{
				var arrayStart = text.IndexOf('[', registriesIndex);
				var registryObject =
					"\n    {\n" +
					"      \"name\": \"OpenUPM\",\n" +
					"      \"url\": \"" + RegistryUrl + "\",\n" +
					"      \"scopes\": [\n" +
					"        \"" + scope + "\"\n" +
					"      ]\n" +
					"    },";
				if (NextNonWhitespaceIs(text, arrayStart + 1, ']'))
					registryObject = registryObject.TrimEnd(',') + "\n  ";
				result = text.Insert(arrayStart + 1, registryObject);
				return true;
			}

			var objectStart = text.LastIndexOf('{', urlIndex);
			var objectEnd = FindObjectEnd(text, objectStart);
			var span = text.Substring(objectStart, objectEnd - objectStart);
			if (span.Contains("\"" + scope + "\""))
				return false;

			var scopesIndex = text.IndexOf("\"scopes\"", objectStart, objectEnd - objectStart);
			var scopesArrayStart = text.IndexOf('[', scopesIndex);
			var scopeInsert = "\n        \"" + scope + "\",";
			if (NextNonWhitespaceIs(text, scopesArrayStart + 1, ']'))
				scopeInsert = "\n        \"" + scope + "\"\n      ";
			result = text.Insert(scopesArrayStart + 1, scopeInsert);
			return true;
		}

		private static bool NextNonWhitespaceIs(string text, int index, char expected)
		{
			while (index < text.Length && char.IsWhiteSpace(text[index]))
				index++;
			return index < text.Length && text[index] == expected;
		}

		private static int FindObjectEnd(string text, int objectStart)
		{
			var depth = 0;
			for (var i = objectStart; i < text.Length; i++)
			{
				if (text[i] == '{')
				{
					depth++;
				}
				else if (text[i] == '}')
				{
					depth--;
					if (depth == 0)
						return i + 1;
				}
			}
			return text.Length;
		}
	}
}
