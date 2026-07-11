using PCG.GraphModel;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Slices city blocks into lots along their long edge.",
		DisplayName = "Lots From Block",
		Category = "Polygons/City",
		Tags = new[] { "region", "city", "lots", "subdivide" })]
	public sealed class LotsFromBlockNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Blocks to slice into lots.", Tags = new[] { "region", "blocks", "source" })]
		public RegionSet Blocks;

		[Input]
		[PcgMemberInfo("Target lot width along the long edge.", Tags = new[] { "width", "size" })]
		public float LotWidth = 12f;

		[Output]
		[PcgMemberInfo("The generated lots.", Tags = new[] { "region", "lots", "results" })]
		public RegionSet Lots => default;
	}
}
