using PCG.Cache;
using UnityEditor;

namespace PCG.Splines
{
	public static class PcgSplinesBootstrap
	{
		[InitializeOnLoadMethod]
		private static void Initialize()
		{
			PcgCacheSerializerRegistry.Register(new PcgSplineSetSerializer());
		}
	}
}
