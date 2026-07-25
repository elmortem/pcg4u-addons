using PCG.GraphModel;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Unions multiple region streams into a single non-overlapping region set.",
		DisplayName = "Union Regions",
		Category = "Polygons/City",
		Tags = new[] { "region", "union", "merge" })]
	public sealed class UnionRegionsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Region streams to union.", Tags = new[] { "region", "source" })]
		public RegionSet Regions;

		[PcgMemberInfo("Fills enclosed holes smaller than this area. Zero preserves every hole.",
			Tags = new[] { "cleanup", "holes", "area" })]
		public float MinimumHoleArea;

		[Output]
		[PcgMemberInfo("Unified non-overlapping regions.", Tags = new[] { "region", "results" })]
		public RegionSet Result => default;
	}
}
