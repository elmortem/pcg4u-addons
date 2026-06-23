using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class BlocksToRoadsNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Blocks;

		public RoadJoinType Join = RoadJoinType.Round;

		public RoadCapType Cap = RoadCapType.Butt;

		public float MiterLimit = 2f;

		[Output]
		public RegionSet Roads => default;
	}
}
