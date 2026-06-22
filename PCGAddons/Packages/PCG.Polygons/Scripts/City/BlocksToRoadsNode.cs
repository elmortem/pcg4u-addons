using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class BlocksToRoadsNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Blocks;

		[Output]
		public RegionSet Roads => default;
	}
}
