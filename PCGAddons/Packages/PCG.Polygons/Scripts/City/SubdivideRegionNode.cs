using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class SubdivideRegionNode : PcgPreviewNode
	{
		[Input]
		public RegionSet Region;

		[Input]
		public float MinSize = 20f;

		[Input]
		public int MaxDepth = 6;

		[Input]
		public float SplitJitter = 0.1f;

		[Input]
		public int Seed;

		[Output]
		public RegionSet Blocks => default;
	}
}
