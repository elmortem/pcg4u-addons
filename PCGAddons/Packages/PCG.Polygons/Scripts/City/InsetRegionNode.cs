using PCG.GraphModel;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Insets or outsets each region by a delta.",
		DisplayName = "Inset Region",
		Category = "Polygons/City",
		Tags = new[] { "region", "inset", "outset", "offset" })]
	public sealed class InsetRegionNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Region set to inset or outset.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Input]
		[PcgMemberInfo("Offset delta; negative insets, positive outsets.", Tags = new[] { "delta", "offset" })]
		public float Delta = -1f;

		[Output]
		[PcgMemberInfo("The inset or outset region set.", Tags = new[] { "region", "results" })]
		public RegionSet Result => default;
	}
}
