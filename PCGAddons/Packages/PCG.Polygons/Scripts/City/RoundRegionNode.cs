using PCG.GraphModel;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Rounds convex and concave corners of polygon regions.",
		DisplayName = "Round Region",
		Category = "Polygons/City",
		Tags = new[] { "region", "round", "corner", "smooth" })]
	public sealed class RoundRegionNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Regions whose corners are rounded.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Input]
		[PcgMemberInfo("World-space rounding radius.", Tags = new[] { "radius", "round" })]
		public float Radius = 1f;

		[Output]
		[PcgMemberInfo("Rounded regions.", Tags = new[] { "region", "results" })]
		public RegionSet Result => default;
	}
}
