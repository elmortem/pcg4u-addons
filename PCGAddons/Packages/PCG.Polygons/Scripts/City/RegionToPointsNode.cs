using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.Polygons.City
{
	public sealed class RegionToPointsNode : PcgPreviewNode
	{
		[Input]
		public RegionSet Region;

		[Input]
		public RegionSet Roads;

		public RegionToPointsMode Mode = RegionToPointsMode.Centroid;

		[Input]
		public int Count = 1;

		[Input]
		public float Spacing = 5f;

		[Input]
		public float Margin = 0f;

		[Input]
		public int Seed;

		[Output]
		public List<PointData> Results => default;
	}
}
