using PCG.Cache;
using UnityEditor;

namespace PCG.Polygons
{
	public static class PcgPolygonsBootstrap
	{
		[InitializeOnLoadMethod]
		private static void Initialize()
		{
			PcgCacheSerializerRegistry.Register(new RegionSetSerializer());
		}
	}
}
