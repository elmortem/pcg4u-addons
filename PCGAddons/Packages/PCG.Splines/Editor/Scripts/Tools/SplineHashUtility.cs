using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	public static class SplineHashUtility
	{
		public static int LinksHash(KnotLinkCollection links)
		{
			if (links == null || links.Count == 0)
				return 0;

			return StringHash(JsonUtility.ToJson(links));
		}

		public static int StringHash(string value)
		{
			if (string.IsNullOrEmpty(value))
				return 0;

			unchecked
			{
				int hash = 17;
				for (int i = 0; i < value.Length; i++)
					hash = hash * 31 + value[i];
				return hash;
			}
		}
	}
}
