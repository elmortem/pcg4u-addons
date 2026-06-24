using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class LotsFromBlockNode : PcgPreviewNode
	{
		[Input]
		public RegionSet Blocks;

		[Input]
		public float LotWidth = 12f;

		[Output]
		public RegionSet Lots => default;
	}
}
