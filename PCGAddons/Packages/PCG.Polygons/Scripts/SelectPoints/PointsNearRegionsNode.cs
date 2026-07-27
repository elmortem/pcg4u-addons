using PCG.GraphModel;
using PCG.Points;
using PCG.Polygons;

namespace PCG.SelectPoints
{
	[PcgNodeInfo("Selects points close to regions within a radius.",
		DisplayName = "Points Near Regions",
		Category = "Select Points/Region",
		Tags = new[] { "points", "region", "select", "near" })]
	public class PointsNearRegionsNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Points far from the regions.", Tags = new[] { "points", "far", "results" })]
		public PcgPointCloud Results => default;

		[Output]
		[PcgMemberInfo("Points near the regions.", Tags = new[] { "points", "near" })]
		public PcgPointCloud NearPoints => default;

		[Input]
		[PcgMemberInfo("Points to test against the regions.", Tags = new[] { "points", "source" })]
		public PcgPointCloud Points = new();

		[Input]
		[PcgMemberInfo("Regions to measure the distance to.", Tags = new[] { "region", "source" })]
		public RegionSet Regions;

		[Input]
		[PcgMemberInfo("Maximum distance to count a point as near.", Tags = new[] { "radius", "distance" })]
		public float Radius = 1f;

		[PcgMemberInfo("Whether the point scale multiplies the radius.", Tags = new[] { "scale" })]
		public bool UseScale;
	}
}
