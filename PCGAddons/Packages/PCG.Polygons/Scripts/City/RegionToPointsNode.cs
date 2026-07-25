using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Generates points inside regions with optional road orientation.",
		DisplayName = "Region To Points",
		Category = "Polygons/City",
		Tags = new[] { "region", "points", "create" })]
	public sealed class RegionToPointsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Regions to place points in.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Input]
		[PcgMemberInfo("Roads used to orient the points toward the nearest edge.", Tags = new[] { "region", "roads" })]
		public RegionSet Roads;

		[Input]
		[PcgMemberInfo("Optional regions where generated point footprints are forbidden.", Tags = new[] { "region", "exclude", "clearance", "roads" })]
		public RegionSet ExclusionRegions;

		[PcgMemberInfo("How points are placed inside each region.", Tags = new[] { "mode" })]
		public RegionToPointsMode Mode = RegionToPointsMode.Centroid;

		[Input]
		[PcgMemberInfo("Number of points per region in random mode.", Tags = new[] { "count", "amount" })]
		public int Count = 1;

		[Input]
		[PcgMemberInfo("Grid spacing between points in grid mode.", Tags = new[] { "spacing", "grid" })]
		public float Spacing = 5f;

		[Input]
		[PcgMemberInfo("Random displacement as a fraction of grid spacing; zero keeps a regular grid.", Tags = new[] { "grid", "jitter", "random" })]
		public float GridJitter;

		[Input]
		[PcgMemberInfo("Inset margin applied before placing points.", Tags = new[] { "margin", "inset" })]
		public float Margin = 0f;

		[Input]
		[PcgMemberInfo("Distance kept between a generated point footprint and exclusion regions.", Tags = new[] { "clearance", "footprint", "exclude" })]
		public float FootprintClearance;

		[Input]
		[PcgMemberInfo("Random seed for point placement.", Tags = new[] { "seed", "random" })]
		public int Seed;

		[Output]
		[PcgMemberInfo("Points generated inside the regions.", Tags = new[] { "points", "results" })]
		public List<PointData> Results => default;
	}
}
