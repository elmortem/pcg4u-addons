using PCG.GraphModel;
using PCG.Points;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Places one point per lot on its street-facing edge with a setback.",
		DisplayName = "Lot Frontage Points",
		Category = "Polygons/City",
		Tags = new[] { "region", "lot", "frontage", "points", "create" })]
	public sealed class LotFrontagePointsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Lot regions to place points on.", Tags = new[] { "region", "lots", "source" })]
		public RegionSet Lots;

		[Input]
		[PcgMemberInfo("Road footprint regions the lots face.", Tags = new[] { "region", "roads" })]
		public RegionSet Roads;

		[Input]
		[PcgMemberInfo("Distance from the frontage edge into the lot.", Tags = new[] { "setback", "offset" })]
		public float Setback = 4f;

		[Input]
		[PcgMemberInfo("Lots farther than this from a road are skipped.", Tags = new[] { "distance", "limit" })]
		public float MaxRoadDistance = 7f;

		[Input]
		[PcgMemberInfo("Frontage edges shorter than this are skipped.", Tags = new[] { "frontage", "limit" })]
		public float MinFrontage = 6f;

		[Input]
		[PcgMemberInfo("Random variation added to the setback.", Tags = new[] { "jitter", "random" })]
		public float SetbackJitter = 0.5f;

		[Input]
		[PcgMemberInfo("Random seed for the setback jitter.", Tags = new[] { "seed", "random" })]
		public int Seed;

		[Input]
		[PcgMemberInfo("Points closer than this to a road are skipped. Zero disables the check.", Tags = new[] { "clearance", "limit" })]
		public float MinPlacementClearance = 2f;

		[Input]
		[PcgMemberInfo("Points farther than this from a road are skipped. Zero disables the check.", Tags = new[] { "distance", "limit" })]
		public float MaxPlacementDistance = 9f;

		[Output]
		[PcgMemberInfo("One point per lot on its frontage edge, oriented toward the road.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Results => default;
	}
}
