using PCG.GraphModel;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Recursively splits regions into city blocks.",
		DisplayName = "Subdivide Region",
		Category = "Polygons/City",
		Tags = new[] { "region", "city", "subdivide", "blocks" })]
	public sealed class SubdivideRegionNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Region set to subdivide.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Input]
		[PcgMemberInfo("Minimum block size that stops splitting.", Tags = new[] { "size", "min" })]
		public float MinSize = 20f;

		[Input]
		[PcgMemberInfo("Maximum recursion depth of the split.", Tags = new[] { "depth", "max" })]
		public int MaxDepth = 6;

		[Input]
		[PcgMemberInfo("Random jitter of the split position.", Tags = new[] { "jitter", "random" })]
		public float SplitJitter = 0.1f;

		[Input]
		[PcgMemberInfo("World-space rotation of the local subdivision grid.", Tags = new[] { "rotation", "angle", "district" })]
		public float Rotation;

		[Input]
		[PcgMemberInfo("Random seed for the subdivision.", Tags = new[] { "seed", "random" })]
		public int Seed;

		[Output]
		[PcgMemberInfo("The subdivided city blocks.", Tags = new[] { "region", "blocks", "results" })]
		public RegionSet Blocks => default;
	}
}
