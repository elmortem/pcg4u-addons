using PCG.GraphModel;
using PCG.Mazes.Graphs;

namespace PCG.Polygons.Convert
{
	[PcgNodeInfo("Converts region boundaries into a planar graph.",
		DisplayName = "Regions To Graph",
		Category = "Polygons/Convert",
		Tags = new[] { "region", "graph", "boundary", "convert" })]
	public sealed class RegionsToGraphNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Regions whose outer and hole boundaries become graph edges.", Tags = new[] { "region", "source" })]
		public RegionSet Regions;

		[Input]
		[PcgMemberInfo("Distance used to merge coincident graph vertices.", Tags = new[] { "merge", "distance", "vertex" })]
		public float MergeDistance = 0.05f;

		[Output]
		[PcgMemberInfo("Boundary graph.", Tags = new[] { "graph", "results" })]
		public Graph Graph => default;
	}
}
