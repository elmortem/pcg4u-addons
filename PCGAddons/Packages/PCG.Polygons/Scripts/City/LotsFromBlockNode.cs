using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class LotsFromBlockNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Blocks;

		[Input]
		public float LotWidth = 12f;

		[Output]
		public RegionSet Lots => default;
	}
}
