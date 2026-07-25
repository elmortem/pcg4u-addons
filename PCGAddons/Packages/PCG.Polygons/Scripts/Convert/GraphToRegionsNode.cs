using PCG.GraphModel;
using PCG.Mazes.Graphs;

namespace PCG.Polygons.Convert
{
	[PcgNodeInfo("Extracts bounded faces of a planar graph as polygon regions.",
		DisplayName = "Graph To Regions",
		Category = "Polygons/Convert",
		Tags = new[] { "graph", "region", "faces", "planar", "convert" })]
	public sealed class GraphToRegionsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Planar graph whose bounded faces become regions.", Tags = new[] { "graph", "source" })]
		public Graph Graph = new();

		[Input]
		[PcgMemberInfo("World Y plane assigned to the generated regions.", Tags = new[] { "height", "plane" })]
		public float PlaneY;

		[Input]
		[PcgMemberInfo("Faces smaller than this world-space area are discarded.", Tags = new[] { "area", "minimum", "filter" })]
		public float MinArea = 1f;

		[Output]
		[PcgMemberInfo("Bounded graph faces.", Tags = new[] { "region", "faces", "results" })]
		public RegionSet Regions => default;
	}
}
